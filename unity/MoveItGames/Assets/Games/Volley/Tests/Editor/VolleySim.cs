using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Volley.Tests
{
    /// <summary>
    /// A simulated player for <see cref="VolleyMatch"/>: for every ball coming to one of its toys it makes the move that
    /// ball wants (or a wrong one, or none, by chance), seen by a camera that samples it every
    /// <see cref="SampleEvery"/> and tells the game <see cref="Lag"/> later (a hit another <see cref="HitDetect"/> later:
    /// the swing detector confirms it). Poses are taken a while before the ball arrives (players get ready) and held a
    /// little after it; hits come near the ball's arrival with a bias and a spread.
    /// </summary>
    public sealed class VolleySim
    {
        public string Name = "good";
        public int Slot;
        public float Lag = 0.12f, SampleEvery = 1f / 30f, HitDetect = 0.1f;
        /// <summary>A pose is taken this long (+- the jitter) before the ball arrives, held this long after.</summary>
        public float PoseLead = 0.6f, PoseLeadJitter = 0.2f, PoseHold = 0.25f;
        /// <summary>A hit's capture time against the ball's arrival: mean and spread (s).</summary>
        public float HitBias = -0.05f, HitError = 0.08f;
        /// <summary>A hit's hand speed (torso lengths a second) and its spread.</summary>
        public float HitSpeed = 6f, HitSpeedJitter = 1.5f;
        /// <summary>No move for a ball; the wrong move for it (a set or a hit at the receive, a bump at the third).</summary>
        public float MissChance, WrongChance;
        /// <summary>Never moves.</summary>
        public bool Still;
        /// <summary>How long after it sees the ball played (the moment the rules plan its touch) a move can start (s): a
        /// person's reaction, and the time to move the arms.</summary>
        public float Reaction = 0.3f;
        /// <summary>Holds the hands together low all the time (and makes no other move): the receive the user found too
        /// easy (2026-11-08).</summary>
        public bool HoldsBump;
        /// <summary>Holds both hands up all the time (and makes no other move): blocks everything.</summary>
        public bool HoldsBlock;
        /// <summary>The defence (<see cref="VolleyMatch.Coming"/>): how often it reads the hitter's look right when it has
        /// the time (Easy's "!" makes it near certain), and how often it blocks when it can't read anything (a disguised tip
        /// it is too slow for, the other player's attack in versus).</summary>
        public float ReadSkill = 0.9f, GuessBlock = 0.5f;
        /// <summary>What it does at each touch (null = the touch's own move: bump, set, hit), and against every attack (null:
        /// it reads the hitter).</summary>
        public Move? AtReceive, AtSecond, AtThird, AlwaysDefend;

        public int Moves { get; private set; }
        /// <summary>Defences it planned: blocks and receives.</summary>
        public int Blocks { get; private set; }
        public int Receives { get; private set; }

        readonly System.Random _rng;
        double _plannedTime = double.NaN;
        int _plannedNumber = -1;
        readonly List<(Move move, double from, double to, float speed)> _plan = new List<(Move, double, double, float)>();
        readonly List<(double knownAt, Move move, double time, float speed)> _queue = new List<(double, Move, double, float)>();
        double _lastSample = double.NaN;

        public VolleySim(int seed, int slot = 0)
        {
            _rng = new System.Random(seed);
            Slot = slot;
        }

        float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        float Gauss()
        {
            double u = 1.0 - _rng.NextDouble(), v = _rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v));
        }

        /// <summary>Called every frame before the match steps: plans the coming ball's move (and a defence against an attack
        /// being set up), samples, tells the game.</summary>
        public void Step(VolleyMatch m)
        {
            if (!Still && !HoldsBump && !HoldsBlock)
            {
                PlanDefence(m);
                Plan(m);
            }
            double now = m.Time;
            if (double.IsNaN(_lastSample)) _lastSample = now;
            while (_lastSample + SampleEvery <= now)
            {
                _lastSample += SampleEvery;
                Sample(_lastSample);
            }
            double settled = now - Lag - SampleEvery;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var q = _queue[i];
                if (q.knownAt <= now)
                {
                    m.See(Slot, q.move, q.time, q.speed, new Vector2(0.2f, -1f).normalized);
                    _queue.RemoveAt(i);
                }
                else if (q.move == Move.Hit) settled = Math.Min(settled, q.time - 0.001);
            }
            m.SawUntil(Slot, settled);
        }

        void Plan(VolleyMatch m)
        {
            var c = m.Next;
            if (!c.Active || c.Player == null || c.Player.Controller != Slot) return;
            if (c.Number == _plannedNumber && Math.Abs(c.Time - _plannedTime) < 1e-6) return;
            _plannedNumber = c.Number;
            _plannedTime = c.Time;
            if (_rng.NextDouble() < MissChance) return;
            bool wrong = _rng.NextDouble() < WrongChance;
            Move move = c.Number switch
            {
                0 => Move.Hit,
                1 => AtReceive ?? (wrong ? (_rng.NextDouble() < 0.5 ? Move.Set : Move.Hit) : Move.Bump),
                2 => AtSecond ?? (wrong ? Move.Bump : Move.Set),
                _ => AtThird ?? (wrong ? Move.Bump : Move.Hit),
            };
            Moves++;
            if (move == Move.Hit)
            {
                double at = c.Time + HitBias + HitError * Gauss();
                // a serve's hit comes while the toss is up
                if (c.Number == 0) at = Math.Max(at, m.Time + 0.2);
                at = Math.Max(at, m.Time + Reaction);
                _plan.Add((Move.Hit, at, at, Mathf.Max(1.8f, HitSpeed + HitSpeedJitter * Gauss())));
            }
            else
            {
                double from = Math.Max(m.Time + Reaction, c.Time - PoseLead + Rand(-PoseLeadJitter, PoseLeadJitter));
                _plan.Add((move, from, c.Time + PoseHold, 0f));
            }
        }

        double _defendHit = double.NaN;

        /// <summary>
        /// An attack set up at its team: it reads the hitter's look as a person would (it shows from the set, Easy's with a
        /// "!"; a disguised tip looks like a smash until <see cref="Threat.TellAt"/>), and shows the block or the receive
        /// once it has reacted, holding it through the hit; switching when a disguised tip shows if it is quick enough. With
        /// nothing to read (the other player's attack) it guesses.
        /// </summary>
        void PlanDefence(VolleyMatch m)
        {
            var t = m.Coming;
            if (!t.Active || t.Defender != Slot || Math.Abs(t.HitTime - _defendHit) < 1e-6) return;
            _defendHit = t.HitTime;
            if (_rng.NextDouble() < MissChance) return;
            // the pose must have been seen three times by the moment of the choice
            double readyBy = t.DecideAt - 3 * SampleEvery - 0.005;
            double first = m.Time + Reaction, until = t.HitTime + 0.3;
            if (AlwaysDefend.HasValue)
            {
                Defend(AlwaysDefend.Value, first, until);
                return;
            }
            if (t.Kind == Attack.None)
            {
                Defend(_rng.NextDouble() < GuessBlock ? Move.Set : Move.Bump, first, until);
                return;
            }
            bool honest = t.TellAt <= m.Time + 1e-6;
            float skill = m.Level.ShowTell ? 1f - (1f - ReadSkill) / 3f : ReadSkill;
            var looks = honest ? t.Kind : Attack.Smash;
            var choice = Read(looks, skill);
            if (first > readyBy) choice = _rng.NextDouble() < GuessBlock ? Move.Set : Move.Bump;
            if (!honest)
            {
                // a disguised tip: the hand comes forward late; switching to the receive if it reacts in time
                double second = t.TellAt + Reaction;
                if (second <= readyBy && _rng.NextDouble() < skill)
                {
                    Defend(choice, first, second - 0.001);
                    Defend(Move.Bump, second, until);
                    return;
                }
            }
            Defend(choice, first, until);
        }

        Move Read(Attack looks, float skill)
        {
            var right = looks == Attack.Smash ? Move.Set : Move.Bump;
            return _rng.NextDouble() < skill ? right : right == Move.Set ? Move.Bump : Move.Set;
        }

        void Defend(Move pose, double from, double to)
        {
            if (pose == Move.Set) Blocks++;
            else Receives++;
            _plan.Add((pose, from, to, 0f));
        }

        void Sample(double t)
        {
            if (HoldsBump || HoldsBlock)
            {
                _queue.Add((t + Lag, HoldsBump ? Move.Bump : Move.Set, t, 0f));
                return;
            }
            for (int i = _plan.Count - 1; i >= 0; i--)
            {
                var p = _plan[i];
                if (p.move == Move.Hit)
                {
                    if (p.from <= t)
                    {
                        _queue.Add((p.from + Lag + HitDetect, Move.Hit, p.from, p.speed));
                        _plan.RemoveAt(i);
                    }
                    continue;
                }
                if (t >= p.from && t <= p.to) _queue.Add((t + Lag, p.move, t, 0f));
                if (t > p.to) _plan.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------------------------------------ profiles

        public static VolleySim Perfect(int seed, int slot = 0) => new VolleySim(seed, slot)
        {
            Name = "perfect", HitBias = -0.08f, HitError = 0.03f, HitSpeed = 8.5f, HitSpeedJitter = 0.6f, PoseLeadJitter = 0.05f, Reaction = 0.2f,
            ReadSkill = 1f,
        };

        public static VolleySim Good(int seed, int slot = 0) => new VolleySim(seed, slot)
        {
            Name = "good", HitBias = -0.05f, HitError = 0.09f, HitSpeed = 6.5f, HitSpeedJitter = 1.5f, MissChance = 0.02f, Reaction = 0.3f,
            ReadSkill = 0.92f,
        };

        /// <summary>A child: later and less sure, slower hands, now and then no move or the wrong one, reads the hitter
        /// less well.</summary>
        public static VolleySim Child(int seed, int slot = 0) => new VolleySim(seed, slot)
        {
            Name = "child", HitBias = 0.02f, HitError = 0.16f, HitSpeed = 4f, HitSpeedJitter = 1.5f, PoseLead = 0.45f, PoseLeadJitter = 0.3f,
            PoseHold = 0.2f, MissChance = 0.07f, WrongChance = 0.06f, Reaction = 0.45f, ReadSkill = 0.75f, GuessBlock = 0.6f,
        };

        /// <summary>A careful child: as late and as slow as the child, but makes a move for every ball (Tenis!'s "careful
        /// child" who swings at every ball) and watches the hitter.</summary>
        public static VolleySim CarefulChild(int seed, int slot = 0) => new VolleySim(seed, slot)
        {
            Name = "careful child", HitBias = 0.02f, HitError = 0.16f, HitSpeed = 4f, HitSpeedJitter = 1.5f, PoseLead = 0.45f, PoseLeadJitter = 0.3f,
            PoseHold = 0.2f, WrongChance = 0.02f, Reaction = 0.45f, ReadSkill = 0.85f, GuessBlock = 0.6f,
        };

        public static VolleySim Struggling(int seed, int slot = 0) => new VolleySim(seed, slot)
        {
            Name = "struggling", HitBias = 0.1f, HitError = 0.25f, HitSpeed = 3f, HitSpeedJitter = 1.2f, PoseLead = 0.3f, PoseLeadJitter = 0.3f,
            PoseHold = 0.1f, MissChance = 0.18f, WrongChance = 0.15f, Reaction = 0.55f, ReadSkill = 0.6f,
        };

        public static VolleySim StandingStill(int seed, int slot = 0) => new VolleySim(seed, slot) { Name = "still", Still = true };

        /// <summary>A good player who always defends the same way without watching the hitter: the hands together low (the
        /// user's 2026-11-08 trick: "keep the hands together before the ball is shot") or both hands up.</summary>
        public static VolleySim AlwaysReceiving(int seed, int slot = 0)
        {
            var s = Good(seed, slot);
            s.Name = "always low";
            s.AlwaysDefend = Move.Bump;
            return s;
        }

        public static VolleySim AlwaysBlocking(int seed, int slot = 0)
        {
            var s = Good(seed, slot);
            s.Name = "always block";
            s.AlwaysDefend = Move.Set;
            return s;
        }

        /// <summary>The same player seen by the TV box sharing its pose AI between two people: half the samples, a little
        /// later.</summary>
        public VolleySim SharingTheCamera()
        {
            SampleEvery = 1f / 15f;
            Lag += 0.05f;
            return this;
        }
    }
}
