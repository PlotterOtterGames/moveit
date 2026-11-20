using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>What a booth's round won: its stars (0..3), the tickets, a new best, and the plush prizes the tickets
    /// reached.</summary>
    public struct RoundReward
    {
        public int Stars, Tickets;
        public bool NewBest;
        public List<int> Prizes;
    }

    /// <summary>
    /// What Lunapark! keeps between rounds (PlayerPrefs "arcade.progress"): each booth's best (one player), the best
    /// party, and the tickets won so far. A round earns 0..3 stars by its score (<see cref="StarsAt"/>) and tickets for
    /// them (2 for playing, 3 more a star); tickets add up to the plush prizes on the shelf (a dinosaur, a bear...), like
    /// a fairground's prize counter. Nothing is sold, nothing is spent: the prizes stay won.
    /// </summary>
    public sealed class ArcadeProgress
    {
        public const int Machines = 6, Prizes = 8;
        /// <summary>Tickets that win each plush, in order.</summary>
        public static readonly int[] PrizeAt = { 10, 30, 60, 100, 150, 220, 300, 400 };

        /// <summary>The score for one, two and three stars, per booth (a minute; one player).</summary>
        public static int[] StarsAt(Machine m) => m switch
        {
            Machine.Whack => new[] { 400, 1000, 2000 },
            Machine.Slice => new[] { 400, 800, 1250 },
            Machine.Wall => new[] { 500, 1000, 1500 },
            Machine.Catch => new[] { 300, 800, 1500 },
            Machine.Flap => new[] { 250, 500, 800 },
            _ => new[] { 700, 1300, 1800 },
        };

        public readonly int[] Best = { -1, -1, -1, -1, -1, -1 };
        public int PartyBest = -1;
        public int Tickets;

        public int PrizesWon
        {
            get
            {
                int n = 0;
                foreach (int at in PrizeAt)
                    if (Tickets >= at) n++;
                return n;
            }
        }

        /// <summary>Tickets still needed for the next plush (0 when all are won).</summary>
        public int ToNextPrize => PrizesWon >= Prizes ? 0 : PrizeAt[PrizesWon] - Tickets;

        public static int Stars(Machine m, int score)
        {
            var at = StarsAt(m);
            int s = 0;
            foreach (int need in at)
                if (score >= need) s++;
            return s;
        }

        public static int TicketsFor(int stars) => 2 + 3 * stars;

        /// <summary>A round of booth <paramref name="m"/> was played: its reward. <paramref name="solo"/> rounds count for
        /// the bests (two players share the tickets, never the bests).</summary>
        public RoundReward Record(Machine m, int score, bool solo = true)
        {
            int stars = Stars(m, score);
            var reward = new RoundReward { Stars = stars, Tickets = TicketsFor(stars), Prizes = new List<int>() };
            int before = PrizesWon;
            Tickets += reward.Tickets;
            for (int i = before; i < PrizesWon; i++) reward.Prizes.Add(i);
            if (solo && score > Best[(int)m])
            {
                reward.NewBest = Best[(int)m] >= 0 || score > 0;
                Best[(int)m] = score;
            }
            return reward;
        }

        /// <summary>A party's total (one player): true when it's a new best.</summary>
        public bool RecordParty(int total)
        {
            if (total <= PartyBest) return false;
            PartyBest = total;
            return true;
        }

        public string Save()
        {
            var c = CultureInfo.InvariantCulture;
            return $"v1;best={string.Join(",", System.Array.ConvertAll(Best, b => b.ToString(c)))};party={PartyBest.ToString(c)};tickets={Tickets.ToString(c)}";
        }

        public static ArcadeProgress Load(string text)
        {
            var p = new ArcadeProgress();
            if (string.IsNullOrEmpty(text) || !text.StartsWith("v1;")) return p;
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string key = part.Substring(0, eq), value = part.Substring(eq + 1);
                switch (key)
                {
                    case "best":
                        var bests = value.Split(',');
                        for (int i = 0; i < Mathf.Min(bests.Length, Machines); i++)
                            if (int.TryParse(bests[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)) p.Best[i] = Mathf.Max(-1, b);
                        break;
                    case "party":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int party)) p.PartyBest = Mathf.Max(-1, party);
                        break;
                    case "tickets":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t)) p.Tickets = Mathf.Max(0, t);
                        break;
                }
            }
            return p;
        }
    }

    /// <summary>
    /// A party (Festë): five of the six booths in a random order, shorter rounds. One player: their points add up (a best
    /// party). Two players: both play every round side by side, the round's winner gets a star, and whoever has more
    /// stars after five rounds wins (more points overall breaks a tie).
    /// </summary>
    public sealed class ArcadeParty
    {
        public const int Rounds = 5;
        public const float RoundSeconds = 40f;

        public readonly int Players;
        public readonly Machine[] Order;
        public readonly int[] Wins, Total;
        public readonly List<int[]> Scores = new List<int[]>();
        public int Round { get; private set; }
        public bool Over => Round >= Order.Length;
        public Machine Current => Order[Mathf.Min(Round, Order.Length - 1)];

        public ArcadeParty(int players, int seed)
        {
            Players = Mathf.Clamp(players, 1, 2);
            Wins = new int[Players];
            Total = new int[Players];
            var rng = new System.Random(seed);
            var all = new List<Machine> { Machine.Whack, Machine.Slice, Machine.Wall, Machine.Catch, Machine.Flap, Machine.KeepUp };
            for (int i = all.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (all[i], all[j]) = (all[j], all[i]);
            }
            Order = all.GetRange(0, Rounds).ToArray();
        }

        /// <summary>A round was played: its winner (0 or 1; -1 = a draw or one player).</summary>
        public int Record(int[] scores)
        {
            var copy = new int[Players];
            for (int p = 0; p < Players; p++)
            {
                copy[p] = scores != null && p < scores.Length ? scores[p] : 0;
                Total[p] += copy[p];
            }
            Scores.Add(copy);
            Round++;
            if (Players < 2 || copy[0] == copy[1]) return -1;
            int w = copy[0] > copy[1] ? 0 : 1;
            Wins[w]++;
            return w;
        }

        /// <summary>The party's winner once it's over: more round wins, else more points; -1 = a draw (or one player).</summary>
        public int Winner
        {
            get
            {
                if (Players < 2) return -1;
                if (Wins[0] != Wins[1]) return Wins[0] > Wins[1] ? 0 : 1;
                if (Total[0] != Total[1]) return Total[0] > Total[1] ? 0 : 1;
                return -1;
            }
        }
    }
}
