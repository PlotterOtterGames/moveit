using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Tennis.Tests
{
    /// <summary>
    /// Two players (2026-11-05): versus (one team each, every rule the same at both ends) and together (one toy each
    /// against the computer's pair, changing places so both serve). The toys run to the ball by themselves, a swing
    /// hits only the ball coming to a toy of that player's, the end of a point waits for the receiving player's camera,
    /// and whole matches between simulated players end, with long rallies between good ones and the better player
    /// winning more.
    /// </summary>
    public class TennisTwoPlayerTests
    {
        const float Dt = 1f / 60f;

        static void StepUntil(TennisMatch m, Func<bool> done, float seconds = 30f)
        {
            double end = m.Time + seconds;
            while (!done())
            {
                Assert.Less(m.Time, end, "timed out waiting");
                m.Step(Dt);
            }
        }

        /// <summary>A simulated player of slot <see cref="Slot"/>: like TennisMatchTests' Sim (swings at every ball of
        /// theirs <see cref="Timing"/> off its arrival, the camera <see cref="Lag"/> late; serves near the toss's top;
        /// their toys run by themselves).</summary>
        sealed class Player
        {
            public int Slot;
            public Func<System.Random, double?> Timing = r => Gauss(r) * 0.09;
            public System.Random Rng;
            public float Lag = 0.1f, Rate = 15f;
            public int Swings;
            public readonly List<SwingResult> Results = new List<SwingResult>();
            readonly Queue<double> _samples = new Queue<double>();
            readonly List<(double at, float speed, Vector2 direction)> _swings = new List<(double, float, Vector2)>();
            double _nextSample = double.NegativeInfinity, _planned = -1, _tossPlanned = -1;

            public void SwingAt(double at, float speed, Vector2 direction) => _swings.Add((at, speed, direction));

            /// <summary>Their swings for one step (before m.Step), their camera after it.</summary>
            public void Move(TennisMatch m)
            {
                var n = m.Next;
                bool incoming = m.Phase == MatchPhase.Rally && n.Active && n.Hitter.Controller == Slot && m.LastHitter == 1 - n.Team;
                if (incoming && !n.Taken && Math.Abs(n.Time - _planned) > 1e-6)
                {
                    _planned = n.Time;
                    var e = Timing(Rng);
                    if (e.HasValue) SwingAt(n.Time + e.Value, 6f, new Vector2(-1f, 0.2f).normalized);
                }
                if (m.Phase == MatchPhase.Toss && m.Server.Controller == Slot && Math.Abs(m.TossTop - _tossPlanned) > 1e-6)
                {
                    _tossPlanned = m.TossTop;
                    SwingAt(m.TossTop + Gauss(Rng) * 0.05, 6f, Vector2.down);
                }
            }

            public void Camera(TennisMatch m)
            {
                if (m.Time >= _nextSample)
                {
                    _nextSample = Math.Max(m.Time, _nextSample + 1.0 / Rate);
                    _samples.Enqueue(m.Time);
                }
                while (_samples.Count > 0 && _samples.Peek() + Lag <= m.Time) m.SawUntil(Slot, _samples.Dequeue());
                for (int i = 0; i < _swings.Count; i++)
                {
                    if (_swings[i].at + Lag > m.Time) continue;
                    var s = _swings[i];
                    _swings.RemoveAt(i--);
                    Swings++;
                    Results.Add(m.Swing(Slot, s.at, s.speed, s.direction));
                }
            }
        }

        static float Gauss(System.Random r) =>
            (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - r.NextDouble())) * Math.Cos(2.0 * Math.PI * r.NextDouble()));

        static void Play(TennisMatch m, Player[] players, float maxSeconds)
        {
            m.Begin();
            while (m.Phase != MatchPhase.Over && m.Time < maxSeconds)
            {
                foreach (var p in players) p.Move(m);
                m.Step(Dt);
                foreach (var p in players) p.Camera(m);
            }
        }

        static Player[] Two(int seed, Func<System.Random, double?> timing1 = null, Func<System.Random, double?> timing2 = null)
        {
            var a = new Player { Slot = 0, Rng = new System.Random(seed * 7 + 1) };
            var b = new Player { Slot = 1, Rng = new System.Random(seed * 7 + 2) };
            if (timing1 != null) a.Timing = timing1;
            if (timing2 != null) b.Timing = timing2;
            return new[] { a, b };
        }

        // ---------------------------------------------------------------- versus

        /// <summary>Versus: singles, both players' toys run to the ball by themselves (player 2's, at the far end,
        /// too).</summary>
        [Test]
        public void Versus_BothTeamsRunToTheBall_ByThemselves()
        {
            var m = new TennisMatch(TennisLevel.For(TennisDifficulty.Medium, 1, 0), mode: TennisMode.Versus, seed: 2);
            m.Begin();
            Assert.AreEqual(0, m.TeamOf(0));
            Assert.AreEqual(1, m.TeamOf(1));
            Assert.IsTrue(m.IsHuman(0) && m.IsHuman(1), "no computer");
            Assert.IsTrue(m.Singles, "versus is singles (2026-11-07)");
            for (int team = 0; team < 2; team++)
                Assert.IsFalse(m.NetPlayer(team).Active, $"team {team}: nobody at the net");
            StepUntil(m, () => m.Phase == MatchPhase.Toss);
            StepUntil(m, () => m.Time >= m.TossTop);
            Assert.AreEqual(SwingResult.Serve, m.Swing(0, m.TossTop, 6f, Vector2.down));
            StepUntil(m, () => m.Next.Active && m.Next.Team == 1);
            var receiver = m.Next.Hitter;
            Assert.AreEqual(1, receiver.Controller, "player 2's toy receives");
            Assert.IsTrue(m.Next.Reachable);
            double contact = m.Next.Time;
            StepUntil(m, () => m.Time >= contact - Dt);
            Assert.Less(Vector2.Distance(receiver.Position, m.Next.Spot), m.ReachTolerance, "it ran there by itself");
        }

        [Test]
        public void Versus_OnlyThePlayerWhoseBallItIs_CanHitIt()
        {
            var m = new TennisMatch(TennisLevel.For(1), mode: TennisMode.Versus, seed: 3);
            m.Begin();
            StepUntil(m, () => m.Phase == MatchPhase.Toss);
            Assert.AreSame(m.Back(0), m.Server, "player 1 serves first");
            StepUntil(m, () => m.Time >= m.TossTop);
            Assert.AreEqual(SwingResult.NoBall, m.Swing(1, m.TossTop, 6f, Vector2.down), "player 2 can't serve player 1's toss");
            Assert.AreEqual(SwingResult.Serve, m.Swing(0, m.TossTop, 6f, Vector2.down));
            StepUntil(m, () => m.Next.Active && m.Next.Team == 1);
            Assert.AreEqual(1, m.Next.Hitter.Controller, "the serve goes to player 2's team");
            double at = m.Next.Time;
            StepUntil(m, () => m.Time >= at);
            Assert.AreEqual(SwingResult.NoBall, m.Swing(0, at, 6f, Vector2.left), "player 1 can't hit player 2's ball");
            var hits = new List<MatchEvent>();
            m.Events += e => { if (e.Kind == MatchEventKind.Hit) hits.Add(e); };
            Assert.AreEqual(SwingResult.Hit, m.Swing(1, at, 6f, Vector2.left), "player 2 returns it");
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(1, hits[0].Team);
            StepUntil(m, () => m.Next.Active || m.Phase != MatchPhase.Rally);
            Assert.IsTrue(m.Next.Active && m.Next.Team == 0 && m.Next.Hitter.Controller == 0, "back to player 1");
            Assert.Greater(Court.Side(0) * hits[0].Shot.Target.z, 0f, "aimed into player 1's half");
            Assert.AreEqual(1, m.Stats[1].Hits);
            Assert.AreEqual(0, m.Stats[0].Hits);
        }

        /// <summary>An ace past player 2: how long after the ball's last bounce the point ends, with only
        /// <paramref name="watched"/>'s camera reporting every step.</summary>
        static double AceDelay(int watched)
        {
            var m = new TennisMatch(TennisLevel.For(1), mode: TennisMode.Versus, seed: 5);
            m.Begin();
            StepUntil(m, () => m.Phase == MatchPhase.Toss);
            StepUntil(m, () => m.Time >= m.TossTop);
            Assert.AreEqual(SwingResult.Serve, m.Swing(0, m.TossTop, 6f, Vector2.down));   // player 2 never swings
            double lastBounce = -1;
            m.Events += e => { if (e.Kind == MatchEventKind.Bounce) lastBounce = m.Time; };
            StepUntil(m, () =>
            {
                m.SawUntil(watched, m.Time);
                return m.Phase == MatchPhase.PointOver;
            }, 12f);
            Assert.AreEqual(0, m.PointWinner, "an ace");
            return m.Time - lastBounce;
        }

        [Test]
        public void Versus_TheEndOfAPoint_WaitsForTheReceivingPlayersCamera()
        {
            // a swing of player 2's from just before the ball went by may still be on its way: the point waits for their
            // camera (up to HitLag), not for player 1's
            Assert.Less(AceDelay(1), 2.5 * Dt, "player 2's camera is up to date: it ends at once");
            Assert.Greater(AceDelay(0), 0.25, "only player 1's camera reports: it waits for player 2's");
        }

        [Test]
        public void Versus_TwoGoodPlayers_PlayRallies_AndEveryMatchEnds()
        {
            int rallies = 0, points = 0, hits0 = 0, hits1 = 0, wins0 = 0;
            for (int seed = 0; seed < 6; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(1), mode: TennisMode.Versus, seed: seed, firstServer: seed % 2);
                var players = Two(seed);
                int longest = 0;
                m.Events += e => { if (e.Kind == MatchEventKind.Point) { points++; longest = Math.Max(longest, m.RallyHits); } };
                Play(m, players, 2400f);
                Assert.AreEqual(MatchPhase.Over, m.Phase, $"seed {seed}: the match ended");
                Assert.IsTrue(m.Score.Winner == 0 || m.Score.Winner == 1);
                rallies += m.LongestRally;
                hits0 += m.Stats[0].Hits;
                hits1 += m.Stats[1].Hits;
                if (m.Score.Winner == 0) wins0++;
            }
            Debug.Log($"versus, two good players: longest rally {rallies / 6.0:0.0} on average, hits {hits0} / {hits1}, player 1 won {wins0} of 6, {points} points");
            Assert.Greater(rallies / 6.0, 4.0, "good players keep the ball going");
            Assert.Greater(hits0, 30);
            Assert.Greater(hits1, 30);
            Assert.Less(Mathf.Abs(hits0 - hits1), 0.4f * Mathf.Max(hits0, hits1), "both play about as much");
        }

        [Test]
        public void Versus_TheBetterPlayer_WinsMore()
        {
            int wins = 0;
            for (int seed = 0; seed < 8; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(1), mode: TennisMode.Versus, seed: 100 + seed, firstServer: seed % 2);
                var players = Two(seed, r => Gauss(r) * 0.05, r => r.NextDouble() < 0.25 ? (double?)null : Gauss(r) * 0.3);
                Play(m, players, 2400f);
                Assert.AreEqual(MatchPhase.Over, m.Phase);
                if (m.Score.Winner == 0) wins++;
            }
            Assert.GreaterOrEqual(wins, 6, "a good player beats a struggling one most of the time");
        }

        // ---------------------------------------------------------------- together

        /// <summary>Together: each player's toy waits at the net on its own player's side of the room (player 1 on the
        /// left), and the back toy runs to the ball by itself.</summary>
        [Test]
        public void Together_TheNetToyWaitsOnItsPlayersSide()
        {
            // the computer serves first: whoever won't serve their team's first game (player 2) starts at the back
            var m = new TennisMatch(TennisLevel.For(1, singles: false), mode: TennisMode.Together, firstServer: 1, seed: 4);
            m.Begin();
            Assert.AreEqual(0, m.TeamOf(0));
            Assert.AreEqual(0, m.TeamOf(1));
            Assert.IsFalse(m.IsHuman(1), "the computer's pair");
            Assert.AreEqual(1, m.Back(0).Controller, "player 2 starts at the back");
            Assert.AreEqual(0, m.NetPlayer(0).Controller, "player 1 at the net");
            Assert.Less(m.NetPlayer(0).Position.x, -0.5f, "player 1's toy waits on the left (their side of the room)");
            StepUntil(m, () => m.Phase == MatchPhase.Rally && m.Next.Active && m.Next.Team == 0);
            Assert.AreSame(m.Back(0), m.Next.Hitter, "player 2's toy returns the serve");
            double contact = m.Next.Time;
            StepUntil(m, () => m.Time >= contact - Dt);
            Assert.Less(Vector2.Distance(m.Back(0).Position, m.Next.Spot), m.ReachTolerance, "it ran there by itself");
        }

        [Test]
        public void Together_TheBallIsTheToysPlayers()
        {
            // a practice-like start: the computer serves; whoever's toy meets the ball, only that player's swing hits it
            for (int seed = 0; seed < 12; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(1, singles: false), mode: TennisMode.Together, seed: seed, firstServer: 1);
                m.Begin();
                StepUntil(m, () => m.Phase == MatchPhase.Rally && m.Next.Active && m.Next.Team == 0 || m.Phase == MatchPhase.PointOver);
                if (m.Phase != MatchPhase.Rally) continue;
                int owner = m.Next.Hitter.Controller;
                double at = m.Next.Time;
                StepUntil(m, () => m.Time >= at);
                Assert.AreEqual(SwingResult.NoBall, m.Swing(1 - owner, at, 6f, Vector2.left), $"seed {seed}: not the partner's ball");
                Assert.AreEqual(SwingResult.Hit, m.Swing(owner, at, 6f, Vector2.left), $"seed {seed}: player {owner + 1} hits it");
                return;
            }
            Assert.Fail("no serve came back in 12 seeds");
        }

        [Test]
        public void Together_TheyChangePlaces_SoBothServe()
        {
            var m = new TennisMatch(TennisLevel.For(1, singles: false), gamesToWin: 4, mode: TennisMode.Together, seed: 9);
            var servers = new List<int>();
            int lastGames = -1;
            m.Events += e =>
            {
                if (e.Kind == MatchEventKind.Toss && e.Team == 0 && m.Score.GamesPlayed != lastGames)
                {
                    lastGames = m.Score.GamesPlayed;
                    servers.Add(m.Server.Controller);
                    Assert.AreSame(m.Back(0), m.Server, "the server plays from the back");
                    Assert.AreNotEqual(m.Server.Controller, m.NetPlayer(0).Controller, "the partner at the net");
                }
            };
            Play(m, Two(9, r => Gauss(r) * 0.05, r => Gauss(r) * 0.05), 3000f);
            Assert.GreaterOrEqual(servers.Count, 2, "the team served at least two games");
            Assert.AreEqual(0, servers[0], "player 1 serves first");
            Assert.AreEqual(1, servers[1], "then player 2");
        }

        /// <summary>Together (doubles): the players' net toy volleys balls that pass within its reach (and its player's
        /// swing hits them: <see cref="ShotKind.Volley"/> or a smash), the back toy gets the rest, the computer's net player
        /// volleys too.</summary>
        [Test]
        public void Together_TheNetPlayers_VolleyWhatPassesWithinReach()
        {
            int humanVolleys = 0, humanBack = 0, cpuVolleys = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(4, singles: false), mode: TennisMode.Together, seed: seed, firstServer: seed % 2);
                Assert.IsFalse(m.Singles);
                m.Events += e =>
                {
                    if (e.Kind != MatchEventKind.Hit || e.Shot.Kind is ShotKind.Serve or ShotKind.PowerServe) return;
                    var p = m.Players[e.Player];
                    if (p.Team == 0 && p.AtNet)
                    {
                        humanVolleys++;
                        Assert.IsTrue(e.Shot.Kind is ShotKind.Volley or ShotKind.Smash, $"seed {seed}: the net player volleys ({e.Shot.Kind})");
                        Assert.Greater(e.Position.z, -TennisMatch.NetDepth - 1.5f, "near the net player, before the bounce");
                    }
                    else if (p.Team == 0) humanBack++;
                    else if (p.AtNet) cpuVolleys++;
                };
                Play(m, Two(seed, r => Gauss(r) * 0.05, r => Gauss(r) * 0.05), 900f);
            }
            Debug.Log($"together: the players' net toy volleyed {humanVolleys}, the back toy hit {humanBack}, the computer's net player volleyed {cpuVolleys}");
            Assert.Greater(humanVolleys, 10, "the net toy plays");
            Assert.Greater(humanBack, humanVolleys, "most balls are the back toy's");
            Assert.Greater(cpuVolleys, 5, "the computer's net player plays");
        }

        /// <summary>Together: a ball the net toy's player lets through (no swing in time) is the back toy's after the
        /// bounce.</summary>
        [Test]
        public void Together_AVolleyLetThrough_IsTheBackPlayers()
        {
            int letThrough = 0;
            for (int seed = 1; seed <= 30 && letThrough < 4; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(4, singles: false), mode: TennisMode.Together, seed: seed);
                bool waiting = false;
                double planned = -1;
                m.Begin();
                while (m.Time < 300 && m.Phase != MatchPhase.Over)
                {
                    m.Step(Dt);
                    m.SawUntil(0, m.Time);
                    m.SawUntil(1, m.Time);
                    if (m.Phase == MatchPhase.Toss && m.ServerTeam == 0 && m.Time >= m.TossTop && m.Server.Serving)
                        m.Swing(m.Server.Controller, m.Time, 6f, Vector2.down);
                    if (m.Phase != MatchPhase.Rally || !m.Next.Active || m.Next.Team != 0 || m.Next.Taken) continue;
                    if (m.Next.Volley && !waiting && Math.Abs(m.Next.Time - planned) > 1e-6)
                    {
                        waiting = true;   // let this one through
                        planned = m.Next.Time;
                        continue;
                    }
                    if (waiting && !m.Next.Volley)
                    {
                        Assert.AreSame(m.Back(0), m.Next.Hitter, $"seed {seed}: the back toy takes it");
                        letThrough++;
                        waiting = false;
                    }
                    if (!m.Next.Volley && m.Time >= m.Next.Time)
                    {
                        planned = m.Next.Time;
                        m.Swing(m.Next.Hitter.Controller, m.Next.Time, 6f, Vector2.left);
                    }
                }
            }
            Assert.GreaterOrEqual(letThrough, 4);
        }

        [Test]
        public void Together_TwoGoodPlayers_UsuallyBeatTheFirstPair()
        {
            int wins = 0;
            for (int seed = 0; seed < 6; seed++)
            {
                var m = new TennisMatch(TennisLevel.For(1, singles: false), mode: TennisMode.Together, seed: 200 + seed, firstServer: seed % 2);
                var players = Two(seed);
                Play(m, players, 2400f);
                Assert.AreEqual(MatchPhase.Over, m.Phase, $"seed {seed}: the match ended");
                Assert.Greater(players[0].Swings, 0);
                Assert.Greater(players[1].Swings, 0, "the net player got balls too");
                if (m.Score.Winner == 0) wins++;
            }
            Assert.GreaterOrEqual(wins, 5, "two good players win the first match");
        }
    }
}
