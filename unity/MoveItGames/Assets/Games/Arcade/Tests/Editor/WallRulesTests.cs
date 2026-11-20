using System;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Arcade.Tests
{
    /// <summary>Muri!: the pose check on exact and noisy bodies, simulated players making the shapes (late, sloppy,
    /// still), and the walls' pace.</summary>
    public class WallRulesTests
    {
        static StageBody Posed(WallPose pose, float error = 0f, System.Random rng = null)
        {
            var b = new SimBody { UseAngles = true };
            float E() => rng == null ? 0f : (float)(rng.NextDouble() * 2 - 1) * error;
            b.LU = pose.LeftUpper + E();
            b.LF = pose.LeftFore + E();
            b.RU = pose.RightUpper + E();
            b.RF = pose.RightFore + E();
            if (pose.Legs)
            {
                b.LeftThigh = pose.LeftThigh + E();
                b.RightThigh = pose.RightThigh + E();
            }
            b.Squat = pose.Squat ? 1f : 0f;
            b.Hips = new Vector2(pose.Step, 0f);
            return b.ToStage(new System.Random(1), 0f);
        }

        [Test]
        public void EveryPose_MatchesItself_AndNotStandingStill()
        {
            var standing = StageBody.Standing();
            foreach (var pose in WallRules.Poses)
            {
                Assert.AreEqual(1f, WallRules.Match(Posed(pose), pose, 30f), 1e-4f, $"{pose.Id} matches itself");
                Assert.Less(WallRules.Match(standing, pose, 40f), WallRules.PassAt, $"standing still doesn't pass {pose.Id}");
                Assert.AreEqual(1f, WallRules.Match(Posed(pose, 20f, new System.Random(pose.Id.Length)), pose, 30f), 1e-4f,
                                $"{pose.Id} with every bone 20 degrees off still matches");
            }
        }

        [Test]
        public void ShapesThatLookAlike_DontPassForEachOther()
        {
            var y = Array.Find(WallRules.Poses, p => p.Id == "y");
            var t = Array.Find(WallRules.Poses, p => p.Id == "t");
            var muscle = Array.Find(WallRules.Poses, p => p.Id == "muscle");
            var heart = Array.Find(WallRules.Poses, p => p.Id == "heart");
            Assert.Less(WallRules.Match(Posed(t), y, 40f), WallRules.PassAt, "arms out isn't arms up");
            Assert.Less(WallRules.Match(Posed(t), muscle, 40f), WallRules.PassAt, "arms out isn't the muscle");
            Assert.Less(WallRules.Match(Posed(y), heart, 40f), WallRules.PassAt, "a Y isn't hands together above the head");
            var stepLeft = Array.Find(WallRules.Poses, p => p.Id == "step_left");
            Assert.Less(WallRules.Match(Posed(y), stepLeft, 40f), 1f, "the right shape in the wrong place isn't perfect");
            var squat = Array.Find(WallRules.Poses, p => p.Id == "squat_t");
            Assert.Less(WallRules.Match(Posed(t), squat, 40f), 1f, "arms out standing up isn't the squat");
        }

        [Test]
        public void ABoneTheCameraDoesntSee_DoesntCount()
        {
            var y = Array.Find(WallRules.Poses, p => p.Id == "y");
            var body = Posed(y);
            body.Seen[(int)Part.LeftHand] = 0f;
            body.Seen[(int)Part.RightHand] = 0f;
            Assert.AreEqual(0.5f, WallRules.Match(body, y, 30f), 1e-4f, "only the upper arms count");
        }

        /// <summary>A player who makes each wall's shape <paramref name="reaction"/> s after it appears, its bones
        /// <paramref name="error"/> degrees off, moving them at <paramref name="degreesPerSecond"/>.</summary>
        static WallRules Play(Profile prof, float error, float degreesPerSecond, int seed = 5, Func<float, float> lateBy = null)
        {
            var rules = new WallRules(1, 60f, seed);
            var rng = new System.Random(seed);
            WallRules.Wall shaping = null;
            float lu = 10, lf = 5, ru = 10, rf = 5, lt = 12, rt = 12;
            float targetErr = 0f;
            Sim.Run(rules, new[] { prof }, (t, p, body) =>
            {
                body.UseAngles = true;
                var coming = rules.Coming;
                if (coming != shaping && coming != null)
                {
                    shaping = coming;
                    targetErr = (float)(rng.NextDouble() * 2 - 1) * error;
                }
                bool act = prof != Profile.Idle && coming != null && t - coming.Spawn >= prof.Reaction + (lateBy?.Invoke(coming.Arrive - coming.Spawn) ?? 0f);
                var pose = act ? coming.Pose : null;
                float step = degreesPerSecond * Sim.Dt;
                lu = Mathf.MoveTowards(lu, pose != null ? pose.LeftUpper + targetErr : 10f, step);
                lf = Mathf.MoveTowards(lf, pose != null ? pose.LeftFore - targetErr : 5f, step);
                ru = Mathf.MoveTowards(ru, pose != null ? pose.RightUpper + targetErr : 10f, step);
                rf = Mathf.MoveTowards(rf, pose != null ? pose.RightFore - targetErr : 5f, step);
                lt = Mathf.MoveTowards(lt, pose != null && pose.Legs ? pose.LeftThigh : 12f, step * 0.5f);
                rt = Mathf.MoveTowards(rt, pose != null && pose.Legs ? pose.RightThigh : 12f, step * 0.5f);
                body.LU = lu;
                body.LF = lf;
                body.RU = ru;
                body.RF = rf;
                body.LeftThigh = lt;
                body.RightThigh = rt;
                body.Squat = Mathf.MoveTowards(body.Squat, pose != null && pose.Squat ? 1f : 0f, 2.5f * Sim.Dt);
                body.Hips.x = Mathf.MoveTowards(body.Hips.x, pose != null ? pose.Step : 0f, prof.StepSpeed * Sim.Dt);
            }, seed);
            var b = rules.Boards[0];
            System.Console.WriteLine($"wall {prof.Name} (error {error}, {degreesPerSecond} deg/s): {rules.Score[0]} points, passed {b.Passed} " +
                                  $"({b.Perfects} perfect), failed {b.Failed}, best streak {b.BestStreak}");
            return rules;
        }

        [Test]
        public void Balance_PerfectToIdle()
        {
            var perfect = Play(Profile.Perfect, 6f, 360f);
            var good = Play(Profile.Good, 12f, 300f);
            var child = Play(Profile.Child, 33f, 160f);
            var idle = Play(Profile.Idle, 0f, 0f);
            Assert.AreEqual(0, perfect.Boards[0].Failed, "a perfect player passes every wall");
            Assert.AreEqual(3, ArcadeProgress.Stars(Machine.Wall, perfect.Score[0]));
            Assert.GreaterOrEqual(good.Score[0], child.Score[0]);
            Assert.GreaterOrEqual(child.Boards[0].Passed, child.Boards[0].Failed, "a sloppy child still passes most walls");
            Assert.GreaterOrEqual(ArcadeProgress.Stars(Machine.Wall, child.Score[0]), 1);
            Assert.AreEqual(0, idle.Score[0], "standing still passes nothing");
            Assert.GreaterOrEqual(perfect.Boards[0].Passed, 12, "a dozen walls or more in a minute");
        }

        [Test]
        public void AShapeMadeJustAfterTheWallArrives_StillPasses_MuchLaterDoesnt()
        {
            // the shape is made right as the wall arrives: the camera shows it ~0.1 s later, within Late
            var justLate = Play(Profile.Perfect, 4f, 2000f, 7, approach => approach - Profile.Perfect.Reaction - 0.1f);
            Assert.AreEqual(0, justLate.Boards[0].Failed, "a shape made just as the wall arrives passes");
            var tooLate = Play(Profile.Perfect, 4f, 2000f, 7, approach => approach - Profile.Perfect.Reaction + 0.25f);
            Assert.AreEqual(0, tooLate.Boards[0].Passed, "a shape made a quarter second after the wall passed fails");
        }
    }
}
