using System;
using UnityEngine;

namespace MoveIt.Games.Sports
{
    public enum FootballPhase { Ready, Pass, Aim, Flight, Outcome, KeeperReady, RunUp, Over }

    /// <summary>What an attack is: a teammate's pass then a first-time shot, a free kick over a wall, a penalty.</summary>
    public enum FootballSituation { Pass, FreeKick, Penalty }

    /// <summary>How a shot ends (known as soon as it is struck).</summary>
    public enum ShotEnd { Goal, Saved, Wide, Post, Wall }

    public enum FootballEvent { Attack, Pass, Kicked, Missed, Goal, Saved, Wide, Post, Blocked, Defend, Shot, PlayerSaved, Conceded, Over }

    /// <summary>
    /// Football (Kinect Sports' football: shots and goalkeeping), without a scene: a match of five rounds, each one
    /// attack and one defence.
    /// <list type="bullet">
    /// <item>Attack, a different situation each round (<see cref="SituationOf"/>): a teammate rolls the ball to the
    /// player and a kick as it arrives (<see cref="KickWindow"/>) shoots first time; or the ball lies still and any kick
    /// takes it (a free kick has a wall of four 9.15 m out: low shots near the middle hit it, lifted or wide ones pass;
    /// a penalty from 11 m). The foot's sideways swing and the body's lean aim it across, its upward swing and speed
    /// how high. The keeper guesses a side; out of its reach = a goal, outside the posts = wide, on them = the post.</item>
    /// <item>Defence: the player is the keeper; a striker shoots at a spot in the goal, harder each round (a penalty
    /// in round 2); any part of the body where the ball crosses the line saves it (forgiving).</item>
    /// </list>
    /// Points: 120 a goal, 100 a save, 200 for winning. Space: x across (the screen's right), y up, z toward the goal
    /// being attacked (the ball starts at z = 0, the goal line at z = <see cref="Distance"/>); in defence the ball comes
    /// from z = <see cref="Distance"/> to the player's goal line at z = 0.
    /// </summary>
    public sealed class FootballMatch
    {
        public const int Rounds = 5;
        public const float HalfWidth = 3.66f, Height = 2.44f, KickWindow = 0.42f, PostRadius = 0.06f;
        public const float KeeperReach = 1.35f, Assist = 0.15f, BallRadius = 0.11f;
        public const float WallDistance = 9.15f, WallHalfWidth = 1.05f, WallHeight = 1.85f, WallJump = 0.35f;
        public const float AimSeconds = 8f;

        public FootballPhase Phase { get; private set; } = FootballPhase.Ready;
        public float PhaseTime { get; private set; }
        public int Round { get; private set; } = 1;
        public bool Attacking { get; private set; } = true;
        public FootballSituation Situation { get; private set; }
        public int Goals { get; private set; }
        public int Conceded { get; private set; }
        public int Saves { get; private set; }
        public bool Over => Phase == FootballPhase.Over;
        /// <summary>From the ball's spot to the goal line this round (m).</summary>
        public float Distance { get; private set; }
        /// <summary>The ball (metres); during defence z is toward the striker.</summary>
        public Vector3 Ball { get; private set; }
        public Vector3 BallVelocity { get; private set; }
        /// <summary>Where the teammate passes from (x = which side) and when the pass arrives (s into the pass).</summary>
        public float PassFrom { get; private set; }
        public float PassArrives { get; private set; }
        /// <summary>The keeper's dive: where it goes across the goal (x), how far along (0..1).</summary>
        public float KeeperTarget { get; private set; }
        public float KeeperDive { get; private set; }
        /// <summary>Where the shot crosses the goal line (attack: the player's; defence: the striker's).</summary>
        public Vector2 Target { get; private set; }
        /// <summary>How the player's shot ends (set when it is struck).</summary>
        public ShotEnd Predicted { get; private set; }
        /// <summary>The wall's x (its middle) in a free kick.</summary>
        public float WallX { get; private set; }
        public float FlightTime { get; private set; }
        public event Action<FootballEvent> Events;

        readonly System.Random _rng;
        float _flight;
        bool _resolved;

        public FootballMatch(int seed)
        {
            _rng = new System.Random(seed);
            StartAttack();
        }

        public bool Won => Goals > Conceded;
        public int Points => Goals * 120 + Saves * 100 + (Over && Won ? 200 : 0);
        public static int[] Needs => new[] { 300, 550, 800, 1100 };

