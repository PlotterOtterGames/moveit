using System;
using System.Collections.Generic;

namespace MoveIt.Games.Runner
{
    /// <summary>What a thing on the course does to the runner.</summary>
    public enum Rule { Jump, Duck, Dodge, Egg, GoldEgg, Sight }

    /// <summary>The art each thing is drawn with (art/runner/valley.py's pieces).</summary>
    public enum Piece { LogLow, StoneRow, LogLong, VineBridge, BrontoNeck, Boulder, Trike, Egg, EggGold, RockArch, RibArch }

    /// <summary>One thing placed on the course: its art, its lane (-1, 0, 1; 0 for the whole-way pieces), how far along
    /// the course its middle is (m), and (eggs) how high it floats.</summary>
    public struct Spawn
    {
        public Piece Piece;
        public int Lane;
        public float At;
        public float Height;
        public bool Wide;

        public Rule Rule => RunnerTrack.RuleOf(Piece);
    }

    /// <summary>
    /// Vrapo!'s course, laid out ahead of the runner in designed patterns instead of random rows (2026-11-18, the console
    /// polish): eggs arc over the logs at the jump's own height (they show when to jump), lines of eggs lead into the free
    /// lane and weave between lanes, whole-way obstacles are one wide piece (a fallen tree, a sleeping brontosaurus's
    /// neck) instead of three copies, the dodge pieces vary (a mossy boulder, a sleeping triceratops), a rare golden egg
    /// sits where it's a little daring to fetch, and every ~300 m the runner passes under a landmark (a stone arch, a
    /// giant ribcage). The first 20 seconds teach one thing at a time; harder combinations come with time.
    /// The rules are the game's (RunnerGame): a jump thing is lower than 0.7 m, a duck thing leaves 1.15 m clear, a dodge
    /// thing fills its lane for 6 m. No engine: tested in EditMode.
    /// </summary>
    public sealed class RunnerTrack
    {
        /// <summary>A jump's height and length in time (RunnerPlayer): egg arcs follow it.</summary>
        public const float JumpHeight = 1.6f, JumpTime = 0.8f;
        /// <summary>An egg's usual height (the runner's middle: picked up standing, sliding or hopping).</summary>
        public const float EggHeight = 1.0f;
        public const float MinRowGap = 14f;
        public const float RowInterval = 1.7f;
        public const float EasySeconds = 20f, MediumSeconds = 45f;
        public const float SightEvery = 300f;

        readonly Random _rng;
        float _nextRow;
        float _nextSight;
        int _rowsSinceGold;
        int _lastPattern = -1;
        int _sights;

        public RunnerTrack(int seed, float firstRow = 45f)
        {
            _rng = new Random(seed);
            _nextRow = firstRow;
            _nextSight = 150f + (float)_rng.NextDouble() * 60f;
        }

        /// <summary>The course distance of the next row not yet laid out.</summary>
        public float NextRow => _nextRow;

        public static Rule RuleOf(Piece p) => p switch
        {
            Piece.LogLow or Piece.StoneRow or Piece.LogLong => Rule.Jump,
            Piece.VineBridge or Piece.BrontoNeck => Rule.Duck,
            Piece.Boulder or Piece.Trike => Rule.Dodge,
            Piece.Egg => Rule.Egg,
            Piece.EggGold => Rule.GoldEgg,
            _ => Rule.Sight,
        };

        /// <summary>How deep a thing is along the course (m): its hitbox, as the old gray boxes.</summary>
        public static float DepthOf(Piece p) => RuleOf(p) switch
        {
            Rule.Dodge => 6f,
            Rule.Egg or Rule.GoldEgg => 0.6f,
            Rule.Sight => 0f,
            _ => 0.4f,
        };

        /// <summary>Lays out rows up to <paramref name="upTo"/> metres along the course. <paramref name="runTime"/> and
        /// <paramref name="speed"/> (m/s) choose how hard and how far apart.</summary>
        public void Fill(float upTo, float runTime, float speed, List<Spawn> into)
        {
            while (_nextRow < upTo)
            {
                float gap = Math.Max(MinRowGap, speed * RowInterval);
                Row(_nextRow, runTime, speed, gap, into);
                _nextRow += gap;
            }
            while (_nextSight < upTo)
            {
                into.Add(new Spawn { Piece = _sights++ % 2 == 0 ? Piece.RockArch : Piece.RibArch, At = _nextSight, Wide = true });
                _nextSight += SightEvery + (float)_rng.NextDouble() * 80f;
            }
        }

        int Lane() => _rng.Next(-1, 2);

        static int Other(int lane, int pick) => pick >= lane ? pick + 1 : pick;   // pick in -1..0: a lane not `lane`

        int OtherLane(int lane) => Other(lane, _rng.Next(-1, 1));

        bool Chance(double p) => _rng.NextDouble() < p;

