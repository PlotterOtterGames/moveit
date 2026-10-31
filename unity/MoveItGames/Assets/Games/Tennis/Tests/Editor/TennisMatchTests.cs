using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Tennis.Tests
{
    /// <summary>
    /// The singles match rules with simulated players: the body moves the player and the assist glides only to near
    /// balls, standing at the net volleys, the swing's timing aims the ball, the camera's lag is taken out, early swings
    /// wait for the ball, the player's shots always go in, and whole matches against the ladder end with believable
    /// results for players who swing and move well, a child, a struggling player, one who never moves and one who stands
    /// still.
    /// </summary>
    public class TennisMatchTests
    {
        const float Dt = 1f / 60f;
        static readonly Vector2 Across = new Vector2(-1f, 0.1f).normalized, Down = new Vector2(-0.2f, -1f).normalized;

        static void StepUntil(TennisMatch m, Func<bool> done, float seconds = 30f)
        {
            double end = m.Time + seconds;
            while (!done())
            {
                Assert.Less(m.Time, end, "timed out waiting");
                m.Step(Dt);
            }
        }

        /// <summary>A practice match where the player has served and the computer's feed is on its way to the player.</summary>
        static TennisMatch FeedComing(int seed, List<MatchEvent> events = null)
        {
            var m = new TennisMatch(TennisLevel.For(1), seed: seed) { Practice = true };
            if (events != null) m.Events += events.Add;
            m.Begin();
            StepUntil(m, () => m.Phase == MatchPhase.Toss);
            StepUntil(m, () => m.Time >= m.TossTop);
            Assert.AreEqual(SwingResult.Serve, m.Swing(m.TossTop, 6f, Down));
            StepUntil(m, () => m.Phase == MatchPhase.Rally && m.LastHitter == 1 && m.Next.Active && m.Next.Team == 0);
            return m;
        }

        static Shot HitWith(TennisMatch m, double offset, float lag = 0f)
        {
            Shot? hit = null;
            m.Events += e => { if (e.Kind == MatchEventKind.Hit && e.Team == 0) hit = e.Shot; };
            StepUntil(m, () => m.Next.Locked);   // the contact stops following the body
            double at = m.Next.Time + offset;
            StepUntil(m, () => m.Time >= at + lag);
            Assert.AreEqual(SwingResult.Hit, m.Swing(at, 6f, Across));
            StepUntil(m, () => hit.HasValue, 2f);
            return hit.Value;
        }

        // ---------------------------------------------------------------- moving

        [Test]
        public void TheBody_MapsOntoThePlayersHalf()
        {
            Assert.AreEqual(new Vector2(0f, -TennisMatch.HomeDepth), TennisMatch.BodyToCourt(0f, 1f), "standing where calibrated");
            Assert.AreEqual(Court.SinglesHalfWidth, TennisMatch.BodyToCourt(0.9f, 1f).x, 1e-4f, "a step of 0.9 torso lengths: the sideline");
            Assert.AreEqual(-Court.SinglesHalfWidth, TennisMatch.BodyToCourt(-0.9f, 1f).x, 1e-4f);
            Assert.AreEqual(-TennisMatch.HomeDepth, TennisMatch.BodyToCourt(0f, 1.025f).y, 1e-4f, "leaning a little: no depth change");
            Assert.Greater(TennisMatch.BodyToCourt(0f, 1.2f).y, -4.5f, "torso 20 % bigger (a step toward the TV): at the net");
            Assert.Less(TennisMatch.BodyToCourt(0f, 0.9f).y, -14.5f, "a step back: behind the baseline");
            Assert.AreEqual(-TennisMatch.NearestToNet, TennisMatch.BodyToCourt(0f, 2f).y, 1e-4f, "never past the net");
        }

        [Test]
        public void ThePlayer_GoesWhereTheBodyStands()
        {
            var m = new TennisMatch(TennisLevel.For(1), seed: 1, firstServer: 1);
            m.Begin();
            m.HumanWanted = new Vector2(3f, -9f);
            for (int i = 0; i < 60; i++) m.Step(Dt);
            Assert.Less(Vector2.Distance(m.Human.Position, new Vector2(3f, -9f)), 0.05f);
        }

        [Test]
        public void TheAssist_GlidesToANearBall_NotToAFarOne()
        {
            int near = 0, far = 0;
            for (int seed = 1; seed <= 8; seed++)
            {
                foreach (bool stepTowardIt in new[] { true, false })
                {
                    var m = new TennisMatch(TennisLevel.For(1), seed: seed, firstServer: 1);
                    m.Begin();
                    // the serve comes; the body stands a metre and a half from the spot beside the ball, or 5 m away
                    StepUntil(m, () => m.Phase == MatchPhase.Rally && m.Next.Active && m.Next.Team == 0);
                    var spot = m.Next.Spot;
                    m.HumanWanted = stepTowardIt ? spot + new Vector2(-0.9f, 1.2f) : spot + new Vector2(-5f, 0f);
                    StepUntil(m, () => m.Next.Locked);
                    double at = m.Next.Time;
                    StepUntil(m, () => m.Time >= at);
                    var r = m.Swing(at, 6f, Across);
                    if (stepTowardIt)
                    {
                        Assert.IsTrue(m.Next.Reachable || r == SwingResult.Hit, $"seed {seed}: near enough for the glide");
                        Assert.AreEqual(SwingResult.Hit, r, $"seed {seed}: standing near, the glide gets there");
                        near++;
                    }
                    else
                    {
                        Assert.AreEqual(SwingResult.TooFar, r, $"seed {seed}: standing on the far side, the ball goes by");
                        StepUntil(m, () => m.Phase == MatchPhase.PointOver, 5f);
                        Assert.AreEqual(1, m.PointWinner);
                        far++;
                    }
                }
            }
            Assert.AreEqual(8, near);
            Assert.AreEqual(8, far);
        }

        [Test]
        public void AtTheNet_ThePlayerVolleys()
        {
            int volleys = 0;
            for (int seed = 1; seed <= 30 && volleys < 3; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(1), seed: seed);
                m.HumanWanted = new Vector2(0f, -4f);   // stepped toward the TV: at the net
                bool volleyHit = false;
                m.Events += e => { if (e.Kind == MatchEventKind.Hit && e.Team == 0 && e.Shot.Kind is ShotKind.Volley or ShotKind.Smash) volleyHit = true; };
                m.Begin();
                StepUntil(m, () => m.Phase == MatchPhase.Toss);
                StepUntil(m, () => m.Time >= m.TossTop);
                m.Swing(m.TossTop, 6f, Down);
                StepUntil(m, () => m.Phase != MatchPhase.Rally || m.LastHitter == 1 && m.Next.Active && m.Next.Team == 0, 6f);
                if (m.Phase != MatchPhase.Rally || !m.Next.Volley) continue;   // not returned, or lobbed over
                m.HumanWanted = new Vector2(m.Next.Spot.x, -4f);   // a step to the side, staying at the net
                StepUntil(m, () => m.Next.Locked);
                if (!m.Next.Volley) continue;
                double at = m.Next.Time;
                StepUntil(m, () => m.Time >= at);
                string where = $"seed {seed}: player at {m.Human.Position}, spot {m.Next.Spot}, ball at {m.Next.Point}, smash {m.Next.Smash}, " +
                               $"reachable {m.Next.Reachable}";
                Assert.AreEqual(SwingResult.Hit, m.Swing(at, 6f, Across), where);
                Assert.IsTrue(volleyHit, $"seed {seed}: hit before the bounce");
                volleys++;
            }
            Assert.AreEqual(3, volleys, "standing at the net, returns are volleyed");
        }

        // ---------------------------------------------------------------- serving and swinging

        [Test]
        public void Serve_AtTheTopOfTheToss_IsAPowerServe_ThatLandsInTheBox()
        {
            var events = new List<MatchEvent>();
            var m = new TennisMatch(TennisLevel.For(1), seed: 5);
            m.Events += events.Add;
            m.Begin();
            StepUntil(m, () => m.Phase == MatchPhase.Toss);
            StepUntil(m, () => m.Time >= m.TossTop + 0.1);
            Assert.AreEqual(SwingResult.Serve, m.Swing(m.TossTop + 0.02, 8f, Down), "the camera saw it at the top, 80 ms ago");
            StepUntil(m, () => events.Exists(e => e.Kind == MatchEventKind.Bounce), 3f);
            var serve = events.Find(e => e.Kind == MatchEventKind.Hit);
            var bounce = events.Find(e => e.Kind == MatchEventKind.Bounce);
            Assert.AreEqual(ShotKind.PowerServe, serve.Shot.Kind);
            Assert.IsTrue(bounce.In, "in");
            Assert.IsTrue(Court.InServiceBox(bounce.Position, 1, deuceCourt: true), $"in the deuce box ({bounce.Position})");
        }

        [Test]
        public void AFirstSwing_TossesTheBall_ASwingWhileItsUp_Serves()
        {
            var m = new TennisMatch(TennisLevel.For(1), seed: 2);
            m.Begin();
            StepUntil(m, () => m.Time > 0.5);
            Assert.AreEqual(MatchPhase.Serve, m.Phase);
            Assert.AreEqual(SwingResult.Toss, m.Swing(m.Time, 6f, Down), "like Wii's flick up");
            Assert.AreEqual(MatchPhase.Toss, m.Phase);
            StepUntil(m, () => m.Time >= m.TossTop + 0.3);
            Assert.AreEqual(SwingResult.Serve, m.Swing(m.Time, 6f, Down));
            Assert.AreEqual(ShotKind.Serve, m.LastShot.Kind, "off the top: an ordinary serve");
        }

        [Test]
        public void AMissedToss_IsCaughtAndTossedAgain()
        {
            var m = new TennisMatch(TennisLevel.For(1), seed: 2);
            m.Begin();
            StepUntil(m, () => m.Phase == MatchPhase.Toss);
            StepUntil(m, () => m.Phase == MatchPhase.Serve, 3f);
            Assert.AreEqual(1, m.TossesMissed);
            StepUntil(m, () => m.Phase == MatchPhase.Toss, 3f);
            Assert.AreEqual(0, m.Score.Points(1), "no fault: nobody scored");
        }

        [Test]
        public void Timing_AimsTheBall_EarlyPullsItAcross_LatePushesItTheOtherWay()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var early = HitWith(FeedComing(seed), -0.3);
                var onTime = HitWith(FeedComing(seed), 0.0);
                var late = HitWith(FeedComing(seed), 0.2);
                var m = FeedComing(seed);
                StepUntil(m, () => m.Next.Locked);
                float strokeSide = (m.Next.Stroke == Stroke.Backhand ? -1f : 1f) * m.Next.Hitter.RacketSide;
                Assert.Less((early.Target.x - onTime.Target.x) * strokeSide, -1.2f, $"seed {seed}: early goes across ({m.Next.Stroke})");
                Assert.Greater((late.Target.x - onTime.Target.x) * strokeSide, 1.2f, $"seed {seed}: late goes the other way");
                Assert.Less(early.Timing, -0.5f);
                Assert.AreEqual(0f, onTime.Timing, 0.01f);
                Assert.IsTrue(onTime.Perfect);
                Assert.Greater(late.Timing, 0.5f);
            }
        }

        [Test]
        public void CameraLag_DoesntMakeAnOnTimeSwingLate()
        {
            var shot = HitWith(FeedComing(3), 0.0, lag: 0.15f);
            Assert.IsTrue(shot.Perfect, "it arrived 150 ms late, but the camera saw it right on time");
            Assert.AreEqual(0f, shot.Timing, 0.01f);
        }

        [Test]
        public void AnEarlySwing_WaitsForTheBall()
        {
            var events = new List<MatchEvent>();
            var m = FeedComing(4, events);
            StepUntil(m, () => m.Next.Locked);
            double contact = m.Next.Time;
            var point = m.Next.Point;
            StepUntil(m, () => m.Time >= contact - 0.3);
            Assert.AreEqual(SwingResult.Hit, m.Swing(m.Time, 6f, Across));
            Assert.AreEqual(1, m.LastHitter, "not hit yet: the ball is still coming");
            StepUntil(m, () => m.LastHitter == 0, 1f);
            Assert.AreEqual(contact, m.Time, 0.03, "hit when the ball arrived");
            var hit = events.FindLast(e => e.Kind == MatchEventKind.Hit);
            Assert.Less(Vector3.Distance(hit.Position, point), 0.05f, "from where the racket was waiting");
        }

        [Test]
        public void TooEarly_IsAnAirSwing_ASecondSwingCanStillHit()
        {
            var m = FeedComing(6);
            StepUntil(m, () => m.Next.Locked);
            double contact = m.Next.Time;
            Assert.AreEqual(SwingResult.Early, m.Swing(m.Time - 0.5, 6f, Across));
            StepUntil(m, () => m.Time >= contact);
            Assert.AreEqual(SwingResult.Hit, m.Swing(contact, 6f, Across));
        }

        [Test]
        public void NoSwing_LosesThePoint()
        {
            var m = new TennisMatch(TennisLevel.For(1), seed: 7, firstServer: 1);
            m.Begin();
            StepUntil(m, () => m.Phase == MatchPhase.PointOver, 20f);
            Assert.AreEqual(1, m.PointWinner, "the serve went by");
            Assert.AreEqual(PointReason.Winner, m.LastReason);
            Assert.AreEqual(1, m.Score.Points(1));
        }

        /// <summary>In, except a swing at the edge of the window against later players (<see cref="TennisLevel.WideFrom"/>).</summary>
        [Test]
        public void ThePlayersShots_GoIn()
        {
            int hits = 0;
            for (int seed = 1; seed <= 25; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(1 + seed % 6), seed: seed);
                var rng = new System.Random(seed);
                bool waiting = false;
                m.Events += e =>
                {
                    if (e.Kind == MatchEventKind.Hit && e.Team == 0) { waiting = Mathf.Abs(e.Shot.Timing) <= m.Level.WideFrom; hits++; }
                    else if (waiting && e.Kind == MatchEventKind.Hit) waiting = false;
                    else if (waiting && e.Kind == MatchEventKind.Bounce)
                    {
                        Assert.IsTrue(e.In, $"seed {seed}: the player's shot bounced out at {e.Position}");
                        waiting = false;
                    }
                    else if (waiting && e.Kind == MatchEventKind.Net) Assert.Fail($"seed {seed}: the player's shot went into the net");
                };
                Play(m, new Sim(r => Gauss(r) * 0.2, 3f + (float)rng.NextDouble() * 9f, rng) { NetRusher = seed % 3 == 0 }, 600f);
            }
            Assert.Greater(hits, 300);
        }

        [Test]
        public void PracticeFeeds_ArriveWhenAsked()
        {
            double asked = 0;
            var m = new TennisMatch(TennisLevel.For(1), seed: 9) { Practice = true };
            m.PracticeContact = earliest => asked = earliest + 0.6;
            m.Begin();
            StepUntil(m, () => m.Phase == MatchPhase.Toss);
            StepUntil(m, () => m.Time >= m.TossTop);
            m.Swing(m.TossTop, 6f, Down);
            StepUntil(m, () => m.LastHitter == 1 && m.Next.Active && m.Next.Team == 0);
            Assert.AreEqual(asked, m.Next.Time, 0.06, "the feed reaches the racket when practice asked");
        }

        // ---------------------------------------------------------------- whole matches

        /// <summary>
        /// A simulated player: swings with its timing at every ball of theirs (and every toss), the game hearing of it
        /// <see cref="Lag"/> later; and moves its body toward where the ball is going after <see cref="Reaction"/>, at
        /// <see cref="MoveSpeed"/> (court metres per second: a step of 0.4 m to the side in half a second is ~8),
        /// arriving <see cref="Miss"/> metres off (random), and back to the middle between balls.
        /// </summary>
        sealed class Sim
        {
            public readonly Func<System.Random, double?> Timing;
            /// <summary>Seconds off the toss's top (null = like <see cref="Timing"/>).</summary>
            public Func<System.Random, double?> ServeTiming;
            public readonly float Speed;
            public readonly System.Random Rng;
            public float Lag = 0.1f;
            public bool Moves = true;
            public float Reaction = 0.3f, MoveSpeed = 7f, Miss = 0.4f;
            /// <summary>Plays at the net after its own shots.</summary>
            public bool NetRusher;

            public Sim(Func<System.Random, double?> timing, float speed, System.Random rng)
            {
                Timing = timing;
                Speed = speed;
                Rng = rng;
            }
        }

        static float Gauss(System.Random r) =>
            (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - r.NextDouble())) * Math.Cos(2.0 * Math.PI * r.NextDouble()));

        static void Play(TennisMatch m, Sim sim, float maxSeconds)
        {
            var home = new Vector2(0f, -TennisMatch.HomeDepth);
            var body = home;
            m.HumanWanted = body;
            m.Begin();
            double plannedFor = -1, swingAt = -1, incomingAt = -1;
            int rally = -1;
            var miss = Vector2.zero;
            while (m.Phase != MatchPhase.Over && m.Time < maxSeconds)
            {
                // the body: toward the ball once it has seen where it goes, else back to the middle (or the net)
                if (sim.Moves)
                {
                    bool incoming = m.Phase == MatchPhase.Rally && m.LastHitter == 1 && m.Next.Active && m.Next.Team == 0;
                    if (incoming && m.RallyHits != rally)
                    {
                        rally = m.RallyHits;
                        incomingAt = m.Time;
                        miss = new Vector2(Gauss(sim.Rng), Gauss(sim.Rng) * 1.5f) * sim.Miss;
                    }
                    var goal = incoming ? (m.Time - incomingAt >= sim.Reaction ? m.Next.Spot + miss : body)
                        : sim.NetRusher && m.Phase == MatchPhase.Rally && m.LastHitter == 0 ? new Vector2(0f, -4f) : home;
                    body = Vector2.MoveTowards(body, goal, sim.MoveSpeed * Dt);
                    m.HumanWanted = body;
                }
                m.Step(Dt);
                double target = -1;
                if (m.Phase == MatchPhase.Toss && m.ServerTeam == 0) target = m.TossTop;
                else if (m.Phase == MatchPhase.Rally && m.Next.Active && m.Next.Team == 0 && !m.Next.Taken && m.Next.Locked) target = m.Next.Time;
                if (target > 0 && Math.Abs(target - plannedFor) > 1e-6)
                {
                    plannedFor = target;
                    var e = m.Phase == MatchPhase.Toss && sim.ServeTiming != null ? sim.ServeTiming(sim.Rng) : sim.Timing(sim.Rng);
                    swingAt = e.HasValue ? target + e.Value : -1;
                }
                if (swingAt > 0 && m.Time >= swingAt + sim.Lag)
                {
                    var dir = new Vector2(-1f, (float)(sim.Rng.NextDouble() * 1.6 - 0.8)).normalized;
                    m.Swing(swingAt, sim.Speed, dir);
                    swingAt = -1;
                }
            }
        }

        sealed class Profile
        {
            public string Name;
            public Func<System.Random, double?> Timing;
            public float Speed = 6f, Reaction = 0.3f, MoveSpeed = 7f, Miss = 0.4f;
            public bool Moves = true, ServesOnly;
        }

        static readonly Profile[] Profiles =
        {
            new Profile { Name = "perfect", Timing = r => Gauss(r) * 0.03, Speed = 9f, Reaction = 0.2f, MoveSpeed = 9f, Miss = 0.2f },
            new Profile { Name = "good", Timing = r => -0.05 + Gauss(r) * 0.12, Speed = 6f, Reaction = 0.35f, MoveSpeed = 6f, Miss = 0.6f },
            new Profile { Name = "child", Timing = r => r.NextDouble() < 0.15 ? (double?)null : -0.1 + Gauss(r) * 0.2, Speed = 4.5f,
                          Reaction = 0.5f, MoveSpeed = 4f, Miss = 1f },
            new Profile { Name = "struggling", Timing = r => r.NextDouble() < 0.3 ? (double?)null : Gauss(r) * 0.35, Speed = 3.5f,
                          Reaction = 0.7f, MoveSpeed = 3f, Miss = 1.5f },
            new Profile { Name = "never moves", Timing = r => -0.05 + Gauss(r) * 0.12, Speed = 6f, Moves = false },
            // serves (else the game waits for the serve forever, like Wii), then never swings or moves
            new Profile { Name = "standing still", Timing = r => null, Speed = 4f, Moves = false, ServesOnly = true },
        };

        /// <summary>Wins out of 10 matches (best of 3 games) per player and ladder level, and how long they take.</summary>
        static (int wins, float minutes, int pointsWon, int pointsLost)[,] Table(int[] levels, int matches = 10)
        {
            var table = new (int, float, int, int)[Profiles.Length, levels.Length];
            for (int p = 0; p < Profiles.Length; p++)
            for (int l = 0; l < levels.Length; l++)
            {
                int wins = 0, won = 0, lost = 0;
                double time = 0;
                var pr = Profiles[p];
                for (int s = 0; s < matches; s++)
                {
                    var m = new TennisMatch(TennisLevel.For(levels[l]), seed: 1000 * p + 100 * l + s);
                    var sim = new Sim(pr.Timing, pr.Speed, new System.Random(s * 31 + p))
                    {
                        Moves = pr.Moves, Reaction = pr.Reaction, MoveSpeed = pr.MoveSpeed, Miss = pr.Miss,
                        ServeTiming = pr.ServesOnly ? r => 0.0 : (Func<System.Random, double?>)null,
                    };
                    Play(m, sim, 1800f);
                    Assert.AreEqual(MatchPhase.Over, m.Phase, $"{pr.Name} at level {levels[l]}: the match ended");
                    if (m.Score.Winner == 0) wins++;
                    won += m.PointsWon;
                    lost += m.PointsLost;
                    time += m.Time;
                }
                table[p, l] = (wins, (float)(time / matches / 60.0), won, lost);
            }
            return table;
        }

        [Test]
        public void Balance_OnTheLadder()
        {
            int[] levels = { 1, 3, 6, 9 };
            var t = Table(levels);
            var log = new System.Text.StringBuilder("singles, wins of 10 (minutes per match, points won-lost):\n");
            for (int p = 0; p < Profiles.Length; p++)
            {
                log.Append($"  {Profiles[p].Name,-15}");
                for (int l = 0; l < levels.Length; l++)
                    log.Append($"  L{levels[l]}: {t[p, l].wins,2} ({t[p, l].minutes:0.0} min, {t[p, l].pointsWon}-{t[p, l].pointsLost})");
                log.Append('\n');
            }
            Debug.Log(log.ToString());
            int Wins(string name, int level) => t[Array.FindIndex(Profiles, x => x.Name == name), Array.IndexOf(levels, level)].wins;
            Assert.AreEqual(10, Wins("perfect", 1));
            Assert.GreaterOrEqual(Wins("perfect", 6), 8, "a perfect player climbs the ladder");
            Assert.GreaterOrEqual(Wins("good", 1), 9);
            Assert.GreaterOrEqual(Wins("child", 1), 6, "a child usually wins the first match");
            Assert.LessOrEqual(Wins("child", 6), 5, "and finds the later ones hard");
            Assert.LessOrEqual(Wins("struggling", 6), 3, "the later players are a real challenge");
            Assert.Less(Wins("never moves", 6), Wins("good", 6), "moving matters once they aim into the corners");
            Assert.AreEqual(0, Wins("standing still", 1), "doing nothing never wins");
            for (int l = 0; l < levels.Length; l++)
                Assert.Less(t[Array.FindIndex(Profiles, x => x.Name == "good"), l].minutes, 12f, "a match is a few minutes");
        }
    }
}
