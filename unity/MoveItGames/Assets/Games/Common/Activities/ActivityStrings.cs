using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games
{
    /// <summary>Text shared by the activity games (Aventura!, Sporte!), Albanian first (the user reviews it).</summary>
    public static class ActivityStrings
    {
        public static string ChooseHint => T("Hap majtas ose djathtas për të zgjedhur, ngri duart lart për të luajtur",
                                             "Step left or right to choose, raise both hands to play");
        public static string ChooseKeys => T("← → zgjidh,  ENTER luaj,  Esc menuja", "← → choose,  ENTER play,  Esc menu");
        public static string Points => T("Pikë", "Points");
        public static string Time => T("Koha", "Time");
        public static string Best(int points) => T($"Rekordi: {points}", $"Best: {points}");
        public static string NoBest => T("Ende pa luajtur", "Not played yet");
        public static string GetReady => T("Bëhu gati!", "Get ready!");
        public static string Finish => T("MBARIM!", "FINISH!");
        public static string TimeUp => T("Koha mbaroi!", "Time's up!");
        public static string BackToChoose => T("Ngri duart lart për të zgjedhur një lojë tjetër", "Raise both hands to choose another one");
        public static string BackKeys => T("ENTER: tjetra", "ENTER: next");
        public static string EscBack => T("Esc = kthehu te zgjedhja", "Esc = back to the choice");
        public static string MedalsWon(int n, int of) => T($"Medalje: {n} nga {of}", $"Medals: {n} of {of}");
        public static string YouWon(Medal m) => m == Medal.None ? T("Provo përsëri për një medalje!", "Try again for a medal!")
                                                                 : T($"Medalje {MedalName(m)}!", $"{MedalName(m)} medal!");
        public static string NextMedal(Medal m, int points) => T($"{MedalName(m)}: {points} pikë", $"{MedalName(m)}: {points} points");

        public static string MedalName(Medal m) => m switch
        {
            Medal.Bronze => T("Bronz", "Bronze"),
            Medal.Silver => T("Argjend", "Silver"),
            Medal.Gold => T("Ar", "Gold"),
            Medal.Platinum => T("Platin", "Platinum"),
            _ => T("Pa medalje", "No medal"),
        };
    }
}
