using System;
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

namespace MoveIt.Games.Dress.Tests
{
    /// <summary>
    /// Vishu! end to end: motion-service-py/fake_sender.py (a scripted player and a drawn camera picture) -> UDP -> the
    /// Dress scene. Screenshots go to unity/captures/dress_*.png for a visual check.
    /// </summary>
    public class DressGameTests
    {
        const int MotionPort = 5015, VideoPort = 5018, ControlPort = 5019;

        Process _sender;
        DressGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            foreach (var key in new[] { "dress.look", "dress.progress", "dress.rightHand" }) PlayerPrefs.DeleteKey(key);
            CameraFeed.VideoPort = VideoPort;
            CameraFeed.ControlPort = ControlPort;
            SceneManager.LoadScene("Dress");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<DressGame>();
            Assert.IsNotNull(_game, "Dress scene has no DressGame");
            var motion = MotionSystem.Instance;
            motion.enabled = false;
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(motion, MotionPort);
            motion.enabled = true;
            typeof(BodyHands).GetField("mouseFallback", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(BodyHands.Instance, false);
            typeof(DressGame).GetField("_introUntil", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_game, 0f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_sender != null && !_sender.HasExited) _sender.Kill();
            _sender?.Dispose();
            _sender = null;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);
            // the game saves the outfit when it goes away: let it, then forget it (the next test starts fresh)
            if (_game != null) Object.Destroy(_game.gameObject);
            yield return null;
            CameraFeed.VideoPort = 5006;
            CameraFeed.ControlPort = 5007;
            foreach (var key in new[] { "dress.look", "dress.progress", "dress.rightHand" }) PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        /// <summary>The scripted player dresses itself with its hands: categories on the left, items on the right.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator Mirror_TheHandsDressThePlayer_AndTheClothesFollow()
        {
            Assert.AreEqual(0, _game.Look.Count, "a fresh start");
            Start("dress", once: true);
            bool shotWings = false, shotArms = false;
            float deadline = Time.realtimeSinceStartup + 60f;
            Vector2 crownBefore = default;
            float start = Time.realtimeSinceStartup;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline)
            {
                float t = Time.realtimeSinceStartup - start;
                if (!shotWings && _game.Look.Effect != Effect.None)
                {
                    shotWings = true;
                    crownBefore = _game.Outfit.SlotCenter[(int)Slot.Head];
                    yield return Screenshot("dress_mirror.png");
                }
                if (shotWings && !shotArms && t > 31.6f)   // the script's "arms up and out" (30.4 .. 32.8 s)
                {
                    shotArms = true;
                    yield return Screenshot("dress_arms_up.png");
                }
                yield return null;
            }
            Assert.IsTrue(_sender.HasExited, "fake_sender.py --script dress did not finish in time");
            yield return Screenshot("dress_end.png");

            var feed = _game.Feed;
            Assert.IsNotNull(feed);
            Assert.Greater(feed.FramesReceived, 100, "the camera picture arrives");
            Assert.IsFalse(_game.ShowingDoll, "the player is dressed, not the doll");
            UnityEngine.Debug.Log($"[Test] look {_game.Look.Save()}, crop {_game.Crop}, torso {_game.Pose.T:0} px");
            Assert.AreEqual("bottom_ballgown", _game.Look.Worn(Slot.Bottom)?.Id, "left hand on Skirts, right hand on the first one");
            Assert.AreEqual("top_princess", _game.Look.Worn(Slot.Top)?.Id);
            Assert.AreEqual("hair_long", _game.Look.Worn(Slot.Hair)?.Id);
            Assert.AreEqual("head_crown", _game.Look.Worn(Slot.Head)?.Id);
            Assert.AreEqual("back_fairy", _game.Look.Worn(Slot.Back)?.Id);
            Assert.AreEqual(1, _game.Look.Shade[(int)Slot.Back], "choosing the wings again gave them their second colour");
            Assert.AreEqual(Effect.Sparkles, _game.Look.Effect, "the magic button");
            Assert.IsNull(_game.Look.Worn(Slot.Face), "nothing else was put on by accident");
            Assert.IsNull(_game.Look.Worn(Slot.Hand));
            Assert.AreEqual(Slot.Back, _game.Category);
            // the clothes are drawn on the body: the crown above the shoulders, the skirt below them
            var outfit = _game.Outfit;
            Assert.IsTrue(outfit.SlotDrawn[(int)Slot.Head] && outfit.SlotDrawn[(int)Slot.Bottom]);
            Assert.Greater(outfit.SlotCenter[(int)Slot.Head].y, _game.Pose.ShoulderC.y + 0.5f * _game.Pose.T);
            Assert.Less(outfit.SlotCenter[(int)Slot.Bottom].y, _game.Pose.ShoulderC.y - 0.8f * _game.Pose.T);
            Assert.AreNotEqual(crownBefore, outfit.SlotCenter[(int)Slot.Head], "the crown moved with the player");
            Assert.Greater(outfit.Glints.Count, 5, "gems and sequins to twinkle");
        }

