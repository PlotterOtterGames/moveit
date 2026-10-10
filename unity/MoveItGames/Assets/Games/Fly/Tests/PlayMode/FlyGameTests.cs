using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            _game.Punched += punches.Add;
            float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;
            bool shotPunch = false, shotLow = false, shotBoss = false, bossSent = false;
            int robotsPlaced = 0;
            Thing robot = null;

            yield return RunFakePlayer(() =>
            {
                var run = _game.Run;
                if (_game.Current != FlyGame.Phase.Playing || run == null) return null;
                run.Settings.HoverSeconds = 999f;   // robots wait to be punched instead of charging
                minX = Mathf.Min(minX, run.Hero.x);
                maxX = Mathf.Max(maxX, run.Hero.x);
                minY = Mathf.Min(minY, run.Hero.y);
                maxY = Mathf.Max(maxY, run.Hero.y);
                // keep one robot hovering right in front of the hero, so every punch has something to hit
                if (run.Phase == FlyPhase.Flight && (robot == null || robot.State != ThingState.Hover && robot.State != ThingState.Approach))
                {
                    robot = run.SpawnRobot(run.Hero.x, run.Hero.y, run.HoverDistance + 1.5f);
                    robotsPlaced++;
                }
                else if (robot != null && robot.State == ThingState.Hover)
                    robot.Position = new Vector3(run.Hero.x, run.Hero.y, robot.Position.z);   // it follows the hero
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
            Log($"punches {string.Join(", ", punches.ConvertAll(p => p ? "right" : "left"))}; robots smashed {r?.RobotsSmashed} of {robotsPlaced} placed; " +
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

        static void Log(string text) => UnityEngine.Debug.Log($"[Test] {text}");

        /// <summary>Runs the scripted player; everyFrame may return a screenshot file name.</summary>
        IEnumerator RunFakePlayer(System.Func<string> everyFrame)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python, $"fake_sender.py --script fly --port {MotionPort} --once")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline)
            {
                string shot = everyFrame();
                if (shot != null) yield return Screenshot(shot);
                else yield return null;
            }
            Assert.IsTrue(_sender.HasExited, "fake_sender.py --script fly did not finish in time");
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
