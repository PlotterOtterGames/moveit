using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Tennis.Tests
{
    /// <summary>
    /// The rules with simulated players, in singles (one player against the computer, since 2026-11-07; doubles, two
    /// players together, are in TennisTwoPlayerTests): the toys run to the ball by themselves, a sideways swing hits when
    /// the ball is near enough, whichever way the hand goes and on either side, a swing up drives it (topspin; a gentle
    /// scoop straight up lobs) and a strong one down misses at once, the computer's first five returns of every point are
    /// sure, a backswing toward the ball's side holds it for the stroke after it (or hits it), early pulls it
    /// across and late sends it down the line, the hand's sideways speed is the power (a fast one on time the perfect
    /// shot) and its tilt the spin, a mistimed shot is weaker but in, the rally starts slow and speeds up every hit, the
    /// camera's lag is taken out, and whole matches at every difficulty end with believable results for players who swing
    /// on time, a child, a struggling player and one who stands still.
    /// </summary>
    public class TennisMatchTests
    {
        const float Dt = 1f / 60f;

        static void StepUntil(TennisMatch m, Func<bool> done, float seconds = 30f, Camera cam = null)
        {
            double end = m.Time + seconds;
            while (!done())
            {
                Assert.Less(m.Time, end, "timed out waiting");
                m.Step(Dt);
                cam?.Drive(m);
            }
        }

        /// <summary>
        /// A camera watching the player: a sample every 1 / <see cref="Rate"/> s that reaches the match <see cref="Lag"/>
        /// later (the rules learn how far it has seen: <see cref="TennisMatch.SawUntil"/>), and the swings planned with
        /// <see cref="SwingAt"/>, handed over with the moment they were seen, also <see cref="Lag"/> late.
        /// </summary>
        sealed class Camera
        {
            public float Lag = 0.1f, Rate = 30f;
            public readonly List<SwingResult> Results = new List<SwingResult>();
            readonly Queue<double> _samples = new Queue<double>();
            readonly List<(double at, float speed, Vector2 direction, bool racket, bool gentle)> _swings =
                new List<(double, float, Vector2, bool, bool)>();
            double _nextSample = double.NegativeInfinity;

            /// <summary>A swing seen at match time <paramref name="at"/>: <paramref name="speed"/> torso lengths per second,
            /// <paramref name="vertical"/> how much of it goes up (+) or down (-), across the body (<paramref name="across"/>
            /// = which way on the player's screen: + = their right), with the racket hand or the other; a
            /// <paramref name="gentle"/> one is a small flick (<see cref="SwingEvent.Gentle"/>).</summary>
            public void SwingAt(double at, float speed = 6f, float vertical = 0f, float across = -1f, bool racket = true, bool gentle = false)
            {
                vertical = Mathf.Clamp(vertical, -1f, 1f);
                _swings.Add((at, speed, new Vector2(Mathf.Sign(across) * Mathf.Sqrt(1f - vertical * vertical), vertical), racket, gentle));
            }

            public void Drive(TennisMatch m)
            {
                if (m.Time >= _nextSample)
                {
                    _nextSample = Math.Max(m.Time, _nextSample + 1.0 / Rate);
                    _samples.Enqueue(m.Time);
                }
                while (_samples.Count > 0 && _samples.Peek() + Lag <= m.Time) m.SawUntil(_samples.Dequeue());
                for (int i = 0; i < _swings.Count; i++)
                {
                    if (_swings[i].at + Lag > m.Time) continue;
                    var s = _swings[i];
                    _swings.RemoveAt(i--);
                    Results.Add(m.Swing(s.at, s.speed, s.direction, s.racket, s.gentle));
                }
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
            Assert.AreEqual(SwingResult.Serve, m.Swing(m.TossTop, 6f, Vector2.down));
            StepUntil(m, () => m.Phase == MatchPhase.Rally && m.LastHitter == 1 && m.Next.Active && m.Next.Team == 0);
            return m;
        }

        /// <summary>Which way a stroke at the coming ball goes on the player's screen (team 0: + = their right): across,
        /// away from the ball's side (a forehand from the racket's side, a backhand toward it).</summary>
        static float Forward(TennisMatch m) => m.Next.Side > 0f ? -1f : 1f;

        /// <summary>Swings at the coming ball (<paramref name="offset"/> seconds off the moment it arrives, as the camera
        /// sees it) and returns the player's shot, or null when it wasn't hit. <paramref name="across"/> 0 = the stroke's
        /// way (<see cref="Forward"/>).</summary>
        static Shot? SwingAt(TennisMatch m, double offset = 0, float lag = 0.1f, float speed = 6f, float vertical = 0f, float across = 0f,
                             Camera cam = null, bool racket = true, bool gentle = false)
        {
            Shot? hit = null;
            m.Events += e => { if (e.Kind == MatchEventKind.Hit && e.Team == 0) hit = e.Shot; };
            cam ??= new Camera { Lag = lag };
            StepUntil(m, () => m.Next.Active, cam: cam);
            cam.SwingAt(m.Next.Time + offset, speed, vertical, across != 0f ? across : Forward(m), racket, gentle);
            StepUntil(m, () => hit.HasValue || m.Phase != MatchPhase.Rally || m.LastHitter != 1, 6f, cam);
            return hit;
        }

        // ---------------------------------------------------------------- moving (by themselves, since 2026-11-07)

        /// <summary>
        /// The player's toys run to the ball by themselves (user, 2026-11-07: "the characters move on their own and go near
        /// where the ball will end up"): as the computer's serve leaves, the receiving back player waits its reaction, then
        /// runs to the spot beside the ball and is there before it; its partner stays at the net. Nobody moves the body.
        /// </summary>
        [Test]
        public void ThePlayersToys_RunToTheBall_ByThemselves()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(TennisDifficulty.Medium, 1, seed), seed: seed, firstServer: 1);
                m.Begin();
                StepUntil(m, () => m.Phase == MatchPhase.Rally && m.Next.Active && m.Next.Team == 0);
                Assert.AreSame(m.Human, m.Next.Hitter, "the back player returns the serve (a serve is never volleyed)");
                Assert.IsTrue(m.Next.Reachable, $"seed {seed}: a serve is reached");
                var start = m.Human.Position;
                double setOff = m.Human.RunAt;
                Assert.Greater(setOff, m.Time - Dt, "it sets off after its reaction");
                StepUntil(m, () => m.Time >= setOff - Dt);
                Assert.Less(Vector2.Distance(m.Human.Position, start), 0.05f, $"seed {seed}: it reacts before it runs");
                double contact = m.Next.Time;
                StepUntil(m, () => m.Time >= contact - Dt);
                Assert.Less(Vector2.Distance(m.Human.Position, m.Next.Spot), m.ReachTolerance, $"seed {seed}: there before the ball");
                Assert.IsFalse(m.HumanNet.Active, "singles: nobody at the net");
            }
        }

        /// <summary>The back player runs in for a short ball and back for a deep one by itself, and every ball the rules
        /// call reachable it reaches in time (within <see cref="TennisMatch.ReachTolerance"/> of the spot as the ball
        /// arrives).</summary>
        [Test]
        public void ShortOrDeep_TheBackPlayerGetsThereInTime()
        {
            int balls = 0, reachable = 0, there = 0;
            float nearest = 99f, furthest = 0f;
            for (int seed = 1; seed <= 12; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(5), seed: seed, firstServer: 1);   // short balls, corners
                m.Begin();
                var cam = new Camera();
                double planned = -1, measured = -1;
                while (m.Time < 120 && m.Phase != MatchPhase.Over)
                {
                    m.Step(Dt);
                    cam.Drive(m);
                    if (m.Phase == MatchPhase.Toss && m.ServerTeam == 0 && m.Time >= m.TossTop) m.Swing(m.Time, 6f, Vector2.down);
                    if (m.Phase != MatchPhase.Rally || !m.Next.Active || m.Next.Team != 0 || m.Next.Taken) continue;
                    if (Math.Abs(m.Next.Time - planned) > 1e-6)
                    {
                        planned = m.Next.Time;
                        cam.SwingAt(m.Next.Time);
                    }
                    if (m.Next.Hitter == m.Human && m.Time >= m.Next.Time - Dt && Math.Abs(m.Next.Time - measured) > 1e-6)
                    {
                        measured = m.Next.Time;
                        balls++;
                        if (!m.Next.Reachable) continue;
                        reachable++;
                        if (Vector2.Distance(m.Human.Position, m.Next.Spot) < m.ReachTolerance) there++;
                        nearest = Mathf.Min(nearest, -m.Next.Spot.y);
                        furthest = Mathf.Max(furthest, -m.Next.Spot.y);
                    }
                }
            }
            Debug.Log($"{balls} balls for the back player, {reachable} reachable, {there} reached in time; met from {nearest:0.0} to {furthest:0.0} m behind the net");
            Assert.Greater(balls, 40);
            Assert.AreEqual(reachable, there, "every ball called reachable was reached in time");
            Assert.Greater(reachable, balls * 0.8f, "most balls are reachable at this strength");
            Assert.Less(nearest, 10f, "short balls: the back player ran in");
            Assert.Greater(furthest, 12.4f, "deep balls: back behind the baseline");
        }

        /// <summary>
        /// Depending on the difficulty not every ball can be reached (user, 2026-11-07): in whole matches of a player who
        /// swings well at every ball, Easy's toys reach every ball, Hard's miss some of the strongest opponent's quick balls
        /// into the far corner (more as a long rally gets quicker, and its attacks on soft balls). A ball out of reach goes
        /// by whatever the swing: too far (the swing reaches for nothing, the toy dives), the point lost.
        /// </summary>
        [Test]
        public void TheDifficulty_DecidesWhetherEveryBallCanBeReached()
        {
            var away = new Dictionary<TennisDifficulty, (int balls, int lost)>();
            int tooFarChecked = 0;
            foreach (TennisDifficulty d in Enum.GetValues(typeof(TennisDifficulty)))
            {
                int balls = 0, lost = 0;
                for (int seed = 1; seed <= 10; seed++)
                {
                    var m = new TennisMatch(TennisLevel.For(d, 2, seed), seed: seed);
                    double counted = -1;
                    // a swing at a ball out of reach: too far, and the toy dives for it
                    m.Events += e =>
                    {
                        if (e.Kind != MatchEventKind.TooFar) return;
                        Assert.Greater(m.Players[e.Player].DiveAt, 0.0, "it dives for the ball");
                        tooFarChecked++;
                    };
                    Play(m, new Sim(r => Gauss(r) * 0.04, new System.Random(seed)), 900f, x =>
                    {
                        var n = x.Next;
                        if (!n.Active || n.Team != 0 || Math.Abs(n.Time - counted) < 1e-6 || x.LastHitter != 1) return;
                        counted = n.Time;
                        balls++;
                        if (!n.Reachable) lost++;
                    });
                }
                away[d] = (balls, lost);
            }
            Debug.Log("balls out of reach: " + string.Join(", ", Enum.GetValues(typeof(TennisDifficulty)).Cast<TennisDifficulty>()
                          .Select(d => $"{d} {away[d].lost} of {away[d].balls} ({100f * away[d].lost / Math.Max(1, away[d].balls):0.0} %)")));
            Assert.AreEqual(0, away[TennisDifficulty.Easy].lost, "Easy: every ball is reached");
            float hard = away[TennisDifficulty.Hard].lost / (float)away[TennisDifficulty.Hard].balls;
            float medium = away[TennisDifficulty.Medium].lost / (float)away[TennisDifficulty.Medium].balls;
            Assert.Greater(hard, medium, "the stronger the opponent, the more balls get away");
            Assert.Greater(hard, 0.05f, "Hard: some balls can't be reached");
            Assert.Less(hard, 0.3f, "but most can");
            Assert.Less(medium, 0.06f, "Medium: nearly every ball");
            Assert.Greater(tooFarChecked, 0, "a swing at a ball out of reach: too far");
        }

        // ---------------------------------------------------------------- swinging at the ball

        /// <summary>A swing while the ball is near hits it, whichever way the hand goes, forehand or backhand.</summary>
        [Test]
        public void ASwingWhenTheBallIsNear_HitsIt_WhicheverWayAndSide()
        {
            int forehands = 0, backhands = 0;
            for (int seed = 1; seed <= 16; seed++)
            {
                var shot = SwingAt(FeedComing(seed), across: seed % 2 == 0 ? 1f : -1f);
                Assert.IsTrue(shot.HasValue, $"seed {seed}: hit");
                Assert.Less(Mathf.Abs(shot.Value.Timing), 0.05f, $"seed {seed}: on time ({shot.Value.Timing:0.00})");
                if (shot.Value.Stroke == Stroke.Forehand) forehands++;
                else backhands++;
            }
            Assert.Greater(forehands, 0, "balls on the racket's side");
            Assert.Greater(backhands, 0, "balls on the other side: the same swing hits");
        }

        [Test]
        public void NoSwing_TheBallGoesBy()
        {
            var events = new List<MatchEvent>();
            var m = FeedComing(2, events);
            StepUntil(m, () => m.Phase != MatchPhase.Rally, 8f, new Camera());
            Assert.AreEqual(1, m.PointWinner, "the ball went by");
            Assert.AreEqual(0, m.HumanHits);
            Assert.IsTrue(events.Exists(e => e.Kind == MatchEventKind.Missed));
        }

        /// <summary>A swing well before the window or well past it misses ("too early" / "too late"); a little early or
        /// late in it still meets the ball. The window is where the ball is (metres), so a slow ball early in a rally
        /// gives more time than a quick one late in it.</summary>
        [Test]
        public void FarTooEarly_OrTooLate_Misses_ALittleOff_StillHits()
        {
            var cases = new (string name, Func<double, double, double> offset, bool hits)[]
            {
                ("well before the window", (early, late) => -(early + 0.25), false),
                ("well past it", (early, late) => late + 0.12, false),
                ("early in it", (early, late) => -early * 0.8, true),
                ("late in it", (early, late) => late * 0.7, true),
            };
            double shortest = 99, longest = 0;
            for (int seed = 1; seed <= 6; seed++)
            foreach (var (name, offset, hits) in cases)
            {
                var m = FeedComing(seed);
                Shot? hit = null;
                m.Events += e => { if (e.Kind == MatchEventKind.Hit && e.Team == 0) hit = e.Shot; };
                var cam = new Camera();
                StepUntil(m, () => m.Next.Active, cam: cam);
                m.Window(m.Next, out double early, out double late);
                shortest = Math.Min(shortest, early + late);
                longest = Math.Max(longest, early + late);
                cam.SwingAt(m.Next.Time + offset(early, late), 6f, 0f, Forward(m));
                StepUntil(m, () => hit.HasValue || m.Phase != MatchPhase.Rally || m.LastHitter != 1, 6f, cam);
                Assert.AreEqual(hits, hit.HasValue, $"seed {seed}: {name} (window -{early:0.00} s .. +{late:0.00} s)");
                if (name == "well before the window") Assert.AreEqual(SwingResult.Early, cam.Results[0]);
                if (name == "well past it") Assert.IsTrue(cam.Results[0] is SwingResult.Late or SwingResult.NoBall, $"seed {seed}: late (or the point was over)");
            }
            Debug.Log($"windows from {shortest:0.00} to {longest:0.00} s");
            Assert.GreaterOrEqual(shortest, 0.47, "at least MinEarly + MinLate");
        }

        /// <summary>A swing too early doesn't use the ball up: a second swing on time still hits it.</summary>
        [Test]
        public void AnAirSwing_ThenOneOnTime_Hits()
        {
            var m = FeedComing(4);
            var cam = new Camera();
            StepUntil(m, () => m.Next.Active, cam: cam);
            cam.SwingAt(m.Next.Time - 0.8);
            cam.SwingAt(m.Next.Time);
            StepUntil(m, () => m.LastHitter == 0 || m.Phase != MatchPhase.Rally, 6f, cam);
            Assert.AreEqual(1, m.HumanHits);
            Assert.AreEqual(SwingResult.Early, cam.Results[0]);
            Assert.AreEqual(SwingResult.Hit, cam.Results[1]);
        }

        [Test]
        public void WhenTheSwingComes_AimsTheBall()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var early = SwingAt(FeedComing(seed), offset: -0.2).Value;
                var onTime = SwingAt(FeedComing(seed), offset: 0.0).Value;
                var late = SwingAt(FeedComing(seed), offset: 0.12).Value;
                var m = FeedComing(seed);
                float side = m.Next.Side;
                // early pulls it across (away from the ball's side), late sends it down the line
                Assert.Less((early.Target.x - onTime.Target.x) * side, -0.5f, $"seed {seed}: early goes across");
                Assert.Greater((late.Target.x - onTime.Target.x) * side, 0.5f, $"seed {seed}: late goes down the line");
                Assert.Less(early.Timing, -0.3f);
                Assert.Greater(late.Timing, 0.3f);
                Assert.IsTrue(onTime.Perfect, "on time: perfect");
                Assert.IsFalse(early.Perfect);
            }
        }

        [Test]
        public void TheHandsSpeed_IsThePower_UpIsTopspin_DownIsASlice()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var fast = SwingAt(FeedComing(seed), offset: 0.1, speed: 10f).Value;
                var slow = SwingAt(FeedComing(seed), offset: 0.1, speed: 3.5f).Value;
                Assert.Greater(fast.Speed, slow.Speed + 2f, $"seed {seed}: a fast swing hits harder");
                Assert.AreEqual(ShotKind.Flat, fast.Kind);
                var match = FeedComing(seed);
                float kept = Mathf.Lerp(1f, match.MistimedPower, Mathf.InverseLerp(match.MistimedFrom, 1f, Mathf.Abs(fast.Timing)));
                Assert.AreEqual(fast.Pace * SpeedOf(ShotKind.Flat, kept), fast.Speed, 0.01f, $"seed {seed}: a fast sideways swing: full power (less a little for the timing)");
                // the best shot: fast and right on time (user, 2026-11-07: "horizontal swings depending on the speed should
                // provide the best shot"); a slow one on time is no perfect shot
                var best = SwingAt(FeedComing(seed), offset: 0.0, speed: 9f).Value;
                var soft = SwingAt(FeedComing(seed), offset: 0.0, speed: 3.5f).Value;
                Assert.IsTrue(best.Perfect, $"seed {seed}: fast and on time: perfect");
                Assert.IsFalse(soft.Perfect, $"seed {seed}: slow: not perfect");
                Assert.Greater(best.Speed, soft.Speed * 1.3f, $"seed {seed}: the best shot is much quicker");
                // the power is the sideways speed: the same hand speed tilted down hits softer than a full slice
                var tilted = SwingAt(FeedComing(seed), offset: 0.15, speed: 10f, vertical: -0.6f).Value;
                Assert.AreEqual(ShotKind.Slice, tilted.Kind);
                Assert.Less(tilted.Speed, tilted.Pace * SpeedOf(ShotKind.Slice, 1f) - 0.5f, $"seed {seed}: a tilted swing's power is its sideways part");
                Assert.AreEqual(ShotKind.Topspin, SwingAt(FeedComing(seed), vertical: 0.45f).Value.Kind, "tilted up a little");
                // up is a drive, not a lob (2026-11-07 evening: "too many lob shots"): only a gentle scoop straight up lobs
                Assert.AreEqual(ShotKind.Topspin, SwingAt(FeedComing(seed), vertical: 0.7f).Value.Kind, "tilted up a lot: topspin");
                Assert.AreEqual(ShotKind.Topspin, SwingAt(FeedComing(seed), vertical: 0.98f).Value.Kind, "straight up: topspin too");
                Assert.AreEqual(ShotKind.Topspin, SwingAt(FeedComing(seed), speed: 2.5f, vertical: 0.8f, gentle: true).Value.Kind,
                                "a gentle swing up, not straight up: topspin");
                Assert.AreEqual(ShotKind.Lob, SwingAt(FeedComing(seed), speed: 2.5f, vertical: 0.97f, gentle: true).Value.Kind,
                                "a gentle scoop straight up: a lob");
                Assert.AreEqual(ShotKind.Slice, SwingAt(FeedComing(seed), vertical: -0.6f).Value.Kind, "tilted down");
            }
        }

        /// <summary>A shot's own speed at full power (TennisMatch.SpeedFor), to compare powers across kinds.</summary>
        static float SpeedOf(ShotKind kind, float power) => kind switch
        {
            ShotKind.Slice => 10.5f + 5f * power,
            _ => 13f + 8f * power,
        };

        /// <summary>
        /// A strong swing down at a ball near enough misses it at once (user, 2026-11-06: "vertical swings should miss
        /// immediately"): no hit, the miss is told as the swing reaches the game (before the ball has even arrived), the
        /// ball goes by and the point is lost. A swing up hits it (2026-11-07: "the upwards moves should count as well";
        /// evening: "too many lob shots"): a low-to-high drive with topspin that lands in. The free hand going up or down is
        /// nothing: the stroke after it still hits.
        /// </summary>
        [Test]
        public void ASwingDown_MissesAtOnce_UpHitsIt_TheFreeHandsIsNothing()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                {
                    var m = FeedComing(seed);
                    double toldAt = -1, ballAt = -1;
                    m.Events += e => { if (e.Kind == MatchEventKind.Vertical) { toldAt = m.Time; ballAt = m.Next.Time; } };
                    var cam = new Camera();
                    Assert.IsFalse(SwingAt(m, offset: -0.25, vertical: -0.9f, cam: cam).HasValue, $"seed {seed}: down misses");
                    Assert.AreEqual(SwingResult.Vertical, cam.Results[0], $"seed {seed}");
                    Assert.Greater(toldAt, 0, "the miss is told");
                    Assert.Less(toldAt, ballAt, "at once: before the ball got there");
                    Assert.AreEqual(1, m.Stats[0].Vertical);
                    Assert.AreEqual(0, m.HumanHits);
                    Assert.AreEqual(MatchPhase.PointOver, m.Phase);
                    Assert.AreEqual(1, m.PointWinner, $"seed {seed}: the ball went by");
                }
                foreach (double offset in new[] { -0.2, 0.0, 0.1 })
                {
                    var up = FeedComing(seed);
                    var upCam = new Camera();
                    bool? landedIn = null;
                    up.Events += e => { if (e.Kind == MatchEventKind.Bounce && up.LastHitter == 0 && landedIn == null) landedIn = e.In; };
                    var drive = SwingAt(up, offset: offset, speed: 7f, vertical: 0.95f, cam: upCam);
                    Assert.IsTrue(drive.HasValue, $"seed {seed}, {offset:+0.0;-0.0} s: a swing up hits");
                    Assert.AreEqual(SwingResult.Hit, upCam.Results[0]);
                    Assert.AreEqual(ShotKind.Topspin, drive.Value.Kind, $"seed {seed}: a topspin drive"); 
                    Assert.AreEqual(0, up.Stats[0].Vertical);
                    StepUntil(up, () => landedIn.HasValue, 6f, upCam);
                    Assert.AreEqual(true, landedIn, $"seed {seed}: it lands in");
                }
                var m2 = FeedComing(seed);
                var cam2 = new Camera();
                StepUntil(m2, () => m2.Next.Active, cam: cam2);
                cam2.SwingAt(m2.Next.Time - 0.15, 6f, 0.95f, Forward(m2), racket: false);
                cam2.SwingAt(m2.Next.Time, 6f, 0f, Forward(m2));
                StepUntil(m2, () => m2.LastHitter == 0 || m2.Phase != MatchPhase.Rally, 6f, cam2);
                Assert.AreEqual(SwingResult.NoBall, cam2.Results[0], $"seed {seed}: the free hand up is nothing");
                Assert.AreEqual(SwingResult.Hit, cam2.Results[1], $"seed {seed}: the stroke after it hits");
            }
        }

        /// <summary>
        /// Every hit's swing tells the view where the ball is when the racket meets it, exactly where the ball then leaves
        /// from (the toy reaches into it there, like Wii's Miis: 2026-11-06): the player's early, on-time and late swings,
        /// the serve, the computer's returns. A swing at a ball the toy stands too far from reaches for nothing: that ball
        /// goes by.
        /// </summary>
        [Test]
        public void EveryHit_ReachesIntoTheBall_WhereItLeavesFrom()
        {
            int checkedHits = 0;
            foreach (double offset in new[] { -0.25, 0.0, 0.12 })
            for (int seed = 1; seed <= 6; seed++)
            {
                var m = FeedComing(seed);
                var hits = new List<(CourtPlayer p, Vector3 from)>();
                m.Events += e => { if (e.Kind == MatchEventKind.Hit) hits.Add((m.Players[e.Player], e.Position)); };
                var hitter = m.Next.Hitter;
                Assert.IsTrue(SwingAt(m, offset: offset).HasValue, $"seed {seed}, {offset:+0.00;-0.00} s: hit");
                var (p, from) = hits[0];
                Assert.AreSame(hitter, p);
                Assert.IsTrue(p.SwingReaches, $"seed {seed}: the swing reaches");
                Assert.Less(Vector3.Distance(p.SwingPoint, from), 0.15f, $"seed {seed}, {offset:+0.00;-0.00} s: the racket meets it where it leaves");
                checkedHits++;
            }
            // the serve and the computer's returns (a real match: the computer returns some)
            var match = new TennisMatch(TennisLevel.For(1), seed: 3);
            int serves = 0, returns = 0;
            match.Events += e =>
            {
                if (e.Kind != MatchEventKind.Hit) return;
                var p = match.Players[e.Player];
                Assert.IsTrue(p.SwingReaches, $"{e.Shot.Kind} by player {e.Player}: the swing reaches");
                // the computer's ball leaves from the physics step it was met in: at most one step further on
                float step = e.Team == 1 ? match.Ball.Velocity.magnitude * BallPhysics.Step + 0.05f : 0.15f;
                Assert.Less(Vector3.Distance(p.SwingPoint, e.Position), Mathf.Max(0.15f, step), $"{e.Shot.Kind} by player {e.Player}");
                if (e.Shot.Kind is ShotKind.Serve or ShotKind.PowerServe) serves++;
                else if (e.Team == 1) returns++;
            };
            match.Begin();
            var cam = new Camera();
            for (int i = 0; i < 60 * 60 && match.Phase != MatchPhase.Over; i++)
            {
                match.Step(Dt);
                cam.Drive(match);
                if (match.Phase == MatchPhase.Toss && match.Server.Controller == 0 && match.Time >= match.TossTop && match.Server.Serving)
                    match.Swing(match.TossTop, 6f, Vector2.down);
                var n = match.Next;
                if (match.Phase == MatchPhase.Rally && n.Active && n.Team == 0 && !n.Taken && !n.SwingShown)
                {
                    n.SwingShown = true;   // (the test's own mark: one swing per ball)
                    cam.SwingAt(n.Time, 6f, 0f, Forward(match));
                }
            }
            Assert.Greater(serves, 0, "serves checked");
            Assert.Greater(returns, 0, "the computer's returns checked");
            Assert.Greater(checkedHits, 0);

            // too far (a toy far too slow to get to the serve): it swings, but reaches for nothing; the ball goes by
            int tooFar = 0;
            for (int seed = 1; seed <= 8; seed++)
            {
                var far = new TennisMatch(TennisLevel.For(1), seed: seed, firstServer: 1) { SureReturns = 0 };   // (a sure ball is always got to)
                far.Begin();
                far.Human.MaxSpeed = 0.2f;
                var farCam = new Camera();
                StepUntil(far, () => far.Phase == MatchPhase.Rally && far.Next.Active && far.Next.Team == 0, cam: farCam);
                if (far.Next.Reachable) continue;   // (the serve came straight to it)
                var swinger = far.Next.Hitter;
                int before = swinger.Swings;
                farCam.SwingAt(far.Next.Time, 6f, 0f, Forward(far));
                StepUntil(far, () => farCam.Results.Count > 0, 3f, farCam);
                Assert.AreEqual(SwingResult.TooFar, farCam.Results[0], $"seed {seed}");
                Assert.Greater(swinger.Swings, before, $"seed {seed}: it swings");
                Assert.IsFalse(swinger.SwingReaches, $"seed {seed}: no reach into a ball that goes by");
                tooFar++;
            }
            Assert.Greater(tooFar, 2, "serves out of reach were swung at");
        }

        /// <summary>
        /// A small flick of the hand (a gentle swing, like a Wii remote's: the user, 2026-11-06) hits a ball in reach like
        /// any swing, softly (a flick up lobs it). Anywhere else it is just the hand moving: with the ball still far no toy
        /// swings and no "too early" is counted, and a gentle move down at a ball in reach doesn't miss it at once (only a
        /// strong swing down does): the flick across after it still hits.
        /// </summary>
        [Test]
        public void AGentleFlick_HitsABallInReach_ElsewhereItIsJustTheHandMoving()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var m = FeedComing(seed);
                var cam = new Camera();
                StepUntil(m, () => m.Next.Active, cam: cam);
                cam.SwingAt(m.Next.Time, 2f, 0.1f, Forward(m), gentle: true);
                StepUntil(m, () => m.LastHitter == 0 || m.Phase != MatchPhase.Rally, 6f, cam);
                Assert.AreEqual(SwingResult.Hit, cam.Results[0], $"seed {seed}: the flick at the ball hits");
                Assert.AreEqual(ShotKind.Flat, m.LastShot.Kind, $"seed {seed}: a soft flat shot");

                var far = FeedComing(seed);
                int swings = far.Human.Swings + far.HumanNet.Swings, whiffs = far.Stats[0].Whiffs;
                Assert.AreEqual(SwingResult.NoBall, far.Swing(far.Time, 2f, new Vector2(Forward(far), 0f), gentle: true), $"seed {seed}");
                Assert.AreEqual(swings, far.Human.Swings + far.HumanNet.Swings, $"seed {seed}: the toys don't twitch with the hand");
                Assert.AreEqual(whiffs, far.Stats[0].Whiffs, $"seed {seed}: no 'too early'");

                var down = FeedComing(seed);
                var cam2 = new Camera();
                StepUntil(down, () => down.Next.Active, cam: cam2);
                cam2.SwingAt(down.Next.Time - 0.1, 2f, -0.95f, Forward(down), gentle: true);
                cam2.SwingAt(down.Next.Time + 0.02, 2f, 0f, Forward(down), gentle: true);
                StepUntil(down, () => down.LastHitter == 0 || down.Phase != MatchPhase.Rally, 6f, cam2);
                Assert.AreEqual(SwingResult.NoBall, cam2.Results[0], $"seed {seed}: a gentle move down is nothing");
                Assert.AreEqual(SwingResult.Hit, cam2.Results[1], $"seed {seed}: the flick after it hits");
                Assert.AreEqual(0, down.Stats[0].Vertical);

                var up = FeedComing(seed);
                var cam3 = new Camera();
                StepUntil(up, () => up.Next.Active, cam: cam3);
                cam3.SwingAt(up.Next.Time, 2f, 0.95f, Forward(up), gentle: true);
                StepUntil(up, () => up.LastHitter == 0 || up.Phase != MatchPhase.Rally, 6f, cam3);
                Assert.AreEqual(SwingResult.Hit, cam3.Results[0], $"seed {seed}: a gentle flick up at the ball hits");
                Assert.AreEqual(ShotKind.Lob, up.LastShot.Kind, $"seed {seed}: a lob");
            }
        }

        /// <summary>
        /// A swing toward the ball's side while it is still coming (the racket going back for a backhand, or the player
        /// swinging toward where they see the ball: 2026-11-07, user: "the shots with the right hand left to right when the
        /// ball comes to the left side are not registered that well") holds the ball if it's in reach: the stroke after it
        /// hits instead, on its own timing, and with no stroke after it the held swing hits as the ball arrives, in. Out of
        /// reach it only gets the racket ready: no hit, no "too early" (the user's real swings, 2026-11-06: a backswing ~0.3 s
        /// before each stroke).
        /// </summary>
        [Test]
        public void ABackswing_HoldsTheBall_TheStrokeAfterItHitsInstead()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var events = new List<MatchEvent>();
                var m = FeedComing(seed, events);
                var cam = new Camera();
                StepUntil(m, () => m.Next.Active, cam: cam);
                m.Window(m.Next, out double early, out _);
                double at = m.Next.Time;
                float forward = Forward(m);
                cam.SwingAt(at - early * 0.8, 7f, -0.1f, -forward);   // back toward the ball's side, the ball in reach
                cam.SwingAt(at + 0.02, 6f, 0.1f, forward);            // and through it
                StepUntil(m, () => m.LastHitter == 0 || m.Phase != MatchPhase.Rally, 6f, cam);
                Assert.AreEqual(SwingResult.Held, cam.Results[0], $"seed {seed}");
                Assert.AreEqual(SwingResult.Hit, cam.Results[1], $"seed {seed}: the stroke hits instead");
                Assert.AreEqual(1, m.HumanHits);
                Assert.IsFalse(events.Exists(e => e.Kind == MatchEventKind.Whiff), $"seed {seed}: no 'too early'");
                Assert.Less(Mathf.Abs(m.LastShot.Timing), 0.2f, $"seed {seed}: the stroke's timing");

                // toward the ball's side and nothing after it: it hits as the ball arrives, and goes in
                var alone = FeedComing(seed);
                var cam2 = new Camera();
                Shot? shot = null;
                bool? landedIn = null;
                alone.Events += e =>
                {
                    if (e.Kind == MatchEventKind.Hit && e.Team == 0) shot = e.Shot;
                    else if (e.Kind == MatchEventKind.Bounce && shot.HasValue && landedIn == null) landedIn = e.In;
                };
                StepUntil(alone, () => alone.Next.Active, cam: cam2);
                alone.Window(alone.Next, out early, out _);
                int swingsBefore = alone.Human.Swings;
                cam2.SwingAt(alone.Next.Time - early * 0.7, 8f, 0f, -Forward(alone));
                StepUntil(alone, () => landedIn.HasValue || alone.Phase != MatchPhase.Rally, 8f, cam2);
                Assert.AreEqual(SwingResult.Held, cam2.Results[0], $"seed {seed}");
                Assert.IsTrue(shot.HasValue, $"seed {seed}: the held swing hit the ball");
                Assert.AreEqual(true, landedIn, $"seed {seed}: in");
                Assert.GreaterOrEqual(shot.Value.Timing, -0.6f, $"seed {seed}: never at the window's edge");
                Assert.Greater(alone.Human.Swings, swingsBefore, $"seed {seed}: the toy swings through it");

                // far before the ball: only the racket going back
                var far = FeedComing(seed);
                var cam3 = new Camera();
                StepUntil(far, () => far.Next.Active, cam: cam3);
                far.Window(far.Next, out early, out _);
                cam3.SwingAt(far.Next.Time - early - 0.2, 7f, 0f, -Forward(far));
                StepUntil(far, () => cam3.Results.Count > 0, 3f, cam3);
                Assert.AreEqual(SwingResult.Backswing, cam3.Results[0], $"seed {seed}");
                Assert.AreEqual(0, far.Stats[0].Whiffs, $"seed {seed}: no 'too early'");
            }
        }

        /// <summary>
        /// A mistimed shot goes in, only weaker (user, 2026-11-07: "many cases where my shot goes outside, what is the logic
        /// behind it?"; before, the window's outer 10 % went wide on Medium and up to 40 % on Hard): a swing at the window's
        /// late edge is slower and shorter than the same swing on time. Only against the strongest
        /// (<see cref="TennisLevel.WideFrom"/>) does the very edge go wide.
        /// </summary>
        [Test]
        public void AMistimedShot_IsWeaker_NotOut()
        {
            foreach (var d in new[] { TennisDifficulty.Easy, TennisDifficulty.Medium })
            for (int round = 0; round < TennisTournament.Rounds; round++)
                Assert.Greater(TennisLevel.For(d, round, 0).WideFrom, 1f, $"{d} round {round}: never wide");
            Assert.Greater(TennisLevel.For(TennisDifficulty.Hard, 0, 0).WideFrom, 1f, "Hard's first round: never wide");
            Assert.AreEqual(0.85f, TennisLevel.For(TennisDifficulty.Hard, 2, 0).WideFrom, 1e-4f, "Hard's final: only the very edge");
            for (int seed = 1; seed <= 6; seed++)
            {
                var onTime = SwingAt(FeedComing(seed), offset: 0.0, speed: 9f).Value;
                var m = FeedComing(seed);
                StepUntil(m, () => m.Next.Active, cam: new Camera());
                m.Window(m.Next, out _, out double late);
                bool? landedIn = null;
                m.Events += e => { if (e.Kind == MatchEventKind.Bounce && m.LastHitter == 0 && landedIn == null) landedIn = e.In; };
                var cam = new Camera();
                var edge = SwingAt(m, offset: late * 0.95, speed: 9f, cam: cam).Value;
                Assert.Greater(edge.Timing, 0.85f, $"seed {seed}: at the window's edge");
                Assert.Less(edge.Speed, onTime.Speed * 0.85f, $"seed {seed}: weaker");
                Assert.Less(Mathf.Abs(edge.Target.z), Mathf.Abs(onTime.Target.z), $"seed {seed}: shorter");
                StepUntil(m, () => landedIn.HasValue, 6f, cam);
                Assert.AreEqual(true, landedIn, $"seed {seed}: in");
            }
        }

        /// <summary>The toy swings with the player: a stroke at the coming ball is drawn timed to the ball; a swing at
        /// nothing at once, as quick as the hand, the way the hand went.</summary>
        [Test]
        public void TheToys_SwingWithThePlayer()
        {
            var m = new TennisMatch(TennisLevel.For(1), seed: 4, firstServer: 1);
            m.Begin();
            // the serve comes (the back player runs to it by itself), the swing as it arrives
            while (!(m.Phase == MatchPhase.Rally && m.Next.Active && m.Next.Team == 0 && m.Next.Time - m.Time < 0.03))
            {
                Assert.Less(m.Time, 20.0, "timed out waiting for the serve");
                m.Step(Dt);
            }
            int back = m.Human.Swings;
            Assert.AreEqual(SwingResult.Hit, m.Swing(m.Next.Time, 9f, new Vector2(Forward(m), 0f)));
            Assert.AreEqual(back + 1, m.Human.Swings, "the hitter swings");
            Assert.IsTrue(m.Human.SwingHits && !m.Human.SwingQuick, "timed to the ball");
            Assert.AreEqual(0, m.HumanNet.Swings, "singles: nobody else");
            Assert.Greater(m.Human.SwingPower, 0.8f, "as hard as the hand");
            // a swing at nothing (the ball on its way to the other side): both at once, a right-hander's hand going left =
            // a forehand
            StepUntil(m, () => m.LastHitter == 0, 2f);   // the early swing's hit waited for the ball
            back = m.Human.Swings;
            Assert.AreEqual(SwingResult.NoBall, m.Swing(m.Time, 4f, new Vector2(-1f, 0.1f)));
            Assert.AreEqual(back + 1, m.Human.Swings);
            Assert.AreEqual(Stroke.Forehand, m.Human.SwingStroke);
            Assert.IsTrue(m.Human.SwingQuick);
            Assert.Less(m.Human.SwingPower, 0.3f, "a gentle one");
            m.Swing(m.Time, 6f, new Vector2(0.1f, 0.99f));
            Assert.AreEqual(Stroke.Scoop, m.Human.SwingStroke, "straight up at no ball: a scoop");
        }

        /// <summary>
        /// The rally starts slow and gets quicker every hit (user, 2026-11-06: "from the moment the ball is served its speed
        /// starts slow, on each hit it gets faster and faster"): the serve flies at the ladder's serve pace, each shot after
        /// it a step quicker up to the top pace, a new point starts slow again, and a quicker ball crosses in less time.
        /// </summary>
        [Test]
        public void TheRally_StartsSlow_AndGetsQuickerEveryHit()
        {
            var level = TennisLevel.For(1);
            Assert.Less(level.ServePace, 1f, "the serve is slower than the balls were before the pace");
            Assert.Greater(level.ServePace, 0.8f, "but not sluggish (0.6 was, the user on the box)");
            Assert.Greater(level.TopPace, 1.3f, "a long rally ends quicker than the balls were before");
            Assert.Less(TennisLevel.For(1).ServePace, TennisLevel.For(6).ServePace, "later pairs start a little quicker");
            var m = new TennisMatch(level, seed: 3) { Practice = true };
            var shots = new List<(double time, Shot shot, int index)>();
            m.Events += e => { if (e.Kind == MatchEventKind.Hit) shots.Add((m.Time, e.Shot, m.RallyHits - 1)); };
            m.Begin();
            var cam = new Camera();
            double planned = -1, served = -1;
            while (m.Time < 180 && shots.Count < 40)
            {
                m.Step(Dt);
                cam.Drive(m);
                if (m.Phase == MatchPhase.Toss && m.Time >= m.TossTop && Math.Abs(m.TossTop - served) > 1e-6)
                {
                    served = m.TossTop;
                    m.Swing(m.Time, 6f, Vector2.down);
                }
                if (m.Phase != MatchPhase.Rally || !m.Next.Active || m.Next.Team != 0 || m.Next.Taken) continue;
                if (Math.Abs(m.Next.Time - planned) > 1e-6)
                {
                    planned = m.Next.Time;
                    cam.SwingAt(m.Next.Time, 7f, 0f, Forward(m));
                }
            }
            Assert.IsTrue(shots[0].shot.Kind is ShotKind.Serve or ShotKind.PowerServe);
            Assert.AreEqual(level.ServePace, shots[0].shot.Pace, 1e-4f, "the serve flies at the serve pace");
            int longest = 0;
            foreach (var s in shots)
            {
                Assert.AreEqual(Mathf.Min(level.TopPace, level.ServePace + level.PaceStep * s.index), s.shot.Pace, 1e-4f,
                                $"hit {s.index} of its rally");
                longest = Math.Max(longest, s.index + 1);
            }
            Assert.GreaterOrEqual(longest, 14, "a long rally (practice: the computer returns everything)");
            // the computer's feeds (always the same kind of shot) reach the player sooner and sooner: its first against one
            // at the top pace
            int first = shots.FindIndex(s => s.shot.Team == 1);
            int top = shots.FindIndex(s => s.shot.Team == 1 && s.index >= 13);
            double early = shots[first + 1].time - shots[first].time, late = shots[top + 1].time - shots[top].time;
            Debug.Log($"the computer's feed crosses in {early:0.00} s at pace {shots[first].shot.Pace:0.00}, {late:0.00} s at " +
                      $"{shots[top].shot.Pace:0.00}; paces " +
                      string.Join(" ", shots.GetRange(0, Math.Min(16, shots.Count)).ConvertAll(s => s.shot.Pace.ToString("0.00"))));
            Assert.Less(late, early * 0.8, "the ball crosses quicker late in the rally");
        }

        [Test]
        public void CameraLag_DoesntMakeAnOnTimeSwingLate()
        {
            var events = new List<MatchEvent>();
            var m = FeedComing(3, events);
            var point = m.Next.Point;
            var shot = SwingAt(m, lag: 0.25f);
            Assert.IsTrue(shot.HasValue, "the swing reached the game 250 ms late, but the camera saw it as the ball arrived");
            Assert.Less(Mathf.Abs(shot.Value.Timing), 0.05f);
            var hit = events.FindLast(e => e.Kind == MatchEventKind.Hit);
            Assert.Less(Vector3.Distance(hit.Position, point), 0.3f, "it leaves from where it was met");
        }

        [Test]
        public void ThePointWaitsForTheCamerasLateSamples()
        {
            var events = new List<MatchEvent>();
            double secondBounce = -1, point = -1;
            var m = FeedComing(5, events);
            m.Events += e =>
            {
                if (e.Kind == MatchEventKind.Bounce && secondBounce < 0 && events.FindAll(x => x.Kind == MatchEventKind.Bounce && x.Team == 0).Count == 2) secondBounce = m.Time;
                if (e.Kind == MatchEventKind.Point) point = m.Time;
            };
            StepUntil(m, () => m.Phase != MatchPhase.Rally, 8f, new Camera { Lag = 0.3f });   // never swings; it sees 0.3 s late
            Assert.Greater(secondBounce, 0);
            Assert.Greater(point - secondBounce, 0.25, "ended once the camera had caught up");
        }

        // ---------------------------------------------------------------- the computer's sure first returns

        /// <summary>
        /// The computer returns the first five balls of every point (2026-11-07 evening, user: "at minimum in all game modes
        /// vs cpu make the bot hit at least 5 times, after that it should depend on the difficulty; it's not fun to make
        /// 1-hit points all the time"): at every difficulty, alone or two together, a point the player wins came after at
        /// least five returns of the computer's; one the player loses before that was their own miss, never a ball their
        /// toy couldn't get to; and the player's shots go in until then. After the fifth the difficulty decides: points
        /// still end, and the harder, the sooner the computer wins them.
        /// </summary>
        [Test]
        public void TheComputer_ReturnsTheFirstFiveBalls_OfEveryPoint()
        {
            foreach (var mode in new[] { TennisMode.Solo, TennisMode.Together })
            foreach (TennisDifficulty d in Enum.GetValues(typeof(TennisDifficulty)))
            {
                int points = 0, won = 0, earlyLosses = 0;
                for (int seed = 1; seed <= 6; seed++)
                {
                    var m = new TennisMatch(TennisLevel.For(d, 2, seed, singles: mode != TennisMode.Together), seed: seed, mode: mode);
                    bool tooFar = false, missed = false;
                    m.Events += e =>
                    {
                        if (e.Kind == MatchEventKind.TooFar) tooFar = true;
                        if (e.Kind is MatchEventKind.Missed or MatchEventKind.Whiff or MatchEventKind.Vertical) missed = true;
                        if (e.Kind == MatchEventKind.Bounce && !e.In && m.LastHitter == 0 && m.CpuReturnsThisPoint < m.SureReturns)
                            Assert.Fail($"{mode} {d} seed {seed}: the player's shot went out in the sure part");
                        if (e.Kind != MatchEventKind.Point) return;
                        points++;
                        if (e.Team == 0)
                        {
                            won++;
                            Assert.GreaterOrEqual(m.CpuReturnsThisPoint, m.SureReturns,
                                                  $"{mode} {d} seed {seed}: the computer returned at least {m.SureReturns} balls before losing the point ({e.Reason})");
                        }
                        else if (m.CpuReturnsThisPoint < m.SureReturns)
                        {
                            // the player's own miss: a ball their toy got to went by (a swing too early or late, or none)
                            earlyLosses++;
                            Assert.IsFalse(tooFar, $"{mode} {d} seed {seed}: no ball out of the toy's reach in the sure part");
                            Assert.AreEqual(PointReason.Winner, e.Reason,
                                            $"{mode} {d} seed {seed}: lost in the sure part only on the player's own miss (missed {missed})");
                        }
                        tooFar = missed = false;
                    };
                    if (mode == TennisMode.Together) PlayTogether(m, seed);
                    else Play(m, new Sim(r => r.NextDouble() < 0.04 ? (double?)null : Gauss(r) * 0.09, new System.Random(seed)), 1800f);
                    Assert.AreEqual(MatchPhase.Over, m.Phase, $"{mode} {d} seed {seed}: the match ended");
                }
                Debug.Log($"{mode} {d}: {points} points, {won} won by the player, {earlyLosses} lost before the computer's fifth return");
                Assert.Greater(won, 0);
            }
        }

        /// <summary>Two simulated players together (doubles against the computer's pair): each swings at the balls coming to
        /// their own toy, a little off the moment they arrive, and serves near the toss's top.</summary>
        static void PlayTogether(TennisMatch m, int seed)
        {
            var rng = new System.Random(seed * 13);
            m.Begin();
            var swings = new List<(int slot, double at, Vector2 dir)>();
            double planned = -1, tossPlanned = -1;
            while (m.Phase != MatchPhase.Over && m.Time < 1800)
            {
                m.Step(Dt);
                m.SawUntil(0, m.Time - 0.1);
                m.SawUntil(1, m.Time - 0.1);
                for (int i = 0; i < swings.Count; i++)
                {
                    if (swings[i].at + 0.1 > m.Time) continue;
                    var s = swings[i];
                    swings.RemoveAt(i--);
                    m.Swing(s.slot, s.at, 6.5f, s.dir);
                }
                var n = m.Next;
                if (m.Phase == MatchPhase.Rally && m.LastHitter == 1 && n.Active && n.Team == 0 && !n.Taken && Math.Abs(n.Time - planned) > 1e-6)
                {
                    planned = n.Time;
                    if (rng.NextDouble() >= 0.04) swings.Add((n.Hitter.Controller, n.Time + Gauss(rng) * 0.09, new Vector2(n.Side > 0f ? -1f : 1f, 0.1f).normalized));
                }
                if (m.Phase == MatchPhase.Toss && m.ServerTeam == 0 && Math.Abs(m.TossTop - tossPlanned) > 1e-6)
                {
                    tossPlanned = m.TossTop;
                    swings.Add((m.Server.Controller, m.TossTop + Gauss(rng) * 0.05, Vector2.down));
                }
            }
        }

        // ---------------------------------------------------------------- singles

        /// <summary>
        /// Singles (user, 2026-11-07: "the second character per team is not needed anymore"): one player a side, the second
        /// of each team off the court (it never moves), nobody volleys, the player's toy plays every ball after the bounce,
        /// and every shot that lands in lands inside the singles lines (the computer's mistakes outside them).
        /// </summary>
        [Test]
        public void Singles_OnePlayerASide_OnTheSinglesCourt()
        {
            int hits = 0, firstBounces = 0, outs = 0;
            for (int seed = 1; seed <= 8; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(TennisDifficulty.Medium, 1, seed), seed: seed);
                Assert.IsTrue(m.Singles);
                Assert.IsFalse(m.Players[1].Active || m.Players[3].Active, "nobody at the nets");
                Assert.AreEqual(Court.SinglesHalfWidth, m.HalfWidth);
                bool first = false, serve = false;
                m.Events += e =>
                {
                    if (e.Kind == MatchEventKind.Hit)
                    {
                        Assert.IsTrue(e.Player == 0 || e.Player == 2, $"seed {seed}: only the singles players hit (player {e.Player})");
                        Assert.AreNotEqual(ShotKind.Volley, e.Shot.Kind, $"seed {seed}: no volleys");
                        first = true;
                        serve = e.Shot.Kind is ShotKind.Serve or ShotKind.PowerServe;
                        hits++;
                    }
                    else if (e.Kind == MatchEventKind.Bounce && first)
                    {
                        first = false;
                        firstBounces++;
                        bool inside = Mathf.Abs(e.Position.x) <= Court.SinglesHalfWidth + Court.BallRadius && Mathf.Abs(e.Position.z) <= Court.HalfLength + Court.BallRadius;
                        if (!serve) Assert.AreEqual(inside, e.In, $"seed {seed}: in means inside the singles lines ({e.Position})");
                        if (!e.In) outs++;
                    }
                };
                var still1 = m.Players[1].Position;
                Play(m, new Sim(r => Gauss(r) * 0.06, new System.Random(seed)), 900f);
                Assert.AreEqual(MatchPhase.Over, m.Phase);
                Assert.AreEqual(still1, m.Players[1].Position, "off the court: never moves");
                Assert.AreEqual(0, m.HumanVolleys);
            }
            Debug.Log($"singles: {hits} hits, {firstBounces} first bounces, {outs} out");
            Assert.Greater(hits, 150);
            Assert.Greater(outs, 0, "the computer's mistakes go out");
        }

        // ---------------------------------------------------------------- serving

        [Test]
        public void Serve_AtTheTopOfTheToss_IsAPowerServe_ThatLandsInTheBox()
        {
            var events = new List<MatchEvent>();
            var m = new TennisMatch(TennisLevel.For(1), seed: 5);
            m.Events += events.Add;
            m.Begin();
            StepUntil(m, () => m.Phase == MatchPhase.Toss);
            StepUntil(m, () => m.Time >= m.TossTop + 0.1);
            Assert.AreEqual(SwingResult.Serve, m.Swing(m.TossTop + 0.02, 8f, Vector2.down), "the camera saw it at the top, 80 ms ago");
            StepUntil(m, () => events.Exists(e => e.Kind == MatchEventKind.Bounce), 3f);
            var serve = events.Find(e => e.Kind == MatchEventKind.Hit);
            var bounce = events.Find(e => e.Kind == MatchEventKind.Bounce);
            Assert.AreEqual(ShotKind.PowerServe, serve.Shot.Kind);
            Assert.AreEqual(0, serve.Player, "the back player serves");
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
            Assert.AreEqual(SwingResult.Toss, m.Swing(m.Time, 6f, Vector2.down), "like Wii's flick up");
            Assert.AreEqual(MatchPhase.Toss, m.Phase);
            StepUntil(m, () => m.Time >= m.TossTop + 0.3);
            Assert.AreEqual(SwingResult.Serve, m.Swing(m.Time, 6f, Vector2.down));
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
        public void NoSwing_LosesThePoint()
        {
            var m = new TennisMatch(TennisLevel.For(1), seed: 7, firstServer: 1);
            m.Begin();
            StepUntil(m, () => m.Phase == MatchPhase.PointOver, 20f, new Camera());
            Assert.AreEqual(1, m.PointWinner, "the serve went by");
            Assert.AreEqual(PointReason.Winner, m.LastReason);
            Assert.AreEqual(1, m.Score.Points(1));
        }

        /// <summary>In, except a swing at the very edge of the window against later players (<see cref="TennisLevel.WideFrom"/>).</summary>
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
                Play(m, new Sim(r => Gauss(r) * 0.12, rng) { Rise = 0.75f }, 600f);   // every kind of sideways shot
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
            m.Swing(m.TossTop, 6f, Vector2.down);
            StepUntil(m, () => m.LastHitter == 1 && m.Next.Active && m.Next.Team == 0);
            Assert.AreEqual(asked, m.Next.Time, 0.06, "the feed reaches the player when practice asked");
            Assert.AreSame(m.Human, m.Next.Hitter, "practice: the back player plays every ball");
        }

        // ---------------------------------------------------------------- whole matches

        /// <summary>
        /// A simulated player, swinging like the user's real swings (the box's log, 2026-11-06): at every ball of theirs
        /// (the camera <see cref="Lag"/> late) a backswing toward the ball's side 0.3 s before the stroke, then the stroke
        /// across, <see cref="Timing"/> off the moment the ball arrives, as fast as <see cref="SwingSpeed"/>, tilted up or
        /// down by up to <see cref="Rise"/>; a share of strokes the wrong way (<see cref="WrongWay"/>) or straight up or
        /// down (<see cref="Vertical"/>: they miss). It serves with a swing near the toss's top. Its toys run to the ball
        /// by themselves.
        /// </summary>
        sealed class Sim
        {
            public readonly Func<System.Random, double?> Timing;
            /// <summary>Seconds off the toss's top (null = like <see cref="Timing"/>).</summary>
            public Func<System.Random, double?> ServeTiming;
            public readonly System.Random Rng;
            public float Lag = 0.1f, Rise = 0.45f, ServeSpeed = 6f, SlowestSwing = 4f, FastestSwing = 9f;
            public float WrongWay, Vertical;
            public bool Backswing = true;

            public Sim(Func<System.Random, double?> timing, System.Random rng)
            {
                Timing = timing;
                Rng = rng;
            }

            public float SwingSpeed() => Mathf.Lerp(SlowestSwing, FastestSwing, (float)Rng.NextDouble());
        }

        static float Gauss(System.Random r) =>
            (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - r.NextDouble())) * Math.Cos(2.0 * Math.PI * r.NextDouble()));

        static void Play(TennisMatch m, Sim sim, float maxSeconds, Action<TennisMatch> each = null)
        {
            m.Begin();
            var cam = new Camera { Lag = sim.Lag };
            double planned = -1, tossPlanned = -1;
            while (m.Phase != MatchPhase.Over && m.Time < maxSeconds)
            {
                m.Step(Dt);
                cam.Drive(m);
                each?.Invoke(m);
                // one stroke per ball (a volley let through is planned again for the back player), the racket taken back
                // before it
                bool incoming = m.Phase == MatchPhase.Rally && m.LastHitter == 1 && m.Next.Active && m.Next.Team == 0;
                if (incoming && !m.Next.Taken && Math.Abs(m.Next.Time - planned) > 1e-6)
                {
                    planned = m.Next.Time;
                    var e = sim.Timing(sim.Rng);
                    if (e.HasValue)
                    {
                        float forward = Forward(m);
                        if (sim.Backswing) cam.SwingAt(m.Next.Time + e.Value - 0.3, sim.SwingSpeed() * 0.8f, 0f, -forward);
                        float tilt = (float)(sim.Rng.NextDouble() * 2.0 - 1.0) * sim.Rise;
                        if (sim.Rng.NextDouble() < sim.Vertical) tilt = tilt >= 0f ? 0.95f : -0.95f;
                        float way = sim.Rng.NextDouble() < sim.WrongWay ? -forward : forward;
                        cam.SwingAt(m.Next.Time + e.Value, sim.SwingSpeed(), tilt, way);
                    }
                }
                // the serve: a swing near the toss's top
                if (m.Phase == MatchPhase.Toss && m.ServerTeam == 0 && Math.Abs(m.TossTop - tossPlanned) > 1e-6)
                {
                    tossPlanned = m.TossTop;
                    var e = sim.ServeTiming != null ? sim.ServeTiming(sim.Rng) : sim.Timing(sim.Rng);
                    if (e.HasValue) cam.SwingAt(m.TossTop + e.Value, sim.ServeSpeed, -1f);
                }
            }
        }

        sealed class Profile
        {
            public string Name;
            public Func<System.Random, double?> Timing;
            public float Slowest = 4f, Fastest = 9f, Rise = 0.45f, WrongWay, Vertical;
            public bool ServesOnly;
        }

        static readonly Profile[] Profiles =
        {
            new Profile { Name = "perfect", Timing = r => Gauss(r) * 0.03, Slowest = 7f, Fastest = 10f, Rise = 0.3f },
            new Profile { Name = "good", Timing = r => Gauss(r) * 0.09, WrongWay = 0.03f, Vertical = 0.02f },
            new Profile { Name = "child", Timing = r => r.NextDouble() < 0.1 ? (double?)null : -0.04 + Gauss(r) * 0.16, Slowest = 3f, Fastest = 6f,
                          Rise = 0.6f, WrongWay = 0.2f, Vertical = 0.06f },
            // the same child swinging at every ball (since 2026-11-07 evening the computer returns the first five balls of
            // every point: a point takes six good swings or more, so whether a child swings at every ball matters most)
            new Profile { Name = "careful child", Timing = r => -0.04 + Gauss(r) * 0.16, Slowest = 3f, Fastest = 6f,
                          Rise = 0.6f, WrongWay = 0.2f, Vertical = 0.06f },
            new Profile { Name = "struggling", Timing = r => r.NextDouble() < 0.25 ? (double?)null : Gauss(r) * 0.25, Slowest = 3f, Fastest = 5f,
                          Rise = 0.6f, WrongWay = 0.3f, Vertical = 0.1f },
            // serves (else the game waits for the serve forever, like Wii), then never swings
            new Profile { Name = "standing still", Timing = r => null, ServesOnly = true },
        };

        /// <summary>Wins out of 10 matches (first to 2 games) per player and difficulty, in a tournament's first round and
        /// its final, and how long they take.</summary>
        static (int wins, float minutes, int pointsWon, int pointsLost)[,] Table((TennisDifficulty d, int round)[] levels, int matches = 10)
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
                    var m = new TennisMatch(TennisLevel.For(levels[l].d, levels[l].round, s), seed: 1000 * p + 100 * l + s);
                    var sim = new Sim(pr.Timing, new System.Random(s * 31 + p))
                    {
                        SlowestSwing = pr.Slowest, FastestSwing = pr.Fastest, ServeTiming = pr.ServesOnly ? r => 0.0 : (Func<System.Random, double?>)null,
                        Rise = pr.Rise, WrongWay = pr.WrongWay, Vertical = pr.Vertical,
                    };
                    Play(m, sim, 1800f);
                    Assert.AreEqual(MatchPhase.Over, m.Phase, $"{pr.Name} at {levels[l].d} round {levels[l].round}: the match ended");
                    if (m.Score.Winner == 0) wins++;
                    won += m.PointsWon;
                    lost += m.PointsLost;
                    time += m.Time;
                }
                table[p, l] = (wins, (float)(time / matches / 60.0), won, lost);
            }
            return table;
        }

        /// <summary>
        /// The three difficulties with simulated players in singles (the toys run by themselves, so only the swings differ):
        /// a tournament's quarter-final and final at each. A child wins Easy, finds Medium's final about even and Hard hard;
        /// a good player wins Medium and loses real points in Hard's final; a perfect one wins everything; doing nothing
        /// never wins. Since 2026-11-07 the player's shots go in (a mistimed one is weaker): points are lost on balls out of
        /// reach, the strong opponents' attacks on soft balls and missed swings.
        /// </summary>
        [Test]
        public void Balance_ByDifficulty()
        {
            var levels = new[]
            {
                (TennisDifficulty.Easy, 0), (TennisDifficulty.Easy, 2), (TennisDifficulty.Medium, 0), (TennisDifficulty.Medium, 2),
                (TennisDifficulty.Hard, 0), (TennisDifficulty.Hard, 2),
            };
            var t = Table(levels);
            var log = new System.Text.StringBuilder("singles, wins of 10 (minutes per match, points won-lost):\n");
            for (int p = 0; p < Profiles.Length; p++)
            {
                log.Append($"  {Profiles[p].Name,-15}");
                for (int l = 0; l < levels.Length; l++)
                    log.Append($"  {levels[l].Item1.ToString()[0]}{levels[l].Item2}: {t[p, l].wins,2} ({t[p, l].minutes:0.0} min, {t[p, l].pointsWon}-{t[p, l].pointsLost})");
                log.Append('\n');
            }
            Debug.Log(log.ToString());
            int Wins(string name, TennisDifficulty d, int round) =>
                t[Array.FindIndex(Profiles, x => x.Name == name), Array.IndexOf(levels, (d, round))].wins;
            Assert.AreEqual(10, Wins("perfect", TennisDifficulty.Easy, 0));
            Assert.GreaterOrEqual(Wins("perfect", TennisDifficulty.Hard, 2), 8, "a perfect player wins Hard's final");
            Assert.GreaterOrEqual(Wins("good", TennisDifficulty.Easy, 2), 9);
            Assert.GreaterOrEqual(Wins("good", TennisDifficulty.Medium, 2), 7, "a good player wins Medium");
            var goodHard = t[Array.FindIndex(Profiles, x => x.Name == "good"), Array.IndexOf(levels, (TennisDifficulty.Hard, 2))];
            Assert.Greater(goodHard.pointsLost, 0.15f * (goodHard.pointsWon + goodHard.pointsLost),
                           "Hard's final is a real match for a good player (since 2026-11-07 their shots go in: they still lose points)");
            // (since 2026-11-07 evening the computer returns the first five balls of every point: a point takes six good
            // swings or more, so whether a child swings at every ball matters most)
            Assert.GreaterOrEqual(Wins("careful child", TennisDifficulty.Easy, 0), 8, "a child who swings at every ball wins Easy");
            Assert.GreaterOrEqual(Wins("careful child", TennisDifficulty.Easy, 2), 8, "even its final");
            var carefulMedium = t[Array.FindIndex(Profiles, x => x.Name == "careful child"), Array.IndexOf(levels, (TennisDifficulty.Medium, 2))];
            Assert.Greater(carefulMedium.pointsLost, 0.2f * (carefulMedium.pointsWon + carefulMedium.pointsLost), "Medium's final is a real match for a child");
            Assert.LessOrEqual(Wins("careful child", TennisDifficulty.Hard, 2), 3, "and Hard's is hard");
            Assert.GreaterOrEqual(Wins("child", TennisDifficulty.Easy, 0), 3, "a child who lets one ball in ten go by still wins on Easy at times");
            Assert.LessOrEqual(Wins("child", TennisDifficulty.Hard, 2), 3, "Hard is hard");
            var strugglingEasy = t[Array.FindIndex(Profiles, x => x.Name == "struggling"), Array.IndexOf(levels, (TennisDifficulty.Easy, 0))];
            Assert.Greater(strugglingEasy.pointsWon, 5, "a struggling player still wins points on Easy");
            Assert.LessOrEqual(Wins("struggling", TennisDifficulty.Medium, 2), 3);
            Assert.AreEqual(0, Wins("standing still", TennisDifficulty.Easy, 0), "doing nothing never wins");
            for (int l = 0; l < levels.Length; l++)
                Assert.Less(t[Array.FindIndex(Profiles, x => x.Name == "good"), l].minutes, 12f, "a match is a few minutes");
        }
    }
}
