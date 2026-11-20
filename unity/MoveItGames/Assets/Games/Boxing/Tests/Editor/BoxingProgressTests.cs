using NUnit.Framework;

namespace MoveIt.Games.Boxing.Tests
{
    public sealed class BoxingProgressTests
    {
        [Test]
        public void FightsOpenOneByOne_EachDifficultyItsOwnClimb()
        {
            var p = new BoxingProgress();
            Assert.IsTrue(p.Unlocked(BoxingDifficulty.Easy, 0));
            Assert.IsFalse(p.Unlocked(BoxingDifficulty.Easy, 1));
            var r = p.Record(BoxingDifficulty.Easy, 0, true, 2);
            Assert.IsTrue(r.FirstWin);
            Assert.AreEqual(2, r.NewStars);
            Assert.IsTrue(p.Unlocked(BoxingDifficulty.Easy, 1));
            Assert.IsFalse(p.Unlocked(BoxingDifficulty.Medium, 1), "medium is its own climb");
            Assert.AreEqual(1, p.Next(BoxingDifficulty.Easy));
            Assert.AreEqual(1, p.Selected[(int)BoxingDifficulty.Easy], "the map moves on to the next fight");
            // a lost fight changes nothing but the counts; a better win keeps the best stars
            p.Record(BoxingDifficulty.Easy, 1, false, 0);
            Assert.IsFalse(p.Won(BoxingDifficulty.Easy, 1));
            r = p.Record(BoxingDifficulty.Easy, 0, true, 3);
            Assert.IsFalse(r.FirstWin);
            Assert.AreEqual(1, r.NewStars);
            Assert.AreEqual(3, p.StarsOf(BoxingDifficulty.Easy, 0));
            Assert.AreEqual(3, p.Fights);
            Assert.AreEqual(2, p.Wins);
        }

        [Test]
        public void AChampion_GivesItsBelt_AndItsGloves_TheClimbsTopMakesAChampion()
        {
            var p = new BoxingProgress();
            for (int i = 0; i < Campaign.FightsPerStage - 1; i++) p.Record(BoxingDifficulty.Medium, i, true, 1);
            Assert.IsFalse(p.GlovesUnlocked(1));
            var r = p.Record(BoxingDifficulty.Medium, Campaign.FightsPerStage - 1, true, 1);
            Assert.IsTrue(r.Belt);
            Assert.AreEqual(1, r.NewGloves, "the gym champion's gloves");
            Assert.IsTrue(p.Belt(BoxingDifficulty.Medium, 0));
            Assert.IsFalse(p.Belt(BoxingDifficulty.Easy, 0));
            Assert.IsTrue(p.GlovesUnlocked(1));
            // the same belt at another difficulty: a belt, no new gloves
            for (int i = 0; i < Campaign.FightsPerStage - 1; i++) p.Record(BoxingDifficulty.Easy, i, true, 1);
            r = p.Record(BoxingDifficulty.Easy, Campaign.FightsPerStage - 1, true, 1);
            Assert.IsTrue(r.Belt);
            Assert.AreEqual(-1, r.NewGloves);
            // the whole climb
            for (int i = 0; i < Campaign.Fights - 1; i++) p.Record(BoxingDifficulty.Hard, i, true, 3);
            r = p.Record(BoxingDifficulty.Hard, Campaign.Fights - 1, true, 3);
            Assert.IsTrue(r.Champion);
            Assert.IsTrue(p.ClimbDone(BoxingDifficulty.Hard));
            for (int g = 0; g < BoxingProgress.GloveCount; g++) Assert.IsTrue(p.GlovesUnlocked(g), $"gloves {g}");
        }

        [Test]
        public void StarsOpenOutfits()
        {
            var p = new BoxingProgress();
            Assert.AreEqual(1, p.OutfitsUnlocked);
            FightReward last = null;
            for (int i = 0; i < 2; i++) last = p.Record(BoxingDifficulty.Easy, i, true, 3);
            Assert.AreEqual(6, p.TotalStars);
            Assert.AreEqual(2, p.OutfitsUnlocked);
            CollectionAssert.AreEqual(new[] { 1 }, last.NewOutfits);
        }

        [Test]
        public void SavedAndLoaded_TheSame()
        {
            var p = new BoxingProgress { Outfit = 1, Gloves = 1, Difficulty = 2 };
            for (int i = 0; i < 5; i++) p.Record(BoxingDifficulty.Medium, i, true, 1 + i % 3);
            p.Record(BoxingDifficulty.Hard, 0, true, 2);
            p.Record(BoxingDifficulty.Hard, 1, false, 0);
            var q = BoxingProgress.Load(p.Save());
            Assert.AreEqual(p.Save(), q.Save());
            Assert.AreEqual(p.TotalStars, q.TotalStars);
            Assert.AreEqual(1, q.Gloves, "the gym champion's gloves were won");
            Assert.AreEqual(2, q.Difficulty);
            Assert.AreEqual(p.Selected[(int)BoxingDifficulty.Medium], q.Selected[(int)BoxingDifficulty.Medium]);
            Assert.AreEqual(0, BoxingProgress.Load("garbage").TotalStars);
            Assert.AreEqual(0, BoxingProgress.Load("").TotalStars);
            // what isn't won any more (an edited save) falls back
            var r = BoxingProgress.Load("b2;" + new string('0', Campaign.Fights) + ";;;5;3;1");
            Assert.AreEqual(0, r.Outfit);
            Assert.AreEqual(0, r.Gloves);
        }
    }
}
