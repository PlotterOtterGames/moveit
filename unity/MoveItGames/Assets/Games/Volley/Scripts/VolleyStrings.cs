using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Volley
{
    /// <summary>Volejboll!-only UI text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's
    /// review.</summary>
    public static class VolleyStrings
    {
        public static string Title => T("Volejboll!", "Volleyball!");
        /// <summary>The three moves, as the waiting screen teaches them (and the icons over the toy that plays the ball).</summary>
        public static string HowTo => T("Lojtarët vrapojnë vetë te topi. Ti bëj lëvizjen:",
                                        "Your players run to the ball by themselves. You make the move:");
        public static string HowBump => T("Duart bashkë poshtë: prit topin", "Hands together low: receive");
        public static string HowSet => T("Dy duart lart: ngrije për shokun", "Both hands up: set it for your partner");
        public static string HowHit => T("Një dorë: godite matanë", "One hand: hit it over");
        /// <summary>The defence (2026-11-08): as their hitter jumps, both hands up block a smash; a tip is received low.</summary>
        public static string HowBlock => T("Kur hidhen për smesh: dy duart lart, blloko", "When they jump to spike: both hands up, block");
        public static string HowDefend => T("Kur sulmojnë, zgjidh: blloko smeshin lart, prit fintën poshtë!",
                                            "When they attack, choose: block a spike up high, receive a tip down low!");
        public static string KeyboardHelp => T("↓ = prit · ↑ = ngrije / blloko · hapësira = godit", "↓ = receive · ↑ = set / block · space = hit");

        // what the moves are called over the toy
        public static string Bump => T("PRIT", "RECEIVE");
        public static string Set => T("NGRIJE", "SET");
        public static string Hit => T("GODIT", "HIT");
        public static string Block => T("BLLOKO", "BLOCK");

        // the defence: how it went
        public static string Blocked => T("Bllok!", "Block!");
        public static string OverTheBlock => T("Mbi bllok!", "Over the block!");
        public static string TooHard => T("Shumë i fortë!", "Too hard!");
        public static string TipCall => T("Fintë!", "Tip!");

        // hints after a ball the player didn't play
        public static string ReceiveHint => T("Topi që vjen matanë pritet me duart bashkë poshtë!", "A ball from the other side: hands together, low!");
        /// <summary>A smash wasn't blocked (received low, or nothing).</summary>
        public static string BlockHint => T("Kur hidhen për smesh: dy duart lart, blloko!", "When they jump to spike: both hands up, block!");
        /// <summary>A tip went over the block.</summary>
        public static string TipHint => T("Fintë! Kur e prekin lehtë, duart bashkë poshtë", "A tip! When they just touch it: hands together, low");
        public static string SetHint => T("Ngri dy duart lart për ta ngritur për shokun!", "Raise both hands to set it for your partner!");
        public static string HitHint => T("Godite me një dorë kur topi është lart!", "Hit it with one hand when it's up high!");
        public static string ServeHint => T("Shërbe ti! Godite me një dorë kur topi është lart", "Your serve! Hit it with one hand while it's up");
        public static string OutOfReach => T("S'e arriti!", "Out of reach!");

        // how a touch went
        public static string Spike => T("Smesh!", "Spike!");
        public static string SuperSpike => T("Smesh i fortë!", "Power spike!");
        public static string Save => T("Shpëtim!", "Save!");
        public static string Ace => T("As!", "Ace!");
        public static string Point => T("Pikë!", "Point!");
        public static string Out => T("Jashtë!", "Out!");
        public static string Net => T("Rrjetë!", "Net!");
        public static string Wrong => T("Gabim!", "Shanked!");
        public static string MatchPoint => T("Pikë ndeshjeje!", "Match point!");
        public static string FirstTo(int points) => T($"Fiton kush arrin {points} pikë (me 2 diferencë)", $"First to {points} points (two clear)");

        // the scoreboard and the results
        public static string You => T("Ti", "You");
        public static string Us => T("Ju", "You");
        public static string Player(int slot) => T($"Lojtari {slot + 1}", $"Player {slot + 1}");
        public static string Versus(string them) => T($"Ti kundër {them}", $"You vs {them}");
        public static string UsVersus(string them) => T($"Ju kundër {them}", $"You vs {them}");
        public static string PlayerVersusPlayer => T("Lojtari 1 kundër Lojtarit 2", "Player 1 vs Player 2");
        public static string YouWon => T("Fitove ndeshjen!", "You won the match!");
        public static string WeWon => T("Fituat ndeshjen!", "You won the match!");
        public static string TheyWon(string them) => T($"Fituan {them}", $"{them} won");
        public static string Won(int slot) => T($"Fitoi {Player(slot)}!", $"{Player(slot)} wins!");
        public static string LongestRally => T("Rrahja më e gjatë", "Longest rally");
        public static string Spikes => T("Smeshe", "Spikes");
        public static string Blocks => T("Blloqe", "Blocks");
        public static string Touches => T("Prekje", "Touches");

        // two players
        public static string SoloMode => T("1 LOJTAR", "1 PLAYER");
        public static string VersusMode => T("KUNDËR", "VERSUS");
        public static string TogetherMode => T("BASHKË", "TOGETHER");
        public static string VersusModeSub => T("2 lojtarë, njëri kundër tjetrit", "2 players, against each other");
        public static string TogetherModeSub => T("2 lojtarë në një skuadër: njëri pret, tjetri e ngre", "2 players on one team: one receives, the other sets");
        public static string StandStill => T("Qëndro pa lëvizur…", "Hold still…");
        public static string RaiseYourHands => T("Ngri duart lart!", "Raise your hands!");
        public static string Ready => T("Gati!", "Ready!");
        public static string StepApart => T("Largohuni pak nga njëri-tjetri", "Move a little apart");
        public static string WaitingFor(int slot) => T($"{Player(slot)}, ngri duart!", $"{Player(slot)}, raise your hands!");
        public static string PlayingAloneIn(int seconds) => T($"ose luan vetëm pas {seconds}…", $"or you play alone in {seconds}…");
        public static string ComeBackPlayer(int slot) => T($"{Player(slot)}, kthehu para kamerës!", $"{Player(slot)}, come back in front of the camera!");
        public static string HoldStillPlayer(int slot) => T($"{Player(slot)}: qëndro pa lëvizur…", $"{Player(slot)}: hold still…");

        // the menu
        public static string PlayersRow => T("Lojtarë", "Players");
        public static string GameRow => T("Loja", "Game");
        public static string DifficultyRow => T("Vështirësia", "Difficulty");
        public static string ViewRow => T("Pamja", "View");
        public static string SingleMatch => T("NDESHJE", "MATCH");
        public static string Tournament => T("TURNE", "TOURNAMENT");
        public static string Difficulty(VolleyDifficulty d) => d switch
        {
            VolleyDifficulty.Easy => T("LEHTË", "EASY"),
            VolleyDifficulty.Medium => T("MESATARE", "MEDIUM"),
            _ => T("VËSHTIRË", "HARD"),
        };
        public static string View(int view) => view == 0 ? T("KLASIKE", "CLASSIC") : T("NGA LART", "FROM ABOVE");
        public static string MenuHint => T("↑ ↓ zgjidh    ← → ndrysho", "↑ ↓ choose    ← → change");

        // the tournament
        public static string RoundName(int round) => round switch
        {
            0 => T("Çerekfinalja", "Quarter-final"),
            1 => T("Gjysmëfinalja", "Semi-final"),
            _ => T("Finalja", "Final"),
        };
        static string RoundIn(int round) => round switch
        {
            0 => T("çerekfinale", "the quarter-final"),
            1 => T("gjysmëfinale", "the semi-final"),
            _ => T("finale", "the final"),
        };
        public static string TournamentOf(VolleyDifficulty d) => T($"Turneu · {Difficulty(d)}", $"Tournament · {Difficulty(d)}");
        public static string SingleOf(VolleyDifficulty d) => T($"Ndeshje · {Difficulty(d)}", $"Match · {Difficulty(d)}");
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
