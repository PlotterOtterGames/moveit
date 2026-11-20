using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// Kap! (catch the sweets in a basket, after the old egg-catching arcade games and Wii Fit's ball heading): candy,
    /// lollipops, candy floss and golden stars fall around the player, and the basket at their waist follows their hips,
    /// so they step side to side under it: where the hips are is the surest thing the camera reads, and the youngest can
    /// play it. From the 10th second bombs fall too (a bomb in the basket costs points and shakes it for a moment). Now and
    /// then a balloon with a prize floats by at head height: a raised hand pops it. Sweets in a row raise the points.
    /// </summary>
    public sealed class CatchRules : MachineRules
    {
        public override Machine Booth => Machine.Catch;

        public enum ItemKind { Candy, Lollipop, Floss, Star, Bomb, Balloon }

        /// <summary>The strip things fall in (torso lengths either side of where the player stood), the basket's rim
        /// height and how far from its middle it catches, where things start.</summary>
        public const float RimY = 0.3f, CatchHalf = 0.5f, TopY = 2.9f, GoneY = -1.3f;
        public const float BalloonReach = 0.45f, Shake = 0.7f;
        public const int BombPoints = -25, BalloonPoints = 40;

        public sealed class Item
        {
            public int Id, Variant;
            public ItemKind Kind;
            public Vector2 Pos;
            public float Speed, Phase, Drift;
            public bool Done, Caught;
            public float DoneAge;
        }

        public sealed class Board
        {
            public readonly List<Item> Items = new List<Item>();
            public readonly System.Random Rng;
            public float NextDrop = 0.7f, NextBalloon = 7f, Shaking, LastX;
            public float BasketX;
            public bool HaveBasket;
            public int Combo, BestCombo, Caught, Missed, Bombs, Balloons;
            public Board(int seed) => Rng = new System.Random(seed);
            /// <summary>x1, then +1 for every 6 in a row, up to x3.</summary>
            public int Multiplier => 1 + Mathf.Min(2, Combo / 6);
        }

        public readonly Board[] Boards;
        /// <summary>The strip things fall in, torso lengths either side of where the player stood (2.25; two players
        /// side by side: narrower).</summary>
        public readonly float Strip;
        int _ids;

        public CatchRules(int players, float duration = 60f, int seed = 1, float strip = 2.25f) : base(players, duration)
        {
            Strip = strip;
            Boards = new Board[Players];
            for (int p = 0; p < Players; p++) Boards[p] = new Board(seed);
        }

        static float DropEvery(float k) => Mathf.Lerp(0.95f, 0.42f, k);
        static float FallSpeed(float k) => Mathf.Lerp(1.15f, 2.4f, k);
        float BombChance(float k) => Time < 10f ? 0f : Mathf.Lerp(0.1f, 0.17f, k);

        public static int PointsOf(ItemKind kind) => kind switch
        {
            ItemKind.Lollipop => 15,
            ItemKind.Floss => 20,
            ItemKind.Star => 50,
            ItemKind.Balloon => BalloonPoints,
            ItemKind.Bomb => BombPoints,
            _ => 10,
        };

        protected override void OnStep(float dt, StageBody[] bodies)
        {
            float k = Progress;
            for (int p = 0; p < Players; p++)
            {
                var b = Boards[p];
                var body = BodyOf(bodies, p);
                b.Shaking = Mathf.Max(0f, b.Shaking - dt);
                if (body != null && body.Sees(Part.LeftHip) && body.Sees(Part.RightHip))
                {
                    b.BasketX = Mathf.Clamp(body.Hips.x, -Strip - 0.3f, Strip + 0.3f);
                    b.HaveBasket = true;
                }
                b.NextDrop -= dt;
                if (b.NextDrop <= 0f && Left > 1f)
                {
                    Drop(p, b, k);
                    b.NextDrop = DropEvery(k) * Range(b.Rng, 0.8f, 1.2f);
                }
                b.NextBalloon -= dt;
                if (b.NextBalloon <= 0f && Left > 3f)
                {
                    Balloon(p, b);
                    b.NextBalloon = Range(b.Rng, 7f, 9.5f);
                }
                Fall(p, b, body, dt);
            }
        }

        void Drop(int p, Board b, float k)
        {
            double roll = b.Rng.NextDouble();
            float bomb = BombChance(k);
            var kind = roll < bomb ? ItemKind.Bomb : roll < bomb + 0.06 ? ItemKind.Star : roll < bomb + 0.18 ? ItemKind.Floss
                     : roll < bomb + 0.38 ? ItemKind.Lollipop : ItemKind.Candy;
            // early on the next one falls within a step or two of the last (always reachable), later anywhere
            float span = Mathf.Lerp(1.6f, 2f * Strip, k);
            float x = Mathf.Clamp(b.LastX + Range(b.Rng, -span, span), -Strip, Strip);
            if (kind != ItemKind.Bomb) b.LastX = x;
            var item = new Item
            {
                Id = ++_ids, Kind = kind, Variant = b.Rng.Next(4), Pos = new Vector2(x, TopY),
                Speed = FallSpeed(k) * Range(b.Rng, 0.85f, 1.15f), Phase = Range(b.Rng, 0f, 6.28f),
            };
            b.Items.Add(item);
            Emit(EventKind.Spawn, p, item.Pos, item: item.Id, variant: (int)kind);
        }

        void Balloon(int p, Board b)
        {
            bool fromLeft = b.Rng.NextDouble() < 0.5;
            var item = new Item
            {
                Id = ++_ids, Kind = ItemKind.Balloon, Variant = b.Rng.Next(4),
                Pos = new Vector2(fromLeft ? -Strip - 0.6f : Strip + 0.6f, Range(b.Rng, 1.85f, 2.2f)),
                Drift = (fromLeft ? 1f : -1f) * Range(b.Rng, 0.55f, 0.75f), Phase = Range(b.Rng, 0f, 6.28f),
            };
            b.Items.Add(item);
            Emit(EventKind.Spawn, p, item.Pos, item: item.Id, variant: (int)item.Kind);
        }

        void Fall(int p, Board b, StageBody body, float dt)
        {
            for (int i = b.Items.Count - 1; i >= 0; i--)
            {
                var it = b.Items[i];
                if (it.Done)
                {
                    it.DoneAge += dt;
                    if (it.DoneAge > 0.6f) b.Items.RemoveAt(i);
                    continue;
                }
                it.Phase += dt;
                if (it.Kind == ItemKind.Balloon)
                {
                    it.Pos.x += it.Drift * dt;
                    it.Pos.y += Mathf.Sin(it.Phase * 2.2f) * 0.12f * dt;
                    if (body != null && Popped(body, it.Pos))
                    {
                        Finish(p, b, it, true);
                        b.Balloons++;
                        Add(p, BalloonPoints);
                        Emit(EventKind.Pop, p, it.Pos, BalloonPoints, item: it.Id, variant: it.Variant);
                    }
                    else if (Mathf.Abs(it.Pos.x) > Strip + 0.8f) b.Items.RemoveAt(i);
                    continue;
                }
                float before = it.Pos.y;
                it.Pos.y -= it.Speed * dt;
                if (it.Kind is ItemKind.Candy or ItemKind.Floss) it.Pos.x += Mathf.Sin(it.Phase * 2.6f) * 0.18f * dt;
                if (before > RimY && it.Pos.y <= RimY && b.HaveBasket && b.Shaking <= 0f && Mathf.Abs(it.Pos.x - b.BasketX) <= CatchHalf)
                {
                    Finish(p, b, it, true);
                    if (it.Kind == ItemKind.Bomb)
                    {
                        b.Bombs++;
                        b.Combo = 0;
                        b.Shaking = Shake;
                        Add(p, BombPoints);
                        Emit(EventKind.Boom, p, it.Pos, BombPoints, item: it.Id, variant: (int)it.Kind);
                    }
                    else
                    {
                        b.Combo++;
                        b.BestCombo = Mathf.Max(b.BestCombo, b.Combo);
                        b.Caught++;
                        int points = PointsOf(it.Kind) * b.Multiplier;
                        Add(p, points);
                        Emit(EventKind.Catch, p, new Vector2(b.BasketX, RimY), points, b.Combo, it.Id, (int)it.Kind);
                    }
                    continue;
                }
                if (it.Pos.y < GoneY)
                {
                    if (it.Kind != ItemKind.Bomb)
                    {
                        b.Missed++;
                        b.Combo = 0;
                        Emit(EventKind.Drop, p, it.Pos, item: it.Id, variant: (int)it.Kind);
                    }
                    b.Items.RemoveAt(i);
                }
            }
        }

        static bool Popped(StageBody body, Vector2 at) =>
            body.Sees(Part.LeftHand) && Vector2.Distance(body[Part.LeftHand], at) <= BalloonReach ||
            body.Sees(Part.RightHand) && Vector2.Distance(body[Part.RightHand], at) <= BalloonReach;

        static void Finish(int p, Board b, Item it, bool caught)
        {
            it.Done = true;
            it.Caught = caught;
            it.DoneAge = 0f;
        }
    }
}
