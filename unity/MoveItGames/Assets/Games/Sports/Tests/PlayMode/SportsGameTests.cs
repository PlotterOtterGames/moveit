using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Sports.Tests
{
    /// <summary>
    /// Sporte! in the editor. Every event builds and plays without a camera (screenshots unity/captures/sports_*.png).
    /// Then the scripted player (fake_sender.py) plays them with the body: running on the spot through a 100 m (--script
    /// sp_run), kicks at the passes (sp_kick).
    /// </summary>
    public class SportsGameTests
    {
        const int MotionPort = 5023;

        Process _sender;
        SportsGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Sports");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<SportsGame>();
            Assert.IsNotNull(_game, "Sports scene has no SportsGame");
            _game.SkipHowTo = true;
            var motion = MotionSystem.Instance;
            motion.enabled = false;
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(motion, MotionPort);
            motion.enabled = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_sender != null && !_sender.HasExited) _sender.Kill();
            _sender?.Dispose();
            _sender = null;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);
            yield return null;
        }

        int IndexOf(string id)
        {
            for (int i = 0; i < _game.Activities.Count; i++)
                if (_game.Activities[i].Id == id) return i;
            Assert.Fail($"no activity {id}");
            return -1;
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator EveryEvent_BuildsAndPlays_WithoutACamera()
        {
            yield return Screenshot("sports_choose.png");
            for (int i = 0; i < _game.Activities.Count; i++)
            {
                _game.StartActivity(i);
                var a = _game.Playing;
                float until = Time.realtimeSinceStartup + 9.5f;
                bool shot = false;
                while (Time.realtimeSinceStartup < until)
                {
                    if (!shot && _game.Current == ActivityGame.Phase.Playing && a.Time > 4f)
                    {
                        shot = true;
                        yield return Screenshot($"sports_{a.Id}.png");
                    }
                    else yield return null;
                }
                Log($"{a.Id}: {a.Time:0.0} s played, {a.Points} points, phase {_game.Current}");
                Assert.AreEqual(ActivityGame.Phase.Playing, _game.Current, $"{a.Id} is playing");
                Assert.Greater(a.Time, 4f, $"{a.Id}'s clock runs");
            }
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Sprint_RunningOnTheSpotRunsTheRace()
        {
            _game.StartActivity(IndexOf("sprint"));
            var track = (TrackActivity)_game.Playing;
            float fastest = 0f;
            bool shot = false, line = false, results = false;
            yield return RunFakePlayer(() =>
            {
                if (_game.Current == ActivityGame.Phase.Results && !results && _game.PhaseClock > 2.2f) { results = true; return "sports_results.png"; }
                if (_game.Current != ActivityGame.Phase.Playing || track.Race == null) return null;
                fastest = Mathf.Max(fastest, track.Race.Human.Speed);
                if (!shot && track.Race.Human.Distance > 30f) { shot = true; return "sports_sprint_running.png"; }
                if (!line && track.Race.Human.Distance > 96f) { line = true; return "sports_sprint_finish.png"; }
                return null;
            }, "sp_run", 60f);
            var race = track.Race;
            Log($"sprint: {track.Running.Steps} steps, top speed {fastest:0.0} m/s, finished {race.Human.Finished} in {race.Human.Finish:0.00} s, " +
                $"place {race.HumanPlace}, false starts {race.FalseStarts}");
            Assert.AreEqual(0, race.FalseStarts, "standing still through the start is no false start");
            Assert.Greater(fastest, 5f, "running on the spot runs");
            Assert.IsTrue(race.Human.Finished, "the runner crossed the line");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator Football_KicksMeetThePasses()
        {
            _game.StartActivity(IndexOf("football"));
            var football = (FootballActivity)_game.Playing;
            int kicked = 0;
            bool shot = false, keeping = false;
            football.Match.Events += e => { if (e == FootballEvent.Kicked) kicked++; };
            yield return RunFakePlayer(() =>
            {
                if (_game.Current != ActivityGame.Phase.Playing) return null;
                var m = football.Match;
                if (!shot && m.Phase == FootballPhase.Flight && m.Attacking) { shot = true; return "sports_football_shot.png"; }
                if (!keeping && m.Phase == FootballPhase.Flight && !m.Attacking && m.Ball.z < 4f) { keeping = true; return "sports_football_keeper.png"; }
                return null;
            }, "sp_kick", 50f);
            Log($"football: {football.KicksSeen} kicks seen, {kicked} met the ball, goals {football.Match.Goals}, round {football.Match.Round}");
            Assert.GreaterOrEqual(football.KicksSeen, 10, "the scripted kicks were seen");
            Assert.GreaterOrEqual(kicked, 2, "kicks met the passes");
        }

        static void Log(string text) => UnityEngine.Debug.Log($"[Test] {text}");

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
            Assert.IsTrue(_sender.HasExited, $"fake_sender.py --script {script} did not finish in time");
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
