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

namespace MoveIt.Games.Sword.Tests
{
    /// <summary>
    /// Shpata!'s duel with the scripted player (fake_sender.py --script sword: sword in the right hand, shield in the
    /// left): a swing at the knight's guard bounces off its shield; the shield held overhead, left and right stops a blow
    /// from there (the test makes the knight strike while each is held), and every block opens the knight for a
    /// counterattack that lands; a blow nobody blocks costs a heart. Screenshots: unity/captures/sword_*.png.
    /// </summary>
    public class SwordGameTests
    {
        const int MotionPort = 5013;

        Process _sender;
        SwordGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Sword");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<SwordGame>();
            Assert.IsNotNull(_game, "Sword scene has no SwordGame");
            var motion = MotionSystem.Instance;
            motion.enabled = false;
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(motion, MotionPort);
            motion.enabled = true;
            _game.Practice = true;   // the knight only attacks when the test says so
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
        public IEnumerator Duel_GuardBouncesSwings_ShieldBlocksOpenTheKnight_UnblockedBlowsHurt()
        {
            var swings = new List<Cut>();
            var blocked = new List<(AttackFrom from, bool shield)>();
            _game.SwingEnded += swings.Add;
            _game.AttackBlocked += (from, shield) => blocked.Add((from, shield));
            var sides = new[] { AttackFrom.Overhead, AttackFrom.Left, AttackFrom.Right };
            var held = new Dictionary<AttackFrom, float>();
            foreach (var s in sides) held[s] = 0f;
            var attacked = new HashSet<AttackFrom>();
            int clangsBeforeBlocks = -1, hitsAfterBlocks = 0;
            float guardTime = 0f;
            bool unblockedSent = false, shotGuard = false, shotSpot = false, shotCounter = false;

            yield return RunFakePlayer(() =>
            {
                var k = _game.CurrentKnight;
                foreach (var s in sides)
                {
                    held[s] = _game.ShieldCoversSpot(s) ? held[s] + Time.deltaTime : 0f;
                    // the shield held over a spot for a moment while the knight guards: it strikes there (once per side,
                    // early in the hold, so the blow lands while the scripted arm still holds the shield)
                    if (!attacked.Contains(s) && held[s] > 0.2f && held[s] < 0.5f && _game.ForceWindUp(s, 0.5f))
                    {
                        attacked.Add(s);
                        if (clangsBeforeBlocks < 0) clangsBeforeBlocks = _game.Clangs;
                        Log($"knight strikes {s} while the shield is there");
                    }
                }
                if (blocked.Count > 0 && k != null && k.State == KnightState.Staggered) hitsAfterBlocks = _game.Hits;
                // after the three blocks, with the arms down: a blow nobody blocks
                guardTime = k != null && k.State == KnightState.Guard ? guardTime + Time.deltaTime : 0f;
                if (!unblockedSent && blocked.Count == 3 && guardTime > 1.0f && _game.ForceWindUp(AttackFrom.Overhead, 0.5f))
                {
                    unblockedSent = true;
                    Log("knight strikes overhead while the arms are down");
                }
                if (!shotGuard && k != null && k.State == KnightState.Guard && k.StateTime > 0.5f) { shotGuard = true; return "sword_guard.png"; }
                if (!shotSpot && k != null && k.State == KnightState.WindUp && k.StateTime > 0.3f) { shotSpot = true; return "sword_block.png"; }
                if (!shotCounter && k != null && k.State == KnightState.Staggered && k.StateTime > 0.4f) { shotCounter = true; return "sword_dizzy.png"; }
                return null;
            });

            Log($"swings {string.Join(", ", swings)}; blocked {string.Join(", ", blocked)}; clangs {_game.Clangs} " +
                $"({clangsBeforeBlocks} before the blocks), hits {_game.Hits} ({hitsAfterBlocks} by the counterattacks), " +
                $"hearts {_game.Hearts}, knight hp {_game.CurrentKnight?.Hp}, score {_game.Score}");
            Assert.AreEqual(SwordGame.Phase.Playing, _game.Current, "raising both hands started the duel");
            Assert.GreaterOrEqual(clangsBeforeBlocks, 1, "the first swing bounced off the guarding knight's shield");
            foreach (var s in sides)
                Assert.IsTrue(blocked.Contains((s, true)), $"the shield held {s} stops a blow from there");
            Assert.GreaterOrEqual(hitsAfterBlocks, 3, "each block opened the knight, and the counterattack landed");
            Assert.IsTrue(unblockedSent, "the test sent a blow at the lowered arms");
            Assert.AreEqual(4, _game.Hearts, "the one blow nobody blocked cost a heart (5 → 4)");
            foreach (var cut in new[] { Cut.Right, Cut.Left, Cut.Down })
                Assert.Contains(cut, swings, $"a {cut} swing is recognised");
        }

        static void Log(string text) => UnityEngine.Debug.Log($"[Test] {text}");

        /// <summary>Runs the scripted player; everyFrame may return a screenshot file name.</summary>
        IEnumerator RunFakePlayer(System.Func<string> everyFrame)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python, $"fake_sender.py --script sword --port {MotionPort} --once")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            float deadline = Time.realtimeSinceStartup + 50f;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline)
            {
                string shot = everyFrame();
                if (shot != null) yield return Screenshot(shot);
                else yield return null;
            }
            Assert.IsTrue(_sender.HasExited, "fake_sender.py --script sword did not finish in time");
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
