using System.Linq;
using NUnit.Framework;

namespace MoveIt.Games.Kart.Tests
{
    /// <summary>The cup: three races on the three tracks, each stronger, points for every place, the standings with ties
    /// broken by the last race.</summary>
    public class KartCupTests
    {
        [Test]
        public void ThreeRaces_OnTheThreeTracks_EachStronger()
        {
            var cup = new KartCup(KartDifficulty.Medium, KartMode.Solo, 4);
            Assert.AreEqual(6, cup.Racers);
            Assert.AreEqual(5, cup.Drivers.Distinct().Count(), "five different drivers");
            float strength = -1f;
            for (int r = 0; r < KartCup.Races; r++)
            {
                Assert.AreEqual(r, cup.Race);
                Assert.AreEqual(r, cup.Track);
                Assert.Greater(cup.Level.Strength, strength);
                strength = cup.Level.Strength;
                cup.Record(new[] { 1, 2, 3, 4, 5, 6 });
            }
            Assert.IsTrue(cup.Over);
            Assert.IsTrue(cup.Won);
            Assert.AreEqual(30, cup.Points[0]);
            Assert.AreEqual(3, cup.Points[5]);
        }

        [Test]
        public void TheStandings_ByPoints_ATieGoesToTheLastRace()
        {
            var cup = new KartCup(KartDifficulty.Easy, KartMode.Versus, 9);
            Assert.AreEqual(2, cup.HumanKarts);
            Assert.AreEqual(4, cup.Drivers.Length);
            cup.Record(new[] { 1, 2, 3, 4, 5, 6 });   // 10 8 6 4 2 1
            cup.Record(new[] { 2, 1, 4, 3, 6, 5 });   // 18 18 10 10 3 3
            Assert.AreEqual(18, cup.Points[0]);
            Assert.AreEqual(18, cup.Points[1]);
            var s = cup.Standings();
            Assert.AreEqual(1, s[0], "level on points: the better place in the last race");
            Assert.AreEqual(0, s[1]);
            Assert.AreEqual(1, cup.StandingOf(1));
            cup.Record(new[] { 6, 5, 1, 2, 3, 4 });   // 19 20 20 18 9 7
            Assert.IsTrue(cup.Over);
            Assert.AreEqual(2, cup.Standings()[0], "the computer's driver won it");
            Assert.IsFalse(cup.Won);
            Assert.IsNotNull(cup.NameOf(2));
            Assert.IsNull(cup.NameOf(0));
        }
    }
}
