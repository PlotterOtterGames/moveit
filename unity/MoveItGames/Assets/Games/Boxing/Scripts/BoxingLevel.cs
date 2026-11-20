using UnityEngine;

namespace MoveIt.Games.Boxing
{
    /// <summary>
    /// Every number of one fight: how strong the opponent is (from its place in the climb and the difficulty, then its own
    /// way of fighting) and how forgiving the rules are with the player (the difficulty). How the shipped games do it:
    /// <list type="bullet">
    /// <item><b>Wii Sports Boxing</b> has no difficulty to choose: the computer's boxer is matched to the player's skill
    /// points, so each next opponent is quicker and hits harder, up to the champion (Matt, whose silver gloves are the
    /// prize);</item>
    /// <item><b>ARMS</b>' Grand Prix lets the player choose a level before the run; the computer gets stronger the deeper
    /// the run goes;</item>
    /// <item><b>Punch-Out!!</b> climbs circuits of ever stronger opponents, each with its own tells and weak spots.</item>
    /// </list>
    /// So: the difficulty is chosen before the climb (three, like the other Tundu games); within it, each fight is
    /// stronger than the last (<see cref="StrengthOf"/>: easy 0 .. 0.45, medium 0.2 .. 0.75, hard 0.45 .. 1); the
    /// opponent's own <see cref="OpponentProfile"/> bends the numbers its way. Strength 0 = the gentlest (long tells, soft
    /// punches, an open guard), 1 = the strongest (short tells, combos, hard punches, defending and hitting back).
    /// </summary>
    public sealed class BoxingLevel
    {
        public BoxingDifficulty Difficulty;
        public float Strength;
        public OpponentProfile Opponent;

        // ------------------------------------------------------------------ the opponent

        /// <summary>Wind-up (the tell) of an ordinary punch, a jab and a power punch (s): its body showing what comes.</summary>
        public float Tell, JabTell, SpecialTell;
        /// <summary>From the end of the wind-up to the impact (s).</summary>
        public float StrikeTime;
        /// <summary>Its punches' damage (times the punch's own, to the hero's 100).</summary>
        public float DamageScale;
        /// <summary>Its health, in the hero's plain punches (a straight punch landed takes 1).</summary>
        public float MaxHealth;
        /// <summary>Open after its punch (s); after one that met nothing (staggered: punches count more); dizzy.</summary>
        public float Recover, Staggered, DizzyTime;
        /// <summary>Chance a punch landing in its wind-up stops the attack.</summary>
        public float InterruptChance;
        /// <summary>Share of the time its guard is open while it waits.</summary>
        public float OpenShare;
        /// <summary>How long its guard stays the same while it waits (s).</summary>
        public float GuardHold;
        /// <summary>Knockdowns it takes to stay down (3 in one round is always a TKO).</summary>
        public int DownsToKO;
        /// <summary>Share of its attacks that are power punches (champions).</summary>
        public float SpecialShare;
        /// <summary>A fireball's flight (s).</summary>
        public float FireballFlight;

        // ------------------------------------------------------------------ its mind (Bout.Brain.cs)

        /// <summary>How long ago the hero it sees is (s): what it reacts to.</summary>
        public float Reaction;
        /// <summary>How often it decides what to do (s).</summary>
        public float ThinkInterval;
        /// <summary>The time between its attacks, on average (s): its aggression.</summary>
        public float AttackGap;
        /// <summary>How well it picks the punch for the opening it sees (0 = nearly at random, 1 = the best nearly always).</summary>
        public float Smart;
        /// <summary>Chance it blocks or slips a hero punch its gloves don't cover already.</summary>
        public float DefendSkill;
        /// <summary>Chance it punches straight back after a defence that worked.</summary>
        public float CounterRate;
        /// <summary>Chance a punch is followed by another (a combo).</summary>
        public float ComboChance;
        /// <summary>Its aim follows the hero through this share of its wind-up, then it's fixed: moving after that escapes.</summary>
        public float Track;
        /// <summary>How far off its aim lands, typically (m).</summary>
        public float AimError;

