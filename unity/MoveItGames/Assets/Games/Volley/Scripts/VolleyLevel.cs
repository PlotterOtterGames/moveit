using UnityEngine;

namespace MoveIt.Games.Volley
{
    /// <summary>Who plays (Tenis!'s choices, 2026-11-07, user: "align the volley game to tennis: the difficulty modes,
    /// effects, vs and together modes with 2 people").</summary>
    public enum VolleyMode
    {
        /// <summary>One player plays both of their team's toys (the one the ball comes to: receive, set, hit) against a
        /// computer pair.</summary>
        Solo,
        /// <summary>Two players on one team, a toy each (player 1's on the left half, player 2's on the right), against a
        /// computer pair: one receives, the other sets it up for them, they hit it over.</summary>
        Together,
        /// <summary>Two players against each other, each playing their whole team (the screen split, each half from
        /// behind its team).</summary>
        Versus,
    }

    /// <summary>How hard (chosen on the menu, remembered): how well the computer's pair plays and how quick the player's
    /// own toys are (they run to the ball by themselves).</summary>
    public enum VolleyDifficulty { Easy, Medium, Hard }

    /// <summary>One match, or a tournament: eight pairs, three rounds (<see cref="VolleyTournament"/>).</summary>
    public enum VolleyFormat { Match, Tournament }

    /// <summary>
    /// The computer's pair, and the player's toys, at one strength (<see cref="Strength"/>: 0 = the gentlest pair ... 8 =
    /// the strongest, every number capped), like Tenis!'s <c>TennisLevel</c>: a stronger pair digs more of the player's
    /// attacks (a hard one less), sends fewer free balls and more smashes and tips (the player must read which: Easy shows
    /// a "!" over a smasher and never tips, Medium's tips look like tips from the set, Hard's like smashes until the last
    /// moment and come more against a player who blocks a lot), attacks harder and into the gaps, makes fewer mistakes and
    /// serves harder, while the player's toys run a little slower and react a little later, so on Hard not every attack
    /// can be reached. A difficulty is a range of strengths, one per tournament round (<see cref="StrengthFor"/>).
    /// </summary>
    public sealed class VolleyLevel
    {
        public VolleyDifficulty Difficulty;
        /// <summary>The tournament round (0 = quarter-final ... 2 = final); a single match plays round 1's strength.</summary>
        public int Round;
        public float Strength;
        /// <summary>How often they dig a normal attack of the player's (before its power is taken off).</summary>
        public float Dig;
        /// <summary>How much a hard attack (fast, a perfect spike) takes off <see cref="Dig"/>.</summary>
        public float Pressure;
        /// <summary>Their spikes' speed across the court (m/s, at pace 1).</summary>
        public float AttackSpeed;
        /// <summary>How often they aim into the gap between or behind the player's toys instead of at one of them.</summary>
        public float AimAway;
        /// <summary>
        /// Their attacks after the sure part (2026-11-08, the defence as a choice at the moment of the hit): a free ball
        /// this often (no jump: received low), else a jump at the net, a tip this share of the time (over a block: received
        /// low), else a smash (blocked with both hands up). No tips on Easy; more against the strong pairs.
        /// </summary>
        public float FreeBall, TipShare;
        /// <summary>Easy: a "!" over a hitter that will smash, from its set (a child learns: "!" = both hands up).</summary>
        public bool ShowTell;
        /// <summary>Hard: this share of the tips is disguised (the hitter looks like it will smash, arm drawn back, a big
        /// jump) until <see cref="TellBefore"/> s before the hit, when the hand comes forward. Other tips look like tips from
        /// the set (the hand forward, a little jump).</summary>
        public float DisguiseShare, TellBefore;
        /// <summary>A strong pair reads the player: its tips get this much more likely (at most) against a player who always
        /// blocks, less likely against one who never does (<see cref="VolleyMatch.BlockRate"/>).</summary>
        public float Adapt;
        /// <summary>Their set for a jumping attack tops out this far (m) above where the hitter meets it on the way down: the
        /// time from the set to the hit, to read the attack (~1.4 s against the gentlest pairs at the rally's start, ~1.1 s
        /// against the strongest).</summary>
        public float SetAbove;
        /// <summary>How often their attack goes into the net or out.</summary>
        public float Error;
        /// <summary>Their serves' speed across the court (m/s, at pace 1).</summary>
        public float ServeSpeed;
        /// <summary>The player's toys (they run to the ball by themselves): how fast (m/s) and how long after the other
        /// side's touch they set off (s). Quick against the gentle pairs (every ball is reached), slower against the strong
        /// ones, whose hard attacks into a gap can get away.</summary>
        public float HumanRunSpeed, HumanReaction;
        /// <summary>The rally's pace (Tenis!'s, user 2026-11-06: "on each hit it gets faster"): every ball flies at a time
        /// scale, the serve at <see cref="ServePace"/>, each time the ball crosses the net <see cref="PaceStep"/> quicker,
        /// up to <see cref="TopPace"/>. The arcs stay the same, only quicker.</summary>
        public float ServePace, PaceStep, TopPace;
        /// <summary>The moves' windows are this much longer: forgiving against the gentle pairs.</summary>
        public float WindowScale = 1f;
        /// <summary>The pair's names (the user should review them), their places in <see cref="Names"/>, which pair of
        /// the seven they are, their shirt colour (the game's palette).</summary>
        public string Name, Partner;
        public int NameIndex, PartnerIndex, Opponent, Colour;
        public string TeamName => $"{Name} & {Partner}";

