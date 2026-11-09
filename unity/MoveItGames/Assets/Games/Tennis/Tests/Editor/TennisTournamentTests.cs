using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MoveIt.Games.Tennis.Tests
{
    /// <summary>
    /// The tournament (2026-11-07): eight teams drawn at random, three rounds; the player's opponents come out of the
    /// bracket and each round is stronger; losing once ends it, and the rest is played out so the bracket has a champion.
    /// The difficulties' strengths rise with the round and from Easy to Hard.
    /// </summary>
    public class TennisTournamentTests
    {
        /// <summary>Singles (one player, 2026-11-07): seven of the fourteen players, each once, different ones from draw to
        /// draw; doubles (two players together): the seven pairs.</summary>
        [Test]
        public void TheDraw_ThePlayerAndSevenOpponents()
        {
            var met = new HashSet<int>();
            for (int seed = 0; seed < 20; seed++)
            {
                var t = new TennisTournament(TennisDifficulty.Medium, seed);
                Assert.IsTrue(t.Singles);
                Assert.AreEqual(TennisTournament.You, t.Draw[0], "the player heads the bracket");
                var others = t.Draw.Skip(1).ToArray();
                Assert.AreEqual(7, others.Distinct().Count(), "seven opponents, each once");
                Assert.IsTrue(others.All(o => o >= 0 && o < TennisLevel.Names.Length), "of the fourteen players");
                met.UnionWith(others);
                Assert.AreEqual(t.Draw[1], t.Opponent, "the quarter-final against the one drawn next to the player");
                Assert.IsTrue(t.Level.Singles);
                Assert.AreEqual(TennisLevel.Names[t.Opponent], t.Level.TeamName, "a single player's name");
                Assert.AreEqual(0, t.Round);
                Assert.IsFalse(t.Over);

                var two = new TennisTournament(TennisDifficulty.Medium, seed, singles: false);
                CollectionAssert.AreEquivalent(Enumerable.Range(0, TennisLevel.Pairs), two.Draw.Skip(1), "doubles: the seven pairs, each once");
                Assert.IsFalse(two.Level.Singles);
            }
            Assert.AreEqual(TennisLevel.Names.Length, met.Count, "every player turns up in some draw");
            Assert.AreNotEqual(new TennisTournament(TennisDifficulty.Easy, 1).Draw, new TennisTournament(TennisDifficulty.Easy, 2).Draw);
        }

        [Test]
        public void WinningEveryMatch_WinsTheTournament_AgainstStrongerPairs()
        {
            var t = new TennisTournament(TennisDifficulty.Hard, 7);
            float strength = -1f;
            for (int round = 0; round < TennisTournament.Rounds; round++)
            {
                Assert.AreEqual(round, t.Round);
                var level = t.Level;
                Assert.AreEqual(t.Opponent, level.Opponent, "the level is the opponent's");
                Assert.AreEqual(round, level.Round);
                Assert.Greater(level.Strength, strength, "each round is stronger");
                strength = level.Strength;
                if (round > 0)
                {
                    // the opponent won its part of the bracket
                    var before = t.Results[round - 1];
                    Assert.IsTrue(before.All(r => r.Played), $"round {round - 1} was played out");
                    Assert.AreEqual(before[1].Winner, t.Opponent);
                }
                t.Record(true, 2, round % 2);
            }
            Assert.IsTrue(t.Won && t.Over && !t.Out);
            Assert.AreEqual(TennisTournament.You, t.Champion);
            Assert.AreEqual(-1, t.Opponent);
            Assert.AreEqual(2, t.Results[2][0].GamesA);
        }

        [Test]
        public void LosingTheSemiFinal_IsOut_TheRestIsPlayedOut()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var t = new TennisTournament(TennisDifficulty.Easy, seed);
                t.Record(true, 2, 1);
                int semiOpponent = t.Opponent;
                t.Record(false, 1, 2);
                Assert.IsTrue(t.Out && t.Over && !t.Won);
                Assert.AreEqual(1, t.Round, "out in the semi-final");
                Assert.AreEqual(semiOpponent, t.Results[1][0].Winner, "the pair that beat the player went through");
                Assert.IsTrue(t.Champion.HasValue, "the final was played out");
                Assert.AreNotEqual(TennisTournament.You, t.Champion.Value);
                var final = t.Results[2][0];
                Assert.AreEqual(t.Results[1][0].Winner, final.A);
                Assert.AreEqual(t.Results[1][1].Winner, final.B);
                Assert.IsTrue(final.Winner == final.A || final.Winner == final.B);
                foreach (var round in t.Results)
                foreach (var r in round)
                {
                    Assert.IsTrue(r.Played);
                    Assert.AreEqual(t.GamesToWin, r.Winner == r.A ? r.GamesA : r.GamesB, "the winner won the match's games");
                    Assert.Less(r.Winner == r.A ? r.GamesB : r.GamesA, t.GamesToWin);
                }
            }
        }

        [Test]
        public void TheDifficulties_RiseWithTheRound_AndFromEasyToHard()
        {
            float last = -1f;
            foreach (var d in new[] { TennisDifficulty.Easy, TennisDifficulty.Medium, TennisDifficulty.Hard })
                for (int round = 0; round < TennisTournament.Rounds; round++)
                {
                    float k = TennisLevel.StrengthFor(d, round);
                    Assert.Greater(k, last, $"{d} round {round}");
                    last = k;
                }
            var easy = TennisLevel.For(TennisDifficulty.Easy, 1, 0);
            var hard = TennisLevel.For(TennisDifficulty.Hard, 1, 0);
            Assert.Greater(easy.HumanRunSpeed, hard.HumanRunSpeed, "the player's toys are quicker on Easy");
            Assert.Less(easy.HumanReaction, hard.HumanReaction, "and react sooner");
            Assert.Greater(easy.WideFrom, 1f, "on Easy a mistimed shot never goes wide");
            Assert.Less(hard.WideFrom, 1f);
            Assert.AreEqual("Sara", TennisLevel.OpponentName(10, singles: true));
            Assert.AreEqual("Sara & Arbi", TennisLevel.OpponentName(5, singles: false));
        }
    }
}
