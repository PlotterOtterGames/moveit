using UnityEngine;

namespace MoveIt.Games.Tennis
{
    /// <summary>How hard Tenis! is (chosen on the waiting screen with the remote, remembered): how strong the computer's
    /// pair plays and how quickly the player's own toys get to the ball (they run there by themselves since 2026-11-07,
    /// like Wii's Miis: on Easy they reach every ball, on Hard a quick ball into a far corner can get away).</summary>
    public enum TennisDifficulty { Easy, Medium, Hard }

    /// <summary>One match, or a tournament: eight pairs, three rounds (quarter-final, semi-final, final), each next pair a
    /// little stronger (<see cref="TennisTournament"/>).</summary>
    public enum TennisFormat { Match, Tournament }

    /// <summary>
    /// How strong the computer's pair is, and how quick the player's toys are. Everything follows one number,
    /// <see cref="Strength"/> (0 = the gentlest pair, 8 = the strongest): a stronger pair hits harder, misses less, aims
    /// into the corners (and past your net player) more, plays short balls and lobs, serves faster and copes with hard
    /// balls better, while the player's toys run a little slower and react a little later, so not every ball can be
    /// reached. Every number is capped. A difficulty is a range of strengths, one per tournament round
    /// (<see cref="StrengthFor"/>); <see cref="For(TennisDifficulty, int, int)"/> builds a pair from it.
    /// </summary>
    public sealed class TennisLevel
    {
        public TennisDifficulty Difficulty;
        /// <summary>The tournament round this pair plays (0 = quarter-final, 1 = semi-final, 2 = final); a single match is
        /// played at round 1's strength.</summary>
        public int Round;
        /// <summary>0 = the gentlest pair ... 8 = the strongest (the old ladder's match number - 1).</summary>
        public float Strength;
        /// <summary>Their usual rally speed (m/s across the court).</summary>
        public float Speed;
        /// <summary>How often they get a normal ball back (before the ball's difficulty is taken off).</summary>
        public float Return;
        /// <summary>How much a hard ball (fast, perfect, smashed, far away) takes off <see cref="Return"/>: 1 at first,
        /// less for stronger pairs.</summary>
        public float Pressure;
        /// <summary>How often a shot they hit goes long, wide or into the net.</summary>
        public float Error;
        /// <summary>How often they aim into the corner away from you instead of back to you.</summary>
        public float AimAway;
        /// <summary>How often they play a short, slow ball (your back player runs in for it).</summary>
        public float ShortChance;
        public float LobChance;
        public float ServeSpeed;
        public float PowerServeChance;
        /// <summary>How often they attack a weak ball of the player's (a soft or mistimed shot, a lob: quick and deep into
        /// the far corner, often out of reach). Since 2026-11-07 the player's shots go in, so this is how a strong opponent
        /// punishes a weak swing.</summary>
        public float Attack;
        /// <summary>The player's shot goes wide when its timing is beyond this share of the window (early or late): only
        /// at the window's very edge, against the strongest (2026-11-07, the user on Hard: "many cases where my shot goes
        /// outside"; before, the outer 10 % went wide on Medium and up to 40 % on Hard). A mistimed shot is weaker
        /// instead (<see cref="TennisMatch.HumanShot"/>): the computer can attack it, but it goes in.</summary>
        public float WideFrom;
        /// <summary>Their run speed (m/s) and how long before they react to a shot (s).</summary>
        public float RunSpeed, Reaction;
        /// <summary>
        /// The player's own toys (they run to the ball by themselves, 2026-11-07, user: "the characters move on their own
        /// and go near where the ball will end up; depending on the difficulty not all balls need to be reached"): how
        /// fast they run (m/s) and how long after the other side's hit they set off (s). Quick and early against the
        /// gentle pairs (every ball is reached), slower against the strong ones, whose quick balls into the far corner
        /// can get away.
        /// </summary>
        public float HumanRunSpeed, HumanReaction;
        /// <summary>How much their aim wanders (s of timing; metres = 3x).</summary>
        public float Wobble;
        /// <summary>
        /// The rally's pace (<see cref="BallState.Pace"/>, user 2026-11-06: "from the moment the ball is served its speed
        /// starts slow, on each hit it gets faster and faster"): the serve flies at <see cref="ServePace"/> of its speed,
        /// every hit after it <see cref="PaceStep"/> quicker, up to <see cref="TopPace"/>. Stronger pairs start a little
        /// quicker and go a little further. The first numbers (a serve at 0.6, top 1.2) felt sluggish to the user on the
        /// box ("make the ball base speed faster"): now the serve at 0.85, the old speed (1) after three hits.
        /// </summary>
        public float ServePace, PaceStep, TopPace;
        /// <summary>The "near enough" window (<see cref="TennisMatch.Window"/>) is this much longer: forgiving against the
        /// gentle opponents (2026-11-07 evening: since the computer returns the first five balls of every point, a child
        /// has to hit six or more to win one, so Easy forgives timing more), the plain window against the strong ones.</summary>
        public float WindowScale = 1f;
        /// <summary>Singles (one opponent: since 2026-11-07, one player and two against each other play singles) or a
        /// pair (two players together play doubles).</summary>
        public bool Singles = true;
        /// <summary>The opponent's first name (singles) or the pair's (the back player, then the net player; the user
        /// should review the names), and their places in the list of fourteen (<see cref="TennisLooks.Opponent"/> dresses
        /// each; no partner in singles: -1).</summary>
        public string Name, Partner;
        public int NameIndex, PartnerIndex = -1;
        /// <summary>Which opponent this is: one of the fourteen players (singles) or of the <see cref="Pairs"/> pairs.</summary>
        public int Opponent;
        /// <summary>The opponent on the scoreboard: "Ana", or a pair: "Ana &amp; Beni".</summary>
        public string TeamName => Singles ? Name : $"{Name} & {Partner}";
        /// <summary>Their shirt colour index (the game's palette).</summary>
        public int Colour;

