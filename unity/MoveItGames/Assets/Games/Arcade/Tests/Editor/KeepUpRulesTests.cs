using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Arcade.Tests
{
    /// <summary>Topi!: a ball bounced off the head and hands by simulated players, the balls' kinds over the minute, the
    /// floor ending a run.</summary>
    public class KeepUpRulesTests
    {
        static KeepUpRules Play(Profile prof, bool useHands, int seed = 5)
        {
            var rules = new KeepUpRules(1, 60f, seed);
            Sim.Run(rules, new[] { prof }, (t, p, body) =>
            {
                var ball = rules.Balls[p];
                if (prof == Profile.Idle || !ball.Live) return;
                // stand under where it will come down (to head height), late by the reaction
                float g = KeepUpRules.GravityOf(ball.Kind);
                float headY = 1.45f + ball.Radius + 0.3f;
                float vy = ball.Vel.y, dy = ball.Pos.y - headY;
                float when = (vy + Mathf.Sqrt(Mathf.Max(0f, vy * vy + 2f * g * dy))) / g;
                float landX = ball.Pos.x + ball.Vel.x * Mathf.Max(0f, when - prof.Reaction * 0.5f);
                body.Hips.x = Mathf.MoveTowards(body.Hips.x, Mathf.Clamp(landX, -2.4f, 2.4f), prof.StepSpeed * Sim.Dt);
                // the hands meet it when it comes down low beside the body (a child's way)
                var step = prof.HandSpeed * Sim.Dt;
                if (useHands && ball.Pos.y < 1.6f && ball.Vel.y < 0f)
                {
                    if (ball.Pos.x < body.Hips.x) body.LeftHand = SimBody.Toward(body.LeftHand, ball.Pos - new Vector2(0f, ball.Radius), step);
                    else body.RightHand = SimBody.Toward(body.RightHand, ball.Pos - new Vector2(0f, ball.Radius), step);
                }
                else
                {
                    body.LeftHand = SimBody.Toward(body.LeftHand, body.Hips + new Vector2(-0.6f, 0.5f), step);
                    body.RightHand = SimBody.Toward(body.RightHand, body.Hips + new Vector2(0.6f, 0.5f), step);
                }
            }, seed);
            var b = rules.Balls[0];
            System.Console.WriteLine($"keep-up {prof.Name}{(useHands ? " (hands)" : "")}: {rules.Score[0]} points, {b.Touches} touches ({b.Headers} headers), " +
                                  $"best streak {b.BestStreak}, {b.Drops} drops");
            return rules;
        }

        [Test]
        public void Balance_StayingUnderTheBall_KeepsItUp()
        {
            var perfect = Play(Profile.Perfect, false);
            var child = Play(Profile.Child, true);
            var idle = Play(Profile.Idle, false);
            Assert.Greater(perfect.Score[0], idle.Score[0] * 3);
            Assert.Greater(child.Score[0], idle.Score[0] * 2);
            Assert.AreEqual(0, ArcadeProgress.Stars(Machine.KeepUp, idle.Score[0]), "standing still: the ball soon drifts off");
            Assert.GreaterOrEqual(perfect.Balls[0].BestStreak, 15, "a good player keeps it up a long time");
            Assert.AreEqual(3, ArcadeProgress.Stars(Machine.KeepUp, perfect.Score[0]));
            Assert.GreaterOrEqual(ArcadeProgress.Stars(Machine.KeepUp, child.Score[0]), 1);
        }

        [Test]
        public void ATouchFromBelow_SendsItUpHighEnough_TheFloorEndsTheRun()
        {
            var rules = new KeepUpRules(1, 60f, 1);
            var body = StageBody.Standing();
            var ball = rules.Balls[0];
            while (!ball.Live) rules.Step(Sim.Dt, new[] { body });
            Assert.AreEqual(KeepUpRules.BallKind.Balloon, ball.Kind, "it starts as a balloon");
            // let it fall onto the head
            float top = float.MinValue;
            bool touched = false;
            for (int i = 0; i < 300 && ball.Live; i++)
            {
                rules.Events.Clear();
                rules.Step(Sim.Dt, new[] { body });
                if (rules.Events.Exists(e => e.Kind == EventKind.Touch && e.Variant == (int)Part.Head)) touched = true;
                if (touched) top = Mathf.Max(top, ball.Pos.y);
                if (touched && ball.Vel.y < 0f && ball.Pos.y < top - 0.05f) break;
            }
            Assert.IsTrue(touched, "the ball bounced off the head");
            Assert.Greater(top, 1.45f + 0.3f + 0.9f, "a header sends it up at least about a torso length");
            // nobody there: it falls to the floor
            for (int i = 0; i < 300 && ball.Live; i++) rules.Step(Sim.Dt, new StageBody[1]);
            Assert.IsFalse(ball.Live, "it reached the floor");
            Assert.AreEqual(0, ball.Streak);
        }

        [Test]
        public void TheBall_IsABalloon_ThenABeachBall_ThenAFootball()
        {
            var rules = new KeepUpRules(1, 60f, 1);
            var kinds = new System.Collections.Generic.HashSet<KeepUpRules.BallKind>();
            while (!rules.Finished)
            {
                rules.Step(Sim.Dt, new StageBody[1]);
                if (rules.Balls[0].Live) kinds.Add(rules.Balls[0].Kind);
            }
            CollectionAssert.AreEquivalent(new[] { KeepUpRules.BallKind.Balloon, KeepUpRules.BallKind.Beach, KeepUpRules.BallKind.Football }, kinds);
        }
    }
}
