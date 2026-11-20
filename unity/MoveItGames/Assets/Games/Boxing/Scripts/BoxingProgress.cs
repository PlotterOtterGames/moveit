using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MoveIt.Games.Boxing
{
    /// <summary>What a won (or lost) fight brought, for the results screen.</summary>
    public sealed class FightReward
    {
        public bool Won, FirstWin;
        public int Stars, NewStars;
        /// <summary>A stage's champion beaten for the first time at this difficulty: its belt.</summary>
        public bool Belt;
        /// <summary>The champion's gloves, new to the hero (any difficulty): their index in
        /// <see cref="BoxingProgress.GloveCount"/>.</summary>
        public int NewGloves = -1;
        public readonly List<int> NewOutfits = new List<int>();
        /// <summary>The whole climb done at this difficulty for the first time.</summary>
        public bool Champion;
    }

    /// <summary>
    /// The climb's progress, kept on the box (no accounts, nothing sold): per difficulty, the best stars of every fight
    /// (0 = not won yet; each fight opens when the one before is won, like Punch-Out!!'s circuits); a stage's champion
    /// beaten = its belt at that difficulty (bronze, silver, gold); the champions' gloves to wear (Wii Sports' prize for
    /// beating its champion was his silver gloves); the stars of every difficulty together open the hero's outfits. Pure
    /// logic (tests); <c>BoxingGame</c> keeps it in PlayerPrefs with <see cref="Save"/> / <see cref="Load"/>. Saved as
    /// "b2" since the climb became four stages (2026-11-19); a "b1" save (six stages) starts over.
    /// </summary>
    public sealed class BoxingProgress
    {
        public const int Difficulties = 3;
        /// <summary>Stars (all difficulties together, 108 at most) that open each outfit (the first is the hero's own).</summary>
        public static readonly int[] OutfitStars = { 0, 5, 12, 20, 30, 42, 58, 80 };
        /// <summary>The classic red gloves, then each stage champion's.</summary>
        public static int GloveCount => 1 + Campaign.Stages.Length;

        readonly int[,] _stars = new int[Difficulties, Campaign.Fights];
        public int Outfit, Gloves, Difficulty;
        /// <summary>The fight chosen on the map, per difficulty.</summary>
        public readonly int[] Selected = new int[Difficulties];
        public int Fights, Wins;

        public int StarsOf(BoxingDifficulty d, int fight) => _stars[(int)d, fight];
        public bool Won(BoxingDifficulty d, int fight) => _stars[(int)d, fight] > 0;
        public bool Unlocked(BoxingDifficulty d, int fight) => fight <= 0 || fight < Campaign.Fights && Won(d, fight - 1);

        /// <summary>The first fight not won yet at <paramref name="d"/> (the last when all are).</summary>
        public int Next(BoxingDifficulty d)
        {
            for (int i = 0; i < Campaign.Fights; i++)
                if (!Won(d, i)) return i;
            return Campaign.Fights - 1;
        }

        public bool Belt(BoxingDifficulty d, int stage) => Won(d, stage * Campaign.FightsPerStage + Campaign.FightsPerStage - 1);
        public bool ClimbDone(BoxingDifficulty d) => Won(d, Campaign.Fights - 1);

        public int TotalStars
        {
            get
            {
                int n = 0;
                foreach (int s in _stars) n += s;
                return n;
            }
        }

        public int StarsAt(BoxingDifficulty d)
        {
            int n = 0;
            for (int i = 0; i < Campaign.Fights; i++) n += _stars[(int)d, i];
            return n;
        }

        public int OutfitsUnlocked
        {
            get
            {
                int total = TotalStars, n = 0;
                foreach (int s in OutfitStars) if (total >= s) n++;
                return n;
            }
        }

        /// <summary>Gloves 0 = the hero's own; gloves g = stage g-1's champion's, theirs once beaten at any difficulty.</summary>
        public bool GlovesUnlocked(int g)
        {
            if (g <= 0) return true;
            if (g >= GloveCount) return false;
            for (int d = 0; d < Difficulties; d++)
                if (Belt((BoxingDifficulty)d, g - 1)) return true;
            return false;
        }

        /// <summary>A fight played: counts it, keeps the best stars, opens what it opens.</summary>
        public FightReward Record(BoxingDifficulty d, int fight, bool won, int stars)
        {
            var r = new FightReward { Won = won, Stars = won ? Math.Max(1, Math.Min(3, stars)) : 0 };
            Fights++;
            if (!won) return r;
            Wins++;
            int before = _stars[(int)d, fight];
            int outfitsBefore = OutfitsUnlocked;
            bool[] gloves = new bool[GloveCount];
            for (int g = 0; g < GloveCount; g++) gloves[g] = GlovesUnlocked(g);
            bool climbBefore = ClimbDone(d);
            r.FirstWin = before == 0;
            r.NewStars = Math.Max(0, r.Stars - before);
            _stars[(int)d, fight] = Math.Max(before, r.Stars);
            var o = Campaign.Opponent(fight);
            r.Belt = o.Champion && before == 0;
            for (int g = 0; g < GloveCount; g++)
                if (!gloves[g] && GlovesUnlocked(g)) r.NewGloves = g;
            for (int i = outfitsBefore; i < OutfitsUnlocked; i++) r.NewOutfits.Add(i);
            r.Champion = !climbBefore && ClimbDone(d);
            if (fight + 1 < Campaign.Fights && Selected[(int)d] == fight) Selected[(int)d] = fight + 1;
            return r;
        }

        // ------------------------------------------------------------------ saving

        const string Version = "b2";

        public string Save()
        {
            var sb = new StringBuilder(Version);
            for (int d = 0; d < Difficulties; d++)
            {
                sb.Append(';');
                for (int i = 0; i < Campaign.Fights; i++) sb.Append((char)('0' + _stars[d, i]));
            }
            sb.Append(';').Append(Outfit).Append(';').Append(Gloves).Append(';').Append(Difficulty);
            for (int d = 0; d < Difficulties; d++) sb.Append(';').Append(Selected[d]);
            sb.Append(';').Append(Fights).Append(';').Append(Wins);
            return sb.ToString();
        }

        public static BoxingProgress Load(string text)
        {
            var p = new BoxingProgress();
            if (string.IsNullOrEmpty(text) || !text.StartsWith(Version + ";", StringComparison.Ordinal)) return p;
            try
            {
                var parts = text.Split(';');
                for (int d = 0; d < Difficulties && d + 1 < parts.Length; d++)
                    for (int i = 0; i < Campaign.Fights && i < parts[d + 1].Length; i++)
                        p._stars[d, i] = Math.Max(0, Math.Min(3, parts[d + 1][i] - '0'));
                int k = Difficulties + 1;
                int Int(int fallback) => k < parts.Length && int.TryParse(parts[k++], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
                p.Outfit = Int(0);
                p.Gloves = Int(0);
                p.Difficulty = Math.Max(0, Math.Min(Difficulties - 1, Int(0)));
                for (int d = 0; d < Difficulties; d++) p.Selected[d] = Math.Max(0, Math.Min(Campaign.Fights - 1, Int(0)));
                p.Fights = Int(0);
                p.Wins = Int(0);
                if (p.Outfit >= p.OutfitsUnlocked) p.Outfit = 0;
                if (!p.GlovesUnlocked(p.Gloves)) p.Gloves = 0;
                for (int d = 0; d < Difficulties; d++)
                    if (!p.Unlocked((BoxingDifficulty)d, p.Selected[d])) p.Selected[d] = p.Next((BoxingDifficulty)d);
            }
            catch (Exception)
            {
                return new BoxingProgress();
            }
            return p;
        }

        /// <summary>Test hook: sets a fight's stars.</summary>
        public void SetStars(BoxingDifficulty d, int fight, int stars) => _stars[(int)d, fight] = Math.Max(0, Math.Min(3, stars));
    }
}
