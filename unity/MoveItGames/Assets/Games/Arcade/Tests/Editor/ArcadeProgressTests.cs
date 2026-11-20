using System.Linq;
using NUnit.Framework;

namespace MoveIt.Games.Arcade.Tests
{
    public class ArcadeProgressTests
    {
        [Test]
        public void Rounds_EarnTickets_TicketsWinThePlushies_InOrder()
        {
            var p = new ArcadeProgress();
            var r = p.Record(Machine.Whack, 0);
            Assert.AreEqual(0, r.Stars);
            Assert.AreEqual(2, r.Tickets, "two tickets just for playing");
            Assert.IsFalse(r.NewBest, "nothing isn't a record");
            r = p.Record(Machine.Whack, ArcadeProgress.StarsAt(Machine.Whack)[2]);
            Assert.AreEqual(3, r.Stars);
            Assert.AreEqual(11, r.Tickets);
            Assert.IsTrue(r.NewBest);
            CollectionAssert.AreEqual(new[] { 0 }, r.Prizes, "13 tickets: the first plush");
            for (int i = 0; i < 40; i++) p.Record(Machine.Catch, ArcadeProgress.StarsAt(Machine.Catch)[2]);
            Assert.AreEqual(ArcadeProgress.Prizes, p.PrizesWon, "every plush in the end");
            Assert.AreEqual(0, p.ToNextPrize);
        }

        [Test]
        public void TwoPlayers_ShareTickets_NotBests()
        {
            var p = new ArcadeProgress();
            var r = p.Record(Machine.Slice, 500, solo: false);
            Assert.IsFalse(r.NewBest);
            Assert.AreEqual(-1, p.Best[(int)Machine.Slice]);
            Assert.Greater(p.Tickets, 0);
        }

        [Test]
        public void SavesAndLoads()
        {
            var p = new ArcadeProgress { Tickets = 77, PartyBest = 1234 };
            p.Best[2] = 900;
            p.Best[5] = 14;
            var q = ArcadeProgress.Load(p.Save());
            Assert.AreEqual(77, q.Tickets);
            Assert.AreEqual(1234, q.PartyBest);
            CollectionAssert.AreEqual(p.Best, q.Best);
            Assert.AreEqual(0, ArcadeProgress.Load("garbage").Tickets);
            Assert.AreEqual(0, ArcadeProgress.Load(null).Tickets);
        }

        [Test]
        public void AParty_IsFiveDifferentBooths_TheWinnerHasMoreRounds_ThenMorePoints()
        {
            var party = new ArcadeParty(2, 42);
            Assert.AreEqual(5, party.Order.Distinct().Count());
            Assert.AreEqual(1, party.Record(new[] { 100, 200 }));
            Assert.AreEqual(0, party.Record(new[] { 300, 50 }));
            Assert.AreEqual(-1, party.Record(new[] { 80, 80 }));
            Assert.AreEqual(0, party.Record(new[] { 90, 10 }));
            Assert.IsFalse(party.Over);
            Assert.AreEqual(1, party.Record(new[] { 0, 500 }));
            Assert.IsTrue(party.Over);
            Assert.AreEqual(2, party.Wins[0]);
            Assert.AreEqual(2, party.Wins[1]);
            Assert.AreEqual(1, party.Winner, "two rounds each: more points wins (840 to 570)");
            var solo = new ArcadeParty(1, 1);
            for (int i = 0; i < 5; i++) Assert.AreEqual(-1, solo.Record(new[] { 100 }));
            Assert.AreEqual(500, solo.Total[0]);
            Assert.AreEqual(-1, solo.Winner);
        }
    }
}
