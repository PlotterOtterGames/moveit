using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Space
{
    /// <summary>Yjet!-only UI text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's
    /// review.</summary>
    public static class SpaceStrings
    {
        public static string Title => T("Yjet!", "Stars!");

        // how to play (the waiting screen)
        public static string Goal => T("Gjej të gjithë planetët e galaktikës!", "Find every planet in the galaxy!");
        public static string HowTurn => T("Anohu ose hap anash: kthehesh", "Lean or step aside: you turn");
        public static string HowFast => T("Të dy krahët lart: më shpejt", "Both arms up: faster");
        public static string HowSlow => T("Ulu: më ngadalë", "Duck: slower");
        public static string HowFind => T("Fluturo pranë një planeti: e zbulon", "Fly close to a planet: you discover it");
        public static string KeyboardHelp => T("← → kthehu · ↑ shpejt · ↓ ngadalë · M harta", "← → turn · ↑ fast · ↓ slow · M the map");

        // flying
        public static string Planets => T("Planetë", "Planets");
        public static string FoundOf(int found, int total) => $"{found} / {total}";
        public static string Here(int found, int count) => T($"{found} nga {count} këtu", $"{found} of {count} here");
        public static string Scanning => T("Duke e zbuluar…", "Discovering…");
        public static string NewPlanet => T("PLANET I RI!", "NEW PLANET!");
        public static string Rare => T("I rrallë!", "Rare!");
        public static string Unknown => T("I panjohur", "Unknown");
        public static string Metres(float m) => m >= 1000f ? $"{m / 1000f:0.0} km" : $"{m:0} m";
        public static string SystemDone(string name) => T($"Sistemi {name} u eksplorua!", $"The {name} system is explored!");
        public static string AllFoundHere => T("Gjete çdo planet këtu!", "You found every planet here!");
        public static string TurnBack => T("Fundi i sistemit: kthehemi!", "The edge of the system: turning back!");
        public static string BoostHint => T("Ngri krahët: më shpejt!", "Raise your arms: faster!");
        public static string TurnHint => T("Anohu për t'u kthyer", "Lean to turn");
        public static string NewSuit => T("Kostum i ri!", "A new suit!");
        public static string FollowArrow => T("Ndiq shigjetën te planeti tjetër", "Follow the arrow to the next planet");

        public static string TypeName(PlanetType t) => t switch
        {
            PlanetType.Home => T("Shtëpia jonë", "Our home"),
            PlanetType.Ocean => T("Botë oqeanesh", "Ocean world"),
            PlanetType.Continents => T("Botë me kontinente", "World of continents"),
            PlanetType.Desert => T("Botë shkretëtirë", "Desert world"),
            PlanetType.Ice => T("Botë akulli", "Ice world"),
            PlanetType.Lava => T("Botë llave", "Lava world"),
            PlanetType.Gas => T("Gjigant gazi", "Gas giant"),
            PlanetType.Jungle => T("Botë xhungle", "Jungle world"),
            PlanetType.Crystal => T("Botë kristalesh", "Crystal world"),
            PlanetType.Crater => T("Botë me kratere", "Crater world"),
            PlanetType.Toxic => T("Botë kënetash", "Swamp world"),
            PlanetType.Candy => T("Botë ëmbëlsirash", "Candy world"),
            _ => T("Planet ylber", "Rainbow planet"),
        };

        public static string StarName(StarKind k) => k switch
        {
            StarKind.Yellow => T("Yll i verdhë", "Yellow star"),
            StarKind.Orange => T("Yll portokalli", "Orange star"),
            StarKind.Red => T("Xhuxh i kuq", "Red dwarf"),
            StarKind.White => T("Yll i bardhë", "White star"),
            _ => T("Gjigant blu", "Blue giant"),
        };

        // the galaxy map
        public static string Map => T("Harta e galaktikës", "The galaxy map");
        public static string MapHintBody => T("Anohu: zgjidh një yll · Krahët lart: fluturo atje", "Lean: choose a star · Arms up: fly there");
        public static string MapHintRemote => T("← → zgjidh · OK fluturo · ↩ kthehu", "← → choose · OK fly · ↩ back");
        public static string FlyTo(string name) => T($"Për në {name}!", $"To {name}!");
        public static string System(string name) => T($"Sistemi {name}", $"The {name} system");
        public static string PlanetsIn(int found, int count) => T($"{found} nga {count} planetë", $"{found} of {count} planets");
        public static string NotVisited => T("Ende pa e vizituar", "Not visited yet");
        public static string YouAreHere => T("Je këtu", "You are here");
        public static string Explored => T("Eksploruar", "Explored");

        // the end
        public static string Finale => T("E eksplorove gjithë galaktikën!", "You explored the whole galaxy!");
        public static string FinaleSub(int planets, int systems) => T($"{planets} planetë në {systems} sisteme yjesh", $"{planets} planets in {systems} star systems");
        public static string TimePlayed(float seconds)
        {
            int m = (int)(seconds / 60f), h = m / 60;
            m %= 60;
            return h > 0 ? T($"{h} orë e {m} minuta", $"{h} h {m} min") : T($"{m} minuta", $"{m} min");
        }
        public static string FlyOn => T("Ngri duart: fluturo sërish!", "Raise your hands: fly on!");

        // the waiting screen
        public static string RaiseToFly => T("Ngri të dy duart për të fluturuar!", "Raise both hands to fly!");
        public static string Continue => T("Vazhdo udhëtimin", "Continue the trip");

        // two players
        public static string Player(int slot) => T($"Lojtari {slot + 1}", $"Player {slot + 1}");
        public static string SoloMode => T("1 LOJTAR", "1 PLAYER");
        public static string TogetherMode => T("2 BASHKË", "2 TOGETHER");
        public static string TogetherModeSub => T("Dy astronautë bashkë: anohuni të dy për t'u kthyer", "Two astronauts together: both lean to turn");
        public static string StandStill => T("Qëndro pa lëvizur…", "Hold still…");
        public static string RaiseYourHands => T("Ngri duart lart për t'u nisur!", "Raise both hands to take off!");
        public static string Ready => T("Gati!", "Ready!");
        public static string StepApart => T("Largohuni pak nga njëri-tjetri", "Move a little apart");
        public static string WaitingFor(int slot) => T($"{Player(slot)}, ngri duart!", $"{Player(slot)}, raise your hands!");
        public static string PlayingAloneIn(int seconds) => T($"ose luan vetëm pas {seconds}…", $"or you play alone in {seconds}…");
        public static string ComeBackPlayer(int slot) => T($"{Player(slot)}, kthehu para kamerës!", $"{Player(slot)}, come back in front of the camera!");

        // the menu
        public static string PlayersRow => T("Lojtarë", "Players");
        public static string SuitRow => T("Kostumi", "Suit");
        public static string SuitLocked(int planets) => T($"{planets} planetë", $"{planets} planets");
        public static string MenuHint => T("↑ ↓ zgjidh    ← → ndrysho", "↑ ↓ choose    ← → change");
    }
}
