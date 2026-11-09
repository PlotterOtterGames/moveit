using System;

namespace MoveIt.Games.Volley
{
    /// <summary>
    /// A tournament (as Tenis!'s): eight pairs in a bracket, the player's team and seven computer pairs drawn at random;
    /// three rounds (quarter-finals, semi-finals, the final); lose once and you're out. Every round is played a little
    /// stronger (<see cref="VolleyLevel.StrengthFor"/>). The other pairs' matches are decided by chance as each round is
    /// played; after the player is out the rest is played out too, so the bracket shows a champion. Pure logic, tested.
    /// </summary>
    public sealed class VolleyTournament
    {
        public const int Teams = 8, Rounds = 3;
        /// <summary>The player's team in <see cref="Draw"/> and in results.</summary>
        public const int You = -1;

        public readonly VolleyDifficulty Difficulty;
        /// <summary>Points to win a match (the simulated scores too).</summary>
        public readonly int PointsToWin;
        /// <summary>The bracket from top to bottom: the player's team first, then the computer's pairs (their numbers:
        /// <see cref="VolleyLevel.OpponentName"/>). Neighbours meet in the quarter-finals, then the winners of 0-3 and of
        /// 4-7 in the semi-finals, then the final.</summary>
        public readonly int[] Draw = new int[Teams];

        /// <summary>A match of the bracket: its two teams (entries of <see cref="Draw"/>) and, once played, the winner and
        /// the points each side won.</summary>
        public struct Result
        {
            public int A, B, Winner, PointsA, PointsB;
            public bool Played;
        }

        /// <summary>Every round's matches in bracket order: 4, 2, 1.</summary>
        public readonly Result[][] Results = { new Result[4], new Result[2], new Result[1] };

        /// <summary>The round the player plays next (0 = quarter-final ... 2 = final); <see cref="Rounds"/> once they've won
        /// the final.</summary>
        public int Round { get; private set; }
        public bool Out { get; private set; }
        public bool Won => Round >= Rounds;
        public bool Over => Out || Won;
        public int? Champion => Results[Rounds - 1][0].Played ? Results[Rounds - 1][0].Winner : (int?)null;

        readonly Random _rng;

        public VolleyTournament(VolleyDifficulty difficulty, int seed, int pointsToWin = 7)
        {
            Difficulty = difficulty;
            PointsToWin = Math.Max(1, pointsToWin);
            _rng = new Random(seed);
            Draw[0] = You;
            var pool = new int[VolleyLevel.Pairs];
            for (int i = 0; i < pool.Length; i++) pool[i] = i;
            for (int i = pool.Length - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            for (int i = 1; i < Teams; i++) Draw[i] = pool[i - 1];
            for (int i = 0; i < 4; i++) Results[0][i] = new Result { A = Draw[2 * i], B = Draw[2 * i + 1] };
        }

        /// <summary>The pair the player meets in the current round, or -1 when it's over.</summary>
        public int Opponent => Over ? -1 : Results[Round][0].B;

        /// <summary>The current round's pair, at the round's strength.</summary>
        public VolleyLevel Level => VolleyLevel.For(Difficulty, Round, Math.Max(0, Opponent));

        public string NameOf(int opponent) => VolleyLevel.OpponentName(opponent);

        /// <summary>The player's match of this round is over: recorded, the round's other matches played (by chance), and the
        /// player goes on to the next round or is out (then the rest is played out).</summary>
        public void Record(bool won, int pointsFor, int pointsAgainst)
        {
            if (Over) return;
            ref var mine = ref Results[Round][0];
            mine.Played = true;
            mine.Winner = won ? You : mine.B;
            mine.PointsA = pointsFor;
            mine.PointsB = pointsAgainst;
            PlayOthers(Round);
            if (won) Round++;
            else
            {
                Out = true;
                for (int r = Round + 1; r < Rounds; r++) PlayOthers(r, all: true);
            }
        }

        void PlayOthers(int round, bool all = false)
        {
            var matches = Results[round];
            for (int i = 0; i < matches.Length; i++)
            {
                if (round > 0)
                {
                    matches[i].A = Results[round - 1][2 * i].Winner;
                    matches[i].B = Results[round - 1][2 * i + 1].Winner;
                }
                if (matches[i].Played || (!all && i == 0)) continue;
                bool aWins = _rng.NextDouble() < 0.5;
                int loser = _rng.Next(Math.Max(1, PointsToWin - 4), PointsToWin - 1);
                matches[i].Winner = aWins ? matches[i].A : matches[i].B;
                matches[i].PointsA = aWins ? PointsToWin : loser;
                matches[i].PointsB = aWins ? loser : PointsToWin;
                matches[i].Played = true;
            }
            if (round + 1 >= Rounds) return;
            var next = Results[round + 1];
            for (int i = 0; i < next.Length; i++)
            {
                if (Results[round][2 * i].Played) next[i].A = Results[round][2 * i].Winner;
                if (Results[round][2 * i + 1].Played) next[i].B = Results[round][2 * i + 1].Winner;
            }
        }
    }
}
