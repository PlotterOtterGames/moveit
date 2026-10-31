using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Tennis
{
    /// <summary>Tenis!-only UI text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's review.</summary>
    public static class TennisStrings
    {
        public static string Title => T("Tenis!", "Tennis!");
        public static string HowTo => T("Lëviz majtas, djathtas, përpara e prapa; tund dorën si raketë kur vjen topi!",
                                        "Move left, right, forward and back; swing your arm like a racket when the ball comes!");
        public static string AimHint => T("Godit herët: topi shkon kryq. Godit vonë: shkon anash.", "Swing early: the ball goes across. Swing late: down the line.");
        public static string KeyboardHelp => T("shigjetat = lëviz, hapësira = godit (mbaj W = lart, S = prerë)",
                                               "arrows = move, space = swing (hold W = lob, S = slice)");
        public static string YourServe => T("Shërbe ti! Godit kur topi është lart", "Your serve! Swing when the ball is up high");
        public static string SwingNow => T("Godit kur rrethi mbyllet!", "Swing when the ring closes!");
        public static string MoveToTheBall => T("Lëviz te rrethi në fushë!", "Move to the ring on the court!");
        public static string GetCloser => T("Afrohu më shumë te topi!", "Get closer to the ball!");
        public static string You => T("Ti", "You");
        public static string Match(int n) => T($"Ndeshja {n}", $"Match {n}");
        public static string Versus(string them) => T($"Ti kundër {them}", $"You vs {them}");
        public static string BestOf3 => T("Fiton kush merr 2 lojëra", "First to win 2 games");

        // the umpire
        public static string Love => T("zero", "love");
        public static string Deuce => T("Barazim!", "Deuce!");
        public static string Advantage(string who) => T($"Avantazh: {who}", $"Advantage: {who}");
        public static string Game(string who) => T($"Loja: {who}!", $"Game: {who}!");
        public static string GamePoint => T("Pikë loje", "Game point");
        public static string MatchPoint => T("Pikë ndeshjeje!", "Match point!");
        public static string Out => T("Jashtë!", "Out!");
        public static string Net => T("Rrjetë!", "Net!");

        // how a shot went
        public static string Early => T("Herët!", "Early!");
        public static string Late => T("Vonë!", "Late!");
        public static string Perfect => T("Përsosur!", "Perfect!");
        public static string PowerServe => T("Shërbim i fuqishëm!", "Power serve!");
        public static string Smash => T("Smesh!", "Smash!");
        public static string Lob => T("Lob!", "Lob!");
        public static string Ace => T("As!", "Ace!");
        public static string Winner => T("Pikë e bukur!", "Winner!");

        // between matches
        public static string YouWon => T("Fitove ndeshjen!", "You won the match!");
        public static string TheyWon(string them) => T($"Fitoi {them}", $"{them} won");
        public static string NextPlayer => T("Vjen lojtari tjetër, më i fortë!", "Here comes the next player, a stronger one!");
        public static string MatchesWon => T("Ndeshje të fituara", "Matches won");
        public static string LongestRally => T("Rrahja më e gjatë", "Longest rally");
        public static string HandSwapped(bool right) => right ? T("Raketa në dorën e djathtë", "Racket in the right hand")
                                                              : T("Raketa në dorën e majtë", "Racket in the left hand");
        public static string SwapHint => T("↑ ose H = ndërro dorën", "↑ or H = switch hands");
    }
}