        /// <summary>Round 1 and 3 and 5: a pass; 2: a free kick; 4: a penalty.</summary>
        public static FootballSituation SituationOf(int round) => round == 2 ? FootballSituation.FreeKick
                                                               : round == 4 ? FootballSituation.Penalty : FootballSituation.Pass;

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        void Enter(FootballPhase p)
        {
            Phase = p;
            PhaseTime = 0f;
        }

        void StartAttack()
        {
            Attacking = true;
            Situation = SituationOf(Round);
            Distance = Situation == FootballSituation.Penalty ? 11f : Situation == FootballSituation.FreeKick ? 21f : 16f;
            PassFrom = _rng.NextDouble() < 0.5 ? -5f : 5f;
            WallX = Situation == FootballSituation.FreeKick ? R(-0.6f, 0.6f) : 0f;
            Ball = Situation == FootballSituation.Pass ? new Vector3(PassFrom, BallRadius, 3.5f) : new Vector3(0f, BallRadius, 0f);
            BallVelocity = Vector3.zero;
            KeeperTarget = KeeperDive = 0f;
            Enter(FootballPhase.Ready);
        }

        /// <summary>
        /// One step. <paramref name="touches"/> (the first <paramref name="count"/>: x across, y up, z = radius) are the
        /// player's body in the goal mouth while keeping.
        /// </summary>
        public void Step(float dt, Vector3[] touches, int count)
        {
            if (Over || dt <= 0f) return;
            PhaseTime += dt;
            switch (Phase)
            {
                case FootballPhase.Ready:
                    if (PhaseTime >= (Situation == FootballSituation.Pass ? 1.4f : 1.8f))
                    {
                        if (Situation == FootballSituation.Pass) StartPass();
                        else Enter(FootballPhase.Aim);
                    }
                    break;
                case FootballPhase.Pass:
                    // rolling in from the side to the player's feet
                    float k = Mathf.Clamp01(PhaseTime / PassArrives);
                    Ball = Vector3.Lerp(new Vector3(PassFrom, BallRadius, 3.5f), new Vector3(0f, BallRadius, 0.35f), k);
                    if (PhaseTime > PassArrives + KickWindow)
                    {
                        Events?.Invoke(FootballEvent.Missed);
                        Enter(FootballPhase.Outcome);
                    }
                    break;
                case FootballPhase.Aim:
                    if (PhaseTime >= AimSeconds) Strike(new Vector2(0f, 0.1f), 3.5f, 0f);   // too long: a soft shot
                    break;
                case FootballPhase.Flight:
                    Fly(dt);
                    if (Attacking)
                    {
                        KeeperDive = Mathf.Clamp01((PhaseTime - 0.16f) / 0.45f);
                        if (!_resolved && Predicted == ShotEnd.Wall && Ball.z >= WallDistance) ResolveAttack();
                        else if (!_resolved && Ball.z >= Distance) ResolveAttack();
                    }
                    else if (Ball.z <= 0f) ResolveDefence(touches, count);
                    break;
                case FootballPhase.Outcome:
                    Fly(dt);
                    if (Attacking) KeeperDive = Mathf.Clamp01(KeeperDive + dt * 2f);
                    if (PhaseTime >= 2.6f) Next();
                    break;
                case FootballPhase.KeeperReady:
                    if (PhaseTime >= 1.8f) Enter(FootballPhase.RunUp);
                    break;
                case FootballPhase.RunUp:
                    if (PhaseTime >= 1.25f) Shoot();
                    break;
            }
        }

        void StartPass()
        {
            PassArrives = 1.5f + (float)_rng.NextDouble() * 0.4f;
            Enter(FootballPhase.Pass);
            Events?.Invoke(FootballEvent.Pass);
        }

        /// <summary>How long until the ball can be kicked: the pass's arrival (negative: it's there / going), 0 while
        /// it lies ready; NaN when there is nothing to kick.</summary>
        public float UntilKick => Phase == FootballPhase.Pass ? PassArrives - PhaseTime : Phase == FootballPhase.Aim ? 0f : float.NaN;

        /// <summary>
        /// A kick: <paramref name="dir"/> = which way the foot went on the screen (x = the screen's right, y = up),
        /// <paramref name="speed"/> torso lengths a second, <paramref name="lean"/> -1..1 how far the body leans that way.
        /// Returns whether it met the ball (a pass near its arrival, a still ball any time).
        /// </summary>
        public bool Kick(Vector2 dir, float speed, float lean)
        {
            if (Phase == FootballPhase.Pass && Mathf.Abs(PhaseTime - PassArrives) <= KickWindow ||
                Phase == FootballPhase.Aim && PhaseTime > 0.4f)
            {
                Strike(dir, speed, lean);
                return true;
            }
            return false;
        }

