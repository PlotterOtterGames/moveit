using System;
using System.Collections.Generic;

namespace MoveIt.Games.Kart
{
    /// <summary>
    /// A cup (Gara!'s tournament, like Tenis!'s and Volejboll!'s): three races, one on each track, the same six karts
    /// (the players' first, then the computer's drivers drawn at the start); every race's places give points
    /// (<see cref="PointsFor"/>: 10, 8, 6, 4, 2, 1), each race a little stronger (<see cref="KartLevel.StrengthFor"/>). The
    /// most points after the third race win the cup (a tie goes to the better place in the last race); the top three stand
    /// on the podium. Pure logic, tested.
    /// </summary>
    public sealed class KartCup
    {
        public const int Races = 3;
        public static readonly int[] PointsFor = { 10, 8, 6, 4, 2, 1 };

        public readonly KartDifficulty Difficulty;
        public readonly KartMode Mode;
        /// <summary>The computer's drivers (<see cref="KartLevel.Names"/> places), in kart order after the players' karts.</summary>
        public readonly int[] Drivers;
        /// <summary>The players' karts (1, or 2 against each other).</summary>
        public readonly int HumanKarts;
        /// <summary>Every racer's points so far (kart order).</summary>
        public readonly int[] Points;
        /// <summary>Every race's places (kart order, 1 = first), as they were recorded.</summary>
        public readonly List<int[]> Places = new List<int[]>();

        /// <summary>The race to run next (0 .. 2); <see cref="Races"/> once the cup is over.</summary>
        public int Race { get; private set; }
        public bool Over => Race >= Races;
        public int Racers => HumanKarts + Drivers.Length;

        public KartCup(KartDifficulty difficulty, KartMode mode, int seed)
        {
            Difficulty = difficulty;
            Mode = mode;
            HumanKarts = mode == KartMode.Versus ? 2 : 1;
            Drivers = DrawDrivers(6 - HumanKarts, seed);
            Points = new int[Racers];
        }

        /// <summary><paramref name="count"/> different drivers of the ten, at random.</summary>
        public static int[] DrawDrivers(int count, int seed)
        {
            var rng = new Random(seed);
            var pool = new List<int>();
            for (int i = 0; i < KartLevel.Drivers; i++) pool.Add(i);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            return pool.GetRange(0, Math.Min(count, pool.Count)).ToArray();
        }

        /// <summary>The track of race <paramref name="race"/>: the coast, the village, the dino valley.</summary>
        public static int TrackOf(int race) => Math.Max(0, Math.Min(KartTracks.Count - 1, race));

        public KartLevel Level => KartLevel.For(Difficulty, Math.Min(Race, Races - 1));
        public int Track => TrackOf(Race);

        /// <summary>A race is over: its places (kart order, 1 = first) give points; on to the next race.</summary>
        public void Record(IList<int> places)
        {
            if (Over) return;
            var copy = new int[Racers];
            for (int i = 0; i < Racers && i < places.Count; i++)
            {
                copy[i] = places[i];
                int p = places[i] - 1;
                if (p >= 0 && p < PointsFor.Length) Points[i] += PointsFor[p];
            }
            Places.Add(copy);
            Race++;
        }

        /// <summary>The racers (kart order) from the most points to the fewest (a tie: the better place in the last race
        /// recorded).</summary>
        public int[] Standings()
        {
            var order = new List<int>();
            for (int i = 0; i < Racers; i++) order.Add(i);
            var last = Places.Count > 0 ? Places[Places.Count - 1] : null;
            order.Sort((a, b) =>
            {
                int c = Points[b].CompareTo(Points[a]);
                if (c != 0) return c;
                if (last != null) return last[a].CompareTo(last[b]);
                return a.CompareTo(b);
            });
            return order.ToArray();
        }

        /// <summary>Racer <paramref name="racer"/>'s place in the standings (1 = leading).</summary>
        public int StandingOf(int racer) => Array.IndexOf(Standings(), racer) + 1;

        /// <summary>The player's (or, against each other, player 1's) kart won the cup.</summary>
        public bool Won => Over && Standings()[0] < HumanKarts;

        public string NameOf(int racer) => racer < HumanKarts ? null : KartLevel.Names[Drivers[racer - HumanKarts] % KartLevel.Drivers];
    }
}