        /// <summary>The computer's players: fourteen for singles, or in pairs for doubles (Ana &amp; Beni, Era &amp; Dini,
        /// ...: seven pairs, the seven other teams of a tournament).</summary>
        public static readonly string[] Names = { "Ana", "Beni", "Era", "Dini", "Lea", "Toni", "Mira", "Leka", "Nora", "Gent", "Sara", "Arbi", "Ema", "Dion" };
        public const int Pairs = 7;

        /// <summary>The player's toys' running against the opponent's strength (tuning: TennisMatchTests' balance and reach
        /// tests).</summary>
        public static float HumanRunSpeedMin = 6.8f, HumanRunSpeedStep = 0.5f, HumanReactionMax = 0.22f, HumanReactionStep = 0.022f;

        /// <summary>How many opponents there are to meet: the fourteen players (singles) or the seven pairs.</summary>
        public static int OpponentCount(bool singles) => singles ? Names.Length : Pairs;

        /// <summary>An opponent's name for the scoreboard and the bracket: "Ana" (singles) or "Ana &amp; Beni".</summary>
        public static string OpponentName(int opponent, bool singles) => singles
            ? Names[Wrap(opponent, Names.Length)]
            : $"{Names[Wrap(opponent, Pairs) * 2]} & {Names[Wrap(opponent, Pairs) * 2 + 1]}";

        static int Wrap(int i, int n) => ((i % n) + n) % n;

        /// <summary>
        /// The strength a difficulty plays at, per tournament round (a single match: round 1). Easy is the old ladder's
        /// first two pairs (a child wins most of these), Medium its 3rd to 5th, Hard its 6th to 9th.
        /// </summary>
        public static float StrengthFor(TennisDifficulty difficulty, int round)
        {
            int r = Mathf.Clamp(round, 0, TennisTournament.Rounds - 1);
            return difficulty switch
            {
                TennisDifficulty.Easy => 0.5f * r,
                TennisDifficulty.Medium => 2.4f + 1f * r,
                _ => 5.2f + 1.4f * r,
            };
        }

        /// <summary>The opponent <paramref name="opponent"/> (a player in singles, a pair in doubles) at a difficulty, in a
        /// tournament's round (a single match: round 1).</summary>
        public static TennisLevel For(TennisDifficulty difficulty, int round, int opponent, bool singles = true)
        {
            var level = Of(StrengthFor(difficulty, round), opponent, singles);
            level.Difficulty = difficulty;
            level.Round = round;
            return level;
        }

        /// <summary>The old ladder's <paramref name="n"/>-th opponent (1 = the gentlest): strength n - 1 (tests, simulations).</summary>
        public static TennisLevel For(int n, bool singles = true)
        {
            int k = Mathf.Max(0, n - 1);
            var level = Of(k, k, singles);
            level.Difficulty = k < 2 ? TennisDifficulty.Easy : k < 5 ? TennisDifficulty.Medium : TennisDifficulty.Hard;
            return level;
        }

        /// <summary>An opponent of strength <paramref name="k"/> (0 = the gentlest ... 8 = the strongest; capped beyond): a
        /// player (singles) or a pair.</summary>
        public static TennisLevel Of(float k, int opponent, bool singles = true)
        {
            k = Mathf.Max(0f, k);
            int o = Wrap(opponent, OpponentCount(singles));
            int first = singles ? o : o * 2;
            return new TennisLevel
            {
                Strength = k,
                Speed = Mathf.Min(19.5f, 13.5f + 1.0f * k),
                Return = Mathf.Min(0.98f, 0.72f + 0.045f * k),
                Pressure = Mathf.Max(0.2f, 1f - 0.1f * k),
                Error = Mathf.Max(0.02f, 0.14f - 0.018f * k),
                AimAway = Mathf.Min(0.7f, 0.12f + 0.09f * k),
                ShortChance = Mathf.Min(0.18f, 0.03f * k),
                LobChance = Mathf.Min(0.14f, 0.03f + 0.02f * k),
                ServeSpeed = Mathf.Min(20f, 15f + 0.8f * k),
                PowerServeChance = Mathf.Min(0.35f, 0.06f * k),
                RunSpeed = Mathf.Min(8.5f, 6.5f + 0.3f * k),
                Reaction = Mathf.Max(0.15f, 0.3f - 0.02f * k),
                Attack = Mathf.Min(0.7f, 0.05f + 0.08f * k),
                HumanRunSpeed = Mathf.Max(HumanRunSpeedMin, 10.5f - HumanRunSpeedStep * k),
                HumanReaction = Mathf.Min(HumanReactionMax, 0.06f + HumanReactionStep * k),
                Wobble = Mathf.Max(0.08f, 0.22f - 0.02f * k),
                ServePace = Mathf.Min(1f, 0.85f + 0.025f * k),
                WindowScale = Mathf.Lerp(1.5f, 1f, Mathf.InverseLerp(0.5f, 5f, k)),
                PaceStep = 0.05f,
                TopPace = Mathf.Min(1.5f, 1.35f + 0.025f * k),
                WideFrom = k < 6.5f ? 99f : 0.85f,
                Singles = singles,
                Name = Names[first],
                Partner = singles ? null : Names[first + 1],
                NameIndex = first,
                PartnerIndex = singles ? -1 : first + 1,
                Opponent = o,
                Colour = o % 4,
            };
        }
    }
}