        /// <summary>The computer's players, in pairs (Drita &amp; Ilir, Besa &amp; Arben, ...: seven pairs, the seven other teams
        /// of a tournament).</summary>
        public static readonly string[] Names =
            { "Drita", "Ilir", "Besa", "Arben", "Jona", "Luan", "Rina", "Altin", "Dea", "Erion", "Kejsi", "Bledi", "Ana", "Driton" };
        public const int Pairs = 7;

        /// <summary>The player's toys' running against the pair's strength (tuning: VolleyMatchTests' balance and reach).</summary>
        public static float HumanRunSpeedMin = 7.8f, HumanRunSpeedStep = 0.38f, HumanReactionMax = 0.17f, HumanReactionStep = 0.014f;
        /// <summary>The attacks' mix against the strength (tuning: VolleyMatchTests' balance and defence tests): free balls
        /// from <see cref="FreeBallMost"/> (the gentlest) to <see cref="FreeBallLeast"/>; tips from strength
        /// <see cref="TipsFrom"/>, <see cref="TipStep"/> a strength up to <see cref="TipMost"/>; from strength
        /// <see cref="DisguiseFrom"/> (Hard) <see cref="DisguiseLeast"/> .. <see cref="DisguiseMost"/> of them disguised until
        /// <see cref="TellBeforeDisguised"/> .. <see cref="TellBeforeLatest"/> s before the hit; from strength
        /// <see cref="AdaptFrom"/> (Medium) the pair reads the player (<see cref="AdaptLeast"/> .. <see cref="AdaptMost"/>); its
        /// attacking sets from <see cref="SetAboveMost"/> to <see cref="SetAboveLeast"/> m above the hit.</summary>
        public static float FreeBallMost = 0.42f, FreeBallLeast = 0.12f, TipsFrom = 1.2f, TipStep = 0.11f, TipMost = 0.4f, DisguiseFrom = 5f,
                            DisguiseLeast = 0.4f, DisguiseMost = 0.5f, TellBeforeDisguised = 0.65f, TellBeforeLatest = 0.5f, AdaptFrom = 2.4f,
                            AdaptLeast = 0.3f, AdaptMost = 0.35f, SetAboveMost = 0.75f, SetAboveLeast = 0.3f;

