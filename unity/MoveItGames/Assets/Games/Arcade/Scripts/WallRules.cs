using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// A shape to make for <see cref="WallRules"/>: each arm's two bones as angles from hanging straight down (+ =
    /// outward, 90 = out to the side, 180 = straight up, - = inward), optionally the thighs (legs apart), a squat, and
    /// where to stand (a hole off to one side: step there).
    /// </summary>
    public sealed class WallPose
    {
        public readonly string Id;
        public readonly float LeftUpper, LeftFore, RightUpper, RightFore;
        public readonly bool Legs;
        public readonly float LeftThigh, RightThigh;
        public readonly bool Squat;
        /// <summary>Where the hips must be, torso lengths from where they stood (0 = the middle).</summary>
        public readonly float Step;
        /// <summary>0 = easy (from the start), 1 = from a third of the way in, 2 = the last third.</summary>
        public readonly int Level;

        public WallPose(string id, float lu, float lf, float ru, float rf, int level, bool squat = false, float step = 0f,
                        float leftThigh = float.NaN, float rightThigh = float.NaN)
        {
            Id = id;
            LeftUpper = lu;
            LeftFore = lf;
            RightUpper = ru;
            RightFore = rf;
            Level = level;
            Squat = squat;
            Step = step;
            Legs = !float.IsNaN(leftThigh);
            LeftThigh = Legs ? leftThigh : 15f;
            RightThigh = Legs ? rightThigh : 15f;
        }

        /// <summary>The bone directions this pose wants: left upper arm, left forearm, right upper arm, right forearm,
        /// left thigh, right thigh (screen space, y up).</summary>
        public Vector2 Dir(int bone) => bone switch
        {
            0 => Geo.Limb(true, LeftUpper),
            1 => Geo.Limb(true, LeftFore),
            2 => Geo.Limb(false, RightUpper),
            3 => Geo.Limb(false, RightFore),
            4 => Geo.Limb(true, LeftThigh),
            _ => Geo.Limb(false, RightThigh),
        };
    }

    /// <summary>
    /// Muri! (a wall with a hole in a person's shape comes at you: make the shape to pass through; after the TV show and
    /// Kinect's Hole in the Wall, Kinect Adventures' Reflex Ridge and WarioWare's poses). Holding a pose is what the
    /// camera reads best (2026-11-01: a held arm shakes ~0.4 % of a torso), so the check is forgiving in time: the best
    /// match from half a second before the wall arrives to a moment after counts, which also covers the camera's lag.
    /// A limb counts within <see cref="Tolerance"/> degrees of its bone; three quarters of the checks pass, all of them
    /// are perfect. One wall at a time, quicker and with harder shapes as the minute goes on. Two players: one wall with a
    /// hole for each, the same shape.
    /// </summary>
    public sealed class WallRules : MachineRules
    {
        public override Machine Booth => Machine.Wall;

        public static readonly WallPose[] Poses =
        {
            new WallPose("y", 150f, 155f, 150f, 155f, 0),
            new WallPose("t", 90f, 90f, 90f, 90f, 0),
            new WallPose("left_up", 180f, 180f, 12f, 8f, 0),
            new WallPose("right_up", 12f, 8f, 180f, 180f, 0),
            new WallPose("star", 135f, 135f, 135f, 135f, 1, leftThigh: 24f, rightThigh: 24f),
            new WallPose("hips", 45f, -55f, 45f, -55f, 1),
            new WallPose("muscle", 90f, 180f, 90f, 180f, 1),
            new WallPose("plane_a", 112f, 112f, 68f, 68f, 1),
            new WallPose("plane_b", 68f, 68f, 112f, 112f, 1),
            new WallPose("squat_t", 90f, 90f, 90f, 90f, 1, squat: true),
            new WallPose("step_left", 150f, 155f, 150f, 155f, 1, step: -1.2f),
            new WallPose("step_right", 90f, 90f, 90f, 90f, 1, step: 1.2f),
            new WallPose("heart", 150f, 222f, 150f, 222f, 2),
            new WallPose("up_out", 180f, 180f, 90f, 90f, 2),
            new WallPose("out_up", 90f, 90f, 180f, 180f, 2),
            new WallPose("zigzag", 90f, 180f, 90f, 0f, 2),
            new WallPose("squat_y", 150f, 155f, 150f, 155f, 2, squat: true),
        };

        /// <summary>Seconds before the arrival from which a match counts, and after it.</summary>
        public const float Early = 0.5f, Late = 0.15f;
        /// <summary>A fraction of the checks this big passes the wall.</summary>
        public const float PassAt = 0.75f;
        /// <summary>Hips this far below where they stood are a squat; this far from the hole's middle is off it.</summary>
        public const float SquatDepth = 0.28f, StepReach = 0.55f;
        public const int PassPoints = 60, PerfectPoints = 100, StreakPoints = 10;

        public sealed class Wall
        {
            public int Id;
            public WallPose Pose;
            public float Spawn, Arrive;
            /// <summary>Per player: the best match so far (0..1), and the outcome once checked.</summary>
            public float[] Best;
            public bool[] Passed, Perfect;
            public bool Checked;
            /// <summary>0 when it appears far away, 1 at the player (beyond 1 it has passed).</summary>
            public float Approach(float now) => (now - Spawn) / Mathf.Max(0.01f, Arrive - Spawn);
        }

        public sealed class Board
        {
            public int Streak, BestStreak, Passed, Perfects, Failed;
            /// <summary>The coming wall's bones as the player holds them now (drawing: the hole's edge lights up).</summary>
            public readonly bool[] Matching = new bool[8];
            public float MatchNow;
        }

        public readonly List<Wall> Walls = new List<Wall>();
        public readonly Board[] Boards;
        readonly System.Random _rng;
        float _nextWall = 1.2f;
        int _ids;
        WallPose _last;

        /// <summary>Holes off to a side (a step to make): not for two players side by side.</summary>
        public readonly bool Steps;

        public WallRules(int players, float duration = 60f, int seed = 1, bool steps = true) : base(players, duration)
        {
            Steps = steps;
            _rng = new System.Random(seed);
            Boards = new Board[Players];
            for (int p = 0; p < Players; p++) Boards[p] = new Board();
        }

        public float Tolerance => Mathf.Lerp(40f, 30f, Progress);
        static float Interval(float k) => Mathf.Lerp(5.2f, 3.4f, k);
        static float ApproachTime(float k) => Mathf.Lerp(4.4f, 2.8f, k);

        /// <summary>The wall coming at the players (not yet checked), or null.</summary>
        public Wall Coming
        {
            get
            {
                foreach (var w in Walls)
                    if (!w.Checked) return w;
                return null;
            }
        }

        protected override void OnStep(float dt, StageBody[] bodies)
        {
            float k = Progress;
            if (Time >= _nextWall && Coming == null && Duration - Time > ApproachTime(k) + Late)
            {
                SpawnWall(k);
                _nextWall = Time + Interval(k);
            }
            var coming = Coming;
            for (int p = 0; p < Players; p++)
            {
                var board = Boards[p];
                var body = BodyOf(bodies, p);
                board.MatchNow = coming != null && body != null ? Match(body, coming.Pose, Tolerance, board.Matching) : 0f;
                if (coming != null && Time >= coming.Arrive - Early) coming.Best[p] = Mathf.Max(coming.Best[p], board.MatchNow);
            }
            if (coming != null && (Time >= coming.Arrive + Late || Time >= coming.Arrive && AllPerfect(coming))) Check(coming);
            Walls.RemoveAll(w => w.Checked && Time > w.Arrive + 1.1f);
        }

        /// <summary>Tests: picks every wall's shape (null = at random, harder as the minute goes on).</summary>
        public System.Func<WallPose> Choose;

        void SpawnWall(float k)
        {
            int level = k < 0.3f ? 0 : k < 0.65f ? 1 : 2;
            var pool = new List<WallPose>();
            foreach (var pose in Poses)
                if (pose.Level <= level && pose != _last && (Steps || Mathf.Abs(pose.Step) < 0.01f) && (level == 0 || pose.Level > 0 || _rng.NextDouble() < 0.35)) pool.Add(pose);
            var chosen = Choose?.Invoke() ?? pool[_rng.Next(pool.Count)];
            _last = chosen;
            var w = new Wall
            {
                Id = ++_ids, Pose = chosen, Spawn = Time, Arrive = Time + ApproachTime(k),
                Best = new float[Players], Passed = new bool[Players], Perfect = new bool[Players],
            };
            Walls.Add(w);
            Emit(EventKind.Spawn, 0, new Vector2(chosen.Step, 1f), item: w.Id);
        }

        bool AllPerfect(Wall w)
        {
            for (int p = 0; p < Players; p++)
                if (w.Best[p] < 1f) return false;
            return true;
        }

        void Check(Wall w)
        {
            w.Checked = true;
            for (int p = 0; p < Players; p++)
            {
                var b = Boards[p];
                var at = new Vector2(w.Pose.Step, 1f);
                if (w.Best[p] >= PassAt)
                {
                    w.Passed[p] = true;
                    w.Perfect[p] = w.Best[p] >= 0.999f;
                    b.Streak++;
                    b.BestStreak = Mathf.Max(b.BestStreak, b.Streak);
                    b.Passed++;
                    if (w.Perfect[p]) b.Perfects++;
                    int points = (w.Perfect[p] ? PerfectPoints : PassPoints) + StreakPoints * Mathf.Min(5, b.Streak - 1);
                    Add(p, points);
                    Emit(w.Perfect[p] ? EventKind.Perfect : EventKind.Pass, p, at, points, b.Streak, w.Id);
                }
                else
                {
                    b.Streak = 0;
                    b.Failed++;
                    Emit(EventKind.Fail, p, at, 0, 0, w.Id, Mathf.RoundToInt(w.Best[p] * 100f));
                }
            }
        }

        /// <summary>
        /// How well <paramref name="body"/> makes <paramref name="pose"/>: the share of its checks met (0..1). The checks:
        /// each arm bone within <paramref name="tolerance"/> degrees of the pose's (four), the thighs when the pose has
        /// legs apart (two), a squat, the hips at the hole's spot when it is off to a side. <paramref name="matching"/>
        /// (optional, 8 long) gets each check's result: bones 0..5 (left upper arm, left forearm, right upper arm, right
        /// forearm, left thigh, right thigh), 6 the squat, 7 the spot. A bone the camera can't see doesn't count.
        /// </summary>
        public static float Match(StageBody body, WallPose pose, float tolerance, bool[] matching = null)
        {
            if (matching != null)
                for (int i = 0; i < matching.Length; i++) matching[i] = false;
            int checks = 4, met = 0;
            for (int bone = 0; bone < 4; bone++)
                if (BoneMatches(body, bone, pose.Dir(bone), tolerance, matching)) met++;
            if (pose.Legs)
            {
                checks += 2;
                for (int bone = 4; bone < 6; bone++)
                    if (BoneMatches(body, bone, pose.Dir(bone), tolerance + 8f, matching)) met++;
            }
            var hips = body.Hips;
            if (pose.Squat)
            {
                checks++;
                if (body.Sees(Part.LeftHip) && body.Sees(Part.RightHip) && hips.y <= -SquatDepth)
                {
                    met++;
                    if (matching != null) matching[6] = true;
                }
            }
            if (Mathf.Abs(pose.Step) > 0.01f)
            {
                checks++;
                if (Mathf.Abs(hips.x - pose.Step) <= StepReach)
                {
                    met++;
                    if (matching != null) matching[7] = true;
                }
            }
            return (float)met / checks;
        }

        static readonly (Part from, Part to)[] Bones =
        {
            (Part.LeftShoulder, Part.LeftElbow), (Part.LeftElbow, Part.LeftHand),
            (Part.RightShoulder, Part.RightElbow), (Part.RightElbow, Part.RightHand),
            (Part.LeftHip, Part.LeftKnee), (Part.RightHip, Part.RightKnee),
        };

        static bool BoneMatches(StageBody body, int bone, Vector2 want, float tolerance, bool[] matching)
        {
            var (from, to) = Bones[bone];
            if (!body.Sees(from) || !body.Sees(to)) return false;
            var have = body[to] - body[from];
            if (have.sqrMagnitude < 1e-4f) return false;
            bool ok = Vector2.Angle(have, want) <= tolerance;
            if (ok && matching != null) matching[bone] = true;
            return ok;
        }
    }
}
