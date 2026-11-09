using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Kart.Tests
{
    /// <summary>
    /// Gara!'s race rules (2026-11-08): the karts go round and the race ends with every place given; smart steering keeps a
    /// hands-off kart on the road on Easy, on Hard the wheel has to take the corners; a jump at a ramp is a trick and a
    /// boost (seen late by the camera too), elsewhere a hop, at "go" a quick start; holding the wheel hard drifts and
    /// letting go boosts; gift boxes give items by place; paint flies to the kart ahead, a shield stops it; honey slows;
    /// stars speed up; together one kart steers as both players; and the balance of simulated players by difficulty.
    /// </summary>
    public class KartRaceTests
    {
        static KartRace NewRace(KartDifficulty d = KartDifficulty.Medium, KartMode mode = KartMode.Solo, int track = 0, int seed = 1, int laps = 3)
        {
            var drivers = KartCup.DrawDrivers(mode == KartMode.Versus ? 4 : 5, seed);
            return new KartRace(KartTracks.Get(track), KartLevel.For(d, 0), mode, laps, drivers, seed);
        }

        static void Run(KartRace race, float seconds, Action<KartRace> every = null)
        {
            for (float t = 0f; t < seconds && race.Phase != KartPhase.Over; t += 1f / 60f)
            {
                every?.Invoke(race);
                race.Step(1f / 60f);
            }
        }

        [Test]
        public void TheKartsGoRound_AndTheRaceEnds_EveryPlaceGiven()
        {
            var race = NewRace(KartDifficulty.Easy);
            var events = new List<KartEvent>();
            race.Events += events.Add;
            Assert.AreEqual(KartPhase.Countdown, race.Phase);
            Assert.AreEqual(6, race.Karts.Length);
            Assert.AreEqual(5, race.Karts[0].Place, "a lone player starts fifth (one computer kart behind)");
            Run(race, 400f, r => r.Steer(0, 0f, true));
            Assert.AreEqual(KartPhase.Over, race.Phase);
            CollectionAssert.AreEquivalent(Enumerable.Range(1, 6), race.Karts.Select(k => k.Place));
            Assert.IsTrue(race.Karts.All(k => k.Finished));
            Assert.AreEqual(new[] { 3, 2, 1, 0 }, events.Where(e => e.Kind == KartEventKind.Countdown).Select(e => e.Value).ToArray());
            Assert.AreEqual(2, events.Count(e => e.Kind == KartEventKind.Lap && e.Kart == 0), "laps 2 and 3");
            Assert.AreEqual(1, events.Count(e => e.Kind == KartEventKind.FinalLap && e.Kart == 0));
            var k0 = race.Karts[0];
            Assert.That(k0.FinishTime, Is.InRange(80.0, 160.0), "three laps of ~35-45 s");
        }

        [Test]
        public void SmartSteering_OnEasy_KeepsAHandsOffKartOnTheRoad()
        {
            for (int track = 0; track < KartTracks.Count; track++)
            {
                var race = NewRace(KartDifficulty.Easy, track: track, laps: 1);
                race.QuietComputer = true;
                float widest = 0f;
                Run(race, 120f, r =>
                {
                    r.Steer(0, 0f, true);
                    widest = Mathf.Max(widest, Mathf.Abs(r.Karts[0].D));
                });
                var k = race.Karts[0];
                Assert.IsTrue(k.Finished, $"track {track}");
                Assert.AreEqual(0, k.WallHits, $"track {track}: a hands-off kart on Easy never meets the barrier");
                Assert.Less(widest, race.Track.HalfWidth, $"track {track}: and stays on the road");
            }
        }

        [Test]
        public void OnHard_TheWheelHasToTakeTheCorners()
        {
            // the hardest race's smart steering helps least
            var drivers = KartCup.DrawDrivers(5, 1);
            var still = new KartRace(KartTracks.Get(0), KartLevel.For(KartDifficulty.Hard, 2), KartMode.Solo, 1, drivers, 1);
            still.QuietComputer = true;
            Run(still, 150f, r => r.Steer(0, 0f, true));
            var steering = new KartRace(KartTracks.Get(0), KartLevel.For(KartDifficulty.Hard, 2), KartMode.Solo, 1, drivers, 1);
            steering.QuietComputer = true;
            var sim = new KartSim(steering, 0, KartSim.Profile.Perfect, 3);
            Run(steering, 150f, r => sim.Step(1f / 60f));
            Assert.Greater(still.Karts[0].WallHits, 3, "a level wheel on Hard runs wide into the barrier");
            Assert.LessOrEqual(steering.Karts[0].WallHits, 1, "steering through the corners doesn't");
            Assert.Less(steering.Karts[0].FinishTime, still.Karts[0].FinishTime - 3.0);
        }

        /// <summary>Drives kart 0 to just before a ramp's lip: the race running, the kart in the middle at speed.</summary>
        static (KartRace race, TrackFeature ramp) BeforeRamp(float metres, KartMode mode = KartMode.Solo)
        {
            var race = NewRace(KartDifficulty.Medium, mode);
            race.QuietComputer = true;
            race.SkipCountdown();
            Run(race, 5f);   // well after "go" (a jump right after it is a quick start)
            var ramp = race.Track.Features.First(f => f.Kind == FeatureKind.Ramp);
            race.Place(race.Karts[0], ramp.S - metres, 0f, 24f);
            // the computer's karts out of the way
            for (int i = 1; i < race.Karts.Length; i++) race.Place(race.Karts[i], ramp.S + 200f + 10f * i, 0f, 20f);
            return (race, ramp);
        }

        [Test]
        public void AJumpAtTheRamp_IsATrick_AndLandsWithABoost()
        {
            var (race, _) = BeforeRamp(9f);
            var events = new List<KartEvent>();
            race.Events += events.Add;
            var k = race.Karts[0];
            // the jump ~0.13 s before the lip (the kart reaches it in ~0.38 s)
            race.Step(0.25f);
            race.Hop(0, race.Time);
            Run(race, 0.4f, r => r.Steer(0, 0f, true));
            Assert.IsTrue(k.Airborne, "off the lip");
            Assert.AreEqual(1, k.Trick, "the jump before the lip is a trick");
            Run(race, 1.5f, r => r.Steer(0, 0f, true));
            Assert.IsFalse(k.Airborne);
            var land = events.First(e => e.Kind == KartEventKind.Land && e.Kart == 0);
            Assert.AreEqual(1, land.Value);
            Assert.IsTrue(events.Any(e => e.Kind == KartEventKind.Trick && e.Kart == 0 && e.Perfect), "0.2 s from the lip: perfect");
            Assert.Greater(k.BoostTime, 0.4f, "a boost on landing");
            Assert.Greater(k.BoostPower, 1.2f);
        }

        [Test]
        public void AJumpSeenLate_StillCounts_WhileTheKartFlies()
        {
            var (race, ramp) = BeforeRamp(6f);
            var k = race.Karts[0];
            Run(race, 0.3f, r => r.Steer(0, 0f, true));
            Assert.IsTrue(k.Airborne);
            double lip = k.TakeOffAt;
            // the player jumped 0.25 s after the lip; the camera's news arrives 0.2 s after that
            Run(race, (float)(lip + 0.45 - race.Time), r => r.Steer(0, 0f, true));
            race.Hop(0, lip + 0.25);
            Assert.AreEqual(1, k.Trick, "late, but the kart is still in the air");
            Assert.IsFalse(k.TrickPerfect);
            // too late: more than the window after the lip
            var (late, _) = BeforeRamp(6f);
            Run(late, 0.3f, r => r.Steer(0, 0f, true));
            double lip2 = late.Karts[0].TakeOffAt;
            Run(late, (float)(lip2 + 0.7 - late.Time), r => r.Steer(0, 0f, true));
            late.Hop(0, lip2 + 0.65 * late.Level.WindowScale + 0.05);
            Assert.AreEqual(0, late.Karts[0].Trick);
        }

        [Test]
        public void AJumpFarFromARamp_IsALittleHop_NoBoost()
        {
            var (race, _) = BeforeRamp(80f);
            var events = new List<KartEvent>();
            race.Events += events.Add;
            var k = race.Karts[0];
            race.Hop(0, race.Time);
            Assert.IsTrue(k.Airborne);
            Run(race, 0.6f, r => r.Steer(0, 0f, true));
            Assert.IsFalse(k.Airborne, "a hop is short");
            Assert.IsTrue(events.Any(e => e.Kind == KartEventKind.Hop));
            Assert.IsFalse(events.Any(e => e.Kind == KartEventKind.Trick));
            Assert.AreEqual(0f, k.BoostTime);
        }

        [Test]
        public void AJumpAtGo_IsAQuickStart_MuchEarlierIsNothing()
        {
            var race = NewRace();
            Run(race, KartRace.CountdownSeconds - 0.2f);
            race.Hop(0, -0.15);   // just before "go"
            Run(race, 0.6f);
            Assert.Greater(race.Karts[0].BoostTime, 0.5f, "a quick start");
            var early = NewRace();
            Run(early, 1.2f);
            early.Hop(0, early.Time);   // two seconds early
            Run(early, KartRace.CountdownSeconds);
            Assert.AreEqual(0f, early.Karts[0].BoostTime);
            // seen after "go" but made within the window
            var late = NewRace();
            Run(late, KartRace.CountdownSeconds + 0.35f);
            late.Hop(0, 0.15);
            Assert.Greater(late.Karts[0].BoostTime, 0.5f);
        }

        [Test]
        public void HoldingTheWheelHard_Drifts_LettingGoBoosts()
        {
            var race = NewRace(KartDifficulty.Easy);
            race.QuietComputer = true;
            race.SkipCountdown();
            var k = race.Karts[0];
            race.Place(k, 300f, 0f, 22f);
            var events = new List<KartEvent>();
            race.Events += events.Add;
            Run(race, 1.6f, r => r.Steer(0, 0.9f, true));
            Assert.IsTrue(k.Drifting || events.Any(e => e.Kind == KartEventKind.DriftEnd), "a hard turn drifts");
            Assert.IsTrue(events.Any(e => e.Kind == KartEventKind.DriftStart && e.Value == 1), "to the right");
            Assert.GreaterOrEqual(events.Where(e => e.Kind == KartEventKind.DriftTier).Select(e => e.Value).DefaultIfEmpty(0).Max(), 1);
            Run(race, 0.4f, r => r.Steer(0, 0f, true));
            Assert.IsFalse(k.Drifting);
            var end = events.Last(e => e.Kind == KartEventKind.DriftEnd);
            Assert.GreaterOrEqual(end.Value, 1);
            Assert.Greater(k.BoostTime, 0.1f, "the sparks' boost");
            // a short hard turn: no drift
            var quick = NewRace(KartDifficulty.Easy);
            quick.QuietComputer = true;
            quick.SkipCountdown();
            quick.Place(quick.Karts[0], 300f, 0f, 22f);
            Run(quick, 0.2f, r => r.Steer(0, 1f, true));
            Run(quick, 0.5f, r => r.Steer(0, 0f, true));
            Assert.IsFalse(quick.Karts[0].Drifting);
            Assert.AreEqual(0f, quick.Karts[0].BoostTime);
        }

        [Test]
        public void GiftBoxes_GiveItems_RocketsBehind_HoneyAhead()
        {
            int rocketsLast = 0, rocketsFirst = 0, honeyFirst = 0, honeyLast = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                foreach (bool last in new[] { true, false })
                {
                    var race = NewRace(KartDifficulty.Medium, seed: seed);
                    race.QuietComputer = true;
                    race.SkipCountdown();
                    var boxes = race.Track.Features.First(f => f.Kind == FeatureKind.Items);
                    var k = race.Karts[0];
                    race.Place(k, boxes.S - 3f, KartRace.BoxD(boxes, 1), 20f);
                    // everybody else ahead (the player last) or behind (the player first)
                    for (int i = 1; i < 6; i++) race.Place(race.Karts[i], boxes.S + (last ? 60f + i * 8f : -60f - i * 8f), (i % 3 - 1) * 4f, 20f);
                    Run(race, 1.6f, r => r.Steer(0, 0f, true));
                    Assert.AreNotEqual(KartItem.None, k.Item, $"seed {seed}: through a box, an item");
                    if (last)
                    {
                        if (k.Item == KartItem.Rocket) rocketsLast++;
                        if (k.Item == KartItem.Honey) honeyLast++;
                    }
                    else
                    {
                        if (k.Item == KartItem.Rocket) rocketsFirst++;
                        if (k.Item == KartItem.Honey) honeyFirst++;
                    }
                }
            }
            Assert.Greater(rocketsLast, 35, "last: mostly rockets");
            Assert.Less(rocketsFirst, 8);
            Assert.Greater(honeyFirst, 15, "first: honey and shields");
            Assert.AreEqual(0, honeyLast);
        }

        [Test]
        public void Paint_FliesToTheKartAhead_AndSpinsItOut_AShieldStopsIt()
        {
            foreach (bool shield in new[] { false, true })
            {
                var race = NewRace(KartDifficulty.Medium);
                race.QuietComputer = true;
                race.SkipCountdown();
                var k = race.Karts[0];
                race.Place(k, 300f, 0f, 22f);
                var ahead = race.Karts[3];
                race.Place(ahead, 330f, 3f, 20f);
                for (int i = 1; i < 6; i++)
                    if (i != 3) race.Place(race.Karts[i], 600f + i * 10f, 0f, 20f);
                ahead.Stars = 5;
                if (shield) ahead.ShieldTime = 10f;
                var events = new List<KartEvent>();
                race.Events += events.Add;
                Run(race, 0.1f, r => r.Steer(0, 0f, true));
                k.Item = KartItem.Paint;
                race.UseItem(0, race.Time);
                Assert.AreEqual(KartItem.None, k.Item);
                Assert.AreEqual(1, race.Paints.Count);
                Assert.AreEqual(3, race.Paints[0].Target, "the kart ahead");
                Run(race, 3f, r => r.Steer(0, 0f, true));
                if (!shield)
                {
                    Assert.IsTrue(events.Any(e => e.Kind == KartEventKind.PaintHit && e.Kart == 3 && e.Other == 0));
                    Assert.AreEqual(2, ahead.Stars, "three stars lost");
                    Assert.AreEqual(1, ahead.PaintHits);
                }
                else
                {
                    Assert.IsTrue(events.Any(e => e.Kind == KartEventKind.ShieldBlock && e.Kart == 3));
                    Assert.AreEqual(5, ahead.Stars);
                    Assert.AreEqual(0f, ahead.ShieldTime, "the shield is used up");
                }
            }
        }

        [Test]
        public void Honey_SlowsTheNextKartThrough()
        {
            var race = NewRace(KartDifficulty.Medium);
            race.QuietComputer = true;
            race.SkipCountdown();
            Run(race, 5f);   // the quick starts' boosts over
            var k = race.Karts[0];
            race.Place(k, 300f, 0f, 22f);
            var behind = race.Karts[2];
            race.Place(behind, 280f, 0f, 22f);
            for (int i = 1; i < 6; i++)
                if (i != 2) race.Place(race.Karts[i], 600f + i * 10f, 6f, 20f);
            k.Item = KartItem.Honey;
            race.UseItem(0, race.Time);
            Assert.AreEqual(1, race.Puddles.Count);
            var events = new List<KartEvent>();
            race.Events += events.Add;
            float slowest = 99f;
            Run(race, 3f, r =>
            {
                r.Steer(0, 0f, true);
                if (events.Any(e => e.Kind == KartEventKind.Honey)) slowest = Mathf.Min(slowest, behind.Speed);
            });
            Assert.IsTrue(events.Any(e => e.Kind == KartEventKind.Honey && e.Kart == 2 && e.Other == 0));
            Assert.AreEqual(0, race.Puddles.Count, "the honey is used up");
            Assert.Less(slowest, 17f, "slowed");
        }

        [Test]
        public void Stars_AreCollected_AndEachGivesALittleSpeed()
        {
            var race = NewRace(KartDifficulty.Medium);
            race.QuietComputer = true;
            race.SkipCountdown();
            var line = race.Track.Features.First(f => f.Kind == FeatureKind.Stars);
            var k = race.Karts[0];
            race.Place(k, line.S - 5f, line.D, 22f);
            Run(race, (line.Length + 10f) / 22f, r => r.Steer(0, 0f, true));
            Assert.AreEqual(line.Count, k.Stars, "the whole line");
            Assert.IsFalse(race.Present(race.Track.Features.IndexOf(line), 0), "gone for a while");
            Run(race, 13f, r => r.Steer(0, 0f, true));
            Assert.IsTrue(race.Present(race.Track.Features.IndexOf(line), 0), "back after 12 s");
        }

        [Test]
        public void Together_OneKart_SteersAsBothPlayers_BothJumpingIsADoubleTrick()
        {
            var (race, _) = BeforeRamp(14f, KartMode.Together);
            Assert.AreEqual(6, race.Karts.Length, "one kart for the two of them, five computer karts");
            Assert.AreSame(race.KartOf(0), race.KartOf(1));
            var k = race.Karts[0];
            // one holds the wheel turned right, the other's hands are down: the one holding steers
            race.Steer(0, 0.6f, true);
            race.Steer(1, -0.6f, false);
            race.Step(0.05f);
            Assert.Greater(k.Steer, 0.2f);
            // both hold it: the average
            race.Steer(0, 0.6f, true);
            race.Steer(1, -0.2f, true);
            Run(race, 0.3f);
            Assert.AreEqual(0.2f, k.Steer, 0.05f);
            race.Steer(1, 0.2f, true);
            race.Steer(0, -0.2f, true);
            // both jump just before the lip
            Run(race, 0.2f);
            race.Hop(0, race.Time);
            race.Hop(1, race.Time - 0.1);
            Run(race, 0.5f);
            Assert.AreEqual(2, k.Trick, "both together: a double trick");
            Run(race, 1.5f);
            Assert.Greater(k.BoostPower, 1.4f);
        }

        [Test]
        public void Versus_TwoKarts_EachDrivenByItsPlayer()
        {
            var race = NewRace(KartDifficulty.Medium, KartMode.Versus);
            Assert.AreEqual(6, race.Karts.Length);
            Assert.AreEqual(0, race.Karts[0].Controller);
            Assert.AreEqual(1, race.Karts[1].Controller);
            Assert.AreNotSame(race.KartOf(0), race.KartOf(1));
            race.SkipCountdown();
            Run(race, 1f, r =>
            {
                r.Steer(0, 0.5f, true);
                r.Steer(1, -0.5f, true);
            });
            Assert.Greater(race.Karts[0].Steer, 0.4f);
            Assert.Less(race.Karts[1].Steer, -0.4f);
            Assert.That(new[] { race.Karts[0].Place, race.Karts[1].Place }, Is.All.GreaterThanOrEqualTo(5), "both at the back of the grid");
        }

        [Test]
        public void OnEasy_TheComputerNeverPaintsAPlayer()
        {
            int hits = 0, aiHits = 0;
            for (int seed = 0; seed < 6; seed++)
            {
                var race = KartSim.Run(KartLevel.For(KartDifficulty.Easy, 2), seed % 3, KartMode.Solo, new[] { KartSim.Profile.Good }, seed,
                                       r => { });
                hits += race.Karts[0].PaintHits;
                aiHits += race.Karts.Skip(1).Sum(k => k.PaintHits);
            }
            Assert.AreEqual(0, hits);
            Assert.Greater(aiHits, 0, "they paint each other");
        }

        /// <summary>
        /// The balance (simulated players with the camera's lag: KartSim), places in 3-lap races on every track, at Easy /
        /// Medium / Hard's first and last race. A child should win most Easy races and finish mid-field on Hard; standing
        /// still (the kart drives itself) should never win.
        /// </summary>
        [Test]
        public void Balance_ByDifficulty()
        {
            var table = Balance(races: 12);
            foreach (var line in table.lines) Console.WriteLine(line);
            float Mean(string profile, KartDifficulty d, int race) => (float)table.places[(profile, d, race)].Average();
            // the hardest race of the hardest cup is tight even for a perfect player (the computer takes every boost too)
            Assert.Less(Mean("perfect", KartDifficulty.Hard, 2), 2.1f, "perfect is at the front even in Hard's last race");
            Assert.GreaterOrEqual(table.places[("perfect", KartDifficulty.Hard, 2)].Count(p => p == 1) * 3, table.places[("perfect", KartDifficulty.Hard, 2)].Count,
                                  "and wins a third of them");
            Assert.Less(Mean("perfect", KartDifficulty.Medium, 2), 2f);
            Assert.Less(Mean("good", KartDifficulty.Easy, 0), 1.6f);
            Assert.Less(Mean("good", KartDifficulty.Medium, 2), 2.6f);
            Assert.Less(Mean("child", KartDifficulty.Easy, 0), 2.2f, "a child wins most Easy races");
            Assert.Greater(Mean("child", KartDifficulty.Hard, 2), 2.5f, "Hard is hard for a child");
            Assert.Greater(Mean("standing still", KartDifficulty.Medium, 0), 4f);
            Assert.IsTrue(table.places.Where(kv => kv.Key.profile == "standing still").All(kv => kv.Value.All(p => p > 1)), "standing still never wins");
            // every race ends in time
            Assert.Less(table.longest, 200.0);
        }

        static (List<string> lines, Dictionary<(string profile, KartDifficulty d, int race), List<int>> places, double longest) Balance(int races)
        {
            var places = new Dictionary<(string, KartDifficulty, int), List<int>>();
            var lines = new List<string> { "places (mean) at Easy / Medium / Hard, first and last race of a cup:" };
            double longest = 0;
            foreach (var p in KartSim.Profile.All)
            {
                string row = $"{p.Name,-15}";
                foreach (KartDifficulty d in Enum.GetValues(typeof(KartDifficulty)))
                    foreach (int r in new[] { 0, 2 })
                    {
                        var list = new List<int>();
                        double time = 0;
                        int tricks = 0, walls = 0;
                        double winners = 0;
                        for (int i = 0; i < races; i++)
                        {
                            var race = KartSim.Run(KartLevel.For(d, r), i % KartTracks.Count, KartMode.Solo, new[] { p }, 1000 * i + 7 * r + (int)d);
                            list.Add(race.Karts[0].Place);
                            time += race.Karts[0].FinishTime;
                            tricks += race.Karts[0].TricksDone;
                            walls += race.Karts[0].WallHits;
                            winners += race.Order()[0].FinishTime;
                            longest = Math.Max(longest, race.Time);
                        }
                        places[(p.Name, d, r)] = list;
                        row += $" {d.ToString()[0]}{r}: {list.Average():0.0} ({time / races:0}s, {tricks / (float)races:0.0}t, {walls / (float)races:0.0}w, {winners / races:0}s)";
                    }
                lines.Add(row);
            }
            return (lines, places, longest);
        }
    }
}
