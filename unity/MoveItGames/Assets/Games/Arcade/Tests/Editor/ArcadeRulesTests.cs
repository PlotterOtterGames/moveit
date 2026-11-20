using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Arcade.Tests
{
    /// <summary>Godit!, Fruta! and Kap! with simulated players (ArcadeSim: the camera's lag, noise, a child's speed).</summary>
    public class ArcadeRulesTests
    {
        static readonly Vector2 RestLeft = new Vector2(-0.52f, -0.24f), RestRight = new Vector2(0.52f, -0.24f);

        // ------------------------------------------------------------------------------------------ Godit! (whack)

        static Action<float, int, SimBody> WhackBrain(WhackRules rules, Profile prof, int seed)
        {
            var rng = new System.Random(seed);
            var noticed = new Dictionary<int, float>();
            var skipped = new HashSet<int>();
            var ready = prof == Profile.Child ? 0.4f : 1.0f;   // a child lets the hands drop between moles
            return (t, p, body) =>
            {
                float step = prof.HandSpeed * Sim.Dt;
                WhackRules.Critter target = null;
                foreach (var c in rules.Boards[p].Critters)
                {
                    if (c.State is not (WhackRules.State.Rising or WhackRules.State.Up) || c.Kind == WhackRules.Kind.Hedgehog) continue;
                    if (!noticed.ContainsKey(c.Id))
                    {
                        noticed[c.Id] = t;
                        if (rng.NextDouble() < prof.Skip) skipped.Add(c.Id);
                    }
                    if (skipped.Contains(c.Id) || t - noticed[c.Id] < prof.Reaction) continue;
                    if (target == null || c.Id < target.Id) target = c;
                }
                var waitL = new Vector2(-0.75f, ready);
                var waitR = new Vector2(0.75f, ready);
                if (target != null)
                {
                    var hole = WhackRules.Holes[target.Hole];
                    bool left = hole.x < 0f || hole.x == 0f && Vector2.Distance(body.LeftHand, hole) < Vector2.Distance(body.RightHand, hole);
                    if (left)
                    {
                        body.LeftHand = SimBody.Toward(body.LeftHand, hole, step);
                        body.RightHand = SimBody.Toward(body.RightHand, waitR, step);
                    }
                    else
                    {
                        body.RightHand = SimBody.Toward(body.RightHand, hole, step);
                        body.LeftHand = SimBody.Toward(body.LeftHand, waitL, step);
                    }
                }
                else
                {
                    body.LeftHand = SimBody.Toward(body.LeftHand, waitL, step);
                    body.RightHand = SimBody.Toward(body.RightHand, waitR, step);
                }
            };
        }

        static int PlayWhack(Profile prof, int seed = 7, int players = 1)
        {
            var rules = new WhackRules(players, 60f, seed);
            Sim.Run(rules, new[] { prof, prof }, WhackBrain(rules, prof, seed + 1), seed);
            var b = rules.Boards[0];
            System.Console.WriteLine($"whack {prof.Name}: {rules.Score[0]} points, {b.Hits} hits ({b.Golds} gold), {b.Escapes} escaped, " +
                                  $"{b.Hedgehogs} hedgehogs, best combo {b.BestCombo}");
            return rules.Score[0];
        }

        [Test]
        public void Whack_Balance_PerfectToIdle()
        {
            int perfect = PlayWhack(Profile.Perfect), good = PlayWhack(Profile.Good), child = PlayWhack(Profile.Child), idle = PlayWhack(Profile.Idle);
            Assert.Greater(perfect, good - 40, "a perfect player scores at least as a good one");
            Assert.Greater(good, child, "a good player beats a child");
            Assert.Greater(child, idle, "a child beats standing still");
            Assert.AreEqual(0, idle, "standing still bonks nothing (no hole by the resting hands)");
            Assert.AreEqual(3, ArcadeProgress.Stars(Machine.Whack, perfect), "a perfect player gets three stars");
            Assert.GreaterOrEqual(ArcadeProgress.Stars(Machine.Whack, child), 1, "a child gets a star or more");
        }

        [Test]
        public void Whack_AHandLeftInAHole_DoesntBonkEveryMole_AMovingOneDoes()
        {
            var rules = new WhackRules(1, 60f, 3);
            var body = StageBody.Standing();
            body[Part.RightHand] = WhackRules.Holes[4];   // camping on the right hole at the shoulder
            int frames = 0;
            while (!rules.Finished && frames++ < 1800)
            {
                rules.Events.Clear();
                rules.Step(Sim.Dt, new[] { body });
            }
            var b = rules.Boards[0];
            System.Console.WriteLine($"camping: {b.Hits} hits of {b.Hits + b.Escapes}");
            Assert.AreEqual(0, b.Hits, "a hand held still in a hole never bonks");

            // a hand moving through the hole bonks even if it was there when the mole came up
            var r2 = new WhackRules(1, 60f, 3);
            while (r2.Boards[0].Critters.Count == 0) r2.Step(Sim.Dt, new[] { body });
            var critter = r2.Boards[0].Critters[0];
            var hole = WhackRules.Holes[critter.Hole];
            var moving = StageBody.Standing();
            var seen = new List<ArcadeEvent>();
            r2.Events.Clear();
            moving[Part.RightHand] = hole + new Vector2(0.2f, 0f);
            r2.Step(Sim.Dt, new[] { moving });
            seen.AddRange(r2.Events);
            r2.Events.Clear();
            moving[Part.RightHand] = hole - new Vector2(0.1f, 0f);
            r2.Step(Sim.Dt, new[] { moving });
            seen.AddRange(r2.Events);
            Assert.IsTrue(critter.Kind == WhackRules.Kind.Hedgehog || seen.Exists(e => e.Kind is EventKind.Hit or EventKind.Gold),
                          "a hand moving through the hole bonks");
        }

        [Test]
        public void Whack_AOneFrameGlitchIntoAHole_IsNotABonk()
        {
            var rules = new WhackRules(1, 60f, 5);
            var body = StageBody.Standing();
            while (rules.Boards[0].Critters.Count == 0) rules.Step(Sim.Dt, new[] { body });
            var critter = rules.Boards[0].Critters[0];
            var glitch = StageBody.Standing();
            glitch[Part.LeftHand] = WhackRules.Holes[critter.Hole];   // the wrist "teleports" more than a torso length
            rules.Events.Clear();
            rules.Step(Sim.Dt, new[] { glitch });
            rules.Step(Sim.Dt, new[] { body });
            Assert.IsFalse(rules.Events.Exists(e => e.Kind is EventKind.Hit or EventKind.Gold or EventKind.Bad), "a glitch never bonks");
        }

        [Test]
        public void Whack_TwoPlayers_GetTheSameMoles()
        {
            var rules = new WhackRules(2, 30f, 11);
            var bodies = new[] { StageBody.Standing(), StageBody.Standing() };
            var holes = new List<int>[] { new List<int>(), new List<int>() };
            while (!rules.Finished)
            {
                rules.Events.Clear();
                rules.Step(Sim.Dt, bodies);
                foreach (var e in rules.Events)
                    if (e.Kind == EventKind.Spawn) holes[e.Player].Add(Array.IndexOf(WhackRules.Holes, e.At) * 10 + e.Variant);
            }
            Assert.Greater(holes[0].Count, 15);
            CollectionAssert.AreEqual(holes[0], holes[1], "both players' moles come out of the same holes, in the same order");
        }

        // ------------------------------------------------------------------------------------------ Fruta! (slice)

        static Action<float, int, SimBody> SliceBrain(SliceRules rules, Profile prof, int seed, bool careful)
        {
            var rng = new System.Random(seed);
            var noticed = new Dictionary<int, float>();
            var skipped = new HashSet<int>();
            return (t, p, body) =>
            {
                float step = prof.HandSpeed * Sim.Dt;
                var board = rules.Boards[p];
                SliceRules.Fruit pick = null;
                foreach (var f in board.Fruit)
                {
                    if (f.Sliced || f.Kind == SliceRules.FruitKind.Bomb || f.Pos.y < -0.2f || f.Pos.y > 2.5f) continue;
                    if (!noticed.ContainsKey(f.Id))
                    {
                        noticed[f.Id] = t;
                        if (rng.NextDouble() < prof.Skip) skipped.Add(f.Id);
                    }
                    if (skipped.Contains(f.Id) || t - noticed[f.Id] < prof.Reaction) continue;
                    if (careful && board.Fruit.Exists(o => o.Kind == SliceRules.FruitKind.Bomb && !o.Sliced && Vector2.Distance(o.Pos, f.Pos) < 0.7f)) continue;
                    if (pick == null || f.Pos.y < pick.Pos.y) pick = f;
                }
                if (pick == null)
                {
                    body.LeftHand = SimBody.Toward(body.LeftHand, new Vector2(-0.8f, 0.8f), step);
                    body.RightHand = SimBody.Toward(body.RightHand, new Vector2(0.8f, 0.8f), step);
                    return;
                }
                // chase it with the nearer hand, through it and a little past (a swipe)
                bool left = pick.Pos.x < 0f;
                var target = pick.Pos + pick.Vel * 0.08f;
                if (left) body.LeftHand = SimBody.Toward(body.LeftHand, target + (target - body.LeftHand).normalized * 0.3f, step);
                else body.RightHand = SimBody.Toward(body.RightHand, target + (target - body.RightHand).normalized * 0.3f, step);
            };
        }

        static (int score, SliceRules rules) PlaySlice(Profile prof, bool careful = true, int seed = 9)
        {
            var rules = new SliceRules(1, 60f, seed);
            Sim.Run(rules, new[] { prof }, SliceBrain(rules, prof, seed + 1, careful), seed);
            var b = rules.Boards[0];
            System.Console.WriteLine($"slice {prof.Name}{(careful ? "" : " (careless)")}: {rules.Score[0]} points, {b.Sliced} sliced ({b.Stars} stars), " +
                                  $"{b.Dropped} dropped, {b.Bombs} bombs, {b.Combos} combos, best streak {b.BestStreak}");
            return (rules.Score[0], rules);
        }

        [Test]
        public void Slice_Balance_PerfectToIdle()
        {
            int perfect = PlaySlice(Profile.Perfect).score, good = PlaySlice(Profile.Good).score;
            int child = PlaySlice(Profile.Child, careful: false).score, idle = PlaySlice(Profile.Idle).score;
            Assert.Greater(perfect, child);
            Assert.Greater(good, child);
            Assert.Greater(child, idle);
            Assert.Less(idle, 40, "standing still slices next to nothing (a fruit flying into a jittering hand)");
            Assert.AreEqual(3, ArcadeProgress.Stars(Machine.Slice, perfect));
            Assert.GreaterOrEqual(ArcadeProgress.Stars(Machine.Slice, child), 1);
        }

        [Test]
        public void Slice_ASlowHand_DoesntSlice_AQuickOneDoes_ThreeInOneSwipeIsACombo()
        {
            var rules = new SliceRules(1, 60f, 1);
            var board = rules.Boards[0];
            rules.Step(Sim.Dt, new StageBody[1]);
            board.Fruit.Clear();
            board.NextWave = 99f;
            for (int i = 0; i < 3; i++)
                board.Fruit.Add(new SliceRules.Fruit { Id = 100 + i, Kind = SliceRules.FruitKind.Apple, Radius = 0.3f, Pos = new Vector2(-0.6f + 0.6f * i, 1.6f), Vel = new Vector2(0f, 0f) });
            var body = StageBody.Standing();
            // slowly through the first one: nothing (0.5 torso lengths a second)
            for (float x = -1.2f; x < -0.2f; x += 0.5f * Sim.Dt)
            {
                body[Part.RightHand] = new Vector2(x, 1.6f);
                foreach (var f in board.Fruit) f.Vel = Vector2.zero;
                rules.Step(Sim.Dt, new[] { body });
                foreach (var f in board.Fruit) f.Pos.y = 1.6f;
            }
            Assert.IsFalse(board.Fruit.Exists(f => f.Sliced), "a slow hand slices nothing");
            // a quick swipe through all three: sliced, and a combo once the hand stops
            rules.Events.Clear();
            var events = new List<ArcadeEvent>();
            for (float x = -1.2f; x <= 1.4f; x += 6f * Sim.Dt)
            {
                body[Part.RightHand] = new Vector2(x, 1.6f);
                rules.Events.Clear();
                rules.Step(Sim.Dt, new[] { body });
                foreach (var f in board.Fruit) f.Pos.y = 1.6f;
                events.AddRange(rules.Events);
            }
            for (int i = 0; i < 10; i++)
            {
                rules.Events.Clear();
                rules.Step(Sim.Dt, new[] { body });
                events.AddRange(rules.Events);
            }
            Assert.AreEqual(3, events.Count(e => e.Kind == EventKind.Slice), "a quick swipe slices all three");
            Assert.IsTrue(events.Exists(e => e.Kind == EventKind.Combo && e.Combo == 3), "three in one swipe: a combo");
        }

        [Test]
        public void Slice_ABomb_CostsPoints_AGlitchFrameThroughItDoesnt()
        {
            var rules = new SliceRules(1, 60f, 1);
            var board = rules.Boards[0];
            rules.Step(Sim.Dt, new StageBody[1]);
            board.Fruit.Clear();
            board.NextWave = 99f;
            var bomb = new SliceRules.Fruit { Id = 7, Kind = SliceRules.FruitKind.Bomb, Radius = 0.3f, Pos = new Vector2(0f, 1.6f) };
            board.Fruit.Add(bomb);
            var body = StageBody.Standing();
            body[Part.RightHand] = new Vector2(0.6f, 1.6f);
            rules.Step(Sim.Dt, new[] { body });
            // one frame: the wrist jumps across the bomb (a glitch), then back
            body[Part.RightHand] = new Vector2(-0.4f, 1.6f);
            bomb.Vel = Vector2.zero;
            rules.Step(Sim.Dt, new[] { body });
            bomb.Pos = new Vector2(0f, 1.6f);
            body[Part.RightHand] = new Vector2(0.6f, 1.6f);
            rules.Step(Sim.Dt, new[] { body });
            bomb.Pos = new Vector2(0f, 1.6f);
            Assert.IsFalse(bomb.Sliced, "one glitched frame never sets a bomb off");
            // a real swipe through it does
            rules.Score[0] = 50;
            for (float x = 1.0f; x >= -1.0f; x -= 6f * Sim.Dt)
            {
                body[Part.RightHand] = new Vector2(x, 1.6f);
                bomb.Vel = Vector2.zero;
                rules.Step(Sim.Dt, new[] { body });
                bomb.Pos = new Vector2(0f, 1.6f);
                if (bomb.Sliced) break;
            }
            Assert.IsTrue(bomb.Sliced, "a swipe through the bomb sets it off");
            Assert.AreEqual(50 + SliceRules.BombPoints, rules.Score[0]);
            Assert.Greater(board.Dazed, 0f, "dazed for a moment");
        }

        // ------------------------------------------------------------------------------------------ Kap! (catch)

        static Action<float, int, SimBody> CatchBrain(CatchRules rules, Profile prof, int seed, bool avoidBombs)
        {
            var rng = new System.Random(seed);
            var skipped = new HashSet<int>();
            var noticed = new Dictionary<int, float>();
            return (t, p, body) =>
            {
                var board = rules.Boards[p];
                float x = body.Hips.x, best = float.MaxValue, goal = x;
                foreach (var it in board.Items)
                {
                    if (it.Done || it.Kind is CatchRules.ItemKind.Bomb or CatchRules.ItemKind.Balloon || it.Pos.y < CatchRules.RimY) continue;
                    if (!noticed.ContainsKey(it.Id))
                    {
                        noticed[it.Id] = t;
                        if (rng.NextDouble() < prof.Skip) skipped.Add(it.Id);
                    }
                    if (skipped.Contains(it.Id) || t - noticed[it.Id] < prof.Reaction) continue;
                    float arrive = (it.Pos.y - CatchRules.RimY) / Mathf.Max(0.1f, it.Speed);
                    float need = Mathf.Abs(it.Pos.x - x) / Mathf.Max(0.1f, prof.StepSpeed);
                    if (need > arrive + 0.15f) continue;     // can't get there
                    if (arrive < best)
                    {
                        best = arrive;
                        goal = it.Pos.x;
                    }
                }
                if (avoidBombs)
                    foreach (var it in board.Items)
                    {
                        if (it.Done || it.Kind != CatchRules.ItemKind.Bomb) continue;
                        float arrive = (it.Pos.y - CatchRules.RimY) / it.Speed;
                        if (arrive > 0f && arrive < 0.6f && Mathf.Abs(it.Pos.x - goal) < CatchRules.CatchHalf + 0.15f)
                            goal = it.Pos.x + Mathf.Sign(goal - it.Pos.x + 0.001f) * (CatchRules.CatchHalf + 0.3f);
                    }
                body.Hips.x = Mathf.MoveTowards(body.Hips.x, goal, prof.StepSpeed * Sim.Dt);
                // a balloon at head height: reach for it
                var balloon = board.Items.Find(i => !i.Done && i.Kind == CatchRules.ItemKind.Balloon && Mathf.Abs(i.Pos.x - body.Hips.x) < 1.2f);
                float step = prof.HandSpeed * Sim.Dt;
                if (balloon != null && prof != Profile.Idle)
                {
                    if (balloon.Pos.x < body.Hips.x) body.LeftHand = SimBody.Toward(body.LeftHand, balloon.Pos, step);
                    else body.RightHand = SimBody.Toward(body.RightHand, balloon.Pos, step);
                }
                else
                {
                    body.LeftHand = SimBody.Toward(body.LeftHand, body.Hips + RestLeft, step + prof.StepSpeed * Sim.Dt);
                    body.RightHand = SimBody.Toward(body.RightHand, body.Hips + RestRight, step + prof.StepSpeed * Sim.Dt);
                }
            };
        }

        static int PlayCatch(Profile prof, bool avoid = true, int seed = 13)
        {
            var rules = new CatchRules(1, 60f, seed);
            Sim.Run(rules, new[] { prof }, CatchBrain(rules, prof, seed + 1, avoid), seed);
            var b = rules.Boards[0];
            System.Console.WriteLine($"catch {prof.Name}: {rules.Score[0]} points, {b.Caught} caught, {b.Missed} missed, {b.Bombs} bombs, " +
                                  $"{b.Balloons} balloons, best combo {b.BestCombo}");
            return rules.Score[0];
        }

        [Test]
        public void Catch_Balance_PerfectToIdle()
        {
            int perfect = PlayCatch(Profile.Perfect), good = PlayCatch(Profile.Good), child = PlayCatch(Profile.Child, avoid: false), idle = PlayCatch(Profile.Idle);
            Assert.Greater(perfect, child);
            Assert.Greater(good, child);
            Assert.Greater(child, idle);
            Assert.AreEqual(3, ArcadeProgress.Stars(Machine.Catch, perfect));
            Assert.GreaterOrEqual(ArcadeProgress.Stars(Machine.Catch, child), 1);
            Assert.LessOrEqual(ArcadeProgress.Stars(Machine.Catch, idle), 1, "standing still catches only what falls into the basket");
        }

        [Test]
        public void Catch_TheBasketFollowsTheHips_ABombShakesIt_ABalloonPopsAtAHand()
        {
            var rules = new CatchRules(1, 60f, 2);
            var board = rules.Boards[0];
            var body = StageBody.Standing(new Vector2(1.1f, 0f));
            rules.Step(Sim.Dt, new[] { body });
            Assert.AreEqual(1.1f, board.BasketX, 0.01f, "the basket is at the hips");
            board.Items.Clear();
            board.NextDrop = board.NextBalloon = 99f;
            board.Items.Add(new CatchRules.Item { Id = 1, Kind = CatchRules.ItemKind.Bomb, Pos = new Vector2(1.2f, CatchRules.RimY + 0.05f), Speed = 2f });
            rules.Score[0] = 40;
            rules.Events.Clear();
            for (int i = 0; i < 5; i++) rules.Step(Sim.Dt, new[] { body });
            Assert.AreEqual(40 + CatchRules.BombPoints, rules.Score[0], "a bomb in the basket costs points");
            Assert.Greater(board.Shaking, 0f);
            // the next candy into the shaking basket isn't caught
            board.Items.Add(new CatchRules.Item { Id = 2, Kind = CatchRules.ItemKind.Candy, Pos = new Vector2(1.1f, CatchRules.RimY + 0.02f), Speed = 2f });
            rules.Step(Sim.Dt, new[] { body });
            Assert.IsFalse(board.Items.Find(i => i.Id == 2).Caught);
            // a balloon at a raised hand pops
            var balloon = new CatchRules.Item { Id = 3, Kind = CatchRules.ItemKind.Balloon, Pos = new Vector2(1.5f, 2.0f), Drift = 0f };
            board.Items.Add(balloon);
            body[Part.RightHand] = new Vector2(1.45f, 1.95f);
            rules.Step(Sim.Dt, new[] { body });
            Assert.IsTrue(balloon.Done && balloon.Caught, "a raised hand pops the balloon");
        }
    }
}
