using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// Fruta! (fruit slicing in the mirror, after Fruit Ninja, Nex Playground's #6 of 2025): fruit is thrown up in arcs
    /// around the player and a quick move of either hand through one slices it. Several in one swipe make a combo; a
    /// golden star fruit is worth three; from the 8th second bombs come up too, and slicing one costs points and a
    /// moment's daze. Fruit that falls back unsliced costs nothing (only the streak). A slice needs the hand moving at
    /// least <see cref="SliceSpeed"/> through it; a bomb needs it two frames running, so one glitched frame of the pose
    /// model's wrist never sets one off.
    /// </summary>
    public sealed class SliceRules : MachineRules
    {
        public override Machine Booth => Machine.Slice;

        public enum FruitKind { Apple, Orange, Melon, Banana, Berry, Pear, Star, Bomb }

        public const float Gravity = 4.2f, LaunchY = -1.55f, GoneY = -2.1f;
        /// <summary>Torso lengths a second a hand must move to slice.</summary>
        public const float SliceSpeed = 2.2f;
        /// <summary>A slice may pass this far outside a fruit and still cut it (torso lengths).</summary>
        public const float Reach = 0.12f;
        public const int FruitPoints = 10, StarPoints = 30, BombPoints = -30;
        public const float Daze = 0.8f;

        public sealed class Fruit
        {
            public int Id;
            public FruitKind Kind;
            public Vector2 Pos, Vel;
            public float Angle, Spin, Radius;
            /// <summary>How quick it flies: velocity times this, gravity times its square (the same arc, sooner).</summary>
            public float Pace = 1f;
            public bool Sliced, Gone;
            /// <summary>Seconds since it was sliced; the cut's direction.</summary>
            public float SlicedAge;
            public Vector2 Cut;
        }

        public sealed class Board
        {
            public readonly List<Fruit> Fruit = new List<Fruit>();
            public readonly System.Random Rng;
            public readonly Vector2[] Previous = new Vector2[2];
            public readonly bool[] HadHand = new bool[2];
            /// <summary>Per hand: frames in a row it moved at slicing speed, and the fruit cut in this swipe.</summary>
            public readonly int[] FastFrames = new int[2], InSwipe = new int[2];
            public readonly float[] SlowFor = new float[2];
            public readonly Vector2[] LastMove = new Vector2[2];
            public float NextWave = 0.9f, Dazed;
            public int Streak, BestStreak, Sliced, Stars, Bombs, Combos, Dropped;
            public Board(int seed) => Rng = new System.Random(seed);
        }

        public readonly Board[] Boards;
        /// <summary>How wide the fruit is thrown (1; two players side by side: narrower).</summary>
        public readonly float Spread;
        int _ids;

        public SliceRules(int players, float duration = 60f, int seed = 1, float spread = 1f) : base(players, duration)
        {
            Spread = spread;
            Boards = new Board[Players];
            for (int p = 0; p < Players; p++) Boards[p] = new Board(seed);
        }

        static float WaveEvery(float k) => Mathf.Lerp(1.5f, 0.75f, k);
        static float PaceAt(float k) => Mathf.Lerp(1f, 1.4f, k);
        float BombChance(float k) => Time < 8f ? 0f : Mathf.Lerp(0.1f, 0.18f, k);

        public static float RadiusOf(FruitKind kind) => kind switch
        {
            FruitKind.Melon => 0.4f,
            FruitKind.Banana => 0.33f,
            FruitKind.Berry => 0.25f,
            _ => 0.3f,
        };

        protected override void OnStep(float dt, StageBody[] bodies)
        {
            float k = Progress;
            for (int p = 0; p < Players; p++)
            {
                var b = Boards[p];
                b.Dazed = Mathf.Max(0f, b.Dazed - dt);
                b.NextWave -= dt;
                if (b.NextWave <= 0f && Left > 1.2f)
                {
                    Wave(p, b, k);
                    b.NextWave = WaveEvery(k) * Range(b.Rng, 0.8f, 1.2f);
                }
                Fly(p, b, dt);
                var body = BodyOf(bodies, p);
                if (body != null) Hands(p, b, body, dt);
            }
        }

        void Wave(int p, Board b, float k)
        {
            int count = 1 + b.Rng.Next(Mathf.Min(4, 1 + Mathf.FloorToInt(4f * k)) + 1);
            float pace = PaceAt(k);
            bool bomb = false;
            for (int i = 0; i < count; i++)
            {
                FruitKind kind;
                double roll = b.Rng.NextDouble();
                if (!bomb && roll < BombChance(k))
                {
                    kind = FruitKind.Bomb;
                    bomb = true;
                }
                else if (roll > 0.95) kind = FruitKind.Star;
                else kind = (FruitKind)b.Rng.Next(6);
                float x0 = Range(b.Rng, -2.0f, 2.0f) * Spread;
                float apex = Range(b.Rng, 1.3f, 2.35f);
                float vy = Mathf.Sqrt(2f * Gravity * (apex - LaunchY));
                float up = vy / Gravity;                         // seconds to the top
                float target = Range(b.Rng, -1.2f, 1.2f) * Spread;   // where it is at the top
                var f = new Fruit
                {
                    Id = ++_ids, Kind = kind, Radius = RadiusOf(kind),
                    Pos = new Vector2(x0, LaunchY - 0.15f * i), Vel = new Vector2((target - x0) / up, vy) * pace, Pace = pace,
                    Angle = Range(b.Rng, 0f, 360f), Spin = Range(b.Rng, -220f, 220f),
                };
                b.Fruit.Add(f);
                Emit(EventKind.Spawn, p, f.Pos, item: f.Id, variant: (int)kind);
            }
        }

        void Fly(int p, Board b, float dt)
        {
            for (int i = b.Fruit.Count - 1; i >= 0; i--)
            {
                var f = b.Fruit[i];
                f.Vel.y -= Gravity * f.Pace * f.Pace * dt;
                f.Pos += f.Vel * dt;
                f.Angle += f.Spin * dt;
                if (f.Sliced) f.SlicedAge += dt;
                if (f.Pos.y < GoneY && f.Vel.y < 0f || f.Sliced && f.SlicedAge > 1.4f)
                {
                    if (!f.Sliced && f.Kind != FruitKind.Bomb)
                    {
                        b.Dropped++;
                        b.Streak = 0;
                        Emit(EventKind.Escape, p, f.Pos, item: f.Id, variant: (int)f.Kind);
                    }
                    b.Fruit.RemoveAt(i);
                }
            }
        }

        static Part Hand(int h) => h == 0 ? Part.LeftHand : Part.RightHand;

        void Hands(int p, Board b, StageBody body, float dt)
        {
            for (int h = 0; h < 2; h++)
            {
                var part = Hand(h);
                if (!body.Sees(part))
                {
                    b.HadHand[h] = false;
                    EndSwipe(p, b, h);
                    continue;
                }
                var at = body[part];
                var from = b.HadHand[h] ? b.Previous[h] : at;
                b.Previous[h] = at;
                b.HadHand[h] = true;
                float step = Vector2.Distance(at, from);
                if (step > 1.2f) continue;     // a glitch of the pose model, not a swipe
                bool fast = step / dt >= SliceSpeed;
                if (!fast)
                {
                    b.FastFrames[h] = 0;
                    b.SlowFor[h] += dt;
                    if (b.SlowFor[h] > 0.12f) EndSwipe(p, b, h);
                    continue;
                }
                // a swipe keeps its direction; a glitch goes out and comes straight back (two fast frames, opposite)
                var move = at - from;
                b.FastFrames[h] = b.FastFrames[h] > 0 && Vector2.Dot(move, b.LastMove[h]) > 0f ? b.FastFrames[h] + 1 : 1;
                b.LastMove[h] = move;
                b.SlowFor[h] = 0f;
                if (b.Dazed > 0f) continue;
                foreach (var f in b.Fruit)
                {
                    if (f.Sliced) continue;
                    if (Geo.SegmentDistance(f.Pos, from, at) > f.Radius + Reach) continue;
                    if (f.Kind == FruitKind.Bomb)
                    {
                        if (b.FastFrames[h] < 2) continue;
                        Boom(p, b, f, at - from);
                        break;
                    }
                    Cut(p, b, f, h, at - from);
                }
            }
        }

        void Cut(int p, Board b, Fruit f, int hand, Vector2 direction)
        {
            f.Sliced = true;
            f.Cut = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
            b.Sliced++;
            b.Streak++;
            b.BestStreak = Mathf.Max(b.BestStreak, b.Streak);
            b.InSwipe[hand]++;
            int points = f.Kind == FruitKind.Star ? StarPoints : FruitPoints;
            if (f.Kind == FruitKind.Star) b.Stars++;
            Add(p, points);
            Emit(EventKind.Slice, p, f.Pos, points, b.InSwipe[hand], f.Id, (int)f.Kind, f.Cut);
        }

        void Boom(int p, Board b, Fruit f, Vector2 direction)
        {
            f.Sliced = true;
            f.Cut = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
            b.Bombs++;
            b.Streak = 0;
            b.Dazed = Daze;
            Add(p, BombPoints);
            Emit(EventKind.Boom, p, f.Pos, BombPoints, item: f.Id, variant: (int)f.Kind);
        }

        /// <summary>The swipe is over: three or more fruit in it was a combo.</summary>
        void EndSwipe(int p, Board b, int hand)
        {
            int n = b.InSwipe[hand];
            b.InSwipe[hand] = 0;
            b.SlowFor[hand] = 0f;
            if (n < 3) return;
            int bonus = n == 3 ? 15 : n == 4 ? 30 : 50;
            b.Combos++;
            Add(p, bonus);
            Emit(EventKind.Combo, p, b.Previous[hand], bonus, n);
        }
    }
}
