using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fairy
{
    public enum FairyDifficulty { Easy, Medium, Hard }

    /// <summary>The body a creature is built on (art/fairy's gloom kit has one of each).</summary>
    public enum GloomBody { Blob, Imp, Bat, Puff, Bug, Jelly, Spiky, Ghost, Crystal, Fish }

    /// <summary>
    /// A kind of gloomy creature, made from the gloom kit's parts: a body, eyes, horns or ears, wings, a tail, a mouth,
    /// its colours (a hue and an accent; the gloom greys them, purifying brings them out).
    /// </summary>
    public sealed class Species
    {
        public int Id;
        public GloomBody Body;
        /// <summary>Which of the kit's eyes (0-3: round, sleepy, grumpy, one big), horns/ears (-1 none, 0-3), wings (-1
        /// none, 0 bat, 1 bug, 2 feathery), tail (-1 none, 0-1), mouth (0-2).</summary>
        public int Eyes, Horns = -1, Wings = -1, Tail = -1, Mouth;
        /// <summary>Hue (0..1) of its body and of its accents; how bright when purified.</summary>
        public float Hue, Accent, Saturation = 0.6f;
        /// <summary>Spots or stripes on its body (0 none, 1 spots, 2 stripes).</summary>
        public int Pattern;
        /// <summary>A size multiplier on the kind's size.</summary>
        public float Scale = 1f;
    }

    /// <summary>The fairy's spells, cast by following their shape on the screen with the wand (2026-11-16, the user:
    /// "cast spells by following the shape on the screen"; <see cref="SpellShapes"/>), the only magic that purifies (the
    /// user, the same evening: "dont allow the wand to kill enemies by itself, only with spells"): the lightning strikes
    /// every creature walking on its side (a zigzag along that side's path), the rainbow every creature flying on its
    /// side (an arc over that side of the sky), the seeds burst round the tree (a wave along the meadow), the shield (a
    /// circle round the tree) throws back, the big magic (a star, the crystal full) purifies everything; opened with
    /// stardust: the freeze (a spiral in the sky) and the healing rain (a heart over the crown).</summary>
    public enum Spell { Shield, Seeds, Burst, Freeze, Heal, Lightning, Rainbow }

    /// <summary>A spell's shape on the screen for a fairy to follow: which spell, and where (-1 the left side of the
    /// garden, +1 the right; 0 the whole garden).</summary>
    public readonly struct SpellOffer : IEquatable<SpellOffer>
    {
        public readonly Spell Spell;
        public readonly int Side;

        public SpellOffer(Spell spell, int side = 0)
        {
            Spell = spell;
            Side = Sided(spell) ? (side < 0 ? -1 : 1) : 0;
        }

        /// <summary>The spells that strike one side of the garden.</summary>
        public static bool Sided(Spell s) => s is Spell.Lightning or Spell.Rainbow;

        public bool Equals(SpellOffer o) => o.Spell == Spell && o.Side == Side;
        public override bool Equals(object obj) => obj is SpellOffer o && Equals(o);
        public override int GetHashCode() => (int)Spell * 3 + Side + 1;
        public override string ToString() => Side == 0 ? Spell.ToString() : $"{Spell} {(Side < 0 ? "left" : "right")}";
    }

    /// <summary>The fairy's element (her spells' colours, chosen on the menu).</summary>
    public enum Element { Light, Fire, Water, Nature }

    public enum CreatureKind
    {
        /// <summary>Small and quick, on the ground.</summary>
        Bug,
        /// <summary>Thorny, on the ground, two hits.</summary>
        Thorn,
        /// <summary>Big and slow, on the ground, five hits.</summary>
        Brute,
        /// <summary>Flies in from the sky toward the tree's crown.</summary>
        Wisp,
        /// <summary>A puff on the ground: purified, it splits into two bugs.</summary>
        Puff,
        /// <summary>Every fifth wave: huge, crowned, stays at the tree pounding it until purified.</summary>
        Boss,
    }

    public enum CreatureState { Coming, Attacking, Purified, Gone }

    /// <summary>The power-up floating round the fairy between waves: touch it with a hand.</summary>
    public enum OrbKind { Heart, Star, Seed }

    /// <summary>One gloomy creature on its way to the tree.</summary>
    public sealed class Creature
    {
        public int Id;
        public CreatureKind Kind;
        public Species Species;
        public CreatureState State;
        /// <summary>Metres in the garden: x across (+ = the screen's right), y up, z away from the camera.</summary>
        public Vector3 Position;
        /// <summary>-1: it came from the left, +1: from the right.</summary>
        public int Side;
        public int Hp, MaxHp;
        /// <summary>Metres a second.</summary>
        public float Speed;
        /// <summary>Its size in metres (as drawn, before the drawing's own scale).</summary>
        public float Size;
        public float Stunned, Flash, StateTime, Age, Phase, AttackTimer;
        /// <summary>A flyer's way: from where it came in to the tree's crown, and how far along (0..1).</summary>
        public Vector3 From, To;
        public float Along;

        public bool Flying => Kind == CreatureKind.Wisp;
        public bool Alive => State == CreatureState.Coming || State == CreatureState.Attacking;
        public bool IsBoss => Kind == CreatureKind.Boss;
        /// <summary>How purified it looks (a hit brightens it a little; 1 = purified).</summary>
        public float Glow => State == CreatureState.Purified ? 1f : MaxHp > 0 ? 0.35f * (1f - (float)Hp / MaxHp) : 0f;
    }

    public enum GardenEventKind
    {
        WaveStart, Spawned, Hit, Purified, Split, TreeHit, Dissolved, Lightning, Rainbow, ShieldUp, Blocked, Seeds, Burst, Freeze, Heal,
        WaveCleared, OrbShown, OrbTaken, OrbLost, TransformReady, Transformed, BossCame, GameOver, Charged,
    }

    public struct GardenEvent
    {
        public GardenEventKind Kind;
        /// <summary>The creature's id, or -1.</summary>
        public int Creature;
        public int Player;
        public Vector3 At;
        public int Count;
        public Spell Spell;
        /// <summary>A sided spell's side (-1 left, +1 right), else 0.</summary>
        public int Side;
        public OrbKind Orb;
        public CreatureKind What;
    }

    /// <summary>A wave's numbers (<see cref="GardenLevel.Wave"/>).</summary>
    public struct WavePlan
    {
        public int Bugs, Thorns, Brutes, Puffs, Wisps;
        public bool Boss;
        /// <summary>Times the kinds' own speeds.</summary>
        public float Speed;
        public int Total => Bugs + Thorns + Brutes + Puffs + Wisps + (Boss ? 1 : 0);
    }

    /// <summary>
    /// Every number of the garden's defence in one place (2026-11-16): how a wave grows (a creature more each wave and
    /// 5 % quicker, as the user's plan said, then shaped by the bots in GardenRunTests), the kinds, the tree, the spells.
    /// </summary>
    public static class GardenLevel
    {
        public static int TreeHealth(FairyDifficulty d, int players) =>
            (d == FairyDifficulty.Easy ? 16 : d == FairyDifficulty.Medium ? 14 : 11) + (players > 1 ? 3 : 0);

        static float CountScale(FairyDifficulty d) => d == FairyDifficulty.Easy ? 0.8f : d == FairyDifficulty.Medium ? 1f : 1.25f;
        static float SpeedScale(FairyDifficulty d) => d == FairyDifficulty.Easy ? 0.7f : d == FairyDifficulty.Medium ? 1f : 1.15f;

        public static WavePlan Wave(int n, FairyDifficulty d, int players)
        {
            float k = CountScale(d) * (players > 1 ? 1.5f : 1f);
            bool boss = n % 5 == 0;
            float bossCut = boss ? 0.6f : 1f;
            // the counts stop growing at what the box draws well (later waves toughen instead: HpOf, the speed)
            float most = players > 1 ? 1.4f : 1f;
            int ground = Mathf.Clamp(Mathf.RoundToInt((3f + 2.1f * n + 0.03f * n * n) * k * bossCut), 2, Mathf.RoundToInt(MaxGround * most));
            int brutes = n >= 4 ? Mathf.RoundToInt((1 + (n - 4) / 3) * k * bossCut) : 0;
            int puffs = n >= 6 ? Mathf.RoundToInt((n - 4) / 3 * k * bossCut) : 0;
            int thorns = n >= 2 ? Mathf.RoundToInt(ground * Mathf.Min(0.4f, 0.1f * (n - 1))) : 0;
            int bugs = Mathf.Max(1, ground - brutes - puffs - thorns);
            int wisps = n >= 2 ? Mathf.Min(Mathf.RoundToInt(MaxWisps * most), Mathf.RoundToInt((0.9f * (n - 1) + 0.02f * n * n) * k * bossCut)) : 0;
            return new WavePlan
            {
                Bugs = bugs, Thorns = thorns, Brutes = brutes, Puffs = puffs, Wisps = wisps, Boss = boss,
                Speed = SpeedScale(d) * (Mathf.Min(2f, 1f + 0.05f * (n - 1)) + 0.03f * Mathf.Max(0, n - 25)),
            };
        }

        /// <summary>Walkers in a run from one side (one lightning for all of them), wisps in a flock.</summary>
        public static int RunSize(int n) => Mathf.Min(10, 2 + n / 3);
        public static int FlockSize(int n) => Mathf.Min(5, 1 + n / 5);

        /// <summary>Seconds between the groups of a wave (a child follows a shape in 4-6 s): 7.5 at the start, less each
        /// wave, at least 2.2; longer on Easy, shorter on Hard.</summary>
        public static float Gap(int n, FairyDifficulty d) =>
            Mathf.Max(2.2f, 7.5f - 0.25f * (n - 1)) * (d == FairyDifficulty.Easy ? 1.3f : d == FairyDifficulty.Hard ? 0.85f : 1f);

        /// <summary>How often a group comes at once with the next, from the other side (from wave 6).</summary>
        public static float PairChance(int n) => Mathf.Clamp(0.06f * (n - 5), 0f, 0.5f);

        /// <summary>Metres a second (the wisps along their flight). The stage was made 0.6 times as wide on 2026-11-16 (the
        /// creatures and the fairy were specks from the old camera): the speeds with it, so every timing stayed.</summary>
        public static float SpeedOf(CreatureKind k) => k switch
        {
            CreatureKind.Bug => 0.9f, CreatureKind.Thorn => 0.63f, CreatureKind.Brute => 0.37f, CreatureKind.Puff => 0.54f,
            CreatureKind.Wisp => 0.84f, _ => 0.27f,
        };

        /// <summary>A wave's most creatures on the ground and in the air (one player; two: 1.4 times).</summary>
        public const int MaxGround = 45, MaxWisps = 18;

        /// <summary>Spells a creature takes (2026-11-16: the spells are the only magic, so one is one hit): one for most,
        /// two for a brute (three from wave 12), the thorns and the wisps toughening from wave 10-12, the boss several
        /// (more each time it comes), everything a little more from wave 16.</summary>
        public static int HpOf(CreatureKind k, int wave)
        {
            int hp = k switch
            {
                CreatureKind.Bug => 1, CreatureKind.Thorn => wave >= 10 ? 2 : 1, CreatureKind.Brute => wave >= 12 ? 3 : 2,
                CreatureKind.Puff => 1, CreatureKind.Wisp => wave >= 12 ? 2 : 1,
                _ => 2 + wave / 5,
            };
            return wave > 15 ? Mathf.Max(hp, Mathf.RoundToInt(hp * (1f + 0.06f * (wave - 15)))) : hp;
        }

        /// <summary>The tree's health a creature takes when it reaches the tree (the boss: each time it pounds).</summary>
        public static int DamageOf(CreatureKind k) => k switch
        {
            CreatureKind.Bug => 1, CreatureKind.Thorn => 2, CreatureKind.Brute => 3, CreatureKind.Puff => 2, CreatureKind.Wisp => 1,
            _ => 2,
        };

        public static float SizeOf(CreatureKind k) => k switch
        {
            CreatureKind.Bug => 0.55f, CreatureKind.Thorn => 0.8f, CreatureKind.Brute => 1.3f, CreatureKind.Puff => 0.85f,
            CreatureKind.Wisp => 0.6f, _ => 2.1f,
        };

        /// <summary>Stardust for purifying one.</summary>
        public static int ValueOf(CreatureKind k) => k switch
        {
            CreatureKind.Bug => 2, CreatureKind.Thorn => 3, CreatureKind.Brute => 8, CreatureKind.Puff => 3, CreatureKind.Wisp => 2,
            _ => 30,
        };

        /// <summary>Seconds before a spell can be cast again (the transformation's third step makes the shield's and the
        /// seeds' quicker). The lightning and the rainbow have none of their own: the wand's charge
        /// (<see cref="Recharge"/>) is their pace.</summary>
        public static float CooldownOf(Spell s, int level) => s switch
        {
            Spell.Shield => level >= 3 ? 9f : 12f,
            Spell.Seeds => level >= 3 ? 7f : 9f,
            Spell.Freeze => 14f,
            Spell.Heal => 18f,
            _ => 0f,
        };

        /// <summary>Seconds a fairy's wand needs to charge again after any spell (2026-11-17, the user: "some sort of
        /// countdown between spells"): the spells are drawn freely now, a bolt in about a second, so this sets the pace.
        /// Quicker from the transformation's third step (the shield and the seeds come back quicker too).</summary>
        public static float Recharge(FairyDifficulty d, int level) =>
            (d == FairyDifficulty.Easy ? 1.4f : d == FairyDifficulty.Medium ? 1.6f : 1.9f) * (level >= 3 ? 0.8f : 1f);

        /// <summary>Hits a spell gives each creature it touches: two from the transformation's second step.</summary>
        public static int SpellPower(int level) => level >= 2 ? 2 : 1;

        public static float ShieldSeconds(FairyDifficulty d) => d == FairyDifficulty.Easy ? 5f : d == FairyDifficulty.Medium ? 4f : 3.5f;

        /// <summary>The crystal fills by itself in this many seconds of a wave (0.7 of it from the transformation's
        /// fourth step), and a little more with every creature purified (when it filled from purifying alone, the big
        /// waves fed a burst every few seconds).</summary>
        public const float MagicSeconds = 40f, MagicPerCreature = 0.02f, MagicPerBig = 0.06f;
        public static float MagicSecondsAt(int level) => level >= 4 ? MagicSeconds * 0.7f : MagicSeconds;
    }

    /// <summary>
    /// Zana!'s garden defence (2026-11-16, from the user's plan: "a fairy defending her enchanted garden; dark creatures
    /// creep in from the edges in waves; spells cast with hand gestures zap them before they reach the fairy tree at the
    /// centre"). Pure logic, no engine: tests play whole runs with simulated players.
    /// <list type="bullet">
    /// <item><b>Waves</b>: creatures walk in from the left and the right along the ground and fly in from the sky toward
    /// the tree; each wave a little bigger and 5 % quicker (<see cref="GardenLevel"/>), every fifth with a boss.
    /// Reaching the tree they take its health (the boss stays and pounds). Purified (never killed), a creature turns
    /// bright and happy and floats away; a puff splits into two bugs.</item>
    /// <item><b>Spells</b> (<see cref="Cast"/>), the only magic that purifies, drawn in the air with the wand (2026-11-17:
    /// <see cref="SpellCaster"/> reads the shape); after each the fairy's wand charges again (<see cref="RechargeOf"/>);
    /// <see cref="Suggest"/> says what would help now (the HUD's spell book glows it): the lightning
    /// strikes everything walking on its side, the rainbow everything flying on its side, the seeds burst round the
    /// tree, the shield (a bubble round the tree) bounces whoever touches it, the big magic (the crystal full) purifies
    /// everything, the freeze stops everything a while, the healing rain gives the tree health back.</item>
    /// <item><b>Between waves</b> a breather: stardust for the wave, and an orb floating round the fairy (a heart, a star
    /// or a seed: touch it with the wand). After every fifth wave the fairy transforms (both arms up:
    /// <see cref="Transform"/>): the spells hit twice, then the shield and the seeds come back quicker, then the
    /// crystal fills quicker.</item>
    /// </list>
    /// Positions in metres: the tree's trunk at <see cref="Tree"/>, its crown at <see cref="Canopy"/>, the fairies at
    /// <see cref="FairyAt"/>.
    /// </summary>
    public sealed class GardenRun
    {
        public enum Stage { Wave, Breather, Transform, Over }

        public const float TreeZ = 4f, TreeRadius = 1.6f, CanopyY = 6.0f, CanopyRadius = 2.3f;
        /// <summary>Where the ground creatures come in (just past the screen's edge), the edge of the picture at the
        /// paths (a creature further out is off screen: no spell reaches it), the shield's bubble and the seeds'
        /// burst round the trunk.</summary>
        public const float SpawnX = 9.6f, LaneEdge = 7.8f, ShieldRadius = 2.8f, SeedRadius = 4.2f;
        /// <summary>Metres a creature is thrown back: by the lightning, along its flight by the rainbow, by the shield, by
        /// the seeds.</summary>
        public const float LightningKnock = 0.8f, RainbowKnock = 1.2f, ShieldKnock = 1.5f, ShieldKnockAir = 1.7f, SeedKnock = 1.8f;
        public const float BreatherSeconds = 7.5f, OrbSeconds = 6.8f, OrbReach = 0.8f, FreezeSeconds = 3.5f;
        public const int MaxLevel = 4, HealAmount = 3;

        public static Vector3 Tree => new Vector3(0f, 0f, TreeZ);
        public static Vector3 Canopy => new Vector3(0f, CanopyY, TreeZ);

        /// <summary>Where fairy <paramref name="i"/> hovers (her hips): well in front of the tree, near the camera, so she
        /// is big on the screen, flying just above the tree's door (lower, her boots hid it).</summary>
        public static Vector3 FairyAt(int i, int players) =>
            players > 1 ? new Vector3(i == 0 ? -1.5f : 1.5f, 2.85f, -1.2f) : new Vector3(0f, 2.85f, -1.2f);

        public readonly FairyDifficulty Difficulty;
        public readonly int Players;
        readonly System.Random _rng;
        readonly int _seed;

        public Stage Current { get; private set; }
        public float StageTime { get; private set; }
        /// <summary>The wave being fought (the one just cleared, in a breather).</summary>
        public int Wave { get; private set; }
        public int WavesCleared { get; private set; }
        public int TreeHealth { get; private set; }
        public int TreeMax { get; private set; }
        /// <summary>The crystal: purifying fills it (1 = the burst is ready).</summary>
        public float Magic { get; private set; }
        /// <summary>The transformation (1-4).</summary>
        public int Level { get; private set; } = 1;
        public int Stardust { get; private set; }
        public int Purified { get; private set; }
        public float ShieldTime { get; private set; }
        public float FrozenTime { get; private set; }
        public bool Hurt { get; private set; }
        public WavePlan Plan { get; private set; }
        public float WaveTime { get; private set; }
        /// <summary>The spells that can be cast (a mask of 1 &lt;&lt; <see cref="Spell"/>): the freeze and the healing rain
        /// once bought.</summary>
        public int Spells = BaseSpells;
        public const int BaseSpells = (1 << (int)Spell.Shield) | (1 << (int)Spell.Seeds) | (1 << (int)Spell.Burst) |
                                      (1 << (int)Spell.Lightning) | (1 << (int)Spell.Rainbow);

        public readonly List<Creature> Creatures = new List<Creature>();
        public readonly List<GardenEvent> Events = new List<GardenEvent>();
        readonly float[] _cooldown = new float[7];
        readonly List<(float at, CreatureKind kind, int side)> _schedule = new List<(float, CreatureKind, int)>();
        readonly List<Species> _species = new List<Species>();
        int _next, _ids;

        // the orb between waves
        public bool OrbActive { get; private set; }
        public OrbKind Orb { get; private set; }
        public Vector3 OrbAt { get; private set; }
        float _orbT;

        public bool Over => Current == Stage.Over;

        public GardenRun(FairyDifficulty difficulty, int players, int seed)
        {
            Difficulty = difficulty;
            Players = Mathf.Clamp(players, 1, 2);
            _seed = seed;
            _rng = new System.Random(seed);
            TreeMax = TreeHealth = GardenLevel.TreeHealth(difficulty, Players);
            MakeSpecies();
            StartWave(1);
        }

        public bool Has(Spell s) => (Spells & (1 << (int)s)) != 0;
        /// <summary>Seconds before <paramref name="s"/> can be cast again (0 = ready).</summary>
        public float Cooldown(Spell s) => _cooldown[(int)s];
        public bool Ready(Spell s) => Has(s) && (s == Spell.Burst ? Magic >= 1f : _cooldown[(int)s] <= 0f);
        public IReadOnlyList<Species> AllSpecies => _species;

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        void Emit(GardenEventKind kind, Creature c = null, int player = -1, int count = 0)
        {
            Events.Add(new GardenEvent
            {
                Kind = kind, Creature = c != null ? c.Id : -1, Player = player, At = c != null ? c.Position : Vector3.zero,
                Count = count, What = c != null ? c.Kind : CreatureKind.Bug,
            });
        }

        // ---------------------------------------------------------------------------------------------- the creatures

        /// <summary>One species a kind, in the garden's colours, from the run's seed (their meshes are built once).</summary>
        void MakeSpecies()
        {
            var bodies = new[] { GloomBody.Bug, GloomBody.Spiky, GloomBody.Blob, GloomBody.Bat, GloomBody.Puff, GloomBody.Imp };
            for (int k = 0; k < bodies.Length; k++)
            {
                _species.Add(new Species
                {
                    Id = 1000 + k, Body = bodies[k],
                    Eyes = k == 5 ? 2 : _rng.Next(0, 4), Horns = k is 1 or 5 ? _rng.Next(0, 4) : _rng.Next(-1, 2), Mouth = _rng.Next(0, 3),
                    Wings = k == 3 ? 0 : k == 0 ? 1 : -1, Tail = k is 2 or 5 ? _rng.Next(0, 2) : -1,
                    Hue = Mathf.Repeat(0.72f + k * 0.13f + R(-0.04f, 0.04f), 1f), Accent = Mathf.Repeat(0.12f + k * 0.21f, 1f),
                    Saturation = R(0.55f, 0.75f), Pattern = _rng.Next(0, 3),
                });
            }
        }

        Species SpeciesOf(CreatureKind k) => _species[(int)k switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, _ => 5 }];

        Creature Spawn(CreatureKind kind, int side, Vector3? at = null)
        {
            var c = new Creature
            {
                Id = ++_ids, Kind = kind, Side = side, Species = SpeciesOf(kind), State = CreatureState.Coming,
                MaxHp = GardenLevel.HpOf(kind, Wave), Speed = GardenLevel.SpeedOf(kind) * Plan.Speed * R(0.9f, 1.1f),
                Size = GardenLevel.SizeOf(kind) * R(0.92f, 1.08f), Phase = R(0f, 6.28f),
            };
            c.Hp = c.MaxHp;
            if (kind == CreatureKind.Wisp)
            {
                c.From = new Vector3(side * R(8.0f, 9.2f), R(6.4f, 7.8f), TreeZ + R(-1.2f, 0.9f));
                c.To = Canopy + new Vector3(side * CanopyRadius, R(-0.6f, 0.8f), 0f);
                c.Position = at ?? c.From;
            }
            else c.Position = at ?? new Vector3(side * SpawnX, 0f, TreeZ + R(-1.1f, 0.9f));
            Creatures.Add(c);
            Emit(GardenEventKind.Spawned, c);
            if (kind == CreatureKind.Boss) Emit(GardenEventKind.BossCame, c);
            return c;
        }

        // ---------------------------------------------------------------------------------------------- waves

        void StartWave(int n)
        {
            Wave = n;
            Current = Stage.Wave;
            StageTime = 0f;
            WaveTime = 0f;
            Hurt = false;
            OrbActive = false;
            Plan = GardenLevel.Wave(n, Difficulty, Players);
            _schedule.Clear();
            _next = 0;
            // the wave comes in groups (2026-11-16, the spells being the only magic: a spell answers a group): runs of
            // walkers from one side, flocks of wisps from one side of the sky, the boss on its own; a group every
            // GardenLevel.Gap seconds, two at once (from both sides) more and more as the waves go on
            var ground = new List<CreatureKind>();
            for (int i = 0; i < Plan.Bugs; i++) ground.Add(CreatureKind.Bug);
            for (int i = 0; i < Plan.Thorns; i++) ground.Add(CreatureKind.Thorn);
            for (int i = 0; i < Plan.Brutes; i++) ground.Add(CreatureKind.Brute);
            for (int i = 0; i < Plan.Puffs; i++) ground.Add(CreatureKind.Puff);
            for (int i = ground.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (ground[i], ground[j]) = (ground[j], ground[i]);
            }
            var groups = new List<List<CreatureKind>>();
            int runSize = GardenLevel.RunSize(n), flockSize = GardenLevel.FlockSize(n);
            for (int i = 0; i < ground.Count; i += runSize) groups.Add(ground.GetRange(i, Mathf.Min(runSize, ground.Count - i)));
            var flocks = new List<List<CreatureKind>>();
            for (int left = Plan.Wisps; left > 0; left -= flockSize)
            {
                var flock = new List<CreatureKind>();
                for (int i = 0; i < Mathf.Min(flockSize, left); i++) flock.Add(CreatureKind.Wisp);
                flocks.Add(flock);
            }
            // the flocks among the runs (never first: a wave opens on the ground), the boss a third of the way in
            foreach (var flock in flocks) groups.Insert(1 + _rng.Next(Mathf.Max(1, groups.Count)), flock);
            if (Plan.Boss) groups.Insert(Mathf.Min(groups.Count, 1 + groups.Count / 3), new List<CreatureKind> { CreatureKind.Boss });
            float at = 0f, gap = GardenLevel.Gap(n, Difficulty), pair = GardenLevel.PairChance(n);
            int side = _rng.Next(2) == 0 ? -1 : 1;
            bool together = false;
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                bool flying = group[0] == CreatureKind.Wisp;
                // the sides by turns, now and then the same side again; a group coming at once with the last one from the
                // other side
                side = together || _rng.NextDouble() < 0.75 ? -side : side;
                for (int i = 0; i < group.Count; i++)
                    _schedule.Add((at + i * (flying ? 0.55f : 0.75f) + R(0f, 0.15f), group[i], side));
                // the next group: after the gap, or at once from the other side
                together = !together && g + 1 < groups.Count && _rng.NextDouble() < pair;
                if (!together) at += gap * R(0.85f, 1.15f) + group.Count * 0.3f;
            }
            _schedule.Sort((a, b) => a.at.CompareTo(b.at));
            Emit(GardenEventKind.WaveStart, count: n);
        }

        /// <summary>The run's time goes on <paramref name="dt"/> seconds.</summary>
        public void Step(float dt)
        {
            if (dt <= 0f) return;
            StageTime += dt;
            for (int i = 0; i < _cooldown.Length; i++) _cooldown[i] = Mathf.Max(0f, _cooldown[i] - dt);
            for (int k = 0; k < _recharge.Length; k++)
            {
                if (_recharge[k] <= 0f) continue;
                _recharge[k] = Mathf.Max(0f, _recharge[k] - dt);
                if (_recharge[k] <= 0f) Events.Add(new GardenEvent { Kind = GardenEventKind.Charged, Player = k, Creature = -1 });
            }
            ShieldTime = Mathf.Max(0f, ShieldTime - dt);
            FrozenTime = Mathf.Max(0f, FrozenTime - dt);
            switch (Current)
            {
                case Stage.Wave:
                    WaveTime += dt;
                    Magic = Mathf.Min(1f, Magic + dt / GardenLevel.MagicSecondsAt(Level));
                    while (_next < _schedule.Count && _schedule[_next].at <= WaveTime)
                    {
                        var (_, kind, side) = _schedule[_next++];
                        Spawn(kind, side);
                    }
                    break;
                case Stage.Breather:
                    StepOrb(dt);
                    if (StageTime >= BreatherSeconds) StartWave(Wave + 1);
                    break;
            }
            Move(dt);
            if (Current == Stage.Wave && TreeHealth <= 0)
            {
                Current = Stage.Over;
                StageTime = 0f;
                Emit(GardenEventKind.GameOver, count: WavesCleared);
                return;
            }
            if (Current == Stage.Wave && !KeepWave && _next >= _schedule.Count && !AnyAlive()) Cleared();
        }

        bool AnyAlive()
        {
            foreach (var c in Creatures)
                if (c.Alive) return true;
            return false;
        }

        void Cleared()
        {
            WavesCleared = Wave;
            int dust = 5 * Wave + (Hurt ? 0 : 10);
            Stardust += dust;
            Emit(GardenEventKind.WaveCleared, count: dust);
            StageTime = 0f;
            if (Wave % 5 == 0 && Level < MaxLevel)
            {
                Current = Stage.Transform;
                Emit(GardenEventKind.TransformReady, count: Level + 1);
            }
            else StartBreather();
        }

        void StartBreather()
        {
            Current = Stage.Breather;
            StageTime = 0f;
            OrbActive = true;
            _orbT = 0f;
            // a heart when the tree has been hurt, else by turns a star or a seed
            Orb = TreeHealth < TreeMax && _rng.NextDouble() < 0.6 ? OrbKind.Heart : _rng.NextDouble() < 0.5 ? OrbKind.Star : OrbKind.Seed;
            StepOrb(0f);
            Events.Add(new GardenEvent { Kind = GardenEventKind.OrbShown, Orb = Orb, At = OrbAt, Creature = -1 });
        }

        /// <summary>After every fifth wave, both arms up (or the game's own time): the fairy transforms.</summary>
        public void Transform()
        {
            if (Current != Stage.Transform) return;
            Level = Mathf.Min(MaxLevel, Level + 1);
            Emit(GardenEventKind.Transformed, count: Level);
            StartBreather();
        }

        /// <summary>The orb loops round the fairies (within their hands' reach) and fades after <see cref="OrbSeconds"/>.</summary>
        void StepOrb(float dt)
        {
            if (!OrbActive) return;
            _orbT += dt;
            var middle = Players > 1 ? (FairyAt(0, 2) + FairyAt(1, 2)) * 0.5f : FairyAt(0, 1);
            float a = _orbT * 1.25f;
            float wide = Players > 1 ? 2.6f : 0.95f;
            OrbAt = middle + new Vector3(Mathf.Sin(a) * wide, 0.55f + Mathf.Sin(a * 2f) * 0.45f, -0.25f);
            if (_orbT >= OrbSeconds)
            {
                OrbActive = false;
                Events.Add(new GardenEvent { Kind = GardenEventKind.OrbLost, Orb = Orb, At = OrbAt, Creature = -1 });
            }
        }

        /// <summary>A fairy's hand at <paramref name="hand"/> (garden metres): the orb taken if it's within reach.</summary>
        public bool TryTakeOrb(Vector3 hand, int player)
        {
            if (!OrbActive || Vector3.Distance(hand, OrbAt) > OrbReach) return false;
            OrbActive = false;
            switch (Orb)
            {
                case OrbKind.Heart:
                    TreeHealth = Mathf.Min(TreeMax, TreeHealth + 3);
                    break;
                case OrbKind.Star:
                    Magic = Mathf.Min(1f, Magic + 0.6f);
                    break;
                default:
                    _cooldown[(int)Spell.Seeds] = 0f;
                    _cooldown[(int)Spell.Shield] = 0f;
                    Stardust += 15;
                    break;
            }
            Events.Add(new GardenEvent { Kind = GardenEventKind.OrbTaken, Orb = Orb, At = OrbAt, Player = player, Creature = -1 });
            return true;
        }

        // ---------------------------------------------------------------------------------------------- moving

        void Move(float dt)
        {
            bool frozen = FrozenTime > 0f;
            for (int i = 0; i < Creatures.Count; i++)
            {
                var c = Creatures[i];
                c.Age += dt;
                c.StateTime += dt;
                c.Flash = Mathf.Max(0f, c.Flash - dt * 4f);
                c.Stunned = Mathf.Max(0f, c.Stunned - dt);
                switch (c.State)
                {
                    case CreatureState.Purified:
                        // bright and happy, it floats up and away
                        c.Position += new Vector3(c.Side * 0.6f, 1.4f, 0.3f) * dt;
                        if (c.StateTime > 2.2f) c.State = CreatureState.Gone;
                        continue;
                    case CreatureState.Gone:
                        continue;
                    case CreatureState.Attacking:
                        if (ShieldTime > 0f)
                        {
                            Bounce(c);
                            continue;
                        }
                        if (frozen || c.Stunned > 0f) continue;
                        c.AttackTimer -= dt;
                        if (c.AttackTimer <= 0f)
                        {
                            c.AttackTimer = 2.5f;
                            HurtTree(c);
                        }
                        continue;
                }
                if (frozen || c.Stunned > 0f) continue;
                if (c.Flying)
                {
                    float length = Mathf.Max(1f, Vector3.Distance(c.From, c.To));
                    c.Along = Mathf.Min(1f, c.Along + c.Speed * dt / length);
                    // a curve over toward the crown, wobbling
                    var p = Vector3.Lerp(c.From, c.To, c.Along);
                    p.y += Mathf.Sin(c.Along * Mathf.PI) * 1.2f + Mathf.Sin(c.Age * 3f + c.Phase) * 0.25f;
                    c.Position = p;
                    float reach = Vector3.Distance(c.Position, Canopy);
                    if (ShieldTime > 0f && reach < ShieldRadius + 0.6f) Bounce(c);
                    else if (c.Along >= 1f) Reached(c);
                }
                else
                {
                    var p = c.Position;
                    p.x -= c.Side * c.Speed * dt;
                    c.Position = p;
                    float x = Mathf.Abs(p.x);
                    if (ShieldTime > 0f && x < ShieldRadius) Bounce(c);
                    else if (x <= TreeRadius + c.Size * 0.45f) Reached(c);
                }
            }
            Creatures.RemoveAll(c => c.State == CreatureState.Gone);
        }

        void Reached(Creature c)
        {
            if (c.IsBoss)
            {
                c.State = CreatureState.Attacking;
                c.StateTime = 0f;
                c.AttackTimer = 0.6f;
                return;
            }
            HurtTree(c);
            // it sinks into the tree as a puff of gloom (no purifying: no stardust)
            c.State = CreatureState.Gone;
            Emit(GardenEventKind.Dissolved, c);
        }

        void HurtTree(Creature c)
        {
            TreeHealth = Mathf.Max(0, TreeHealth - GardenLevel.DamageOf(c.Kind));
            Hurt = true;
            Emit(GardenEventKind.TreeHit, c, count: GardenLevel.DamageOf(c.Kind));
        }

        /// <summary>A creature touching the shield: thrown back and dizzy a moment (the shield protects, it doesn't
        /// purify: when its bounce stung too, a shield up half the time made the tree safe).</summary>
        void Bounce(Creature c)
        {
            Emit(GardenEventKind.Blocked, c);
            if (c.State == CreatureState.Attacking)
            {
                c.State = CreatureState.Coming;
                c.StateTime = 0f;
            }
            c.Flash = 0.6f;
            c.Stunned = Mathf.Max(c.Stunned, 0.6f);
            if (c.Flying)
            {
                float length = Mathf.Max(1f, Vector3.Distance(c.From, c.To));
                c.Along = Mathf.Max(0f, c.Along - ShieldKnockAir / length);
            }
            else
            {
                var p = c.Position;
                p.x = Mathf.Clamp(p.x + c.Side * ShieldKnock, -SpawnX, SpawnX);
                c.Position = p;
            }
        }

        /// <summary><paramref name="damage"/> hits on <paramref name="c"/>, knocked back <paramref name="knock"/> metres on the
        /// ground (<paramref name="knockAir"/> along a flight).</summary>
        void Hit(Creature c, int damage, float knock, int player, float knockAir = -1f, float stun = 0.35f)
        {
            if (!c.Alive) return;
            c.Hp -= damage;
            c.Flash = 1f;
            c.Stunned = Mathf.Max(c.Stunned, stun);
            if (c.Flying)
            {
                float length = Mathf.Max(1f, Vector3.Distance(c.From, c.To));
                c.Along = Mathf.Max(0f, c.Along - (knockAir >= 0f ? knockAir : knock) / length);
            }
            else
            {
                var p = c.Position;
                p.x = Mathf.Clamp(p.x + c.Side * knock, -SpawnX, SpawnX);
                c.Position = p;
            }
            if (c.State == CreatureState.Attacking && knock > 0f)
            {
                c.State = CreatureState.Coming;
                c.StateTime = 0f;
            }
            if (c.Hp > 0)
            {
                Emit(GardenEventKind.Hit, c, player, damage);
                return;
            }
            c.State = CreatureState.Purified;
            c.StateTime = 0f;
            Purified++;
            Stardust += GardenLevel.ValueOf(c.Kind);
            Magic = Mathf.Min(1f, Magic + (c.Kind is CreatureKind.Brute or CreatureKind.Boss ? GardenLevel.MagicPerBig : GardenLevel.MagicPerCreature));
            Emit(GardenEventKind.Purified, c, player);
            if (c.Kind == CreatureKind.Puff)
            {
                // a puff splits into two bugs where it was
                for (int s = 0; s < 2; s++)
                {
                    var b = Spawn(CreatureKind.Bug, c.Side, c.Position + new Vector3(c.Side * (0.25f + s * 0.55f), 0f, s == 0 ? -0.45f : 0.45f));
                    b.Stunned = 0.5f;
                }
                Emit(GardenEventKind.Split, c, player);
            }
        }

        // ---------------------------------------------------------------------------------------------- spells

        /// <summary>Would <paramref name="o"/>'s spell touch <paramref name="c"/> (alive or not)? The lightning and the
        /// rainbow strike their whole side, the creatures still coming in too (one spell for a run of them: a child
        /// follows a shape every 4-6 s); the big magic everything in the picture (<see cref="Reach"/> past its edge).
        /// <paramref name="seen"/>: only what is in the picture or coming into it (<see cref="ComingIn"/>; a shape is
        /// chosen for what can be seen).</summary>
        public static bool Touches(SpellOffer o, Creature c, bool seen = false)
        {
            float x = c.Position.x;
            float side = seen ? LaneEdge + ComingIn : float.MaxValue;
            switch (o.Spell)
            {
                case Spell.Lightning: return !c.Flying && x * o.Side > 0f && Mathf.Abs(x) <= side;
                case Spell.Rainbow: return c.Flying && x * o.Side > 0f && Mathf.Abs(x) <= side;
                case Spell.Seeds: return (c.Flying ? Vector3.Distance(c.Position, Canopy) : Mathf.Abs(x)) <= SeedRadius;
                case Spell.Burst: return Mathf.Abs(x) <= LaneEdge + Reach;
                default: return false;
            }
        }

        /// <summary>Metres past the picture's edge the big magic still reaches (a creature half in the picture).</summary>
        public const float Reach = 0.6f;

        /// <summary>Player <paramref name="player"/> casts <paramref name="spell"/> (its shape drawn; the lightning and
        /// the rainbow on <paramref name="side"/>): the creatures it touched, or -1 when it can't be cast now (not bought,
        /// the crystal not full, coming back, the wand still charging, no side).</summary>
        public int Cast(Spell spell, int player = 0, int side = 0)
        {
            int k = Mathf.Clamp(player, 0, 1);
            if (Current == Stage.Over || !Ready(spell) || _recharge[k] > 0f || SpellOffer.Sided(spell) && side == 0) return -1;
            var offer = new SpellOffer(spell, side);
            int power = GardenLevel.SpellPower(Level);
            int count = 0;
            switch (spell)
            {
                case Spell.Lightning:
                case Spell.Rainbow:
                    // by index: a puff purified splits into new bugs (they come down later, not under this spell)
                    for (int i = 0, n = Creatures.Count; i < n; i++)
                    {
                        var c = Creatures[i];
                        if (!c.Alive || !Touches(offer, c)) continue;
                        if (spell == Spell.Lightning) Hit(c, power, LightningKnock, player, stun: 0.5f);
                        else Hit(c, power, 0f, player, RainbowKnock, 0.6f);
                        count++;
                    }
                    Events.Add(new GardenEvent
                    {
                        Kind = spell == Spell.Lightning ? GardenEventKind.Lightning : GardenEventKind.Rainbow, Player = player,
                        Count = count, Creature = -1, Side = offer.Side, Spell = spell,
                    });
                    break;
                case Spell.Shield:
                    ShieldTime = GardenLevel.ShieldSeconds(Difficulty);
                    Events.Add(new GardenEvent { Kind = GardenEventKind.ShieldUp, Player = player, Creature = -1, At = Tree });
                    break;
                case Spell.Seeds:
                    for (int i = 0, n = Creatures.Count; i < n; i++)
                    {
                        var c = Creatures[i];
                        if (!c.Alive || !Touches(offer, c)) continue;
                        Hit(c, power, SeedKnock, player);
                        count++;
                    }
                    Events.Add(new GardenEvent { Kind = GardenEventKind.Seeds, Player = player, Count = count, Creature = -1, At = Tree });
                    break;
                case Spell.Burst:
                    for (int i = 0, n = Creatures.Count; i < n; i++)
                    {
                        var c = Creatures[i];
                        if (!c.Alive || !Touches(offer, c)) continue;
                        Hit(c, 999, 0f, player);
                        count++;
                    }
                    Magic = 0f;
                    Events.Add(new GardenEvent { Kind = GardenEventKind.Burst, Player = player, Count = count, Creature = -1 });
                    break;
                case Spell.Freeze:
                    FrozenTime = FreezeSeconds;
                    foreach (var c in Creatures)
                        if (c.Alive) count++;
                    Events.Add(new GardenEvent { Kind = GardenEventKind.Freeze, Player = player, Count = count, Creature = -1 });
                    break;
                case Spell.Heal:
                    TreeHealth = Mathf.Min(TreeMax, TreeHealth + HealAmount);
                    Events.Add(new GardenEvent { Kind = GardenEventKind.Heal, Player = player, Creature = -1, At = Canopy });
                    break;
            }
            if (spell != Spell.Burst) _cooldown[(int)spell] = GardenLevel.CooldownOf(spell, Level);
            _recharge[k] = RechargeTime;
            return count;
        }

        // ---------------------------------------------------------------------------------------------- the wand's charge

        /// <summary>Seconds player <paramref name="player"/>'s wand still needs to charge (0: a spell can be drawn).</summary>
        public float RechargeOf(int player) => _recharge[Mathf.Clamp(player, 0, 1)];
        /// <summary>A wand's whole charge now (seconds).</summary>
        public float RechargeTime => GardenLevel.Recharge(Difficulty, Level);
        public bool Charged(int player) => _recharge[Mathf.Clamp(player, 0, 1)] <= 0f;
        readonly float[] _recharge = new float[2];

        /// <summary>The side a lightning or a rainbow drawn on <paramref name="drawn"/> (-1 the left half of the screen, 1
        /// the right) strikes: that side, unless nothing it could touch is there and something is on the other (a child
        /// drew it where the wand happened to be). Other spells: 0.</summary>
        public int SideFor(Spell spell, int drawn)
        {
            if (!SpellOffer.Sided(spell)) return 0;
            int side = drawn < 0 ? -1 : 1;
            if (Score(new SpellOffer(spell, side)) == float.MaxValue && Score(new SpellOffer(spell, -side)) < float.MaxValue) side = -side;
            return side;
        }

        // ---------------------------------------------------------------------------------------------- what would help

        /// <summary>
        /// What would help player <paramref name="player"/>'s fairy most now (null: nothing; the HUD's spell book glows it,
        /// the simulated players draw it): the big magic when the crystal is full, the healing rain for a hurt tree, the
        /// shield when creatures are about to reach it from both sides or a big one is, the freeze for a full garden; else
        /// the lightning or the rainbow on the side whose creatures will reach the tree soonest (more of them count as
        /// sooner: <see cref="Score"/>), or the seeds when a few are near the tree on both sides. With two fairies, each
        /// one's own side first.
        /// </summary>
        public SpellOffer? Suggest(int player = 0) => Current == Stage.Wave ? Pick(Mathf.Clamp(player, 0, 1)) : null;

        /// <summary>Metres past the picture's edge a creature counts as coming in when a spell is chosen.</summary>
        public const float ComingIn = 1f;

        static readonly Spell[] Whole = { Spell.Burst, Spell.Heal, Spell.Shield, Spell.Freeze };
        static readonly SpellOffer[] Attacks =
        {
            new SpellOffer(Spell.Lightning, -1), new SpellOffer(Spell.Lightning, 1), new SpellOffer(Spell.Rainbow, -1),
            new SpellOffer(Spell.Rainbow, 1), new SpellOffer(Spell.Seeds),
        };

        /// <summary>A group that will reach the tree this soon (<see cref="Score"/>) comes before the whole garden's
        /// spells (2026-11-16: the big magic's star taking the place of a lightning, a simulated child spent 13 s
        /// following it while the thorns it would have stopped reached the tree); the big magic anyway when this many
        /// are in the picture.</summary>
        public const float Urgent = 4f;
        public const int BurstAnyway = 8;

        /// <summary>How pressing the most pressing group may be (<see cref="Score"/>) for a whole-garden spell to come
        /// first: the freeze whenever it's wanted (five or more in the picture: then something always presses, and it is
        /// the spell for that; bought with stardust, it must come up), the shield when groups are a few seconds out (it
        /// buys time), the big magic and the healing rain in a calm moment.</summary>
        static float Tolerance(Spell sp) => sp switch
        {
            Spell.Freeze => float.MinValue,
            Spell.Shield => 2f,
            _ => Urgent,
        };

        SpellOffer? Pick(int k)
        {
            var attack = BestAttack(k);
            float pressing = attack.HasValue ? Score(attack.Value, k) : float.MaxValue;
            foreach (var sp in Whole)
            {
                var o = new SpellOffer(sp);
                if (!Ready(sp) || !Useful(o)) continue;
                if (pressing < Tolerance(sp) && !(sp == Spell.Burst && InPicture() >= BurstAnyway)) continue;
                return o;
            }
            return attack;
        }

        /// <summary>The lightning, rainbow or seeds whose creatures press most (<see cref="Score"/>), for fairy
        /// <paramref name="k"/>.</summary>
        SpellOffer? BestAttack(int k)
        {
            SpellOffer? best = null;
            float bestScore = float.MaxValue;
            foreach (var o in Attacks)
            {
                if (!Ready(o.Spell) || o.Spell == Spell.Seeds && !Useful(o)) continue;
                float s = Score(o, k);
                if (s < bestScore)
                {
                    bestScore = s;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>How pressing <paramref name="o"/>'s creatures are for fairy <paramref name="k"/> (lower: sooner):
        /// seconds before the first of them reaches the tree, less 0.7 s for each heart they would take (up to six), the
        /// seeds 0.8 s sooner (both sides at once); with two fairies the other's side 3 s later. No creatures: never.</summary>
        public float Score(SpellOffer o, int k = 0)
        {
            float soonest = float.MaxValue;
            int weight = 0;
            foreach (var c in Creatures)
            {
                if (!c.Alive || !Touches(o, c, true)) continue;
                weight += GardenLevel.DamageOf(c.Kind);
                float t = c.State == CreatureState.Attacking ? 0f : TimeToTree(c);
                if (t < soonest) soonest = t;
            }
            if (weight == 0) return float.MaxValue;
            float score = soonest - 0.7f * Mathf.Min(weight, 6);
            if (o.Spell == Spell.Seeds) score -= 0.8f;
            if (Players > 1 && o.Side != 0 && o.Side != (k == 0 ? -1 : 1)) score += 3f;
            return score;
        }

        /// <summary>Would <paramref name="o"/> help now?</summary>
        public bool Useful(SpellOffer o) => o.Spell switch
        {
            Spell.Burst => InPicture() >= 3 || BossHere(),
            Spell.Heal => TreeHealth <= TreeMax - 3,
            Spell.Shield => ShieldWanted(),
            Spell.Freeze => InPicture() >= 5,
            Spell.Seeds => Near(SeedRadius) >= 2,
            _ => Score(o) < float.MaxValue,
        };

        /// <summary>The shield is wanted when a brute or the boss is in the picture (they take more than one spell), or
        /// creatures are coming from both sides (one spell can't stop them all); offered while nothing presses
        /// (<see cref="Urgent"/>), it keeps the tree safe a few seconds while they are dealt with.</summary>
        bool ShieldWanted()
        {
            int seen = 0;
            bool left = false, right = false;
            foreach (var c in Creatures)
            {
                if (!c.Alive || Mathf.Abs(c.Position.x) > LaneEdge) continue;
                if (c.Kind is CreatureKind.Brute or CreatureKind.Boss) return true;
                seen++;
                if (c.Position.x < 0f) left = true;
                else right = true;
            }
            return seen >= 2 && left && right;
        }

        bool BossHere()
        {
            foreach (var c in Creatures)
                if (c.IsBoss && c.Alive && Mathf.Abs(c.Position.x) <= LaneEdge) return true;
            return false;
        }

        /// <summary>Living creatures in the picture (between the screen's edges).</summary>
        public int InPicture()
        {
            int n = 0;
            foreach (var c in Creatures)
                if (c.Alive && Mathf.Abs(c.Position.x) <= LaneEdge) n++;
            return n;
        }

        /// <summary>Living creatures that would reach the tree within <paramref name="seconds"/> (the boss pounding it
        /// counts).</summary>
        public int Threat(float seconds)
        {
            int n = 0;
            foreach (var c in Creatures)
                if (c.Alive && (c.State == CreatureState.Attacking || TimeToTree(c) <= seconds)) n++;
            return n;
        }

        /// <summary>Seconds <paramref name="c"/> needs to reach the tree at its speed.</summary>
        public static float TimeToTree(Creature c)
        {
            float speed = Mathf.Max(0.05f, c.Speed);
            if (c.Flying) return (1f - c.Along) * Mathf.Max(1f, Vector3.Distance(c.From, c.To)) / speed;
            return Mathf.Max(0f, Mathf.Abs(c.Position.x) - TreeRadius - c.Size * 0.45f) / speed;
        }

        /// <summary>The creature in the picture that will reach the tree soonest (the game playing itself, hints).</summary>
        public Creature MostPressing()
        {
            Creature best = null;
            float soonest = float.MaxValue;
            foreach (var c in Creatures)
            {
                if (!c.Alive || Mathf.Abs(c.Position.x) > LaneEdge) continue;
                float t = c.State == CreatureState.Attacking ? 0f : TimeToTree(c);
                if (t < soonest)
                {
                    soonest = t;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>How far <paramref name="c"/> has still to go to the tree (metres).</summary>
        public static float ToTree(Creature c) => c.Flying
            ? Vector3.Distance(c.Position, Canopy)
            : Mathf.Max(0f, Mathf.Abs(c.Position.x) - TreeRadius);

        /// <summary>How many living creatures are within <paramref name="metres"/> of the tree.</summary>
        public int Near(float metres)
        {
            int n = 0;
            foreach (var c in Creatures)
            {
                if (!c.Alive) continue;
                float d = c.Flying ? Vector3.Distance(c.Position, Canopy) : Mathf.Abs(c.Position.x);
                if (d <= metres) n++;
            }
            return n;
        }

        /// <summary>Tests: a creature of <paramref name="kind"/> from <paramref name="side"/> at <paramref name="x"/> (the
        /// wave's own schedule goes on too).</summary>
        public Creature Add(CreatureKind kind, int side, float x)
        {
            var c = Spawn(kind, side);
            if (!c.Flying) c.Position = new Vector3(x, 0f, TreeZ);
            return c;
        }

        /// <summary>PerfLab (<c>wave=</c>) and tests: the run jumps to wave <paramref name="n"/>, with the transformations
        /// a player would have had by then (a busy late wave on the box, the same every time).</summary>
        public void SkipTo(int n)
        {
            if (n <= Wave || Current == Stage.Over) return;
            Creatures.Clear();
            Level = Mathf.Min(MaxLevel, 1 + (n - 1) / 5);
            WavesCleared = n - 1;
            StartWave(n);
        }

        /// <summary>Tests: the wave doesn't end while nothing is left.</summary>
        public bool KeepWave;

        /// <summary>Tests: the wave's schedule emptied (only the creatures added by hand come).</summary>
        public void ClearSchedule()
        {
            _schedule.Clear();
            _next = 0;
        }

        public Creature Find(int id)
        {
            foreach (var c in Creatures)
                if (c.Id == id) return c;
            return null;
        }
    }
}