        // ------------------------------------------------------------------ the rules (the difficulty)

        /// <summary>A defence counts if shown at any moment this long (s) before the decision (just before the impact).</summary>
        public float DefenceWindow;
        /// <summary>How close to its line a punch still hits (m): a smaller one is dodged by a smaller move.</summary>
        public float HitRadius;
        /// <summary>Share of a punch's damage that gets through the guard.</summary>
        public float BlockChip;
        /// <summary>Hero punches landed in a row that make it dizzy.</summary>
        public int ComboToDizzy;
        /// <summary>The referee's count while the hero is down (s a number).</summary>
        public float HeroCountInterval;
        /// <summary>Punches (any) the hero needs to get up after the first knockdown (+2 after each).</summary>
        public int GetUpPunches;
        /// <summary>The hero's health after getting up (share).</summary>
        public float HeroGetUpHealth;
        /// <summary>Power (0 .. 1 = the super) a dodge, a block, a counter and a plain landed punch bring.</summary>
        public float PowerDodge, PowerBlock, PowerCounter, PowerHit;
        /// <summary>The super punch takes this share of its full health.</summary>
        public float SuperShare;

        public float RoundSeconds = 60f;
        public int Rounds = 3;
        public const float CountInterval = 0.9f, RestSeconds = 5f, StartSeconds = 1.8f;

        static float Lerp(float a, float b, float t) => a + (b - a) * Mathf.Clamp01(t);

        /// <summary>Fight <paramref name="number"/>'s strength at <paramref name="difficulty"/> (0 .. 1).</summary>
        public static float StrengthOf(BoxingDifficulty difficulty, int number)
        {
            float t = Campaign.Fights <= 1 ? 0f : number / (float)(Campaign.Fights - 1);
            return difficulty switch
            {
                BoxingDifficulty.Easy => Lerp(0f, 0.45f, t),
                BoxingDifficulty.Medium => Lerp(0.2f, 0.75f, t),
                _ => Lerp(0.45f, 1f, t),
            };
        }

        public static BoxingLevel For(BoxingDifficulty difficulty, int number) =>
            For(difficulty, Campaign.Opponent(number), StrengthOf(difficulty, number));

