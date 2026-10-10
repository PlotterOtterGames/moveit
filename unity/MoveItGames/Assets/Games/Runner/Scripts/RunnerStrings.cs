using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Runner
{
    /// <summary>Runner-only UI text (shared text lives in <see cref="CommonStrings"/>).</summary>
    public static class RunnerStrings
    {
        public static string Title => T("Vrapo!", "Run!");
        public static string KeyboardHelp => T("← →  HAPËSIRË  ↓", "← →  SPACE  ↓");
        public static string Jump => T("KËRCE!", "JUMP!");
        public static string Duck => T("ULU!", "DUCK!");
        public static string Switch => T("KALO ANASH!", "MOVE ASIDE!");
        public static string Coins => T("Vezë", "Eggs");   // the dino collects eggs
    }
}
