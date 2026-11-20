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
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Boxing.Tests
{
    /// <summary>
    /// Superboks! in its scene: the climb's map and every stage (its intro, its fight from behind the see-through hero;
    /// screenshots boxing_*.png in unity/captures), a fight played by the scripted boxer (fake_sender --script box: punches
    /// of every kind, a held guard, dodges by stepping aside, a duck, a flurry, both arms up) through the camera pipeline,
    /// and a won fight: the knockdown's shot, the referee's count, the knockout from the side and the front, the results.
    /// </summary>
    public class BoxingGameTests
    {
        const int MotionPort = 5031;

        Process _sender;
        BoxingGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            BoxingGame.Saving = false;
            PlayerPrefs.DeleteKey("boxing.progress");
            SceneManager.LoadScene("Boxing");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<BoxingGame>();
            Assert.IsNotNull(_game, "Boxing scene has no BoxingGame");
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
            BoxingGame.Saving = true;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);
            yield return null;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator TheMap_AndEveryStage_FromTheMapAndFromTheFight()
        {
            yield return new WaitForSeconds(1.2f);
            yield return Screenshot("boxing_map.png");
            _game.OpenAll(BoxingDifficulty.Easy);
            for (int stage = 0; stage < Campaign.Stages.Length; stage++)
            {
                int fight = stage * Campaign.FightsPerStage + Campaign.FightsPerStage - 1;
                _game.Choose(BoxingDifficulty.Easy, fight);
                yield return new WaitForSeconds(0.8f);
                yield return Screenshot($"boxing_stage{stage}_map.png");
                _game.BeginIntro();
                yield return new WaitForSeconds(0.7f);
                yield return Screenshot($"boxing_stage{stage}_intro.png");
                yield return new WaitForSeconds(1.5f);
                if (stage == 0) yield return Screenshot("boxing_intro_vs.png");
                float deadline = Time.realtimeSinceStartup + 8f;
                while (Time.realtimeSinceStartup < deadline && _game.Current != BoxingGame.Phase.Fight) yield return null;
                Assert.AreEqual(BoxingGame.Phase.Fight, _game.Current, "the fight starts after the intro");
                yield return new WaitForSeconds(2.6f);
                yield return Screenshot($"boxing_stage{stage}_fight.png");
                Log($"stage {stage}: {Campaign.Opponent(fight).Id} ({_game.Bout.Opponent.Name}), the opponent {_game.Bout.State}");
                // back to the map for the next stage
                typeof(BoxingGame).GetMethod("SetPhase", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(_game, new object[] { BoxingGame.Phase.Waiting });
            }
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator TheScriptedBoxer_Punches_Guards_Dodges_Ducks_ThroughTheCamera()
        {
            int landed = 0, blocked = 0, thrown = 0, impacts = 0;
            var kinds = new HashSet<Blow>();
            bool body = false, guardSeen = false, leanLeft = false, leanRight = false, duckSeen = false, armsUp = false, hintShot = false;
            _game.BoutEvents += e =>
            {
                if (e.Kind == BoutEventKind.Thrown)
                {
                    thrown++;
                    kinds.Add(e.Blow);
                    body |= e.Body;
                }
                if (e.Kind == BoutEventKind.Landed && e.Outcome is Outcome.Hit or Outcome.Interrupted) landed++;
                if (e.Kind == BoutEventKind.Landed && e.Outcome == Outcome.Blocked) blocked++;
                if (e.Kind == BoutEventKind.Impact) impacts++;
            };
            StartFakePlayer("box");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && (MotionSystem.Instance.ActiveBody == null || !MotionSystem.Instance.ActiveBody.IsTracking)) yield return null;
            Assert.IsNotNull(MotionSystem.Instance.ActiveBody, "the scripted boxer is seen");
            // both hands up on the map start the fight (the first one, easy)
            deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline && _game.Current == BoxingGame.Phase.Waiting) yield return null;
            if (_game.Current == BoxingGame.Phase.Waiting) _game.BeginIntro();
            deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && _game.Current != BoxingGame.Phase.Fight) yield return null;
            Assert.AreEqual(BoxingGame.Phase.Fight, _game.Current);
            bool shotFight = false, shotHit = false, shotGuard = false, shotLean = false, shotDuck = false;
            int lastLanded = 0;
            deadline = Time.realtimeSinceStartup + 75f;
            while (Time.realtimeSinceStartup < deadline && _game.Current == BoxingGame.Phase.Fight)
            {
                var b = _game.Bout;
                var pose = b.LastPose;
                guardSeen |= pose.Guard;
                leanLeft |= pose.Lean < 0;
                leanRight |= pose.Lean > 0;
                duckSeen |= pose.Duck;
                armsUp |= pose.ArmsUp;
                if (!shotFight && b.Phase == BoutPhase.Fighting && b.Time > 4.0)
                {
                    shotFight = true;
                    yield return Screenshot("boxing_fight.png");
                }
                // its punch coming at the camera (no prompt: its body shows it)
                if (!hintShot && b.State == Foe.Strike && b.StateTime > b.StateLength * 0.5f)
                {
                    hintShot = true;
                    yield return Screenshot("boxing_incoming.png");
                }
                if (!shotHit && landed > lastLanded && landed >= 3)
                {
                    shotHit = true;
                    yield return Screenshot("boxing_hit.png");
                }
                lastLanded = landed;
                if (!shotGuard && pose.Guard && b.Phase == BoutPhase.Fighting)
                {
                    shotGuard = true;
                    yield return Screenshot("boxing_guard.png");
                }
                if (!shotLean && pose.Lean != 0 && b.Phase == BoutPhase.Fighting)
                {
                    shotLean = true;
                    yield return Screenshot("boxing_lean.png");
                }
                if (!shotDuck && pose.Duck && b.Phase == BoutPhase.Fighting)
                {
                    shotDuck = true;
                    yield return Screenshot("boxing_duck.png");
                }
                if (b.Phase == BoutPhase.Down)
                {
                    yield return Screenshot(b.DownFighter == 0 ? "boxing_down_hero.png" : "boxing_down_foe.png");
                    while (_game.Current == BoxingGame.Phase.Fight && _game.Bout.Phase == BoutPhase.Down) yield return null;
                }
                yield return null;
            }
            Log($"thrown {thrown} (kinds {string.Join(", ", kinds)}, body {body}), landed {landed}, blocked {blocked}, their punches {impacts}; " +
                $"guard {guardSeen}, lean left {leanLeft}, right {leanRight}, duck {duckSeen}, arms up {armsUp}; moves: glitches {_game.Moves.GlitchesIgnored}, raises {_game.Moves.RaisesIgnored}");
            Assert.GreaterOrEqual(thrown, 15, "the scripted punches reach the fight");
            Assert.GreaterOrEqual(landed, 5, "and land");
            Assert.That(kinds.Contains(Blow.Straight), "jabs");
            Assert.That(kinds.Contains(Blow.Uppercut) || kinds.Contains(Blow.Hook), "an uppercut or a hook");
            Assert.That(guardSeen, "the guard is seen");
            Assert.That(leanLeft && leanRight, "dodges both ways");
            Assert.That(duckSeen, "the duck");
            Assert.That(armsUp, "both arms up");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AWonFight_ShowsTheResults_AndOpensTheNext()
        {
            yield return new WaitForSeconds(0.8f);
            _game.Choose(BoxingDifficulty.Easy, 0);
            _game.BeginIntro();
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && _game.Current != BoxingGame.Phase.Fight) yield return null;
            Assert.AreEqual(BoxingGame.Phase.Fight, _game.Current);
            deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline && _game.Bout.Phase != BoutPhase.Fighting) yield return null;
            // knock it down until it stays down: punches from the keys (no camera here)
            for (int down = 0; down < 6 && _game.Bout.Phase != BoutPhase.Over; down++)
            {
                while (_game.Bout.Phase is BoutPhase.Down or BoutPhase.Starting or BoutPhase.Rest) yield return null;
                if (_game.Bout.Phase == BoutPhase.Over) break;
                _game.Bout.SetHealth(1f);
                _game.Bout.HoldGuard(Guard.Open, 2f);
                _game.Bout.Punch(_game.Bout.Time, true, Blow.Uppercut, false, 5f);
                yield return new WaitForSeconds(0.4f);
                if (down == 0) yield return Screenshot("boxing_knockdown.png");
                if (down == 0)
                {
                    yield return new WaitForSeconds(2.2f);
                    yield return Screenshot("boxing_count.png");
                }
                deadline = Time.realtimeSinceStartup + 14f;
                while (Time.realtimeSinceStartup < deadline && _game.Bout.Phase == BoutPhase.Down) yield return null;
            }
            Assert.AreEqual(BoutPhase.Over, _game.Bout.Phase);
            Assert.IsTrue(_game.Bout.HeroWon);
            yield return new WaitForSeconds(0.6f);
            yield return Screenshot("boxing_ko_side.png");
            yield return new WaitForSeconds(2.0f);
            yield return Screenshot("boxing_ko_front.png");
            deadline = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < deadline && _game.Current != BoxingGame.Phase.Result) yield return null;
            Assert.AreEqual(BoxingGame.Phase.Result, _game.Current);
            yield return new WaitForSeconds(2.6f);
            yield return Screenshot("boxing_results.png");
            Assert.IsTrue(_game.Progress.Won(BoxingDifficulty.Easy, 0));
            Assert.IsTrue(_game.Progress.Unlocked(BoxingDifficulty.Easy, 1));
            Assert.AreEqual(1, _game.SelectedFight, "the map moved on to the next fight");
        }

        static void Log(string text) => Debug.Log($"[BoxingTest] {text}");

        void StartFakePlayer(string script)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python, $"fake_sender.py --script {script} --port {MotionPort}")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
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