        public static BoxingLevel For(BoxingDifficulty difficulty, OpponentProfile o, float s)
        {
            s = Mathf.Clamp01(s);
            var l = new BoxingLevel { Difficulty = difficulty, Strength = s, Opponent = o };
            l.Tell = Lerp(1.0f, 0.55f, s) * o.Tell;
            l.JabTell = Lerp(0.72f, 0.38f, s) * o.Tell;
            l.SpecialTell = Lerp(1.45f, 0.95f, s) * Mathf.Sqrt(o.Tell);
            l.StrikeTime = Lerp(0.28f, 0.17f, s);
            l.DamageScale = Lerp(0.65f, 1.5f, s) * o.Power;
            l.MaxHealth = Lerp(44f, 80f, s) * o.Toughness * (o.Champion ? 1.15f : 1f);
            l.Recover = Lerp(0.9f, 0.45f, s);
            l.Staggered = Lerp(1.25f, 0.7f, s);
            l.DizzyTime = Lerp(1.7f, 1.05f, s);
            l.InterruptChance = Lerp(0.5f, 0.05f, s);
            l.OpenShare = Mathf.Clamp(Lerp(0.5f, 0.14f, s) * o.OpenGuard, 0.05f, 0.9f);
            l.GuardHold = Lerp(3.4f, 2.0f, s);
            l.DownsToKO = (s < 0.35f ? 2 : 3) + (o.Champion ? 1 : 0);
            l.SpecialShare = o.Specials.Length == 0 ? 0f : Lerp(0.2f, 0.32f, s);
            l.FireballFlight = Lerp(1.35f, 0.95f, s);
            // its mind
            l.Reaction = Lerp(0.42f, 0.15f, s);
            l.ThinkInterval = Lerp(0.36f, 0.15f, s);
            l.AttackGap = Lerp(2.5f, 1.05f, s) / o.Aggression;
            l.Smart = Lerp(0.2f, 0.92f, s);
            l.DefendSkill = Mathf.Clamp(Lerp(0.03f, 0.34f, s) * o.Defense, 0f, 0.6f);
            l.CounterRate = Mathf.Clamp(Lerp(0.1f, 0.55f, s) * o.Counter, 0f, 0.85f);
            l.ComboChance = Mathf.Clamp(Lerp(0.05f, 0.4f, s) * o.Combo, 0f, 0.75f);
            l.Track = Lerp(0.3f, 0.72f, s);
            l.AimError = Lerp(0.06f, 0.015f, s);

            switch (difficulty)
            {
                case BoxingDifficulty.Easy:
                    l.DefenceWindow = 0.3f;
                    l.HitRadius = 0.085f;
                    l.BlockChip = 0f;
                    l.ComboToDizzy = 4;
                    l.HeroCountInterval = 1.15f;
                    l.GetUpPunches = 4;
                    l.HeroGetUpHealth = 0.7f;
                    l.PowerDodge = 0.16f;
                    l.PowerBlock = 0.06f;
                    l.PowerCounter = 0.06f;
                    l.PowerHit = 0.012f;
                    l.SuperShare = 0.28f;
                    break;
                case BoxingDifficulty.Medium:
                    l.DefenceWindow = 0.2f;
                    l.HitRadius = 0.105f;
                    l.BlockChip = 0.1f;
                    l.ComboToDizzy = 5;
                    l.HeroCountInterval = 1f;
                    l.GetUpPunches = 6;
                    l.HeroGetUpHealth = 0.6f;
                    l.PowerDodge = 0.12f;
                    l.PowerBlock = 0.045f;
                    l.PowerCounter = 0.05f;
                    l.PowerHit = 0.008f;
                    l.SuperShare = 0.24f;
                    break;
                default:
                    l.DefenceWindow = 0.12f;
                    l.HitRadius = 0.125f;
                    l.BlockChip = 0.15f;
                    l.ComboToDizzy = 6;
                    l.HeroCountInterval = 0.9f;
                    l.GetUpPunches = 8;
                    l.HeroGetUpHealth = 0.5f;
                    l.PowerDodge = 0.1f;
                    l.PowerBlock = 0.035f;
                    l.PowerCounter = 0.04f;
                    l.PowerHit = 0.006f;
                    l.SuperShare = 0.2f;
                    break;
            }
            return l;
        }

        /// <summary>The wind-up of <paramref name="attack"/>.</summary>
        public float TellFor(Attack attack) => attack switch
        {
            Attack.Jab => JabTell,
            Attack.Fireball => SpecialTell * 0.8f,
            _ when Campaign.IsSpecial(attack) => SpecialTell,
            _ => Tell,
        };

        /// <summary>The damage <paramref name="attack"/> does to the hero (of 100) when it lands.</summary>
        public float DamageOf(Attack attack) => DamageScale * attack switch
        {
            Attack.Jab => 6f,
            Attack.Cross => 9f,
            Attack.Hook => 10f,
            Attack.Uppercut => 12f,
            Attack.Body => 8f,
            Attack.Fireball => 14f,
            _ => 19f,
        };

        /// <summary>How much of the opponent's health the hero's punch takes, in plain punches (before counters): its kind
        /// (a hook a quarter more, an uppercut half again) and how hard it was thrown.</summary>
        public static float PunchDamage(Blow blow, float speed)
        {
            float kind = blow switch { Blow.Hook => 1.25f, Blow.Uppercut => 1.5f, _ => 1f };
            // a punch seen at 2.2 torso lengths a second is a plain one; up to 6 a hard one (+25 %)
            float hard = Mathf.Clamp01((speed - 2.2f) / 3.8f);
            return kind * (0.92f + 0.25f * hard);
        }
    }
}
