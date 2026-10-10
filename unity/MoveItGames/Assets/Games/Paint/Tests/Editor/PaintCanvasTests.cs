using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Paint.Tests
{
    /// <summary>The painting itself (GPU stamps, read back on the CPU), the paint bucket, undo, and the pen rule.</summary>
    public class PaintCanvasTests
    {
        const int W = 128, H = 64;
        PaintCanvas _canvas;
        Material _brush;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("Tundu/Brush");
            Assume.That(shader != null && shader.isSupported, "Tundu/Brush shader not available");
            _brush = new Material(shader);
            _canvas = new PaintCanvas(W, H, _brush);
        }

        [TearDown]
        public void TearDown()
        {
            _canvas?.Dispose();
            if (_brush != null) Object.DestroyImmediate(_brush);
        }

        Color32 At(Color32[] px, int x, int y) => px[y * W + x];

        [Test]
        public void Stamp_NearTheTop_IsAtTheTopOfThePicture()
        {
            _canvas.Stamp(new Vector2(64, 56), 10f, 0f, Color.red, BrushShape.Round);
            var px = _canvas.ReadPixels();
            var top = At(px, 64, 56);
            Assert.Greater(top.a, 200, "the stamp's centre should be painted");
            Assert.Greater(top.r, 200);
            Assert.Less(top.g, 40);
            Assert.AreEqual(0, At(px, 64, 8).a, "the bottom must stay empty (y is up, like the screen)");
            Assert.AreEqual(0, At(px, 64, 40).a, "the stamp is only 10 px across");
        }

        [Test]
        public void Fill_StaysInsideAClosedRing()
        {
            for (int a = 0; a < 360; a += 3)
            {
                float r = a * Mathf.Deg2Rad;
                _canvas.Stamp(new Vector2(64 + 20 * Mathf.Cos(r), 32 + 20 * Mathf.Sin(r)), 4f, 0f, Color.black, BrushShape.Round);
            }
            int filled = _canvas.Fill(new Vector2(64, 32), Color.blue);
            var px = _canvas.ReadPixels();
            Assert.Greater(filled, 800, "the inside of a radius-20 ring is ~1000 px");
            Assert.Less(filled, 1400);
            Assert.Greater(At(px, 64, 32).b, 200, "centre filled blue");
            Assert.AreEqual(0, At(px, 5, 5).a, "outside the ring stays empty");
        }

        [Test]
        public void Fill_StopsAtPageLines()
        {
            var walls = new bool[W * H];
            for (int y = 0; y < H; y++) walls[y * W + 64] = true;   // a vertical line down the middle
            _canvas.Fill(new Vector2(20, 20), Color.green, walls);
            var px = _canvas.ReadPixels();
            Assert.Greater(At(px, 20, 20).g, 200, "left side filled");
            Assert.AreEqual(0, At(px, 100, 20).a, "the line keeps the right side empty");
        }

        [Test]
        public void Undo_BringsBackThePictureBeforeTheStroke()
        {
            _canvas.Stamp(new Vector2(30, 30), 12f, 0f, Color.red, BrushShape.Round);
            _canvas.PushUndo();
            _canvas.Stamp(new Vector2(90, 30), 12f, 0f, Color.blue, BrushShape.Round);
            Assert.Greater(At(_canvas.ReadPixels(), 90, 30).a, 200);
            Assert.IsTrue(_canvas.Undo());
            var px = _canvas.ReadPixels();
            Assert.AreEqual(0, At(px, 90, 30).a, "the blue stamp is undone");
            Assert.Greater(At(px, 30, 30).a, 200, "the red one, painted before, stays");
            Assert.IsFalse(_canvas.Undo(), "only one step was saved");
        }

        [Test]
        public void Eraser_MakesPaintTransparentAgain()
        {
            _canvas.Stamp(new Vector2(64, 32), 30f, 0f, Color.yellow, BrushShape.Round);
            _canvas.Flush();
            _canvas.Stamp(new Vector2(64, 32), 12f, 0f, Color.white, BrushShape.Round, erase: true);
            var px = _canvas.ReadPixels();
            Assert.Less(At(px, 64, 32).a, 10, "erased in the middle");
            Assert.Greater(At(px, 52, 32).a, 200, "paint around the eraser stays");
        }

        [Test]
        public void Eraser_RightAfterPainting_InTheSameBatch_StillErases()
        {
            _canvas.Stamp(new Vector2(64, 32), 30f, 0f, Color.yellow, BrushShape.Round);
            _canvas.Stamp(new Vector2(64, 32), 12f, 0f, Color.white, BrushShape.Round, erase: true);
            _canvas.Stamp(new Vector2(90, 32), 8f, 0f, Color.red, BrushShape.Round);
            var px = _canvas.ReadPixels();
            Assert.Less(At(px, 64, 32).a, 10, "stamps are drawn in the order they were made");
            Assert.Greater(At(px, 90, 32).a, 200, "paint after the eraser still lands");
        }

        [Test]
        public void Mirror_RepeatsStrokesOnTheOtherSide()
        {
            var stroke = new BrushStroke(1);
            var s = new BrushSettings { Tool = PaintTool.Marker, Color = Color.red, Size = 6f, Mirror = true };
            stroke.Begin(_canvas, s, new Vector2(20, 20));
            stroke.MoveTo(_canvas, s, new Vector2(30, 40), 0.03f);
            stroke.MoveTo(_canvas, s, new Vector2(40, 20), 0.03f);
            stroke.End(_canvas, s);
            var px = _canvas.ReadPixels();
            Assert.Greater(At(px, 20, 20).a, 200);
            Assert.Greater(At(px, W - 20, 20).a, 200, "mirrored stroke on the right");
        }

        [Test]
        public void WetPaint_ShowsAtOnce_DriesLater_AndATailCanBeWiped()
        {
            _canvas.WetSeconds = 0.5f;
            _canvas.Clock = 10f;
            _canvas.Stamp(new Vector2(20, 32), 8f, 0f, Color.red, BrushShape.Round, owner: 0);    // the stroke
            _canvas.Clock = 10.3f;
            _canvas.Stamp(new Vector2(60, 32), 8f, 0f, Color.red, BrushShape.Round, owner: 0);    // the arm dropping
            _canvas.Stamp(new Vector2(100, 32), 8f, 0f, Color.blue, BrushShape.Round, owner: 1);  // the other hand
            _canvas.Dry();
            _canvas.Flush();
            _canvas.Present();
            Assert.AreEqual(3, _canvas.WetCount, "nothing is 0.5 s old yet");
            Assert.AreNotSame(_canvas.Texture, _canvas.Shown, "wet paint is shown on a copy of the picture");

            Assert.AreEqual(1, _canvas.Wipe(0, 10.2f), "only hand 0's stamps from 10.2 s on");
            _canvas.Clock = 11f;
            var px = _canvas.ReadPixels();
            Assert.Greater(At(px, 20, 32).a, 200, "the stroke dried into the picture");
            Assert.AreEqual(0, At(px, 60, 32).a, "the wiped tail never reaches the picture");
            Assert.Greater(At(px, 100, 32).a, 200, "the other hand's paint is kept");
        }

        // ---------------------------------------------------------------- the pen rule

        [Test]
        public void Pen_PaintsOnlyWhileTheSwitchIsUp()
        {
            var pen = new PaintPen();
            var p = new Vector2(0.5f, 0.5f);
            Assert.AreEqual(0, pen.UpdateSwitch(true, p, true, true, false, false), "switch down: the brush moves freely");
            Assert.AreEqual(PenState.Aiming, pen.State);
            Assert.AreEqual(1, pen.UpdateSwitch(true, p, true, true, false, true), "switch up: paints at once");
            Assert.AreEqual(0, pen.UpdateSwitch(true, p + Vector2.one * 0.1f, true, true, false, true), "keeps painting while it stays up");
            Assert.AreEqual(-1, pen.UpdateSwitch(true, p, true, true, false, false), "switch down: stops at once");
            pen.UpdateSwitch(true, p, true, true, false, true);
            Assert.AreEqual(-1, pen.UpdateSwitch(true, p, true, false, false, true), "over a toolbar the brush never paints");
            Assert.AreEqual(PenState.Aiming, pen.State, "...but the cursor stays, to press buttons");
            Assert.AreEqual(0, pen.UpdateSwitch(true, p, true, true, true, true), "the painting arm resting at the side: no paint");
            Assert.AreEqual(PenState.Away, pen.State);
        }

        [Test]
        public void Switch_TurnsOnHighAndOffLow_SoItDoesNotFlicker()
        {
            const float on = 0.6f, off = 0.35f;
            Assert.IsFalse(PaintGame.SwitchState(false, 0.5f, on, off), "below 'on': stays off");
            Assert.IsTrue(PaintGame.SwitchState(false, 0.65f, on, off), "raised to the chest: on");
            Assert.IsTrue(PaintGame.SwitchState(true, 0.45f, on, off), "sagging a little doesn't switch it off");
            Assert.IsFalse(PaintGame.SwitchState(true, 0.3f, on, off), "lowered: off");
        }

        [Test]
        public void Interpolator_FollowsTheSamples_NeverPastAStop_AndCarriesTheSwitch()
        {
            const double latency = 0.04, interval = 1.0 / 30.0;
            // the hand moves right at 1 unit/s for 0.5 s, then stops at x = 0.5; the switch goes up at 0.2 s
            bool flagSeenBeforeX = false;
            float maxX = 0f;
            double local = 1000.0 + latency;
            var probe = new HandInterpolator(4f, 1f, 1f);
            for (int i = 0; i <= 30; i++)   // replay: feed samples as they arrive, evaluate every 60 fps frame
            {
                double t = 1000.0 + i * interval;
                probe.Sample(t, new Vector2(Mathf.Min(0.5f, (float)(i * interval)), 0.3f), i * interval >= 0.2, t + latency);
                for (int f = 0; f < 2; f++)
                {
                    local = t + latency + f / 60.0;
                    var p = probe.Evaluate(local, 1f / 60f, out bool flag);
                    maxX = Mathf.Max(maxX, p.x);
                    if (flag && p.x < 0.15f) flagSeenBeforeX = true;
                }
            }
            for (int f = 0; f < 60; f++) maxX = Mathf.Max(maxX, probe.Evaluate(local + f / 60.0, 1f / 60f, out _).x);
            Assert.LessOrEqual(maxX, 0.5f + 1e-4f, "never slides past where the hand stopped");
            Assert.Greater(maxX, 0.49f, "but gets there");
            Assert.IsFalse(flagSeenBeforeX, "the switch comes on where the brush was when it went up (x = 0.2), not earlier");
        }
    }
}
