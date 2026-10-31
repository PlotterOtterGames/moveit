using System.Linq;
using NUnit.Framework;

namespace MoveIt.Games.Fly.Tests
{
    /// <summary>
    /// <see cref="FlyProgress"/>: what carries over between Fluturo! runs. Three missions of different kinds; one done in a
    /// run pays its stars at once and is replaced by a harder one when the run ends; stars from every run add up and
    /// unlock outfits in order (the newest is worn at once); the bests; saving and loading (a damaged save starts afresh).
    /// </summary>
    public sealed class FlyProgressTests
    {
        static FlyProgress WithMissions(params Mission[] missions)
        {
            var p = new FlyProgress();
            p.Missions.Clear();
            p.Missions.AddRange(missions);
            return p;
        }

        [Test]
        public void ThereAreThreeMissions_OfDifferentKinds()
        {
            var p = new FlyProgress();
            Assert.AreEqual(FlyProgress.MissionSlots, p.Missions.Count);
            Assert.AreEqual(p.Missions.Count, p.Missions.Select(m => m.Kind).Distinct().Count());
            Assert.IsFalse(p.Missions.Any(m => m.Kind is MissionKind.BeatBoss or MissionKind.ReturnSparks),
                           "nothing a first run can't reach");
        }

        [Test]
        public void AMissionDoneInARun_PaysAtOnce_AndAHarderOneComesAfterTheRun()
        {
            var p = WithMissions(FlyProgress.Make(MissionKind.SmashRobots, 0), FlyProgress.Make(MissionKind.Combo, 0),
                                 FlyProgress.Make(MissionKind.CollectStars, 0));
            p.BeginRun();
            Assert.IsEmpty(p.Update(new RunStats { RobotsSmashed = 14 }));
            var done = p.Update(new RunStats { RobotsSmashed = 15 });
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual(MissionKind.SmashRobots, done[0].Kind);
            Assert.AreEqual(done[0].Reward, p.Stars, "paid at once");
            Assert.IsEmpty(p.Update(new RunStats { RobotsSmashed = 30 }), "only once");

            var result = p.EndRun(new RunStats { RobotsSmashed = 30, StarsCollected = 12, District = 2, Score = 400 });
            Assert.AreEqual(12, result.StarsFromRun);
            Assert.AreEqual(done[0].Reward, result.StarsFromMissions);
            Assert.AreEqual(12 + done[0].Reward, p.Stars);
            CollectionAssert.AreEqual(new[] { MissionKind.SmashRobots }, result.Completed.Select(m => m.Kind));
            Assert.AreEqual(3, p.Missions.Count, "replaced");
            Assert.IsFalse(p.Missions.Any(m => m.Done || m.Progress != 0), "the next run starts from zero");
            Assert.AreEqual(1, p.MissionsDone);
        }

        [Test]
        public void MissionsAreCountedInOneRun()
        {
            var p = WithMissions(FlyProgress.Make(MissionKind.SmashRobots, 0), FlyProgress.Make(MissionKind.Combo, 0),
                                 FlyProgress.Make(MissionKind.CollectStars, 0));
            p.EndRun(new RunStats { RobotsSmashed = 10 });
            p.EndRun(new RunStats { RobotsSmashed = 10 });
            Assert.AreEqual(0, p.MissionsDone, "10 and 10 in two runs isn't 15 in one");
        }

        [Test]
        public void AQuickClash_IsDoneByWinningOneInTime()
        {
            var m = FlyProgress.Make(MissionKind.QuickClash, 0);
            var p = WithMissions(m, FlyProgress.Make(MissionKind.Combo, 0), FlyProgress.Make(MissionKind.CollectStars, 0));
            p.BeginRun();
            Assert.IsEmpty(p.Update(new RunStats { QuickestClash = 999f }), "no clash won yet");
            Assert.IsEmpty(p.Update(new RunStats { QuickestClash = m.Target + 0.5f }), "too slow");
            Assert.AreEqual(1, p.Update(new RunStats { QuickestClash = m.Target - 0.5f }).Count);
        }

        [Test]
        public void HarderTiers_AskMore_AndPayMore()
        {
            foreach (MissionKind kind in System.Enum.GetValues(typeof(MissionKind)))
            {
                Mission easy = FlyProgress.Make(kind, 0), hard = FlyProgress.Make(kind, 6);
                if (kind == MissionKind.QuickClash) Assert.LessOrEqual(hard.Target, easy.Target, kind.ToString());
                else Assert.GreaterOrEqual(hard.Target, easy.Target, kind.ToString());
                Assert.Greater(hard.Reward, easy.Reward, kind.ToString());
            }
        }

        [Test]
        public void Stars_UnlockOutfitsInOrder_TheNewestIsWornAtOnce()
        {
            var p = new FlyProgress();
            Assert.AreEqual(1, p.Unlocked);
            Assert.AreEqual(FlyProgress.OutfitStars[1], p.NextOutfitStars);
            var result = p.EndRun(new RunStats { StarsCollected = FlyProgress.OutfitStars[2] });
            CollectionAssert.AreEqual(new[] { 1, 2 }, result.NewOutfits, "two outfits at once");
            Assert.AreEqual(2, p.Outfit, "the newest put on");
            Assert.AreEqual(3, p.Unlocked);
            p.CycleOutfit(1);
            Assert.AreEqual(0, p.Outfit, "round to the first");
            p.CycleOutfit(-1);
            Assert.AreEqual(2, p.Outfit);
            p.Stars = 100000;
            Assert.AreEqual(FlyProgress.OutfitStars.Length, p.Unlocked);
            Assert.AreEqual(-1, p.NextOutfitStars, "all unlocked");
        }

        [Test]
        public void TheBests_AreKept()
        {
            var p = new FlyProgress();
            var r = p.EndRun(new RunStats { Score = 500, District = 3 });
            Assert.IsTrue(r.NewBestScore && r.NewBestDistrict);
            r = p.EndRun(new RunStats { Score = 300, District = 4 });
            Assert.IsFalse(r.NewBestScore);
            Assert.IsTrue(r.NewBestDistrict);
            Assert.AreEqual(500, p.BestScore);
            Assert.AreEqual(4, p.BestDistrict);
        }

        [Test]
        public void SavedAndLoaded_ItIsTheSame_ADamagedSaveStartsAfresh()
        {
            var p = new FlyProgress();
            p.EndRun(new RunStats { StarsCollected = 120, Score = 900, District = 5, RobotsSmashed = 100 });
            var q = FlyProgress.Load(p.Save());
            Assert.AreEqual(p.Stars, q.Stars);
            Assert.AreEqual(p.MissionsDone, q.MissionsDone);
            Assert.AreEqual(p.BestScore, q.BestScore);
            Assert.AreEqual(p.BestDistrict, q.BestDistrict);
            Assert.AreEqual(p.Outfit, q.Outfit);
            CollectionAssert.AreEqual(p.Missions.Select(m => (m.Kind, m.Target, m.Reward)), q.Missions.Select(m => (m.Kind, m.Target, m.Reward)));

            foreach (var bad in new[] { null, "", "garbage", "1|x|y|z|w|v", "9|1|2|3|4|5", "1|5|0|0|0|99|77:1:1" })
            {
                var fresh = FlyProgress.Load(bad);
                Assert.AreEqual(3, fresh.Missions.Count, bad ?? "null");
                Assert.Less(fresh.Outfit, fresh.Unlocked, bad ?? "null");
            }
        }
    }
}
