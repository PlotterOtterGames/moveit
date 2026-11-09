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

namespace MoveIt.Games.Volley.Tests
{
    /// <summary>
    /// Volejboll! (2026-11-07) with the scripted player (fake_sender.py --script volley: hands up, then 10 s cycles of the
    /// bump, the set, a one-arm spike and both hands up again for a block; the first cycle serves). In practice the
    /// computer times its balls to the cycle, so each move has a ball: the body serves, blocks the computer's smash (the
    /// point; it serves again), receives its free ball with the bump, sets with both hands up and spikes with one arm,
    /// again and again. Two players (--script volley_duo --alternate): together, each plays their own toy; versus, the
    /// screen splits. And the menu's tournament played through. Screenshots: unity/captures/volley_*.png.
    /// </summary>
    public class VolleyGameTests
    {
        const int MotionPort = 5022;
        const double Period = 10.0;

        Process _sender;
        VolleyGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Volley");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<VolleyGame>();
            Assert.IsNotNull(_game, "Volley scene has no VolleyGame");
            _game.NextMode = VolleyMode.Solo;   // the remembered choice may be a two-player one
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

        [UnityTest, Timeout(240000)]
        public IEnumerator Practice_TheBodysMoves_ServeBlockReceiveSetSpike()
        {
            var touches = new List<VolleyEvent>();
            int bumpsSeen = 0, setsSeen = 0, hitsSeen = 0;
            double cycle = -1;   // the match time a cycle of the script started (from its first bump pose seen)
            double lastBumpSample = -10;
            _game.Practice = true;
            // the computer's attacks by turns: a smash (the block) and a free ball (the receive)
            _game.PracticeAttack = n => n % 2 == 0 ? Attack.Smash : Attack.Free;
            int defences = 0;
            _game.MovesSeen += (slot, e, t) =>
            {
                if (e.Move == Move.Bump)
                {
                    // a cycle's bump starts 0.2 s into it and counts after three samples (~0.1 s)
                    if (t - lastBumpSample > 2.0) cycle = t - 0.3;
                    lastBumpSample = t;
                    bumpsSeen++;
                }
                else if (e.Move == Move.Set) setsSeen++;
                else if (e.Move == Move.Hit)
                {
                    hitsSeen++;
                    Log($"hit {e.Speed:0.0} torso/s ({e.Direction.x:0.0}, {e.Direction.y:0.0}) at {t:0.00}");
                }
            };
            _game.MatchEvents += e =>
            {
                if (e.Kind == VolleyEventKind.Touch && e.Team == 0)
                {
                    touches.Add(e);
                    Log($"touch {e.Touch} #{e.Number}{(e.Late ? " late" : "")} power {e.Power:0.00} at ({e.Position.x:0.0}, {e.Position.y:0.0}, {e.Position.z:0.0})");
                }
                if (e.Kind is VolleyEventKind.Missed) Log($"missed #{e.Number}");
                if (e.Kind == VolleyEventKind.Defence)
                {
                    defences++;
                    Log($"defence: {e.Attack} met with {e.Choice}");
                }
            };
            // the script's cycle: the bump held 0.2-1.9 s, the set 2.3-3.4 s, the spike at 4.4 s (the first cycle serves at
            // 4.4 and 6.4 s instead), the block 7.6-8.9 s; the computer's balls come to the next such moment, its attack at
            // 8.3 s
            double Next(int number, double earliest)
            {
                if (cycle < 0) return earliest;
                double offset = number switch { 0 => 4.4 - 0.6, 1 => 1.2, 2 => 3.0, 3 => 4.45, _ => 8.3 };
                double t = cycle + offset;
                while (t < earliest) t += Period;
                return t;
            }
            _game.PracticeContact = Next;

            bool shotReceive = false, shotSet = false, shotSpike = false, shotServe = false, shotBlock = false, shotBlocked = false;
            yield return RunFakePlayer(() =>
            {
                var m = _game.Match;
                if (_game.Current != VolleyGame.Phase.Playing) return null;
                var c = m.Next;
                if (!shotServe && m.Phase == VolleyPhase.Toss && m.Server.Controller >= 0 && m.Time > c.Time - 0.2) { shotServe = true; return "volley_serve.png"; }
                if (!shotBlock && m.Coming.Active && m.Coming.HitTime - m.Time < 0.25 && m.ShowsNow(0) == Defence.Block) { shotBlock = true; return "volley_block.png"; }
                if (!shotBlocked && m.LastTouch == Touch.Block && m.Phase == VolleyPhase.Rally) { shotBlocked = true; return "volley_blocked.png"; }
                if (c.Active && c.Team == 0 && c.Time - m.Time < 0.35 && c.Time > m.Time)
                {
                    if (!shotReceive && c.Number == 1) { shotReceive = true; return "volley_receive.png"; }
                    if (!shotSet && c.Number == 2) { shotSet = true; return "volley_set.png"; }
                    if (!shotSpike && c.Number == 3 && touches.Count(t => t.Touch == Touch.Spike) >= 1) { shotSpike = true; return "volley_spike.png"; }
                }
                return null;
            }, "volley", 130f);

            Log($"moves seen: {bumpsSeen} bump samples, {setsSeen} set samples, {hitsSeen} hits; touches: {string.Join(", ", touches.Select(t => t.Touch))}; " +
                $"glitches ignored {_game.GlitchesIgnored}");
            Assert.AreEqual(VolleyGame.Phase.Playing, _game.Current, "raising both hands started the game");
            Assert.AreEqual(4, _game.Views.Count(v => v != null), "two against two");
            Assert.IsTrue(!_game.Views[0].Low && _game.Views[2].Low, "the near team in full detail, the far one low-detail");
            Assert.IsTrue(touches.Exists(t => t.Touch == Touch.Serve), "a hit served");
            Assert.GreaterOrEqual(touches.Count(t => t.Touch == Touch.Block), 2, "both hands up as the computer smashed: blocked");
            Assert.Greater(defences, 2, "the computer's smashes came at the player");
            Assert.GreaterOrEqual(touches.Count(t => t.Touch == Touch.Bump && t.Number == 1), 3, "hands together low received ball after ball");
            Assert.GreaterOrEqual(touches.Count(t => t.Touch == Touch.Set && t.Number == 2), 3, "both hands up set it");
            Assert.GreaterOrEqual(touches.Count(t => t.Touch == Touch.Spike), 3, "one arm spiked it over");
            Assert.IsFalse(touches.Exists(t => t.Touch == Touch.Shank), "never the wrong move");
            Assert.Greater(touches.Where(t => t.Touch == Touch.Spike).Min(t => t.Position.y), 1.6f, "spiked from up high");
            Assert.IsFalse(touches.Exists(t => t.Touch == Touch.Push), "the set and the bump never pushed it over (each touch got its own move)");
        }

