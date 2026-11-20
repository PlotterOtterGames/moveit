using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Boxing.Tests
{
    /// <summary>
    /// Simulated players for the fight rules (2026-11-19 overhaul: Wii's loop, an opponent that decides, its punches
    /// physical): each sees a wind-up after its reaction time, waits some of the tell (a good player waits for the punch to
    /// commit, a child moves at once), then moves its head out of the punch's way (a lean, a duck, a step: as far and as
    /// fast as it can) or raises its gloves (a body blow must be blocked, an uppercut dodged), holds it past the impact, and
    /// comes back; it punches into the opponent's openings (the body against a high guard, the head against a low one, an
    /// uppercut into its cover; into a stagger), all through the camera's lag.
    /// </summary>
    public static class BoutSim
    {
        public sealed class Player
        {
            public string Name = "";
            /// <summary>From a wind-up's start to acting on it (s).</summary>
            public float Reaction = 0.3f;
            /// <summary>Chance to pick a way out that works for the punch it sees (else a guess).</summary>
            public float Read = 0.9f;
            /// <summary>Chance to defend at all.</summary>
            public float Defends = 1f;
            /// <summary>Against an ordinary punch the gloves stop: the guard instead of a dodge.</summary>
            public float GuardShare = 0.45f;
            /// <summary>How much of the rest of the tell it waits before moving (0 = at once, 1 = to its very end).</summary>
            public float Patience = 0.5f;
            /// <summary>How far it moves its head (m) and how fast (m/s); camera noise on it (m).</summary>
            public float Move = 0.22f, MoveSpeed = 1.8f, Noise = 0.008f;
            /// <summary>Holds the dodge this long past the impact (s).</summary>
            public float Hold = 0.3f;
            /// <summary>Punches a second when it punches; punching when nothing is open (a share of the time).</summary>
            public float Rate = 2.6f, Careless = 0.2f;
            /// <summary>Punching where the gloves aren't.</summary>
            public float Aim = 0.7f;
            /// <summary>Punching right after a dodge or a block (into the stagger).</summary>
            public float Counter = 0.6f;
            /// <summary>The camera's lag (s).</summary>
            public float Lag = 0.13f, LagJitter = 0.03f;
            public bool UsesSuper = true;
            /// <summary>Punches a second getting up.</summary>
            public float GetUpRate = 3f;
            public bool Turtle, Masher, Still;
            public override string ToString() => Name;
        }

        public static readonly Player Perfect = new Player { Name = "perfect", Reaction = 0.18f, Read = 1f, GuardShare = 0.3f, Patience = 0.75f, Move = 0.26f, MoveSpeed = 2.2f, Rate = 2.8f, Careless = 0f, Aim = 1f, Counter = 0.9f, GetUpRate = 5f };
        public static readonly Player Good = new Player { Name = "good", Reaction = 0.3f, Read = 0.9f, GuardShare = 0.45f, Patience = 0.55f, Move = 0.22f, MoveSpeed = 1.8f, Rate = 2.2f, Careless = 0.2f, Aim = 0.7f, Counter = 0.6f };
        public static readonly Player CarefulChild = new Player { Name = "careful child", Reaction = 0.42f, Read = 0.8f, GuardShare = 0.6f, Patience = 0.3f, Move = 0.2f, MoveSpeed = 1.4f, Rate = 1.7f, Careless = 0.3f, Aim = 0.4f, Counter = 0.4f };
        public static readonly Player Child = new Player { Name = "child", Reaction = 0.55f, Read = 0.65f, Defends = 0.8f, GuardShare = 0.5f, Patience = 0.1f, Move = 0.18f, MoveSpeed = 1.2f, Rate = 1.9f, Careless = 0.6f, Aim = 0.25f, Counter = 0.3f, GetUpRate = 2.6f };
        public static readonly Player Struggling = new Player { Name = "struggling", Reaction = 0.7f, Read = 0.45f, Defends = 0.6f, GuardShare = 0.5f, Patience = 0f, Move = 0.15f, MoveSpeed = 1f, Rate = 1.3f, Careless = 0.7f, Aim = 0.1f, Counter = 0.15f, GetUpRate = 2.2f };
        public static readonly Player Masher = new Player { Name = "masher", Masher = true, Rate = 3.2f, Defends = 0f, Careless = 1f, Aim = 0f, Counter = 0f, GetUpRate = 4f };
        public static readonly Player Turtle = new Player { Name = "turtle", Turtle = true, Rate = 0f, UsesSuper = false };
        public static readonly Player Still = new Player { Name = "still", Still = true, Rate = 0f, Defends = 0f, GetUpRate = 0f, UsesSuper = false };

        public static readonly Player[] Players = { Perfect, Good, CarefulChild, Child, Struggling, Masher, Turtle, Still };

        public struct Result
        {
            public bool Won;
            public Ending Ending;
            public float Seconds;
            public int Stars, HeroDowns, FoeDowns, Thrown, Landed, Dodges, Blocks, HitsTaken, Supers, Counters;
            public int FoeThrown, FoeHead, FoeBody, FoeDefended, FoeCounters, CoversBroken;
            public override string ToString() =>
                $"{(Won ? "won" : "lost")} {Ending} {Seconds:0}s stars {Stars} downs {HeroDowns}/{FoeDowns} punches {Landed}/{Thrown} " +
                $"dodges {Dodges} blocks {Blocks} hits {HitsTaken} supers {Supers} | foe {FoeThrown} (body {FoeBody}) defended {FoeDefended} " +
                $"counters {FoeCounters} covers broken {CoversBroken}";
        }

        public static Result Run(BoxingLevel level, Player player, int seed, Action<Bout> each = null)
        {
            var bout = new Bout(level, seed);
            var rng = new System.Random(seed * 7919 + 13);
            const float dt = 1f / 30f;
            // what the player did, with its match time, waiting for the camera's lag
            var pending = new Queue<(double arrive, double time, HeroPose pose, bool punch, bool right, Blow blow, bool body)>();
            // the defence planned against the punch coming: from when, until when, which pose
            double defendFrom = double.MaxValue, defendUntil = double.MinValue;
            HeroPose defence = default;
            Vector2 head = Vector2.zero;
            float bodyX = 0f;
            double nextPunch = 0, superUntil = double.MinValue, counterUntil = double.MinValue;
            bool right = true;
            int seen = 0;
            while (bout.Phase != BoutPhase.Over && bout.Time < 1200)
            {
                double now = bout.Time;
                // what happened: a new wind-up (or a fireball) to react to, its punch over (counter into the stagger)
                for (; seen < bout.Events.Count; seen++)
                {
                    var e = bout.Events[seen];
                    if (e.Kind == BoutEventKind.WindUp || e.Kind == BoutEventKind.FireballThrown)
                    {
                        if (player.Still || player.Masher || player.Turtle || rng.NextDouble() >= player.Defends) continue;
                        bool fireball = e.Kind == BoutEventKind.FireballThrown;
                        var a = fireball ? new PlannedAttack { Attack = Attack.Fireball } : bout.Attack;
                        float rest = fireball ? level.FireballFlight : bout.StateLength + level.StrikeTime;
                        defence = Choose(a, player, rng);
                        double start = now + player.Reaction;
                        double end = now + rest;
                        double wait = Math.Max(0.0, (end - 0.12 - start) * player.Patience);
                        defendFrom = start + wait + Gauss(rng, 0.04f);
                        defendUntil = end + player.Hold + MaxWaitPad;
                    }
                    else if (e.Kind == BoutEventKind.Impact && e.Outcome != Outcome.Hit)
                        counterUntil = now + 0.7;
                }
                bout.Events.Clear();
                seen = 0;

                // the pose this moment: the head moving toward the defence (or back to the middle)
                bool defending = now >= defendFrom && now <= defendUntil;
                var want = defending ? defence : default;
                if (player.Turtle) want = new HeroPose { Guard = true };
                if (bout.Power >= 1f && player.UsesSuper && !player.Still && bout.State is Foe.Ready or Foe.Recover or Foe.Staggered && !defending)
                    superUntil = now + 0.35;
                float step = player.MoveSpeed * dt;
                head = Vector2.MoveTowards(head, want.Head, step);
                bodyX = Mathf.MoveTowards(bodyX, want.BodyX, step);
                var pose = new HeroPose
                {
                    Guard = want.Guard, Lean = Math.Abs(head.x) > 0.1f ? Math.Sign(head.x) : 0, Duck = -head.y > 0.12f,
                    HeadX = head.x + Gauss(rng, player.Noise), HeadDrop = -head.y + Gauss(rng, player.Noise), BodyX = bodyX,
                    ArmsUp = now < superUntil,
                };
                double lag = Math.Max(0.03, player.Lag + Gauss(rng, player.LagJitter));
                pending.Enqueue((now + lag, now, pose, false, false, Blow.Straight, false));

                // punches
                if (!player.Still && !player.Turtle && now >= nextPunch && player.Rate > 0f)
                {
                    bool down = bout.Phase == BoutPhase.Down && bout.DownFighter == 0;
                    bool fighting = bout.Phase == BoutPhase.Fighting && bout.Hero == HeroState.Fighting;
                    if (down)
                    {
                        if (player.GetUpRate > 0f)
                        {
                            pending.Enqueue((now + lag, now, pose, true, right, Blow.Straight, false));
                            right = !right;
                            nextPunch = now + 1.0 / player.GetUpRate * (0.8 + 0.4 * rng.NextDouble());
                        }
                    }
                    else if (fighting && (player.Masher || !defending) && now >= superUntil)
                    {
                        bool open = bout.State is Foe.Recover or Foe.Staggered or Foe.Dizzy or Foe.Taunt or Foe.Hurt or Foe.Throwing
                                    || bout.State is Foe.Ready or Foe.Blocking or Foe.WindUp && bout.Guard == Guard.Open;
                        bool aimed = rng.NextDouble() < player.Aim;
                        // a player who reads the guard punches where the gloves aren't (an uppercut into the cover)
                        bool target = aimed && (bout.State is Foe.Covering or Foe.Ready or Foe.Blocking or Foe.WindUp);
                        bool counter = now < counterUntil && rng.NextDouble() < player.Counter * dt * 12;
                        if (player.Masher || open || target || counter || rng.NextDouble() < player.Careless * dt * 6)
                        {
                            var blow = Blow.Straight;
                            bool body = false;
                            if (aimed)
                            {
                                if (bout.State == Foe.Covering) blow = Blow.Uppercut;
                                else if (bout.Guard == Guard.High) { body = rng.NextDouble() < 0.6; blow = body ? Blow.Straight : Blow.Uppercut; }
                                else if (bout.Guard == Guard.Low) blow = rng.NextDouble() < 0.7 ? Blow.Straight : Blow.Hook;
                                else blow = rng.NextDouble() < 0.6 ? Blow.Straight : Blow.Hook;
                            }
                            else
                            {
                                double r = rng.NextDouble();
                                blow = r < 0.6 ? Blow.Straight : r < 0.85 ? Blow.Hook : Blow.Uppercut;
                                body = blow != Blow.Uppercut && rng.NextDouble() < 0.15;
                            }
                            pending.Enqueue((now + lag, now, pose, true, right, blow, body));
                            right = !right;
                            nextPunch = now + 1.0 / player.Rate * (0.75 + 0.5 * rng.NextDouble());
                        }
                    }
                }

                // the camera's news arrives
                while (pending.Count > 0 && pending.Peek().arrive <= now)
                {
                    var p = pending.Dequeue();
                    if (p.punch) bout.Punch(p.time, p.right, p.blow, p.body, 3.5f + (float)rng.NextDouble() * 2f);
                    else
                    {
                        bout.Pose(p.time, p.pose);
                        bout.SawUntil(p.time);
                    }
                }
                each?.Invoke(bout);
                bout.Step(dt);
            }
            return new Result
            {
                Won = bout.HeroWon, Ending = bout.Ending, Seconds = (float)bout.Time, Stars = bout.Stars, HeroDowns = bout.HeroKnockdowns,
                FoeDowns = bout.Knockdowns, Thrown = bout.Thrown, Landed = bout.Landed, Dodges = bout.Dodges, Blocks = bout.Blocks,
                HitsTaken = bout.HitsTaken, Supers = bout.Supers, Counters = bout.Counters,
                FoeThrown = bout.FoeThrown, FoeHead = bout.FoeHead, FoeBody = bout.FoeBody, FoeDefended = bout.FoeDefended,
                FoeCounters = bout.FoeCounters, CoversBroken = bout.CoversBroken,
            };
        }

        /// <summary>The impact may wait for the camera: hold the dodge a little longer.</summary>
        const double MaxWaitPad = 0.15;

        /// <summary>The defence a player picks: a way out that works by its reading skill (a body blow: the gloves; an
        /// ordinary punch the gloves stop: the guard or a dodge; an uppercut or a power punch: a dodge), else a guess.</summary>
        static HeroPose Choose(PlannedAttack a, Player player, System.Random rng)
        {
            var centred = a;
            centred.Aim = Vector2.zero;
            if (rng.NextDouble() >= player.Read)
            {
                int guess = rng.Next(4);
                return guess switch
                {
                    0 => new HeroPose { Guard = true },
                    1 => Bout.Escape(new PlannedAttack { Attack = Attack.Jab }, player.Move, 0),
                    2 => Bout.Escape(new PlannedAttack { Attack = Attack.Jab }, player.Move, 1),
                    _ => Bout.Escape(new PlannedAttack { Attack = Attack.Jab }, player.Move, 2),
                };
            }
            if (a.Body) return new HeroPose { Guard = true };
            if (Bout.GuardStops(a.Attack) && rng.NextDouble() < player.GuardShare) return new HeroPose { Guard = true };
            return Bout.Escape(centred, player.Move, rng.Next(6));
        }

        static float Gauss(System.Random rng, float sigma)
        {
            double u = Math.Max(1e-9, rng.NextDouble()), v = rng.NextDouble();
            return (float)(Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v)) * sigma;
        }
    }
}
