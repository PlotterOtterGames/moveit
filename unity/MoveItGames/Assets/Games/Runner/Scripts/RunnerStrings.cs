using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Runner
{
    /// <summary>Runner-only UI text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's
    /// review (2026-11-18: Tagline, Distance, Eggs, Golden, Metres, Milestone, Result, ScoreLine, BestLine, Beat).</summary>
    public static class RunnerStrings
    {
        public static string Title => T("Vrapo!", "Run!");
        public static string Tagline => T("Vrapo me Dinon nëpër luginë!", "Run with Dino through the valley!");
        public static string KeyboardHelp => T("← →  HAPËSIRË  ↓", "← →  SPACE  ↓");
        public static string Jump => T("KËRCE!", "JUMP!");
        public static string Duck => T("ULU!", "DUCK!");
        public static string Switch => T("KALO ANASH!", "MOVE ASIDE!");
        public static string Coins => T("Vezë", "Eggs");   // the dino collects eggs
        public static string Eggs => T("Vezë", "Eggs");
        public static string Distance => T("Distanca", "Distance");
        public static string Golden => T("Vezë e artë!", "Golden egg!");
        public static string Metres(int m) => $"{m} m";
        public static string Milestone(int m) => T($"{m} metra!", $"{m} metres!");
        public static string Result => T("Bravo, Dino!", "Well run, Dino!");
        public static string Beat => T("Rekord i ri!", "New best!");
        public static string Ready => T("Gati?", "Ready?");
    }
}
