using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Boxing.Tests
{
    /// <summary>
    /// Superboks!'s rules (<see cref="Bout"/>) one by one: punches that land where the head is, a guard that stops what it
    /// covers, the cover an uppercut breaks, a dodge's stagger and the counter, an opponent that decides (Bout.Brain.cs:
    /// the opening it sees, the robot's rhythm, the shadow's mirror); then whole fights between the climb's opponents and
    /// simulated players (<see cref="BoutSim"/>: the camera's lag, reaction times, moving the head out of the way).
    /// </summary>
    public sealed class BoutTests
    {
        const float Dt = 1f / 30f;

        /// <summary>A fight under way with no chance in it: the opponent decides nothing (<see cref="Bout.Passive"/>), its
        /// guard open until a test says otherwise, its aim exact, and it attacks only when told.</summary>
        static Bout Fighting(BoxingDifficulty d = BoxingDifficulty.Medium, int fight = 4, int seed = 3)
        {
            var level = BoxingLevel.For(d, fight);
            level.ComboChance = level.DefendSkill = level.CounterRate = level.AimError = 0f;
            var b = new Bout(level, seed);
            b.Begin();
            b.HoldGuard(Guard.Open);
            b.Passive = true;
            return b;
        }

        /// <summary>A fight with the opponent deciding, the hero unbreakable (for watching what it does).</summary>
        static Bout Deciding(BoxingDifficulty d, int fight, int seed = 5, Action<BoxingLevel> tweak = null)
        {
            var level = BoxingLevel.For(d, fight);
            tweak?.Invoke(level);
            var b = new Bout(level, seed);
            b.Begin();
            b.SetHero(1e6f, 0f);
            return b;
        }

        /// <summary>Steps the bout for <paramref name="seconds"/>, the camera telling everything at once (no lag) with the
        /// hero holding <paramref name="pose"/>.</summary>
        static void Run(Bout b, float seconds, HeroPose pose = default)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                b.Pose(b.Time, pose);
                b.SawUntil(b.Time);
                b.Step(Dt);
            }
        }

        static List<BoutEvent> Take(Bout b, BoutEventKind kind) => b.Events.Where(e => e.Kind == kind).ToList();

        static Outcome ImpactOf(Bout b) => Take(b, BoutEventKind.Impact).Single().Outcome;

        /// <summary>Throws <paramref name="attack"/> aimed at the middle (fixed at once) at a hero holding
        /// <paramref name="pose"/> through it.</summary>
        static Outcome Against(Attack attack, HeroPose pose, int side = -1, BoxingDifficulty d = BoxingDifficulty.Medium)
        {
            var b = Fighting(d);
            b.Throw(attack, side, 0, Vector2.zero);
            Run(b, b.Level.TellFor(attack) + b.Level.StrikeTime + 0.6f, pose);
            return ImpactOf(b);
        }

        static HeroPose Head(float x, float drop = 0f, bool guard = false, float bodyX = 0f) =>
            new HeroPose { HeadX = x, HeadDrop = drop, Guard = guard, BodyX = bodyX };

        // ---------------------------------------------------------------- its punches land where the head is

        [Test]
        public void AStraightPunch_HitsAHeadThatStayed_MissesOneThatMovedAway()
        {
            Assert.AreEqual(Outcome.Hit, Against(Attack.Cross, Head(0f)));
            Assert.AreEqual(Outcome.Hit, Against(Attack.Cross, Head(0.06f)), "a little off its line still hits");
            Assert.AreEqual(Outcome.Dodged, Against(Attack.Cross, Head(0.2f)));
            Assert.AreEqual(Outcome.Dodged, Against(Attack.Cross, Head(-0.2f)));
            Assert.AreEqual(Outcome.Dodged, Against(Attack.Jab, Head(0f, 0.2f)), "under it");
            // after a punch into the air it is off balance: open
            var b = Fighting();
            b.Throw(Attack.Cross, 0, 0, Vector2.zero);
            Run(b, b.Level.Tell + b.Level.StrikeTime + 0.1f, Head(0.22f));
            Assert.AreEqual(Foe.Staggered, b.State);
        }

        [Test]
        public void AHook_IsEscapedUnderIt_OrFarAwayFromItsSide_NotByLeaningIntoIt()
        {
            // from the screen's left, sweeping right
            Assert.AreEqual(Outcome.Hit, Against(Attack.Hook, Head(0f), -1));
            Assert.AreEqual(Outcome.Dodged, Against(Attack.Hook, Head(0f, 0.2f), -1), "under it");
            Assert.AreEqual(Outcome.Dodged, Against(Attack.Hook, Head(0.24f), -1), "far away from its side");
            Assert.AreEqual(Outcome.Hit, Against(Attack.Hook, Head(0.1f), -1), "not far enough");
            Assert.AreEqual(Outcome.Hit, Against(Attack.Hook, Head(-0.25f), -1), "leaning into it");
            Assert.AreEqual(Outcome.Dodged, Against(Attack.Hook, Head(-0.24f), 1), "the other side's mirror");
        }

        [Test]
        public void AnUppercut_IsEscapedAside_NotByDucking_NorByTheGuard()
        {
            Assert.AreEqual(Outcome.Dodged, Against(Attack.Uppercut, Head(0.2f)));
            Assert.AreEqual(Outcome.Dodged, Against(Attack.Uppercut, Head(-0.2f)));
            Assert.AreEqual(Outcome.Hit, Against(Attack.Uppercut, Head(0f, 0.25f)), "ducking into it");
            Assert.AreEqual(Outcome.Hit, Against(Attack.Uppercut, Head(0f, 0f, guard: true)), "between the gloves");
        }

        [Test]
        public void TheGuard_BlocksHeadPunchesAndBodyBlows_UppercutsAndPowerPunchesGoThrough()
        {
            var b = Fighting();
            b.Throw(Attack.Cross, 0, 0, Vector2.zero);
            Run(b, 2f, Head(0f, 0f, guard: true));
            var e = Take(b, BoutEventKind.Impact).Single();
            Assert.AreEqual(Outcome.Blocked, e.Outcome);
            Assert.AreEqual(b.Level.DamageOf(Attack.Cross) * b.Level.BlockChip, e.Damage, 1e-3f);

            b = Fighting();
            b.Throw(Attack.Body, 0, 0, Vector2.zero);
            Run(b, 2f, Head(0f, 0f, guard: true));
            Assert.AreEqual(Outcome.Blocked, Take(b, BoutEventKind.Impact).Single().Outcome, "a body blow: block it");

            Assert.AreEqual(Outcome.Hit, Against(Attack.Haymaker, Head(0f, 0f, guard: true)));
            Assert.AreEqual(Outcome.Hit, Against(Attack.Body, Head(0.25f)), "a lean leaves the hips where they were");
            Assert.AreEqual(Outcome.Dodged, Against(Attack.Body, Head(0.25f, 0f, false, 0.25f)), "a step takes them away");
            // easy: nothing gets through the gloves
            b = Fighting(BoxingDifficulty.Easy, 1);
            b.Throw(Attack.Hook, -1, 0, Vector2.zero);
            Run(b, 2f, Head(0f, 0f, guard: true));
            Assert.AreEqual(0f, Take(b, BoutEventKind.Impact).Single().Damage, 1e-4f);
        }

        [Test]
        public void EachPunch_HasItsWayOut_AndHitsAStandingHero()
        {
            foreach (Attack attack in Enum.GetValues(typeof(Attack)))
                foreach (int side in new[] { -1, 1 })
                {
                    var a = new PlannedAttack { Attack = attack, Side = Campaign.FromSide(attack) ? side : 0, Aim = Vector2.zero };
                    if (a.Body) a.Aim = new Vector2(0f, -0.45f);
                    float r = BoxingLevel.For(BoxingDifficulty.Hard, Campaign.Fights - 1).HitRadius;
                    Assert.IsTrue(Bout.Meets(a, Vector2.zero, 0f, r), $"{attack} hits a standing hero");
                    for (int pick = 0; pick < 6; pick++)
                    {
                        var p = Bout.Escape(a, 0.3f, pick);
                        Assert.IsFalse(Bout.Meets(a, p.Head, p.BodyX, r), $"{attack} from {side}: escape {pick} ({p.HeadX:0.00}, {p.HeadDrop:0.00}, {p.BodyX:0.00})");
                    }
                }
        }

        [Test]
        public void TheAim_FollowsTheHeadThroughThe_TrackedPartOfTheTell_ThenAMoveEscapes()
        {
            Bout Make()
            {
                var b = Fighting(BoxingDifficulty.Hard, 10);
                b.Level.Reaction = 0f;
                b.Level.Track = 0.6f;
                return b;
            }
            // moving early: the aim follows the head there
            var early = Make();
            early.Throw(Attack.Cross);
            Run(early, early.Level.Tell + early.Level.StrikeTime + 0.6f, Head(0.22f));
            Assert.AreEqual(Outcome.Hit, ImpactOf(early), "moved before the aim was fixed");
            // moving after the aim is fixed: out of the way
            var late = Make();
            late.Throw(Attack.Cross);
            Run(late, late.Level.Tell * 0.75f, Head(0f));
            Assert.IsTrue(late.Committed);
            Run(late, late.Level.Tell * 0.25f + late.Level.StrikeTime + 0.6f, Head(0.22f));
            Assert.AreEqual(Outcome.Dodged, ImpactOf(late), "moved after it");
        }

        [Test]
        public void TheDefence_IsWhatTheCameraSawJustBeforeTheImpact_TheImpactWaitsForTheNews()
        {
            var b = Fighting();
            b.Throw(Attack.Cross, 0, 0, Vector2.zero);
            float impact = b.Level.Tell + b.Level.StrikeTime;
            const float lag = 0.17f;
            bool waited = false;
            // the camera is late: each pose arrives lag seconds after it happened; the head leaves just before the impact
            var history = new List<(double t, HeroPose p)>();
            for (float t = 0f; t < impact + 0.8f; t += Dt)
            {
                double now = b.Time;
                history.Add((now, now >= impact - 0.12f ? Head(0.22f) : Head(0f)));
                foreach (var h in history.Where(h => h.t <= now - lag && h.t > now - lag - Dt + 1e-6)) b.Pose(h.t, h.p);
                b.SawUntil(now - lag);
                if (b.State == Foe.Impact) waited = true;
                b.Step(Dt);
            }
            Assert.IsTrue(waited, "the hitstop waited for the camera");
            Assert.AreEqual(Outcome.Dodged, ImpactOf(b));
        }

        // ---------------------------------------------------------------- the hero's punches against what it does

        [Test]
        public void APunch_LandsOnAnOpenGuard_TheGuardStopsWhatItCovers()
        {
            var b = Fighting();
            float before = b.Health;
            b.Punch(b.Time, true, Blow.Straight, false, 3f);
            Run(b, 0.3f);
            Assert.AreEqual(Outcome.Hit, Take(b, BoutEventKind.Landed).Single().Outcome);
            Assert.AreEqual(BoxingLevel.PunchDamage(Blow.Straight, 3f), before - b.Health, 1e-3f);

            b = Fighting();
            b.HoldGuard(Guard.High);
            b.Punch(b.Time, true, Blow.Straight, false, 3f);
            b.Punch(b.Time, false, Blow.Straight, true, 3f);
            b.Punch(b.Time, true, Blow.Uppercut, false, 3f);
            Run(b, 0.4f);
            var landed = Take(b, BoutEventKind.Landed);
            Assert.AreEqual(Outcome.Blocked, landed[0].Outcome, "the head behind a high guard");
            Assert.AreEqual(Outcome.Hit, landed[1].Outcome, "the body under it");
            Assert.AreEqual(Outcome.Hit, landed[2].Outcome, "an uppercut between the gloves");

            b = Fighting();
            b.HoldGuard(Guard.Low);
            b.Punch(b.Time, true, Blow.Straight, true, 3f);
            b.Punch(b.Time, false, Blow.Hook, false, 3f);
            Run(b, 0.4f);
            landed = Take(b, BoutEventKind.Landed);
            Assert.AreEqual(Outcome.Blocked, landed[0].Outcome, "the body behind a low guard");
            Assert.AreEqual(Outcome.Hit, landed[1].Outcome, "the head over it");
        }

        [Test]
        public void AStrongOpponent_DefendsAndCounters_AWeakOneHardly()
        {
            (float defended, int counters) Measure(BoxingDifficulty d, int fight)
            {
                var b = Deciding(d, fight, 9, l => l.AttackGap = 100f);
                int thrown = 0;
                for (int i = 0; i < 40; i++)
                {
                    b.SetHealth(b.MaxHealth);
                    b.HoldGuard(Guard.Open, 0.01f);
                    if (b.Phase == BoutPhase.Fighting && b.Hero == HeroState.Fighting)
                    {
                        b.Punch(b.Time, i % 2 == 0, Blow.Straight, false, 3.5f);
                        thrown++;
                    }
                    Run(b, 0.9f);
                }
                return (b.FoeDefended / (float)thrown, b.FoeCounters);
            }
            var weak = Measure(BoxingDifficulty.Easy, 0);
            var strong = Measure(BoxingDifficulty.Hard, Campaign.Fights - 1);
            Console.WriteLine($"weak: defended {weak.defended:0.00}, counters {weak.counters}; strong: {strong.defended:0.00}, {strong.counters}");
            Assert.Greater(strong.defended, weak.defended + 0.15f);
            Assert.Less(strong.defended, 0.7f, "most punches still land: it's no wall");
            Assert.Greater(strong.counters, weak.counters + 2);
        }

        [Test]
        public void PunchesInARow_MakeItCoverUp_AnUppercutBreaksTheCover()
        {
            var b = Fighting(BoxingDifficulty.Medium, 6);
            int cover = b.Level.Strength < 0.3f ? 4 : 3;
            for (int i = 0; i < cover; i++)
            {
                b.Punch(b.Time, i % 2 == 0, Blow.Straight, false, 3f);
                Run(b, 0.25f);
            }
            Assert.AreEqual(Foe.Covering, b.State, "covering up");
            Assert.AreEqual(1, Take(b, BoutEventKind.Covers).Count);
            // into the cover: blocked, the body too
            b.Punch(b.Time, true, Blow.Straight, false, 3f);
            b.Punch(b.Time, false, Blow.Straight, true, 3f);
            Run(b, 0.2f);
            var into = Take(b, BoutEventKind.Landed).Skip(cover).ToList();
            Assert.That(into.All(e => e.Outcome == Outcome.Blocked), "the cover holds straight punches, high and low");
            // an uppercut gets between the gloves and breaks it
            b.Punch(b.Time, true, Blow.Uppercut, false, 3f);
            Run(b, 0.2f);
            var up = Take(b, BoutEventKind.Landed).Last();
            Assert.AreEqual(Outcome.Hit, up.Outcome);
            Assert.IsTrue(up.CoverBroken);
            Assert.AreNotEqual(Foe.Covering, b.State);
            Assert.AreEqual(1, b.CoversBroken);
        }

        [Test]
        public void ADodge_LeavesItStaggered_ACounterCountsNearlyDouble_AFlurryMakesItDizzy()
        {
            var b = Fighting(BoxingDifficulty.Easy, 2);
            b.Throw(Attack.Cross, 0, 0, Vector2.zero);
            Run(b, b.Level.Tell + b.Level.StrikeTime + 0.1f, Head(0.22f));
            Assert.AreEqual(Foe.Staggered, b.State);
            float before = b.Health;
            b.Punch(b.Time, true, Blow.Straight, false, 3f);
            Run(b, 0.2f);
            var hit = Take(b, BoutEventKind.Landed).Last();
            Assert.IsTrue(hit.Counter);
            Assert.AreEqual(BoxingLevel.PunchDamage(Blow.Straight, 3f) * Bout.StaggerHit, before - b.Health, 0.05f);
            for (int i = 0; i < 6 && b.State != Foe.Dizzy; i++)
            {
                b.Punch(b.Time, i % 2 == 0, Blow.Hook, false, 3f);
                Run(b, 0.2f);
            }
            Assert.AreEqual(Foe.Dizzy, b.State, "dizzy after a combo");
            Assert.AreEqual(1, Take(b, BoutEventKind.Dizzy).Count);
        }

        // ---------------------------------------------------------------- the opponent decides

        static int FirstWith(params Attack[] moves)
        {
            for (int i = 0; i < Campaign.Fights; i++)
            {
                var o = Campaign.Opponent(i);
                if (moves.All(m => o.Moves.Any(x => x.attack == m)) && o.Specials.Length == 0 && !o.Rhythm) return i;
            }
            throw new InvalidOperationException("no opponent with those punches");
        }

        static Dictionary<Attack, int> Attacks(Bout b, float seconds, HeroPose pose)
        {
            var count = new Dictionary<Attack, int>();
            for (float t = 0f; t < seconds; t += 0.5f)
            {
                Run(b, 0.5f, pose);
                foreach (var e in b.Events.Where(e => e.Kind == BoutEventKind.WindUp))
                    count[e.Attack] = count.TryGetValue(e.Attack, out int n) ? n + 1 : 1;
                b.Events.Clear();
                b.SetHealth(b.MaxHealth);
            }
            return count;
        }

        static float Share(Dictionary<Attack, int> c, params Attack[] which)
        {
            int all = c.Values.Sum();
            return all == 0 ? 0f : which.Sum(a => c.TryGetValue(a, out int n) ? n : 0) / (float)all;
        }

        [Test]
        public void AgainstRaisedGloves_ItGoesRoundThem_AgainstAnOpenGuard_StraightAtTheHead()
        {
            int fight = FirstWith(Attack.Jab, Attack.Body, Attack.Uppercut);
            var guarded = Attacks(Deciding(BoxingDifficulty.Hard, fight), 90f, Head(0f, 0f, guard: true));
            var open = Attacks(Deciding(BoxingDifficulty.Hard, fight), 90f, Head(0f));
            float round = Share(guarded, Attack.Body, Attack.Uppercut), roundOpen = Share(open, Attack.Body, Attack.Uppercut);
            Console.WriteLine($"fight {fight}: body+uppercut against the guard {round:0.00}, against an open one {roundOpen:0.00}");
            Assert.Greater(round, 0.55f);
            Assert.Greater(round, roundOpen + 0.25f);
        }

        [Test]
        public void ItHooksTheSideTheHeroLeansTo()
        {
            int fight = FirstWith(Attack.Hook);
            var b = Deciding(BoxingDifficulty.Hard, fight);
            int same = 0, other = 0;
            for (float t = 0f; t < 120f; t += 0.5f)
            {
                Run(b, 0.5f, Head(0.15f));
                foreach (var e in b.Events.Where(e => e.Kind == BoutEventKind.WindUp && e.Attack == Attack.Hook))
                {
                    if (e.Side > 0) same++;
                    else other++;
                }
                b.Events.Clear();
                b.SetHealth(b.MaxHealth);
            }
            Console.WriteLine($"hooks from the side leant to {same}, the other {other}");
            Assert.Greater(same, 3 * Math.Max(1, other));
        }

        [Test]
        public void TheRobot_KeepsItsRhythm_TheSamePunchesInTheSameOrder()
        {
            int fight = Enumerable.Range(0, Campaign.Fights).First(i => Campaign.Opponent(i).Rhythm);
            var b = Deciding(BoxingDifficulty.Medium, fight);
            var order = new List<Attack>();
            var starts = new List<double>();
            for (float t = 0f; t < 60f && order.Count < 13; t += Dt)
            {
                Run(b, Dt, Head(0f, 0f, guard: true));
                foreach (var e in b.Events.Where(e => e.Kind == BoutEventKind.WindUp))
                {
                    order.Add(e.Attack);
                    starts.Add(b.Time);
                }
                b.Events.Clear();
            }
            var pattern = Campaign.Opponent(fight).Moves.Select(m => m.attack).ToArray();
            Assert.GreaterOrEqual(order.Count, 12);
            for (int i = 0; i < 12; i++) Assert.AreEqual(pattern[i % pattern.Length], order[i], $"punch {i}");
            // on a beat: the gaps between its punches hardly vary
            var gaps = starts.Zip(starts.Skip(1), (a, c) => c - a).ToList();
            Console.WriteLine("gaps " + string.Join(" ", gaps.Select(g => g.ToString("0.00"))));
            Assert.Less(gaps.Max() - gaps.Min(), 0.25, "a steady beat");
        }

        [Test]
        public void TheShadow_LearnsTheHerosFavouritePunch_AndGuardsAgainstIt()
        {
            int fight = Enumerable.Range(0, Campaign.Fights).First(i => Campaign.Opponent(i).Mirror);
            // how many straight punches at the head it stops (its gloves open each time: its reading alone), against a hero
            // who throws only those and one who mixes them with hooks, uppercuts and body blows
            (int stopped, int thrown, int favourite) Straights(bool mixing)
            {
                var b = Deciding(BoxingDifficulty.Medium, fight, 4, l => { l.AttackGap = 100f; l.CounterRate = 0f; });
                Blow[] mix = { Blow.Straight, Blow.Hook, Blow.Uppercut, Blow.Straight };
                int stopped = 0, thrown = 0;
                for (int i = 0; i < 120; i++)
                {
                    b.SetHealth(b.MaxHealth);
                    b.HoldGuard(Guard.Open, 0.01f);
                    var blow = mixing ? mix[i % mix.Length] : Blow.Straight;
                    bool body = mixing && i % 4 == 3;
                    b.Punch(b.Time, i % 2 == 0, blow, body, 3.5f);
                    Run(b, 0.9f);
                    if (i >= 20 && blow == Blow.Straight && !body)
                    {
                        thrown++;
                        if (Take(b, BoutEventKind.Landed).Any(e => e.Outcome is Outcome.Blocked or Outcome.Missed)) stopped++;
                    }
                    b.Events.Clear();
                }
                return (stopped, thrown, b.Favourite);
            }
            var same = Straights(false);
            var mixed = Straights(true);
            Console.WriteLine($"straight punches stopped: always the same {same.stopped}/{same.thrown}, in a mix {mixed.stopped}/{mixed.thrown}");
            Assert.AreEqual(0, same.favourite, "straight punches at the head");
            Assert.AreEqual(-1, mixed.favourite, "no favourite in a mix");
            Assert.Greater(same.stopped / (float)same.thrown, mixed.stopped / (float)mixed.thrown + 0.2f,
                           "it reads the punch thrown again and again");
        }

        // ---------------------------------------------------------------- knockdowns, rounds, the super, fireballs

        [Test]
        public void TheHero_GetsUpByPunching_BeforeTen_ElseItsAKnockout()
        {
            var b = Fighting(BoxingDifficulty.Medium, 5);
            b.SetHero(1f, 0f);
            b.Throw(Attack.Cross);
            Run(b, 2f);
            Assert.AreEqual(BoutPhase.Down, b.Phase);
            Assert.AreEqual(0, b.DownFighter);
            Run(b, 1.6f);
            for (int i = 0; i < b.GetUpNeeded; i++)
            {
                b.Punch(b.Time, i % 2 == 0, Blow.Straight, false, 3f);
                Run(b, 0.25f);
            }
            Run(b, 0.5f);
            Assert.AreEqual(BoutPhase.Fighting, b.Phase, "up again");
            Assert.Greater(b.HeroHealth, 30f);

            b = Fighting(BoxingDifficulty.Medium, 5);
            b.SetHero(1f, 0f);
            b.Throw(Attack.Cross);
            Run(b, 16f);
            Assert.AreEqual(BoutPhase.Over, b.Phase);
            Assert.AreEqual(Ending.KO, b.Ending);
            Assert.IsFalse(b.HeroWon);
        }

        [Test]
        public void TheOpponent_StaysDownAfterEnoughKnockdowns_ThreeInARoundIsATKO()
        {
            var b = Fighting(BoxingDifficulty.Easy, 0);
            Assert.AreEqual(2, b.Level.DownsToKO);
            for (int down = 1; down <= 2; down++)
            {
                b.SetHealth(0.5f);
                b.HoldGuard(Guard.Open);
                b.Punch(b.Time, true, Blow.Straight, false, 3f);
                Run(b, 0.3f);
                Assert.AreEqual(BoutPhase.Down, b.Phase, $"knockdown {down}");
                Run(b, 12f);
                if (down == 1) Assert.AreEqual(BoutPhase.Fighting, b.Phase, "gets up after the first");
            }
            Assert.AreEqual(BoutPhase.Over, b.Phase);
            Assert.AreEqual(Ending.KO, b.Ending);
            Assert.IsTrue(b.HeroWon);
            Assert.AreEqual(3, b.Stars, "won, never down, before the last round");

            // three in one round: a TKO even where it would get up
            b = Fighting(BoxingDifficulty.Hard, Campaign.Fights - 1);
            for (int down = 1; down <= 3 && b.Phase != BoutPhase.Over; down++)
            {
                b.SetHealth(0.5f);
                b.HoldGuard(Guard.Open);
                b.Punch(b.Time, true, Blow.Straight, false, 3f);
                Run(b, 0.3f);
                Run(b, 12f);
            }
            Assert.AreEqual(Ending.TKO, b.Ending);
            Assert.IsTrue(b.HeroWon);
        }

        [Test]
        public void AfterTheLastRound_TheDecision_GoesToTheOneWhoDidMore()
        {
            var b = Fighting(BoxingDifficulty.Medium, 3);
            for (int round = 0; round < b.Level.Rounds; round++)
            {
                b.HoldGuard(Guard.Open);
                for (int i = 0; i < 4; i++)
                {
                    b.Punch(b.Time, i % 2 == 0, Blow.Straight, false, 3f);
                    Run(b, 0.6f);
                }
                b.SetClock(0.1f);
                Run(b, 0.2f);
                if (round < b.Level.Rounds - 1)
                {
                    Assert.AreEqual(BoutPhase.Rest, b.Phase);
                    Run(b, BoxingLevel.RestSeconds + BoxingLevel.StartSeconds + 0.2f);
                    Assert.AreEqual(round + 2, b.Round);
                }
            }
            Assert.AreEqual(BoutPhase.Over, b.Phase);
            Assert.AreEqual(Ending.Decision, b.Ending);
            Assert.IsTrue(b.HeroWon);
            Assert.AreEqual(2, b.Stars, "a decision: the win and never down");
        }

        [Test]
        public void TheSuperPunch_TakesItsShare_WhenThePowerIsFull_ThenItsDizzy()
        {
            var b = Fighting();
            b.SetHero(100f, 0.5f);
            Run(b, 0.3f, new HeroPose { ArmsUp = true });
            Assert.AreEqual(HeroState.Fighting, b.Hero, "not full: nothing");
            b.SetHero(100f, 1f);
            float before = b.Health;
            Run(b, 0.1f, new HeroPose { ArmsUp = true });
            Assert.AreEqual(HeroState.Super, b.Hero);
            Assert.AreEqual(Foe.Frozen, b.State);
            Run(b, Bout.SuperRise + Bout.SuperDive + 0.1f);
            Assert.AreEqual(before - b.Level.SuperShare * b.MaxHealth, b.Health, 0.01f);
            Assert.AreEqual(0f, b.Power);
            Assert.AreEqual(Foe.Dizzy, b.State);
        }

        [Test]
        public void AFireball_PunchedBack_HitsTheThrower_ElseDodgeIt()
        {
            int final = Campaign.Fights - 1;
            var b = Fighting(BoxingDifficulty.Medium, final);
            b.Throw(Attack.Fireball, 0, 0, Vector2.zero);
            Run(b, b.Level.TellFor(Attack.Fireball) + 0.05f);
            Assert.IsTrue(b.FireballFlying);
            float before = b.Health;
            Run(b, (float)(b.FireballArrives - b.Time) - 0.1f);
            b.Punch(b.Time, true, Blow.Straight, false, 3f);
            Run(b, 0.6f);
            Assert.AreEqual(Outcome.Returned, Take(b, BoutEventKind.FireballAt)[0].Outcome);
            Assert.Less(b.Health, before);

            b = Fighting(BoxingDifficulty.Medium, final);
            b.Throw(Attack.Fireball, 0, 0, Vector2.zero);
            Run(b, 4f, HeroPose.Keys(false, 0, true));
            Assert.AreEqual(Outcome.Dodged, Take(b, BoutEventKind.FireballAt)[0].Outcome);

            b = Fighting(BoxingDifficulty.Medium, final);
            b.Throw(Attack.Fireball, 0, 0, Vector2.zero);
            Run(b, 4f, Head(0f, 0f, guard: true));
            Assert.AreEqual(Outcome.Hit, Take(b, BoutEventKind.FireballAt)[0].Outcome, "gloves don't stop fire");
        }

        // ---------------------------------------------------------------- the climb

        [Test]
        public void TheClimb_GetsStrongerFightByFight_HardIsStrongFromTheStart()
        {
            foreach (BoxingDifficulty d in Enum.GetValues(typeof(BoxingDifficulty)))
                for (int i = 1; i < Campaign.Fights; i++)
                    Assert.Greater(BoxingLevel.StrengthOf(d, i), BoxingLevel.StrengthOf(d, i - 1), $"{d} fight {i}");
            Assert.GreaterOrEqual(BoxingLevel.StrengthOf(BoxingDifficulty.Hard, 0), BoxingLevel.StrengthOf(BoxingDifficulty.Easy, Campaign.Fights - 1));
            Assert.AreEqual(4, Campaign.Stages.Length, "the gym, the roof, the mountain, the stars");
            Assert.That(Campaign.Stages.All(s => s.Opponents.Length == Campaign.FightsPerStage));
            Assert.That(Campaign.Stages.All(s => s.Opponents[2].Specials.Length > 0), "every champion has a power punch");
            // the same profile, stronger: shorter tells, harder punches, quicker to see, smarter, a better defence
            var gentle = BoxingLevel.For(BoxingDifficulty.Easy, 0);
            var strong = BoxingLevel.For(BoxingDifficulty.Hard, Campaign.Fights - 1);
            Assert.Greater(gentle.Tell, strong.Tell);
            Assert.Less(gentle.DamageScale, strong.DamageScale);
            Assert.Greater(gentle.Reaction, strong.Reaction);
            Assert.Less(gentle.Smart, strong.Smart);
            Assert.Less(gentle.DefendSkill, strong.DefendSkill);
            Assert.Less(gentle.Track, strong.Track);
            Assert.Less(gentle.HitRadius, strong.HitRadius);
        }

        // ---------------------------------------------------------------- whole fights

        /// <summary>
        /// Simulated players through the climb at each difficulty (the first, a middle and the last fight; wins of 6 and
        /// minutes). What it must be: anyone who tries wins the first fights on easy; standing still or only guarding
        /// never wins; mashing without defending wins on easy's first fights only; a good player gets through medium; the
        /// last fight on hard needs a very good one.
        /// </summary>
        [Test]
        public void Balance_ByDifficulty()
        {
            var table = Table(6);
            Console.WriteLine(table.text);
            int W(string player, BoxingDifficulty d, int column) => table.wins[(player, d, column)];
            // anyone who tries wins easy's first fight; the careful child gets through easy
            foreach (var p in new[] { "perfect", "good", "careful child", "child" }) Assert.GreaterOrEqual(W(p, BoxingDifficulty.Easy, 0), 5, p);
            Assert.GreaterOrEqual(W("careful child", BoxingDifficulty.Easy, 2), 4);
            // doing nothing, or only guarding, never wins
            foreach (BoxingDifficulty d in Enum.GetValues(typeof(BoxingDifficulty)))
                for (int c = 0; c < 3; c++)
                {
                    Assert.AreEqual(0, W("still", d, c), $"still {d} {c}");
                    Assert.AreEqual(0, W("turtle", d, c), $"turtle {d} {c}");
                }
            // mashing without defending: easy's start, not the top of medium or anything on hard's end
            Assert.GreaterOrEqual(W("masher", BoxingDifficulty.Easy, 0), 4);
            Assert.LessOrEqual(W("masher", BoxingDifficulty.Medium, 2), 1);
            Assert.AreEqual(0, W("masher", BoxingDifficulty.Hard, 2));
            // a good player gets through medium, a perfect one through hard
            Assert.GreaterOrEqual(W("good", BoxingDifficulty.Medium, 2), 4);
            Assert.GreaterOrEqual(W("perfect", BoxingDifficulty.Hard, 2), 5);
        }

        static readonly int[] Columns = { 0, Campaign.Fights / 2, Campaign.Fights - 1 };

        static (string text, Dictionary<(string, BoxingDifficulty, int), int> wins) Table(int runs)
        {
            var sb = new StringBuilder();
            var wins = new Dictionary<(string, BoxingDifficulty, int), int>();
            sb.AppendLine($"wins of {runs} (minutes, hero downs, foe body share, foe defended share), fights {string.Join(" / ", Columns)}");
            foreach (BoxingDifficulty d in Enum.GetValues(typeof(BoxingDifficulty)))
            {
                sb.AppendLine(d.ToString());
                foreach (var player in BoutSim.Players)
                {
                    sb.Append($"  {player.Name,-14}");
                    for (int c = 0; c < Columns.Length; c++)
                    {
                        int won = 0;
                        float minutes = 0f, body = 0f, defended = 0f;
                        int downs = 0;
                        for (int s = 0; s < runs; s++)
                        {
                            var r = BoutSim.Run(BoxingLevel.For(d, Columns[c]), player, 100 + s * 31 + c);
                            if (r.Won) won++;
                            minutes += r.Seconds / 60f;
                            downs += r.HeroDowns;
                            body += r.FoeThrown > 0 ? r.FoeBody / (float)r.FoeThrown : 0f;
                            defended += r.Thrown > 0 ? r.FoeDefended / (float)r.Thrown : 0f;
                        }
                        wins[(player.Name, d, c)] = won;
                        sb.Append($"  {won}/{runs} ({minutes / runs:0.0}m {downs / (float)runs:0.0}d b{body / runs:0.00} f{defended / runs:0.00})");
                    }
                    sb.AppendLine();
                }
            }
            return (sb.ToString(), wins);
        }

        [Test, Explicit("Diagnostic: the balance table for every fight of the climb, two players")]
        public void Report_EveryFight()
        {
            var sb = new StringBuilder();
            foreach (BoxingDifficulty d in Enum.GetValues(typeof(BoxingDifficulty)))
            foreach (var player in new[] { BoutSim.Good, BoutSim.Child })
            {
                sb.Append($"{d} {player.Name}:");
                for (int i = 0; i < Campaign.Fights; i++)
                {
                    int won = 0;
                    float minutes = 0f;
                    for (int s = 0; s < 4; s++)
                    {
                        var r = BoutSim.Run(BoxingLevel.For(d, i), player, 500 + s);
                        if (r.Won) won++;
                        minutes += r.Seconds / 60f / 4f;
                    }
                    sb.Append($" {i}:{won}/4 {minutes:0.0}");
                }
                sb.AppendLine();
            }
            Console.WriteLine(sb.ToString());
        }

        [Test, Explicit("Diagnostic: one fight's story, the good player against a middle opponent")]
        public void Report_OneFight()
        {
            var r = BoutSim.Run(BoxingLevel.For(BoxingDifficulty.Medium, 9), BoutSim.Good, 77);
            Console.WriteLine(r.ToString());
        }
    }
}
