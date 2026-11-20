using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Boxing
{
    /// <summary>Superboks!-only UI text (shared text lives in <see cref="CommonStrings"/>). All the Albanian needs the
    /// user's review (2026-11-18; the overhaul's new lines 2026-11-19).</summary>
    public static class BoxingStrings
    {
        public static string Title => T("Superboks!", "Superboks!");
        public static string Climb => T("Ngjitu deri te yjet!", "Climb to the stars!");
        public static string Hero => T("SUPERBOKS", "SUPERBOKS");

        // the waiting screen's menu
        public static string DifficultyRow => T("Vështirësia", "Difficulty");
        public static string FightRow => T("Ndeshja", "Fight");
        public static string OutfitRow => T("Veshja", "Outfit");
        public static string GlovesRow => T("Dorezat", "Gloves");
        public static string Difficulty(BoxingDifficulty d) => d switch
        {
            BoxingDifficulty.Easy => T("LEHTË", "EASY"),
            BoxingDifficulty.Medium => T("MESATARE", "MEDIUM"),
            _ => T("VËSHTIRË", "HARD"),
        };
        public static string FightOf(int n, int total) => T($"Ndeshja {n} / {total}", $"Fight {n} / {total}");
        public static string MenuHint => T("↑ ↓ zgjidh rreshtin · ← → ndrysho · dy duart lart ose OK: boks!",
                                           "↑ ↓ pick a row · ← → change · both hands up or OK: fight!");
        public static string Locked => T("Mund kundërshtarin e mëparshëm", "Beat the one before");
        public static string ChampionFight => T("Kampioni i skenës!", "The stage's champion!");

        // the four places of the climb
        public static string Stage(int stage) => stage switch
        {
            0 => T("Palestra", "The Gym"),
            1 => T("Çatia", "The Roof"),
            2 => T("Mali", "The Mountain"),
            _ => T("Yjet", "The Stars"),
        };
        public static string StageLine(int stage) => stage switch
        {
            0 => T("Ringu i lagjes, natën", "The neighbourhood ring at night"),
            1 => T("Mbi qytet, në perëndim", "Above the city at sunset"),
            2 => T("Majë e bjeshkëve, mbi re", "A peak above the clouds"),
            _ => T("Arena e Doktor Errësirës", "Doctor Darkness's arena"),
        };

        /// <summary>What beats each opponent (the map's card: the one thing it teaches).</summary>
        public static string Tip(string id) => id switch
        {
            "beni" => T("Grushto kur dorezat i ka poshtë", "Punch when its gloves are down"),
            "rina" => T("Dorezat lart? Godit në bark ose me aperkat", "Gloves up? Hit the belly, or uppercut"),
            "leka" => T("Grushti i Madh nuk bllokohet: anoju!", "The Haymaker can't be blocked: lean away!"),
            "robotik" => T("Lëviz gjithmonë njësoj: mësoje ritmin", "Always the same moves: learn its rhythm"),
            "magneta" => T("Bllokoji goditjet në bark, godit kokën", "Block its body blows, hit the head"),
            "robobos" => T("Grushti Raketë: anoju në çast", "Rocket Fist: lean at the last moment"),
            "ujku" => T("Grushta anash një pas një: blloko ose ulu", "Hooks one after another: block or duck"),
            "shqiponja" => T("Shmanget shpejt: prit grushtin e saj, pastaj godit", "Quick to dodge: wait for its punch, then hit"),
            "jeti" => T("Fshirja vjen në lartësinë e kokës: ulu!", "The Sweep comes at head height: duck!"),
            "gugu" => T("Ritmi i çuditshëm: shiko krahun, jo orën", "An odd rhythm: watch its arm, not the clock"),
            "hija" => T("Mëson grushtin tënd të preferuar: ndryshoji", "It learns your favourite punch: mix them up"),
            "erresira" => T("Topin e zjarrit kthehe me grusht!", "Punch its fireball back!"),
            _ => "",
        };

        // the moves (the map's little figures)
        public static string MovePunch => T("Grushto", "Punch");
        public static string MoveBlock => T("Blloko", "Block");
        public static string MoveDodge => T("Anoju", "Lean");
        public static string MoveDuck => T("Ulu", "Duck");
        public static string MoveSuper => T("Super!", "Super!");
        public static string KeyboardHelp => T("J K = grushta · U = aperkat · ↑ = blloko · ← → = anoju · ↓ = ulu · S = super",
                                               "J K = punches · U = uppercut · ↑ = block · ← → = lean · ↓ = duck · S = super");

        // the fight
        public static string Round(int n) => T($"RAUNDI {n}", $"ROUND {n}");
        public static string Fight => T("BOKS!", "FIGHT!");
        public static string Rest => T("Pushim", "Rest");
        public static string FinalRound => T("Raundi i fundit!", "Final round!");
        public static string Blocked => T("Bllok!", "Block!");
        public static string Dodged => T("Shmangie!", "Dodge!");
        public static string Counter => T("KUNDËR!", "COUNTER!");
        public static string CoverBroken => T("Mbrojtja u thye!", "Guard broken!");
        public static string Dizzy => T("I trullosur!", "Dizzy!");
        public static string GuardBroken => T("Mbrojtja nuk e mban!", "The guard can't stop it!");
        public static string Knockdown => T("RRËZIM!", "DOWN!");
        public static string GetUp => T("NGRIHU!", "GET UP!");
        public static string GetUpHow => T("Grushto shpejt: majtas, djathtas!", "Punch fast: left, right!");
        public static string PowerFull => T("Fuqia plot: ngri dy krahët!", "Power full: raise both arms!");
        public static string Super => T("GRUSHTI SUPER!", "SUPER PUNCH!");
        public static string Returned => T("E ktheve!", "Sent back!");
        public static string KO => T("NOKAUT!", "KNOCKOUT!");
        public static string TKO => T("NOKAUT TEKNIK!", "TECHNICAL KNOCKOUT!");
        public static string WonOnPoints => T("Fitore me pikë!", "Won on points!");
        public static string LostOnPoints => T("Humbe me pikë", "Lost on points");
        public static string HighGuardTip => T("Dorezat lart: godit në bark, ose me aperkat!", "Gloves up high: hit the belly, or uppercut!");
        public static string LowGuardTip => T("Dorezat poshtë: godit në kokë!", "Gloves down low: hit the head!");
        public static string CoverTip => T("U mbyll? Aperkat nga poshtë, mes dorezave!", "Covering up? Uppercut between the gloves!");

        /// <summary>A power punch's name, called out as it comes.</summary>
        public static string Special(Attack a) => a switch
        {
            Attack.Haymaker => T("Grushti i Madh!", "The Haymaker!"),
            Attack.Sweep => T("Fshirja!", "The Sweep!"),
            Attack.Rocket => T("Grushti Raketë!", "Rocket Fist!"),
            Attack.Lightning => T("Rrufeja!", "Lightning!"),
            Attack.Fireball => T("Topi i Zjarrit!", "Fireball!"),
            _ => "",
        };

        // the results
        public static string YouWon => T("Fitove!", "You won!");
        public static string YouLost => T("Humbe!", "You lost!");
        public static string TryAgain => T("Ngri duart: provo përsëri", "Raise your hands: try again");
        public static string NextFight => T("Ngri duart: ndeshja tjetër", "Raise your hands: next fight");
        public static string BackToMap => T("← →: harta", "← →: the map");
        public static string NewBelt => T("Rrip i ri!", "New belt!");
        public static string NewGloves => T("Doreza të reja!", "New gloves!");
        public static string NewOutfit => T("Veshje e re!", "New outfit!");
        public static string Champion => T("KAMPION I YJEVE!", "CHAMPION OF THE STARS!");
        public static string StarWin => T("Fitore", "Win");
        public static string StarNoDown => T("Pa rënë", "Never down");
        public static string StarKO => T("Nokaut i shpejtë", "Quick knockout");
        public static string Punches(int landed, int thrown) => T($"Grushta: {landed} nga {thrown}", $"Punches: {landed} of {thrown}");
        public static string Defences(int dodges, int blocks) => T($"Shmangie {dodges} · Blloqe {blocks}", $"Dodges {dodges} · Blocks {blocks}");
    }
}
