using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Fly.Tests
{
    /// <summary>
    /// Fluturo!'s rules (<see cref="FlyRun"/>): each mechanic on its own (scripted, nothing spawns by itself), then whole
    /// games played by simulated players: a perfect one wins all three levels, one who stands still loses in the first,
    /// one who only dodges can't beat a boss, a child-like one (slow to react, a third of its punches late, dodges half
    /// the time) usually wins, and a struggling one clears a level or two. <see cref="Balance"/> prints the numbers
    /// (2026-10-10: perfect 20/20, good 20/20, child 17/20, struggling 5/20 wins; about 3 minutes a game).
    /// </summary>
    public sealed class FlyRunTests
    {
        const float Dt = 1f / 60f;

        static FlyRun Scripted(int level = 1)
        {
            var run = new FlyRun(1) { Scripted = true };
            run.Start(level);
            return run;
        }

        /// <summary>Steps until <paramref name="until"/> is true (or the time runs out); returns every event seen.</summary>
        static List<FlyEvent> StepUntil(FlyRun run, System.Func<bool> until, float seconds = 20f, Vector2 steer = default,
                                       System.Func<Vector2> steering = null)
        {
            var events = new List<FlyEvent>();
            for (float t = 0f; t < seconds && !until(); t += Dt)
            {
                run.Step(Dt, steering != null ? steering() : steer, false);
                events.AddRange(run.Events);
            }
            return events;
        }

        static bool Has(IEnumerable<FlyEvent> events, FlyEventKind kind) => events.Any(e => e.Kind == kind);

        // ---------------------------------------------------------------- robots

        [Test]
        public void Robot_FliesIn_Hovers_ThenCharges_AndHurtsAHeroWhoStays()
        {
            var run = Scripted();
            var robot = run.SpawnRobot(0.3f, 0.2f);
            StepUntil(run, () => robot.State == ThingState.Hover);
            Assert.AreEqual(ThingState.Hover, robot.State, "it stopped in front of the hero");
            Assert.AreEqual(run.HoverDistance, robot.Position.z, 0.1f);
            var events = StepUntil(run, () => robot.State == ThingState.Gone, 5f);
            Assert.IsTrue(Has(events, FlyEventKind.RobotCharges), "it charged");
            Assert.IsTrue(Has(events, FlyEventKind.RobotHitHero), "and hit the hero, who didn't move");
            Assert.AreEqual(run.StartHearts - 1, run.Hearts);
        }

        [Test]
        public void MovingAwayWhileARobotCharges_DodgesIt()
        {
            var run = Scripted();
            var robot = run.SpawnRobot(0f, 0f, 6f);
            bool charging = false;
            var events = StepUntil(run, () => robot.State is ThingState.Gone or ThingState.Smashed, 10f,
                steering: () => (charging = charging || robot.State == ThingState.Lunge) ? new Vector2(1f, 0f) : Vector2.zero);
            Assert.IsTrue(Has(events, FlyEventKind.RobotMissed), "it charged past");
            Assert.AreEqual(run.StartHearts, run.Hearts);
        }

        [Test]
        public void Punch_SmashesTheRobotInReach_ElseItsAWhiff()
        {
            var run = Scripted();
            run.Step(Dt, Vector2.zero, punch: true);
            Assert.IsTrue(Has(run.Events, FlyEventKind.Whiff), "nothing to hit");

            var robot = run.SpawnRobot(0.6f, 0.4f, 6f);
            StepUntil(run, () => robot.State == ThingState.Hover);
            Assert.IsTrue(run.CanPunch(robot), "a hovering robot this close is within reach");
            run.Step(Dt, Vector2.zero, punch: true);
            Assert.IsTrue(Has(run.Events, FlyEventKind.RobotSmashed));
            Assert.AreEqual(ThingState.Smashed, robot.State);
            Assert.IsTrue(robot.Punched);
            Assert.AreEqual(10, run.Score);
            Assert.AreEqual(1, run.RobotsSmashed);
        }

        [Test]
        public void ARobotTooFarToTheSide_IsOutOfReach_UntilTheHeroFliesOver()
        {
            var run = Scripted();
            var robot = run.SpawnRobot(2f, 0f, 6f);
            StepUntil(run, () => robot.State == ThingState.Hover);
            Assert.IsFalse(run.CanPunch(robot), "2 m to the side is too far");
            StepUntil(run, () => run.CanPunch(robot), 2f, new Vector2(1f, 0f));
            Assert.IsTrue(run.CanPunch(robot), "flying toward it brings it within reach");
        }

        [Test]
        public void APunch_HitsTheNearestRobot_PreferringTheFistsSide()
        {
            var run = Scripted();
            var far = run.SpawnRobot(-0.5f, 0f, 3.4f);
            var near = run.SpawnRobot(0.5f, 0f, 3.0f);
            run.Step(Dt, Vector2.zero, punch: true, rightHand: true);
            Assert.AreEqual(ThingState.Smashed, near.State, "the right fist takes the nearer one, on its side");
            Assert.AreNotEqual(ThingState.Smashed, far.State);

            var run2 = Scripted();
            var left = run2.SpawnRobot(-0.6f, 0f, 3.2f);
            var right = run2.SpawnRobot(0.6f, 0f, 3.0f);
            run2.Step(Dt, Vector2.zero, punch: true, rightHand: false);
            Assert.AreEqual(ThingState.Smashed, left.State, "a near tie goes to the punching fist's side");
            Assert.AreNotEqual(ThingState.Smashed, right.State);
        }

        // ---------------------------------------------------------------- stars and hearts

        [Test]
        public void Stars_AreCollectedByFlyingThroughThem()
        {
            var run = Scripted();
            var on = run.SpawnStar(0.2f, 0.1f);
            var off = run.SpawnStar(-2f, 0f);
            var events = StepUntil(run, () => run.Things.Count == 0, 6f);
            Assert.AreEqual(1, events.Count(e => e.Kind == FlyEventKind.StarCollected), "only the star on the hero's path");
            Assert.AreEqual(on.Id, events.First(e => e.Kind == FlyEventKind.StarCollected).ThingId);
            Assert.AreEqual(5, run.Score);
        }

        [Test]
        public void AHeart_GivesOneBack_UpToTheStart()
        {
            var run = Scripted();
            var robot = run.SpawnRobot(0f, 0f, 6f);
            StepUntil(run, () => robot.State == ThingState.Gone);
            Assert.AreEqual(run.StartHearts - 1, run.Hearts);
            run.SpawnHeart(0f, 0f);
            run.SpawnHeart(0f, 0f, 2f);
            StepUntil(run, () => run.Things.Count == 0, 6f);
            Assert.AreEqual(run.StartHearts, run.Hearts, "two hearts, but never more than the start");
        }

        // ---------------------------------------------------------------- the boss

        [Test]
        public void FireballsPunchedBack_HurtTheBoss_AndBeatingItClearsTheLevel()
        {
            var run = Scripted();
            run.SpawnRobot(0f, 0f, 6f);
            StepUntil(run, () => run.Hearts < run.StartHearts);   // lose one heart first
            run.BeginBoss();
            var events = StepUntil(run, () => run.Phase == FlyPhase.Boss, 5f);
            Assert.IsTrue(Has(events, FlyEventKind.BossAppears));
            int hp = run.Boss.Hp;
            Assert.AreEqual(LevelSettings.For(1).BossHp, hp);
            for (int i = 0; i < hp; i++)
            {
                var ball = run.Throw(new Vector3(run.Hero.x, run.Hero.y, 0f));
                StepUntil(run, () => run.CanPunch(ball), 5f);
                run.Step(Dt, Vector2.zero, punch: true);
                Assert.IsTrue(Has(run.Events, FlyEventKind.FireballReturned), $"fireball {i + 1} punched back");
                events = StepUntil(run, () => ball.State == ThingState.Gone, 5f);
                Assert.IsTrue(Has(events, FlyEventKind.BossHit), $"fireball {i + 1} hit the boss");
            }
            Assert.IsTrue(Has(events, FlyEventKind.BossDefeated));
            Assert.AreEqual(FlyPhase.LevelClear, run.Phase);
            Assert.AreEqual(run.StartHearts, run.Hearts, "hearts filled up again");
            StepUntil(run, () => run.Phase == FlyPhase.Flight, 6f);
            Assert.AreEqual(2, run.Level, "on to level 2");
        }

        [Test]
        public void AFireball_HurtsAHeroWhoStays_AndMissesOneWhoDodges()
        {
            var run = Scripted();
            run.BeginBoss();
            StepUntil(run, () => run.Phase == FlyPhase.Boss, 5f);
            var ball = run.Throw(Vector3.zero);
            var events = StepUntil(run, () => ball.State == ThingState.Gone, 5f);
            Assert.IsTrue(Has(events, FlyEventKind.FireballHitHero));
            Assert.AreEqual(run.StartHearts - 1, run.Hearts);

            ball = run.Throw(Vector3.zero);
            events = StepUntil(run, () => ball.State == ThingState.Gone, 5f, new Vector2(-1f, 0f));
            Assert.IsFalse(Has(events, FlyEventKind.FireballHitHero), "dodged");
            Assert.AreEqual(run.StartHearts - 1, run.Hearts);
        }

        [Test]
        public void BeatingTheThirdBoss_WinsTheGame()
        {
            var run = Scripted(level: 3);
            run.BeginBoss();
            StepUntil(run, () => run.Phase == FlyPhase.Boss, 5f);
            var events = new List<FlyEvent>();
            for (int i = 0; i < 20 && !run.Over; i++)
            {
                var ball = run.Throw(new Vector3(run.Hero.x, run.Hero.y, 0f));
                StepUntil(run, () => run.CanPunch(ball), 5f);
                run.Step(Dt, Vector2.zero, punch: true);
                events.AddRange(StepUntil(run, () => ball.State == ThingState.Gone, 5f));
            }
            Assert.AreEqual(FlyPhase.Won, run.Phase);
            Assert.IsTrue(run.Over);
            Assert.IsTrue(Has(events, FlyEventKind.Won));
            Assert.AreEqual(LevelSettings.For(3).BossHp * 15 + 300 + 50, run.Score, "returns, the boss, the no-hit bonus");
        }

        [Test]
        public void LosingTheLastHeart_LosesTheGame()
        {
            var run = Scripted();
            var events = new List<FlyEvent>();
            for (int i = 0; i < run.StartHearts; i++)
            {
                var robot = run.SpawnRobot(0f, 0f, 6f);
                events.AddRange(StepUntil(run, () => robot.State == ThingState.Gone));
            }
            Assert.AreEqual(0, run.Hearts);
            Assert.AreEqual(FlyPhase.Lost, run.Phase);
            Assert.IsTrue(Has(events, FlyEventKind.Lost));
            var more = run.SpawnRobot(0f, 0f, 6f);
            StepUntil(run, () => more.State == ThingState.Gone);
            Assert.AreEqual(0, run.Hearts, "nothing more happens once the game is lost");
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

        // ---------------------------------------------------------------- whole games

        /// <summary>A simulated player. It notices a thing <see cref="Reaction"/> s after it appears and lines up with the
        /// most urgent robot or fireball. While it's in reach it tries a punch every half second, and each try works
        /// <see cref="Accuracy"/> of the time (late, missed, not seen by the camera), so a robot hovering for two seconds
        /// is rarely missed but a fast fireball sometimes is. Whatever still comes at it, it tries to dodge, managing
        /// <see cref="DodgeSkill"/> of the time (else it freezes). Between threats it flies to the stars.</summary>
        sealed class Bot
        {
            public float Reaction = 0.15f, Accuracy = 1f, DodgeSkill = 1f;
            public bool Moves = true, Punches = true;
            public bool Dodges { get => DodgeSkill > 0f; set => DodgeSkill = value ? 1f : 0f; }
            readonly System.Random _rng;
            readonly Dictionary<int, float> _seen = new Dictionary<int, float>();
            readonly Dictionary<int, bool> _willDodge = new Dictionary<int, bool>();
            readonly Dictionary<int, float> _comingAt = new Dictionary<int, float>(), _nextTry = new Dictionary<int, float>();
            Vector2 _steer;
            float _time, _punchCooldown;

            public Bot(int seed) => _rng = new System.Random(seed * 31 + 7);

            public (Vector2, bool) Decide(FlyRun run)
            {
                _time += Dt;
                _punchCooldown -= Dt;
                Thing threat = null;
                foreach (var t in run.Things)
                {
                    if (!_seen.ContainsKey(t.Id)) _seen[t.Id] = _time;
                    if (!t.Dangerous || _time - _seen[t.Id] < Reaction || t.Position.z > 18f) continue;
                    if (!_willDodge.ContainsKey(t.Id)) _willDodge[t.Id] = _rng.NextDouble() < DodgeSkill;
                    if (threat == null || t.Position.z < threat.Position.z) threat = t;
                }

                var want = _steer;
                bool punch = false;
                if (threat != null)
                {
                    var spot = new Vector2(threat.Position.x / run.HalfWidth, threat.Position.y / run.HalfHeight);
                    if (Punches && threat.Kind == ThingKind.Robot && threat.State != ThingState.Lunge) want = spot;
                    if (Punches && run.CanPunch(threat) && _punchCooldown <= 0f)
                    {
                        if (!_nextTry.ContainsKey(threat.Id)) _nextTry[threat.Id] = _time;
                        if (_time >= _nextTry[threat.Id])
                        {
                            _nextTry[threat.Id] = _time + 0.5f;
                            if (_rng.NextDouble() < Accuracy)
                            {
                                punch = true;
                                _punchCooldown = 0.3f;
                            }
                        }
                    }
                    // its punches didn't work and it's nearly here: dodge
                    if (!punch && (threat.State == ThingState.Lunge || threat.Kind == ThingKind.Fireball && threat.Position.z < 2f))
                    {
                        // it's coming: after a reaction time, away from where it's going (or frozen)
                        if (!_comingAt.ContainsKey(threat.Id)) _comingAt[threat.Id] = _time;
                        if (_willDodge[threat.Id] && _time - _comingAt[threat.Id] >= Reaction)
                        {
                            var target = new Vector2(threat.Target.x / run.HalfWidth, threat.Target.y / run.HalfHeight);
                            want = new Vector2(target.x > 0f ? target.x - 1.4f : target.x + 1.4f, 0f);
                        }
                    }
                }
                else
                {
                    Thing star = null;
                    foreach (var t in run.Things)
                        if (t.Kind is ThingKind.Star or ThingKind.Heart && t.Position.z > 0.5f && t.Position.z < 20f &&
                            (star == null || t.Position.z < star.Position.z))
                            star = t;
                    want = star != null ? new Vector2(star.Position.x / run.HalfWidth, star.Position.y / run.HalfHeight) : Vector2.zero;
                }
                if (!Moves) want = Vector2.zero;
                // a person doesn't jump to the new place at once
                _steer = Vector2.Lerp(_steer, want, 1f - Mathf.Exp(-Dt / Mathf.Max(0.05f, Reaction)));
                return (_steer, punch);
            }
        }

        sealed class Outcome
        {
            public bool Won;
            public int LevelsCleared, Score, HitsTaken;
            public float Minutes;
        }

        static Outcome Play(Bot bot, int seed, float maxMinutes = 12f)
        {
            var run = new FlyRun(seed);
            run.Start();
            float t = 0f;
            int cleared = 0;
            while (!run.Over && t < maxMinutes * 60f)
            {
                var (steer, punch) = bot.Decide(run);
                run.Step(Dt, steer, punch);
                foreach (var e in run.Events)
                    if (e.Kind == FlyEventKind.BossDefeated) cleared++;
                t += Dt;
            }
            return new Outcome { Won = run.Phase == FlyPhase.Won, LevelsCleared = cleared, Score = run.Score, HitsTaken = run.HitsTaken, Minutes = t / 60f };
        }

        static Bot Perfect(int seed) => new Bot(seed);
        static Bot Kid(int seed) => new Bot(seed) { Reaction = 0.45f, Accuracy = 0.65f, DodgeSkill = 0.5f };
        static Bot Struggling(int seed) => new Bot(seed) { Reaction = 0.6f, Accuracy = 0.5f, DodgeSkill = 0.3f };
        static Bot Good(int seed) => new Bot(seed) { Reaction = 0.3f, Accuracy = 0.85f, DodgeSkill = 0.6f };
        static Bot Statue(int seed) => new Bot(seed) { Moves = false, Punches = false, Dodges = false };
        static Bot Dodger(int seed) => new Bot(seed) { Punches = false };

        [Test]
        public void APerfectPlayer_WinsAllThreeLevels()
        {
            for (int seed = 0; seed < 6; seed++)
            {
                var o = Play(Perfect(seed), seed);
                Assert.IsTrue(o.Won, $"seed {seed}: cleared {o.LevelsCleared}, hits {o.HitsTaken}");
                Assert.LessOrEqual(o.HitsTaken, 2, $"seed {seed}");
                Assert.Less(o.Minutes, 6f, "a whole game takes a few minutes");
            }
        }

        [Test]
        public void APlayerWhoStandsStill_LosesInTheFirstLevel()
        {
            for (int seed = 0; seed < 6; seed++)
            {
                var o = Play(Statue(seed), seed);
                Assert.IsFalse(o.Won);
                Assert.AreEqual(0, o.LevelsCleared, $"seed {seed}");
                Assert.Less(o.Minutes, 1f, $"seed {seed}: robots keep charging, so it ends soon");
            }
        }

        [Test]
        public void APlayerWhoNeverPunches_CantBeatABoss()
        {
            for (int seed = 0; seed < 4; seed++)
            {
                var o = Play(Dodger(seed), seed, maxMinutes: 5f);
                Assert.IsFalse(o.Won);
                Assert.AreEqual(0, o.LevelsCleared, $"seed {seed}");
            }
        }

        [Test]
        public void AChildLikePlayer_UsuallyWins_AStrugglingOneClearsALevelOrTwo()
        {
            const int games = 20;
            var kid = Enumerable.Range(0, games).Select(seed => Play(Kid(seed), seed)).ToList();
            Assert.GreaterOrEqual(kid.Count(o => o.LevelsCleared >= 1), games * 0.9, "the first level is gentle");
            Assert.GreaterOrEqual(kid.Count(o => o.Won), games * 0.5, "a child who keeps punching usually wins");
            var struggling = Enumerable.Range(0, games).Select(seed => Play(Struggling(seed), seed)).ToList();
            Assert.GreaterOrEqual(struggling.Count(o => o.LevelsCleared >= 1), games * 0.5, "even a struggling player sees a boss fall");
            Assert.LessOrEqual(struggling.Count(o => o.Won), games * 0.5, "but winning isn't a given");
        }

        [Test]
        public void Balance()
        {
            foreach (var (name, make) in new (string, System.Func<int, Bot>)[]
                     {
                         ("perfect (reacts in 0.15 s)", Perfect), ("good (0.3 s, 85 % punches, 60 % dodges)", Good),
                         ("child (0.45 s, 65 % punches, 50 % dodges)", Kid),
                         ("struggling (0.6 s, 50 %, 30 %)", Struggling),
                         ("never punches, dodges", Dodger), ("stands still", Statue),
                     })
            {
                var games = Enumerable.Range(0, 20).Select(seed => Play(make(seed), seed, 8f)).ToList();
                TestContext.WriteLine($"{name,-42} won {games.Count(g => g.Won),2}/20, levels cleared {games.Average(g => g.LevelsCleared):0.0}, " +
                                      $"hearts lost {games.Average(g => g.HitsTaken):0.0}, score {games.Average(g => g.Score):0}, " +
                                      $"minutes {games.Average(g => g.Minutes):0.0}");
            }
        }
    }
}
