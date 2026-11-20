using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Runner.Tests
{
    /// <summary>
    /// Vrapo! after the console polish (2026-11-18). The scripted player (fake_sender.py --script runner: hands up, then
    /// steps left and right, hops, ducks) starts a run from the title, the countdown swings the camera round behind Dino,
    /// his jumps and slides follow the body (nothing stops the run: the script doesn't look where it goes). Then without a
    /// camera: the valley at every time of day, and a crash into the first thing in the middle lane, the dizzy shot and the
    /// results with a new record. Screenshots: unity/captures/runner_*.png.
    /// </summary>
    public class RunnerGameTests
    {
        const int MotionPort = 5032;

        Process _sender;
        RunnerGame _game;
        int _bestBefore;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _bestBefore = PlayerPrefs.GetInt("runner.best", 0);
            SceneManager.LoadScene("Runner");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<RunnerGame>();
            Assert.IsNotNull(_game, "Runner scene has no RunnerGame");
            var motion = MotionSystem.Instance;
            motion.enabled = false;
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(motion, MotionPort);
            motion.enabled = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RunnerGame.TestTime = -1f;
            RunnerGame.Invincible = false;
            Time.timeScale = 1f;
            PlayerPrefs.SetInt("runner.best", _bestBefore);
            if (_sender != null && !_sender.HasExited) _sender.Kill();
            _sender?.Dispose();
            _sender = null;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TheScriptedPlayer_StartsARun_JumpsAndSlides()
        {
            RunnerGame.Invincible = true;
            bool title = false, count = false, run = false, jump = false, slide = false, egg = false;
            int jumps = 0, slides = 0;
            bool inAir = false, sliding = false;
            float slideFrom = -1f;
            float phaseFrom = 0f;
            string phase = "";
            int eggs = 0;
            float eggAt = -1f;
            yield return RunFakePlayer(() =>
            {
                if (_game.Phase != phase)
                {
                    phase = _game.Phase;
                    phaseFrom = Time.time;
                }
                float into = Time.time - phaseFrom;
                var p = _game.Player;
                if (phase == "Running")
                {
                    if (p.InAir && !inAir) jumps++;
                    if (p.IsSliding && !sliding)
                    {
                        slides++;
                        slideFrom = Time.time;
                    }
                    inAir = p.InAir;
                    sliding = p.IsSliding;
                    if (_game.Eggs > eggs)
                    {
                        eggs = _game.Eggs;
                        if (eggAt < 0f) eggAt = Time.time;
                    }
                }
                if (!title && phase == "Waiting" && Time.timeSinceLevelLoad > 2.4f) { title = true; return "runner_title.png"; }
                if (!count && phase == "Countdown" && into > 1.3f) { count = true; return "runner_countdown.png"; }
                if (!run && phase == "Running" && into > 5f) { run = true; return "runner_run.png"; }
                if (!jump && phase == "Running" && p.InAir && p.FeetY > 1.2f) { jump = true; return "runner_jump.png"; }
                if (!slide && phase == "Running" && p.IsSliding && slideFrom > 0f && Time.time - slideFrom > 0.25f) { slide = true; return "runner_slide.png"; }
                if (!egg && eggAt > 0f && Time.time - eggAt > 0.18f) { egg = true; return "runner_egg.png"; }
                return null;
            }, "runner", 70f);
            Debug.Log($"[RunnerTest] {jumps} jumps, {slides} slides, {eggs} eggs, {_game.Distance:0} m");
            Assert.IsTrue(run, "the hands up started a run");
            Assert.GreaterOrEqual(jumps, 2, "the hops jumped");
            Assert.GreaterOrEqual(slides, 1, "the duck slid");
            Assert.Greater(_game.Distance, 80f, "the run went on");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator TheValley_AtEveryTimeOfDay_ThenACrash_AndANewRecord()
        {
            PlayerPrefs.SetInt("runner.best", 1);
            RunnerGame.Invincible = true;
            yield return Wait(1.5f);
            _game.TestStart();
            yield return Wait(4.5f);
            Assert.AreEqual("Running", _game.Phase);
            string[] names = { "morning", "day", "golden", "sunset", "night", "dawn" };
            // the scarf streams back in the run's wind
            Assert.IsTrue(_game.Player.ScarfReady, "the scarf's bones found");
            var running = _game.Player.ScarfTip(0);
            Debug.Log($"[RunnerTest] scarf tip running {running}");
            Assert.Less(running.z, -0.35f, "the scarf streams back while running");
            for (int i = 0; i < names.Length; i++)
            {
                RunnerGame.TestTime = 4f + i * (RunnerWorld.TimeHold + RunnerWorld.TimeBlend);
                yield return Wait(2.2f);
                yield return Screenshot($"runner_tod_{names[i]}.png");
            }
            // a crash: nothing steers, Dino runs into the first thing in the middle lane
            RunnerGame.TestTime = 4f;
            RunnerGame.Invincible = false;
            float deadline = Time.realtimeSinceStartup + 40f;
            while (_game.Phase == "Running" && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual("Crashed", _game.Phase, "ran into something");
            yield return Wait(0.9f);
            yield return Screenshot("runner_crash.png");
            yield return Wait(0.8f);
            var sitting = _game.Player.ScarfTip(0);
            Debug.Log($"[RunnerTest] scarf tip sitting {sitting}");
            Assert.Less(sitting.y, -0.2f, "the scarf hangs down once he sits");
            while (_game.Phase != "GameOver" && Time.realtimeSinceStartup < deadline + 5f) yield return null;
            yield return Wait(2.6f);
            yield return Screenshot("runner_results.png");
            Assert.IsTrue(_game.NewBest, "beat the best of 1");
        }

        static IEnumerator Wait(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        IEnumerator RunFakePlayer(System.Func<string> everyFrame, string script, float seconds)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python, $"fake_sender.py --script {script} --port {MotionPort} --once --video-control-port 0")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline)
            {
                string shot = everyFrame();
                if (shot != null) yield return Screenshot(shot);
                else yield return null;
            }
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
