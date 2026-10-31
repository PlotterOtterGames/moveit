using System;
using System.Collections.Generic;
using MoveIt.Motion;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Tennis.Tests
{
    /// <summary>
    /// Swings on a simulated camera: 30 fps, the player's torso 0.25 picture heights long, MediaPipe-like noise (0.9 % of
    /// a torso length still, 3 % moving slowly, ~8 % on a fast hand, as measured on a real session), through the motion
    /// SDK's low-lag track (BodyTracker's fast One Euro filter: what the game feeds the detector). Hand paths are
    /// minimum-jerk moves like a real arm's, in torso lengths from the shoulders' middle on the mirrored screen.
    /// </summary>
    public class SwingDetectorTests
    {
        const float Torso = 0.25f, Fps = 30f;
        static readonly Vector2 Shoulders = new Vector2(0.9f, 0.62f);

        /// <summary>A move of the right hand from <paramref name="from"/> to <paramref name="to"/> (torso lengths) taking
        /// <paramref name="seconds"/>, starting at <paramref name="start"/>; the left hand hangs still.</summary>
        static Func<double, (Vector2 left, Vector2 right, Vector2 shoulders)> Move(Vector2 from, Vector2 to, float seconds, float start = 1f,
                                                                                    Func<double, Vector2> body = null)
        {
            return t =>
            {
                float k = Mathf.Clamp01((float)(t - start) / seconds);
                float m = k * k * k * (10f - 15f * k + 6f * k * k);
                var offset = body?.Invoke(t) ?? Vector2.zero;
                return (new Vector2(-0.45f, -0.95f) + offset, Vector2.Lerp(from, to, m) + offset, offset);
            };
        }

        static List<SwingEvent> Run(Func<double, (Vector2 left, Vector2 right, Vector2 shoulders)> pose, float seconds, int seed = 1,
                                    float visibility = 0.95f)
        {
            var d = new SwingDetector();
            var rng = new System.Random(seed);
            // BodyTracker.FastMinCutoff / FastBeta / FastDerivativeCutoff, scaled by the torso length
            var fl = new OneEuroFilter3(1f, 4f, 1f);
            var fr = new OneEuroFilter3(1f, 4f, 1f);
            var fs = new OneEuroFilter3(1f, 4f, 1f);
            float Gauss() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2.0 * Math.PI * rng.NextDouble()));
            var swings = new List<SwingEvent>();
            Vector2 lastRight = default;
            for (int i = 0; i < seconds * Fps; i++)
            {
                double t = i / Fps;
                var (l, r, s) = pose(t);
                float speed = i > 0 ? (r - lastRight).magnitude * Fps : 0f;
                lastRight = r;
                // the moving hand is noisier (blur), the still one and the shoulders are not
                Vector2 N(float n) => new Vector2(Gauss(), Gauss()) * (n * Torso);
                float dt = 1f / Fps;
                var sh = (Vector2)fs.Filter(Shoulders + s * Torso + N(0.006f), dt, Torso);
                var lw = (Vector2)fl.Filter(Shoulders + l * Torso + N(0.009f), dt, Torso);
                var rw = (Vector2)fr.Filter(Shoulders + r * Torso + N(0.009f + Mathf.Min(0.08f, speed * 0.01f)), dt, Torso);
                if (d.Update(t, sh, Torso, lw, visibility, rw, visibility, out var e)) swings.Add(e);
            }
            return swings;
        }

        // the screen is a mirror: the player's right hand is on the screen's right (+x)
        static readonly Vector2 ForehandBack = new Vector2(1.6f, -0.3f), ForehandFront = new Vector2(-1.1f, 0.1f);

        [Test]
        public void Forehand_IsOneSwing_SeenEarlyInTheMove_GoingAcross()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var swings = Run(Move(ForehandBack, ForehandFront, 0.3f), 2.5f, seed);
                Assert.AreEqual(1, swings.Count, $"seed {seed}");
                Assert.IsTrue(swings[0].RightHand);
                Assert.Less(swings[0].Direction.x, -0.8f, "across to the screen's left");
                Assert.That(swings[0].Time, Is.InRange(1.0, 1.3), "seen while the hand is fast, before the move ends");
            }
        }

        [Test]
        public void Backhand_Serve_Lob_Slice_AreSwings_WithTheirDirection()
        {
            var moves = new (string name, Vector2 from, Vector2 to, float seconds, Func<Vector2, bool> direction)[]
            {
                ("backhand", new Vector2(-1.0f, -0.2f), new Vector2(1.7f, 0.2f), 0.3f, d => d.x > 0.8f),
                ("overhead serve", new Vector2(0.3f, 1.6f), new Vector2(-0.3f, -0.6f), 0.3f, d => d.y < -0.8f),
                ("lob (scoop up)", new Vector2(1.0f, -1.6f), new Vector2(0.4f, 1.2f), 0.45f, d => d.y > 0.8f),
                ("slice (chop down)", new Vector2(1.4f, 0.9f), new Vector2(-0.6f, -0.8f), 0.3f, d => d.y < -0.3f),
            };
            foreach (var (name, from, to, seconds, direction) in moves)
            for (int seed = 1; seed <= 10; seed++)
            {
                var swings = Run(Move(from, to, seconds), 2.5f, seed);
                Assert.AreEqual(1, swings.Count, $"{name}, seed {seed}");
                Assert.IsTrue(direction(swings[0].Direction), $"{name}: direction {swings[0].Direction}");
            }
        }

        [Test]
        public void TheSpeed_IsTheSwingsFastest_WhereverTheFramesFall()
        {
            // the same forehand, started at different moments between two camera frames
            var speeds = new List<float>();
            for (int k = 0; k < 6; k++)
            {
                var swings = Run(Move(ForehandBack, ForehandFront, 0.3f, start: 1f + k / (6f * Fps)), 2.5f, seed: 3 + k);
                Assert.AreEqual(1, swings.Count);
                speeds.Add(swings[0].Speed);
            }
            speeds.Sort();
            Assert.Greater(speeds[0], 8f, $"a full forehand reads as one (slowest {speeds[0]:0.0} torso lengths per second)");
            Assert.Less(speeds[speeds.Count - 1] / speeds[0], 1.35f, $"the same swing reads the same ({string.Join(", ", speeds)})");
        }

        [Test]
        public void AChildsSlowSwing_StillCounts()
        {
            // a third of an adult's forehand, slowly: ~4 torso lengths per second at its fastest
            for (int seed = 1; seed <= 20; seed++)
                Assert.AreEqual(1, Run(Move(new Vector2(1.1f, -0.6f), new Vector2(0.1f, -0.3f), 0.42f), 2.5f, seed).Count, $"seed {seed}");
        }

        [Test]
        public void StillHands_Fidgeting_RaisingAHandToTheFace_SwayingAndJumping_AreNoSwings()
        {
            var cases = new (string name, Func<double, (Vector2, Vector2, Vector2)> pose)[]
            {
                ("still", Move(new Vector2(0.45f, -0.95f), new Vector2(0.45f, -0.95f), 1f)),
                // calmly, in 1.2 s (~2 torso lengths per second at most); a brisk one (0.9 s) may count: the threshold stays low
                // for small children's swings, and an extra swing at worst swings at the air
                ("hand calmly up to the face", Move(new Vector2(0.45f, -0.95f), new Vector2(0.1f, 0.25f), 1.2f)),
                ("scratching the head", t => (new Vector2(-0.45f, -0.95f),
                    new Vector2(0.15f, 0.55f) + new Vector2(Mathf.Sin((float)t * 9f), Mathf.Cos((float)t * 9f)) * 0.06f, Vector2.zero)),
                ("swaying", Move(new Vector2(0.45f, -0.95f), new Vector2(0.45f, -0.95f), 1f,
                    body: t => new Vector2(0.4f * Mathf.Sin((float)t * 3f), 0f))),
                ("jumping", Move(new Vector2(0.45f, -0.95f), new Vector2(0.45f, -0.95f), 1f,
                    body: t => new Vector2(0f, 0.8f * Mathf.Max(0f, Mathf.Sin((float)t * 5f))))),
            };
            foreach (var (name, pose) in cases)
            for (int seed = 1; seed <= 10; seed++)
                Assert.AreEqual(0, Run(pose, 8f, seed).Count, $"{name}, seed {seed}");
        }

        [Test]
        public void Flailing_SwingsAgain_ButNotFasterThanTheCooldown()
        {
            var swings = Run(t => (new Vector2(-0.45f, -0.95f), new Vector2(1.4f * Mathf.Sin((float)t * 7f), 0f), Vector2.zero), 6f);
            Assert.GreaterOrEqual(swings.Count, 6, "back and forth keeps swinging");
            for (int i = 1; i < swings.Count; i++) Assert.GreaterOrEqual(swings[i].Time - swings[i - 1].Time, 0.34);
        }

        [Test]
        public void UnsureSamples_AreNoSwings()
        {
            Assert.AreEqual(0, Run(Move(ForehandBack, ForehandFront, 0.3f), 2.5f, visibility: 0.2f).Count);
        }
    }
}
