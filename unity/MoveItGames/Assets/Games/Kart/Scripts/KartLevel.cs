using UnityEngine;

namespace MoveIt.Games.Kart
{
    /// <summary>Who plays (Tenis!'s and Volejboll!'s choices).</summary>
    public enum KartMode
    {
        /// <summary>One player drives their kart against five computer karts.</summary>
        Solo,
        /// <summary>Two players drive ONE kart together (Kinect's River Rush raft): the kart steers as their two wheels
        /// together (the average of those holding one), either one's jump does the trick (both together: a double trick),
        /// either one's raised hands use the item. One picture: a parent and a small child can share a kart.</summary>
        Together,
        /// <summary>Two players, a kart each, the screen split down the middle (player 1's half on the left, where they
        /// stand), against each other and four computer karts.</summary>
        Versus,
    }

    /// <summary>How hard (chosen on the menu, remembered).</summary>
    public enum KartDifficulty { Easy, Medium, Hard }

    /// <summary>One race, or a cup: three races, one on each track, points for every place.</summary>
    public enum KartFormat { Race, Cup }

    /// <summary>
    /// The computer's karts, and the player's own kart, at one strength (<see cref="Strength"/>: 0 = the gentlest ... 8 =
    /// the strongest, every number capped), like Tenis!'s and Volejboll!'s levels. Everything is faster on a harder
    /// difficulty (the karts' base speed), the computer drives quicker and keeps closer (its rubber band), does more tricks
    /// and drift boosts, throws paint at the player (never on Easy) and makes fewer mistakes; the player's smart steering
    /// helps less: on Easy the kart follows the road by itself and the wheel only moves it across the road (Mario Kart 8
    /// Deluxe's smart steering, for small children), on Hard the player steers through the corners themselves. A
    /// difficulty is a range of strengths, one per race of a cup (<see cref="StrengthFor"/>).
    /// </summary>
    public sealed class KartLevel
    {
        public KartDifficulty Difficulty;
        /// <summary>The cup's race (0 .. 2); a single race plays the first race's strength.</summary>
        public int Race;
        public float Strength;
        /// <summary>The karts' cruising speed (m/s): the player's flat out, the computer's reference.</summary>
        public float BaseSpeed;
        /// <summary>Smart steering, 0..1: how much of the road's curve the player's kart follows by itself (1 = all of it:
        /// the wheel only moves it across the road), and how strongly it straightens along the road and keeps off the
        /// barrier when the wheel is level.</summary>
        public float Assist;
        /// <summary>The computer's speed against <see cref="BaseSpeed"/> (each kart a little either side of it).</summary>
        public float AiPace;
        /// <summary>The rubber band: a computer kart ahead of the player slows by up to this share, one behind speeds up
        /// by up to <see cref="AiCatchUp"/> (fully 80 m away): races stay close.</summary>
        public float AiEaseAhead, AiCatchUp;
        /// <summary>How often a computer kart does a trick at a jump, a drift boost through a corner it can drift.</summary>
        public float AiTricks, AiDrifts;
        /// <summary>How often a computer kart throws its paint at the kart ahead when that's a player (0 on Easy: the
        /// computer only ever paints other computer karts).</summary>
        public float AiHitsPlayers;
        /// <summary>How readily the computer uses its items (and aims for the boost pads and the gift boxes).</summary>
        public float AiItems;
        /// <summary>How far (m) a computer kart wanders off its line, and how often a lap it takes a corner too wide.</summary>
        public float AiWobble, AiMistakes;
        /// <summary>The player's kart: how much of a bump or a barrier's speed it keeps.</summary>
        public float BumpKeep;
        /// <summary>The share of the player's gift boxes that give the rocket (the most useful item for a child behind).</summary>
        public float RocketBias;
        /// <summary>The camera's moves count this much later than on the plain windows (jumps at the ramp): forgiving on
        /// the gentle levels.</summary>
        public float WindowScale;

        /// <summary>The computer's drivers (the user should review the names): five race each race.</summary>
        public static readonly string[] Names = { "Ana", "Beni", "Era", "Dini", "Lea", "Toni", "Mira", "Leka", "Nora", "Gent" };
        public const int Drivers = 10;

        /// <summary>The player's kart and the computer's (tuning: KartRaceTests' balance).</summary>
        public static float BaseSpeedEasy = 21f, BaseSpeedStep = 0.7f;
        /// <summary>Smart steering from Medium's first race to the strongest (Easy: all of it).</summary>
        public static float AssistMedium = 0.75f, AssistHardest = 0.3f;
        /// <summary>The computer's pace against the base speed (a little lower on the strong levels, whose karts take
        /// every boost: tricks, drifts, pads), and its rubber band.</summary>
        public static float AiPaceEasy = 0.97f, AiPaceStep = 0.0068f, AiEaseMost = 0.07f, AiEaseStep = 0.006f, AiCatchLeast = 0.02f,
                            AiCatchStep = 0.006f;

        /// <summary>The strength a difficulty plays at, per race of a cup (a single race: the first).</summary>
        public static float StrengthFor(KartDifficulty difficulty, int race)
        {
            int r = Mathf.Clamp(race, 0, KartCup.Races - 1);
            return difficulty switch
            {
                KartDifficulty.Easy => 0.5f * r,
                KartDifficulty.Medium => 2.4f + 1f * r,
                _ => 5.2f + 1.4f * r,
            };
        }

        public static KartLevel For(KartDifficulty difficulty, int race)
        {
            var level = Of(StrengthFor(difficulty, race));
            level.Difficulty = difficulty;
            level.Race = race;
            return level;
        }

        /// <summary>Two players against each other: Medium's computer, the plain windows.</summary>
        public static KartLevel Versus(KartDifficulty difficulty) => For(difficulty, 0);

        /// <summary>A level of strength <paramref name="k"/> (0 = the gentlest ... 8 = the strongest; capped beyond).</summary>
        public static KartLevel Of(float k)
        {
            k = Mathf.Max(0f, k);
            return new KartLevel
            {
                Strength = k,
                BaseSpeed = Mathf.Min(BaseSpeedEasy + BaseSpeedStep * 8f, BaseSpeedEasy + BaseSpeedStep * k),
                Assist = k < 1.5f ? 1f : Mathf.Lerp(AssistMedium, AssistHardest, Mathf.InverseLerp(1.5f, 8f, k)),
                AiPace = AiPaceEasy - AiPaceStep * Mathf.Min(k, 8f),
                AiEaseAhead = Mathf.Max(0.02f, AiEaseMost - AiEaseStep * k),
                AiCatchUp = Mathf.Min(0.12f, AiCatchLeast + AiCatchStep * k),
                AiTricks = Mathf.Min(0.92f, 0.35f + 0.07f * k),
                AiDrifts = Mathf.Min(0.9f, 0.2f + 0.09f * k),
                AiHitsPlayers = k < 1.5f ? 0f : Mathf.Min(1f, 0.35f + 0.1f * (k - 1.5f)),
                AiItems = Mathf.Min(0.95f, 0.3f + 0.08f * k),
                AiWobble = Mathf.Max(0.15f, 0.7f - 0.07f * k),
                AiMistakes = Mathf.Max(0.1f, 1.2f - 0.13f * k),
                BumpKeep = Mathf.Lerp(0.9f, 0.78f, k / 8f),
                RocketBias = Mathf.Lerp(0.5f, 0.15f, k / 8f),
                WindowScale = Mathf.Lerp(1.4f, 1f, Mathf.InverseLerp(0.5f, 5f, k)),
            };
        }
    }
}
