using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Fly.Tests
{
    /// <summary>
    /// Fluturo!'s rules (<see cref="FlyRun"/>), an endless punch 'em up in the air: each mechanic on its own (scripted,
    /// nothing happens by itself): drone swarms, the rush from every side, fist to fist (the faster puncher wins), the
    /// boss, the districts getting harder; then whole runs played by simulated players who punch at different speeds:
    /// the faster one gets further, one who stands still loses in the first district, one who never punches never saves
    /// one. <see cref="Balance"/> prints the numbers.
    /// </summary>
    public sealed class FlyRunTests
    {
        const float Dt = 1f / 60f;

        static FlyRun Scripted(int district = 1)
        {
            var run = new FlyRun(1) { Scripted = true };
            run.Start(district);
            return run;
        }

        /// <summary>A scripted rush with one robot waiting at (x, y, z), arrived and ready.</summary>
        static (FlyRun, Thing) Rush(float x = 0.4f, float y = 0.2f, float z = 6f, int hp = 1, int district = 1)
        {
            var run = Scripted(district);
            run.BeginRush(0);
            var robot = run.SpawnRobot(x, y, z, hp);
            StepUntil(run, () => robot.State == ThingState.Wait && run.Phase == FlyPhase.Rush, 10f);
            return (run, robot);
        }

        /// <summary>Steps until <paramref name="until"/> is true (or the time runs out); returns every event seen.</summary>
        static List<FlyEvent> StepUntil(FlyRun run, System.Func<bool> until, float seconds = 20f, Vector2 steer = default,
                                       System.Func<Vector2> steering = null, System.Func<bool> punching = null)
        {
            var events = new List<FlyEvent>();
            for (float t = 0f; t < seconds && !until(); t += Dt)
            {
                run.Step(Dt, steering != null ? steering() : steer, punching != null && punching());
                events.AddRange(run.Events);
            }
            return events;
        }

        static List<FlyEvent> Punch(FlyRun run, bool right = true)
        {
            run.Step(Dt, Vector2.zero, true, right);
            return run.Events.ToList();
        }

        static bool Has(IEnumerable<FlyEvent> events, FlyEventKind kind) => events.Any(e => e.Kind == kind);

        /// <summary>A player punching <paramref name="perSecond"/> times a second, evenly.</summary>
        static System.Func<bool> Puncher(float perSecond)
        {
            float clock = 0f;
            return () =>
            {
                clock += Dt * perSecond;
                if (clock < 1f) return false;
                clock -= 1f;
                return true;
            };
        }

        // ---------------------------------------------------------------- flying: swarms, stars

        [Test]
        public void TheHeroFlies_DroneSwarmsComeOnTheWay_ThenTheRush()
        {
            var run = new FlyRun(3);
            run.Start();
            var events = StepUntil(run, () => run.Phase == FlyPhase.Stop, 40f);
            Assert.AreEqual(FlyPhase.Stop, run.Phase, "a rush after the flight");
            Assert.AreEqual(run.Settings.FlightMeters, run.Distance, 1f);
            Assert.AreEqual(run.Settings.Swarms, events.Count(e => e.Kind == FlyEventKind.SwarmComes), "the flight's swarms");
            Assert.IsTrue(Has(events, FlyEventKind.RushStarts));
            Assert.IsFalse(run.Things.Any(t => t.Kind is ThingKind.Star or ThingKind.Heart or ThingKind.Drone && t.Alive),
                           "the street is cleared for the rush");
            StepUntil(run, () => run.CurrentSpeed == 0f, 3f);
            Assert.AreEqual(0f, run.CurrentSpeed, "the hero hovers: the street stops");
        }

        [Test]
        public void Drones_ArePunchedAsTheyCome_EachDropsAStar_AWholeSwarmIsABonus()
        {
            var run = Scripted();
            run.SpawnSwarm(FlyRun.Formation.Column, 3, 0f, 0f);
            var events = new List<FlyEvent>();
            for (float t = 0f; t < 8f && run.Things.Any(x => x.Kind == ThingKind.Drone && x.Alive); t += Dt)
            {
                bool punch = run.Things.Any(x => x.Kind == ThingKind.Drone && run.CanPunch(x));
                run.Step(Dt, Vector2.zero, punch);
                events.AddRange(run.Events);
            }
            Assert.AreEqual(3, events.Count(e => e.Kind == FlyEventKind.DroneSmashed), "one punch each, one after another");
            Assert.AreEqual(3 + 3, run.StarsCollected, "a star from each, three more for the whole swarm");
            Assert.IsTrue(Has(events, FlyEventKind.SwarmCleared));
            Assert.AreEqual(1, run.SwarmsCleared);
            Assert.AreEqual(3, run.Combo, "three hits in a row");
        }

        [Test]
        public void ADroneThatGetsThrough_OnlyBumps_TheComboIsLost()
        {
            var run = Scripted();
            run.SpawnSwarm(FlyRun.Formation.Column, 2, 0f, 0f);
            StepUntil(run, () => run.Things.Any(x => x.Kind == ThingKind.Drone && run.CanPunch(x)), 5f);
            Punch(run);
            Assert.AreEqual(1, run.Combo);
            var events = StepUntil(run, () => !run.Things.Any(x => x.Kind == ThingKind.Drone && x.Alive), 6f);
            Assert.IsTrue(Has(events, FlyEventKind.DroneBumped), "the second flew into the hero");
            Assert.AreEqual(run.StartHearts, run.Hearts, "no heart lost to a drone");
            Assert.AreEqual(0, run.Combo);
            Assert.IsFalse(Has(events, FlyEventKind.SwarmCleared));
        }

        [Test]
        public void Stars_AreCollectedByFlyingThroughThem()
        {
            var run = Scripted();
            var on = run.SpawnStar(0.2f, 0.1f);
            run.SpawnStar(-2f, 0f);
            var events = StepUntil(run, () => run.Things.Count == 0, 8f);
            Assert.AreEqual(1, events.Count(e => e.Kind == FlyEventKind.StarCollected), "only the star on the hero's path");
            Assert.AreEqual(on.Id, events.First(e => e.Kind == FlyEventKind.StarCollected).ThingId);
            Assert.AreEqual(5, run.Score);
            Assert.AreEqual(1, run.StarsCollected);
        }

        [Test]
        public void TurningACorner_DropsTheStarsAhead_RobotsAndDronesFollow()
        {
            var run = Scripted();
            var near = run.SpawnStar(0f, 0f);
            near.Position = new Vector3(0f, 0f, 1f);    // already reaching the hero: stays
            var far = run.SpawnStar(0.5f, 0.2f);
            var heart = run.SpawnHeart(0f, 0.3f);
            var robot = run.SpawnRobot(0.4f, 0.1f, 20f);
            run.SpawnSwarm(FlyRun.Formation.Line, 3);
            run.Turned();
            CollectionAssert.Contains(run.Things, near);
            CollectionAssert.DoesNotContain(run.Things, far, "a star over the old street is gone");
            CollectionAssert.DoesNotContain(run.Things, heart);
            CollectionAssert.Contains(run.Things, robot, "robots fly after the hero");
            Assert.AreEqual(3, run.Things.Count(t => t.Kind == ThingKind.Drone), "so do drones");
        }

        // ---------------------------------------------------------------- the rush

        [Test]
        public void TheRush_SendsGroups_ThatChargeInFromSeveralSidesAtOnce()
        {
            var run = new FlyRun(4);
            run.Start(3);   // groups of four
            run.BeginRush();
            var events = StepUntil(run, () => run.Things.Count(t => t.Kind == ThingKind.Robot && !t.Thrower) >= run.Settings.GroupSize, 3f);
            Assert.IsTrue(Has(events, FlyEventKind.GroupComes));
            var fighters = run.Things.Where(t => t.Kind == ThingKind.Robot && !t.Thrower).ToList();
            Assert.AreEqual(run.Settings.GroupSize, fighters.Count);
            int sides = 0;
            if (fighters.Any(t => t.Position.x < -5f)) sides++;
            if (fighters.Any(t => t.Position.x > 5f)) sides++;
            if (fighters.Any(t => t.Position.y > 4f)) sides++;
            if (fighters.Any(t => t.Position.z > 10f)) sides++;
            Assert.GreaterOrEqual(sides, 3, "from the left, the right, above and ahead");
            StepUntil(run, () => false, 2.5f);
            Assert.GreaterOrEqual(run.Things.Count(t => t.Attacking), 3, "several at the hero at the same time");
            Assert.AreEqual(fighters.Count, fighters.Select(t => t.Spot).Distinct().Count(), "each to its own spot");
        }

        [Test]
        public void AFighter_ChargesIn_WindsUpBesideTheHero_ThenStrikes()
        {
            var run = Scripted();
            run.BeginRush(0);
            run.SpawnGroup();
            var fighter = run.Things.First(t => t.Kind == ThingKind.Robot);
            var events = StepUntil(run, () => fighter.State == ThingState.Windup, 5f);
            Assert.AreEqual(ThingState.Windup, fighter.State);
            Assert.IsTrue(run.CanPunch(fighter), "beside the hero: within reach");
            Assert.LessOrEqual(Vector2.Distance(new Vector2(fighter.Position.x, fighter.Position.y), run.Hero), run.SpotRadius + 0.3f);
            events = StepUntil(run, () => run.HitsTaken > 0, 3f);
            Assert.IsTrue(Has(events, FlyEventKind.RobotCharges));
            Assert.Less(run.Hearts, run.StartHearts, "the hero stayed: hit");
        }

        [Test]
        public void SteppingAsideAsItStrikes_DodgesIt()
        {
            var (run, robot) = Rush();
            run.Attack(robot);
            var events = StepUntil(run, () => robot.State == ThingState.Retreat, 6f,
                steering: () => robot.State == ThingState.Lunge ? new Vector2(robot.Target.x > 0f ? -1f : 1f, 0f) : Vector2.zero);
            Assert.IsTrue(Has(events, FlyEventKind.RobotMissed), "it struck at where the hero was");
            Assert.AreEqual(run.StartHearts, run.Hearts);
        }

        [Test]
        public void ARobotOnItsWayIn_CanAlreadyBePunched()
        {
            var (run, robot) = Rush(z: 9f);
            Assert.IsFalse(run.CanPunch(robot), "waiting at its place: out of reach");
            run.Attack(robot);
            StepUntil(run, () => run.CanPunch(robot), 3f);
            Assert.AreEqual(ThingState.Advance, robot.State, "still coming");
            Assert.IsTrue(Has(Punch(run), FlyEventKind.RobotSmashed));
            Assert.AreEqual(10, run.Score);
        }

        [Test]
        public void APunch_GoesToTheRobotAboutToStrike()
        {
            var (run, first) = Rush(-1f, z: 4f);
            var second = run.SpawnRobot(1f, 0f, 4f);
            StepUntil(run, () => second.State == ThingState.Wait, 10f);
            run.Attack(first);
            StepUntil(run, () => first.State == ThingState.Windup && first.StateTime > run.Settings.WindupSeconds * 0.1f, 4f);
            run.Attack(second);
            StepUntil(run, () => second.State == ThingState.Windup || first.State != ThingState.Windup, 4f);
            Assert.AreEqual(ThingState.Windup, first.State, "still winding up");
            Assert.IsTrue(run.CanPunch(first) && run.CanPunch(second), "both in reach, one each side");
            Punch(run, right: true);
            Assert.AreEqual(ThingState.Smashed, first.State, "the one about to strike goes first, though the right fist is on the other one's side");
        }

        [Test]
        public void BothFistsAtOnce_SmashTwoRobots()
        {
            var (run, a) = Rush(-1f);
            var b = run.SpawnRobot(1f, 0f, 6f);
            StepUntil(run, () => b.State == ThingState.Wait, 10f);
            run.Attack(a);
            run.Attack(b);
            StepUntil(run, () => run.CanPunch(a) && run.CanPunch(b), 3f);
            run.Step(Dt, Vector2.zero, true, true, bothHands: true);
            Assert.AreEqual(2, run.Events.Count(e => e.Kind == FlyEventKind.RobotSmashed));
        }

        [Test]
        public void AnArmouredRobot_TakesTwoPunches_ItComesStraightBack()
        {
            var (run, robot) = Rush(hp: 2);
            run.Attack(robot);
            StepUntil(run, () => robot.State == ThingState.Windup, 3f);
            Assert.IsTrue(Has(Punch(run), FlyEventKind.RobotHit), "the first punch knocks it back");
            Assert.AreEqual(ThingState.Stagger, robot.State);
            StepUntil(run, () => robot.StateTime > 0.2f, 1f);
            Assert.IsTrue(run.CanPunch(robot), "still within reach for a quick second punch");
            Assert.IsTrue(Has(Punch(run, right: false), FlyEventKind.RobotSmashed));
            Assert.AreEqual(5 + 20 + 3, run.Score, "a hit, an armoured robot out, and a combo of two");
        }

        [Test]
        public void AThrowersSpark_PunchedBack_KnocksOutTheThrower()
        {
            var (run, thrower) = Rush(z: 7f);
            thrower.Thrower = true;
            var spark = run.ThrowSpark(thrower);
            StepUntil(run, () => run.CanPunch(spark), 4f);
            Assert.IsTrue(Has(Punch(run), FlyEventKind.FireballReturned));
            var events = StepUntil(run, () => thrower.State == ThingState.Smashed, 4f);
            Assert.IsTrue(Has(events, FlyEventKind.RobotSmashed), "its own spark knocked it out");
        }

        [Test]
        public void BeatingEveryRobot_EndsTheRush_ThenTheChampionComes()
        {
            var run = new FlyRun(2);
            run.Start();
            run.BeginRush();
            int size = run.RushSize;
            var events = StepUntil(run, () => run.Phase == FlyPhase.ClashIntro, 60f,
                punching: () => run.Things.Any(x => x.Kind == ThingKind.Robot && run.CanPunch(x)));
            Assert.AreEqual(FlyPhase.ClashIntro, run.Phase);
            Assert.IsTrue(Has(events, FlyEventKind.RushCleared));
            Assert.AreEqual(size, run.RobotsSmashed, "every robot of the rush");
            Assert.AreEqual(run.StartHearts, run.Hearts, "punching each one as it came, nobody got through");
            Assert.AreEqual(1, run.CleanRushes);
            Assert.IsTrue(Has(events, FlyEventKind.ClashStarts));
        }

        // ---------------------------------------------------------------- the clash: the faster puncher wins

        [Test]
        public void TheClash_TheFasterPuncherWins()
        {
            var run = new FlyRun(5);
            run.Start();
            run.BeginClash(false);
            var events = StepUntil(run, () => run.Phase == FlyPhase.Cleared, 20f, punching: Puncher(4f));
            Assert.IsTrue(Has(events, FlyEventKind.ClashGo));
            Assert.IsTrue(Has(events, FlyEventKind.ClashWon), "4 punches a second beat the champion's 1.7");
            Assert.AreEqual(run.StartHearts, run.Hearts);
            Assert.AreEqual(1, run.ClashesWon);
            Assert.AreEqual(10, run.StarsCollected, "a knockout is worth ten stars");
            Assert.Less(run.QuickestClash, 10f);
            Assert.Greater(events.Count(e => e.Kind == FlyEventKind.ClashRobotPunch), 3, "the champion punched back all along");
        }

        [Test]
        public void TheClash_TheSlowerPuncherLosesHearts_TheBarStartsAgain()
        {
            var run = new FlyRun(5);
            run.Start();
            run.BeginClash(false);
            var events = StepUntil(run, () => run.HitsTaken > 0, 20f, punching: Puncher(0.8f));
            Assert.IsTrue(Has(events, FlyEventKind.ClashLost), "the champion was faster");
            Assert.AreEqual(run.StartHearts - 1, run.Hearts);
            Assert.AreEqual(0f, run.Clash.Bar, 1e-4f, "back to the middle");
            Assert.AreEqual(FlyPhase.Clash, run.Phase, "and on");
        }

        [Test]
        public void ANearlyEvenClash_TheChampionTires_SoItEnds()
        {
            var run = new FlyRun(6);
            run.Start(5);
            run.BeginClash(false);
            float rate = run.Clash.Rate;
            var events = StepUntil(run, () => run.Phase is FlyPhase.Cleared or FlyPhase.Lost, 60f, punching: Puncher(rate * 0.85f));
            Assert.IsTrue(Has(events, FlyEventKind.ClashWon), $"won in {run.Clash.Time:0} s");
            Assert.Less(run.Clash.CurrentRate, rate, "tired by then");
        }

        [Test]
        public void ASavedDistrict_GivesAHeartBack_AndTheNextIsHarder()
        {
            var run = new FlyRun(7);
            run.Start();
            run.BeginClash(false);
            StepUntil(run, () => run.HitsTaken > 0, 20f, punching: Puncher(0.8f));   // a heart lost first
            int hearts = run.Hearts;
            var events = StepUntil(run, () => run.District == 2, 30f, punching: Puncher(5f));
            Assert.IsTrue(Has(events, FlyEventKind.DistrictStarted));
            Assert.AreEqual(hearts + 1, run.Hearts, "a heart back");
            Assert.AreEqual(FlyPhase.Flight, run.Phase);
            Assert.Greater(run.Settings.FlightSpeed, DistrictSettings.For(1).FlightSpeed);
            Assert.Greater(run.Settings.ClashRate, DistrictSettings.For(1).ClashRate);
        }

        [Test]
        public void Districts_GetHarderForEver_WithinLimits_ABossEveryThird()
        {
            for (int d = 1; d < 40; d++)
            {
                DistrictSettings a = DistrictSettings.For(d), b = DistrictSettings.For(d + 1);
                Assert.GreaterOrEqual(b.FlightSpeed, a.FlightSpeed, $"district {d}");
                Assert.GreaterOrEqual(b.GroupSize, a.GroupSize, $"district {d}");
                Assert.GreaterOrEqual(b.Groups, a.Groups, $"district {d}");
                Assert.GreaterOrEqual(b.ChargeSpeed, a.ChargeSpeed, $"district {d}");
                Assert.LessOrEqual(b.WindupSeconds, a.WindupSeconds, $"district {d}");
                Assert.GreaterOrEqual(b.ClashRate - (b.Boss ? 0.4f : 0f), a.ClashRate - (a.Boss ? 0.4f : 0f), $"district {d}");
                Assert.AreEqual(d % 3 == 0, a.Boss, $"district {d}");
                Assert.LessOrEqual(a.ClashRate, 4.6f, "a child's flailing can still win");
                Assert.GreaterOrEqual(a.WindupSeconds, 0.45f, "time to react");
                Assert.LessOrEqual(a.GroupSize, 7);
            }
        }

        // ---------------------------------------------------------------- the boss

        [Test]
        public void TheBoss_FireballsPunchedBack_ThenTheFinalClash()
        {
            var run = Scripted(3);
            run.BeginBoss();
            var events = StepUntil(run, () => run.Phase == FlyPhase.Boss, 5f);
            Assert.IsTrue(Has(events, FlyEventKind.BossAppears));
            int hp = run.Boss.Hp;
            Assert.AreEqual(DistrictSettings.For(3).BossHp, hp);
            for (int i = 0; i < hp; i++)
            {
                var ball = run.Throw(new Vector3(run.Hero.x, run.Hero.y, 0f));
                StepUntil(run, () => run.CanPunch(ball), 5f);
                Assert.IsTrue(Has(Punch(run), FlyEventKind.FireballReturned), $"fireball {i + 1} punched back");
                events = StepUntil(run, () => ball.State == ThingState.Gone, 5f);
                Assert.IsTrue(Has(events, FlyEventKind.BossHit), $"fireball {i + 1} hit the boss");
            }
            Assert.IsTrue(Has(events, FlyEventKind.ClashStarts), "its fireballs beaten, it comes face to face");
            Assert.IsTrue(run.Clash.WithBoss);
            events = StepUntil(run, () => run.Phase == FlyPhase.Cleared, 20f, punching: Puncher(4f));
            Assert.IsTrue(Has(events, FlyEventKind.ClashWon));
            Assert.IsTrue(Has(events, FlyEventKind.BossDefeated));
            Assert.AreEqual(1, run.BossesBeaten);
            Assert.AreEqual(25, run.StarsCollected);
            StepUntil(run, () => run.Phase == FlyPhase.Flight, 5f);
            Assert.AreEqual(4, run.District, "on to the next district");
            Assert.IsNull(run.Boss);
        }

        [Test]
        public void AFireball_HurtsAHeroWhoStays_AndMissesOneWhoDodges()
        {
            var run = Scripted(3);
            run.BeginBoss();
            StepUntil(run, () => run.Phase == FlyPhase.Boss, 5f);
            var ball = run.Throw(Vector3.zero);
            var events = StepUntil(run, () => ball.State == ThingState.Gone, 5f);
            Assert.IsTrue(Has(events, FlyEventKind.FireballHitHero));
            Assert.AreEqual(run.StartHearts - 1, run.Hearts);

            ball = run.Throw(Vector3.zero);
            events = StepUntil(run, () => ball.State == ThingState.Gone, 5f, new Vector2(-1f, 0f));
            Assert.IsFalse(Has(events, FlyEventKind.FireballHitHero), "dodged");
        }

        // ---------------------------------------------------------------- hearts, records, new enemies

        [Test]
        public void HitsInARow_BuildACombo_GettingHurtBreaksIt()
        {
            var (run, a) = Rush(-1f);
            var b = run.SpawnRobot(1f, 0f, 6f);
            var c = run.SpawnRobot(0f, 0.5f, 7f);
            StepUntil(run, () => b.State == ThingState.Wait && c.State == ThingState.Wait, 10f);
            foreach (var robot in new[] { a, b })
            {
                run.Attack(robot);
                StepUntil(run, () => run.CanPunch(robot), 3f);
                Punch(run);
            }
            Assert.AreEqual(2, run.Combo);
            Assert.AreEqual(10 + 13, run.Score, "the second smash in a row earns 3 more");
            run.Attack(c);
            StepUntil(run, () => c.State == ThingState.Retreat, 6f);
            Assert.AreEqual(0, run.Combo, "hurt: the combo is over");
            Assert.AreEqual(2, run.BestCombo);
        }

        [Test]
        public void AHeart_GivesOneBack_UpToTheStart()
        {
            var (run, robot) = Rush();
            run.Attack(robot);
            StepUntil(run, () => robot.State == ThingState.Retreat, 6f);
            Assert.AreEqual(run.StartHearts - 1, run.Hearts);
            var flying = Scripted();
            flying.SpawnHeart(0f, 0f);
            StepUntil(flying, () => flying.Things.Count == 0, 8f);
            Assert.AreEqual(flying.StartHearts, flying.Hearts, "never more than the start");
        }

        [Test]
        public void LosingTheLastHeart_EndsTheRun()
        {
            var run = Scripted();
            run.BeginClash(false);
            StepUntil(run, () => run.Phase == FlyPhase.Clash, 5f);
            var events = new List<FlyEvent>();
            for (int i = 0; i < 200 && !run.Over; i++)
            {
                run.ChampionPunch();   // (what it causes shows in the next step's events)
                events.AddRange(StepUntil(run, () => run.Clash.Recover <= 0f && run.Events.Count == 0, 3f));
            }
            Assert.AreEqual(0, run.Hearts);
            Assert.AreEqual(FlyPhase.Lost, run.Phase);
            Assert.IsTrue(run.Over);
            Assert.AreEqual(run.StartHearts, events.Count(e => e.Kind == FlyEventKind.ClashLost));
            Assert.IsTrue(Has(events, FlyEventKind.Lost));
        }

        [Test]
        public void PassingTheRecord_IsAnnouncedOnce()
        {
            var (run, a) = Rush(-1f);
            run.Record = 15;
            var b = run.SpawnRobot(1f, 0f, 6f);
            StepUntil(run, () => b.State == ThingState.Wait, 10f);
            var events = new List<FlyEvent>();
            foreach (var robot in new[] { a, b })
            {
                run.Attack(robot);
                StepUntil(run, () => run.CanPunch(robot), 3f);
                events.AddRange(Punch(run));
                events.AddRange(StepUntil(run, () => false, 0.5f));
            }
            Assert.AreEqual(1, events.Count(e => e.Kind == FlyEventKind.NewRecord), $"score {run.Score} passed 15 once");
        }

        [Test]
        public void NewKindsOfRobots_ComeDistrictByDistrict_EachAnnouncedOnce()
        {
            var run = new FlyRun(8);
            run.Start();
            var bot = Perfect(8);
            var met = new List<(EnemyKind kind, int district)>();
            for (float t = 0f; t < 600f && run.District <= 4 && !run.Over; t += Dt)
            {
                var (steer, punch) = bot.Decide(run);
                run.Step(Dt, steer, punch);
                foreach (var e in run.Events)
                    if (e.Kind == FlyEventKind.NewEnemy) met.Add(((EnemyKind)(int)e.Value, run.District));
            }
            Assert.AreEqual(met.Count, met.Select(m => m.kind).Distinct().Count(), "each kind once");
            var first = met.ToDictionary(m => m.kind, m => m.district);
            Assert.AreEqual(1, first[EnemyKind.Drone]);
            Assert.AreEqual(1, first[EnemyKind.Robot]);
            Assert.AreEqual(1, first[EnemyKind.Champion]);
            Assert.AreEqual(2, first[EnemyKind.Armoured]);
            Assert.AreEqual(3, first[EnemyKind.Thrower]);
            Assert.AreEqual(3, first[EnemyKind.Boss]);
            Assert.AreEqual(4, first[EnemyKind.Dart]);
        }

        // ---------------------------------------------------------------- steering

        [Test]
        public void Steering_FollowsTheBody()
        {
            Assert.AreEqual(Vector2.zero, FlyRun.Steer(0.03f, 0.02f, 0f), "standing where calibrated: the middle");
            Assert.AreEqual(1f, FlyRun.Steer(0.9f, 0f, 0f).x, 1e-4f, "a full step right: the right edge");
            Assert.AreEqual(-1f, FlyRun.Steer(-1.5f, 0f, 0f).x, 1e-4f);
            Assert.AreEqual(0.5f, FlyRun.Steer(0.455f, 0f, 0f).x, 0.02f, "half a step: half way");
            Assert.AreEqual(-1f, FlyRun.Steer(0f, -0.6f, 0f).y, 1e-4f, "ducking: the bottom");
            Assert.AreEqual(1f, FlyRun.Steer(0f, 0f, 1f).y, 1e-4f, "a jump: the top");
            Assert.AreEqual(0f, FlyRun.Steer(0f, 0.2f, 0f).y, 1e-4f, "standing on tiptoe isn't flying up");
        }

        // ---------------------------------------------------------------- whole runs

        /// <summary>A simulated player. It notices a robot coming (or a fireball, a drone) <see cref="Reaction"/> s after it
        /// starts. While one is in reach it tries a punch every <see cref="PunchEvery"/> s, each try working
        /// <see cref="Accuracy"/> of the time (late, missed, not seen by the camera). In a clash it punches
        /// <see cref="MashRate"/> times a second. When a robot strikes or a fireball is nearly there it tries to dodge,
        /// managing <see cref="DodgeSkill"/> of the time. While flying it goes for the drones and the stars.</summary>
        sealed class Bot
        {
            public float Reaction = 0.15f, Accuracy = 1f, DodgeSkill = 1f, PunchEvery = 0.25f, MashRate = 5f;
            public bool Moves = true, Punches = true;
            public bool Dodges { get => DodgeSkill > 0f; set => DodgeSkill = value ? 1f : 0f; }
            readonly System.Random _rng;
            readonly Dictionary<int, float> _seen = new Dictionary<int, float>();
            readonly Dictionary<int, bool> _willDodge = new Dictionary<int, bool>();
            readonly Dictionary<int, float> _comingAt = new Dictionary<int, float>();
            Vector2 _steer;
            float _time, _nextTry, _mash;

            public Bot(int seed) => _rng = new System.Random(seed * 31 + 7);

            public (Vector2, bool) Decide(FlyRun run)
            {
                _time += Dt;
                if (run.Phase is FlyPhase.Clash or FlyPhase.ClashIntro)
                {
                    // fist to fist: as fast as it can (a little uneven)
                    if (!Punches || run.Phase == FlyPhase.ClashIntro) return (_steer, false);
                    _mash += Dt * MashRate * (0.85f + 0.3f * (float)_rng.NextDouble());
                    if (_mash < 1f) return (_steer, false);
                    _mash -= 1f;
                    return (_steer, true);
                }
                Thing threat = null;
                foreach (var t in run.Things)
                {
                    bool coming = t.Attacking || t.Dangerous || t.Kind == ThingKind.Drone && t.Alive && t.Position.z < 12f;
                    if (!coming)
                    {
                        _seen.Remove(t.Id);
                        continue;
                    }
                    if (!_seen.ContainsKey(t.Id)) _seen[t.Id] = _time;
                    if (_time - _seen[t.Id] < Reaction) continue;
                    if (!_willDodge.ContainsKey(t.Id)) _willDodge[t.Id] = _rng.NextDouble() < DodgeSkill;
                    if (threat == null || t.Position.z < threat.Position.z) threat = t;
                }

                var want = Vector2.zero;
                bool punch = false;
                bool canHit = threat != null && run.CanPunch(threat);
                if (Punches && canHit && _time >= _nextTry)
                {
                    _nextTry = _time + PunchEvery;
                    punch = _rng.NextDouble() < Accuracy;
                }
                if (threat != null && threat.Kind != ThingKind.Drone && !punch && threat.Dangerous &&
                    (threat.Kind == ThingKind.Robot || threat.Position.z < 2f))
                {
                    // it's striking (or nearly here): after a reaction time, away from where it's going (or frozen)
                    if (!_comingAt.ContainsKey(threat.Id)) _comingAt[threat.Id] = _time;
                    if (_willDodge[threat.Id] && _time - _comingAt[threat.Id] >= Reaction * 0.5f)
                    {
                        var target = new Vector2(threat.Target.x / run.HalfWidth, threat.Target.y / run.HalfHeight);
                        want = new Vector2(target.x > 0f ? target.x - 1.4f : target.x + 1.4f, 0f);
                    }
                    else want = _steer;
                }
                else if (run.Phase == FlyPhase.Flight)
                {
                    // flying: toward the nearest drone ahead, else the stars
                    Thing aim = null;
                    foreach (var t in run.Things)
                        if ((t.Kind == ThingKind.Drone && t.Alive || t.Kind is ThingKind.Star or ThingKind.Heart) && t.Position.z > 0.5f &&
                            t.Position.z < 20f && (aim == null || t.Position.z < aim.Position.z))
                            aim = t;
                    if (aim != null) want = new Vector2(aim.Position.x / run.HalfWidth, aim.Position.y / run.HalfHeight);
                }
                if (!Moves) want = Vector2.zero;
                // a person doesn't jump to the new place at once
                _steer = Vector2.Lerp(_steer, want, 1f - Mathf.Exp(-Dt / Mathf.Max(0.05f, Reaction)));
                return (_steer, punch);
            }
        }

        sealed class Outcome
        {
            public int District, Score, HitsTaken, BestCombo, ClashesWon, ClashesLost, RobotsSmashed, Stars;
            public float Minutes;
        }

        static Outcome Play(Bot bot, int seed, float maxMinutes = 15f)
        {
            var run = new FlyRun(seed);
            run.Start();
            float t = 0f;
            int clashesLost = 0;
            while (!run.Over && t < maxMinutes * 60f)
            {
                var (steer, punch) = bot.Decide(run);
                run.Step(Dt, steer, punch);
                foreach (var e in run.Events)
                    if (e.Kind == FlyEventKind.ClashLost) clashesLost++;
                t += Dt;
            }
            return new Outcome
            {
                District = run.District, Score = run.Score, HitsTaken = run.HitsTaken, BestCombo = run.BestCombo,
                ClashesWon = run.ClashesWon, ClashesLost = clashesLost, RobotsSmashed = run.RobotsSmashed,
                Stars = run.StarsCollected, Minutes = t / 60f,
            };
        }

        static Bot Perfect(int seed) => new Bot(seed) { MashRate = 5f };
        static Bot Good(int seed) => new Bot(seed) { Reaction = 0.3f, Accuracy = 0.85f, DodgeSkill = 0.6f, PunchEvery = 0.35f, MashRate = 4f };
        static Bot Kid(int seed) => new Bot(seed) { Reaction = 0.45f, Accuracy = 0.65f, DodgeSkill = 0.5f, PunchEvery = 0.4f, MashRate = 3f };
        static Bot Struggling(int seed) => new Bot(seed) { Reaction = 0.6f, Accuracy = 0.5f, DodgeSkill = 0.3f, PunchEvery = 0.6f, MashRate = 2.2f };
        static Bot Statue(int seed) => new Bot(seed) { Moves = false, Punches = false, Dodges = false };
        static Bot Dodger(int seed) => new Bot(seed) { Punches = false };

        [Test]
        public void APlayerWhoStandsStill_LosesInTheFirstDistrict()
        {
            for (int seed = 0; seed < 6; seed++)
            {
                var o = Play(Statue(seed), seed);
                Assert.AreEqual(1, o.District, $"seed {seed}");
                Assert.Less(o.Minutes, 1.5f, $"seed {seed}: the robots keep coming, so it ends soon");
            }
        }

        [Test]
        public void APlayerWhoNeverPunches_NeverSavesADistrict()
        {
            for (int seed = 0; seed < 4; seed++)
                Assert.AreEqual(1, Play(Dodger(seed), seed, maxMinutes: 5f).District, $"seed {seed}");
        }

        [Test]
        public void TheFasterYouPunch_TheFurtherYouGet()
        {
            const int games = 12;
            double Mean(System.Func<int, Bot> make) => Enumerable.Range(0, games).Select(s => Play(make(s), s, 12f)).Average(o => o.District);
            double perfect = Mean(Perfect), good = Mean(Good), kid = Mean(Kid), struggling = Mean(Struggling);
            Assert.Greater(perfect, good, "perfect beyond good");
            Assert.Greater(good, kid, "good beyond child");
            Assert.Greater(kid, struggling, "child beyond struggling");
            Assert.GreaterOrEqual(struggling, 1.5, "a struggling player still saves a district now and then");
            Assert.GreaterOrEqual(kid, 3.5, "a child gets a few districts in");
        }

        [Test]
        public void AChildLikePlayer_PlaysAFewMinutes_AndSeesAKnockout()
        {
            var kid = Enumerable.Range(0, 12).Select(seed => Play(Kid(seed), seed)).ToList();
            Assert.GreaterOrEqual(kid.Count(o => o.ClashesWon >= 1), 11, "nearly every child wins a clash");
            double minutes = kid.Average(o => o.Minutes);
            Assert.That(minutes, Is.InRange(1.5, 10.0), "a run lasts a few minutes");
        }

        [Test]
        public void Balance()
        {
            foreach (var (name, make) in new (string, System.Func<int, Bot>)[]
                     {
                         ("perfect (0.15 s, 4 punches/s, clash 5/s)", Perfect),
                         ("good (0.3 s, 3/s, 85 % land, clash 4/s)", Good),
                         ("child (0.45 s, 2.5/s, 65 % land, clash 3/s)", Kid),
                         ("struggling (0.6 s, 1.7/s, 50 %, clash 2.2/s)", Struggling),
                         ("never punches, dodges", Dodger), ("stands still", Statue),
                     })
            {
                var games = Enumerable.Range(0, 20).Select(seed => Play(make(seed), seed, 15f)).ToList();
                var districts = games.Select(g => g.District).OrderBy(d => d).ToList();
                TestContext.WriteLine($"{name,-46} district mean {districts.Average():0.0} (median {districts[10]}, {districts[0]}-{districts[19]}), " +
                                      $"clashes won {games.Average(g => g.ClashesWon):0.0} lost {games.Average(g => g.ClashesLost):0.0}, " +
                                      $"robots {games.Average(g => g.RobotsSmashed):0}, stars {games.Average(g => g.Stars):0}, " +
                                      $"best combo {games.Average(g => g.BestCombo):0}, score {games.Average(g => g.Score):0}, " +
                                      $"minutes {games.Average(g => g.Minutes):0.0}");
            }
        }
    }
}