        void Strike(Vector2 dir, float speed, float lean)
        {
            var from = new Vector3(Ball.x, BallRadius, Ball.z);
            float aim = Mathf.Clamp(dir.x * 1.3f + lean * 0.8f, -1.15f, 1.15f);
            float up = Mathf.Clamp(0.25f + Mathf.Max(0f, dir.y) * 1.6f + Mathf.Max(0f, speed - 3f) * 0.12f, 0.15f, 2.9f);
            // a free kick: kids kick low, so a kick that means it (fast or lifted) is chipped over the wall
            if (Situation == FootballSituation.FreeKick && (dir.y > 0.25f || speed > 5.5f)) up = Mathf.Max(up, 1.5f);
            Target = new Vector2(aim * (HalfWidth - 0.4f), up);
            float ballSpeed = Mathf.Clamp(16f + speed * 2.2f, 18f, 30f);
            var to = new Vector3(Target.x, Target.y, Distance);
            Launch(from, to, ballSpeed, Situation == FootballSituation.FreeKick ? 2.4f : 0f);
            Predicted = Predict();
            // the keeper guesses: the side the ball goes, often (more when it's near the middle), else the other way
            float guess = Mathf.Abs(Target.x) < 1.2f ? 0f : Mathf.Sign(Target.x) * 2.3f;
            KeeperTarget = Predicted == ShotEnd.Saved ? (Mathf.Abs(Target.x) < 0.9f ? Target.x : Mathf.Sign(Target.x) * Mathf.Min(2.3f, Mathf.Abs(Target.x)))
                         : Predicted == ShotEnd.Goal ? (Mathf.Abs(Target.x) < 1.2f ? -Mathf.Sign(Target.x + 0.01f) * 2.3f : -Mathf.Sign(Target.x) * R(0f, 2.3f))
                         : guess;
            KeeperDive = 0f;
            _resolved = false;
            Enter(FootballPhase.Flight);
            Events?.Invoke(FootballEvent.Kicked);
        }

        /// <summary>How the shot will end: off target, on the post, into the wall, saved (a share of the reachable
        /// ones, fewer when the shot is fast or high in a corner), else a goal.</summary>
        ShotEnd Predict()
        {
            float ax = Mathf.Abs(Target.x);
            if (ax > HalfWidth + PostRadius + BallRadius || Target.y > Height + PostRadius + BallRadius) return ShotEnd.Wide;
            if (Mathf.Abs(ax - HalfWidth) < PostRadius + BallRadius * 0.6f || Mathf.Abs(Target.y - Height) < PostRadius + BallRadius * 0.6f)
                return ShotEnd.Post;
            if (Situation == FootballSituation.FreeKick)
            {
                // where the ball passes the wall
                float k = WallDistance / Distance;
                var at = Vector3.Lerp(new Vector3(0f, BallRadius, 0f), new Vector3(Target.x, Target.y, Distance), k);
                float height = at.y + 4f * 2.4f * k * (1f - k);   // the chip's extra arc
                if (Mathf.Abs(at.x - WallX) < WallHalfWidth + BallRadius && height < WallHeight + WallJump + BallRadius) return ShotEnd.Wall;
            }
            float corner = Mathf.Clamp01((ax - 1.2f) / 2f) * 0.5f + Mathf.Clamp01((Target.y - 1.4f) / 0.9f) * 0.3f;
            float save = Situation == FootballSituation.Penalty ? 0.22f : 0.38f;
            save *= 1f - corner;
            if (FlightTimeGuess() < 0.6f) save *= 0.7f;
            return _rng.NextDouble() < save ? ShotEnd.Saved : ShotEnd.Goal;
        }

        float FlightTimeGuess() => _flight;

        void Launch(Vector3 from, Vector3 to, float speed, float extraArc)
        {
            Ball = from;
            _flight = Vector3.Distance(from, to) / speed;
            FlightTime = _flight;
            // a ballistic arc that crosses the goal line at the target after _flight seconds (a chip: higher in between)
            var d = to - from;
            float g = 9.81f + extraArc * 8f / (_flight * _flight) * 0.5f;
            _gravity = g;
            BallVelocity = new Vector3(d.x / _flight, d.y / _flight + 0.5f * g * _flight, d.z / _flight);
        }

