using System;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// A tournament (Tenis!'s second format, 2026-11-07, user: "3 difficulties, a single game and a tournament mode"):
    /// eight in a bracket, the player (or the two players' team) and seven computer opponents drawn at random: seven of the
    /// fourteen players in singles (2026-11-07: one player plays singles), seven pairs in doubles (two players together);
    /// three rounds (quarter-finals, semi-finals, the final); lose once and you're out. The player's opponent in each round
    /// is whoever won that part of the bracket, and every round is played a little stronger
    /// (<see cref="TennisLevel.StrengthFor"/>). The other pairs' matches are decided by chance as each round is played
    /// (the bracket shows them); after the player is out, the rest is played out too, so the bracket shows a champion.
    /// Pure logic, tested.
    /// </summary>
    public sealed class TennisTournament
    {
        public const int Teams = 8, Rounds = 3;
        /// <summary>The player's team in <see cref="Draw"/> and in results.</summary>
        public const int You = -1;

        public readonly TennisDifficulty Difficulty;
        /// <summary>Games to win a match (the simulated scores too).</summary>
        public readonly int GamesToWin;
        /// <summary>Singles (the opponents are players) or doubles (pairs).</summary>
        public readonly bool Singles;
        /// <summary>The bracket from top to bottom: the player's team (<see cref="You"/>) first, then the computer's
        /// opponents (their numbers: <see cref="TennisLevel.OpponentName"/>). Neighbours meet in the quarter-finals (0-1,
        /// 2-3, 4-5, 6-7), then the winners of 0-3 and of 4-7 in the semi-finals, then the final.</summary>
        public readonly int[] Draw = new int[Teams];

        /// <summary>A match of the bracket: its two teams (entries of <see cref="Draw"/>), and once played, the winner and
        /// the games each side won.</summary>
        public struct Result
        {
            public int A, B, Winner, GamesA, GamesB;
            public bool Played;
        }

        /// <summary>Every round's matches in bracket order: 4, 2, 1.</summary>
        public readonly Result[][] Results = { new Result[4], new Result[2], new Result[1] };

        /// <summary>The round the player plays next (0 = quarter-final ... 2 = final); <see cref="Rounds"/> once they've
        /// won the final.</summary>
        public int Round { get; private set; }
        /// <summary>The player lost a match: out of the tournament.</summary>
        public bool Out { get; private set; }
        public bool Won => Round >= Rounds;
        public bool Over => Out || Won;

        /// <summary>The tournament's winner (an entry of <see cref="Draw"/>) once the final is played, else null.</summary>
        public int? Champion => Results[Rounds - 1][0].Played ? Results[Rounds - 1][0].Winner : (int?)null;

        readonly Random _rng;

        public TennisTournament(TennisDifficulty difficulty, int seed, int gamesToWin = 2, bool singles = true)
        {
            Difficulty = difficulty;
            GamesToWin = Math.Max(1, gamesToWin);
            Singles = singles;
            _rng = new Random(seed);
            Draw[0] = You;
            var pool = new int[TennisLevel.OpponentCount(singles)];
            for (int i = 0; i < pool.Length; i++) pool[i] = i;
            for (int i = pool.Length - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            for (int i = 1; i < Teams; i++) Draw[i] = pool[i - 1];
            for (int i = 0; i < 4; i++) Results[0][i] = new Result { A = Draw[2 * i], B = Draw[2 * i + 1] };
        }

        /// <summary>The opponent the player meets in the current round (a computer opponent's number), or -1 when it's
        /// over.</summary>
        public int Opponent => Over ? -1 : Results[Round][0].B;

        /// <summary>The opponent of the current round, at the round's strength.</summary>
        public TennisLevel Level => TennisLevel.For(Difficulty, Round, Math.Max(0, Opponent), Singles);

        /// <summary>A computer opponent's name in this tournament ("Ana", or "Ana &amp; Beni" in doubles).</summary>
        public string NameOf(int opponent) => TennisLevel.OpponentName(opponent, Singles);

        /// <summary>
        /// The player's match of this round is over: it's recorded, the round's other matches are played (by chance), and
        /// the player moves on to the next round, or is out (then the rest of the tournament is played out).
        /// </summary>
        public void Record(bool won, int gamesFor, int gamesAgainst)
        {
            if (Over) return;
            ref var mine = ref Results[Round][0];
            mine.Played = true;
            mine.Winner = won ? You : mine.B;
            mine.GamesA = gamesFor;
            mine.GamesB = gamesAgainst;
            PlayOthers(Round);
            if (won) Round++;
            else
            {
                Out = true;
                for (int r = Round + 1; r < Rounds; r++) PlayOthers(r, all: true);
            }
        }

        /// <summary>Plays round <paramref name="round"/>'s computer matches (all of them with <paramref name="all"/>: the
        /// player is out), then sets the next round's pairings.</summary>
        void PlayOthers(int round, bool all = false)
        {
            var matches = Results[round];
            for (int i = 0; i < matches.Length; i++)
            {
                if (round > 0) { matches[i].A = Results[round - 1][2 * i].Winner; matches[i].B = Results[round - 1][2 * i + 1].Winner; }
                if (matches[i].Played || (!all && i == 0)) continue;
                bool aWins = _rng.NextDouble() < 0.5;
                int loser = _rng.Next(GamesToWin);
                matches[i].Winner = aWins ? matches[i].A : matches[i].B;
                matches[i].GamesA = aWins ? GamesToWin : loser;
                matches[i].GamesB = aWins ? loser : GamesToWin;
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
