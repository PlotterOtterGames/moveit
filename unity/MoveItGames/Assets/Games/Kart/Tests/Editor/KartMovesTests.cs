using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Kart.Tests
{
    /// <summary>
    /// Gara!'s camera reader on simulated bodies (view space as the camera sees a player ~2.8 m away: a torso of 0.18
    /// picture heights, 30 samples a second): the invisible wheel turns with the hands, a one-frame glitch of a hand is
    /// held off while a real turn follows a sample late, the lean steers with the hands down, a jump from a knee dip is a
    /// hop and turning the wheel isn't, lifting the wheel (sitting down) is a hop but one hand's glitch isn't, both hands
    /// up use the item once per raise.
    /// </summary>
    public class KartMovesTests
    {
        const float Torso = 0.18f, Hz = 30f;
        static readonly Vector2 ShoulderL = new Vector2(0.80f, 0.55f), ShoulderR = new Vector2(0.95f, 0.55f);
        static readonly Vector2 HipL = new Vector2(0.83f, 0.37f), HipR = new Vector2(0.92f, 0.37f);
        static readonly Vector2 Nose = new Vector2(0.875f, 0.63f);

        /// <summary>A player holding a wheel turned <paramref name="degrees"/> clockwise, hands
        /// <paramref name="apart"/> torso lengths apart at chest height; the body raised by <paramref name="lift"/>.</summary>
        static KartBodySample Wheel(float degrees, float lift = 0f, float apart = 0.9f, float handsUp = 0f)
        {
            float a = degrees * Mathf.Deg2Rad, r = apart * Torso * 0.5f;
            var centre = new Vector2(0.875f, 0.47f + lift + handsUp * Torso);
            return Body(centre + new Vector2(-r * Mathf.Cos(a), r * Mathf.Sin(a)), centre + new Vector2(r * Mathf.Cos(a), -r * Mathf.Sin(a)), lift);
        }

        static KartBodySample Body(Vector2 leftWrist, Vector2 rightWrist, float lift = 0f, float lean = 0f) => new KartBodySample
        {
            LeftShoulder = ShoulderL + new Vector2(lean * 0.4f, lift + lean * 0.5f), RightShoulder = ShoulderR + new Vector2(lean * 0.4f, lift - lean * 0.5f),
            LeftHip = HipL + new Vector2(0f, lift), RightHip = HipR + new Vector2(0f, lift), Nose = Nose + new Vector2(lean, lift),
            LeftWrist = leftWrist, RightWrist = rightWrist,
            LeftWristVisibility = 0.95f, RightWristVisibility = 0.95f, HipVisibility = 0.95f, NoseVisibility = 0.98f,
            Torso = Torso,
        };

        static List<KartMoveEvent> Feed(KartMoves m, IEnumerable<KartBodySample> samples, ref double t)
        {
            var seen = new List<KartMoveEvent>();
            foreach (var s in samples)
            {
                t += 1.0 / Hz;
                m.Update(t, s, seen);
            }
            return seen;
        }

        static IEnumerable<KartBodySample> Repeat(KartBodySample s, int n) => Enumerable.Repeat(s, n);

        [Test]
        public void TheWheel_TurnsWithTheHands()
        {
            var m = new KartMoves();
            double t = 0;
            Feed(m, Repeat(Wheel(0f), 20), ref t);
            Assert.IsTrue(m.Holding);
            Assert.AreEqual(0f, m.Steer, 0.01f, "level: straight on");
            foreach (var (deg, min, max) in new[] { (3f, -0.01f, 0.01f), (20f, 0.18f, 0.32f), (45f, 0.65f, 0.82f), (70f, 0.99f, 1.01f), (-30f, -0.53f, -0.35f) })
            {
                Feed(m, Repeat(Wheel(deg), 20), ref t);
                Assert.That(m.Steer, Is.InRange(min, max), $"{deg} degrees");
                Assert.AreEqual(deg, m.WheelAngle, 1.5f);
            }
            // the wheel tilted at "go" becomes level
            Feed(m, Repeat(Wheel(8f), 20), ref t);
            m.Calibrate();
            Feed(m, Repeat(Wheel(8f), 5), ref t);
            Assert.AreEqual(0f, m.Steer, 0.01f);
            Feed(m, Repeat(Wheel(38f), 20), ref t);
            Assert.That(m.Steer, Is.InRange(0.4f, 0.55f), "30 degrees from the new level");
        }

        [Test]
        public void AHandsGlitch_IsHeldOff_ARealTurnFollowsASampleLate()
        {
            var m = new KartMoves();
            double t = 0;
            Feed(m, Repeat(Wheel(0f), 20), ref t);
            float worst = 0f;
            // the right hand pulled onto its elbow for a frame (down and out), then back: the wheel would read ~-60 degrees
            var glitch = Wheel(0f);
            glitch.RightWrist += new Vector2(0.02f, -0.12f);
            foreach (var s in new[] { glitch, Wheel(0f), Wheel(0f), Wheel(0f) })
            {
                t += 1.0 / Hz;
                m.Update(t, s, null);
                worst = Mathf.Max(worst, Mathf.Abs(m.Steer));
            }
            Assert.Less(worst, 0.05f, "a one-frame glitch doesn't turn the kart");
            Assert.AreEqual(1, m.Ignored);
            // a real, quick turn (60 degrees within two samples) comes through a sample late
            Feed(m, new[] { Wheel(30f), Wheel(60f) }, ref t);
            Feed(m, Repeat(Wheel(60f), 3), ref t);
            Assert.Greater(m.Steer, 0.8f);
        }

        [Test]
        public void TheHandsDown_TheLeanSteers()
        {
            var m = new KartMoves();
            double t = 0;
            var down = Body(new Vector2(0.78f, 0.38f), new Vector2(0.97f, 0.38f));
            Feed(m, Repeat(down, 20), ref t);
            Assert.IsFalse(m.Holding, "arms hanging: no wheel");
            Assert.AreEqual(0f, m.Steer, 0.02f);
            var lean = Body(new Vector2(0.78f, 0.38f), new Vector2(0.97f, 0.38f), lean: 0.03f);
            Feed(m, Repeat(lean, 20), ref t);
            Assert.Greater(m.Steer, 0.4f, "leaning right turns right");
            var other = Body(new Vector2(0.78f, 0.38f), new Vector2(0.97f, 0.38f), lean: -0.03f);
            Feed(m, Repeat(other, 20), ref t);
            Assert.Less(m.Steer, -0.4f);
        }

        [Test]
        public void AJump_FromAKneeDip_IsAHop_TurningTheWheelIsNot()
        {
            var m = new KartMoves();
            double t = 0;
            Feed(m, Repeat(Wheel(0f), 30), ref t);
            // turning the wheel left and right for three seconds: no hop
            var turning = new List<KartBodySample>();
            for (int i = 0; i < 90; i++) turning.Add(Wheel(50f * Mathf.Sin(i / Hz * 2f * Mathf.PI * 0.8f)));
            Assert.IsEmpty(Feed(m, turning, ref t), "turning isn't jumping");
            // a jump: a knee dip (0.06 down over 0.25 s), the take-off (0.12 up in 0.15 s), the flight, the landing
            var jump = new List<KartBodySample>();
            for (int i = 0; i <= 7; i++) jump.Add(Wheel(0f, lift: -0.012f * Mathf.Sin(i / 7f * Mathf.PI * 0.5f)));
            for (int i = 1; i <= 5; i++) jump.Add(Wheel(0f, lift: Mathf.Lerp(-0.012f, 0.05f, i / 5f)));
            for (int i = 1; i <= 8; i++) jump.Add(Wheel(0f, lift: 0.05f * Mathf.Cos(i / 8f * Mathf.PI * 0.5f)));
            jump.AddRange(Repeat(Wheel(0f), 15));
            var seen = Feed(m, jump, ref t);
            Assert.AreEqual(1, seen.Count(e => e.Move == KartMove.Hop && e.Jumped), "one jump, one hop");
        }

        [Test]
        public void TheWheelLifted_IsAHop_OneHandsGlitchIsNot()
        {
            var m = new KartMoves();
            double t = 0;
            Feed(m, Repeat(Wheel(0f), 30), ref t);
            // both hands lift the wheel 0.35 torso lengths in 0.2 s (sitting on the couch)
            var lift = new List<KartBodySample>();
            for (int i = 1; i <= 6; i++) lift.Add(Wheel(0f, handsUp: 0.35f * i / 6f));
            for (int i = 1; i <= 6; i++) lift.Add(Wheel(0f, handsUp: 0.35f * (1f - i / 6f)));
            lift.AddRange(Repeat(Wheel(0f), 10));
            var seen = Feed(m, lift, ref t);
            Assert.AreEqual(1, seen.Count(e => e.Move == KartMove.Hop && !e.Jumped));
            Assert.IsFalse(seen.Any(e => e.Move == KartMove.HandsUp), "not up to the head: no item");
            // one hand flips up for two frames: nothing
            var glitch = Wheel(0f);
            glitch.LeftWrist += new Vector2(0f, 0.1f);
            Assert.IsEmpty(Feed(m, new[] { glitch, glitch }.Concat(Repeat(Wheel(0f), 10)), ref t));
        }

        [Test]
        public void BothHandsUp_UseTheItem_OncePerRaise()
        {
            var m = new KartMoves();
            double t = 0;
            Feed(m, Repeat(Wheel(0f), 20), ref t);
            var up = Body(new Vector2(0.84f, 0.70f), new Vector2(0.91f, 0.70f));
            var seen = Feed(m, Repeat(up, 20), ref t);
            Assert.AreEqual(1, seen.Count(e => e.Move == KartMove.HandsUp), "held up: one item");
            Assert.IsFalse(m.Holding, "hands up isn't holding the wheel");
            seen = Feed(m, Repeat(Wheel(0f), 10).Concat(Repeat(up, 10)), ref t);
            Assert.AreEqual(1, seen.Count(e => e.Move == KartMove.HandsUp), "down to the wheel and up again: the next");
            Assert.IsTrue(seen.Any(e => e.Move == KartMove.Hop), "a raise this quick also lifts the kart (a hop or a ramp's trick)");
        }
    }
}
