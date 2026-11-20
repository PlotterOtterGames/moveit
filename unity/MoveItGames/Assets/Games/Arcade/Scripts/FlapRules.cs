using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// A flap of both arms from the camera (Zogu!): both hands moving down together, quickly, is a flap; one hand alone,
    /// arms held still, lowered slowly or raised, and the pose model's glitches are not. Each hand's height goes through a
    /// median of its last three samples (a one-frame glitch never gets through) and a flap needs both hands faster than
    /// <see cref="DownSpeed"/> two samples running (a two-frame glitch is one step down, then one back: never two in a
    /// row), at 30 or the box's 15 samples a second for two players. A flap is ready again once both hands have come back
    /// up a little (or after half a second). Its strength follows how fast the arms came down (big flaps fly higher).
    /// Torso lengths relative to the shoulders, so it doesn't matter where the player stands.
    /// </summary>
    public sealed class FlapDetector
    {
        /// <summary>Both hands at least this fast downward (torso lengths a second), two samples running, is a flap.
        /// Lowering the arms in a second or so peaks at ~1.9; a child's small quick flap is ~3-5, a big one 5-8.</summary>
        public const float DownSpeed = 2.5f;
        /// <summary>Hands this far back up from their lowest point arm the next flap.</summary>
        public const float Rearm = 0.25f;

        readonly float[,] _raw = new float[2, 3];
        readonly float[] _y = new float[2], _lowest = new float[2], _lastV = new float[2];
        readonly int[] _fast = new int[2];
        int _samples;
        bool _armed = true;
        float _sinceFlap = 10f;

        public int Flaps { get; private set; }
        /// <summary>The last flap's strength, 0.6 .. 1.4.</summary>
        public float Strength { get; private set; }

        public void Reset()
        {
            _samples = 0;
            _armed = true;
            _sinceFlap = 10f;
            _fast[0] = _fast[1] = 0;
        }

        /// <summary>One sample of the body; true when a flap happened.</summary>
        public bool Update(StageBody body, float dt)
        {
            _sinceFlap += dt;
            if (body == null || !body.Sees(Part.LeftHand) || !body.Sees(Part.RightHand) || dt <= 0f)
            {
                _samples = 0;
                _fast[0] = _fast[1] = 0;
                return false;
            }
            float shoulders = body.Shoulders.y;
            float yl = body[Part.LeftHand].y - shoulders, yr = body[Part.RightHand].y - shoulders;
            int slot = _samples % 3;
            _raw[0, slot] = yl;
            _raw[1, slot] = yr;
            _samples++;
            if (_samples < 3)
            {
                for (int h = 0; h < 2; h++) _y[h] = _lowest[h] = h == 0 ? yl : yr;
                return false;
            }
            for (int h = 0; h < 2; h++)
            {
                float m = Median(_raw[h, 0], _raw[h, 1], _raw[h, 2]);
                float v = (m - _y[h]) / dt;
                _y[h] = m;
                _lowest[h] = Mathf.Min(_lowest[h], m);
                _fast[h] = v < -DownSpeed ? _fast[h] + 1 : 0;
                _lastV[h] = v;
            }
            if (!_armed && (_sinceFlap > 0.5f || _y[0] - _lowest[0] > Rearm && _y[1] - _lowest[1] > Rearm)) _armed = true;
            if (_armed && _fast[0] >= 2 && _fast[1] >= 2)
            {
                _armed = false;
                _sinceFlap = 0f;
                _lowest[0] = _y[0];
                _lowest[1] = _y[1];
                Strength = Mathf.Clamp(-(_lastV[0] + _lastV[1]) * 0.5f / 5f, 0.6f, 1.4f);
                Flaps++;
                return true;
            }
            return false;
        }

        static float Median(float a, float b, float c) => Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
    }

    /// <summary>
    /// Zogu! (flap your arms to fly a little bird over the fun fair at night, after Kinect Adventures' Space Pop and Wii
    /// Fit Plus's bird flight): the bird flies on by itself; every flap of both arms lifts it, and it glides down slowly
    /// between flaps. Stars to collect in arcs, balloons to pop, rings to fly through; from the 8th second storm clouds
    /// drift by, and bumping one makes the bird tumble for a moment (no game over, only the time lost). The sky's
    /// height is 0 (the ground) to 1 (the top); along it, distance in the same units. Two players: two birds in the same
    /// sky, each star for whoever gets there first.
    /// </summary>
    public sealed class FlapRules : MachineRules
    {
        public override Machine Booth => Machine.Flap;

        public enum ThingKind { Star, Balloon, Ring, Cloud }

        public const float Gravity = 1.2f, Lift = 0.62f, MaxRise = 0.95f, MaxFall = 0.85f;
        public const float Ground = 0.07f, Ceiling = 0.94f, BirdRadius = 0.07f;
        /// <summary>Where the birds are on the screen (distance ahead of the left edge, in sky heights), and how much
        /// sky is ahead of them.</summary>
        public const float BirdX = 0.55f, Ahead = 1.4f;
        public const int StarPoints = 10, BalloonPoints = 25, RingPoints = 30;
        public const float Tumble = 0.7f;

        public sealed class Bird
        {
            public float Y = 0.5f, Vy, Stunned, FlapAge = 9f, Tilt;
            public int Stars, Balloons, Rings, Bumps, Flaps;
            /// <summary>Two players fly a little apart in distance, so both birds show.</summary>
            public float Offset;
        }

        public sealed class Thing
        {
            public int Id, Variant;
            public ThingKind Kind;
            public float X, Y, Phase;
            public bool Done;
            public int TakenBy = -1;
            public float DoneAge;
            public float Radius => Kind switch { ThingKind.Star => 0.065f, ThingKind.Balloon => 0.075f, ThingKind.Ring => 0.085f, _ => 0.1f };
        }

        public readonly Bird[] Birds;
        public readonly FlapDetector[] Detectors;
        public readonly List<Thing> Things = new List<Thing>();
        /// <summary>How far the sky has scrolled.</summary>
        public float Distance { get; private set; }
        public float Speed => Mathf.Lerp(0.42f, 0.7f, Progress);
        readonly System.Random _rng;
        float _nextPattern = 0.6f, _nextCloud = 8f;
        int _ids;

        public FlapRules(int players, float duration = 60f, int seed = 1) : base(players, duration)
        {
            _rng = new System.Random(seed);
            Birds = new Bird[Players];
            Detectors = new FlapDetector[Players];
            for (int p = 0; p < Players; p++)
            {
                Birds[p] = new Bird { Offset = Players == 2 ? (p == 0 ? 0.12f : -0.12f) : 0f, Y = Players == 2 ? (p == 0 ? 0.58f : 0.42f) : 0.5f };
                Detectors[p] = new FlapDetector();
            }
        }

        /// <summary>A flap from the keys or a test: player <paramref name="p"/> flaps with <paramref name="strength"/>.</summary>
        public void Flap(int p, float strength = 1f)
        {
            var bird = Birds[p];
            if (bird.Stunned > 0f) return;
            // a flap always lifts it by about the same (still rising, a little more): steady hops a child can steer with
            bird.Vy = Mathf.Min(MaxRise, Mathf.Max(bird.Vy, 0f) * 0.5f + Lift * Mathf.Clamp(strength, 0.6f, 1.4f));
            bird.FlapAge = 0f;
            bird.Flaps++;
            Emit(EventKind.Flap, p, new Vector2(BirdAt(p), bird.Y));
        }

        public float BirdAt(int p) => Distance + Birds[p].Offset;

        protected override void OnStep(float dt, StageBody[] bodies)
        {
            Distance += Speed * dt;
            Spawn();
            for (int p = 0; p < Players; p++)
            {
                if (Detectors[p].Update(BodyOf(bodies, p), dt)) Flap(p, Detectors[p].Strength);
                Fly(p, dt);
            }
            foreach (var t in Things)
            {
                t.Phase += dt;
                if (t.Done) t.DoneAge += dt;
                if (t.Kind == ThingKind.Cloud) t.Y += Mathf.Sin(t.Phase * 0.9f) * 0.03f * dt;
            }
            Things.RemoveAll(t => t.X < Distance - BirdX - 0.3f || t.Done && t.DoneAge > 0.7f);
        }

        void Fly(int p, float dt)
        {
            var bird = Birds[p];
            bird.FlapAge += dt;
            if (bird.Stunned > 0f)
            {
                bird.Stunned -= dt;
                bird.Vy = Mathf.Max(bird.Vy - Gravity * 1.3f * dt, -MaxFall);
            }
            else bird.Vy = Mathf.Max(bird.Vy - Gravity * dt, -MaxFall * (bird.FlapAge < 0.4f ? 1f : 0.55f));   // gliding between flaps
            bird.Y += bird.Vy * dt;
            if (bird.Y < Ground)
            {
                bird.Y = Ground;
                bird.Vy = 0f;
            }
            if (bird.Y > Ceiling)
            {
                bird.Y = Ceiling;
                bird.Vy = Mathf.Min(bird.Vy, 0f);
            }
            bird.Tilt = Mathf.Lerp(bird.Tilt, Mathf.Clamp(bird.Vy * 30f, -35f, 35f), Mathf.Clamp01(dt * 8f));
            float x = BirdAt(p);
            foreach (var t in Things)
            {
                if (t.Done) continue;
                float dx = t.X - x, dy = t.Y - bird.Y;
                switch (t.Kind)
                {
                    case ThingKind.Star when dx * dx + dy * dy <= Sq(t.Radius + BirdRadius):
                        Take(p, t, ++bird.Stars, StarPoints, EventKind.Star);
                        break;
                    case ThingKind.Balloon when dx * dx + dy * dy <= Sq(t.Radius + BirdRadius):
                        bird.Balloons++;
                        Take(p, t, 0, BalloonPoints, EventKind.Pop);
                        break;
                    case ThingKind.Ring when Mathf.Abs(dx) < 0.03f && Mathf.Abs(dy) <= t.Radius - 0.01f:
                        bird.Rings++;
                        Take(p, t, 0, RingPoints, EventKind.Ring);
                        break;
                    case ThingKind.Cloud when bird.Stunned <= 0f && dx * dx + dy * dy <= Sq(t.Radius + BirdRadius * 0.7f):
                        bird.Bumps++;
                        bird.Stunned = Tumble;
                        bird.Vy = -0.3f;
                        Emit(EventKind.Bump, p, new Vector2(x, bird.Y), item: t.Id);
                        break;
                }
            }
        }

        static float Sq(float v) => v * v;

        void Take(int p, Thing t, int combo, int points, EventKind kind)
        {
            t.Done = true;
            t.TakenBy = p;
            t.DoneAge = 0f;
            Add(p, points);
            Emit(kind, p, new Vector2(t.X, t.Y), points, combo, t.Id, t.Variant);
        }

        /// <summary>Patterns ahead of the birds: arcs and lines of stars, a ring, balloons; clouds from the 8th second.</summary>
        void Spawn()
        {
            float front = Distance + Ahead;
            float k = Progress;
            while (_nextPattern < front && Left > 1.5f)
            {
                float start = Mathf.Max(_nextPattern, Distance + 0.8f);
                int pattern = _rng.Next(Time < 4f ? 2 : 5);
                float mid = Range(_rng, 0.25f, 0.75f);
                switch (pattern)
                {
                    case 0:   // an arc of stars
                        for (int i = 0; i < 5; i++)
                            Place(ThingKind.Star, start + i * 0.13f, Mathf.Clamp(mid + 0.16f * Mathf.Sin(i / 4f * Mathf.PI), 0.2f, 0.88f));
                        _nextPattern = start + 0.9f;
                        break;
                    case 1:   // a line going up or down
                        float slope = _rng.NextDouble() < 0.5 ? 0.07f : -0.07f;
                        for (int i = 0; i < 5; i++) Place(ThingKind.Star, start + i * 0.13f, Mathf.Clamp(mid + slope * (i - 2), 0.2f, 0.88f));
                        _nextPattern = start + 0.85f;
                        break;
                    case 2:   // a ring with stars leading into it
                        Place(ThingKind.Star, start, mid);
                        Place(ThingKind.Star, start + 0.12f, mid);
                        Place(ThingKind.Ring, start + 0.32f, mid);
                        _nextPattern = start + 0.8f;
                        break;
                    case 3:   // balloons at two heights
                        Place(ThingKind.Balloon, start, Mathf.Clamp(mid + 0.18f, 0.28f, 0.85f), _rng.Next(4));
                        Place(ThingKind.Balloon, start + 0.3f, Mathf.Clamp(mid - 0.18f, 0.28f, 0.85f), _rng.Next(4));
                        _nextPattern = start + 0.75f;
                        break;
                    default:  // a dive: high, low, high
                        for (int i = 0; i < 6; i++)
                            Place(ThingKind.Star, start + i * 0.12f, Mathf.Clamp(0.5f + 0.3f * Mathf.Cos(i / 5f * Mathf.PI * 2f), 0.2f, 0.88f));
                        _nextPattern = start + 0.95f;
                        break;
                }
                _nextPattern += Mathf.Lerp(0.35f, 0.15f, k);
            }
            if (Time >= _nextCloud && Left > 2f)
            {
                // a cloud away from the stars ahead (never blocking the whole sky)
                float y = Range(_rng, 0.2f, 0.8f);
                Place(ThingKind.Cloud, front + 0.2f, y);
                _nextCloud = Time + Mathf.Lerp(4.5f, 2.2f, k) * Range(_rng, 0.8f, 1.2f);
            }
        }

        void Place(ThingKind kind, float x, float y, int variant = 0)
        {
            var t = new Thing { Id = ++_ids, Kind = kind, X = x, Y = y, Variant = variant, Phase = Range(_rng, 0f, 6.28f) };
            Things.Add(t);
        }
    }
}