        /// <summary>Two players together (--script volley_duo --alternate: one person per packet, like the box sharing its
        /// NPU): both raise their hands, each one's moves are read on their own, and each plays their own toy.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator TwoPlayers_Together_EachPlaysTheirOwnToy()
        {
            _game.NextMode = VolleyMode.Together;
            _game.FirstServer = 1;
            var movesBySlot = new int[2];
            var touchesBy = new int[2];
            _game.MovesSeen += (slot, e, t) => movesBySlot[slot]++;
            _game.MatchEvents += e =>
            {
                if (e.Kind != VolleyEventKind.Touch || e.Team != 0) return;
                touchesBy[_game.Match.Players[e.Player].Controller]++;
                Log($"player {_game.Match.Players[e.Player].Controller + 1}'s toy: {e.Touch} #{e.Number}");
            };
            bool shotWaiting = false, shotPlaying = false;
            yield return RunFakePlayer(() =>
            {
                if (_game.Current == VolleyGame.Phase.Waiting && _game.Slots.Present == 2 && !shotWaiting && _game.Slots[0].Calibrated)
                {
                    shotWaiting = true;
                    return "volley_two_waiting.png";
                }
                if (_game.Current != VolleyGame.Phase.Playing) return null;
                var c = _game.Match.Next;
                if (!shotPlaying && c.Active && c.Team == 0 && c.Time - _game.Match.Time < 0.3) { shotPlaying = true; return "volley_together.png"; }
                return null;
            }, "volley_duo --alternate", 75f);
            Log($"together: moves seen P1 {movesBySlot[0]}, P2 {movesBySlot[1]}; touches P1's toy {touchesBy[0]}, P2's {touchesBy[1]}; " +
                $"score {_game.Match.Score.Points(0)}-{_game.Match.Score.Points(1)}");
            Assert.AreEqual(VolleyMode.Together, _game.Mode, "both raised their hands: together started");
            Assert.AreEqual(0, _game.Match.Players[0].Controller);
            Assert.AreEqual(1, _game.Match.Players[1].Controller);
            Assert.Greater(movesBySlot[0], 0, "player 1's moves read");
            Assert.Greater(movesBySlot[1], 0, "player 2's moves read");
            Assert.Greater(touchesBy[0] + touchesBy[1], 2, "their toys played the ball");
        }

