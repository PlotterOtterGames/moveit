using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Kart
{
    /// <summary>Gara!-only UI text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's
    /// review.</summary>
    public static class KartStrings
    {
        public static string Title => T("Gara!", "Race!");

        // how to play (the waiting screen's cards)
        public static string HowTo => T("Makina ecën vetë. Ti drejtoje:", "The kart drives by itself. You steer:");
        public static string HowSteer => T("Dy duart përpara si në timon: ktheje majtas ose djathtas", "Both hands out like on a wheel: turn it left or right");
        public static string HowJump => T("Kërce te rampa: salto dhe turbo!", "Jump at a ramp: a trick and a turbo!");
        public static string HowDrift => T("Mbaje timonin të kthyer fort në kthesë: shkëndija, pastaj turbo", "Hold the wheel turned hard through a corner: sparks, then a turbo");
        public static string HowItem => T("Ngri duart lart: përdor dhuratën", "Raise both hands: use your gift");
        public static string KeyboardHelp => T("← → drejto · hapësira = kërce · ↑ / Enter = dhurata", "← → steer · space = jump · ↑ / Enter = gift");

        // during the race
        public static string Go => T("NISU!", "GO!");
        public static string Lap(int lap, int laps) => T($"Xhiro {lap}/{laps}", $"Lap {lap}/{laps}");
        public static string LapShort => T("XHIRO", "LAP");
        public static string Laps(int laps) => T($"{laps} xhiro", $"{laps} laps");
        public static string FinalLap => T("Xhiroja e fundit!", "Final lap!");
        public static string HoldTheWheel => T("Mbaj timonin me dy duar!", "Hold the wheel with both hands!");
        public static string JumpHint => T("Kërce te rampa!", "Jump at the ramp!");
        public static string ItemHint => T("Ngri duart për ta përdorur!", "Raise your hands to use it!");
        public static string QuickStart => T("Nisje e shpejtë!", "Quick start!");
        public static string Trick => T("Salto!", "Trick!");
        public static string PerfectTrick => T("Salto e përsosur!", "Perfect trick!");
        public static string DoubleTrick => T("Salto dyshe!", "Double trick!");
        public static string Turbo => T("Turbo!", "Turbo!");
        public static string SuperTurbo => T("Super turbo!", "Super turbo!");
        public static string Painted => T("Bojë!", "Splat!");
        public static string HoneyHit => T("Mjaltë!", "Honey!");
        public static string Blocked => T("Mburoja!", "Shielded!");
        public static string Overtake => T("Kalove!", "Overtake!");
        public static string Finish => T("FINISH!", "FINISH!");

        public static string Item(KartItem item) => item switch
        {
            KartItem.Rocket => T("Raketë", "Rocket"),
            KartItem.Paint => T("Bombë me bojë", "Paint bomb"),
            KartItem.Shield => T("Mburojë", "Shield"),
            KartItem.Honey => T("Mjaltë", "Honey"),
            _ => "",
        };

        /// <summary>"First place" ... (the place is masculine: "vendi"; no gender for the player).</summary>
        public static string PlaceName(int place) => place switch
        {
            1 => T("Vendi i parë!", "First place!"),
            2 => T("Vendi i dytë!", "Second place!"),
            3 => T("Vendi i tretë!", "Third place!"),
            4 => T("Vendi i katërt", "Fourth place"),
            5 => T("Vendi i pestë", "Fifth place"),
            _ => T("Vendi i gjashtë", "Sixth place"),
        };
        public static string PlaceShort => T("VENDI", "PLACE");

        // tracks
        public static string Track(int track) => track switch
        {
            0 => T("Bregdeti", "The Coast"),
            1 => T("Fshati", "The Village"),
            _ => T("Lugina e Dinozaurëve", "Dino Valley"),
        };
        static string TrackShort(int track) => track switch
        {
            0 => T("BREGDETI", "COAST"),
            1 => T("FSHATI", "VILLAGE"),
            2 => T("DINOZAURËT", "DINOS"),
            _ => T("RASTËSI", "RANDOM"),
        };

        // the scoreboard and the results
        public static string You => T("Ti", "You");
        public static string Us => T("Ju", "You");
        public static string Player(int slot) => T($"Lojtari {slot + 1}", $"Player {slot + 1}");
        public static string Results => T("Rezultatet", "Results");
        public static string Time => T("Koha", "Time");
        public static string Points => T("Pikë", "Points");
        public static string Stars => T("Yje", "Stars");
        public static string Tricks => T("Salto", "Tricks");
        public static string Won(int slot) => T($"Fitoi {Player(slot)}!", $"{Player(slot)} wins!");
        public static string YouWon => T("Fitove garën!", "You won the race!");
        public static string WeWon => T("Fituat garën!", "You won the race!");
        public static string Finished(int place, bool two) => place == 1 ? (two ? WeWon : YouWon) : PlaceName(place);

        // the cup
        public static string Cup => T("Kupa", "The Cup");
        public static string CupOf(KartDifficulty d) => T($"Kupa · {Difficulty(d)}", $"Cup · {Difficulty(d)}");
        public static string RaceOfCup(int race) => T($"Gara {race + 1} nga 3", $"Race {race + 1} of 3");
        public static string Standings => T("Renditja", "Standings");
        public static string NextRace(int track) => T($"Gara tjetër: {Track(track)}", $"Next race: {Track(track)}");
        public static string RaiseForNext(bool two) => two ? T("Ngrini duart të dy për garën tjetër", "Both raise your hands for the next race")
                                                           : T("Ngri duart lart për garën tjetër", "Raise both hands for the next race");
        public static string WonTheCup(bool two) => two ? T("Fituat kupën!", "You won the cup!") : T("Fitove kupën!", "You won the cup!");
        public static string CupPlace(int place, bool two) => place switch
        {
            1 => WonTheCup(two),
            2 => two ? T("Dolët të dytët në kupë!", "Second in the cup!") : T("Dole i dyti në kupë!", "Second in the cup!"),
            3 => two ? T("Dolët të tretët në kupë!", "Third in the cup!") : T("Dole i treti në kupë!", "Third in the cup!"),
            _ => T($"Vendi i {place}-t në kupë", $"Place {place} in the cup"),
        };
        public static string CupWinner(string name) => T($"Fitues i kupës: {name}", $"Cup winner: {name}");
        public static string Cups => T("Kupa", "Cups");
        public static string AgainOrMenu(bool two) => two ? T("Ngrini duart për të luajtur sërish · shigjetat: menuja", "Both raise your hands to play again · arrows: the menu")
                                                          : T("Ngri duart për të luajtur sërish · shigjetat: menuja", "Raise both hands to play again · arrows: the menu");

        // two players
        public static string SoloMode => T("1 LOJTAR", "1 PLAYER");
        public static string VersusMode => T("KUNDËR", "VERSUS");
        public static string TogetherMode => T("BASHKË", "TOGETHER");
        public static string VersusModeSub => T("2 lojtarë, një makinë secili, njëri kundër tjetrit", "2 players, a kart each, against each other");
        public static string TogetherModeSub => T("2 lojtarë në një makinë: e drejtoni bashkë", "2 players in one kart: you steer it together");
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
        public static string TrackRow => T("Pista", "Track");
        public static string ViewRow => T("Pamja", "View");
        public static string SingleRace => T("GARË", "RACE");
        public static string CupMode => T("KUPË", "CUP");
        public static string Difficulty(KartDifficulty d) => d switch
        {
            KartDifficulty.Easy => T("LEHTË", "EASY"),
            KartDifficulty.Medium => T("MESATARE", "MEDIUM"),
            _ => T("VËSHTIRË", "HARD"),
        };
        public static string TrackOption(int track) => TrackShort(track);
        public static string View(int view) => view == 0 ? T("PRAPA", "BEHIND") : T("LART", "HIGH");
        public static string MenuHint => T("↑ ↓ zgjidh    ← → ndrysho", "↑ ↓ choose    ← → change");
        public static string AllThree => T("Kupa: të tria pistat", "The cup: all three tracks");
    }
}
