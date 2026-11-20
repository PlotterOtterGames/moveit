using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>Lunapark!'s six booths (2026-11-09): quick score games of a minute, each on one move the camera reads
    /// well.</summary>
    public enum Machine { Whack, Slice, Wall, Catch, Flap, KeepUp }

    /// <summary>The body parts the booths read, by the player's own sides. The picture is a mirror, so the player's left
    /// hand is on the screen's left.</summary>
    public enum Part
    {
        Head, LeftShoulder, RightShoulder, LeftElbow, RightElbow, LeftHand, RightHand,
        LeftHip, RightHip, LeftKnee, RightKnee, LeftFoot, RightFoot,
    }

    /// <summary>
    /// One player's body in their stage: torso lengths from where their hips were when the booth started, x to the
    /// screen's right, y up. Measuring in the player's own torso lengths gives a six-year-old and a parent the same
    /// booth, wherever they stand: the moles sit within arm's reach, the candy falls within a step or two.
    /// A grown-up standing still: hips 0, shoulders ~1, head ~1.4, hands hanging ~0, knees ~-0.9, feet ~-1.85, an arm
    /// reaches ~1.3 from its shoulder. <see cref="Part.Head"/> is the middle of the head, <see cref="Part.LeftHand"/> the
    /// palm (the wrist carried on a little along the arm: <see cref="Body.HandPoint"/>).
    /// </summary>
    public sealed class StageBody
    {
        public const int PartCount = 13;
        public readonly Vector2[] At = new Vector2[PartCount];
        /// <summary>How sure the camera is of each part, 0..1.</summary>
        public readonly float[] Seen = new float[PartCount];
        /// <summary>The camera sees this player now.</summary>
        public bool Present;

        public Vector2 this[Part p]
        {
            get => At[(int)p];
            set => At[(int)p] = value;
        }

        public Vector2 Hips => (At[(int)Part.LeftHip] + At[(int)Part.RightHip]) * 0.5f;
        public Vector2 Shoulders => (At[(int)Part.LeftShoulder] + At[(int)Part.RightShoulder]) * 0.5f;
        public bool Sees(Part p, float min = 0.3f) => Present && Seen[(int)p] >= min;

        public void CopyFrom(StageBody other)
        {
            System.Array.Copy(other.At, At, PartCount);
            System.Array.Copy(other.Seen, Seen, PartCount);
            Present = other.Present;
        }

        /// <summary>A grown-up standing still, hands hanging, its hips at <paramref name="hips"/> (tests, the keys).</summary>
        public static StageBody Standing(Vector2 hips = default)
        {
            var b = new StageBody { Present = true };
            b.SetStanding(hips);
            return b;
        }

        public void SetStanding(Vector2 hips)
        {
            Set(Part.Head, hips + new Vector2(0f, 1.45f));
            Set(Part.LeftShoulder, hips + new Vector2(-0.42f, 1f));
            Set(Part.RightShoulder, hips + new Vector2(0.42f, 1f));
            Set(Part.LeftElbow, hips + new Vector2(-0.5f, 0.42f));
            Set(Part.RightElbow, hips + new Vector2(0.5f, 0.42f));
            Set(Part.LeftHand, hips + new Vector2(-0.52f, -0.24f));
            Set(Part.RightHand, hips + new Vector2(0.52f, -0.24f));
            Set(Part.LeftHip, hips + new Vector2(-0.2f, 0f));
            Set(Part.RightHip, hips + new Vector2(0.2f, 0f));
            Set(Part.LeftKnee, hips + new Vector2(-0.22f, -0.92f));
            Set(Part.RightKnee, hips + new Vector2(0.22f, -0.92f));
            Set(Part.LeftFoot, hips + new Vector2(-0.22f, -1.85f));
            Set(Part.RightFoot, hips + new Vector2(0.22f, -1.85f));
        }

        void Set(Part p, Vector2 at)
        {
            At[(int)p] = at;
            Seen[(int)p] = 1f;
        }

        public static bool IsLeft(Part p) => p is Part.LeftShoulder or Part.LeftElbow or Part.LeftHand or Part.LeftHip or Part.LeftKnee or Part.LeftFoot;
    }

    public enum EventKind
    {
        /// <summary>Something new came out (a mole, a fruit, a wall, a ball).</summary>
        Spawn,
        Hit, Gold, Bad, Escape, Slice, Combo, Pass, Perfect, Fail, Catch, Drop, Flap, Touch, Pop, Bump, Star, Ring, NewBall, Boom,
    }

    /// <summary>Something that happened in a booth this frame, for the sounds, the effects and the popups.</summary>
    public struct ArcadeEvent
    {
        public EventKind Kind;
        public int Player;
        /// <summary>Where it happened (the booth's own space: a player's stage, or Zogu!'s sky).</summary>
        public Vector2 At;
        public int Points;
        public int Combo;
        /// <summary>The thing it happened to (its id), or -1.</summary>
        public int Item;
        /// <summary>A booth's own detail (the fruit's kind, the part a ball touched...).</summary>
        public int Variant;
        public Vector2 Direction;
    }

    /// <summary>
    /// A booth's rules, without the engine: one or two players, a minute of play, points. The game feeds every frame each
    /// player's <see cref="StageBody"/>; the booth answers with its state (for drawing) and <see cref="Events"/>. Both
    /// players get the same sequence (each their own copy of it, from the same seed): a fair race.
    /// </summary>
    public abstract class MachineRules
    {
        public readonly int Players;
        public readonly int[] Score;
        public float Duration { get; }
        public float Time { get; private set; }
        public float Left => Mathf.Max(0f, Duration - Time);
        public bool Finished => Time >= Duration;
        /// <summary>0 at the start .. 1 at the end: what the booth ramps up with.</summary>
        public float Progress => Mathf.Clamp01(Time / Duration);
        /// <summary>This frame's events (the game clears them after reading).</summary>
        public readonly List<ArcadeEvent> Events = new List<ArcadeEvent>();
        public abstract Machine Booth { get; }

        protected MachineRules(int players, float duration)
        {
            Players = Mathf.Clamp(players, 1, 2);
            Score = new int[Players];
            Duration = Mathf.Max(5f, duration);
        }

        /// <summary>One frame: <paramref name="bodies"/>[p] is player p's body (null or not <see cref="StageBody.Present"/>
        /// = not seen; the booth goes on without them).</summary>
        public void Step(float dt, StageBody[] bodies)
        {
            if (Finished || dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);
            Time += dt;
            OnStep(dt, bodies);
        }

        protected abstract void OnStep(float dt, StageBody[] bodies);

        protected static StageBody BodyOf(StageBody[] bodies, int p) =>
            bodies != null && p < bodies.Length && bodies[p] != null && bodies[p].Present ? bodies[p] : null;

        protected void Add(int player, int points) => Score[player] = Mathf.Max(0, Score[player] + points);

        protected void Emit(EventKind kind, int player, Vector2 at, int points = 0, int combo = 0, int item = -1, int variant = 0,
                            Vector2 direction = default) =>
            Events.Add(new ArcadeEvent { Kind = kind, Player = player, At = at, Points = points, Combo = combo, Item = item, Variant = variant, Direction = direction });

        protected static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }

    public static class Geo
    {
        /// <summary>The point of segment a-b nearest to p.</summary>
        public static Vector2 Closest(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len = ab.sqrMagnitude;
            if (len < 1e-8f) return a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return a + ab * t;
        }

        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b) => Vector2.Distance(p, Closest(p, a, b));

        /// <summary>A direction <paramref name="degrees"/> from hanging straight down, turned outward (away from the
        /// body's middle) for positive angles: 90 = straight out to the side, 180 = straight up, negative = inward. Left =
        /// the player's left side (the screen's left).</summary>
        public static Vector2 Limb(bool left, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r) * (left ? -1f : 1f), -Mathf.Cos(r));
        }
    }
}
