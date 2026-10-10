using NUnit.Framework;

namespace MoveIt.Games.Bowling.Tests
{
    public class BowlingScoreTests
    {
        static BowlingScore Play(int frames, params int[] rolls)
        {
            var score = new BowlingScore(frames);
            foreach (int r in rolls) score.Roll(r);
            return score;
        }

        [Test]
        public void PerfectGame_Is300()
        {
            var s = Play(10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10);
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(300, s.Total);
            Assert.AreEqual("X X X", s.Marks(9));
        }

        [Test]
        public void AllNineMiss_Is90()
        {
            var s = Play(10, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0);
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(90, s.Total);
            Assert.AreEqual("9 -", s.Marks(0));
        }

        [Test]
        public void AllFiveSpares_Is150()
        {
            var s = Play(10, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5);
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(150, s.Total);
            Assert.AreEqual("5 /", s.Marks(0));
        }

        [Test]
        public void KnownGame_ScoresCorrectly()
        {
            // X, 7/, 9-, X, -8, 8/, -6, X, X, X81  = 167
            var s = Play(10, 10, 7, 3, 9, 0, 10, 0, 8, 8, 2, 0, 6, 10, 10, 10, 8, 1);
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(167, s.Total);
            Assert.AreEqual(new int?[] { 20, 39, 48, 66, 74, 84, 90, 120, 148, 167 }, s.CumulativeScores());
        }

        [Test]
        public void StrikeScore_WaitsForTwoBonusRolls()
        {
            var s = Play(10, 10, 3);
            Assert.IsNull(s.CumulativeScores()[0]);
            s.Roll(4);
            Assert.AreEqual(17, s.CumulativeScores()[0]);
            Assert.AreEqual(24, s.CumulativeScores()[1]);
        }

        [Test]
        public void Roll_ReportsWhenTheFrameIsOver()
        {
            var s = new BowlingScore(10);
            Assert.IsFalse(s.Roll(3), "first ball of an open frame");
            Assert.IsTrue(s.Roll(4), "second ball ends the frame");
            Assert.IsTrue(s.Roll(10), "a strike ends the frame");
            Assert.AreEqual(2, s.CurrentFrame);
        }

        [Test]
        public void PinsStanding_AndRacks_FollowTheRules()
        {
            var s = new BowlingScore(10);
            Assert.IsTrue(s.NeedsFullRack);
            s.Roll(6);
            Assert.IsFalse(s.NeedsFullRack);
            Assert.AreEqual(4, s.PinsStanding);
            s.Roll(9);  // more than standing: clamped to 4, a spare
            Assert.AreEqual("6 /", s.Marks(0));
            Assert.IsNull(s.CumulativeScores()[0], "a spare waits for the next ball");
            Assert.IsTrue(s.NeedsFullRack);
        }

        [Test]
        public void LastFrame_StrikeGivesTwoBonusBallsWithRacks()
        {
            var s = new BowlingScore(1);
            s.Roll(10);
            Assert.IsFalse(s.IsComplete);
            Assert.IsTrue(s.NeedsFullRack, "new rack after a strike in the last frame");
            s.Roll(7);
            Assert.IsFalse(s.NeedsFullRack);
            Assert.AreEqual(3, s.PinsStanding);
            s.Roll(3);
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(20, s.Total);
            Assert.AreEqual("X 7 /", s.Marks(0));
        }

        [Test]
        public void LastFrame_SpareGivesOneBonusBall()
        {
            var s = new BowlingScore(1);
            s.Roll(4);
            s.Roll(6);
            Assert.IsFalse(s.IsComplete);
            Assert.IsTrue(s.NeedsFullRack);
            s.Roll(5);
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(15, s.Total);
            Assert.AreEqual("4 / 5", s.Marks(0));
        }

        [Test]
        public void QuickFiveFrameGame_PerfectIs150()
        {
            var s = Play(5, 10, 10, 10, 10, 10, 10, 10);
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(150, s.Total);
        }
    }
}
