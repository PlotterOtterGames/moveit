using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MoveIt.Games.Volley.Tests
{
    /// <summary>The tournament (as Tenis!'s): eight pairs drawn at random, three rounds, each stronger; losing once ends
    /// it and the rest is played out so the bracket has a champion.</summary>
    public class VolleyTournamentTests
    {
        [Test]
        public void TheDraw_ThePlayersTeamAndTheSevenPairs()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var t = new VolleyTournament(VolleyDifficulty.Medium, seed);
                Assert.AreEqual(VolleyTournament.You, t.Draw[0]);
                CollectionAssert.AreEquivalent(Enumerable.Range(0, VolleyLevel.Pairs), t.Draw.Skip(1), "the seven pairs, each once");
                Assert.AreEqual(t.Draw[1], t.Opponent);
                Assert.AreEqual(VolleyLevel.OpponentName(t.Opponent), t.Level.TeamName);
                Assert.AreEqual(0, t.Round);
            }
            Assert.AreNotEqual(new VolleyTournament(VolleyDifficulty.Easy, 1).Draw, new VolleyTournament(VolleyDifficulty.Easy, 2).Draw);
        }

        [Test]
        public void WinningEveryMatch_WinsIt_AgainstStrongerPairs()
        {
            var t = new VolleyTournament(VolleyDifficulty.Hard, 7);
            float strength = -1f;
            for (int round = 0; round < VolleyTournament.Rounds; round++)
            {
                Assert.AreEqual(round, t.Round);
                Assert.Greater(t.Level.Strength, strength, "each round stronger");
                strength = t.Level.Strength;
                if (round > 0) Assert.IsTrue(t.Results[round - 1].All(r => r.Played));
                t.Record(true, 7, 4);
            }
            Assert.IsTrue(t.Won);
            Assert.IsTrue(t.Over);
            Assert.AreEqual(VolleyTournament.You, t.Champion);
            Assert.AreEqual(-1, t.Opponent);
        }

        [Test]
        public void LosingOnce_EndsIt_TheRestIsPlayedOut()
        {
            var t = new VolleyTournament(VolleyDifficulty.Easy, 3);
            t.Record(true, 7, 5);
            int semiOpponent = t.Opponent;
            t.Record(false, 4, 7);
            Assert.IsTrue(t.Out);
            Assert.IsFalse(t.Won);
            Assert.AreEqual(1, t.Round, "out in the semi-final");
            Assert.IsTrue(t.Champion.HasValue);
            Assert.AreNotEqual(VolleyTournament.You, t.Champion.Value);
            Assert.AreEqual(semiOpponent, t.Results[1][0].Winner, "the pair that beat the player went on");
            foreach (var round in t.Results)
            foreach (var r in round)
            {
                Assert.IsTrue(r.Played);
                Assert.AreEqual(7, System.Math.Max(r.PointsA, r.PointsB), "won with the points to win");
            }
        }

        [Test]
        public void TheDifficulties_RiseWithTheRound_AndFromEasyToHard()
        {
            var all = new List<float>();
            foreach (var d in new[] { VolleyDifficulty.Easy, VolleyDifficulty.Medium, VolleyDifficulty.Hard })
                for (int r = 0; r < VolleyTournament.Rounds; r++)
                {
                    float s = VolleyLevel.StrengthFor(d, r);
                    if (all.Count > 0) Assert.Greater(s, all[all.Count - 1]);
                    all.Add(s);
                }
            var easy = VolleyLevel.For(VolleyDifficulty.Easy, 0, 0);
            var hard = VolleyLevel.For(VolleyDifficulty.Hard, 2, 0);
            Assert.Greater(hard.Dig, easy.Dig);
            Assert.Greater(hard.AttackSpeed, easy.AttackSpeed);
            Assert.Less(hard.HumanRunSpeed, easy.HumanRunSpeed);
            Assert.Less(hard.Error, easy.Error);
            Assert.Greater(easy.WindowScale, hard.WindowScale, "Easy forgives more");
        }
    }
}
