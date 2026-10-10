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

namespace MoveIt.Games.Paint.Tests
{
    /// <summary>
    /// Pikturo! end to end: motion-service-py/fake_sender.py --script paint (a player who holds the right hand on the
    /// colour column, then paints with the right hand while raising the left one as the switch: a circle, then a line
    /// at a new spot reached with the switch down) -> UDP -> the Paint scene.
    /// Screenshots go to unity/captures/ for a visual check.
    /// </summary>
    public class PaintGameTests
    {
        const int MotionPort = 5012, VideoPort = 5016, ControlPort = 5017;

        Process _sender;
        PaintGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            CameraFeed.VideoPort = VideoPort;
            CameraFeed.ControlPort = ControlPort;
            SceneManager.LoadScene("Paint");
            yield return null;
            yield return null;
            _game = Object.FindAnyObjectByType<PaintGame>();
            Assert.IsNotNull(_game, "Paint scene has no PaintGame");

            // listen on a test port (a real camera service may be running on 5005), and ignore the mouse
            var motion = MotionSystem.Instance;
            motion.enabled = false;
            typeof(MotionSystem).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(motion, MotionPort);
            motion.enabled = true;
            typeof(BodyHands).GetField("mouseFallback", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(BodyHands.Instance, false);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_sender != null && !_sender.HasExited) _sender.Kill();
            _sender?.Dispose();
            _sender = null;
            if (MotionSystem.Instance != null) Object.Destroy(MotionSystem.Instance.gameObject);   // survives scene loads
            CameraFeed.VideoPort = 5006;
            CameraFeed.ControlPort = 5007;
            yield return null;
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator PaperMode_OneHandPaints_TheOtherIsTheSwitch()
        {
            Assert.AreEqual(PaintGame.Mode.Paper, _game.CurrentMode);
            Assert.AreEqual(0, _game.ColorIndex);
            if (!_game.RightHanded) _game.SwapHands();   // the script paints with the right hand
            yield return RunFakePlayer();
            yield return Screenshot("paint_paper.png");

            Assert.AreEqual(5, _game.ColorIndex, "holding the right hand on the colour column picks the 6th colour (blue)");
            var px = _game.Canvas.ReadPixels();
            int painted = 0;
            foreach (var p in px) if (p.a > 32) painted++;
            UnityEngine.Debug.Log($"[Test] painted pixels: {painted}");
            Assert.Greater(painted, 15000, "a circle and a line at brush size M");

            // the circle (centre 0.62, 0.55; radii 0.16 x 0.2 of the screen) and the line (x 0.3, y 0.75 -> 0.35)
            Assert.IsTrue(PaintedNear(px, 0.46f, 0.55f), "left side of the circle");
            Assert.IsTrue(PaintedNear(px, 0.62f, 0.75f), "top of the circle");
            Assert.IsTrue(PaintedNear(px, 0.62f, 0.35f), "bottom of the circle");
            Assert.IsTrue(PaintedNear(px, 0.3f, 0.55f), "the line painted at the new spot");
            Assert.AreEqual(2, _game.StrokeCount, "two strokes: one per time the switch went up");
            Assert.IsTrue(IsBlue(px, 0.46f, 0.55f), "painted in the colour that was picked");
            // the hand travels up from the hip to the circle's start and back down: neither may leave a line
            Assert.IsFalse(PaintedNear(px, 0.735f, 0.28f, 14), "raising the hand to its start point must not paint");
            Assert.IsFalse(PaintedNear(px, 0.72f, 0.18f, 14), "dropping the arm after painting must not leave a tail");
            Assert.IsFalse(PaintedNear(px, 0.83f, 0.25f, 14), "moving to the colour column must not paint");
            Assert.IsFalse(PaintedNear(px, 0.49f, 0.36f, 14), "moving to the line's start with the switch down must not paint");
            Assert.IsFalse(PaintedNear(px, 0.22f, 0.48f, 14), "the switch hand itself never paints");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator CameraMode_ShowsTheCameraPictureAndPaintsOverIt()
        {
            if (!_game.RightHanded) _game.SwapHands();
            _game.SetMode(PaintGame.Mode.Camera);
            yield return RunFakePlayer();
            yield return Screenshot("paint_camera.png");

            var feed = _game.Feed;
            Assert.IsNotNull(feed, "camera mode asks for the camera picture");
            Assert.Greater(feed.FramesReceived, 100, "frames arrive while the mode is on");
            Assert.AreEqual(640, feed.Width);
            Assert.AreEqual(360, feed.Height);
            UnityEngine.Debug.Log($"[Test] camera feed {feed.Fps:0} fps, decode {feed.DecodeMs:0.0} ms, crop {_game.CameraCrop}");
            // framed on the player: from the knees (1 torso below the hips) to 1.3 torso lengths above the shoulders
            Assert.Less(_game.CameraCrop.height, 0.95f, "the picture is zoomed in on the player");
            Assert.Greater(_game.CameraCrop.height, 0.5f);
            Assert.Greater(_game.Canvas.CountPainted(), 5000, "the player painted over the camera picture");

            _game.SetMode(PaintGame.Mode.Paper);
            yield return null;
            Assert.IsNull(_game.Feed, "leaving camera mode stops the camera picture");
        }

        /// <summary>
        /// Every tool once, on the dino colouring page, for a visual check (unity/captures/paint_tools.png): bucket fills
        /// bounded by the page lines, then one stroke per brush, the rainbow with symmetry on. Also saves a photo.
        /// </summary>
        [UnityTest, Timeout(30000)]
        public IEnumerator AllTools_OnAColouringPage()
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(PaintGame).GetField("_introUntil", flags).SetValue(_game, 0f);
            typeof(PaintGame).GetMethod("NextPage", flags).Invoke(_game, null);
            Assert.AreEqual(0, _game.PageIndex, "first colouring page");
            var walls = (bool[])typeof(PaintGame).GetMethod("Walls", flags).Invoke(_game, new object[] { 0 });
            Assert.IsNotNull(walls, "the page is readable, so its lines stop the bucket");
            var canvas = _game.Canvas;
            Vector2 C(float sx, float sy) => new Vector2(sx * canvas.Width, sy * canvas.Height);

            int body = canvas.Fill(C(0.47f, 0.35f), PaintGame.Palette[3], walls);
            int volcano = canvas.Fill(C(0.79f, 0.25f), PaintGame.Palette[8], walls);
            UnityEngine.Debug.Log($"[Test] bucket: dino body {body} px, volcano {volcano} px");
            Assert.Greater(body, 20000, "the dino's body is a big closed area");
            Assert.Less(body, 200000, "...but the fill must not leak out of it");
            Assert.Greater(volcano, 10000);
            Assert.Less(volcano, 120000);

            var tools = new[]
            {
                (PaintTool.Marker, BrushShape.Round, 0), (PaintTool.Rainbow, BrushShape.Round, 1), (PaintTool.Spray, BrushShape.Round, 4),
                (PaintTool.Neon, BrushShape.Round, 6), (PaintTool.Glitter, BrushShape.Round, 7), (PaintTool.Stamp, BrushShape.Star, 2),
                (PaintTool.Stamp, BrushShape.Heart, 7), (PaintTool.Stamp, BrushShape.Footprint, 9), (PaintTool.Eraser, BrushShape.Round, 0),
            };
            for (int i = 0; i < tools.Length; i++)
            {
                var (tool, shape, color) = tools[i];
                var s = new BrushSettings { Tool = tool, StampShape = shape, Color = PaintGame.Palette[color], Size = 28f, Mirror = tool == PaintTool.Rainbow };
                var stroke = new BrushStroke(i);
                float y = 0.86f - i * 0.055f;
                // the eraser rubs across the marker line and the bucket-filled egg
                var from = tool == PaintTool.Eraser ? C(0.2f, 0.86f) : C(0.11f, y);
                stroke.Begin(canvas, s, from);
                for (int k = 1; k <= 24; k++)
                {
                    float t = k / 24f;
                    var p = tool == PaintTool.Eraser ? Vector2.Lerp(C(0.2f, 0.88f), C(0.2f, 0.62f), t)
                                                     : C(0.11f + 0.3f * t, y + 0.015f * Mathf.Sin(t * Mathf.PI * 2f));
                    stroke.MoveTo(canvas, s, p, 1f / 30f);
                }
                stroke.End(canvas, s);
            }
            canvas.Flush();
            yield return null;
            yield return Screenshot("paint_tools.png");

            _game.SavePhoto();
            Assert.IsTrue(File.Exists(_game.LastPhoto), "the photo is saved on the box");
            UnityEngine.Debug.Log($"[Test] photo {_game.LastPhoto} ({new FileInfo(_game.LastPhoto).Length / 1024} KB)");
            File.Copy(_game.LastPhoto, Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures/paint_photo.png")), true);
            File.Delete(_game.LastPhoto);   // don't leave test pictures in the gallery
        }

        bool PaintedNear(Color32[] px, float sx, float sy, int radius = 22)
        {
            var c = _game.Canvas;
            int cx = (int)(sx * c.Width), cy = (int)(sy * c.Height);
            for (int y = Mathf.Max(0, cy - radius); y <= Mathf.Min(c.Height - 1, cy + radius); y++)
            for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(c.Width - 1, cx + radius); x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius && px[y * c.Width + x].a > 64) return true;
            return false;
        }

        bool IsBlue(Color32[] px, float sx, float sy, int radius = 22)
        {
            var c = _game.Canvas;
            int cx = (int)(sx * c.Width), cy = (int)(sy * c.Height);
            for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                var p = px[Mathf.Clamp(y, 0, c.Height - 1) * c.Width + Mathf.Clamp(x, 0, c.Width - 1)];
                if (p.a > 200) return p.b > 200 && p.r < 120;
            }
            return false;
        }

        IEnumerator RunFakePlayer()
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python,
                $"fake_sender.py --script paint --port {MotionPort} --video-control-port {ControlPort} --once")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            float deadline = Time.realtimeSinceStartup + 40f;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(_sender.HasExited, "fake_sender.py --script paint did not finish in time");
            yield return new WaitForSecondsRealtime(0.6f);   // the last wet paint dries
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
