using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Motion.Tests
{
    public sealed class JointTimelineTests
    {
        const double Camera = 1.0 / 29.8;   // the X98K Pro's USB camera

        /// <summary>
        /// A joint sweeping at a steady 1 unit per second, sampled by the camera; each sample reaches the game 40-52 ms
        /// later (the NPU's time varies) and is handled at the start of the next game frame. Returns the smallest and
        /// largest step between frames, in frames' worth of the sweep (1 = perfectly even), for the newest sample as it
        /// arrived and for the timeline.
        /// </summary>
        static (float min, float max, float newestMin, float newestMax) Sweep(double fps, int seed)
        {
            var timeline = new JointTimeline();
            var rng = new System.Random(seed);
            var joints = new Vector3[Skeleton.JointCount];
            var shown = new Vector3[Skeleton.JointCount];
            double capture = 0, arrival = 0.04 + rng.NextDouble() * 0.012;
            float newest = float.NaN, previousShown = float.NaN, previousNewest = float.NaN;
            float min = float.MaxValue, max = 0f, newestMin = float.MaxValue, newestMax = 0f;
            float even = (float)(1.0 / fps);
            for (int f = 0; f < (int)(6 * fps); f++)
            {
                double now = 0.1 + f / fps;
                while (arrival <= now)
                {
                    for (int j = 0; j < joints.Length; j++) joints[j] = new Vector3((float)capture, j, 0f);
                    timeline.Add(capture, joints, now);
                    newest = (float)capture;
                    capture += Camera;
                    arrival = capture + 0.04 + rng.NextDouble() * 0.012;
                }
                if (!timeline.Evaluate(now, shown)) continue;
                if (f > fps && !float.IsNaN(previousShown))   // after the first second (the latency estimate settled)
                {
                    float step = (shown[(int)Joint.RightWrist].x - previousShown) / even;
                    min = Mathf.Min(min, step);
                    max = Mathf.Max(max, step);
                    float newestStep = (newest - previousNewest) / even;
                    newestMin = Mathf.Min(newestMin, newestStep);
                    newestMax = Mathf.Max(newestMax, newestStep);
                    Assert.AreEqual((float)(int)Joint.RightWrist, shown[(int)Joint.RightWrist].y, 1e-4f, "every joint keeps its own track");
                }
                previousShown = shown[(int)Joint.RightWrist].x;
                previousNewest = newest;
            }
            return (min, max, newestMin, newestMax);
        }

        [Test]
        public void Body_MovesEvenlyEveryFrame_OnA30And60FpsScreen()
        {
            foreach (double fps in new[] { 30.0, 60.0 })
                for (int seed = 1; seed <= 3; seed++)
                {
                    var (min, max, newestMin, newestMax) = Sweep(fps, seed);
                    // the newest sample as it arrives (how the skeleton mirror drew until 2026-10-31) stalls and jumps
                    Assert.Less(newestMin, 0.5f, $"{fps} fps, seed {seed}: the newest pose as it arrives stalls (steps {newestMin:0.00}..{newestMax:0.00})");
                    Assert.Greater(newestMax, 1.5f, $"{fps} fps, seed {seed}: and jumps");
                    Assert.Greater(min, 0.85f, $"{fps} fps, seed {seed}: never stalls (steps {min:0.00}..{max:0.00})");
                    Assert.Less(max, 1.15f, $"{fps} fps, seed {seed}: never jumps (steps {min:0.00}..{max:0.00})");
                }
        }

        [Test]
        public void Body_GlidesOnALittle_WhenTheSamplesStop_ThenWaits()
        {
            var timeline = new JointTimeline { Margin = 0.5f, MaxExtrapolation = 0.08f };
            var joints = new Vector3[Skeleton.JointCount];
            var shown = new Vector3[Skeleton.JointCount];
            double capture = 0;
            for (; capture < 1.0; capture += Camera)
            {
                for (int j = 0; j < joints.Length; j++) joints[j] = new Vector3((float)capture, 0f, 0f);   // 1 unit per second
                timeline.Add(capture, joints, capture + 0.05);
            }
            float last = joints[0].x;
            for (double now = capture + 0.05; now < capture + 1.5; now += 1.0 / 60.0)   // the player is lost: no samples
            {
                timeline.Evaluate(now, shown);
                Assert.LessOrEqual(shown[0].x, last + 0.08f + 1e-4f, "at most 80 ms of gliding past the newest sample");
            }
            Assert.AreEqual(last + 0.08f, shown[0].x, 0.01f, "then it waits there");
        }

        [Test]
        public void Body_ComesToRestWhereTheJointStopped()
        {
            var timeline = new JointTimeline();
            var joints = new Vector3[Skeleton.JointCount];
            var shown = new Vector3[Skeleton.JointCount];
            double capture = 0;
            float x = 0f, most = float.MinValue;
            for (int i = 0; i < 90; i++, capture += Camera)
            {
                x = i < 45 ? (float)capture * 2f : x;   // a fast move, then still
                for (int j = 0; j < joints.Length; j++) joints[j] = new Vector3(x, 0f, 0f);
                double now = capture + 0.05;
                timeline.Add(capture, joints, now);
                timeline.Evaluate(now, shown);
                most = Mathf.Max(most, shown[0].x);
            }
            Assert.AreEqual(x, shown[0].x, 1e-4f, "rests exactly on the joint");
            Assert.Less(most - x, 2f * 0.08f, "an overshoot at the stop is at most the glide");
        }

        [Test]
        public void Body_StartsOver_WhenTheCaptureClockJumps()
        {
            // The capture times are the sender's Unix time: when the box sets its clock, they jump by seconds. Waiting for
            // samples newer than the old ones would hold the skeleton still that long.
            var timeline = new JointTimeline();
            var joints = new Vector3[Skeleton.JointCount];
            var shown = new Vector3[Skeleton.JointCount];
            double capture = 100.0, now = 5.0;
            for (int i = 0; i < 30; i++, capture += Camera, now += Camera) timeline.Add(capture, joints, now);
            capture -= 6.0;   // the clock was set back 6 s
            for (int i = 0; i < 5; i++, capture += Camera, now += Camera)
            {
                for (int j = 0; j < joints.Length; j++) joints[j] = new Vector3(1f + i, 0f, 0f);
                timeline.Add(capture, joints, now);
            }
            timeline.Evaluate(now, shown);
            Assert.Greater(shown[0].x, 3.5f, "follows the new samples at once");
        }
    }
}
