using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Motion.Tests
{
    public sealed class HandCursorTests
    {
        [Test]
        public void Presence_RidesThroughUnsureFrames_AndLosesTheHandAfterTheGrace()
        {
            var presence = new HandPresence();
            double t = 10.0;
            Assert.IsFalse(presence.Sample(0.4f, t), "an unsure frame doesn't find a hand");
            Assert.IsFalse(presence.Tracked(t));
            Assert.IsTrue(presence.Sample(0.9f, t += 0.033), "a confident one does");
            Assert.IsTrue(presence.Sample(0.35f, t += 0.033), "once found, a dip to 0.35 is still used (like the skeleton mirror)");
            Assert.IsFalse(presence.Sample(0.1f, t += 0.033), "a frame the AI really can't see is skipped...");
            Assert.IsTrue(presence.Tracked(t), "...but the hand isn't dropped for it");
            Assert.IsTrue(presence.Tracked(t + 0.2), "nor for a few in a row");
            Assert.IsFalse(presence.Tracked(t + 0.4), "lost once nothing usable came for the grace time");
            Assert.IsFalse(presence.Sample(0.4f, t + 0.4), "and found again only on a confident frame");
        }

        [Test]
        public void Cursor_MovesEvenlyEveryFrame_WhenSamplesArriveUnevenly()
        {
            // A hand sweeping at 1 zone width per second; the camera samples it 30 times a second, but each sample
            // reaches the game 30-55 ms later (uneven inference time) and is handled on the next 60 Hz frame.
            var cursor = new HandInterpolator(1f, 4f, 2f) { Margin = 0.5f, MaxExtrapolation = 0.08f };
            var rng = new System.Random(3);
            const double interval = 1.0 / 30.0, frame = 1.0 / 60.0;
            double nextCapture = 0, arrival = 0.03 + rng.NextDouble() * 0.025;
            float previous = float.NaN, minStep = float.MaxValue, maxStep = 0f;
            for (int f = 0; f < 180; f++)
            {
                double now = 0.1 + f * frame;
                while (arrival <= now)
                {
                    cursor.Sample(nextCapture, new Vector2((float)nextCapture, 0.5f), false, now);
                    nextCapture += interval;
                    arrival = nextCapture + 0.03 + rng.NextDouble() * 0.025;
                }
                if (!cursor.HasSample) continue;
                float x = cursor.Evaluate(now, (float)frame, out _).x;
                if (f > 60 && !float.IsNaN(previous))   // after the filters settled
                {
                    minStep = Mathf.Min(minStep, x - previous);
                    maxStep = Mathf.Max(maxStep, x - previous);
                }
                previous = x;
            }
            // Now 0.93..1.08 frames' worth. Timed by the frame that received each sample (until 2026-10-09), the steps
            // ranged from 0.2 to 1.7: the cursor stalled and lurched on a hand moving at a steady speed.
            Assert.Greater(minStep, (float)frame * 0.75f, $"never stalls (steps {minStep:0.0000}..{maxStep:0.0000})");
            Assert.Less(maxStep, (float)frame * 1.25f, $"never jumps (steps {minStep:0.0000}..{maxStep:0.0000})");
        }

        [Test]
        public void Cursor_GlidesOnWhenTheNextSampleIsLate_ButNotFurtherThanAllowed()
        {
            var cursor = new HandInterpolator(100f, 0f, 1f) { Margin = 0f, MaxExtrapolation = 0.08f };   // (filter nearly off)
            const double latency = 0.04;
            for (int i = 0; i <= 10; i++)
            {
                double t = i / 30.0;
                cursor.Sample(t, new Vector2((float)t, 0f), false, t + latency);
            }
            double last = 10 / 30.0;
            float soon = cursor.Evaluate(last + latency + 0.03, 1f / 60f, out _).x;
            Assert.AreEqual(last + 0.03, soon, 0.005, "keeps moving at the hand's speed while the next sample is due");
            float later = soon;
            for (int f = 1; f <= 30; f++) later = cursor.Evaluate(last + latency + 0.03 + f / 60.0, 1f / 60f, out _).x;
            Assert.AreEqual(last + 0.08, later, 0.005, "then waits: a hand that stopped isn't chased off the screen");
        }

        [Test]
        public void Fit_DrawsASteadySweepWhereItWas_WithSteadierSteps()
        {
            // A hand sweeping at 1 unit/s, sampled 30 times a second with camera noise (sd 0.01), 40 ms latency. Pikturo's
            // brush (a parabola through the last 0.25 s) against straight lines between the same samples (filters off).
            var rng = new System.Random(7);
            var fit = new HandInterpolator(100f, 0f, 1f) { Margin = 1f, FitWindow = 0.25f };
            var lines = new HandInterpolator(100f, 0f, 1f) { Margin = 1f };
            double fitSum = 0, fitSteps = 0, lineSteps = 0;
            int n = 0, sample = 0;
            Vector2 previousFit = default, previousLine = default;
            for (int f = 0; f < 480; f++)
            {
                double now = 0.05 + f / 60.0;
                for (; sample / 30.0 + 0.04 <= now; sample++)
                {
                    double t = sample / 30.0;
                    var p = new Vector2((float)t + Noise(rng), Noise(rng));
                    fit.Sample(t, p, false, t + 0.04);
                    lines.Sample(t, p, false, t + 0.04);
                }
                Vector2 a = fit.Evaluate(now, 1f / 60f, out _), b = lines.Evaluate(now, 1f / 60f, out _);
                if (f > 60)   // after a second: the window is full
                {
                    fitSum += a.x - (now - 0.04 - 1.0 / 30.0);   // where the hand was, one camera frame back
                    var hand = new Vector2(1f / 60f, 0f);       // how far it really moved this frame
                    fitSteps += (a - previousFit - hand).sqrMagnitude;
                    lineSteps += (b - previousLine - hand).sqrMagnitude;
                    n++;
                }
                previousFit = a;
                previousLine = b;
            }
            double fitRms = System.Math.Sqrt(fitSteps / n), lineRms = System.Math.Sqrt(lineSteps / n);
            Assert.AreEqual(0.0, fitSum / n, 0.002, "a steady move is drawn where the hand was, not behind it (a filter lags)");
            // Python, same setup: 0.81. Most of the brush's gain comes later, in its One Euro filter: a curve without a
            // corner at every sample keeps the filter's speed estimate, and so its smoothing, steady.
            Assert.Less(fitRms, 0.92 * lineRms, $"steadier steps: {fitRms:0.00000} vs {lineRms:0.00000} between samples");
        }

        [Test]
        public void Fit_NeverRunsOnPastTheNewestSample()
        {
            // A hand moving at 1 unit/s whose samples stop coming: a parabola continued past its samples would shoot off.
            var cursor = new HandInterpolator(100f, 0f, 1f) { Margin = 1f, FitWindow = 0.25f };
            for (int i = 0; i <= 12; i++)
            {
                double t = i / 30.0;
                cursor.Sample(t, new Vector2((float)t, 0f), false, t + 0.04);
            }
            double last = 12 / 30.0;
            float x = 0f;
            for (int f = 0; f < 30; f++) x = cursor.Evaluate(last + 0.04 + f / 60.0, 1f / 60f, out _).x;
            Assert.AreEqual(last, x, 0.003, "waits at the newest sample");
        }

        [Test]
        public void Rope_IgnoresShakeSmallerThanTheString_AndTrailsAPull()
        {
            var rope = new CursorRope(6f);
            Assert.AreEqual(new Vector2(100f, 100f), rope.Follow(new Vector2(100f, 100f)), "starts at the cursor");
            var rng = new System.Random(5);
            for (int i = 0; i < 60; i++)
            {
                var shake = new Vector2((float)rng.NextDouble() * 8f - 4f, (float)rng.NextDouble() * 8f - 4f);
                Assert.AreEqual(new Vector2(100f, 100f), rope.Follow(new Vector2(100f, 100f) + shake), "a hand held still paints nothing new");
            }
            Vector2 tip = default;
            for (int x = 100; x <= 160; x += 3) tip = rope.Follow(new Vector2(x, 100f));
            Assert.AreEqual(154f, tip.x, 0.001f, "a pull drags it along, one string length behind");
            Assert.AreEqual(100f, tip.y, 0.001f);
            rope.Reset();
            Assert.AreEqual(new Vector2(10f, 20f), rope.Follow(new Vector2(10f, 20f)), "after a reset it starts at the cursor again");
        }

        static float Noise(System.Random rng)   // normal, sd 0.01
        {
            double u = 1.0 - rng.NextDouble(), v = rng.NextDouble();
            return (float)(0.01 * System.Math.Sqrt(-2.0 * System.Math.Log(u)) * System.Math.Cos(2.0 * System.Math.PI * v));
        }
    }
}
