using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Bowling
{
    /// <summary>Bowling-only UI text (shared text lives in <see cref="CommonStrings"/>).</summary>
    public static class BowlingStrings
    {
        public static string Title => "Bowling";
        public static string KeyboardHelp => T("← → vendi   ↑ ↓ drejtimi   HAPËSIRË (mbaje) hidh", "← → position   ↑ ↓ aim   SPACE (hold) throw");
        public static string HowManyPlayers => T("Sa lojtarë?", "How many players?");
        public static string PlayersHint => T("Hap majtas ose djathtas për të ndryshuar, ngri duart për të konfirmuar", "Step left or right to change, raise both hands to confirm");
        public static string PlayersKeyboardHint => T("← → ndrysho,  ENTER konfirmo", "← → change,  ENTER confirm");
        public static string Player(int n) => T($"Lojtari {n}", $"Player {n}");
        public static string Turn(int n) => T($"Radha e Lojtarit {n}", $"Player {n}'s turn");
        public static string AimHint => T("Lëviz anash për të zgjedhur vendin, pastaj tunde dorën përpara", "Move sideways to pick your spot, then swing your arm forward");
        public static string Strike => "STRIKE!";
        public static string Spare => "SPARE!";
        public static string Gutter => T("Në kanal!", "Gutter!");
        public static string PinsDown(int n) => T($"Kunja të rrëzuar: {n}", $"Pins down: {n}");
        public static string Total => T("Gjithsej", "Total");
        public static string Winner(int n) => T($"Fitoi Lojtari {n}!", $"Player {n} wins!");
        public static string Draw => T("Barazim!", "It's a draw!");
    }
}
