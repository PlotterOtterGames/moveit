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
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Kart.Tests
{
    /// <summary>
    /// Gara! (2026-11-08) with the scripted player (fake_sender.py --script kart: both hands on an invisible wheel, 8 s
    /// cycles of turning left and right, a hard right turn held, both hands up, a jump): a whole race on the coast, the wheel
    /// steering the kart, the hard turn drifting, the jumps and raised hands reaching it, to the results. Two players
    /// against each other (--script kart_duo --alternate): the screen splits, each wheel steers its own kart. A cup played
    /// through on the keys (one-lap races, fast): the standings between races, the podium and the cup. Screenshots:
    /// unity/captures/kart_*.png.
    /// </summary>
    public class KartGameTests
    {
        const int MotionPort = 5023;

        Process _sender;
        KartGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Kart");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<KartGame>();
            Assert.IsNotNull(_game, "Kart scene has no KartGame");
            _game.NextMode = KartMode.Solo;
            _game.Menu.Format = KartFormat.Race;
            _game.Menu.Difficulty = KartDifficulty.Easy;
            _game.Menu.Track = 0;
            _game.Menu.View = 0;
            var motion = MotionSystem.Instance;
            motion.enabled = false;
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(motion, MotionPort);
            motion.enabled = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (_sender != null && !_sender.HasExited) _sender.Kill();
            _sender?.Dispose();
            _sender = null;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);
            yield return null;
        }

        [UnityTest, Timeout(280000)]
        public IEnumerator Solo_TheWheelSteers_JumpsAndRaisedHandsReachTheKart_ARaceToTheEnd()
        {
            _game.Practice = true;   // the computer quiet: nothing paints the scripted player
            int hops = 0, handsUp = 0;
            float minSteer = 0f, maxSteer = 0f, maxWheel = 0f;
            bool held = false;
            var events = new List<KartEvent>();
            _game.MovesSeen += (slot, e, t) =>
            {
                if (e.Move == KartMove.Hop) hops++;
                else handsUp++;
            };
            _game.RaceEvents += e =>
            {
                if (e.Kart == 0) events.Add(e);
            };
            bool shotWaiting = false, shotCount = false, shotRace = false, shotDrift = false, shotAir = false, shotResults = false, shotItem = false;
            float raceFrom = -1f, resultsFrom = -1f;
            bool results = false;
            int place = 0;
            _game.RaceEvents += e =>
            {
                if (e.Kart == 0 && e.Kind == KartEventKind.Finish) place = e.Value;
            };
            yield return RunFakePlayer(() =>
            {
                var m = _game.MovesOf(0);
                if (_game.Current == KartGame.Phase.Racing)
                {
                    minSteer = Mathf.Min(minSteer, m.Steer);
                    maxSteer = Mathf.Max(maxSteer, m.Steer);
                    maxWheel = Mathf.Max(maxWheel, Mathf.Abs(m.WheelAngle));
                    held |= m.Holding;
                    if (raceFrom < 0f) raceFrom = Time.time;
                }
                var race = _game.Race;
                var k = race?.Karts[0];
                if (!shotWaiting && _game.Current == KartGame.Phase.Waiting && Time.timeSinceLevelLoad > 2.5f) { shotWaiting = true; return "kart_menu.png"; }
                if (!shotCount && _game.Current == KartGame.Phase.Intro && race.Time > -1.6) { shotCount = true; return "kart_countdown.png"; }
                if (_game.Current == KartGame.Phase.Results)
                {
                    results = true;
                    if (!shotResults && Time.time - resultsFrom > 1.6f) { shotResults = true; return "kart_results.png"; }
                    if (resultsFrom < 0f) resultsFrom = Time.time;
                }
                if (k == null || _game.Current != KartGame.Phase.Racing) return null;
                if (!shotRace && Time.time - raceFrom > 9f) { shotRace = true; return "kart_racing.png"; }
                if (!shotDrift && k.Drifting && k.DriftTier >= 1) { shotDrift = true; return "kart_drift.png"; }
                if (!shotAir && k.Airborne && k.Height > 0.6f) { shotAir = true; return "kart_air.png"; }
                if (!shotItem && k.Item != KartItem.None && k.ItemRoll <= 0f) { shotItem = true; return "kart_item.png"; }
                return null;
            }, "kart", 200f);
            Log($"camera: {hops} jumps, {handsUp} hands up; the wheel {minSteer:0.00} .. {maxSteer:0.00} (up to {maxWheel:0} degrees), held {held}");
            Log("the kart: " + string.Join(", ", events.GroupBy(e => e.Kind).Select(g => $"{g.Key} {g.Count()}")));
            Assert.IsTrue(held, "both hands on the wheel");
            Assert.Greater(hops, 8, "a jump every cycle");
            Assert.GreaterOrEqual(handsUp, 6, "both hands up every cycle");
            Assert.Less(minSteer, -0.25f, "turned left");
            Assert.Greater(maxSteer, 0.75f, "turned hard right");
            Assert.IsTrue(events.Any(e => e.Kind == KartEventKind.DriftStart && e.Value == 1), "the hard right turn drifts");
            Assert.IsTrue(events.Any(e => e.Kind is KartEventKind.Hop or KartEventKind.Trick or KartEventKind.QuickStart), "the jumps reach the kart");
            Assert.Greater(place, 0, "over the line");
            Assert.IsTrue(results, "the results (hands up after them starts the next race)");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Versus_TwoKarts_TheScreenSplits_EachWheelSteersItsOwn()
        {
            _game.NextMode = KartMode.Versus;
            _game.Practice = true;
            float[] min = { 0f, 0f }, max = { 0f, 0f };
            bool shot = false;
            yield return RunFakePlayer(() =>
            {
                if (_game.Current != KartGame.Phase.Racing) return null;
                for (int k = 0; k < 2; k++)
                {
                    float s = _game.Race.KartOf(k).Steer;
                    min[k] = Mathf.Min(min[k], s);
                    max[k] = Mathf.Max(max[k], s);
                }
                if (!shot && _game.Race.Time > 6.0)
                {
                    shot = true;
                    return "kart_versus.png";
                }
                return null;
            }, "kart_duo --alternate", 60f, untilDone: true);
            Log($"versus: player 1 steered {min[0]:0.00} .. {max[0]:0.00}, player 2 {min[1]:0.00} .. {max[1]:0.00}");
            Assert.AreEqual(KartMode.Versus, _game.Mode);
            Assert.IsNotNull(_game.SplitCamera, "the screen split");
            Assert.AreNotSame(_game.Race.KartOf(0), _game.Race.KartOf(1));
            for (int k = 0; k < 2; k++)
            {
                Assert.Less(min[k], -0.2f, $"player {k + 1} turned left");
                Assert.Greater(max[k], 0.5f, $"player {k + 1} turned right");
            }
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Cup_ThreeRaces_TheStandings_ThePodium()
        {
            _game.Menu.Format = KartFormat.Cup;
            _game.Laps = 1;
            _game.Practice = true;
            yield return new WaitForSeconds(1f);
            Time.timeScale = 4f;
            _game.Continue();
            int standings = 0;
            float deadline = Time.realtimeSinceStartup + 200f;
            while (Time.realtimeSinceStartup < deadline && _game.Current != KartGame.Phase.Results)
            {
                // the player's kart flies (a boost all along: the test is the cup's screens, not the race)
                var race = _game.Race;
                if (race != null && race.Phase == KartPhase.Racing)
                {
                    race.Karts[0].BoostTime = 1f;
                    race.Karts[0].BoostPower = 1.35f;
                }
                if (_game.Current == KartGame.Phase.Standings)
                {
                    standings++;
                    Time.timeScale = 1f;
                    yield return new WaitForSeconds(2.2f);
                    yield return Screenshot($"kart_standings_{standings}.png");
                    Assert.AreEqual(standings, _game.Cup.Race, "the cup's next race");
                    Time.timeScale = 4f;
                    _game.Continue();
                }
                yield return null;
            }
            Time.timeScale = 1f;
            Assert.AreEqual(KartGame.Phase.Results, _game.Current, "the cup ended");
            Assert.AreEqual(2, standings, "standings after races 1 and 2");
            Assert.IsTrue(_game.Cup.Over);
            Assert.AreEqual(1, _game.Cup.StandingOf(0), "the flying kart won it");
            Log("cup: " + string.Join(", ", _game.Cup.Standings().Select(r => $"{r}: {_game.Cup.Points[r]}")));
            yield return new WaitForSeconds(1.8f);
            yield return Screenshot("kart_cup.png");
        }

        static void Log(string text) => Debug.Log($"[KartTest] {text}");

        IEnumerator RunFakePlayer(System.Func<string> everyFrame, string script, float seconds, bool untilDone = true)
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
            if (untilDone) Assert.IsTrue(_sender.HasExited, $"fake_sender.py --script {script} did not finish in time");
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
