using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Slash
{
    /// <summary>Fruta-only UI text (shared text lives in <see cref="CommonStrings"/>).</summary>
    public static class SlashStrings
    {
        public static string Title => T("Fruta!", "Fruit Slash!");
        public static string HowTo => T("Copëto frutat me duar, shmang bombat!", "Slice the fruit with your hands, avoid the bombs!");
        public static string KeyboardHelp => T("lëviz mausin shpejt", "swipe with the mouse");
        public static string TimeUp => T("Koha mbaroi!", "Time's up!");
        public static string Sliced => T("Fruta të copëtuara", "Fruit sliced");
        public static string BestCombo => T("Kombo më e mirë", "Best combo");
        public static string Combo(int n) => T($"KOMBO x{n}!", $"COMBO x{n}!");
    }
}
