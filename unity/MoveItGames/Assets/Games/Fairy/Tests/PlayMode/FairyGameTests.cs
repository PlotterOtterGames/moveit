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

namespace MoveIt.Games.Fairy.Tests
{
    /// <summary>
    /// Zana!'s garden with the wand (2026-11-16; only spells purify since that evening; drawn freely in the air since
    /// 2026-11-17): a scripted player (fake_sender.py --script fairy: the left hand draws a lightning bolt on the left, then
    /// the right hand the shield's circle) casts them by drawing them; the game playing itself draws what would help
    /// through the boss's wave and the transformation, then lets the creatures through until the tree falls; two fairies
    /// each draw their own bolt (--script fairy_duo --alternate); the hint; the garden grown full.
    /// Nothing is saved (<see cref="FairyGame.Saving"/>). Screenshots: unity/captures/fairy_*.png.
    /// </summary>
    public class FairyGameTests
    {
        const int MotionPort = 5029;

        Process _sender;
        FairyGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("Fairy");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<FairyGame>();
            Assert.IsNotNull(_game, "Fairy scene has no FairyGame");
            _game.Saving = false;
            _game.Choose(false);
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
            if (_game != null)
            {
                _game.Autoplay = false;
                _game.Idle = false;
                _game.Choose(false);
            }
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);
            yield return null;
        }

        /// <summary>A bug walking in on <paramref name="side"/> when nothing walks there (once a wave: the waves still end).</summary>
        static void Somebody(GardenRun run, int side, ref int wave)
        {
            if (wave == run.Wave || run.Current != GardenRun.Stage.Wave) return;
            foreach (var c in run.Creatures)
                if (c.Alive && !c.Flying && c.Position.x * side > 0f) return;
            wave = run.Wave;
            run.Add(CreatureKind.Bug, side, side * 8.4f);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Solo_DrawingTheShapes_CastsTheSpells_TheWandAloneDoesNothing()
        {
            yield return new WaitForSeconds(1.5f);
            yield return Screenshot("fairy_menu.png");
            int purified = 0, shields = 0, bolts = 0, cast = 0, struck = 0, reads = 0, nothing = 0;
            bool pointed = false, castFrame = false;
            var last = Spell.Shield;
            _game.Happened += e =>
            {
                if (e.Kind == GardenEventKind.Purified) purified++;
                if (e.Kind == GardenEventKind.ShieldUp) shields++;
                if (e.Kind == GardenEventKind.Lightning)
                {
                    bolts++;
                    struck += e.Count;
                }
            };
            _game.SpellCast += (player, spell) =>
            {
                cast++;
                last = spell;
                castFrame = true;
                Log($"player {player + 1} drew the {spell}");
            };
            _game.StrokeRead += (player, read) =>
            {
                if (read.Found) reads++;
                else nothing++;
            };
            StartFakePlayer("fairy");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && (MotionSystem.Instance.ActiveBody == null || !MotionSystem.Instance.ActiveBody.IsTracking)) yield return null;
            Assert.IsNotNull(MotionSystem.Instance.ActiveBody, "the scripted player is seen");
            // the scripted player raises its hands (the start), the wave's title, then it defends
            deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline && _game.Current == FairyGame.Phase.Waiting) yield return null;
            if (_game.Current == FairyGame.Phase.Waiting) _game.Continue();
            yield return new WaitForSeconds(1f);
            yield return Screenshot("fairy_intro.png");
            deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && _game.Current == FairyGame.Phase.Intro) yield return null;
            Assert.AreEqual(FairyGame.Phase.Playing, _game.Current);
            bool shotPlay = false, shotTrail = false, shotCast = false, shotCountdown = false, shotShield = false, shotCircle = false;
            int added = 0;
            deadline = Time.realtimeSinceStartup + 95f;
            while (Time.realtimeSinceStartup < deadline && _game.Current is FairyGame.Phase.Playing or FairyGame.Phase.Transform)
            {
                var run = _game.Run;
                // something on the left for the bolts to strike (the scripted hand draws them there)
                Somebody(run, -1, ref added);
                pointed |= _game.PointsAt(0, out _);
                var caster = _game.CasterOf(0);
                if (!shotPlay && run.WaveTime > 5f)
                {
                    shotPlay = true;
                    yield return Screenshot("fairy_playing.png");
                }
                if (!shotTrail && caster.Drawing && caster.Stroke.Count > 60 && run.Charged(0))
                {
                    shotTrail = true;
                    yield return Screenshot("fairy_trail.png");
                }
                if (!shotCircle && caster.Drawing && caster.Stroke.Count > 110 && run.Charged(0) && last == Spell.Lightning)
                {
                    shotCircle = true;
                    yield return Screenshot("fairy_trail_circle.png");
                }
                if (!shotCast && castFrame && last == Spell.Lightning)
                {
                    shotCast = true;
                    yield return new WaitForSeconds(0.12f);
                    yield return Screenshot("fairy_cast.png");
                }
                if (!shotCountdown && run.RechargeOf(0) > 0.3f && run.RechargeOf(0) < run.RechargeTime * 0.6f && _game.PointsAt(0, out _))
                {
                    shotCountdown = true;
                    yield return Screenshot("fairy_countdown.png");
                }
                castFrame = false;
                if (!shotShield && run.ShieldTime > 0.5f && run.ShieldTime < GardenLevel.ShieldSeconds(run.Difficulty) - 1.2f)
                {
                    shotShield = true;
                    yield return Screenshot("fairy_shield.png");
                }
                if (bolts >= 3 && shields >= 1 && shotTrail && Time.realtimeSinceStartup > deadline - 45f) break;
                yield return null;
            }
            Log($"spells drawn {cast} (lightnings {bolts} striking {struck}, shields {shields}), strokes read {reads}, as nothing {nothing}; " +
                $"{purified} purified; wave {_game.Run.Wave}, {_game.Run.WavesCleared} saved, tree {_game.Run.TreeHealth}/{_game.Run.TreeMax}");
            Assert.IsTrue(pointed, "the raised hand points the wand");
            Assert.That(bolts, Is.GreaterThanOrEqualTo(2), "drawing the bolt casts the lightning");
            Assert.That(shields, Is.GreaterThanOrEqualTo(1), "drawing the circle casts the shield");
            Assert.That(purified, Is.GreaterThanOrEqualTo(2), "the lightning purifies what walks on its side");
            Assert.That(purified, Is.LessThanOrEqualTo(struck), "nothing purified but by a spell: the wand alone does nothing");
        }

        [UnityTest, Timeout(420000)]
        public IEnumerator Autoplay_DrawsTheSpells_TheBossTheTransformation_ThenTheTreeFalls()
        {
            _game.Autoplay = true;
            Time.timeScale = 3f;
            bool shotWave = false, shotBoss = false, shotOrb = false, shotFollow = false, transformed = false;
            int traced = 0, nothing = 0;
            _game.Happened += e =>
            {
                if (e.Kind == GardenEventKind.Transformed) transformed = true;
            };
            _game.SpellCast += (player, spell) => traced++;
            _game.StrokeRead += (player, read) =>
            {
                if (!read.Found) nothing++;
            };
            float deadline = Time.realtimeSinceStartup + 240f;
            while (Time.realtimeSinceStartup < deadline && _game.Current != FairyGame.Phase.Transform && _game.Current != FairyGame.Phase.Over)
            {
                var run = _game.Run;
                if (!shotWave && _game.Current == FairyGame.Phase.Playing && run.Wave == 3 && run.WaveTime > 6f)
                {
                    shotWave = true;
                    yield return Shot("fairy_wave.png");
                }
                if (!shotFollow && _game.CasterOf(0).Drawing && _game.CasterOf(0).Stroke.Count > 40)
                {
                    shotFollow = true;
                    yield return Shot("fairy_auto_trace.png");
                }
                if (!shotOrb && run.Current == GardenRun.Stage.Breather && run.OrbActive && run.StageTime > 0.8f)
                {
                    shotOrb = true;
                    yield return Shot("fairy_orb.png");
                }
                if (!shotBoss && run.Plan.Boss && run.Creatures.Exists(c => c.IsBoss && c.Alive && Mathf.Abs(c.Position.x) < 6f))
                {
                    shotBoss = true;
                    yield return Shot("fairy_boss.png");
                }
                yield return null;
            }
            Log($"after the fifth wave: {_game.Run.WavesCleared} saved, tree {_game.Run.TreeHealth}/{_game.Run.TreeMax}, spells drawn {traced} ({nothing} read as nothing): " +
                $"lightning {_game.CastsOf(Spell.Lightning)}, rainbow {_game.CastsOf(Spell.Rainbow)}, shield {_game.CastsOf(Spell.Shield)}, " +
                $"seeds {_game.CastsOf(Spell.Seeds)}, big magic {_game.CastsOf(Spell.Burst)}");
            Assert.AreEqual(FairyGame.Phase.Transform, _game.Current, "the fifth wave saved: the transformation");
            Assert.That(_game.Run.WavesCleared, Is.EqualTo(5));
            Assert.That(_game.CastsOf(Spell.Lightning), Is.GreaterThanOrEqualTo(5), "the game playing itself draws the lightnings");
            Assert.That(nothing, Is.LessThanOrEqualTo(traced / 5), "its own clean shapes are read");
            Assert.That(_game.CastsOf(Spell.Rainbow), Is.GreaterThanOrEqualTo(1), "and the rainbows");
            Time.timeScale = 1f;
            _game.Autoplay = false;
            yield return new WaitForSeconds(0.8f);
            yield return Screenshot("fairy_transform.png");
            _game.Continue();
            yield return new WaitForSeconds(1.2f);
            yield return Screenshot("fairy_transformed.png");
            Assert.IsTrue(transformed);
            Assert.That(_game.Run.Level, Is.EqualTo(2));
            Assert.That(_game.World.Decorations, Is.GreaterThanOrEqualTo(Mathf.Min(GardenProgress.MaxDecorations, _game.Progress.Decorations + 5)), "the garden grew a flower a wave");
            // nobody defends now: the creatures reach the tree
            _game.Idle = true;
            Time.timeScale = 4f;
            deadline = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < deadline && _game.Current != FairyGame.Phase.Over) yield return null;
            Time.timeScale = 1f;
            Assert.AreEqual(FairyGame.Phase.Over, _game.Current, "the tree fell");
            yield return new WaitForSeconds(3f);
            yield return Screenshot("fairy_over.png");
            Log($"waves saved {_game.Run.WavesCleared}, stardust {_game.Run.Stardust}, purified {_game.Run.Purified}, level {_game.Run.Level}; " +
                $"spells drawn {traced}; best {_game.LastOpened.Best}");
            Assert.That(_game.Run.Stardust, Is.GreaterThan(0));
            _game.Idle = false;
            _game.Continue();
            yield return null;
            Assert.AreEqual(FairyGame.Phase.Waiting, _game.Current, "back on the waiting screen");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Together_EachFairyDrawsHerOwnSpells()
        {
            _game.Choose(true);
            StartFakePlayer("fairy_duo", alternate: true);
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline && _game.Current == FairyGame.Phase.Waiting) yield return null;
            if (_game.Current == FairyGame.Phase.Waiting) _game.Continue();
            Assert.IsTrue(_game.Two, "two fairies");
            deadline = Time.realtimeSinceStartup + 60f;
            bool shot = false;
            int left = 0, right = 0;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_game.Current == FairyGame.Phase.Playing)
                {
                    // fairy 1 a bolt on the left, fairy 2 on the right (the scripted hands draw those)
                    Somebody(_game.Run, -1, ref left);
                    Somebody(_game.Run, 1, ref right);
                }
                if (!shot && _game.Current == FairyGame.Phase.Playing && _game.CasterOf(0).Stroke.Count > 40 && _game.CasterOf(1).Stroke.Count > 40)
                {
                    shot = true;
                    yield return Screenshot("fairy_together.png");
                }
                if (_game.CastsBy(0) >= 2 && _game.CastsBy(1) >= 2 && shot) break;
                yield return null;
            }
            Log($"spells drawn by fairy 1: {_game.CastsBy(0)}, fairy 2: {_game.CastsBy(1)}");
            Assert.That(_game.CastsBy(0), Is.GreaterThanOrEqualTo(1), "fairy 1 draws her bolt");
            Assert.That(_game.CastsBy(1), Is.GreaterThanOrEqualTo(1), "fairy 2 draws hers");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TheFairy_HasAMoveForEachSpell()
        {
            _game.Continue();   // the wave's title
            _game.Continue();   // playing
            _game.Idle = true;
            yield return new WaitForSeconds(1.5f);
            var fairy = _game.Fairies[0];
            foreach (var (move, side) in new[] { (CastMove.Up, -1), (CastMove.Out, -1), (CastMove.Out, 1), (CastMove.Wide, 0), (CastMove.Down, 0), (CastMove.Twirl, 0) })
            {
                fairy.Cast(move, side);
                yield return new WaitForSeconds(0.38f);
                Assert.IsTrue(fairy.Casting, $"{move} still on");
                yield return Screenshot($"fairy_move_{move.ToString().ToLowerInvariant()}{(side < 0 ? "_left" : side > 0 ? "_right" : "")}.png");
                yield return new WaitForSeconds(0.9f);
                Assert.IsFalse(fairy.Casting, $"{move} done");
            }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator NobodyDrawing_TheHintShowsTheHelpfulShape()
        {
            _game.Continue();   // the wave's title
            _game.Continue();   // playing
            yield return null;
            var run = _game.Run;
            run.ClearSchedule();
            run.KeepWave = true;
            run.Add(CreatureKind.Thorn, -1, -8.4f);
            yield return new WaitForSeconds(7.2f);
            Assert.That(run.Suggest(), Is.Not.Null);
            yield return Screenshot("fairy_hint.png");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TheGarden_GrownFull()
        {
            _game.World.SetDecorations(GardenProgress.MaxDecorations);
            yield return new WaitForSeconds(0.5f);
            _game.Continue();   // the stage camera
            yield return new WaitForSeconds(3.2f);
            yield return Screenshot("fairy_garden_full.png");
            Assert.That(_game.World.Decorations, Is.EqualTo(GardenProgress.MaxDecorations));
        }

        static void Log(string text) => Debug.Log($"[FairyTest] {text}");

        void StartFakePlayer(string script, bool alternate = false)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python, $"fake_sender.py --script {script} --port {MotionPort}{(alternate ? " --alternate" : "")}")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        /// <summary>A screenshot at normal speed (the game's time scale put back after).</summary>
        static IEnumerator Shot(string name)
        {
            float scale = Time.timeScale;
            Time.timeScale = 1f;
            yield return Screenshot(name);
            Time.timeScale = scale;
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