        void Row(float z, float runTime, float speed, float gap, List<Spawn> into)
        {
            int patterns = runTime < EasySeconds ? 4 : runTime < MediumSeconds ? 7 : 9;
            int pattern = _rng.Next(patterns);
            if (pattern == _lastPattern) pattern = _rng.Next(patterns);   // fewer repeats in a row
            _lastPattern = pattern;
            int free = Lane();
            _rowsSinceGold++;
            switch (pattern)
            {
                case 0: // one dodge thing, eggs leading into a free lane
                {
                    int a = OtherLane(free);
                    Add(into, DodgePiece(), a, z + 3f);
                    EggLine(into, free, z - 6f, 5);
                    break;
                }
                case 1: // a fallen tree right across: jump it, eggs arcing over it in one lane
                    Add(into, Piece.LogLong, 0, z, wide: true);
                    EggArc(into, Lane(), z, speed);
                    break;
                case 2: // a long neck right across: duck under it, low eggs under it
                    Add(into, Piece.BrontoNeck, 0, z, wide: true);
                    EggLine(into, Lane(), z - 2.5f, 3, height: 0.55f, spacing: 2.5f);
                    break;
                case 3: // a breather: eggs weaving from lane to lane (lane changes taught)
                    EggWeave(into, z - 4f, free);
                    break;
                case 4: // two dodge things: find the free lane
                    for (int lane = -1; lane <= 1; lane++)
                        if (lane != free) Add(into, DodgePiece(), lane, z + 3f);
                    EggLine(into, free, z - 2f, 5);
                    break;
                case 5: // a dodge thing, a jump thing, a free lane
                case 6: // a dodge thing, a duck thing, a free lane
                {
                    int a = OtherLane(free);
                    int b = -free - a;
                    Add(into, DodgePiece(), a, z + 3f);
                    if (pattern == 5)
                    {
                        Add(into, Chance(0.5) ? Piece.LogLow : Piece.StoneRow, b, z);
                        if (Chance(0.5)) EggArc(into, b, z, speed);
                        else EggLine(into, free, z - 4f, 4);
                    }
                    else
                    {
                        Add(into, Piece.VineBridge, b, z);
                        EggLine(into, free, z - 4f, 4);
                    }
                    break;
                }
                case 7: // jump things in two lanes, the third blocked: jump (eggs over the logs)
                {
                    int a = OtherLane(free);
                    for (int lane = -1; lane <= 1; lane++)
                    {
                        if (lane == a) Add(into, DodgePiece(), lane, z + 3f);
                        else Add(into, Chance(0.5) ? Piece.LogLow : Piece.StoneRow, lane, z);
                    }
                    EggArc(into, free, z, speed);
                    break;
                }
                default: // a quick double: a whole-way jump, then a dodge thing soon after
                {
                    Add(into, Piece.LogLong, 0, z, wide: true);
                    float second = z + Math.Max(9f, speed * 0.85f);
                    if (second < z + gap - 6f)
                    {
                        int a = Lane();
                        Add(into, DodgePiece(), a, second + 3f);
                        EggLine(into, OtherLane(a), second - 3f, 3);
                    }
                    break;
                }
            }
            // a golden egg now and then, past the row in its free lane (not after the quick double: its second thing
            // may stand there)
            if (pattern < 8 && _rowsSinceGold > 18 && Chance(0.35))
            {
                _rowsSinceGold = 0;
                into.Add(new Spawn { Piece = Piece.EggGold, Lane = free, At = z + Math.Min(9f, gap * 0.4f), Height = EggHeight + 0.2f });
            }
        }

        Piece DodgePiece() => Chance(0.4) ? Piece.Trike : Piece.Boulder;

        static void Add(List<Spawn> into, Piece piece, int lane, float at, bool wide = false) =>
            into.Add(new Spawn { Piece = piece, Lane = wide ? 0 : lane, At = at, Wide = wide });

        static void EggLine(List<Spawn> into, int lane, float from, int count, float height = EggHeight, float spacing = 2.5f)
        {
            for (int i = 0; i < count; i++)
                into.Add(new Spawn { Piece = Piece.Egg, Lane = lane, At = from + i * spacing, Height = height });
        }

        /// <summary>Eggs along the jump's own arc over a thing at <paramref name="at"/>: picked up only by jumping on time.</summary>
        static void EggArc(List<Spawn> into, int lane, float at, float speed)
        {
            float length = speed * JumpTime;
            const int n = 5;
            for (int i = 0; i < n; i++)
            {
                float p = (i + 0.5f) / n;
                float feet = 4f * JumpHeight * p * (1f - p);
                into.Add(new Spawn { Piece = Piece.Egg, Lane = lane, At = at + (p - 0.5f) * length, Height = feet + 0.9f });
            }
        }

        void EggWeave(List<Spawn> into, float from, int start)
        {
            int lane = start;
            int dir = lane == 1 ? -1 : lane == -1 ? 1 : (Chance(0.5) ? 1 : -1);
            for (int i = 0; i < 7; i++)
            {
                into.Add(new Spawn { Piece = Piece.Egg, Lane = lane, At = from + i * 2.6f, Height = EggHeight });
                if (i % 2 == 1)
                {
                    if (lane + dir > 1 || lane + dir < -1) dir = -dir;
                    lane += dir;
                }
            }
        }
    }
}
