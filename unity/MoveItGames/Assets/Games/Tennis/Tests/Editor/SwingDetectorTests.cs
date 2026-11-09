using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Tennis.Tests
{
    /// <summary>
    /// Swings on a simulated camera: 30 fps (15 when two players share the box's pose AI), the player's torso 0.25 picture
    /// heights long, MediaPipe-like noise (0.9 % of a torso length still, 3 % moving slowly, ~8 % on a fast hand, as
    /// measured on a real session), raw samples as the game passes them (the detector filters them itself). Hand paths
    /// are minimum-jerk moves like a real arm's, in torso lengths from the shoulders' middle on the mirrored screen. The
    /// pose model's glitches as seen on the box (2026-11-06): the wrist flipped to another spot for one to three frames,
    /// or pulled toward its elbow.
    /// </summary>
    public class SwingDetectorTests
    {
        const float Torso = 0.25f;
        static readonly Vector2 Shoulders = new Vector2(0.9f, 0.62f);
        /// <summary>The right elbow (torso lengths from the shoulders' middle), where a collapsing wrist is pulled.</summary>
        static readonly Vector2 Elbow = new Vector2(0.42f, -0.62f);

        /// <summary>A glitch of the right wrist: from <see cref="Frame"/> on for <see cref="Frames"/> samples, flipped by
        /// <see cref="Offset"/> (torso lengths) or, with <see cref="Collapse"/>, pulled that share of the way to its elbow.</summary>
        struct Glitch
        {
            public int Frame, Frames;
            public Vector2 Offset;
            public float Collapse;
        }

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
                                    float visibility = 0.95f, float fps = 30f, Glitch[] glitches = null, SwingDetector detector = null)
        {
            var d = detector ?? new SwingDetector();
            var rng = new System.Random(seed);
            float Gauss() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2.0 * Math.PI * rng.NextDouble()));
            var swings = new List<SwingEvent>();
            Vector2 lastRight = default;
            // the camera's frames fall anywhere against the move
            double phase = (seed % 6) / (6.0 * fps);
            for (int i = 0; i < seconds * fps; i++)
            {
                double t = i / fps + phase;
                var (l, r, s) = pose(t);
                float speed = i > 0 ? (r - lastRight).magnitude * fps : 0f;
                lastRight = r;
                float vis = visibility;
                if (glitches != null)
                    foreach (var g in glitches)
                    {
                        if (i < g.Frame || i >= g.Frame + g.Frames) continue;
                        r = g.Collapse > 0f ? Vector2.Lerp(r, Elbow + s, g.Collapse) : r + g.Offset;
                        vis = Mathf.Min(vis, 0.8f);
                    }
                // the moving hand is noisier (blur), the still one and the shoulders are not
                Vector2 N(float n) => new Vector2(Gauss(), Gauss()) * (n * Torso);
                var sh = Shoulders + s * Torso + N(0.006f);
                var lw = Shoulders + l * Torso + N(0.009f);
                var rw = Shoulders + r * Torso + N(0.009f + Mathf.Min(0.08f, speed * 0.01f));
                if (d.Update(t, sh, Torso, lw, visibility, rw, vis, out var e)) swings.Add(e);
            }
            return swings;
        }

        static int Frame(float seconds, int seed, float fps = 30f) => Mathf.CeilToInt((seconds - (seed % 6) / (6f * fps)) * fps - 1e-4f);

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
                Assert.IsFalse(swings[0].Gentle, $"seed {seed}: a full forehand is strong");
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
                var swings = Run(Move(ForehandBack, ForehandFront, 0.3f, start: 1f + k / (6f * 30f)), 2.5f, seed: 3 + 6 * k);
                Assert.AreEqual(1, swings.Count);
                speeds.Add(swings[0].Speed);
            }
            speeds.Sort();
            // raw samples: the fastest moment reads within ~30 % of the true 16.9; full power from 10 (TennisMatch.FastSwing)
            Assert.Greater(speeds[0], 10f, $"a full forehand reads as one (slowest {speeds[0]:0.0} torso lengths per second)");
            Assert.Less(speeds[speeds.Count - 1] / speeds[0], 1.6f, $"the same swing reads about the same ({string.Join(", ", speeds)})");
        }

        [Test]
        public void AChildsSlowSwing_StillCounts()
        {
            // a third of an adult's forehand, slowly: ~4 torso lengths per second at its fastest
            int found = 0;
            for (int seed = 1; seed <= 20; seed++) found += Run(Move(new Vector2(1.1f, -0.6f), new Vector2(0.1f, -0.3f), 0.42f), 2.5f, seed).Count == 1 ? 1 : 0;
            Assert.GreaterOrEqual(found, 19);
        }

        /// <summary>
        /// A flick of the forearm, like a Wii remote's (the user, 2026-11-06: "register the hands even in small amounts,
        /// moving the whole arm is tiring"): 0.25-0.5 torso lengths (~12-25 cm for an adult) in 0.15-0.25 s. The old
        /// detector found none of them; now nearly all, as gentle swings (the game takes those at a ball in reach).
        /// </summary>
        [Test]
        public void SmallFlicks_LikeAWiiRemote_AreSwings()
        {
            var flicks = new (string name, Vector2 from, Vector2 to, float seconds)[]
            {
                ("forearm flick", new Vector2(0.55f, -0.75f), new Vector2(0.05f, -0.65f), 0.25f),
                ("small flick", new Vector2(0.5f, -0.75f), new Vector2(0.15f, -0.7f), 0.25f),
                ("small quick flick", new Vector2(0.5f, -0.75f), new Vector2(0.2f, -0.72f), 0.15f),
                ("tiny flick", new Vector2(0.45f, -0.75f), new Vector2(0.2f, -0.72f), 0.2f),
                ("backhand flick", new Vector2(0.0f, -0.7f), new Vector2(0.4f, -0.75f), 0.25f),
            };
            foreach (var (name, from, to, seconds) in flicks)
            {
                int found = 0, gentle = 0;
                for (int seed = 1; seed <= 30; seed++)
                {
                    var swings = Run(Move(from, to, seconds), 2.5f, seed);
                    if (swings.Count != 1) continue;
                    found++;
                    if (swings[0].Gentle) gentle++;
                    Assert.Greater(swings[0].Direction.x * Mathf.Sign(to.x - from.x), 0f, $"{name}, seed {seed}: its way");
                    Assert.Less(Mathf.Abs(swings[0].Direction.y), new SwingDetector().VerticalLimit, $"{name}, seed {seed}: sideways");
                }
                Assert.GreaterOrEqual(found, 27, $"{name}: found {found} of 30");
            }
        }

        /// <summary>Two players share the box's pose AI: ~15 samples a second each. Swings and a forearm flick still count.</summary>
        [Test]
        public void AtFifteenSamplesASecond_SwingsAndFlicksStillCount()
        {
            int full = 0, flick = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                full += Run(Move(ForehandBack, ForehandFront, 0.3f), 2.5f, seed, fps: 15f).Count == 1 ? 1 : 0;
                flick += Run(Move(new Vector2(0.55f, -0.75f), new Vector2(0.05f, -0.65f), 0.25f), 2.5f, seed, fps: 15f).Count == 1 ? 1 : 0;
            }
            Assert.AreEqual(30, full, "full forehands");
            Assert.GreaterOrEqual(flick, 27, "forearm flicks");
        }

        /// <summary>Still hands, scratching the head, swaying and jumping are no moves of the hand at all; lifting a hand
        /// calmly to the face is one, but never a strong swing (gentle moves count only at a ball in reach, and up or down
        /// is no stroke).</summary>
        [Test]
        public void StillHands_Fidgeting_RaisingAHandToTheFace_SwayingAndJumping_AreNoSwings()
        {
            var cases = new (string name, Func<double, (Vector2, Vector2, Vector2)> pose, bool gentleAllowed)[]
            {
                ("still", Move(new Vector2(0.45f, -0.95f), new Vector2(0.45f, -0.95f), 1f), false),
                ("hand calmly up to the face", Move(new Vector2(0.45f, -0.95f), new Vector2(0.1f, 0.25f), 1.2f), true),
                ("scratching the head", t => (new Vector2(-0.45f, -0.95f),
                    new Vector2(0.15f, 0.55f) + new Vector2(Mathf.Sin((float)t * 9f), Mathf.Cos((float)t * 9f)) * 0.06f, Vector2.zero), false),
                ("swaying", Move(new Vector2(0.45f, -0.95f), new Vector2(0.45f, -0.95f), 1f,
                    body: t => new Vector2(0.4f * Mathf.Sin((float)t * 3f), 0f)), false),
                ("jumping", Move(new Vector2(0.45f, -0.95f), new Vector2(0.45f, -0.95f), 1f,
                    body: t => new Vector2(0f, 0.8f * Mathf.Max(0f, Mathf.Sin((float)t * 5f)))), false),
            };
            foreach (var (name, pose, gentleAllowed) in cases)
            for (int seed = 1; seed <= 10; seed++)
            {
                var swings = Run(pose, 8f, seed);
                Assert.AreEqual(0, swings.FindAll(s => !s.Gentle).Count, $"{name}, seed {seed}: strong swings");
                if (!gentleAllowed) Assert.AreEqual(0, swings.Count, $"{name}, seed {seed}");
                else foreach (var s in swings) Assert.GreaterOrEqual(Mathf.Abs(s.Direction.y), 0.77f, $"{name}, seed {seed}: up, not across");
            }
        }

        /// <summary>
        /// The pose model's glitches on a still hand (as on the box, 2026-11-06: the wrist flipped up by the head and
        /// back, pulled onto its elbow, for one to three frames) are no swings: the old detector swung at every flip.
        /// </summary>
        [Test]
        public void TheModelsGlitches_OfAStillHand_AreNoSwings()
        {
            var kinds = new (string name, Vector2 offset, float collapse, int frames)[]
            {
                ("flip 0.6 for a frame", new Vector2(-0.2f, -0.57f), 0f, 1),
                ("flip 1.2 up, a frame", new Vector2(0.1f, 1.2f), 0f, 1),
                ("flip 0.8 for two frames", new Vector2(0.75f, -0.3f), 0f, 2),
                ("flip 0.4 for three frames", new Vector2(-0.4f, 0f), 0f, 3),
                ("collapse onto the elbow, two frames", Vector2.zero, 0.8f, 2),
                ("collapse onto the elbow, four frames", Vector2.zero, 0.8f, 4),
            };
            var still = Move(new Vector2(0.1f, -0.75f), new Vector2(0.1f, -0.75f), 1f);
            foreach (var (name, offset, collapse, frames) in kinds)
            for (int seed = 1; seed <= 20; seed++)
            {
                var glitches = new[]
                {
                    new Glitch { Frame = Frame(1f, seed), Frames = frames, Offset = offset, Collapse = collapse },
                    new Glitch { Frame = Frame(2f, seed), Frames = frames, Offset = -offset, Collapse = collapse },
                };
                var d = new SwingDetector();
                Assert.AreEqual(0, Run(still, 3f, seed, glitches: glitches, detector: d).Count, $"{name}, seed {seed}");
            }
        }

        /// <summary>A glitch frame in the middle of a real swing doesn't lose it (the median takes the frame out).</summary>
        [Test]
        public void AOneFrameGlitch_InTheMiddleOfASwing_KeepsIt()
        {
            int kept = 0, runs = 0;
            foreach (float at in new[] { 1.05f, 1.1f, 1.15f })
            for (int seed = 1; seed <= 20; seed++)
            {
                var glitch = new[] { new Glitch { Frame = Frame(at, seed), Frames = 1, Offset = new Vector2(-0.2f, -0.57f) } };
                kept += Run(Move(new Vector2(0.55f, -0.75f), new Vector2(0.05f, -0.65f), 0.25f), 2.5f, seed, glitches: glitch).Count == 1 ? 1 : 0;
                runs++;
            }
            Assert.GreaterOrEqual(kept, runs - 4, $"kept {kept} of {runs}");
        }

        [Test]
        public void Flailing_SwingsAgain_ButNotFasterThanTheCooldown()
        {
            var swings = Run(t => (new Vector2(-0.45f, -0.95f), new Vector2(1.4f * Mathf.Sin((float)t * 7f), 0f), Vector2.zero), 6f);
            Assert.GreaterOrEqual(swings.Count, 6, "back and forth keeps swinging");
            for (int i = 1; i < swings.Count; i++) Assert.GreaterOrEqual(swings[i].Time - swings[i - 1].Time, new SwingDetector().Cooldown - 0.01);
        }

        [Test]
        public void UnsureSamples_AreNoSwings()
        {
            Assert.AreEqual(0, Run(Move(ForehandBack, ForehandFront, 0.3f), 2.5f, visibility: 0.2f).Count);
        }

        /// <summary>A hand along key points (time, torso lengths from the shoulders' middle), minimum-jerk between them.</summary>
        static Func<double, Vector2> Path(params (float t, Vector2 p)[] keys) => t =>
        {
            if (t <= keys[0].t) return keys[0].p;
            for (int i = 1; i < keys.Length; i++)
            {
                if (t > keys[i].t) continue;
                float k = (float)(t - keys[i - 1].t) / (keys[i].t - keys[i - 1].t);
                return Vector2.Lerp(keys[i - 1].p, keys[i].p, k * k * k * (10f - 15f * k + 6f * k * k));
            }
            return keys[keys.Length - 1].p;
        };

        static readonly Vector2 Hanging = new Vector2(-0.45f, -0.95f), Front = new Vector2(0.2f, -0.45f);

        /// <summary>
        /// The free hand (the left: the racket is in the right) thrown up or dropped is never a swing, and a stroke of the
        /// racket hand at the same moment is still seen, with its own direction (2026-11-06: the user's left hand going
        /// up was taken for a swing, "too early").
        /// </summary>
        [Test]
        public void TheFreeHandUpOrDown_IsNoSwing_TheRacketHandsStrokeStillIs()
        {
            var up = Path((1f, Hanging), (1.3f, new Vector2(-0.7f, 0.9f)), (2f, new Vector2(-0.7f, 0.9f)), (2.4f, Hanging));
            var stroke = Path((1f, ForehandBack), (1.3f, ForehandFront));
            for (int seed = 1; seed <= 10; seed++)
            {
                Assert.AreEqual(0, Run(t => (up(t), ForehandBack, Vector2.zero), 3f, seed).Count, $"seed {seed}: the free hand up and down");
                var both = Run(t => (up(t), stroke(t), Vector2.zero), 3f, seed);
                Assert.AreEqual(1, both.Count, $"seed {seed}: the stroke while the free hand goes up");
                Assert.IsTrue(both[0].RightHand, $"seed {seed}: the racket hand's");
                Assert.Less(both[0].Direction.x, -0.8f, $"seed {seed}: across");
            }
        }

        /// <summary>The racket taken back and the stroke 0.3 s later are two swings (the old 0.35 s cooldown dropped the
        /// stroke), each with its own direction: back toward the racket's side, then across.</summary>
        [Test]
        public void ABackswing_ThenTheStroke_AreTwoSwings()
        {
            var hand = Path((1f, Front), (1.28f, ForehandBack), (1.32f, ForehandBack), (1.6f, ForehandFront), (2.6f, Hanging));
            for (int seed = 1; seed <= 20; seed++)
            {
                var swings = Run(t => (Hanging, hand(t), Vector2.zero), 3f, seed);
                Assert.GreaterOrEqual(swings.Count, 2, $"seed {seed}");
                Assert.Greater(swings[0].Direction.x, 0.8f, $"seed {seed}: the backswing goes to the racket's side");
                Assert.Less(swings[1].Direction.x, -0.8f, $"seed {seed}: the stroke goes across");
                Assert.That(swings[1].Time, Is.InRange(1.3, 1.55), $"seed {seed}: the stroke seen as it gets going");
            }
        }
    }
}