        public static string OpponentName(int opponent)
        {
            int o = Wrap(opponent, Pairs);
            return $"{Names[o * 2]} & {Names[o * 2 + 1]}";
        }

        static int Wrap(int i, int n) => ((i % n) + n) % n;

        /// <summary>The strength a difficulty plays at, per tournament round (a single match: round 1), as Tenis!'s.</summary>
        public static float StrengthFor(VolleyDifficulty difficulty, int round)
        {
            int r = Mathf.Clamp(round, 0, VolleyTournament.Rounds - 1);
            return difficulty switch
            {
                VolleyDifficulty.Easy => 0.5f * r,
                VolleyDifficulty.Medium => 2.4f + 1f * r,
                _ => 5.2f + 1.4f * r,
            };
        }

        /// <summary>Pair <paramref name="opponent"/> at a difficulty, in a tournament's round (a single match: round 1).</summary>
        public static VolleyLevel For(VolleyDifficulty difficulty, int round, int opponent)
        {
            var level = Of(StrengthFor(difficulty, round), opponent);
            level.Difficulty = difficulty;
            level.Round = round;
            return level;
        }

        /// <summary>Two players against each other: no computer, but their toys run and reach like a strong pair's toys
        /// would let them (else two good players' rallies went on and on: 43 times over the net), and the windows are the
        /// plain ones.</summary>
        public static VolleyLevel Versus() => Of(5f, 0);

        /// <summary>A pair of strength <paramref name="k"/> (0 = the gentlest ... 8 = the strongest; capped beyond).</summary>
        public static VolleyLevel Of(float k, int opponent)
        {
            k = Mathf.Max(0f, k);
            int o = Wrap(opponent, Pairs);
            return new VolleyLevel
            {
                Strength = k,
                Dig = Mathf.Min(0.85f, 0.6f + 0.032f * k),
                Pressure = Mathf.Max(0.42f, 0.9f - 0.06f * k),
                AttackSpeed = Mathf.Min(12.5f, 9f + 0.45f * k),
                AimAway = Mathf.Min(0.5f, 0.1f + 0.05f * k),
                FreeBall = Mathf.Lerp(FreeBallMost, FreeBallLeast, k / 8f),
                TipShare = Mathf.Clamp(TipStep * (k - TipsFrom), 0f, TipMost),
                ShowTell = k < 2f,
                DisguiseShare = k < DisguiseFrom ? 0f : Mathf.Lerp(DisguiseLeast, DisguiseMost, Mathf.InverseLerp(DisguiseFrom, 8f, k)),
                TellBefore = Mathf.Lerp(TellBeforeDisguised, TellBeforeLatest, Mathf.InverseLerp(DisguiseFrom, 8f, k)),
                Adapt = k < AdaptFrom ? 0f : Mathf.Lerp(AdaptLeast, AdaptMost, Mathf.InverseLerp(AdaptFrom, DisguiseFrom, k)),
                SetAbove = Mathf.Lerp(SetAboveMost, SetAboveLeast, k / 8f),
                Error = Mathf.Max(0.03f, 0.14f - 0.014f * k),
                ServeSpeed = Mathf.Min(12.5f, 8.5f + 0.5f * k),
                HumanRunSpeed = Mathf.Max(HumanRunSpeedMin, 10.5f - HumanRunSpeedStep * k),
                HumanReaction = Mathf.Min(HumanReactionMax, 0.06f + HumanReactionStep * k),
                ServePace = Mathf.Min(1f, 0.85f + 0.02f * k),
                PaceStep = 0.04f,
                TopPace = Mathf.Min(1.4f, 1.2f + 0.025f * k),
                WindowScale = Mathf.Lerp(1.4f, 1f, Mathf.InverseLerp(0.5f, 5f, k)),
                Name = Names[o * 2],
                Partner = Names[o * 2 + 1],
                NameIndex = o * 2,
                PartnerIndex = o * 2 + 1,
                Opponent = o,
                Colour = o % 4,
            };
        }
    }
}
