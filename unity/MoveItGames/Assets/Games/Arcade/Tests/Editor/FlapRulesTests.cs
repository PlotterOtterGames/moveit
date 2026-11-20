using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Arcade.Tests
{
    /// <summary>Zogu!: the flap detector on simulated arms (both, one, slow, glitches, 30 and 15 samples a second), and
    /// birds flown by simulated players.</summary>
    public class FlapRulesTests
    {
        /// <summary>Both arms flapping: from out to the side (~100 degrees) down to the hips (~20) in
        /// <paramref name="down"/> s and back up in <paramref name="up"/> s.</summary>
        static float FlapAngle(float t, float down, float up, float high = 100f, float low = 20f)
        {
            float period = down + up, u = t % period;
            if (u < down) return Mathf.Lerp(high, low, Mathf.SmoothStep(0f, 1f, u / down));
            return Mathf.Lerp(low, high, Mathf.SmoothStep(0f, 1f, (u - down) / up));
        }

        static int Count(System.Func<float, (float left, float right)> arms, float seconds, float rate = 30f, float glitchesPerSecond = 0f)
        {
            var d = new FlapDetector();
            var rng = new System.Random(4);
            var body = new SimBody { UseAngles = true };
            int glitchFrames = 0;
            int flaps = 0;
            float dt = 1f / rate;
            for (float t = 0f; t < seconds; t += dt)
            {
                var (l, r) = arms(t);
                body.LU = l;
                body.LF = l;
                body.RU = r;
                body.RF = r;
                var stage = body.ToStage(rng, 0.01f);
                if (glitchFrames == 0 && rng.NextDouble() < glitchesPerSecond * dt) glitchFrames = 1 + rng.Next(2);
                if (glitchFrames > 0)
                {
                    glitchFrames--;
                    var hand = rng.NextDouble() < 0.5 ? Part.LeftHand : Part.RightHand;
                    stage[hand] += new Vector2((float)(rng.NextDouble() - 0.5) * 1.6f, (float)(rng.NextDouble() - 0.5) * 1.6f);
                }
                if (d.Update(stage, dt)) flaps++;
            }
            return flaps;
        }

        [Test]
        public void BothArmsFlapping_AreFlaps_AtThirtyAndFifteenSamplesASecond()
        {
            // 1.5 flaps a second for 10 s
            int at30 = Count(t => (FlapAngle(t, 0.22f, 0.45f), FlapAngle(t, 0.22f, 0.45f)), 10f);
            int at15 = Count(t => (FlapAngle(t, 0.22f, 0.45f), FlapAngle(t, 0.22f, 0.45f)), 10f, 15f);
            System.Console.WriteLine($"flaps: {at30} at 30 Hz, {at15} at 15 Hz (15 expected)");
            Assert.That(at30, Is.InRange(13, 16));
            Assert.That(at15, Is.InRange(12, 16));
            // small quick flaps (a child's: 40 degrees in 0.18 s) count too
            int small = Count(t => (FlapAngle(t, 0.18f, 0.35f, 70f, 30f), FlapAngle(t, 0.18f, 0.35f, 70f, 30f)), 10f);
            System.Console.WriteLine($"small flaps: {small} (19 expected)");
            Assert.GreaterOrEqual(small, 15);
            // gentle ones: 50 degrees in 0.35 s, 1.25 a second, counted by the box's 15 poses a second
            int gentle = Count(t => (FlapAngle(t, 0.35f, 0.45f, 80f, 30f), FlapAngle(t, 0.35f, 0.45f, 80f, 30f)), 10f, 15f);
            System.Console.WriteLine($"gentle flaps: {gentle} (12-13 expected)");
            Assert.GreaterOrEqual(gentle, 10);
        }

        [Test]
        public void OneArm_SlowArms_StillArms_AndGlitches_AreNotFlaps()
        {
            Assert.AreEqual(0, Count(t => (FlapAngle(t, 0.22f, 0.45f), 15f), 10f), "one arm flapping isn't a flap");
            Assert.AreEqual(0, Count(t => (FlapAngle(t, 1.2f, 1.2f), FlapAngle(t, 1.2f, 1.2f)), 10f), "arms lowered slowly aren't flaps");
            Assert.AreEqual(0, Count(t => (15f, 15f), 20f, 30f, 2f), "still arms with the pose model's glitches (2 a second) aren't flaps");
            Assert.AreEqual(0, Count(t => (90f, 90f), 20f, 30f, 2f), "arms held out with glitches aren't flaps");
        }

        static FlapRules Fly(Profile prof, float targetHeight, int seed = 3)
        {
            var rules = new FlapRules(1, 60f, seed);
            float flapStart = -9f;
            Sim.Run(rules, new[] { prof }, (t, p, body) =>
            {
                body.UseAngles = true;
                var bird = rules.Birds[p];
                // aim for the next star ahead (or a height), flap when below it
                float goal = targetHeight;
                float best = float.MaxValue;
                foreach (var th in rules.Things)
                {
                    float ahead = th.X - rules.BirdAt(p);
                    if (th.Done || ahead < 0.05f || ahead > 0.6f) continue;
                    if (th.Kind == FlapRules.ThingKind.Cloud) continue;
                    if (ahead < best)
                    {
                        best = ahead;
                        goal = th.Y;
                    }
                }
                float since = t - flapStart;
                if (prof != Profile.Idle && since > 0.3f + prof.Reaction * 0.5f && bird.Y < goal - 0.04f + (prof == Profile.Child ? -0.06f : 0f))
                    flapStart = t;
                float a = since < 0.55f ? FlapAngle(since, 0.2f, 0.35f) : 100f;
                if (prof == Profile.Idle) a = 15f;
                body.LU = body.LF = body.RU = body.RF = a;
            }, seed);
            var b = rules.Birds[0];
            System.Console.WriteLine($"flap {prof.Name}: {rules.Score[0]} points, {b.Stars} stars, {b.Balloons} balloons, {b.Rings} rings, " +
                                  $"{b.Bumps} clouds, {b.Flaps} flaps");
            return rules;
        }

        [Test]
        public void Balance_FlappingKeepsTheBirdUp_StandingStillDoesnt()
        {
            var perfect = Fly(Profile.Perfect, 0.5f);
            var child = Fly(Profile.Child, 0.5f);
            var idle = Fly(Profile.Idle, 0.5f);
            Assert.Greater(perfect.Score[0], 200);
            Assert.Greater(child.Score[0], 200);
            Assert.AreEqual(0, idle.Birds[0].Flaps, "standing still never flaps");
            Assert.AreEqual(3, ArcadeProgress.Stars(Machine.Flap, perfect.Score[0]));
            Assert.GreaterOrEqual(ArcadeProgress.Stars(Machine.Flap, child.Score[0]), 1);
        }

        [Test]
        public void ABirdThatDoesntFlap_SitsOnTheGround_AFlapLiftsIt()
        {
            var rules = new FlapRules(1, 60f, 1);
            for (int i = 0; i < 90; i++) rules.Step(Sim.Dt, new StageBody[1]);
            Assert.AreEqual(FlapRules.Ground, rules.Birds[0].Y, 1e-4f, "it glides down to the ground");
            rules.Flap(0);
            for (int i = 0; i < 10; i++) rules.Step(Sim.Dt, new StageBody[1]);
            Assert.Greater(rules.Birds[0].Y, FlapRules.Ground + 0.1f, "a flap lifts it");
        }
    }
}
