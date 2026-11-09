using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Volley.Tests
{
    /// <summary>
    /// Volejboll!'s rules (2026-11-07) with simulated players: the three touches and their moves (the bump receives, both
    /// hands up set, one hand hits over as fast as the hand went), what a wrong move or none does, late moves through the
    /// camera's lag, the computer's sure first returns, the difficulties, two players together and against each other,
    /// the scoring, the balance.
    /// </summary>
    public class VolleyMatchTests
    {
        const float Dt = 1f / 60f;

        sealed class Run
        {
            public VolleyMatch Match;
            public readonly List<VolleyEvent> Events = new List<VolleyEvent>();
            /// <summary>Every touch with the ball's flight right after it (speed across, the height it left from).</summary>
            public readonly List<(VolleyEvent e, float across, float height)> Touches = new List<(VolleyEvent, float, float)>();
            public double Seconds;
        }

        static Run Play(VolleyLevel level, VolleyMode mode, VolleySim[] sims, int seed, int points = 7, int firstServer = 0,
                        double maxSeconds = 1500, Func<Run, bool> stop = null)
        {
            var run = new Run();
            var m = new VolleyMatch(level, points, seed, firstServer, mode);
            run.Match = m;
            m.Events += e =>
            {
                run.Events.Add(e);
                if (e.Kind == VolleyEventKind.Touch)
                {
                    var v = m.Ball.Velocity;
                    run.Touches.Add((e, new Vector2(v.x, v.z).magnitude, m.Ball.From.y));
                }
            };
            m.Begin();
            while (m.Phase != VolleyPhase.Over && m.Time < maxSeconds)
            {
                foreach (var s in sims) s.Step(m);
                m.Step(Dt);
                if (stop != null && stop(run)) break;
            }
            run.Seconds = m.Time;
            return run;
        }

        static IEnumerable<(VolleyEvent e, float across, float height)> Ours(Run r, int team = 0) => r.Touches.Where(t => t.e.Team == team);

        [Test]
        public void TheBumpReceives_BothHandsUpSet_OneHandHitsItOver()
        {
            // the computer serves; the player's toys receive, set and spike
            var run = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { VolleySim.Perfect(1) }, 3, firstServer: 1,
                           stop: r => r.Touches.Any(t => t.e.Team == 0 && t.e.Touch == Touch.Spike));
            var ours = Ours(run).Select(t => t.e).ToList();
            Assert.GreaterOrEqual(ours.Count, 3, "three touches");
            Assert.AreEqual(Touch.Bump, ours[0].Touch, "the serve received with the bump");
            Assert.AreEqual(1, ours[0].Number);
            Assert.AreEqual(Touch.Set, ours[1].Touch, "both hands up: set for the teammate");
            Assert.AreEqual(2, ours[1].Number);
            Assert.AreNotEqual(ours[0].Player, ours[1].Player, "the other toy sets");
            Assert.AreEqual(Touch.Spike, ours[2].Touch, "one hand: hit over");
            Assert.AreEqual(3, ours[2].Number);
            Assert.AreEqual(ours[0].Player, ours[2].Player, "the one who received hits");
            var spike = Ours(run).First(t => t.e.Touch == Touch.Spike);
            Assert.Greater(spike.height, 1.75f, "hit near the top of the set");
            // and it went over: the computer plays it next or it came down on its side
            var after = run.Events.SkipWhile(e => !(e.Kind == VolleyEventKind.Touch && e.Touch == Touch.Spike)).Skip(1).ToList();
            Assert.IsFalse(after.Any(e => e.Kind == VolleyEventKind.Net), "never into the net");
        }

        [Test]
        public void ABallOverTheNet_IsReceivedOnlyWithTheBump()
        {
            foreach (var wrong in new[] { Move.Set, Move.Hit })
            {
                var sim = new VolleySim(2) { AtReceive = wrong };
                var run = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { sim }, 5, firstServer: 1,
                               stop: r => r.Events.Any(e => e.Kind == VolleyEventKind.Point));
                var shank = run.Events.FirstOrDefault(e => e.Kind == VolleyEventKind.Touch && e.Team == 0);
                Assert.AreEqual(Touch.Shank, shank.Touch, $"{wrong} at the receive: off the arms");
                Assert.AreEqual(wrong, shank.Move);
                var point = run.Events.Last(e => e.Kind == VolleyEventKind.Point);
                Assert.AreEqual(1, point.Team, "their point");
                Assert.AreEqual(PointReason.Wrong, point.Reason);
                Assert.AreEqual(1, run.Match.Stats[0].Wrong);
            }
        }

        /// <summary>Every jumping attack at the player's team (<see cref="VolleyEventKind.Defence"/>): what it was, what they
        /// chose, and the point it ended in (or none: the rally went on).</summary>
        static List<(Attack attack, Defence choice, PointReason? reason, int winner)> Defences(Run run, int team = 0)
        {
            var list = new List<(Attack, Defence, PointReason?, int)>();
            (Attack attack, Defence choice)? open = null;
            foreach (var e in run.Events)
            {
                if (e.Kind == VolleyEventKind.Defence && e.Team == team)
                {
                    if (open.HasValue) list.Add((open.Value.attack, open.Value.choice, null, -1));
                    open = (e.Attack, e.Choice);
                }
                else if (open.HasValue && e.Kind == VolleyEventKind.Point)
                {
                    list.Add((open.Value.attack, open.Value.choice, e.Reason, e.Team));
                    open = null;
                }
                else if (open.HasValue && e.Kind == VolleyEventKind.Touch && e.Team == team && e.Touch is Touch.Bump or Touch.Set)
                {
                    // received, the rally goes on
                    list.Add((open.Value.attack, open.Value.choice, null, -1));
                    open = null;
                }
            }
            return list;
        }

        /// <summary>
        /// The defence is a choice at the moment of the other side's hit (2026-11-08, user: "the player can keep the hands
        /// together before the ball is shot ... the player chooses between low hands save or high hands block; depending on
        /// the type of shot the chosen save is correct or wrong"): a smash is stopped only by the block (back down: the
        /// player's point), and received low it flies off the arms; a tip goes over a block and is received low. Holding one
        /// pose all along stops only one kind; reading the hitter stops both.
        /// </summary>
        [Test]
        public void ASmash_IsStoppedOnlyByTheBlock_ATip_OnlyByTheReceive()
        {
            var level = VolleyLevel.For(VolleyDifficulty.Medium, 2, 0);
            List<(Attack attack, Defence choice, PointReason? reason, int winner)> Defend(Func<int, VolleySim> make)
            {
                var all = new List<(Attack, Defence, PointReason?, int)>();
                for (int seed = 0; seed < 4; seed++) all.AddRange(Defences(Play(VolleyLevel.For(VolleyDifficulty.Medium, 2, seed), VolleyMode.Solo, new[] { make(seed) }, 600 + seed)));
                return all;
            }
            string Describe(string name, List<(Attack attack, Defence choice, PointReason? reason, int winner)> d) =>
                $"{name}: {d.Count(x => x.attack == Attack.Smash)} smashes ({d.Count(x => x.attack == Attack.Smash && x.reason == PointReason.Block)} blocked), " +
                $"{d.Count(x => x.attack == Attack.Tip)} tips ({d.Count(x => x.attack == Attack.Tip && x.reason == null)} received, " +
                $"{d.Count(x => x.reason == PointReason.OverBlock)} over the block)";

            var low = Defend(s => VolleySim.AlwaysReceiving(s));
            var high = Defend(s => VolleySim.AlwaysBlocking(s));
            var reads = Defend(s => VolleySim.Perfect(s));
            Debug.Log($"Medium final ({level.FreeBall:0.00} free balls, {level.TipShare:0.00} tips):\n{Describe("always low", low)}\n" +
                      $"{Describe("always block", high)}\n{Describe("reads the hitter", reads)}");

            // hands together low: no smash ever stopped (too hard, or nothing), every tip received
            Assert.Greater(low.Count(x => x.attack == Attack.Smash), 10);
            Assert.IsTrue(low.Where(x => x.attack == Attack.Smash).All(x => x.winner == 1 && x.reason is PointReason.TooHard or PointReason.Landed),
                          "received low, a smash flies off the arms: their point");
            Assert.IsTrue(low.Where(x => x.attack == Attack.Tip).All(x => x.reason == null || x.reason != PointReason.OverBlock), "a tip is received low");
            // both hands up: every smash blocked down for a point, every tip over the block for theirs
            Assert.IsTrue(high.Where(x => x.attack == Attack.Smash).All(x => x.reason == PointReason.Block && x.winner == 0), "a smash into the block: our point");
            Assert.Greater(high.Count(x => x.attack == Attack.Tip), 2);
            Assert.IsTrue(high.Where(x => x.attack == Attack.Tip).All(x => x.reason == PointReason.OverBlock && x.winner == 1), "a tip over the block: their point");
            // reading the hitter: every smash blocked, every tip received
            Assert.IsTrue(reads.Where(x => x.attack == Attack.Smash).All(x => x.reason == PointReason.Block), "read: smashes blocked");
            Assert.IsTrue(reads.Where(x => x.attack == Attack.Tip).All(x => x.reason != PointReason.OverBlock), "read: tips received");
            Assert.Greater(reads.Count(x => x.attack == Attack.Tip && x.reason == null), 1);
        }

        /// <summary>The choice is the pose the camera saw as the hitter hit (<see cref="VolleyMatch.BlockLead"/> before the
        /// hit, through the camera's lag): hands up by then block the smash; hands up only after it are too late; the
        /// computer's hitter waits at the top of its jump for the camera's news.</summary>
        [Test]
        public void TheBlock_IsWhatThePlayerShowsAsTheHitterHits()
        {
            foreach (var (from, to, blocked) in new[] { (-0.5, -0.04, true), (0.02, 0.4, false), (-0.25, -0.12, true) })
            {
                var m = new VolleyMatch(VolleyLevel.Of(3f, 0), 7, 41) { Practice = true, PracticeAttack = n => Attack.Smash };
                int blocks = 0, smashes = 0;
                m.Events += e =>
                {
                    if (e.Kind == VolleyEventKind.Touch && e.Touch == Touch.Block) blocks++;
                    if (e.Kind == VolleyEventKind.Defence) smashes++;
                };
                const double lag = 0.17, every = 1.0 / 15.0;
                var queue = new List<(double at, double time)>();
                double sample = 0;
                m.Begin();
                // the player serves (a hit while the toss is up), then shows the block around the computer's hit
                while (m.Time < 15.0 && m.Phase != VolleyPhase.PointOver)
                {
                    if (m.Phase == VolleyPhase.Toss) m.See(0, Move.Hit, m.Time, 6f, new Vector2(0f, -1f));
                    while (sample + every <= m.Time)
                    {
                        sample += every;
                        if (m.Coming.Active && sample >= m.Coming.DecideAt + from && sample <= m.Coming.DecideAt + to) queue.Add((sample + lag, sample));
                    }
                    for (int i = queue.Count - 1; i >= 0; i--)
                        if (queue[i].at <= m.Time)
                        {
                            m.See(0, Move.Set, queue[i].time);
                            queue.RemoveAt(i);
                        }
                    m.SawUntil(0, m.Time - lag - every);
                    m.Step(Dt);
                }
                Assert.AreEqual(1, smashes, "a smash came");
                Assert.AreEqual(blocked ? 1 : 0, blocks, $"hands up from {from:+0.00;-0.00} to {to:+0.00;-0.00} s around the moment of the choice");
            }
        }

        /// <summary>The computer's attacks by difficulty: free balls and smashes on Easy (a "!" over a smasher), tips from
        /// Medium, some disguised on Hard (they look like smashes until just before the hit), where it also tips more against
        /// a player who always blocks.</summary>
        [Test]
        public void TheAttacks_ByDifficulty_EasyNeverTips_HardDisguisesAndReadsThePlayer()
        {
            (int free, int smash, int tip, int disguised) Count(VolleyDifficulty d, Func<int, VolleySim> make)
            {
                int free = 0, smash = 0, tip = 0, disguised = 0;
                for (int seed = 0; seed < 4; seed++)
                {
                    var m = new VolleyMatch(VolleyLevel.For(d, 2, seed), 7, 700 + seed, seed % 2);
                    var sim = make(seed);
                    bool counted = false;
                    m.Events += e =>
                    {
                        if (e.Kind == VolleyEventKind.Touch && e.Team == 1 && e.Number == 3 && !m.SureRally && e.Attack == Attack.Free && m.CpuReturnsThisRally > m.SureReturns) free++;
                    };
                    m.Begin();
                    while (m.Phase != VolleyPhase.Over && m.Time < 1500)
                    {
                        sim.Step(m);
                        if (m.Coming.Active && !counted)
                        {
                            counted = true;
                            if (m.Coming.Kind == Attack.Smash) smash++;
                            else
                            {
                                tip++;
                                if (m.Coming.TellAt > m.Time + 0.05) disguised++;
                            }
                        }
                        else if (!m.Coming.Active) counted = false;
                        m.Step(Dt);
                    }
                }
                return (free, smash, tip, disguised);
            }
            var easy = Count(VolleyDifficulty.Easy, s => VolleySim.Good(s));
            var medium = Count(VolleyDifficulty.Medium, s => VolleySim.Good(s));
            var hardLow = Count(VolleyDifficulty.Hard, s => VolleySim.AlwaysReceiving(s));
            var hardHigh = Count(VolleyDifficulty.Hard, s => VolleySim.AlwaysBlocking(s));
            Debug.Log($"finals' attacks (free, smash, tip, disguised): Easy {easy}, Medium {medium}, Hard against a player always low {hardLow}, always blocking {hardHigh}");
            Assert.AreEqual(0, easy.tip, "Easy never tips");
            Assert.Greater(easy.smash, 10);
            Assert.IsTrue(VolleyLevel.For(VolleyDifficulty.Easy, 2, 0).ShowTell && !VolleyLevel.For(VolleyDifficulty.Medium, 0, 0).ShowTell, "the \"!\" only on Easy");
            Assert.Greater(medium.tip, 3, "Medium tips");
            Assert.AreEqual(0, medium.disguised, "Medium's tips look like tips");
            Assert.Greater(hardHigh.disguised, 2, "Hard disguises some tips");
            float TipShare((int free, int smash, int tip, int disguised) c) => c.tip / (float)Mathf.Max(1, c.tip + c.smash);
            Assert.Greater(TipShare(hardHigh), TipShare(hardLow) + 0.15f, "Hard tips more against a player who always blocks");
        }

        [Test]
        public void NoMove_TheBallComesDown_TheirPoint()
        {
            var run = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { VolleySim.StandingStill(1) }, 7, firstServer: 1,
                           stop: r => r.Events.Any(e => e.Kind == VolleyEventKind.Point));
            Assert.IsTrue(run.Events.Any(e => e.Kind == VolleyEventKind.Missed && e.Team == 0 && e.Number == 1), "a ball with no move for it");
            var point = run.Events.Last(e => e.Kind == VolleyEventKind.Point);
            Assert.AreEqual(1, point.Team);
            Assert.AreEqual(PointReason.Landed, point.Reason);
        }

        [Test]
        public void OneHandAtTheSecondTouch_PushesItOver_APoseAtTheThird_Too()
        {
            var second = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { new VolleySim(3) { AtSecond = Move.Hit } }, 9, firstServer: 1,
                              stop: r => Ours(r).Count() >= 2);
            var two = Ours(second).Select(t => t.e).ToList();
            Assert.AreEqual(Touch.Push, two[1].Touch, "a hit at the second touch goes over (softly)");
            Assert.AreEqual(2, two[1].Number);

            // (the set's pose let go after the second touch and taken again: a pose held on isn't a new move)
            var third = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo,
                             new[] { new VolleySim(4) { AtThird = Move.Set, PoseHold = 0.05f, PoseLead = 0.22f, PoseLeadJitter = 0f } }, 11, firstServer: 1,
                             stop: r => Ours(r).Count() >= 3);
            var three = Ours(third).Select(t => t.e).ToList();
            Assert.AreEqual(Touch.Set, three[1].Touch);
            Assert.AreEqual(Touch.Push, three[2].Touch, "a pose at the third touch pushes it over");
            Assert.Less(Ours(third).Last().across, 9f, "slowly");

            var bumpSet = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { new VolleySim(5) { AtSecond = Move.Bump } }, 13, firstServer: 1,
                               stop: r => Ours(r).Count() >= 3);
            var b = Ours(bumpSet).Select(t => t.e).ToList();
            Assert.AreEqual(Touch.Bump, b[1].Touch, "a bump at the second touch sets it too");
            Assert.AreEqual(Touch.Spike, b[2].Touch);
        }

        [Test]
        public void AFasterHand_HitsTheBallFaster()
        {
            float Average(float hand)
            {
                var speeds = new List<float>();
                for (int seed = 0; seed < 6; seed++)
                {
                    var sim = new VolleySim(seed) { HitSpeed = hand, HitSpeedJitter = 0.2f, HitError = 0.03f };
                    var run = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { sim }, 100 + seed, maxSeconds: 120);
                    speeds.AddRange(Ours(run).Where(t => t.e.Touch == Touch.Spike).Select(t => t.across));
                }
                Assert.Greater(speeds.Count, 10);
                return speeds.Average();
            }
            float slow = Average(2.5f), fast = Average(8.5f);
            Debug.Log($"spikes across: gentle hand {slow:0.0} m/s, fast hand {fast:0.0} m/s");
            Assert.Greater(fast, slow * 1.4f);
        }

        [Test]
        public void ALateHit_ThroughTheCamerasLag_StillMeetsTheBall_NearTheTop()
        {
            // the hit seen 0.1 s after the ball's moment and told 0.17 + 0.1 s later still (two players on the box): the ball
            // is at the top of the set and hardly moves
            var sim = new VolleySim(6) { HitBias = 0.1f, HitError = 0.01f, Lag = 0.17f };
            var run = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { sim }, 15, firstServer: 1, maxSeconds: 150);
            var spikes = Ours(run).Where(t => t.e.Touch == Touch.Spike).ToList();
            Assert.Greater(spikes.Count, 3, "late hits still spike");
            Assert.IsTrue(spikes.All(s => s.e.Late), "late");
            Assert.Greater(spikes.Min(s => s.height), 1.55f, "from above the net");
            Assert.IsFalse(run.Events.Any(e => e.Kind == VolleyEventKind.Net && e.Team == 0), "and over it");
        }

        [Test]
        public void ALateBump_SavesTheBall_FromLowerDown()
        {
            // the bump taken only as the ball arrives, seen late: a save from low down, still up to the teammate
            var sim = new VolleySim(7) { PoseLead = 0f, PoseLeadJitter = 0f, PoseHold = 0.12f, Lag = 0.2f };
            var run = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { sim }, 17, firstServer: 1,
                           stop: r => Ours(r).Count() >= 2 || r.Events.Any(e => e.Kind == VolleyEventKind.Point));
            var first = Ours(run).First();
            Assert.AreEqual(Touch.Bump, first.e.Touch);
            Assert.IsTrue(first.e.Late);
            Assert.Less(first.height, VolleyMatch.BumpHeight);
            Assert.GreaterOrEqual(first.height, VolleyMatch.LowestTouch * 0.8f - 1e-3f);
            Assert.AreEqual(2, Ours(run).Count(), "the teammate plays it next");
        }

        [Test]
        public void ThePlayersHits_GoOverAndIn()
        {
            int spikes = 0;
            for (int seed = 0; seed < 8; seed++)
            {
                var run = Play(VolleyLevel.Of(4f, seed), VolleyMode.Solo, new[] { VolleySim.Child(seed) }, 200 + seed, maxSeconds: 300);
                // the player's hits over the net (a spike, a push, a serve) with nothing after them on their side: their
                // flight ends in the other court (a set nobody hit may drift into the net: that's a miss, not a hit)
                bool hitOver = false;
                foreach (var e in run.Events)
                {
                    if (e.Kind == VolleyEventKind.Touch && e.Team == 0 && e.Touch == Touch.Spike) spikes++;
                    if (e.Kind is VolleyEventKind.Touch or VolleyEventKind.Missed && e.Team == 0)
                        hitOver = e.Kind == VolleyEventKind.Touch && e.Touch is Touch.Spike or Touch.Push or Touch.Serve;
                    if (e.Kind == VolleyEventKind.Touch && e.Team == 1) hitOver = false;
                    Assert.IsFalse(hitOver && e.Kind == VolleyEventKind.Net && e.Team == 0, "the player's hits never go into the net");
                    Assert.IsFalse(hitOver && e.Kind == VolleyEventKind.Point && e.Team == 1, "nor out");
                }
            }
            Assert.Greater(spikes, 30);
        }

        /// <summary>Tenis!'s rule (user 2026-11-07: "make the bot hit at least 5 times, after that it should depend on the
        /// difficulty"): every rally, the computer's first two returns come back and within reach; a point the player
        /// loses before them is their own miss or wrong move, never a ball out of reach.</summary>
        [Test]
        public void TheComputer_ReturnsItsFirstTwoBalls_OfEveryRally()
        {
            foreach (var difficulty in new[] { VolleyDifficulty.Easy, VolleyDifficulty.Medium, VolleyDifficulty.Hard })
            {
                int rallies = 0;
                for (int seed = 0; seed < 5; seed++)
                {
                    var level = VolleyLevel.For(difficulty, 2, seed);
                    var m = new VolleyMatch(level, 7, 300 + seed, seed % 2);
                    var sim = VolleySim.Child(seed);
                    bool missed = false;
                    m.Events += e =>
                    {
                        if (e.Kind is VolleyEventKind.Missed or VolleyEventKind.OutOfReach || (e.Kind == VolleyEventKind.Touch && e.Team == 0 && e.Touch == Touch.Shank))
                        {
                            Assert.AreNotEqual(VolleyEventKind.OutOfReach, m.SureRally ? e.Kind : VolleyEventKind.Missed,
                                               $"{difficulty}: a ball of the sure part out of reach");
                            missed = true;
                        }
                        if (e.Kind != VolleyEventKind.Point) return;
                        rallies++;
                        if (e.Team == 0) Assert.GreaterOrEqual(m.CpuReturnsThisRally, m.SureReturns, $"{difficulty}: won before the computer's sure returns");
                        else if (m.CpuReturnsThisRally < m.SureReturns) Assert.IsTrue(missed, $"{difficulty}: lost in the sure part without a miss ({e.Reason})");
                        missed = false;
                    };
                    m.Begin();
                    while (m.Phase != VolleyPhase.Over && m.Time < 1500)
                    {
                        sim.Step(m);
                        m.Step(Dt);
                    }
                }
                Assert.Greater(rallies, 30);
            }
        }

        [Test]
        public void TheDifficulty_DecidesWhetherEveryBallCanBeReached()
        {
            var table = new List<string>();
            foreach (var difficulty in new[] { VolleyDifficulty.Easy, VolleyDifficulty.Medium, VolleyDifficulty.Hard })
            {
                int balls = 0, away = 0;
                for (int seed = 0; seed < 6; seed++)
                {
                    var run = Play(VolleyLevel.For(difficulty, 2, seed), VolleyMode.Solo, new[] { VolleySim.Good(seed) }, 400 + seed);
                    balls += run.Events.Count(e => e.Kind == VolleyEventKind.Touch && e.Team == 1 && e.Number is 0 or 3);
                    away += run.Events.Count(e => e.Kind == VolleyEventKind.OutOfReach);
                }
                table.Add($"{difficulty}: {away} of {balls} balls out of reach ({100f * away / Mathf.Max(1, balls):0.0} %)");
                if (difficulty == VolleyDifficulty.Easy) Assert.AreEqual(0, away, "Easy: every ball reached");
                // (since 2026-11-08 a smash is the block's or nobody's and a tip read right is always reached: what can get
                // away on the harder difficulties is a free ball or a serve into a gap, and the defence is the challenge)
                Assert.Less(away, balls * 0.15f, $"{difficulty}: most balls reached");
            }
            Debug.Log(string.Join("\n", table));
        }

        [Test]
        public void Together_EachPlayerPlaysTheirOwnToy_TheirHalf()
        {
            var sims = new[] { VolleySim.Good(1, 0).SharingTheCamera(), VolleySim.Good(2, 1).SharingTheCamera() };
            var run = Play(VolleyLevel.For(VolleyDifficulty.Easy, 1, 0), VolleyMode.Together, sims, 21);
            var m = run.Match;
            Assert.AreEqual(0, m.Players[0].Controller);
            Assert.AreEqual(1, m.Players[1].Controller);
            Assert.AreEqual(VolleyPhase.Over, m.Phase, "a whole match");
            var ours = Ours(run).Select(t => t.e).ToList();
            Assert.Greater(ours.Count(e => e.Player == 0), 10, "player 1's toy plays");
            Assert.Greater(ours.Count(e => e.Player == 1), 10, "player 2's toy plays");
            Assert.Greater(m.Stats[0].Touches, 10);
            Assert.Greater(m.Stats[1].Touches, 10);
            // a set is always for the other player
            for (int i = 1; i < ours.Count; i++)
                if (ours[i].Touch == Touch.Set && ours[i].Number == 2) Assert.AreNotEqual(ours[i - 1].Player, ours[i].Player);
            // receives on the left half are player 1's
            foreach (var e in run.Events.Where(e => e.Kind == VolleyEventKind.Touch && e.Team == 0 && e.Number == 1 && e.Touch == Touch.Bump))
                if (Mathf.Abs(e.Position.x) > 0.6f) Assert.AreEqual(e.Position.x < 0f ? 0 : 1, e.Player, "the receive on its own half");
            Assert.Less(m.PaceFor(0), VolleyLevel.For(VolleyDifficulty.Easy, 1, 0).ServePace, "two players: a slower ball");
            Debug.Log($"together: {m.Score.Points(0)}-{m.Score.Points(1)} in {run.Seconds / 60:0.0} min");
        }

        [Test]
        public void Versus_TwoPlayers_EachPlaysTheirWholeTeam()
        {
            var sims = new[] { VolleySim.Good(3, 0).SharingTheCamera(), VolleySim.Good(4, 1).SharingTheCamera() };
            var run = Play(VolleyLevel.Versus(), VolleyMode.Versus, sims, 23);
            var m = run.Match;
            Assert.AreEqual(VolleyPhase.Over, m.Phase, "a whole match");
            Assert.IsFalse(m.SureRally);
            for (int team = 0; team < 2; team++)
            {
                Assert.Greater(Ours(run, team).Count(t => t.e.Touch == Touch.Spike), 5, $"team {team} spikes");
                Assert.IsTrue(Ours(run, team).All(t => run.Match.Players[t.e.Player].Controller == team), "each player's own team");
            }
            Debug.Log($"versus: {m.Score.Points(0)}-{m.Score.Points(1)} in {run.Seconds / 60:0.0} min, longest rally {m.LongestRally}");
            Assert.Less(run.Seconds, 600, "points end (it isn't an endless rally)");
            Assert.Less(m.LongestRally, 30);
        }

        /// <summary>Versus: a player's hit at the third touch is a smash or, gentle, a tip, and the other player chooses as it
        /// comes (block or receive) like against the computer.</summary>
        [Test]
        public void Versus_AStrongHitIsASmash_AGentleOneATip_TheOtherPlayerDefends()
        {
            var gentle = VolleySim.Good(5, 0).SharingTheCamera();
            gentle.HitSpeed = 2.4f;
            gentle.HitSpeedJitter = 0.2f;
            var strong = VolleySim.Good(6, 1).SharingTheCamera();
            var run = Play(VolleyLevel.Versus(), VolleyMode.Versus, new[] { gentle, strong }, 29, maxSeconds: 400);
            var atOne = Defences(run, 1);
            var atZero = Defences(run, 0);
            Debug.Log($"versus: at player 2 {atOne.Count(d => d.attack == Attack.Tip)} tips, {atOne.Count(d => d.attack == Attack.Smash)} smashes; " +
                      $"at player 1 {atZero.Count(d => d.attack == Attack.Tip)} tips, {atZero.Count(d => d.attack == Attack.Smash)} smashes, " +
                      $"{atZero.Count(d => d.reason == PointReason.Block)} blocked");
            Assert.Greater(atOne.Count, 3);
            Assert.IsTrue(atOne.All(d => d.attack == Attack.Tip), "player 1's gentle hits are tips");
            Assert.Greater(atZero.Count, 3);
            Assert.IsTrue(atZero.All(d => d.attack == Attack.Smash), "player 2's strong ones smashes");
            Assert.Greater(atZero.Count(d => d.reason == PointReason.Block), 0, "and some are blocked");
            Assert.IsTrue(atZero.Concat(atOne).All(d => d.choice != Defence.None), "each chose");
        }

        /// <summary>Practice (the PlayMode test's mode): a scripted player on a fixed 10 s cycle (the bump held 0.2-1.9 s,
        /// the set 2.3-3.4 s, a hit at 4.4 s, both hands up again 7.6-8.9 s: the block; the first cycle serves at 4.4 and
        /// 6.4 s), the computer timing its balls to it: its attacks come at 8.3 s, a smash and a free ball by turns, so the
        /// cycles go block (the point, the player serves again) and receive, set, spike (2026-11-08: the computer's own
        /// passes were stretched to the player's slots too, so only every other cycle had a ball).</summary>
        [Test]
        public void Practice_TimesThePlayersBalls_ToAScriptedCycle()
        {
            const double period = 10.0, lag = 0.1, every = 1.0 / 30.0;
            var m = new VolleyMatch(VolleyLevel.Of(0f, 0), 7, 31) { Practice = true, PracticeAttack = n => n % 2 == 0 ? Attack.Smash : Attack.Free };
            m.PracticeContact = (number, earliest) =>
            {
                double t = number switch { 0 => 4.4 - 0.6, 1 => 1.2, 2 => 3.0, 3 => 4.45, _ => 8.3 };
                while (t < earliest) t += period;
                return t;
            };
            var touches = new List<VolleyEvent>();
            m.Events += e =>
            {
                if (e.Kind == VolleyEventKind.Touch && e.Team == 0) touches.Add(e);
            };
            var queue = new List<(double at, Move move, double time)>();
            double sample = 0;
            m.Begin();
            while (m.Time < 100.0)
            {
                while (sample + every <= m.Time)
                {
                    sample += every;
                    double u = sample % period;
                    int k = (int)(sample / period);
                    if (u >= 0.2 && u <= 1.9) queue.Add((sample + lag, Move.Bump, sample));
                    else if (u >= 2.3 && u <= 3.4 || u >= 7.6 && u <= 8.9) queue.Add((sample + lag, Move.Set, sample));
                    foreach (double hit in k == 0 ? new[] { 4.4, 6.4 } : new[] { 4.4 })
                        if (u >= hit && u < hit + every) queue.Add((sample + lag + 0.1, Move.Hit, sample));
                }
                for (int i = queue.Count - 1; i >= 0; i--)
                    if (queue[i].at <= m.Time)
                    {
                        m.See(0, queue[i].move, queue[i].time, 8f, new Vector2(0f, -1f));
                        queue.RemoveAt(i);
                    }
                m.SawUntil(0, m.Time - lag - 0.12);
                m.Step(Dt);
            }
            string seen = string.Join(", ", touches.Select(t => $"{t.Touch}#{t.Number}"));
            Debug.Log($"practice: {seen}");
            Assert.AreEqual(Touch.Serve, touches[0].Touch);
            Assert.GreaterOrEqual(touches.Count(t => t.Touch == Touch.Block), 3, $"the smashes blocked: {seen}");
            Assert.GreaterOrEqual(touches.Count(t => t.Touch == Touch.Bump && t.Number == 1), 3, "the free balls received");
            Assert.GreaterOrEqual(touches.Count(t => t.Touch == Touch.Set && t.Number == 2), 3, "set");
            Assert.GreaterOrEqual(touches.Count(t => t.Touch == Touch.Spike), 3, "spiked");
            Assert.IsFalse(touches.Exists(t => t.Touch is Touch.Shank or Touch.Push), "each touch got its own move");
        }

        [Test]
        public void Score_RallyScoring_TwoClear_Capped()
        {
            var s = new VolleyScore(7);
            for (int i = 0; i < 6; i++)
            {
                s.PointTo(0);
                s.PointTo(1);
            }
            Assert.AreEqual(6, s.Points(0));
            Assert.AreEqual(1, s.Server, "whoever won the rally serves");
            Assert.IsTrue(s.MatchPoint(0) == false || s.Points(0) >= 6);
            s.PointTo(0);
            Assert.IsFalse(s.MatchOver, "7-6: two clear needed");
            Assert.IsTrue(s.MatchPoint(0));
            s.PointTo(1);
            s.PointTo(0);
            s.PointTo(1);
            s.PointTo(0);
            s.PointTo(1);
            Assert.AreEqual(9, s.Points(0));
            Assert.AreEqual(9, s.Points(1));
            Assert.IsTrue(s.MatchPoint(0) && s.MatchPoint(1), "the cap: the next point wins");
            s.PointTo(1);
            Assert.AreEqual(1, s.Winner);
            var quick = new VolleyScore(7);
            for (int i = 0; i < 7; i++) quick.PointTo(0);
            Assert.AreEqual(0, quick.Winner, "7-0");
        }

        [Test]
        public void TheServe_IsAHitWhileTheBallIsUp_ElseTossedAgain()
        {
            // nobody hits: tossed again, then the toy serves by itself (the game never stalls)
            var run = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { VolleySim.StandingStill(1) }, 25,
                           stop: r => r.Touches.Any(t => t.e.Touch == Touch.Serve));
            Assert.GreaterOrEqual(run.Events.Count(e => e.Kind == VolleyEventKind.Caught), 2, "caught again");
            Assert.AreEqual(Touch.Serve, run.Touches[0].e.Touch);
            Assert.Less(run.Touches[0].e.Power, 0.3f, "softly");

            var served = Play(VolleyLevel.Of(0f, 0), VolleyMode.Solo, new[] { VolleySim.Perfect(2) }, 27,
                              stop: r => r.Touches.Any(t => t.e.Touch == Touch.Serve));
            Assert.AreEqual(0, served.Events.Count(e => e.Kind == VolleyEventKind.Caught), "served at the first toss");
            Assert.Greater(served.Touches[0].e.Power, 0.6f, "as hard as the hand");
        }

        /// <summary>
        /// The balance by difficulty (wins of 8: the quarter-final's and the final's strengths): what each kind of player
        /// wins, and how long a match takes.
        /// </summary>
        [Test]
        public void Balance_ByDifficulty()
        {
            var profiles = new (string name, Func<int, VolleySim> make)[]
            {
                ("perfect", s => VolleySim.Perfect(s)), ("good", s => VolleySim.Good(s)), ("careful", s => VolleySim.CarefulChild(s)),
                ("child", s => VolleySim.Child(s)),
                ("struggling", s => VolleySim.Struggling(s)), ("still", s => VolleySim.StandingStill(s)),
                ("always low", s => VolleySim.AlwaysReceiving(s)), ("always up", s => VolleySim.AlwaysBlocking(s)),
            };
            var wins = new Dictionary<(string, VolleyDifficulty, int), int>();
            var conceded = new Dictionary<(string, VolleyDifficulty, int), int>();
            var lines = new List<string>();
            foreach (var (name, make) in profiles)
            {
                var line = $"{name,-11}";
                foreach (var d in new[] { VolleyDifficulty.Easy, VolleyDifficulty.Medium, VolleyDifficulty.Hard })
                foreach (int round in new[] { 0, 2 })
                {
                    int won = 0, pointsFor = 0, pointsAgainst = 0;
                    double seconds = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        var run = Play(VolleyLevel.For(d, round, i), VolleyMode.Solo, new[] { make(1000 + i) }, 500 + i * 7, firstServer: i % 2);
                        if (run.Match.Score.Winner == 0) won++;
                        pointsFor += run.Match.Score.Points(0);
                        pointsAgainst += run.Match.Score.Points(1);
                        seconds += run.Seconds;
                    }
                    wins[(name, d, round)] = won;
                    conceded[(name, d, round)] = pointsAgainst;
                    line += $"  {d.ToString()[0]}{round}: {won}/8 ({pointsFor}-{pointsAgainst}, {seconds / 8 / 60:0.0} min)";
                }
                lines.Add(line);
            }
            Debug.Log(string.Join("\n", lines));
            foreach (var d in new[] { VolleyDifficulty.Easy, VolleyDifficulty.Medium, VolleyDifficulty.Hard })
            {
                Assert.GreaterOrEqual(wins[("perfect", d, 2)], 7, $"a perfect player wins {d}");
                Assert.AreEqual(0, wins[("still", d, 0)], $"standing still never wins ({d})");
            }
            Assert.GreaterOrEqual(wins[("good", VolleyDifficulty.Medium, 2)], 7, "a good player wins Medium");
            Assert.GreaterOrEqual(wins[("good", VolleyDifficulty.Hard, 2)], 5, "and mostly Hard");
            Assert.GreaterOrEqual(wins[("careful", VolleyDifficulty.Easy, 2)], 7, "a careful child wins Easy");
            Assert.GreaterOrEqual(wins[("child", VolleyDifficulty.Easy, 0)], 2, "a child who lets balls go wins some");
            Assert.LessOrEqual(wins[("struggling", VolleyDifficulty.Medium, 0)], 1, "struggling doesn't win Medium");
            // holding one pose to defend is no defence (2026-11-08): a good player who never blocks loses from Easy on; one
            // who always blocks wins Easy (it never tips: "!" = both hands up), concedes far more on Medium, where the pair
            // starts tipping over the blocks, and loses on Hard
            Assert.LessOrEqual(wins[("always low", VolleyDifficulty.Easy, 2)], 2, "always low doesn't win Easy");
            Assert.LessOrEqual(wins[("always low", VolleyDifficulty.Medium, 0)], 1, "nor Medium");
            Assert.AreEqual(0, wins[("always low", VolleyDifficulty.Hard, 0)], "nor Hard");
            Assert.Greater(conceded[("always up", VolleyDifficulty.Medium, 0)], 3 * conceded[("good", VolleyDifficulty.Medium, 0)],
                           "always blocking concedes far more on Medium than reading the hitter");
            Assert.LessOrEqual(wins[("always up", VolleyDifficulty.Medium, 2)], 5, "always blocking doesn't win Medium's final");
            Assert.LessOrEqual(wins[("always up", VolleyDifficulty.Hard, 0)], 2, "nor Hard");
        }
    }
}