        /// <summary>
        /// The scripted player turns 45 and 90 degrees, 60 the other way, then spins: the outfit wraps around the body
        /// (front and back) and is drawn all along; screenshots at each angle (dress_turn_*.png).
        /// </summary>
        [UnityTest, Timeout(90000)]
        public IEnumerator Turning_TheClothesWrapAroundTheBody()
        {
            foreach (var (slot, item) in new[] { (Slot.Top, 0), (Slot.Bottom, 0), (Slot.Hair, 0), (Slot.Head, 0), (Slot.Back, 0) })
                _game.Wear(slot, item);
            _game.Look.Effect = Effect.None;
            Start("turn", once: true);
            yield return WaitFor(() => _game.Pose.Valid && !_game.ShowingDoll, 20f, "the player is seen");
            var shots = new (float deg, string name)[] { (40f, "45"), (80f, "90"), (-55f, "left60"), (135f, "135"), (178f, "back"), (270f, "270") };
            var taken = new bool[shots.Length];
            float maxYaw = 0f, minYaw = 0f;
            int frames = 0, missing = 0;
            float deadline = Time.realtimeSinceStartup + 45f;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline)
            {
                float yaw = _game.Pose.Yaw * Mathf.Rad2Deg;
                maxYaw = Mathf.Max(maxYaw, yaw);
                minYaw = Mathf.Min(minYaw, yaw);
                if (!_game.ShowingDoll)
                {
                    frames++;
                    if (!_game.Outfit.SlotDrawn[(int)Slot.Top] || !_game.Outfit.SlotDrawn[(int)Slot.Bottom]) missing++;
                }
                for (int i = 0; i < shots.Length; i++)
                {
                    if (taken[i] || (shots[i].deg > 0f ? yaw < shots[i].deg : yaw > shots[i].deg)) continue;
                    taken[i] = true;
                    UnityEngine.Debug.Log($"[Test] turned {yaw:0} deg (head {_game.Pose.HeadYaw * Mathf.Rad2Deg:0}): dress_turn_{shots[i].name}.png");
                    yield return Screenshot($"dress_turn_{shots[i].name}.png");
                }
                yield return null;
            }
            Assert.IsTrue(_sender.HasExited, "fake_sender.py --script turn did not finish in time");
            UnityEngine.Debug.Log($"[Test] turn range {minYaw:0} .. {maxYaw:0} deg, {missing} of {frames} frames without the dress");
            Assert.Greater(maxYaw, 300f, "the spin is followed all the way round");
            Assert.Less(minYaw, -50f, "the turn to the left");
            Assert.AreEqual(0, missing, "the dress is drawn at every angle");
            Assert.IsTrue(Array.TrueForAll(taken, x => x), "every angle was reached");
        }

        /// <summary>A fashion show: the theme, dressing, the runway (three poses the scripted player strikes), the judges.</summary>
        [UnityTest, Timeout(150000)]
        public IEnumerator Show_ThePosesCount_AndTheJudgesGiveStars()
        {
            typeof(DressGame).GetField("poseSeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_game, 12f);
            Start("runway", once: false);
            yield return WaitFor(() => _game.Pose.Valid && !_game.ShowingDoll, 20f, "the player is seen");
            _game.StartShow();
            Assert.AreEqual(DressGame.Mode.ShowIntro, _game.CurrentMode);
            yield return WaitFor(() => _game.CurrentMode == DressGame.Mode.ShowDressing, 8f, "dressing time");
            _game.Look.Surprise(new System.Random(3), _game.ShowTheme);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Screenshot("dress_show_dressing.png");
            _game.StartWalk(ShowPose.Star, ShowPose.Hips, ShowPose.Heart);
            yield return new WaitForSecondsRealtime(1.0f);
            yield return Screenshot("dress_runway_curtains.png");
            yield return WaitFor(() => _game.PoseIndex >= 1 || _game.CurrentMode != DressGame.Mode.ShowWalk, 30f, "the first pose");
            yield return Screenshot("dress_runway.png");
            yield return WaitFor(() => _game.CurrentMode == DressGame.Mode.ShowJudges, 60f, "all three poses");
            yield return new WaitForSecondsRealtime(6f);
            yield return Screenshot("dress_judges.png");
            yield return WaitFor(() => _game.CurrentMode == DressGame.Mode.ShowResults, 20f, "the results");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Screenshot("dress_results.png");

            UnityEngine.Debug.Log($"[Test] theme {_game.ShowTheme}, poses {string.Join(", ", _game.PoseScores)}, stars {_game.ShowStars}");
            foreach (var score in _game.PoseScores) Assert.GreaterOrEqual(score, 0.75f, "every pose was struck and held");
            Assert.GreaterOrEqual(_game.ShowStars, 9, "a themed surprise look with good poses");
            Assert.LessOrEqual(_game.ShowStars, 15);
            Assert.AreEqual(_game.ShowStars, _game.Progress.Stars, "the stars are kept");
            Assert.AreEqual(1, _game.Progress.Shows);
            Assert.IsNotNull(_game.LastPhoto, "a photo on the runway");
            CopyAndDelete(_game.LastPhoto, "dress_runway_photo.png");
        }

        /// <summary>Nobody in front of the camera: the paper doll wears the outfit; a photo is saved.</summary>
        [UnityTest, Timeout(30000)]
        public IEnumerator Doll_WearsTheOutfit_WhenNobodyIsThere()
        {
            _game.Look.Surprise(new System.Random(11), Theme.Fairy);
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.IsTrue(_game.ShowingDoll);
            Assert.IsTrue(_game.Pose.Valid);
            Assert.IsTrue(_game.Outfit.SlotDrawn[(int)Slot.Top] && _game.Outfit.SlotDrawn[(int)Slot.Bottom]);
            yield return Screenshot("dress_doll.png");
            _game.TakePhoto();
            Assert.IsTrue(File.Exists(_game.LastPhoto));
            CopyAndDelete(_game.LastPhoto, "dress_photo.png");
            _game.NextMagic();
            Assert.AreNotEqual(Effect.None, _game.Look.Effect);
            // every magic effect for a moment, for a look (unity/captures/dress_magic_*.png)
            foreach (Effect e in new[] { Effect.Butterflies, Effect.Hearts, Effect.Snow, Effect.Bubbles })
            {
                _game.Look.Effect = e;
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Screenshot($"dress_magic_{e.ToString().ToLowerInvariant()}.png");
            }
        }

        /// <summary>Every item on the doll, six looks (item k of every slot), for a visual check (dress_items_k.png).</summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator AllItems_OnTheDoll()
        {
            for (int k = 0; k < 6; k++)
            {
                _game.Look.Clear();
                for (int s = 0; s < DressCatalog.SlotCount; s++) _game.Wear((Slot)s, k);
                _game.Look.Effect = Effect.None;
                yield return new WaitForSecondsRealtime(1.2f);
                for (int s = 0; s < DressCatalog.SlotCount; s++)
                    Assert.IsTrue(_game.Outfit.SlotDrawn[s], $"{DressCatalog.Items[s][k].Id} is drawn");
                yield return Screenshot($"dress_items_{k}.png");
            }
            // the same without particles (glints, poofs), to tell what is what
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                var particles = new ParticleSystem.Particle[ps.particleCount];
                ps.GetParticles(particles);
                UnityEngine.Debug.Log($"[Test] {ps.name}: {particles.Length} particles " + string.Join("; ", System.Linq.Enumerable.Select(particles,
                    p => $"at {p.position} size {p.GetCurrentSize3D(ps)} life {p.remainingLifetime:0.00}/{p.startLifetime:0.00}")));
                ps.GetComponent<ParticleSystemRenderer>().enabled = false;
            }
            yield return Screenshot("dress_items_nofx.png");
        }

        void Start(string script, bool once)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python,
                $"fake_sender.py --script {script} --port {MotionPort} --video-control-port {ControlPort}{(once ? " --once" : "")}")
            {
                WorkingDirectory = serviceDir, UseShellExecute = false, CreateNoWindow = true,
            });
        }

        static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), $"timed out waiting for {what}");
        }

        static void CopyAndDelete(string photo, string name)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            Directory.CreateDirectory(dir);
            File.Copy(photo, Path.Combine(dir, name), true);
            File.Delete(photo);   // don't leave test pictures in the gallery
        }

        static IEnumerator Screenshot(string name)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            Directory.CreateDirectory(dir);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name));
            yield return null;
            yield return null;
        }
    }
}
