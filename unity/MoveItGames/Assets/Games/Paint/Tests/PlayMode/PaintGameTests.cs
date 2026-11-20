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
    /// Pikturo! end to end: motion-service-py/fake_sender.py plays it (scripts paint, paint2, paint_page) -> UDP -> the
    /// Paint scene. The scripted hands aim at HUD points of a 1920x1080 screen (the paper at x 432..1488, y 160..952);
    /// the tests look at the same points of the paper, whatever shape the Game view has. Screenshots go to
    /// unity/captures/ for a visual check.
    /// </summary>
    public class PaintGameTests
    {
        const int MotionPort = 5012, VideoPort = 5016, ControlPort = 5017;
        const int Blue = 6;

        Process _sender;
        PaintGame _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PlayerPrefs.DeleteKey("paint.way");          // every test starts the easy way, right-handed
            PlayerPrefs.DeleteKey("paint.rightHanded");
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
            PlayerPrefs.DeleteKey("paint.way");
            PlayerPrefs.DeleteKey("paint.rightHanded");
            yield return null;
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator TheEasyWay_RaiseAHandAndItPaints_LowerItAndItStops()
        {
            Assert.AreEqual(PaintGame.Mode.Paper, _game.CurrentMode);
            Assert.AreEqual(PaintWay.Easy, _game.Way, "the easy way is the default");
            Assert.AreEqual(0, _game.ColorIndex);
            yield return Screenshot("paint_howto_easy.png", waitForFigure: true);
            yield return RunFakePlayer("paint");
            yield return Screenshot("paint_easy.png");

            Assert.AreEqual(Blue, _game.ColorIndex, "holding the right hand on the blue pot picks it");
            var px = _game.Canvas.ReadPixels();
            int painted = 0;
            foreach (var p in px) if (p.a > 32) painted++;
            UnityEngine.Debug.Log($"[Test] painted pixels: {painted}, strokes {_game.StrokeCount}");
            Assert.Greater(painted, 30000, "a circle and a line at brush size M");
            AssertCircleAndLine(px);
            Assert.AreEqual(2, _game.StrokeCount, "two strokes: one per time the hand came up and stopped");
            // the hand travels up from the hip to each start point and back down: none of that may leave a line
            Assert.IsFalse(PaintedNear(px, 1214, 800, 14), "raising the hand to the circle's start must not paint");
            Assert.IsFalse(PaintedNear(px, 1424, 838, 14), "moving to the colour pot (across the paper's corner) must not paint");
            Assert.IsFalse(PaintedNear(px, 933, 700, 14), "raising the hand to the line's start must not paint");
            Assert.IsFalse(PaintedNear(px, 924, 900, 14), "dropping the arm after the line must not leave a tail");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator TwoHands_OneHandPaints_TheOtherIsTheSwitch()
        {
            _game.SetWay(PaintWay.TwoHands);
            Assert.IsTrue(_game.RightHanded, "the script paints with the right hand");
            yield return Screenshot("paint_howto_two.png", waitForFigure: true);
            yield return RunFakePlayer("paint2");
            yield return Screenshot("paint_two_hands.png");

            Assert.AreEqual(Blue, _game.ColorIndex);
            var px = _game.Canvas.ReadPixels();
            AssertCircleAndLine(px);
            Assert.AreEqual(2, _game.StrokeCount, "two strokes: one per time the switch went up");
            Assert.IsFalse(PaintedNear(px, 1214, 800, 14), "raising the hand to its start point must not paint");
            Assert.IsFalse(PaintedNear(px, 1424, 838, 14), "moving to the colour pot must not paint");
            Assert.IsFalse(PaintedNear(px, 933, 700, 14), "moving to the line's start with the switch down must not paint");
            Assert.IsFalse(PaintedNear(px, 524, 527, 14), "the switch hand itself never paints");
        }

        /// <summary>The scripts' circle (centre 1050, 520, radius 150) and line (x 700, y 330 -> 700), in blue.</summary>
        void AssertCircleAndLine(Color32[] px)
        {
            Assert.IsTrue(PaintedNear(px, 900, 520), "left side of the circle");
            Assert.IsTrue(PaintedNear(px, 1050, 370), "top of the circle");
            Assert.IsTrue(PaintedNear(px, 1050, 670), "bottom of the circle");
            Assert.IsTrue(PaintedNear(px, 1200, 520), "right side of the circle, where it starts and ends");
            Assert.IsFalse(PaintedNear(px, 1050, 520, 60), "...and nothing in its middle: it is a circle");
            Assert.IsTrue(PaintedNear(px, 700, 520), "the line painted at the new spot");
            Assert.IsTrue(PaintedNear(px, 700, 350), "...from its top");
            Assert.IsTrue(PaintedNear(px, 700, 680), "...to its bottom");
            Assert.IsTrue(IsBlue(px, 900, 520), "painted in the colour that was picked");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator CameraMode_ShowsTheCameraPictureAndPaintsOverIt()
        {
            _game.SetMode(PaintGame.Mode.Camera);
            yield return RunFakePlayer("paint");
            yield return Screenshot("paint_camera.png");

            var feed = _game.Feed;
            Assert.IsNotNull(feed, "camera mode asks for the camera picture");
            Assert.Greater(feed.FramesReceived, 100, "frames arrive while the mode is on");
            Assert.AreEqual(640, feed.Width);
            Assert.AreEqual(360, feed.Height);
            UnityEngine.Debug.Log($"[Test] camera feed {feed.Fps:0} fps, decode {feed.DecodeMs:0.0} ms, crop {_game.CameraCrop}");
            // framed on the player: from 0.75 torso lengths below the hips to 1.15 above the shoulders
            Assert.Less(_game.CameraCrop.height, 0.8f, "the picture is zoomed in on the player");
            Assert.Greater(_game.CameraCrop.height, 0.4f);
            Assert.AreEqual(Blue, _game.ColorIndex, "the colour pots hang on the player's picture, within the same reach");
            Assert.AreEqual(2, _game.StrokeCount);
            Assert.Greater(_game.Canvas.CountPainted(), 5000, "the player painted over the camera picture");

            _game.SetMode(PaintGame.Mode.Paper);
            yield return null;
            Assert.IsNull(_game.Feed, "leaving camera mode stops the camera picture");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator AColouringPage_ThePaintStaysInsideTheLines_AndTheAreaFillsUp()
        {
            _game.ShowPage(0);
            yield return null;
            var page = _game.Regions;
            Assert.IsNotNull(page, "the dino page has areas");
            int volcano = page.At(C(1338, 750).x, C(1338, 750).y), sky = page.At(C(1436, 715).x, C(1436, 715).y);
            Assert.AreNotEqual(volcano, sky);
            Assert.IsTrue(page.Counts(volcano), "the volcano counts toward the finished picture");
            Assert.IsFalse(page.Counts(sky), "the sky behind everything doesn't");
            UnityEngine.Debug.Log($"[Test] dino page: {page.Count} areas, {page.CountedTotal} to colour, volcano {page.Area(volcano)} px");

            yield return RunFakePlayer("paint_page");
            yield return Screenshot("paint_page.png");

            var px = _game.Canvas.ReadPixels();
            Assert.IsTrue(page.Done(volcano), $"rubbed to and fro across the volcano: it is done (coverage {page.Coverage(volcano):0.00})");
            Assert.IsTrue(PaintedNear(px, 1338, 750, 4), "the volcano is coloured where the hand rubbed");
            Assert.IsTrue(PaintedNear(px, 1338, 612, 3), "...and at its top, where the hand never was: it filled itself up");
            Assert.IsFalse(PaintedNear(px, 1436, 715, 5), "the hand went 24 px over the volcano's right edge: no paint in the sky");
            Assert.IsFalse(PaintedNear(px, 1240, 715, 5), "...nor over its left edge");
            Assert.Less(page.Coverage(sky), 0.01f);
            Assert.AreEqual(1, page.CountedDone);
            Assert.AreEqual(0, _game.PicturesFinished, "one area of ten: the picture isn't finished yet");
        }

        /// <summary>
        /// Every tool once, on the dino colouring page, for a visual check (unity/captures/paint_tools.png): the bucket,
        /// then one stroke per brush in the sky, the rainbow with symmetry on. Saves a photo, then finishes the picture
        /// with the bucket ("Bravo!", paint_bravo.png), and a look at the dark sheet with the glowing brushes.
        /// </summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator EveryTool_OnTheDinoPage_APhoto_AndAFinishedPicture()
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(PaintGame).GetField("_introUntil", flags).SetValue(_game, 0f);
            _game.ShowPage(0);
            yield return null;
            Assert.AreEqual(0, _game.PageIndex, "first colouring page");
            var page = _game.Regions;
            var canvas = _game.Canvas;
            Vector2 S(float sx, float sy) => new Vector2(sx * canvas.Width, sy * canvas.Height);

            _game.SelectColor(4);
            _game.BucketAt(C(910, 720));     // the dino
            _game.SelectColor(9);
            _game.BucketAt(C(1338, 750));    // the volcano
            canvas.Flush();
            var px = canvas.ReadPixels();
            Assert.IsTrue(PaintedNear(px, 910, 720, 3), "the dino's body is filled");
            Assert.IsTrue(PaintedNear(px, 1080, 500, 3), "...all of it: its head too");
            Assert.IsFalse(PaintedNear(px, 910, 300, 5), "...but the fill doesn't leak into the sky");
            Assert.AreEqual(2, page.CountedDone);

            var tools = new[]
            {
                (PaintTool.Marker, BrushShape.Round, 0), (PaintTool.Rainbow, BrushShape.Round, 1), (PaintTool.Spray, BrushShape.Round, 5),
                (PaintTool.Neon, BrushShape.Round, 7), (PaintTool.Glitter, BrushShape.Round, 8), (PaintTool.Stamp, BrushShape.Star, 2),
                (PaintTool.Stamp, BrushShape.Heart, 8), (PaintTool.Stamp, BrushShape.Footprint, 10), (PaintTool.Eraser, BrushShape.Round, 0),
            };
            for (int i = 0; i < tools.Length; i++)
            {
                var (tool, shape, color) = tools[i];
                var s = new BrushSettings { Tool = tool, StampShape = shape, Color = PaintGame.Palette[color], Size = 30f, Mirror = tool == PaintTool.Rainbow };
                var stroke = new BrushStroke(i);
                float y = 0.67f - i * 0.03f;
                // the eraser rubs down across the lines above it
                var from = tool == PaintTool.Eraser ? S(0.12f, 0.69f) : S(0.04f, y);
                stroke.Begin(canvas, s, from);
                for (int k = 1; k <= 24; k++)
                {
                    float t = k / 24f;
                    var p = tool == PaintTool.Eraser ? Vector2.Lerp(S(0.12f, 0.69f), S(0.12f, 0.5f), t)
                                                     : S(0.04f + 0.3f * t, y + 0.012f * Mathf.Sin(t * Mathf.PI * 2f));
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

            // the rest of the picture with the bucket: when every area that counts is done, it is finished
            Assert.AreEqual(0, _game.PicturesFinished);
            for (int id = 1; id <= page.Count; id++)
            {
                if (!page.Counts(id) || page.Done(id)) continue;
                _game.SelectColor(id % PaintGame.Palette.Length);
                _game.BucketAt(page.Middle(id));
            }
            Assert.IsTrue(page.Finished);
            Assert.AreEqual(1, _game.PicturesFinished, "every area that counts is coloured: the picture is finished");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Screenshot("paint_bravo.png");

            // undo takes the last area back: not finished any more
            _game.Undo();
            Assert.IsFalse(page.Finished, "undo brings back what was coloured, too");

            // the next sheets: every page keeps its painting; after the pages comes the dark sheet
            for (int i = 0; i < _game.PageCount; i++) _game.NextPage();
            Assert.IsTrue(_game.DarkPaper);
            Assert.IsNull(_game.Regions, "a blank sheet has no areas");
            Assert.AreEqual(0, _game.Canvas.CountPainted(), "the dark sheet is empty: the dino's paint stayed on its page");
            var glow = new[] { (PaintTool.Neon, 5), (PaintTool.Glitter, 2), (PaintTool.Marker, 11), (PaintTool.Rainbow, 0), (PaintTool.Neon, 8) };
            for (int i = 0; i < glow.Length; i++)
            {
                var s = new BrushSettings { Tool = glow[i].Item1, Color = PaintGame.Palette[glow[i].Item2], Size = 30f, StampShape = BrushShape.Star };
                var stroke = new BrushStroke(20 + i);
                stroke.Begin(_game.Canvas, s, S(0.12f, 0.82f - i * 0.15f));
                for (int k = 1; k <= 40; k++)
                {
                    float t = k / 40f;
                    stroke.MoveTo(_game.Canvas, s, S(0.12f + 0.76f * t, 0.82f - i * 0.15f + 0.05f * Mathf.Sin(t * Mathf.PI * 3f)), 1f / 30f);
                }
                stroke.End(_game.Canvas, s);
            }
            _game.Canvas.Flush();
            yield return new WaitForSecondsRealtime(3.2f);   // (the "Bravo!" card is gone)
            yield return Screenshot("paint_dark.png");
            _game.NextPage();
            _game.NextPage();
            Assert.AreEqual(0, _game.PageIndex, "round again: the dino page");
            Assert.Greater(_game.Canvas.CountPainted(), 100000, "...with its painting, as it was left");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>A HUD point of the scripts' 1920x1080 screen (paper x 432..1488, y 160..952) as a canvas point.</summary>
        Vector2 C(float hudX, float hudY)
        {
            var c = _game.Canvas;
            if (_game.CurrentMode == PaintGame.Mode.Paper)
                return new Vector2((hudX - 432f) / 1056f * c.Width, (1f - (hudY - 160f) / 792f) * c.Height);
            return new Vector2(hudX / 1920f * c.Width, (1f - hudY / 1080f) * c.Height);
        }

        bool PaintedNear(Color32[] px, float hudX, float hudY, int radius = 22)
        {
            var c = _game.Canvas;
            var at = C(hudX, hudY);
            int cx = (int)at.x, cy = (int)at.y;
            for (int y = Mathf.Max(0, cy - radius); y <= Mathf.Min(c.Height - 1, cy + radius); y++)
            for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(c.Width - 1, cx + radius); x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius && px[y * c.Width + x].a > 64) return true;
            return false;
        }

        bool IsBlue(Color32[] px, float hudX, float hudY, int radius = 22)
        {
            var c = _game.Canvas;
            var at = C(hudX, hudY);
            int cx = (int)at.x, cy = (int)at.y;
            for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                var p = px[Mathf.Clamp(y, 0, c.Height - 1) * c.Width + Mathf.Clamp(x, 0, c.Width - 1)];
                if (p.a > 200) return p.b > 200 && p.r < 120;
            }
            return false;
        }

        IEnumerator RunFakePlayer(string script)
        {
            string serviceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../motion-service-py"));
            string python = Path.Combine(serviceDir, ".venv/Scripts/python.exe");
            Assume.That(File.Exists(python), $"Python venv not found at {python}");
            _sender = Process.Start(new ProcessStartInfo(python,
                $"fake_sender.py --script {script} --port {MotionPort} --video-control-port {ControlPort} --once")
            {
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            float deadline = Time.realtimeSinceStartup + 40f;
            while (!_sender.HasExited && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(_sender.HasExited, $"fake_sender.py --script {script} did not finish in time");
            yield return new WaitForSecondsRealtime(1.4f);   // the last wet paint dries
        }

        /// <param name="waitForFigure">Waits until the how-to card's figure is in the middle of its stroke (its loop is 4 s).</param>
        static IEnumerator Screenshot(string name, bool waitForFigure = false)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            Directory.CreateDirectory(dir);
            if (waitForFigure)
                while (Mathf.Abs(Mathf.Repeat(Time.unscaledTime, 4f) - 2.35f) > 0.05f) yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name));
            yield return null;
            yield return null;
        }
    }
}
