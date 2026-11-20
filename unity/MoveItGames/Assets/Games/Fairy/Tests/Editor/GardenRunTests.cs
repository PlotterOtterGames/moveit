using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoveIt.Games.Fairy.Tests
{
    /// <summary>Zana!'s garden defence (2026-11-16): the rules one by one, then whole runs played by simulated players.</summary>
    public class GardenRunTests
    {
        static GardenRun Quiet(FairyDifficulty d = FairyDifficulty.Medium, int players = 1)
        {
            var run = new GardenRun(d, players, 7);
            run.ClearSchedule();
            run.Events.Clear();
            return run;
        }

        static void Steps(GardenRun run, float seconds, float dt = 0.05f)
        {
            for (float t = 0f; t < seconds; t += dt) run.Step(dt);
        }

        [Test]
        public void Waves_GrowAndQuicken_EveryFifthHasABoss()
        {
            var w1 = GardenLevel.Wave(1, FairyDifficulty.Medium, 1);
            var w2 = GardenLevel.Wave(2, FairyDifficulty.Medium, 1);
            var w4 = GardenLevel.Wave(4, FairyDifficulty.Medium, 1);
            var w5 = GardenLevel.Wave(5, FairyDifficulty.Medium, 1);
            var w10 = GardenLevel.Wave(10, FairyDifficulty.Medium, 1);
            Assert.That(w2.Total, Is.GreaterThan(w1.Total));
            Assert.That(w10.Total, Is.GreaterThan(w4.Total));
            Assert.That(w2.Speed / w1.Speed, Is.EqualTo(1.05f).Within(0.001f));
            Assert.IsFalse(w4.Boss);
            Assert.IsTrue(w5.Boss && w10.Boss);
            Assert.That(w1.Wisps, Is.EqualTo(0), "the first wave walks");
            Assert.That(GardenLevel.Wave(6, FairyDifficulty.Easy, 1).Total, Is.LessThan(GardenLevel.Wave(6, FairyDifficulty.Hard, 1).Total));
            Assert.That(GardenLevel.Wave(6, FairyDifficulty.Medium, 2).Total, Is.GreaterThan(GardenLevel.Wave(6, FairyDifficulty.Medium, 1).Total));
        }

        [Test]
        public void ACreatureReachingTheTree_TakesItsHealth_AndSinksIn()
        {
            var run = Quiet();
            int before = run.TreeHealth;
            var bug = run.Add(CreatureKind.Bug, -1, -4.2f);
            Steps(run, 8f);
            Assert.That(run.TreeHealth, Is.EqualTo(before - GardenLevel.DamageOf(CreatureKind.Bug)));
            Assert.IsNull(run.Find(bug.Id));
            Assert.IsTrue(run.Events.Any(e => e.Kind == GardenEventKind.TreeHit));
            Assert.That(run.Purified, Is.EqualTo(0), "no stardust for one that got through");
        }

        [Test]
        public void TheLightning_StrikesEverythingWalkingOnItsSide()
        {
            var run = Quiet();
            var bug = run.Add(CreatureKind.Bug, -1, -5f);
            var thorn = run.Add(CreatureKind.Thorn, -1, -3.4f);
            var brute = run.Add(CreatureKind.Brute, -1, -6.5f);
            var right = run.Add(CreatureKind.Bug, 1, 4.5f);
            var wisp = run.Add(CreatureKind.Wisp, -1, 0f);
            var outside = run.Add(CreatureKind.Bug, -1, -9.4f);
            Steps(run, 0.2f);
            outside.Position = new Vector3(-9.4f, 0f, GardenRun.TreeZ);
            float x = brute.Position.x;
            Assert.That(run.Cast(Spell.Lightning, 0, -1), Is.EqualTo(4), "the four walking on the left");
            Assert.That(bug.State, Is.EqualTo(CreatureState.Purified));
            Assert.That(thorn.State, Is.EqualTo(CreatureState.Purified));
            Assert.That(brute.Hp, Is.EqualTo(brute.MaxHp - 1), "a brute takes two");
            Assert.That(brute.Position.x, Is.LessThan(x), "thrown back");
            Assert.That(right.Hp, Is.EqualTo(right.MaxHp), "not the other side");
            Assert.That(wisp.Hp, Is.EqualTo(wisp.MaxHp), "not what flies");
            Assert.That(outside.State, Is.EqualTo(CreatureState.Purified), "the whole side: one still coming in too");
            Assert.That(run.Cast(Spell.Lightning, 0, 0), Is.EqualTo(-1), "a lightning needs a side");
            Assert.That(run.Cast(Spell.Lightning, 1, -1), Is.EqualTo(1), "always ready: the brute again");
            Assert.That(brute.State, Is.EqualTo(CreatureState.Purified));
            var e = run.Events.Last(ev => ev.Kind == GardenEventKind.Lightning);
            Assert.That(e.Side, Is.EqualTo(-1));
            Assert.That(e.Player, Is.EqualTo(1));
        }

        [Test]
        public void TheRainbow_StrikesWhatFliesOnItsSide()
        {
            var run = Quiet();
            var left = run.Add(CreatureKind.Wisp, -1, 0f);
            var right = run.Add(CreatureKind.Wisp, 1, 0f);
            var walker = run.Add(CreatureKind.Bug, 1, 5f);
            left.Along = right.Along = 0.25f;
            Steps(run, 0.1f);
            float along = right.Along;
            Assert.That(run.Cast(Spell.Rainbow, 0, 1), Is.EqualTo(1));
            Assert.That(right.State, Is.EqualTo(CreatureState.Purified));
            Assert.That(left.State, Is.EqualTo(CreatureState.Coming));
            Assert.That(walker.Hp, Is.EqualTo(walker.MaxHp), "the rainbow is for what flies");
            Assert.That(run.Events.Any(ev => ev.Kind == GardenEventKind.Rainbow && ev.Side == 1));
        }

        [Test]
        public void TheShield_BouncesWhoeverTouchesIt_TheTreeIsSafe()
        {
            var run = Quiet();
            int health = run.TreeHealth;
            Assert.That(run.Cast(Spell.Shield), Is.EqualTo(0));
            var brute = run.Add(CreatureKind.Brute, 1, 3.6f);
            Steps(run, 3f);
            Assert.That(run.TreeHealth, Is.EqualTo(health));
            Assert.That(brute.Hp, Is.EqualTo(brute.MaxHp), "the bubble protects, it doesn't purify");
            Assert.That(Mathf.Abs(brute.Position.x), Is.GreaterThan(GardenRun.ShieldRadius - 0.1f));
            Assert.That(run.Cast(Spell.Shield), Is.EqualTo(-1), "the shield comes back after a while");
            Assert.IsTrue(run.Events.Any(e => e.Kind == GardenEventKind.Blocked));
        }

        [Test]
        public void TheSeeds_HitEverythingNearTheTree_NotFarAway()
        {
            var run = Quiet();
            var near = run.Add(CreatureKind.Thorn, -1, -3f);
            var near2 = run.Add(CreatureKind.Bug, 1, 2.7f);
            var far = run.Add(CreatureKind.Bug, 1, 7.2f);
            Assert.That(run.Cast(Spell.Seeds), Is.EqualTo(2));
            Assert.That(near.State, Is.EqualTo(CreatureState.Purified));
            Assert.That(near2.State, Is.EqualTo(CreatureState.Purified));
            Assert.That(far.State, Is.EqualTo(CreatureState.Coming));
            Assert.That(run.Cast(Spell.Seeds), Is.EqualTo(-1));
        }

        [Test]
        public void TheCrystal_FillsWithTheWavesTime_AndWithPurifying()
        {
            // purifying: a little each
            var run = Quiet();
            for (int i = 0; i < 10; i++)
            {
                run.Add(CreatureKind.Bug, -1, -5.4f);
                run.Cast(Spell.Lightning, 0, -1);
                Steps(run, run.RechargeOf(0) + 0.05f);
            }
            // (the wave's own time adds a little while the wand charges)
            Assert.That(run.Magic, Is.GreaterThanOrEqualTo(10 * GardenLevel.MagicPerCreature - 0.001f).And.LessThan(0.9f));
            // and a wave's time (one brute kept far off keeps the wave going)
            run = Quiet();
            var brute = run.Add(CreatureKind.Brute, -1, -9f);
            float t = 0f;
            while (run.Magic < 1f && t < 60f)
            {
                brute.Position = new Vector3(-9f, 0f, GardenRun.TreeZ);
                run.Step(0.05f);
                t += 0.05f;
            }
            Assert.That(t, Is.EqualTo(GardenLevel.MagicSeconds).Within(0.2f));
        }

        [Test]
        public void TheBurst_NeedsAFullCrystal_ThenPurifiesEverything()
        {
            var run = Quiet();
            var brute = run.Add(CreatureKind.Brute, -1, -9f);
            Assert.That(run.Cast(Spell.Burst), Is.EqualTo(-1));
            while (run.Magic < 1f)
            {
                brute.Position = new Vector3(-7f, 0f, GardenRun.TreeZ);
                run.Step(0.05f);
            }
            var wisp = run.Add(CreatureKind.Wisp, 1, 0f);
            Steps(run, 2f);
            Assert.That(run.Cast(Spell.Burst), Is.EqualTo(2));
            Assert.That(brute.State, Is.EqualTo(CreatureState.Purified));
            Assert.That(wisp.State, Is.EqualTo(CreatureState.Purified));
            Assert.That(run.Magic, Is.EqualTo(0f));
        }

        [Test]
        public void APuff_SplitsIntoTwoBugs()
        {
            var run = Quiet();
            var puff = run.Add(CreatureKind.Puff, -1, -5.4f);
            Assert.That(run.Cast(Spell.Lightning, 0, -1), Is.EqualTo(1));
            Assert.That(puff.State, Is.EqualTo(CreatureState.Purified));
            var bugs = run.Creatures.Where(c => c.Kind == CreatureKind.Bug && c.Alive).ToList();
            Assert.That(bugs.Count, Is.EqualTo(2));
            Assert.IsTrue(bugs.All(b => b.Position.x < 0f));
        }

        [Test]
        public void TheBoss_StaysAtTheTree_PoundingIt_UntilPurified()
        {
            var run = Quiet();
            var boss = run.Add(CreatureKind.Boss, 1, 3f);
            int health = run.TreeHealth;
            Steps(run, 12f);
            Assert.That(boss.State, Is.EqualTo(CreatureState.Attacking));
            Assert.That(run.TreeHealth, Is.LessThanOrEqualTo(health - 2 * GardenLevel.DamageOf(CreatureKind.Boss)));
            int hp = boss.Hp;
            Assert.That(hp, Is.GreaterThanOrEqualTo(2), "the boss takes several spells");
            run.Cast(Spell.Lightning, 0, 1);
            Assert.That(boss.Hp, Is.EqualTo(hp - 1));
            Assert.That(boss.State, Is.EqualTo(CreatureState.Coming), "knocked off the tree");
        }

        [Test]
        public void EveryFifthWave_TheFairyTransforms_TheSpellsGrowStronger()
        {
            var run = new GardenRun(FairyDifficulty.Easy, 1, 3);
            var bot = GardenBot.Perfect(1);
            float t = 0f;
            while (run.WavesCleared < 5 && !run.Over && t < 600f)
            {
                if (run.Current != GardenRun.Stage.Transform) bot.Act(run, t);
                run.Step(0.05f);
                t += 0.05f;
            }
            Assert.That(run.Current, Is.EqualTo(GardenRun.Stage.Transform));
            Assert.That(run.Level, Is.EqualTo(1));
            Assert.That(GardenLevel.SpellPower(run.Level), Is.EqualTo(1));
            run.Transform();
            Assert.That(run.Level, Is.EqualTo(2));
            Assert.That(GardenLevel.SpellPower(run.Level), Is.EqualTo(2));
            Assert.That(run.Current, Is.EqualTo(GardenRun.Stage.Breather));
            Assert.IsTrue(run.OrbActive);
        }

        /// <summary>Runs <paramref name="run"/> while the creatures in <paramref name="held"/> stay where they are.</summary>
        static void Go(GardenRun run, List<(Creature c, float x)> held, float seconds, Func<bool> until = null)
        {
            for (float t = 0f; t < seconds && (until == null || !until()); t += 0.05f)
            {
                foreach (var (c, x) in held)
                    if (c.Alive) c.Position = new Vector3(x, 0f, GardenRun.TreeZ);
                run.Step(0.05f);
            }
        }

        [Test]
        public void TheSuggestion_IsWhatHelpsNow()
        {
            var run = Quiet();
            run.KeepWave = true;
            var held = new List<(Creature c, float x)>();
            Go(run, held, 0.1f);
            Assert.IsNull(run.Suggest(), "nothing in the picture: nothing to draw");
            // a bug walking in on the left: the lightning there
            var bug = run.Add(CreatureKind.Bug, -1, -6f);
            Go(run, held, 0.1f);
            Assert.That(run.Suggest(), Is.EqualTo(new SpellOffer(Spell.Lightning, -1)));
            Assert.That(run.Cast(Spell.Lightning, 0, -1), Is.EqualTo(1));
            Assert.That(bug.State, Is.EqualTo(CreatureState.Purified));
            // a wisp flying in on the right: the rainbow there; a thorn about to reach the tree on the left comes first
            var wisp = run.Add(CreatureKind.Wisp, 1, 0f);
            wisp.Along = 0.2f;
            Go(run, held, 0.1f);
            Assert.That(run.Suggest(), Is.EqualTo(new SpellOffer(Spell.Rainbow, 1)));
            held.Add((run.Add(CreatureKind.Thorn, -1, -2.7f), -2.7f));
            Go(run, held, 0.1f);
            Assert.That(run.Suggest(), Is.EqualTo(new SpellOffer(Spell.Lightning, -1)));
            // a group about to reach the tree comes before the big magic; then the big magic
            for (int i = 0; i < 4; i++) held.Add((run.Add(CreatureKind.Brute, 1, 7f + 0.2f * i), 7f + 0.2f * i));
            Go(run, held, 60f, () => run.Magic >= 1f);
            Assert.That(run.Magic, Is.GreaterThanOrEqualTo(1f));
            Assert.That(run.Suggest(), Is.EqualTo(new SpellOffer(Spell.Lightning, -1)), "the thorn first");
            Go(run, held, 3f, () => run.Charged(0));
            Assert.That(run.Cast(Spell.Lightning, 0, -1), Is.EqualTo(1));
            Go(run, held, 0.05f);
            Assert.That(run.Suggest(), Is.EqualTo(new SpellOffer(Spell.Burst)), "nothing pressing: the big magic");
            Go(run, held, 3f, () => run.Charged(0));
            Assert.That(run.Cast(Spell.Burst), Is.GreaterThanOrEqualTo(4), "the four brutes");
            // creatures near the tree on both sides: the seeds; a brute coming (nothing pressing yet): the shield
            run = Quiet();
            run.KeepWave = true;
            held = new List<(Creature c, float x)> { (run.Add(CreatureKind.Bug, -1, -2.6f), -2.6f) };
            held.Add((run.Add(CreatureKind.Bug, 1, 2.6f), 2.6f));
            Go(run, held, 0.1f);
            Assert.That(run.Suggest(), Is.EqualTo(new SpellOffer(Spell.Seeds)), "both sides near the tree");
            Assert.That(run.Cast(Spell.Seeds), Is.EqualTo(2));
            held.Clear();
            held.Add((run.Add(CreatureKind.Brute, -1, -7f), -7f));
            Go(run, held, 3f, () => run.Charged(0));
            Assert.That(run.Suggest(), Is.EqualTo(new SpellOffer(Spell.Shield)));
            Assert.That(run.Cast(Spell.Shield), Is.EqualTo(0));
            Go(run, held, 0.1f);
            Assert.That(run.Suggest(), Is.EqualTo(new SpellOffer(Spell.Lightning, -1)), "then its lightning");
        }

        [Test]
        public void TheWand_ChargesBetweenSpells()
        {
            var run = Quiet(FairyDifficulty.Medium);
            run.KeepWave = true;
            var held = new List<(Creature c, float x)>();
            for (int i = 0; i < 3; i++) held.Add((run.Add(CreatureKind.Brute, -1, -6f - i), -6f - i));
            Assert.IsTrue(run.Charged(0));
            Assert.That(run.Cast(Spell.Lightning, 0, -1), Is.EqualTo(3));
            float charge = run.RechargeOf(0);
            Assert.That(charge, Is.EqualTo(GardenLevel.Recharge(FairyDifficulty.Medium, 1)).Within(1e-4f));
            Assert.That(run.Cast(Spell.Lightning, 0, -1), Is.EqualTo(-1), "still charging: nothing");
            Go(run, held, charge - 0.1f);
            Assert.IsFalse(run.Charged(0));
            Go(run, held, 0.15f);
            Assert.IsTrue(run.Charged(0));
            Assert.IsTrue(run.Events.Any(e => e.Kind == GardenEventKind.Charged && e.Player == 0), "the wand says it's ready");
            Assert.That(run.Cast(Spell.Lightning, 0, -1), Is.EqualTo(3), "the brutes take two hits");
            // easier, quicker; a fairy transformed thrice charges quicker; each fairy her own wand
            Assert.That(GardenLevel.Recharge(FairyDifficulty.Easy, 1), Is.LessThan(GardenLevel.Recharge(FairyDifficulty.Hard, 1)));
            Assert.That(GardenLevel.Recharge(FairyDifficulty.Medium, 3), Is.LessThan(GardenLevel.Recharge(FairyDifficulty.Medium, 2)));
            var two = Quiet(players: 2);
            two.KeepWave = true;
            two.Add(CreatureKind.Brute, 1, 6f);
            Assert.That(two.Cast(Spell.Lightning, 0, 1), Is.EqualTo(1));
            Assert.That(two.Cast(Spell.Lightning, 1, 1), Is.EqualTo(1), "the other fairy's wand is charged");
        }

        [Test]
        public void TheLightning_StrikesTheSideItsDrawnOn_OrWhereSomethingIs()
        {
            var run = Quiet();
            run.KeepWave = true;
            run.Add(CreatureKind.Bug, -1, -6f);
            run.Add(CreatureKind.Bug, 1, 6f);
            Assert.That(run.SideFor(Spell.Lightning, -1), Is.EqualTo(-1));
            Assert.That(run.SideFor(Spell.Lightning, 1), Is.EqualTo(1));
            Assert.That(run.SideFor(Spell.Rainbow, 1), Is.EqualTo(1), "nothing flying anywhere: where it was drawn");
            Assert.That(run.SideFor(Spell.Shield, 1), Is.EqualTo(0));
            run = Quiet();
            run.KeepWave = true;
            run.Add(CreatureKind.Bug, -1, -6f);
            Assert.That(run.SideFor(Spell.Lightning, 1), Is.EqualTo(-1), "drawn on the empty side: the other side's bug");
            var wisp = run.Add(CreatureKind.Wisp, 1, 0f);
            wisp.Along = 0.3f;
            Assert.That(run.SideFor(Spell.Rainbow, -1), Is.EqualTo(1));
        }

        [Test]
        public void TwoFairies_AreEachSuggestedTheirOwnSide()
        {
            var run = Quiet(players: 2);
            run.KeepWave = true;
            var held = new List<(Creature c, float x)> { (run.Add(CreatureKind.Bug, -1, -4.5f), -4.5f), (run.Add(CreatureKind.Bug, 1, 4.7f), 4.7f) };
            // (the shield just used: else it would come first, with creatures coming from both sides)
            run.Cast(Spell.Shield, 1);
            Go(run, held, 3f, () => run.Charged(1));
            Assert.That(run.Suggest(0), Is.EqualTo(new SpellOffer(Spell.Lightning, -1)), "fairy 1 on the left");
            Assert.That(run.Suggest(1), Is.EqualTo(new SpellOffer(Spell.Lightning, 1)), "fairy 2 on the right");
            Assert.That(run.Cast(Spell.Lightning, 1, 1), Is.EqualTo(1));
            Go(run, held, 0.1f);
            Assert.That(run.Suggest(1), Is.EqualTo(new SpellOffer(Spell.Lightning, -1)), "her side quiet: the other side's");
        }

        [Test]
        public void TheOrb_FloatsRoundTheFairy_AHandWithinReachTakesIt()
        {
            var run = Quiet();
            Steps(run, 0.2f);
            Assert.That(run.Current, Is.EqualTo(GardenRun.Stage.Breather), "nothing came: the wave is saved");
            Assert.IsTrue(run.OrbActive);
            Assert.That(Vector3.Distance(run.OrbAt, GardenRun.FairyAt(0, 1)), Is.LessThan(1.6f), "within her arms' reach");
            Assert.IsFalse(run.TryTakeOrb(run.OrbAt + Vector3.up * 2f, 0));
            int dust = run.Stardust;
            float magic = run.Magic;
            int health = run.TreeHealth;
            Assert.IsTrue(run.TryTakeOrb(run.OrbAt, 0));
            Assert.IsFalse(run.OrbActive);
            Assert.IsTrue(run.Stardust > dust || run.Magic > magic || run.TreeHealth >= health);
            Steps(run, GardenRun.BreatherSeconds);
            Assert.That(run.Current, Is.EqualTo(GardenRun.Stage.Wave));
            Assert.That(run.Wave, Is.EqualTo(2));
        }

        [Test]
        public void TheFreezeAndTheHeal_OnlyOnceOpened()
        {
            var run = Quiet();
            Assert.That(run.Cast(Spell.Freeze), Is.EqualTo(-1));
            Assert.That(run.Cast(Spell.Heal), Is.EqualTo(-1));
            var p = new GardenProgress { Stardust = GardenProgress.HealAt };
            run.Spells = p.SpellMask;
            var bug = run.Add(CreatureKind.Bug, -1, -5.4f);
            Assert.That(run.Cast(Spell.Freeze), Is.EqualTo(1));
            float x = bug.Position.x;
            Steps(run, 2f);
            Assert.That(bug.Position.x, Is.EqualTo(x), "frozen");
            Steps(run, 2f);
            Assert.That(bug.Position.x, Is.GreaterThan(x));
            Steps(run, 6f);
            int hurt = run.TreeHealth;
            Assert.That(run.Cast(Spell.Heal), Is.EqualTo(0));
            Assert.That(run.TreeHealth, Is.EqualTo(Mathf.Min(run.TreeMax, hurt + GardenRun.HealAmount)));
        }

        [Test]
        public void Progress_KeepsStardust_OpensWingsDressesSpells_InOrder()
        {
            var p = new GardenProgress();
            Assert.IsTrue(p.WingsOpen(0) && p.DressOpen(0));
            Assert.IsFalse(p.WingsOpen(1) || p.HasFreeze);
            var run = new GardenRun(FairyDifficulty.Medium, 1, 1);
            var bot = GardenBot.Good(1);
            float t = 0f;
            while (!run.Over && run.WavesCleared < 6 && t < 900f)
            {
                if (run.Current == GardenRun.Stage.Transform) run.Transform();
                bot.Act(run, t);
                run.Step(0.05f);
                t += 0.05f;
            }
            var opened = p.Add(run, t);
            Assert.That(p.Stardust, Is.EqualTo(run.Stardust));
            Assert.That(p.Waves, Is.EqualTo(run.WavesCleared));
            Assert.IsTrue(opened.Best);
            Assert.That(p.Best[(int)FairyDifficulty.Medium], Is.EqualTo(run.WavesCleared));
            Assert.IsTrue(opened.Wings, "the first wings come within a few waves");
            var back = GardenProgress.Load(p.Save());
            Assert.That(back.Save(), Is.EqualTo(p.Save()));
            Assert.That(GardenProgress.Load("dust=x;best=;dress=9").Dress, Is.EqualTo(0), "garbage reads as a fresh start");
        }

        // ---------------------------------------------------------------------------------------------- the balance

        /// <summary>Runs played by <paramref name="make"/>'s simulated players (one a fairy): waves saved and minutes,
        /// averaged, and the spells cast a minute.</summary>
        static (float waves, float minutes, float spells) Play(Func<int, GardenBot> make, FairyDifficulty d, int runs, int players = 1)
        {
            float waves = 0f, minutes = 0f, spells = 0f;
            for (int k = 0; k < runs; k++)
            {
                var run = new GardenRun(d, players, 100 + k);
                var bots = new GardenBot[players];
                for (int p = 0; p < players; p++)
                {
                    bots[p] = make(k * 7 + p);
                    bots[p].Player = p;
                }
                float t = 0f;
                while (!run.Over && run.WavesCleared < 40 && t < 3600f)
                {
                    if (run.Current == GardenRun.Stage.Transform) run.Transform();
                    else
                        foreach (var bot in bots)
                            bot.Act(run, t);
                    run.Step(0.05f);
                    t += 0.05f;
                }
                waves += run.WavesCleared;
                minutes += t / 60f;
                foreach (var bot in bots) spells += bot.Casts / Mathf.Max(1f, t / 60f);
            }
            return (waves / runs, minutes / runs, spells / runs);
        }

        [Test]
        public void Balance_ByDifficulty()
        {
            var profiles = new (string name, Func<int, GardenBot> make)[]
            {
                ("perfect", GardenBot.Perfect), ("good", GardenBot.Good), ("child", GardenBot.Child),
                ("struggling", GardenBot.Struggling), ("still", k => GardenBot.Still()),
            };
            var table = new Dictionary<(string, FairyDifficulty), float>();
            foreach (var (name, make) in profiles)
            {
                var line = $"{name,-11}";
                foreach (FairyDifficulty d in Enum.GetValues(typeof(FairyDifficulty)))
                {
                    var (waves, minutes, spells) = Play(make, d, 6);
                    table[(name, d)] = waves;
                    line += $"  {d}: {waves,5:0.0} waves ({minutes,4:0.0} min, {spells,4:0} spells/min)";
                }
                Debug.Log(line);
            }
            Assert.That(table[("still", FairyDifficulty.Easy)], Is.LessThanOrEqualTo(2f), "doing nothing loses soon");
            Assert.That(table[("struggling", FairyDifficulty.Easy)], Is.InRange(2f, 9f));
            Assert.That(table[("child", FairyDifficulty.Easy)], Is.InRange(6f, 18f));
            // 2026-11-17: the wand's charge between spells made a good player ~25 % slower (good Medium 15 -> ~12)
            Assert.That(table[("good", FairyDifficulty.Medium)], Is.InRange(9f, 26f));
            Assert.That(table[("perfect", FairyDifficulty.Hard)], Is.LessThan(40f), "everyone's garden falls in the end");
            Assert.That(table[("perfect", FairyDifficulty.Hard)], Is.GreaterThan(table[("good", FairyDifficulty.Hard)]));
            Assert.That(table[("good", FairyDifficulty.Easy)], Is.GreaterThan(table[("good", FairyDifficulty.Hard)]));
        }

        [Test]
        public void Balance_TwoFairies()
        {
            foreach (var (name, make) in new (string, Func<int, GardenBot>)[] { ("good", GardenBot.Good), ("child", GardenBot.Child) })
            {
                var (one, _, _) = Play(make, FairyDifficulty.Medium, 4);
                var (two, minutes, spells) = Play(make, FairyDifficulty.Medium, 4, 2);
                Debug.Log($"{name,-6} Medium: one fairy {one:0.0} waves, two {two:0.0} ({minutes:0.0} min, {spells:0} spells/min each)");
                Assert.That(two, Is.GreaterThanOrEqualTo(one * 0.6f), "two fairies hold about as long as one");
            }
        }
    }

    /// <summary>
    /// A simulated player for <see cref="GardenRun"/> drawing spells in the air (2026-11-17): once its fairy's wand is
    /// charged (or <see cref="Anticipate"/> seconds before: a good player starts the next shape while the wand charges)
    /// and after its <see cref="Reaction"/>, it draws what would help (<see cref="GardenRun.Suggest"/>) on that side, at
    /// <see cref="DrawSpeed"/> HUD pixels a second along the shape (<see cref="SpellShapes"/>' size), stopping now and then
    /// (<see cref="Pauses"/> a second, 0.4-0.9 s each), plus the rest that ends a stroke; <see cref="Misread"/> of its
    /// strokes read as nothing (it draws again). A stroke that ends while the wand still charges is lost, like the game's.
    /// Misreads from SpellReaderTests: a careful adult ~10 %, a sloppy child ~19 % (some of those read as another spell:
    /// counted as lost here). The orb is taken when it comes past (<see cref="OrbChance"/>).
    /// </summary>
    public sealed class GardenBot
    {
        public float Reaction, DrawSpeed, Pauses, Misread, Anticipate, OrbChance;
        public bool Idle;
        /// <summary>Which fairy it plays.</summary>
        public int Player;
        /// <summary>Spells it has cast, and strokes lost (misread, or ended while charging).</summary>
        public int Casts, Lost;
        readonly System.Random _rng;
        bool _orbTried, _drawing;
        SpellOffer _plan;
        float _waited, _left, _pause, _last = -1f;

        GardenBot(int seed) => _rng = new System.Random(seed);

        public static GardenBot Perfect(int k) => new GardenBot(k)
        {
            Reaction = 0.3f, DrawSpeed = 950f, Pauses = 0f, Misread = 0f, Anticipate = 1f, OrbChance = 1f,
        };
        public static GardenBot Good(int k) => new GardenBot(k)
        {
            Reaction = 0.55f, DrawSpeed = 620f, Pauses = 0.1f, Misread = 0.1f, Anticipate = 0.4f, OrbChance = 0.9f,
        };
        public static GardenBot Child(int k) => new GardenBot(k)
        {
            Reaction = 1f, DrawSpeed = 380f, Pauses = 0.35f, Misread = 0.2f, Anticipate = 0f, OrbChance = 0.6f,
        };
        public static GardenBot Struggling(int k) => new GardenBot(k)
        {
            Reaction = 1.6f, DrawSpeed = 220f, Pauses = 0.6f, Misread = 0.35f, Anticipate = 0f, OrbChance = 0.35f,
        };
        public static GardenBot Still() => new GardenBot(0) { Idle = true };

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        static readonly Dictionary<Spell, float> Lengths = new Dictionary<Spell, float>();

        static float LengthOf(SpellOffer o)
        {
            if (!Lengths.TryGetValue(o.Spell, out float l))
                Lengths[o.Spell] = l = SpellShapes.Length(SpellShapes.PathOf(new SpellOffer(o.Spell, SpellOffer.Sided(o.Spell) ? 1 : 0), 1920f));
            return l;
        }

        public void Act(GardenRun run, float now)
        {
            float dt = _last < 0f ? 0.05f : Mathf.Clamp(now - _last, 0f, 0.2f);
            _last = now;
            if (Idle) return;
            if (run.Current == GardenRun.Stage.Breather)
            {
                _drawing = false;
                if (run.OrbActive && !_orbTried && run.StageTime > 1.5f)
                {
                    _orbTried = true;
                    if (_rng.NextDouble() < OrbChance) run.TryTakeOrb(run.OrbAt, Player);
                }
                return;
            }
            _orbTried = false;
            if (run.Current != GardenRun.Stage.Wave) return;
            if (!_drawing)
            {
                // waits for the wand (a good player starts a little before it's charged), notices what would help, draws it
                if (run.RechargeOf(Player) > Anticipate) return;
                var s = run.Suggest(Player);
                if (!s.HasValue)
                {
                    _waited = 0f;
                    return;
                }
                _waited += dt;
                if (_waited < Reaction * R(0.8f, 1.25f)) return;
                _plan = s.Value;
                _left = LengthOf(_plan) / DrawSpeed * R(0.85f, 1.2f) + 0.3f;
                _drawing = true;
                _pause = 0f;
                return;
            }
            if (_pause > 0f)
            {
                _pause -= dt;
                return;
            }
            if (Pauses > 0f && _rng.NextDouble() < Pauses * dt)
            {
                _pause = R(0.4f, 0.9f);
                return;
            }
            _left -= dt;
            if (_left > 0f) return;
            _drawing = false;
            _waited = 0f;
            if (_rng.NextDouble() < Misread || !run.Charged(Player))
            {
                Lost++;
                return;
            }
            // read: cast where it was drawn (the rules move a bolt to the side with something on it)
            if (run.Cast(_plan.Spell, Player, run.SideFor(_plan.Spell, _plan.Side)) >= 0) Casts++;
            else Lost++;
        }
    }
}
