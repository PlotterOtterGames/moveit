using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Sword
{
    /// <summary>Shpata!-only UI text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's review.</summary>
    public static class SwordStrings
    {
        public static string Title => T("Shpata!", "Swords!");
        public static string HowTo => T("Mbrohu me mburojë kur kalorësi sulmon, pastaj godite!",
                                        "Block with your shield when the knight attacks, then hit back!");
        public static string KeyboardHelp => T("hapësira = mburoja, shigjetat = godit", "space = shield, arrows = swing");
        public static string ShieldHere => T("Mburoja te rrethi!", "Shield on the circle!");
        public static string HitNow => T("Tani godite!", "Now hit it!");
        public static string OpenGuard => T("E uli mburojën: godite!", "It lowered its shield: hit it!");
        public static string WaitForAttack => T("Mburoja e tij të ndal: prit sulmin, mbrohu, pastaj godit!",
                                                "Its shield stops you: wait for its attack, block, then hit!");
        public static string Hit => T("Goditje!", "Hit!");
        public static string Blocked => T("Mbrojtje!", "Blocked!");
        public static string Ouch => T("Oj!", "Ouch!");
        public static string Victory => T("Fitore!", "Victory!");
        public static string NextKnight => T("Vjen kalorësi tjetër!", "Here comes the next knight!");
        public static string HeartsBack(int n) => T($"+{n} zemra", $"+{n} hearts");
        public static string Knight(int n) => T($"Kalorësi {n}", $"Knight {n}");
        public static string KnightsBeaten => T("Kalorës të mundur", "Knights beaten");
        public static string HandSwapped(bool right) => right ? T("Shpata në dorën e djathtë", "Sword in the right hand")
                                                              : T("Shpata në dorën e majtë", "Sword in the left hand");
        public static string SwapHint => T("↑ ose H = ndërro duart", "↑ or H = switch hands");
    }
}
