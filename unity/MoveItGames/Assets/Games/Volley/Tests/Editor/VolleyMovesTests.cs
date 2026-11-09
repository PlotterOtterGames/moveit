using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Volley.Tests
{
    /// <summary>
    /// The volleyball's moves read from simulated arms (view units: image heights, y up, mirrored; a torso of 0.2 with the
    /// camera's noise): both hands up = set, hands together low = bump, a one-hand swing = a hit as fast as the hand;
    /// raising both hands, arms hanging, a still hand with the pose model's glitches = nothing; the same at 15 samples a
    /// second (two players on the TV box).
    /// </summary>
    public class VolleyMovesTests
    {
        const float Torso = 0.2f;
        static readonly Vector2 Shoulders = new Vector2(0.9f, 0.62f);

        /// <summary>Wrists relative to the shoulders' middle, in torso lengths (x = the screen's right, y up).</summary>
        static readonly Vector2 HangL = new Vector2(-0.36f, -0.95f), HangR = new Vector2(0.36f, -0.95f);
        static readonly Vector2 BumpL = new Vector2(-0.06f, -0.85f), BumpR = new Vector2(0.06f, -0.85f);
        static readonly Vector2 SetL = new Vector2(-0.28f, 0.62f), SetR = new Vector2(0.28f, 0.62f);

        sealed class Arms
        {
            public double Time;
            public float Rate = 30f, Noise = 0.02f;   // noise in torso lengths (0.004 image heights)
            public readonly VolleyMoves Moves = new VolleyMoves();
            public readonly List<MoveEvent> Seen = new List<MoveEvent>();
            readonly System.Random _rng;
            /// <summary>Glitches (per second), like the box's (2026-11-06): a wrist flipped 15-50 cm (0.3-1 torso lengths)
            /// somewhere else for a frame or two.</summary>
            public float Glitches;
            int _glitchFrames;
            bool _glitchRight;
            Vector2 _glitch;

            public Arms(int seed) => _rng = new System.Random(seed);

            float Gauss()
            {
                double u = 1.0 - _rng.NextDouble(), v = _rng.NextDouble();
                return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v));
            }

            /// <summary>Moves both wrists from where they are to <paramref name="l"/>, <paramref name="r"/> over
            /// <paramref name="seconds"/> (min-jerk), sampling at <see cref="Rate"/>.</summary>
            public void To(Vector2 l0, Vector2 r0, Vector2 l, Vector2 r, float seconds)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
                for (int i = 1; i <= n; i++)
                {
                    float k = (float)i / n;
                    float s = k * k * k * (10f - 15f * k + 6f * k * k);
                    Sample(Vector2.Lerp(l0, l, s), Vector2.Lerp(r0, r, s));
                }
            }

            public void Hold(Vector2 l, Vector2 r, float seconds) => To(l, r, l, r, seconds);

            void Sample(Vector2 l, Vector2 r)
            {
                Time += 1.0 / Rate;
                var nl = l + new Vector2(Gauss(), Gauss()) * Noise;
                var nr = r + new Vector2(Gauss(), Gauss()) * Noise;
                float lv = 0.95f, rv = 0.95f;
                if (_glitchFrames <= 0 && _rng.NextDouble() < Glitches / Rate)
                {
                    _glitchFrames = 1 + _rng.Next(2);
                    _glitchRight = _rng.NextDouble() < 0.6;
                    float a = (float)(_rng.NextDouble() * Math.PI * 2), size = 0.3f + (float)_rng.NextDouble() * 0.7f;
                    _glitch = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size;
                }
                if (_glitchFrames > 0)
                {
                    _glitchFrames--;
                    if (_glitchRight)
                    {
                        nr += _glitch;
                        rv = 0.75f;
                    }
                    else
                    {
                        nl += _glitch;
                        lv = 0.75f;
                    }
                }
                Moves.Update(Time, Shoulders, Torso, Shoulders + nl * Torso, lv, Shoulders + nr * Torso, rv, Seen);
            }

            public int Count(Move m) => Seen.Count(e => e.Move == m);
            public IEnumerable<MoveEvent> Hits => Seen.Where(e => e.Move == Move.Hit);
        }

        [Test]
        public void BothHandsUp_IsASet_HandsTogetherLow_IsABump_AtEitherRate()
        {
            foreach (float rate in new[] { 30f, 15f })
            {
                var a = new Arms(1) { Rate = rate };
                a.Hold(HangL, HangR, 1f);
                Assert.AreEqual(0, a.Seen.Count, "arms hanging: nothing");
                a.To(HangL, HangR, SetL, SetR, 0.5f);
                a.Hold(SetL, SetR, 0.6f);
                Assert.Greater(a.Count(Move.Set), (int)(rate * 0.4f), "held up: a set every sample");
                Assert.AreEqual(0, a.Count(Move.Hit), "raising both hands is no hit");
                a.To(SetL, SetR, HangL, HangR, 0.5f);
                a.Hold(HangL, HangR, 0.6f);
                a.Seen.Clear();
                a.To(HangL, HangR, BumpL, BumpR, 0.4f);
                a.Hold(BumpL, BumpR, 0.6f);
                Assert.Greater(a.Count(Move.Bump), (int)(rate * 0.4f), "together low: a bump");
                Assert.AreEqual(0, a.Count(Move.Set));
                Assert.AreEqual(0, a.Count(Move.Hit), "bringing the hands together is no hit");
            }
        }

        [Test]
        public void OneHandSwing_IsAHit_AsFastAsTheHand()
        {
            foreach (float rate in new[] { 30f, 15f })
            {
                float Hit(float seconds)
                {
                    var a = new Arms(2) { Rate = rate };
                    Vector2 up = new Vector2(0.32f, 0.7f), down = new Vector2(0.15f, -0.75f);
                    a.Hold(HangL, HangR, 0.8f);
                    a.To(HangL, HangR, HangL, up, 0.6f);      // the hand goes up slowly (a wind-up, no hit)
                    a.Hold(HangL, up, 0.3f);
                    int before = a.Hits.Count();
                    a.To(HangL, up, HangL, down, seconds);    // the spike
                    a.Hold(HangL, down, 0.5f);
                    Assert.AreEqual(0, before, "the slow wind-up is no hit");
                    var hits = a.Hits.ToList();
                    Assert.AreEqual(1, hits.Count, $"one hit ({seconds} s)");
                    Assert.IsTrue(hits[0].RightHand);
                    Assert.Less(hits[0].Direction.y, -0.5f, "down");
                    return hits[0].Speed;
                }
                float quick = Hit(0.18f), lazy = Hit(0.45f);
                Debug.Log($"{rate} Hz: quick {quick:0.0}, lazy {lazy:0.0} torso lengths a second");
                Assert.Greater(quick, lazy * 1.5f);
            }
        }

        [Test]
        public void EitherHand_Hits()
        {
            var a = new Arms(3);
            Vector2 up = new Vector2(-0.32f, 0.7f), down = new Vector2(-0.15f, -0.75f);
            a.Hold(HangL, HangR, 0.8f);
            a.To(HangL, HangR, up, HangR, 0.6f);
            a.Hold(up, HangR, 0.3f);
            a.To(up, HangR, down, HangR, 0.2f);
            a.Hold(down, HangR, 0.4f);
            var hits = a.Hits.ToList();
            Assert.AreEqual(1, hits.Count, "the left hand's spike");
            Assert.IsFalse(hits[0].RightHand);
        }

        [Test]
        public void FromTheSetPose_OneHandSwungDown_IsAHit()
        {
            var a = new Arms(4);
            a.Hold(HangL, HangR, 0.6f);
            a.To(HangL, HangR, SetL, SetR, 0.5f);
            a.Hold(SetL, SetR, 0.4f);
            int hitsBefore = a.Hits.Count();
            a.To(SetL, SetR, SetL, new Vector2(0.2f, -0.7f), 0.2f);
            a.Hold(SetL, new Vector2(0.2f, -0.7f), 0.4f);
            Assert.AreEqual(0, hitsBefore);
            Assert.AreEqual(1, a.Hits.Count(), "the other hand stayed up: one hand's hit");
        }

        [Test]
        public void BothHandsDownAtOnce_IsNoHit()
        {
            var a = new Arms(5);
            a.Hold(HangL, HangR, 0.6f);
            a.To(HangL, HangR, SetL, SetR, 0.5f);
            a.Hold(SetL, SetR, 0.4f);
            a.To(SetL, SetR, HangL, HangR, 0.2f);   // both arms dropped quickly after a set
            a.Hold(HangL, HangR, 0.5f);
            Assert.AreEqual(0, a.Hits.Count());
            Assert.Greater(a.Moves.TwoHanded, 0, "seen as two hands");
        }

        /// <summary>
        /// The pose model's glitches (2 a second here: a hundred times the box's usual rate, so the test sees thousands) almost
        /// never make a hit or a pose: measured 0 false hits a minute at 30 samples a second, ~0.2 at 15; a pose for ~1.5 % of
        /// the glitches, when two come back to back (harmless: a pose only ever helps the player). A held pose stays held
        /// through them.
        /// </summary>
        [Test]
        public void TheModelsGlitches_AlmostNeverMakeAHitOrAPose()
        {
            foreach (float rate in new[] { 30f, 15f })
            {
                int hits = 0, poses = 0, glitches = 0, held = 0, samples = 0;
                double minutes = 0;
                for (int seed = 0; seed < 24; seed++)
                {
                    var hang = new Arms(seed) { Rate = rate };
                    hang.Hold(HangL, HangR, 1f);
                    hang.Seen.Clear();
                    hang.Glitches = 2f;
                    hang.Hold(HangL, HangR, 15f);
                    hits += hang.Hits.Count();
                    poses += hang.Count(Move.Bump) + hang.Count(Move.Set);
                    glitches += (int)(15f * 2f);
                    minutes += 0.25;

                    var bump = new Arms(100 + seed) { Rate = rate };
                    bump.To(HangL, HangR, BumpL, BumpR, 0.5f);
                    bump.Hold(BumpL, BumpR, 0.5f);
                    bump.Seen.Clear();
                    bump.Glitches = 2f;
                    bump.Hold(BumpL, BumpR, 15f);
                    hits += bump.Hits.Count();
                    held += bump.Count(Move.Bump);
                    samples += (int)(15f * rate);
                    minutes += 0.25;
                }
                Debug.Log($"{rate} Hz: {hits / minutes:0.00} false hits a minute, a pose for {100f * poses / glitches:0.0} % of the glitches, " +
                          $"the bump held {100f * held / samples:0} % of the samples");
                Assert.LessOrEqual(hits / minutes, 0.5, $"{rate} Hz: hits from glitches");
                Assert.LessOrEqual((float)poses / glitches, 0.04f, $"{rate} Hz: poses from glitches");
                Assert.Greater((float)held / samples, 0.85f, $"{rate} Hz: the bump held through them");
            }
        }

        [Test]
        public void Settled_WaitsForAHitStillBeingConfirmed()
        {
            var a = new Arms(7);
            Vector2 up = new Vector2(0.32f, 0.7f), down = new Vector2(0.15f, -0.75f);
            a.Hold(HangL, HangR, 0.8f);
            a.To(HangL, HangR, HangL, up, 0.6f);
            a.Hold(HangL, up, 0.3f);
            a.To(HangL, up, HangL, down, 0.2f);
            a.Hold(HangL, down, 0.4f);
            var hit = a.Hits.Single();
            Assert.Greater(a.Moves.Settled, hit.Time, "settled past the hit once it is reported");
            Assert.LessOrEqual(a.Moves.Settled, a.Time);
        }
    }
}
