using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Sports.Tests
{
    /// <summary>Sporte!'s rules without a scene: running on the spot, arm flicks and kicks from simulated joints, the
    /// race and football (volleyball is Volejboll!'s own since 2026-11-07).</summary>
    public class SportsRulesTests
    {
        const float Dt = 1f / 30f;   // camera samples
        const float Torso = 0.2f;     // view units: a player about two metres away

        /// <summary>Feeds a run detector <paramref name="seconds"/> of knees (and ankles) taking turns at
        /// <paramref name="stepsPerSecond"/>, the arms pumping; or both knees together (a jump).</summary>
        static RunDetector Run(float stepsPerSecond, float seconds, bool together = false, bool legsSeen = true)
        {
            var r = new RunDetector();
            for (float t = 0.01f; t < seconds; t += Dt)
            {
                float a = Mathf.Max(0f, Mathf.Sin(Mathf.PI * stepsPerSecond * t)) * 0.45f * Torso;
                float b = Mathf.Max(0f, Mathf.Sin(Mathf.PI * stepsPerSecond * t + Mathf.PI)) * 0.45f * Torso;
                if (together) b = a;
                float knee = 0.35f, ankle = 0.1f, wrist = 0.55f;
                r.Update(t, knee + a, knee + b, ankle + a * 0.9f, ankle + b * 0.9f, legsSeen ? 1f : 0f, wrist + b * 0.6f, wrist + a * 0.6f, Torso);
            }
            return r;
        }

        [Test]
        public void Running_TheStepsGiveTheSpeed_AJumpGivesNone()
        {
            var r = Run(3.4f, 3f);
            Assert.That(r.Cadence, Is.InRange(2.9f, 3.9f), "about 3.4 steps a second");
            Assert.Greater(r.Speed, 6f, "a good run on the spot");
            var slow = Run(2f, 3f);
            Assert.Less(slow.Speed, r.Speed, "slower steps, slower running");
            Assert.AreEqual(0, Run(3f, 3f, together: true).Steps, "both knees up together (a jump) is no step");
            Assert.Greater(Run(3f, 3f, legsSeen: false).Steps, 4, "the arms pumping count when the legs aren't seen");
        }

        static bool FlickFrom(Vector2 from, Vector2 to, float seconds, out Flick flick)
        {
            var f = new ArmFlick();
            flick = default;
            bool any = false;
            var shoulders = new Vector2(0.9f, 0.6f);
            for (float t = 0.01f; t < seconds + 0.5f; t += Dt)
            {
                float k = Mathf.Clamp01((t - 0.2f) / seconds);
                var hand = shoulders + Vector2.Lerp(from, to, k * k * (3f - 2f * k)) * Torso;
                if (f.Update(t, shoulders, Torso, shoulders + new Vector2(-0.3f, -0.6f) * Torso, 1f, hand, 1f, out var got))
                {
                    flick = got;
                    any = true;
                }
            }
            return any;
        }

        [Test]
        public void ArmFlick_AQuickMoveFlicks_ASlowOneDoesNot()
        {
            Assert.IsTrue(FlickFrom(new Vector2(0.3f, -0.6f), new Vector2(0.4f, 0.6f), 0.15f, out var up), "a quick swing up");
            Assert.IsTrue(up.RightHand && up.Direction.y > 0.7f, "with the hand and the way it went");
            Assert.IsFalse(FlickFrom(new Vector2(0.3f, -0.6f), new Vector2(0.4f, 0.6f), 1.5f, out _), "raising a hand slowly is nothing");
        }

        [Test]
        public void Kick_OneFootFast_ButAJumpIsNoKick()
        {
            var k = new KickDetector();
            var hips = new Vector2(0.9f, 0.45f);
            Vector2 left = hips + new Vector2(-0.12f, -2f) * Torso, rest = hips + new Vector2(0.12f, -2f) * Torso;
            bool kicked = false, jumped = false;
            for (float t = 0.01f; t < 1.5f; t += Dt)
            {
                float u = Mathf.Clamp01((t - 0.5f) / 0.15f);
                var right = rest + new Vector2(0.05f, 1.1f * u) * Torso;   // the right foot swings up fast
                kicked |= k.Update(t, hips, Torso, left, 1f, right, 1f, out _);
            }
            var j = new KickDetector();
            for (float t = 0.01f; t < 1.5f; t += Dt)
            {
                float u = Mathf.Clamp01((t - 0.5f) / 0.15f) * 1.1f * Torso;   // both feet up: a jump (hips with them)
                jumped |= j.Update(t, hips + Vector2.up * u, Torso, left + Vector2.up * u * 2f, 1f, rest + Vector2.up * u * 2f, 1f, out _);
            }
            Assert.IsTrue(kicked, "a fast foot is a kick");
            Assert.IsFalse(jumped, "both feet together is not");
        }

        [Test]
        public void Race_MovingInTheStartIsAFalseStart_ThenTheRunnerFinishes()
        {
            var race = new TrackRace(1);
            int falseStarts = 0;
            race.Events += (e, _, __) => { if (e == RaceEvent.FalseStart) falseStarts++; };
            for (int i = 0; i < 60; i++) race.Step(Dt, 0f);           // "on your marks"
            for (int i = 0; i < 20; i++) race.Step(Dt, 6f);           // running in the "set"
            Assert.AreEqual(1, falseStarts);
            Assert.AreEqual(RacePhase.Marks, race.Phase, "back to the start");
            float t = 0f;
            while (race.Phase != RacePhase.Over && t < 60f)
            {
                race.Step(Dt, race.Phase == RacePhase.Running ? 9.5f : 0f);
                t += Dt;
            }
            Assert.IsTrue(race.Human.Finished);
            Assert.That(race.Human.Finish, Is.InRange(10f, 11.5f), "100 m at 9.5 m/s");
            Assert.AreEqual(1, race.HumanPlace, "faster than the computer's runners");
            Assert.Greater(race.Points, TrackRace.Needs[2], "gold");
        }

        [Test]
        public void Race_ALeanAtTheLineGainsTime_TooEarlyItDoesNothing()
        {
            float Run(float dipAt)
            {
                var race = new TrackRace(3);
                bool dipped = false;
                for (int i = 0; i < 2000 && race.Phase != RacePhase.Over; i++)
                {
                    race.Step(Dt, race.Phase == RacePhase.Running ? 8f : 0f);
                    if (!dipped && race.Phase == RacePhase.Running && race.Human.Distance >= dipAt) dipped = race.Dip() || dipAt < 90f;
                }
                return race.Human.Finish;
            }
            float none = Run(200f), early = Run(50f), late = Run(96f);
            Assert.AreEqual(none, early, 1e-4f, "a lean half way down the track does nothing");
            Assert.AreEqual(none - TrackRace.DipGain, late, 0.02f, "a lean in the last metres gains the dip");
        }

        [Test]
        public void Football_AKickInTimeShoots_ABodyOnTheShotSaves()
        {
            var match = new FootballMatch(4);
            bool kicked = false;
            float t = 0f;
            while (match.Attacking && t < 20f)
            {
                if (!kicked && !float.IsNaN(match.UntilKick) && match.UntilKick < 0.05f) kicked = match.Kick(new Vector2(-0.8f, 0.3f).normalized, 6f, -0.5f);
                match.Step(Dt, new Vector3[0], 0);
                t += Dt;
            }
            Assert.IsTrue(kicked, "a kick as the pass arrives meets the ball");
            Assert.AreEqual(FootballPhase.KeeperReady, match.Phase, "then it's the player's turn in goal");
            Assert.IsFalse(match.Kick(Vector2.up, 6f, 0f), "no kicking while keeping");
            var spot = new Vector2(1.5f, 1.2f);
            match.ShootAt(spot);
            var body = new[] { new Vector3(spot.x, spot.y, 0.2f) };
            int saves = match.Saves;
            for (int i = 0; i < 60 && match.Phase == FootballPhase.Flight; i++) match.Step(Dt, body, 1);
            Assert.AreEqual(saves + 1, match.Saves, "a hand where the shot goes saves it");
        }
    }
}
