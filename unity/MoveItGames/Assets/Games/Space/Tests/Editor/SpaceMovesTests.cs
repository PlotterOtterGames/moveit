using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Space.Tests
{
    /// <summary>Yjet!'s body controls on simulated bodies: a step or a lean steers, both arms up boost (a glitch doesn't),
    /// ducking slows.</summary>
    public class SpaceMovesTests
    {
        const float T = 0.3f;

        /// <summary>A standing player (torso 0.3 view units, the hips at y 0), moved <paramref name="side"/> torso lengths
        /// to the right, leaning <paramref name="lean"/> degrees right, down <paramref name="duck"/>, arms up or not.</summary>
        static SpaceBodySample Body(float side = 0f, float lean = 0f, float duck = 0f, bool armsUp = false, bool oneArm = false)
        {
            float x = side * T, y = -duck * T;
            float a = -lean * Mathf.Deg2Rad;
            var across = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (T * 0.62f);
            var shoulders = new Vector2(x, y + T);
            var b = new SpaceBodySample
            {
                LeftShoulder = shoulders - across, RightShoulder = shoulders + across,
                LeftHip = new Vector2(x - T * 0.4f, y), RightHip = new Vector2(x + T * 0.4f, y),
                Nose = shoulders + new Vector2(0f, T * 0.55f),
                LeftWristVisibility = 0.9f, RightWristVisibility = 0.9f, HipVisibility = 0.9f, NoseVisibility = 0.9f,
                Torso = T,
            };
            b.LeftWrist = armsUp || oneArm ? shoulders + new Vector2(-T * 0.5f, T * 1.1f) : new Vector2(x - T * 0.7f, y - T * 0.1f);
            b.RightWrist = armsUp ? shoulders + new Vector2(T * 0.5f, T * 1.1f) : new Vector2(x + T * 0.7f, y - T * 0.1f);
            return b;
        }

        static SpaceMoves Ready(out double t)
        {
            var m = new SpaceMoves();
            t = 1.0;
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body());
            m.Calibrate();
            return m;
        }

        [Test]
        public void AStep_SteersHard_ALean_Gently_StandingStill_Not()
        {
            var m = Ready(out double t);
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body());
            Assert.AreEqual(0f, m.Steer, 1e-4f, "standing in the middle");
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body(side: 0.03f, lean: 2f));
            Assert.AreEqual(0f, m.Steer, 1e-4f, "swaying: the dead zones");
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body(side: 0.7f));
            Assert.AreEqual(1f, m.Steer, 1e-3f, "a step to the right: full right");
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body(side: -0.7f));
            Assert.AreEqual(-1f, m.Steer, 1e-3f, "and left");
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body(lean: 14f));
            Assert.That(m.Steer, Is.InRange(0.25f, 0.6f), "a lean to the right: gently right");
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body(lean: -30f));
            Assert.AreEqual(-0.8f, m.Steer, 0.01f, "a deep lean left: most of a turn");
        }

        [Test]
        public void BothArmsUp_Boost_OneArmOrOneGlitch_Not()
        {
            var m = Ready(out double t);
            m.Update(t += 1.0 / 30, Body(armsUp: true));
            Assert.IsFalse(m.Boosting, "one sample: maybe a glitch");
            m.Update(t += 1.0 / 30, Body());
            m.Update(t += 1.0 / 30, Body(armsUp: true));
            Assert.IsFalse(m.Boosting);
            m.Update(t += 1.0 / 30, Body(armsUp: true));
            Assert.IsTrue(m.Boosting, "two in a row");
            m.Update(t += 1.0 / 30, Body());
            m.Update(t += 1.0 / 30, Body());
            Assert.IsTrue(m.Boosting, "two samples down: still on");
            m.Update(t += 1.0 / 30, Body());
            Assert.IsFalse(m.Boosting, "three: off");
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body(oneArm: true));
            Assert.IsFalse(m.Boosting, "one arm up is no boost");
        }

        [Test]
        public void Ducking_IsSeen_StandingUpEndsIt()
        {
            var m = Ready(out double t);
            for (int i = 0; i < 5; i++) m.Update(t += 1.0 / 30, Body(duck: 0.1f));
            Assert.IsFalse(m.Ducking, "a little dip");
            for (int i = 0; i < 5; i++) m.Update(t += 1.0 / 30, Body(duck: 0.3f));
            Assert.IsTrue(m.Ducking);
            for (int i = 0; i < 5; i++) m.Update(t += 1.0 / 30, Body(duck: 0.15f));
            Assert.IsTrue(m.Ducking, "on until clearly up");
            for (int i = 0; i < 5; i++) m.Update(t += 1.0 / 30, Body());
            Assert.IsFalse(m.Ducking);
        }

        [Test]
        public void Calibrate_WhereTheyStandIsTheMiddle()
        {
            var m = new SpaceMoves();
            double t = 1.0;
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body(side: 0.5f));
            m.Calibrate();
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body(side: 0.5f));
            Assert.AreEqual(0f, m.Steer, 1e-3f, "standing off-centre when the trip starts is straight on");
            for (int i = 0; i < 10; i++) m.Update(t += 1.0 / 30, Body(side: 1.2f));
            Assert.AreEqual(1f, m.Steer, 1e-3f);
        }
    }
}
