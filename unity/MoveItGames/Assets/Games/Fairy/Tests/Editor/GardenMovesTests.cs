using System;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Fairy.Tests
{
    /// <summary>Zana!'s body moves on simulated arms: both arms up (the transformation) is seen and ends; hanging arms with
    /// the box's glitches never are.</summary>
    public class GardenMovesTests
    {
        // a standing player in view units (y up): torso 0.5, shoulders at 1.4, hips at 0.9
        const float Torso = 0.5f;
        static readonly Vector2 Shoulders = new Vector2(0f, 1.4f);

        static FairyBodySample Body(Vector2 left, Vector2 right)
        {
            return new FairyBodySample
            {
                LeftShoulder = new Vector2(-0.2f, 1.4f), RightShoulder = new Vector2(0.2f, 1.4f),
                LeftHip = new Vector2(-0.12f, 0.9f), RightHip = new Vector2(0.12f, 0.9f), Nose = new Vector2(0f, 1.62f),
                LeftWrist = Shoulders + left * Torso, RightWrist = Shoulders + right * Torso,
                LeftWristVisibility = 0.95f, RightWristVisibility = 0.95f, HipVisibility = 0.9f, NoseVisibility = 0.95f,
                Torso = Torso,
            };
        }

        /// <summary>Plays the wrists' paths (torso lengths round the shoulders) at <paramref name="rate"/> samples a second
        /// with camera noise and, now and then, a one-frame glitch flinging a wrist.</summary>
        static void Play(GardenMoves m, Func<float, (Vector2 l, Vector2 r)> path, float seconds, float rate = 30f, int seed = 1,
                         float glitches = 0f, Action<float> each = null)
        {
            var rng = new System.Random(seed);
            float N() => (float)(rng.NextDouble() * 2 - 1) * 0.02f;
            for (float t = 0f; t < seconds; t += 1f / rate)
            {
                var (l, r) = path(t);
                l += new Vector2(N(), N());
                r += new Vector2(N(), N());
                if (glitches > 0f && rng.NextDouble() < glitches / rate)
                {
                    var fling = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 1.6f;
                    if (rng.Next(2) == 0) l += fling;
                    else r += fling;
                }
                m.Update(t, Body(l, r));
                each?.Invoke(t);
            }
        }

        [Test]
        public void BothArmsUp_IsSeen_AndEnds()
        {
            foreach (float rate in new[] { 30f, 15f })
            {
                var m = new GardenMoves();
                bool seen = false, after = false;
                Play(m, t => t < 1f ? (new Vector2(-0.4f, -1f), new Vector2(0.4f, -1f))
                                  : t < 2f ? (new Vector2(-0.5f, 0.9f), new Vector2(0.5f, 0.9f))
                                  : (new Vector2(-0.4f, -1f), new Vector2(0.4f, -1f)), 3f, rate, 4,
                     each: t =>
                     {
                         if (t > 1.3f && t < 1.9f && m.ArmsUp) seen = true;
                         if (t > 2.5f && m.ArmsUp) after = true;
                     });
                Assert.IsTrue(seen, $"{rate} a second");
                Assert.IsFalse(after, $"{rate} a second");
            }
        }

        [Test]
        public void HangingArms_OneArmUp_OrTheBoxGlitches_AreNoArmsUp()
        {
            bool any = false;
            var m = new GardenMoves();
            // arms hanging, with the box's glitches a hundred times their rate
            Play(m, t => (new Vector2(-0.35f, -1.1f), new Vector2(0.35f, -1.1f)), 60f, 30f, 3, glitches: 2f, each: _ => any |= m.ArmsUp);
            Assert.IsFalse(any);
            // one hand up pointing the wand, the other hanging
            m = new GardenMoves();
            Play(m, t => (new Vector2(-0.35f, -1.1f), new Vector2(0.4f + 0.3f * Mathf.Sin(t * 3f), 0.6f)), 10f, 30f, 5, each: _ => any |= m.ArmsUp);
            Assert.IsFalse(any);
        }
    }
}
