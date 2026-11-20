using System;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Fairy.Tests
{
    /// <summary>The wand on simulated hands (2026-11-16): the raised hand points across the whole screen, resting hands
    /// point at nothing, the other hand takes over, unsure samples don't throw it, a still hand holds it still.</summary>
    public class WandTests
    {
        static readonly Vector2 Rest = new Vector2(0f, -1.15f);

        /// <summary>Plays the hands (torso lengths round the shoulders, x to the player's right) at 30 samples a second,
        /// received 70 ms after capture, the game at 60 frames a second; <paramref name="each"/> every frame.</summary>
        /// <summary>The clock goes on from one call to the next (a wand ignores samples older than the last one).</summary>
        double _clock;

        [SetUp]
        public void Clock() => _clock = 0;

        void Play(Wand w, Func<float, (Vector2 l, Vector2 r, float lv, float rv)> hands, float seconds, float noise = 0.006f,
                  int seed = 1, Action<float> each = null)
        {
            var rng = new System.Random(seed);
            float N() => (float)(rng.NextDouble() * 2 - 1) * noise;
            double start = _clock, nextSample = _clock;
            for (float t = 0f; t < seconds; t += 1f / 60f)
            {
                double now = start + t;
                while (nextSample + 0.07 <= now)
                {
                    var (l, r, lv, rv) = hands((float)(nextSample - start));
                    w.Sample(nextSample, nextSample + 0.07, l + new Vector2(N(), N()), lv, r + new Vector2(N(), N()), rv);
                    nextSample += 1.0 / 30.0;
                }
                w.Update(now, 1f / 60f);
                each?.Invoke(t);
            }
            _clock = start + seconds;
        }

        [Test]
        public void TheRaisedHand_PointsAcrossTheWholeScreen()
        {
            var w = new Wand();
            // the right hand raised to the zone's middle, then held at each of its corners (the bottom ones are under the
            // height a hand is raised to: once up, the wand stays with it down to the zone's bottom)
            var corners = new[]
            {
                new Vector2(w.Inward, (w.Bottom + w.Top) / 2f),
                new Vector2(w.Inward + w.HalfWidth, w.Top), new Vector2(w.Inward - w.HalfWidth, w.Top),
                new Vector2(w.Inward - w.HalfWidth, w.Bottom + 0.05f), new Vector2(w.Inward + w.HalfWidth, w.Bottom + 0.05f),
            };
            var expected = new[] { new Vector2(0.5f, 0.5f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.03f), new Vector2(1f, 0.03f) };
            for (int i = 0; i < corners.Length; i++)
            {
                var at = corners[i];
                var want = expected[i];
                var seen = Vector2.zero;
                Play(w, t => (Rest, at, 0.9f, 0.9f), 0.8f, each: _ => seen = w.Position);
                Assert.IsTrue(w.Active, $"corner {i}");
                Assert.That(w.Hand, Is.EqualTo(1));
                Assert.That(Vector2.Distance(seen, want), Is.LessThan(0.035f), $"corner {i}: {seen} for {want}");
            }
        }

        [Test]
        public void RestingHands_PointAtNothing_TheOtherHandTakesOver()
        {
            var w = new Wand();
            Play(w, t => (Rest, Rest, 0.9f, 0.9f), 1f);
            Assert.IsFalse(w.Active, "both hands resting");
            // the right hand up
            Play(w, t => (Rest, new Vector2(0.5f, 0.2f), 0.9f, 0.9f), 0.6f);
            Assert.IsTrue(w.Active);
            Assert.That(w.Hand, Is.EqualTo(1));
            // the right hand down, the left up: the left takes the wand
            Play(w, t => (new Vector2(-0.6f, 0.3f), Rest, 0.9f, 0.9f), 0.6f);
            Assert.IsTrue(w.Active);
            Assert.That(w.Hand, Is.EqualTo(0));
            Assert.That(w.Position.x, Is.LessThan(0.5f), "the left hand out to its side points left");
            // both up: the left keeps it
            Play(w, t => (new Vector2(-0.6f, 0.3f), new Vector2(0.6f, 0.3f), 0.9f, 0.9f), 0.6f);
            Assert.That(w.Hand, Is.EqualTo(0));
            // a hand lowered to the belly still holds it (it's the zone's bottom); at the side it rests
            Play(w, t => (new Vector2(-0.2f, -0.72f), Rest, 0.9f, 0.9f), 0.6f);
            Assert.IsTrue(w.Active, "pointing low");
            Play(w, t => (Rest, Rest, 0.9f, 0.9f), 0.6f);
            Assert.IsFalse(w.Active);
        }

        [Test]
        public void UnsureSamples_DontThrowIt_ALostHandRests()
        {
            var w = new Wand();
            var at = new Vector2(0.3f, 0f);
            float worst = 0f;
            var last = Vector2.zero;
            bool had = false;
            // every fourth sample unsure and flung across the screen (the pose model's glitches with a low visibility)
            int n = 0;
            Play(w, t =>
            {
                n++;
                return n % 4 == 0 ? (Rest, at + new Vector2(1.2f, -0.9f), 0.9f, 0.25f) : (Rest, at, 0.9f, 0.9f);
            }, 3f, each: t =>
            {
                if (t > 0.5f && had) worst = Mathf.Max(worst, Vector2.Distance(w.Position, last));
                last = w.Position;
                had = w.Active;
            });
            Assert.That(worst, Is.LessThan(0.01f), "no jumps from the unsure samples");
            // the hand unseen: the wand rests
            Play(w, t => (Rest, at, 0.9f, 0.1f), 1f);
            Assert.IsFalse(w.Active);
        }

        [Test]
        public void AStillHand_HoldsItStill_AMovingOne_ItFollows()
        {
            var w = new Wand();
            float shake = 0f;
            var last = Vector2.zero;
            int frames = 0;
            Play(w, t => (Rest, new Vector2(0.3f, 0f), 0.9f, 0.9f), 4f, noise: 0.008f, each: t =>
            {
                if (t > 1f)
                {
                    shake += Vector2.Distance(w.Position, last);
                    frames++;
                }
                last = w.Position;
            });
            // a pixel a frame or less on a 1920 x 1080 screen
            Assert.That(shake / frames * 1080f, Is.LessThan(1.5f), "a still hand's wand per frame, px");
            // a quick move across: the wand gets there within a fifth of a second of the hand
            float arrived = -1f;
            Play(w, t => (Rest, t < 0.2f ? new Vector2(0.3f, 0f) : new Vector2(1.2f, 0.6f), 0.9f, 0.9f), 1f, each: t =>
            {
                if (arrived < 0f && Vector2.Distance(w.Position, w.ToScreen(1, new Vector2(1.2f, 0.6f))) < 0.03f) arrived = t;
            });
            Assert.That(arrived, Is.InRange(0.2f, 0.45f), "the wand follows a quick move");
        }
    }
}
