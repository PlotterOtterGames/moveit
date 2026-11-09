using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Tennis
{
    /// <summary>Tenis!-only UI text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's review.</summary>
    public static class TennisStrings
    {
        public static string Title => T("Tenis!", "Tennis!");
        /// <summary>Since 2026-11-07 the toys run to the ball by themselves: the player only swings (one toy each: singles,
        /// or one each of a pair when two play together).</summary>
        public static string HowTo => T("Lojtari yt vrapon vetë te topi. Ti lëkunde dorën anash kur topi të afrohet!",
                                        "Your player runs to the ball by itself. Swing your hand sideways when the ball comes close!");
        /// <summary>A strong swing down at the ball missed it (since 2026-11-07 a swing up lobs it).</summary>
        public static string SwingSideways => T("Lëkunde dorën anash ose lart, jo poshtë!", "Swing sideways or up, not down!");
        public static string AimHint => T("Godit herët: topi shkon kryq. Vonë: shkon drejt.", "Swing early: the ball goes across. Late: straight.");
        public static string KeyboardHelp => T("hapësira = godit (mbaj W = lart, S = prerë)", "space = swing (hold W = lob, S = slice)");
        public static string YourServe => T("Shërbe ti! Godit kur topi është lart", "Your serve! Swing when the ball is up high");
        public static string SwingWhenClose => T("Lëkunde dorën anash kur topi të afrohet!", "Swing your hand sideways when the ball comes close!");
        /// <summary>A ball the toy couldn't get to (a quick ball into a far corner, on the harder difficulties).</summary>
        public static string OutOfReach => T("S'e arriti!", "Out of reach!");
        public static string TooEarly => T("Herët! Prit pak topin.", "Too early! Wait for the ball.");
        public static string TooLate => T("Vonë! Godit pak më shpejt.", "Too late! Swing a little sooner.");
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
        public static string Perfect => T("Përsosur!", "Perfect!");
        public static string PowerServe => T("Shërbim i fuqishëm!", "Power serve!");
        public static string Smash => T("Smesh!", "Smash!");
        public static string Lob => T("Lob!", "Lob!");
        public static string Ace => T("As!", "Ace!");
        public static string Winner => T("Pikë e bukur!", "Winner!");

        // between matches
        public static string YouWon => T("Fitove ndeshjen!", "You won the match!");
        /// <summary>The computer won: one player (singles) or a pair.</summary>
        public static string TheyWon(string them, bool one) => one ? T($"Fitoi {them}", $"{them} won") : T($"Fituan {them}", $"{them} won");
        public static string MatchesWon => T("Ndeshje të fituara", "Matches won");
        public static string LongestRally => T("Rrahja më e gjatë", "Longest rally");
        public static string HandSwapped(bool right) => right ? T("Raketa në dorën e djathtë", "Racket in the right hand")
                                                              : T("Raketa në dorën e majtë", "Racket in the left hand");
        public static string SwapHint => T("↑ ose H = ndërro dorën", "↑ or H = switch hands");

        // two players (2026-11-05)
        public static string Player(int slot) => T($"Lojtari {slot + 1}", $"Player {slot + 1}");
        public static string VersusMode => T("KUNDËR", "VERSUS");
        public static string VersusModeSub => T("2 lojtarë, njëri kundër tjetrit", "2 players, against each other");
        public static string TogetherMode => T("BASHKË", "TOGETHER");
        public static string TogetherModeSub => T("2 lojtarë kundër kompjuterit", "2 players against the computer");
        public static string ChooseMode => T("← → në telekomandë: zgjidh lojën", "← → on the remote: choose the game");
        public static string SoloMode => T("1 LOJTAR", "1 PLAYER");
        public static string StandStill => T("Qëndro pa lëvizur…", "Hold still…");
        public static string RaiseYourHands => T("Ngri duart lart!", "Raise your hands!");
        public static string Ready => T("Gati!", "Ready!");
        public static string StepApart => T("Largohuni pak nga njëri-tjetri", "Move a little apart");
        public static string WaitingFor(int slot) => T($"{Player(slot)}, ngri duart!", $"{Player(slot)}, raise your hands!");
        public static string PlayingAloneIn(int seconds) => T($"ose luan vetëm pas {seconds}…", $"or you play alone in {seconds}…");
        public static string ComeBackPlayer(int slot) => T($"{Player(slot)}, kthehu para kamerës!", $"{Player(slot)}, come back in front of the camera!");
        public static string HoldStillPlayer(int slot) => T($"{Player(slot)}: qëndro pa lëvizur…", $"{Player(slot)}: hold still…");
        public static string Us => T("Ju", "You");
        public static string UsVersus(string them) => T($"Ju kundër {them}", $"You vs {them}");
        public static string PlayerVersusPlayer => T("Lojtari 1 kundër Lojtarit 2", "Player 1 vs Player 2");
        public static string WeWon => T("Fituat ndeshjen!", "You won the match!");
        public static string Won(int slot) => T($"Fitoi {Player(slot)}!", $"{Player(slot)} wins!");
        public static string SwapPlaces => T("Ndërroni vendet!", "Change places!");
        public static string AtTheBack(int slot) => T($"{Player(slot)} prapa, shoku te rrjeta", $"{Player(slot)} at the back, partner at the net");
        public static string Hits => T("Goditje", "Hits");
        public static string PerfectShots => T("Të përsosura", "Perfect");
        public static string Rematch => T("Ngrini duart të dy për revanshin", "Both raise your hands for a rematch");

        // the menu on the waiting screen (2026-11-07): who plays, what, how hard, from where
        public static string PlayersRow => T("Lojtarë", "Players");
        public static string GameRow => T("Loja", "Game");
        public static string DifficultyRow => T("Vështirësia", "Difficulty");
        public static string ViewRow => T("Pamja", "View");
        public static string SingleMatch => T("NDESHJE", "MATCH");
        public static string Tournament => T("TURNE", "TOURNAMENT");
        public static string Difficulty(TennisDifficulty d) => d switch
        {
            TennisDifficulty.Easy => T("LEHTË", "EASY"),
            TennisDifficulty.Medium => T("MESATARE", "MEDIUM"),
            _ => T("VËSHTIRË", "HARD"),
        };
        /// <summary>The camera: a little steeper than Wii's (the default), or high above the court.</summary>
        public static string View(int view) => view == 0 ? T("KLASIKE", "CLASSIC") : T("NGA LART", "FROM ABOVE");
        public static string MenuHint => T("↑ ↓ zgjidh    ← → ndrysho", "↑ ↓ choose    ← → change");
        public static string StartHint(bool two) => two ? T("Ngrini duart të dy për të filluar", "Both raise your hands to start")
                                                        : T("Ngri duart lart për të filluar", "Raise both hands to start");
        public static string HandHint => T("Gjatë lojës: ↑ ose H = ndërro dorën e raketës", "While playing: ↑ or H = switch the racket hand");

        // the tournament
        public static string RoundName(int round) => round switch
        {
            0 => T("Çerekfinalja", "Quarter-final"),
            1 => T("Gjysmëfinalja", "Semi-final"),
            _ => T("Finalja", "Final"),
        };
        /// <summary>A round's name after "in" ("në çerekfinale").</summary>
        static string RoundIn(int round) => round switch
        {
            0 => T("çerekfinale", "the quarter-final"),
            1 => T("gjysmëfinale", "the semi-final"),
            _ => T("finale", "the final"),
        };
        public static string TournamentOf(TennisDifficulty d) => T($"Turneu · {Difficulty(d)}", $"Tournament · {Difficulty(d)}");
        public static string SingleOf(TennisDifficulty d) => T($"Ndeshje · {Difficulty(d)}", $"Match · {Difficulty(d)}");
        /// <summary>Won a round: through to the next (one player, or two: plural).</summary>
        public static string ThroughTo(int round, bool two) => two ? T($"Kaluat në {RoundIn(round)}!", $"Through to {RoundIn(round)}!")
                                                                   : T($"Kalove në {RoundIn(round)}!", $"Through to {RoundIn(round)}!");
        public static string OutIn(int round, bool two) => round >= 2
            ? (two ? T("Humbët finalen", "You lost the final") : T("Humbe finalen", "You lost the final"))
            : (two ? T($"U eliminuat në {RoundIn(round)}", $"Out in {RoundIn(round)}") : T($"U eliminove në {RoundIn(round)}", $"Out in {RoundIn(round)}"));
        public static string Champion(bool two) => two ? T("Kampionë!", "Champions!") : T("Kampion!", "Champion!");
        public static string WonTheCup(bool two) => two ? T("Fituat kupën e turneut!", "You won the tournament cup!")
                                                        : T("Fitove kupën e turneut!", "You won the tournament cup!");
        public static string TournamentWinner(string team) => T($"Fitues i turneut: {team}", $"Tournament winner: {team}");
        public static string NextMatch(string them) => T($"Ndeshja tjetër: kundër {them}", $"Next match: against {them}");
        public static string RaiseForNext(bool two) => two ? T("Ngrini duart të dy për ndeshjen tjetër", "Both raise your hands for the next match")
                                                           : T("Ngri duart lart për ndeshjen tjetër", "Raise both hands for the next match");
        public static string Cups => T("Kupa", "Cups");
        public static string AgainOrMenu(bool two) => two ? T("Ngrini duart për të luajtur sërish · shigjetat: menuja", "Both raise your hands to play again · arrows: the menu")
                                                          : T("Ngri duart për të luajtur sërish · shigjetat: menuja", "Raise both hands to play again · arrows: the menu");
    }
}
