using NUnit.Framework;

namespace MoveIt.Games.Tennis.Tests
{
    public class TennisScoreTests
    {
        [Test]
        public void FourStraightPoints_WinTheGame_AndTheServeChangesTeams()
        {
            var s = new TennisScore(2, firstServer: 0);
            Assert.AreEqual(ScoreCall.Points, s.PointTo(0));
            Assert.AreEqual("15", s.PointsText(0));
            Assert.AreEqual("0", s.PointsText(1));
            s.PointTo(0);
            s.PointTo(0);
            Assert.AreEqual("40", s.PointsText(0));
            Assert.IsTrue(s.GamePoint(0));
            Assert.AreEqual(ScoreCall.Game, s.PointTo(0));
            Assert.AreEqual(1, s.Games(0));
            Assert.AreEqual(0, s.Points(0));
            Assert.AreEqual(1, s.Server, "the other team serves the next game");
        }

        [Test]
        public void Deuce_Advantage_BackToDeuce_ThenTwoAhead()
        {
            var s = new TennisScore();
            for (int i = 0; i < 2; i++) { s.PointTo(0); s.PointTo(1); }
            s.PointTo(0);
            Assert.AreEqual(ScoreCall.Deuce, s.PointTo(1), "40 - 40");
            Assert.IsTrue(s.IsDeuce);
            Assert.AreEqual(ScoreCall.Advantage, s.PointTo(1));
            Assert.AreEqual(1, s.Advantage);
            Assert.AreEqual("AD", s.PointsText(1));
            Assert.AreEqual("40", s.PointsText(0));
            Assert.AreEqual(ScoreCall.Deuce, s.PointTo(0), "back to deuce");
            Assert.AreEqual(ScoreCall.Advantage, s.PointTo(0));
            Assert.AreEqual(ScoreCall.Game, s.PointTo(0), "two ahead wins the game");
            Assert.AreEqual(1, s.Games(0));
        }

        [Test]
        public void BestOfThree_FirstToTwoGames_WinsTheMatch()
        {
            var s = new TennisScore(2);
            for (int i = 0; i < 4; i++) s.PointTo(1);
            Assert.AreEqual(1, s.Games(1));
            for (int i = 0; i < 4; i++) s.PointTo(0);
            Assert.AreEqual(1, s.Games(0));
            for (int i = 0; i < 3; i++) s.PointTo(0);
            Assert.IsTrue(s.MatchPoint(0));
            Assert.IsFalse(s.MatchPoint(1));
            Assert.AreEqual(ScoreCall.Match, s.PointTo(0));
            Assert.IsTrue(s.MatchOver);
            Assert.AreEqual(0, s.Winner);
            Assert.AreEqual(ScoreCall.Match, s.PointTo(1), "nothing changes after the match");
            Assert.AreEqual(2, s.Games(0));
        }

        [Test]
        public void TheServeChangesSidesEveryPoint_DeuceCourtFirst()
        {
            var s = new TennisScore();
            Assert.IsTrue(s.DeuceCourt);
            s.PointTo(0);
            Assert.IsFalse(s.DeuceCourt);
            s.PointTo(1);
            Assert.IsTrue(s.DeuceCourt);
            s.PointTo(0);
            s.PointTo(0);
            s.PointTo(0);   // game: the next one starts in the deuce court again
            Assert.IsTrue(s.DeuceCourt);
        }
    }
}
