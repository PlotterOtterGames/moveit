using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// Godit! (whack-a-mole in the mirror, after EyeToy's Kung Foo and the fairground's mole table): seven little grass
    /// islands float around the player within arm's reach, moles pop out of them and a hand on one bonks it. Golden moles
    /// are worth three; from the 10th second hedgehogs pop out too, and a hand on one costs points and a moment's daze.
    /// The holes avoid the front of the chest, where the pose model's wrists jump (2026-11-01: ~85 % of the jumps while a
    /// hand passed in front of the chest). Any touch counts (no speed needed), but a hand left in a hole doesn't bonk
    /// every mole that comes up there: it has to come in from outside, or move.
    /// </summary>
    public sealed class WhackRules : MachineRules
    {
        public override Machine Booth => Machine.Whack;

        /// <summary>The holes in the stage (torso lengths from the hips): high left, above the head, high right, out to
        /// the sides at the shoulders, low left and right by the hips.</summary>
        public static readonly Vector2[] Holes =
        {
            new Vector2(-1.25f, 2.05f), new Vector2(0f, 2.35f), new Vector2(1.25f, 2.05f),
            new Vector2(-1.45f, 1.15f), new Vector2(1.45f, 1.15f),
            new Vector2(-1.05f, 0.25f), new Vector2(1.05f, 0.25f),
        };

        public const float HitRadius = 0.42f, RiseTime = 0.16f, SinkTime = 0.2f, HitShow = 0.5f;
        /// <summary>A hand this fast (torso lengths a second) bonks even if it was already in the hole.</summary>
        public const float MovingSpeed = 1.2f;
        public const int MolePoints = 10, GoldPoints = 30, HedgehogPoints = -20;
        public const float GoldChance = 0.08f, Daze = 0.6f;

        public enum Kind { Mole, Gold, Hedgehog }
        public enum State { Rising, Up, Hit, Sinking }

        public sealed class Critter
        {
            public int Id, Hole;
            public Kind Kind;
            public State State;
            public float StateTime, UpFor;
            /// <summary>Per hand: has it been outside the hole since this critter came up?</summary>
            public readonly bool[] Clear = new bool[2];
            /// <summary>How far out of its hole, 0..1 (drawing).</summary>
            public float Out => State switch
            {
                State.Rising => Mathf.Clamp01(StateTime / RiseTime),
                State.Sinking => 1f - Mathf.Clamp01(StateTime / SinkTime),
                State.Hit => 1f - Mathf.Clamp01((StateTime - 0.25f) / (HitShow - 0.25f)),
                _ => 1f,
            };
        }

        public sealed class Board
        {
            public readonly List<Critter> Critters = new List<Critter>();
            public readonly float[] HoleFreeAt = new float[Holes.Length];
            public readonly Vector2[] PreviousHand = new Vector2[2];
            public readonly bool[] HadHand = new bool[2];
            public readonly System.Random Rng;
            public float NextSpawn = 0.8f, Dazed;
            public int Combo, BestCombo, Hits, Golds, Escapes, Hedgehogs, LastHole = -1;
            public Board(int seed) => Rng = new System.Random(seed);
            /// <summary>The points a mole is worth now: x1, x2 after 4 in a row, x3 after 8, x4 after 12.</summary>
            public int Multiplier => 1 + Mathf.Min(3, Combo / 4);
        }

        public readonly Board[] Boards;
        /// <summary>The holes' sideways spread (1; two players standing side by side get theirs a little closer in).</summary>
        public readonly float Spread;
        int _ids;

        public WhackRules(int players, float duration = 60f, int seed = 1, float spread = 1f) : base(players, duration)
        {
            Spread = spread;
            Boards = new Board[Players];
            for (int p = 0; p < Players; p++) Boards[p] = new Board(seed);
        }

        // the ramp (k = 0 .. 1 over the booth's minute)
        static float SpawnEvery(float k) => Mathf.Lerp(1.15f, 0.5f, k);
        static float UpTime(float k) => Mathf.Lerp(1.6f, 0.8f, k);
        static int MaxUp(float k) => k < 0.25f ? 1 : k < 0.6f ? 2 : 3;
        float HedgehogChance(float k) => Time < 10f ? 0f : Mathf.Lerp(0.12f, 0.2f, k);

        protected override void OnStep(float dt, StageBody[] bodies)
        {
            float k = Progress;
            for (int p = 0; p < Players; p++)
            {
                var b = Boards[p];
                var body = BodyOf(bodies, p);
                b.Dazed = Mathf.Max(0f, b.Dazed - dt);
                b.NextSpawn -= dt;
                int up = 0;
                foreach (var c in b.Critters)
                    if (c.State is State.Rising or State.Up) up++;
                if (b.NextSpawn <= 0f && up < MaxUp(k) && Spawn(p, b, k, body)) b.NextSpawn = SpawnEvery(k) * Range(b.Rng, 0.75f, 1.25f);
                else if (b.NextSpawn <= 0f) b.NextSpawn = 0.1f;
                Advance(p, b, dt);
                if (body != null) Hands(p, b, body, dt);
            }
        }

        bool Spawn(int p, Board b, float k, StageBody body)
        {
            var free = new List<int>();
            for (int h = 0; h < Holes.Length; h++)
            {
                if (Time < b.HoleFreeAt[h] || h == b.LastHole) continue;
                bool taken = false;
                foreach (var c in b.Critters)
                    if (c.Hole == h) taken = true;
                if (!taken) free.Add(h);
            }
            if (free.Count == 0) return false;
            int hole = free[b.Rng.Next(free.Count)];
            double roll = b.Rng.NextDouble();
            var kind = roll < HedgehogChance(k) ? Kind.Hedgehog : roll < HedgehogChance(k) + GoldChance ? Kind.Gold : Kind.Mole;
            var critter = new Critter { Id = ++_ids, Hole = hole, Kind = kind, State = State.Rising, UpFor = UpTime(k) * Range(b.Rng, 0.85f, 1.15f) };
            // a hand already outside the hole is clear; one resting in it has to come out first
            for (int h = 0; h < 2; h++)
                critter.Clear[h] = body == null || !body.Sees(Hand(h)) || Vector2.Distance(body[Hand(h)], HoleAt(hole)) > HitRadius;
            b.Critters.Add(critter);
            b.LastHole = hole;
            Emit(EventKind.Spawn, p, HoleAt(hole), item: critter.Id, variant: (int)kind);
            return true;
        }

        void Advance(int p, Board b, float dt)
        {
            for (int i = b.Critters.Count - 1; i >= 0; i--)
            {
                var c = b.Critters[i];
                c.StateTime += dt;
                switch (c.State)
                {
                    case State.Rising when c.StateTime >= RiseTime:
                        c.State = State.Up;
                        c.StateTime = 0f;
                        break;
                    case State.Up when c.StateTime >= c.UpFor:
                        c.State = State.Sinking;
                        c.StateTime = 0f;
                        if (c.Kind != Kind.Hedgehog)
                        {
                            b.Escapes++;
                            b.Combo = 0;
                            Emit(EventKind.Escape, p, HoleAt(c.Hole), item: c.Id, variant: (int)c.Kind);
                        }
                        break;
                    case State.Hit when c.StateTime >= HitShow:
                    case State.Sinking when c.StateTime >= SinkTime:
                        b.HoleFreeAt[c.Hole] = Time + 0.3f;
                        b.Critters.RemoveAt(i);
                        break;
                }
            }
        }

        static Part Hand(int h) => h == 0 ? Part.LeftHand : Part.RightHand;

        /// <summary>Hole <paramref name="h"/> in this booth's stage (the spread applied).</summary>
        public Vector2 HoleAt(int h) => new Vector2(Holes[h].x * Spread, Holes[h].y);

        void Hands(int p, Board b, StageBody body, float dt)
        {
            for (int h = 0; h < 2; h++)
            {
                var part = Hand(h);
                if (!body.Sees(part))
                {
                    b.HadHand[h] = false;
                    continue;
                }
                var at = body[part];
                var previous = b.HadHand[h] ? b.PreviousHand[h] : at;
                float speed = Vector2.Distance(at, previous) / dt;
                // a jump of more than a torso length in one frame is the pose model's glitch, not a hand
                bool glitch = Vector2.Distance(at, previous) > 0.9f;
                b.PreviousHand[h] = at;
                b.HadHand[h] = true;
                if (glitch) continue;
                foreach (var c in b.Critters)
                {
                    if (c.State is not (State.Rising or State.Up)) continue;
                    float d = Vector2.Distance(at, HoleAt(c.Hole));
                    if (d > HitRadius * 1.15f)
                    {
                        c.Clear[h] = true;
                        continue;
                    }
                    if (d > HitRadius || b.Dazed > 0f) continue;
                    if (!c.Clear[h] && speed < MovingSpeed) continue;
                    Bonk(p, b, c, at - previous);
                    break;
                }
            }
        }

        void Bonk(int p, Board b, Critter c, Vector2 direction)
        {
            c.State = State.Hit;
            c.StateTime = 0f;
            var at = HoleAt(c.Hole);
            if (c.Kind == Kind.Hedgehog)
            {
                b.Hedgehogs++;
                b.Combo = 0;
                b.Dazed = Daze;
                Add(p, HedgehogPoints);
                Emit(EventKind.Bad, p, at, HedgehogPoints, item: c.Id, variant: (int)c.Kind);
                return;
            }
            b.Combo++;
            b.BestCombo = Mathf.Max(b.BestCombo, b.Combo);
            int points = (c.Kind == Kind.Gold ? GoldPoints : MolePoints) * b.Multiplier;
            if (c.Kind == Kind.Gold) b.Golds++;
            b.Hits++;
            Add(p, points);
            Emit(c.Kind == Kind.Gold ? EventKind.Gold : EventKind.Hit, p, at, points, b.Combo, c.Id, (int)c.Kind, direction);
        }
    }
}
