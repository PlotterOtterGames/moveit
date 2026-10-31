using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Fly.Tests
{
    /// <summary>
    /// Fluturo! with the scripted player (fake_sender.py --script fly): raising both hands starts the game; jabs straight
    /// at the camera (seen only through the world joints), a hook, punches while flying left and right are each one punch
    /// with the right hand reported, and each smashes the robot the test keeps hovering in front of the hero; stepping
    /// left and right flies the hero there, ducking flies it low, a jump lifts it; nothing else is a punch. Then the boss
    /// flies in. Screenshots: unity/captures/fly_*.png.
    /// </summary>
    public class FlyGameTests
    {
        const int MotionPort = 5014;

        Process _sender;
        FlyGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Fly");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<FlyGame>();
            Assert.IsNotNull(_game, "Fly scene has no FlyGame");
            var motion = MotionSystem.Instance;
            motion.enabled = false;
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(motion, MotionPort);
            motion.enabled = true;
            _game.Practice = true;   // nothing spawns by itself: the test places the robots
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_sender != null && !_sender.HasExited) _sender.Kill();
            _sender?.Dispose();
            _sender = null;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);   // survives scene loads
            yield return null;
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator Fly_PunchesSmashRobots_TheBodySteers()
        {
            var punches = new List<bool>();
            var times = new List<float>();
            _game.Punched += right =>
            {
                punches.Add(right);
                times.Add(Time.time);
            };
            float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;
            bool shotPunch = false, shotLow = false, shotBoss = false, bossSent = false;
            int robotsPlaced = 0;
            Thing robot = null;

            yield return RunFakePlayer("fly", () =>
            {
                var run = _game.Run;
                if (_game.Current != FlyGame.Phase.Playing || run == null) return null;
                minX = Mathf.Min(minX, run.Hero.x);
                maxX = Mathf.Max(maxX, run.Hero.x);
                minY = Mathf.Min(minY, run.Hero.y);
                maxY = Mathf.Max(maxY, run.Hero.y);
                // keep one robot waiting right in front of the hero (practice mode: it never attacks), so every punch
                // has something to hit
                if (run.Phase == FlyPhase.Flight && (robot == null || robot.State is not (ThingState.Wait or ThingState.Approach)))
                {
                    robot = run.SpawnRobot(run.Hero.x, run.Hero.y, 3f);
                    robot.Position = robot.Home;
                    robotsPlaced++;
                }
                else if (robot != null && robot.State == ThingState.Wait)
                    robot.Position = robot.Home = new Vector3(run.Hero.x, run.Hero.y, 3f);   // it follows the hero
                if (!shotPunch && robot != null && run.CanPunch(robot) && punches.Count >= 1) { shotPunch = true; return "fly_punch.png"; }
                if (!shotLow && run.Hero.y < -0.8f) { shotLow = true; return "fly_low.png"; }
                // after the script's last punch: the boss
                if (!bossSent && punches.Count >= 6)
                {
                    bossSent = true;
                    run.BeginBoss();
                }
                if (!shotBoss && run.Phase == FlyPhase.Boss && run.PhaseTime > 0.5f) { shotBoss = true; return "fly_boss.png"; }
                return null;
            });

            var r = _game.Run;
            Log($"punches {string.Join(", ", punches.ConvertAll(p => p ? "right" : "left"))} " +
                $"at {string.Join(", ", times.ConvertAll(t => (t - times[0]).ToString("0.00")))} s; robots smashed {r?.RobotsSmashed} of {robotsPlaced} placed; " +
                $"hero x {minX:0.00}..{maxX:0.00}, y {minY:0.00}..{maxY:0.00}; hearts {r?.Hearts}; phase {r?.Phase}");
            Assert.IsNotNull(r, "raising both hands started the game");
            CollectionAssert.AreEqual(new[] { true, false, true, false, true, true }, punches,
                "right jab, left jab, right while flying left, left while flying right, right hook, right jab: no other punches");
            Assert.AreEqual(6, r.RobotsSmashed, "every punch smashed the robot in front of the hero");
            Assert.Less(minX, -1.2f, "stepping left flew the hero left");
            Assert.Greater(maxX, 1.2f, "stepping right flew the hero right");
            Assert.Less(minY, -0.7f, "ducking flew the hero low");
            Assert.Greater(maxY, 0.5f, "a jump lifted the hero");
            Assert.AreEqual(r.StartHearts, r.Hearts, "nothing hurt the hero");
            Assert.IsTrue(bossSent && r.Phase is FlyPhase.BossArrives or FlyPhase.Boss, "the boss came");
        }

        /// <summary>
        /// A whole first district played by the body (fake_sender --script brawl: fists up, then jabs left and right
        /// without a break, about three a second, stepping side to side): drones punched on the way, the rush beaten,
        /// the champion knocked out in the clash (the faster puncher wins), on into district 2 with every heart.
        /// Screenshots: fly_rush.png, fly_clash.png.
        /// </summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator ABrawlingPlayer_SavesTheFirstDistrict()
        {
            _game.Practice = false;
            var seen = new List<FlyEventKind>();
            _game.RunEvent += e => seen.Add(e.Kind);
            bool shotRush = false, shotClash = false;
            yield return RunFakePlayer("brawl", () =>
            {
                var run = _game.Run;
                if (_game.Current != FlyGame.Phase.Playing || run == null) return null;
                if (!shotRush && run.Phase == FlyPhase.Rush && run.Things.Count(t => t.Attacking) >= 2) { shotRush = true; return "fly_rush.png"; }
                if (!shotClash && run.Phase == FlyPhase.Clash && run.Clash.Time > 1.5f) { shotClash = true; return "fly_clash.png"; }
                return null;
            }, done: () => _game.Run != null && _game.Run.District >= 2, seconds: 100f);
            var r = _game.Run;
            Log($"district {r?.District}, robots {r?.RobotsSmashed}, drones {r?.DronesSmashed}, clashes won {r?.ClashesWon} " +
                $"(quickest {r?.QuickestClash:0.0} s), hearts {r?.Hearts}, stars {r?.StarsCollected}, score {r?.Score}");
            Assert.IsNotNull(r);
            Assert.GreaterOrEqual(r.District, 2, "the first district saved");
            Assert.Contains(FlyEventKind.RushCleared, seen);
            Assert.Contains(FlyEventKind.ClashWon, seen, "the champion knocked out");
            Assert.Greater(r.DronesSmashed, 0, "drones punched on the way");
            Assert.AreEqual(r.StartHearts, r.Hearts, "punching all along, nothing got through");
        }

        /// <summary>
        /// The scripted player steps right and stays, then left and stays (fake_sender --script turns). Each time, once the
        /// hero is over on that side, the test puts a crossroads with streets both ways 30 m ahead: the hero turns
        /// right, then left, along an arc, onto the cross street; going back to the middle in between turns nothing.
        /// Screenshot: fly_turn.png (mid-turn).
        /// </summary>
        [UnityTest, Timeout(90000)]
        public IEnumerator SteppingAside_AtACrossroads_TurnsIntoThatStreet()
        {
            var turns = new List<TurnChoice>();
            bool armedRight = false, armedLeft = false, shot = false;
            float headingAtFirst = 0f;
            _game.Turned += t =>
            {
                if (armedRight && turns.Count < 2) turns.Add(t);   // later crossroads (the script runs on) don't count
                if (turns.Count == 1) headingAtFirst = _game.Route.Heading;
            };
            yield return RunFakePlayer("turns", () =>
            {
                var run = _game.Run;
                if (_game.Current != FlyGame.Phase.Playing || run == null) return null;
                var route = _game.Route;
                float side = route.TurnAt * run.HalfWidth + 0.1f;   // far enough over to turn
                if (!armedRight && run.Hero.x > side && !route.Turning)
                {
                    PutCrossroadsAhead(route);
                    armedRight = true;
                }
                if (armedRight && !armedLeft && turns.Count == 1 && !route.Turning && run.Hero.x < -side)
                {
                    PutCrossroadsAhead(route);
                    armedLeft = true;
                }
                if (!shot && route.Turning && route.TurnProgress > 0.4f)
                {
                    shot = true;
                    return "fly_turn.png";
                }
                return null;
            });
            Log($"turns {string.Join(", ", turns)}; heading after the first {headingAtFirst:0}, now {_game.Route.Heading:0}; " +
                $"turns taken in all {_game.Route.TurnsTaken}");
            Assert.IsTrue(armedRight && armedLeft, "the scripted player stepped right, then left");
            CollectionAssert.AreEqual(new[] { TurnChoice.Right, TurnChoice.Left }, turns, "a turn each way, in the order stepped");
            Assert.AreEqual(0f, Mathf.Repeat(_game.Route.Heading + 1e-4f, 90f), 1e-2f, "flying straight along a street again");
        }

        /// <summary>Moves the route 30 m before a crossroads with streets ahead, left and right, in its current direction.</summary>
        static void PutCrossroadsAhead(FlyRoute route)
        {
            int dir = route.Direction;
            var p = route.Position;
            for (int r = 0; r < 20; r++)
            for (int i = -r; i <= r; i++)
            for (int j = -r; j <= r; j++)
            {
                int ci = Mathf.RoundToInt(p.x / route.Pitch) + i, cj = Mathf.RoundToInt(p.y / route.Pitch) + j;
                bool full = route.Street(ci, cj, dir) && route.Street(ci, cj, dir + 1) && route.Street(ci, cj, dir + 3) &&
                            route.Street(ci, cj, dir + 2);
                if (!full) continue;
                route.StartAt(ci, cj, dir, 30f);
                return;
            }
            Assert.Fail("no full crossroads nearby");
        }

        static void Log(string text) => UnityEngine.Debug.Log($"[Test] {text}");

        /// <summary>Runs the scripted player (until it finishes, or <paramref name="done"/>); everyFrame may return a
        /// screenshot file name.</summary>
        IEnumerator RunFakePlayer(string script, System.Func<string> everyFrame, System.Func<bool> done = null, float seconds = 60f)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python, $"fake_sender.py --script {script} --port {MotionPort} --once")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline && (done == null || !done()))
            {
                string shot = everyFrame();
                if (shot != null) yield return Screenshot(shot);
                else yield return null;
            }
            if (done != null) Assert.IsTrue(done(), $"fake_sender.py --script {script}: not done in {seconds} s");
            else Assert.IsTrue(_sender.HasExited, $"fake_sender.py --script {script} did not finish in time");
        }

        static IEnumerator Screenshot(string name)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            Directory.CreateDirectory(dir);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name));
            yield return null;
        }
    }
}
