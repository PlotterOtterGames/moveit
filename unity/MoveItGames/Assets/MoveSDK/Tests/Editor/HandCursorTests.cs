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
    }
}