        /// <summary>Two players against each other (the same script): the screen splits, all four toys low-detail.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator TwoPlayers_Versus_SplitScreen()
        {
            _game.NextMode = VolleyMode.Versus;
            bool split = false, shot = false;
            int splitFrames = 0, lowFrames = 0;
            var touchesBy = new int[2];
            _game.MatchEvents += e =>
            {
                if (e.Kind == VolleyEventKind.Touch) touchesBy[e.Team]++;
            };
            yield return RunFakePlayer(() =>
            {
                if (_game.SplitCamera != null)
                {
                    split = true;
                    splitFrames++;
                    if (_game.Views.All(v => v.Low)) lowFrames++;
                }
                if (_game.Current != VolleyGame.Phase.Playing) return null;
                if (!shot && _game.Match.Phase == VolleyPhase.Rally && _game.Match.Crossings >= 1) { shot = true; return "volley_versus.png"; }
                return null;
            }, "volley_duo --alternate", 75f);
            Log($"versus: touches team 1 {touchesBy[0]}, team 2 {touchesBy[1]}, score {_game.Match.Score.Points(0)}-{_game.Match.Score.Points(1)}");
            Assert.AreEqual(VolleyMode.Versus, _game.Mode);
            Assert.IsTrue(split, "the screen was split");
            Assert.AreEqual(splitFrames, lowFrames, "split screen: all four toys low-detail");
            Assert.Greater(touchesBy[0], 0, "player 1's team played");
            Assert.Greater(touchesBy[1], 0, "player 2's team played");
        }

        /// <summary>The menu's tournament (no camera: the remote's OK and the test's hooks): three matches against stronger
        /// pairs, the bracket between them, the cup at the end. Screenshots: volley_menu.png, volley_bracket.png,
        /// volley_champion.png.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator Menu_ATournament_TheBracket_TheCup()
        {
            const string cupKey = "volley.cups.1";
            int cupsBefore = PlayerPrefs.GetInt(cupKey, 0);
            var menu = _game.Menu;
            var saved = (menu.Mode, menu.Format, menu.Difficulty, menu.View);
            try
            {
                menu.Mode = VolleyMode.Solo;
                menu.Format = VolleyFormat.Tournament;
                menu.Difficulty = VolleyDifficulty.Medium;
                menu.View = 0;
                menu.Selected = VolleyMenu.Row.Difficulty;
                yield return new WaitForSeconds(1f);
                yield return Screenshot("volley_menu.png");
                _game.Continue();
                Assert.AreEqual(VolleyGame.Phase.Intro, _game.Current);
                Assert.IsNotNull(_game.Tournament);
                var opponents = new List<int>();
                float strength = -1f;
                for (int round = 0; round < VolleyTournament.Rounds; round++)
                {
                    yield return WaitFor(() => _game.Current == VolleyGame.Phase.Playing, 10f);
                    Assert.AreEqual(round, _game.Tournament.Round);
                    Assert.Greater(_game.Match.Level.Strength, strength, "each round a stronger pair");
                    strength = _game.Match.Level.Strength;
                    opponents.Add(_game.Tournament.Opponent);
                    if (round == 0)
                    {
                        yield return new WaitForSeconds(3f);
                        yield return Screenshot("volley_match.png");
                        menu.View = 1;
                        yield return new WaitForSeconds(1.2f);
                        yield return Screenshot("volley_view_high.png");
                        menu.View = 0;
                    }
                    _game.Match.Award(0);
                    yield return WaitFor(() => _game.Current is VolleyGame.Phase.Bracket or VolleyGame.Phase.Results, 10f);
                    if (round == VolleyTournament.Rounds - 1) break;
                    Assert.AreEqual(VolleyGame.Phase.Bracket, _game.Current);
                    if (round == 0)
                    {
                        yield return new WaitForSeconds(0.5f);
                        yield return Screenshot("volley_bracket.png");
                    }
                    _game.Continue();
                }
                Assert.AreEqual(VolleyGame.Phase.Results, _game.Current);
                Assert.IsTrue(_game.Tournament.Won);
                Assert.AreEqual(cupsBefore + 1, PlayerPrefs.GetInt(cupKey, 0), "the cup counted for Medium");
                Assert.AreEqual(3, opponents.Distinct().Count(), "three different pairs");
                yield return new WaitForSeconds(2f);
                yield return Screenshot("volley_champion.png");
            }
            finally
            {
                PlayerPrefs.SetInt(cupKey, cupsBefore);
                (menu.Mode, menu.Format, menu.Difficulty, menu.View) = saved;
                menu.Save();
            }
        }

        static IEnumerator WaitFor(System.Func<bool> done, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(done(), "timed out waiting");
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