        float _gravity = 9.81f;

        void Fly(float dt)
        {
            var v = BallVelocity;
            v.y -= _gravity * dt;
            var p = Ball + v * dt;
            if (p.y < BallRadius && v.y < 0f)
            {
                p.y = BallRadius;
                v.y = -v.y * 0.5f;
                v.x *= 0.8f;
                v.z *= 0.8f;
                _gravity = 9.81f;
            }
            Ball = p;
            BallVelocity = v;
        }

        void ResolveAttack()
        {
            _resolved = true;
            _gravity = 9.81f;
            switch (Predicted)
            {
                case ShotEnd.Wide:
                    Events?.Invoke(FootballEvent.Wide);
                    break;
                case ShotEnd.Post:
                    BallVelocity = new Vector3(-BallVelocity.x * 0.4f + R(-2f, 2f), 3.5f, -BallVelocity.z * 0.35f);
                    Events?.Invoke(FootballEvent.Post);
                    break;
                case ShotEnd.Wall:
                    BallVelocity = new Vector3(R(-3f, 3f), 4f, -6f);
                    Events?.Invoke(FootballEvent.Blocked);
                    break;
                case ShotEnd.Saved:
                    BallVelocity = new Vector3(BallVelocity.x * -0.2f, 3f, -6f);
                    Events?.Invoke(FootballEvent.Saved);
                    break;
                default:
                    Goals++;
                    BallVelocity *= 0.15f;
                    Events?.Invoke(FootballEvent.Goal);
                    break;
            }
            Enter(FootballPhase.Outcome);
        }

        void Shoot()
        {
            // a spot in the goal: corners and the middle, low and high; harder each round, the second a penalty
            float x = R(-1f, 1f) * (HalfWidth - 0.6f);
            float y = 0.25f + R(0f, 1f) * (Round >= 3 ? 1.9f : 1.4f);
            Target = new Vector2(x, y);
            Distance = Round == 2 ? 11f : 16f;
            Launch(new Vector3(0f, BallRadius, Distance), new Vector3(x, y, 0f), 13f + Round * 1.4f, 0f);
            Enter(FootballPhase.Flight);
            Events?.Invoke(FootballEvent.Shot);
        }

        /// <summary>Tests: the next shot goes there (else a random spot).</summary>
        public void ShootAt(Vector2 spot, float speed = 14f)
        {
            if (Phase != FootballPhase.RunUp && Phase != FootballPhase.KeeperReady) return;
            Target = spot;
            Launch(new Vector3(0f, BallRadius, Distance), new Vector3(spot.x, spot.y, 0f), speed, 0f);
            Enter(FootballPhase.Flight);
            Events?.Invoke(FootballEvent.Shot);
        }

        void ResolveDefence(Vector3[] touches, int count)
        {
            var at = new Vector2(Ball.x, Ball.y);
            for (int i = 0; i < count; i++)
            {
                var t = touches[i];
                if (Vector2.Distance(at, new Vector2(t.x, t.y)) < BallRadius + t.z + Assist)
                {
                    Saves++;
                    BallVelocity = new Vector3((at.x - t.x) * 6f, 3.5f, 7f);
                    Events?.Invoke(FootballEvent.PlayerSaved);
                    Enter(FootballPhase.Outcome);
                    return;
                }
            }
            if (Mathf.Abs(at.x) < HalfWidth && at.y < Height)
            {
                Conceded++;
                BallVelocity *= 0.2f;
                Events?.Invoke(FootballEvent.Conceded);
            }
            else Events?.Invoke(FootballEvent.Wide);
            Enter(FootballPhase.Outcome);
        }

        void Next()
        {
            if (Attacking)
            {
                Attacking = false;
                Distance = Round == 2 ? 11f : 16f;
                Ball = new Vector3(0f, BallRadius, Distance - 0.6f);
                BallVelocity = Vector3.zero;
                Enter(FootballPhase.KeeperReady);
                Events?.Invoke(FootballEvent.Defend);
                return;
            }
            if (Round >= Rounds)
            {
                Enter(FootballPhase.Over);
                Events?.Invoke(FootballEvent.Over);
                return;
            }
            Round++;
            StartAttack();
            Events?.Invoke(FootballEvent.Attack);
        }
    }
}
