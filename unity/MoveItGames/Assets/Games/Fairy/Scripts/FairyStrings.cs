using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Fairy
{
    /// <summary>Zana!'s text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's review.</summary>
    public static class FairyStrings
    {
        public static string Title => T("Zana!", "Fairies!");
        public static string Goal => T("Mbro pemën e zanave nga hijet!", "Protect the fairy tree from the shadows!");

        // how to play
        public static string HowWand => T("Ngri dorën: ajo është shkopi magjik", "Raise a hand: it is the magic wand");
        public static string HowDraw => T("Vizato magjinë në ajër: pastron hijet", "Draw the magic in the air: it cleans the shadows");
        public static string HowArms => T("Pas 5 valësh: duart lart", "After 5 waves: both arms up");
        public static string DrawThis(Spell s) => T($"Vizato: {SpellName(s)}!", $"Draw: {SpellName(s)}!");
        public static string TryAgain => T("Provo sërish!", "Try again!");
        public static string Wait => T("Prit, shkopi po mbushet!", "Wait, the wand is charging!");
        public static string CrystalNotFull => T("Kristali s'është plot!", "The crystal isn't full yet!");
        public static string ComesBack(Spell s, int seconds) => T($"{SpellName(s)}: pas {seconds} s", $"{SpellName(s)}: in {seconds} s");
        public static string KeyboardHelp => T("Miu është shkopi magjik · Hapësira: transformimi", "The mouse is the magic wand · Space: the transformation");
        public static string StartHint => T("Ngri duart lart ose shtyp OK për të luajtur", "Raise your hands or press OK to play");

        // the spells
        public static string SpellName(Spell s) => s switch
        {
            Spell.Shield => T("Mburoja", "Shield"),
            Spell.Seeds => T("Farat", "Seeds"),
            Spell.Burst => T("Magjia e madhe", "Big magic"),
            Spell.Freeze => T("Ngrirja", "Freeze"),
            Spell.Lightning => T("Rrufeja", "Lightning"),
            Spell.Rainbow => T("Ylberi", "Rainbow"),
            _ => T("Shiu shërues", "Healing rain"),
        };

        // playing
        public static string Wave(int n) => T($"Vala {n}", $"Wave {n}");
        public static string DefendTree => T("Mbro pemën!", "Protect the tree!");
        public static string WaveSaved => T("Vala u mbrojt!", "Wave saved!");
        public static string BossComes => T("Mbreti i hijeve!", "The shadow king!");
        public static string MagicReady => T("Kristali plot: vizato yllin!", "The crystal is full: draw the star!");
        public static string BigMagic => T("MAGJIA E MADHE!", "THE BIG MAGIC!");
        public static string Frozen => T("Ngrirje!", "Freeze!");
        public static string Healed => T("Pema u shërua!", "The tree is healed!");
        public static string CatchOrb => T("Kape me shkop!", "Catch it with the wand!");
        public static string Orb(OrbKind k) => k switch
        {
            OrbKind.Heart => T("Zemër: pema +3!", "A heart: the tree +3!"),
            OrbKind.Star => T("Yll: kristali mbushet!", "A star: the crystal fills!"),
            _ => T("Farë: magjitë gati!", "A seed: the spells are ready!"),
        };
        public static string Transform => T("TRANSFORMIM!", "TRANSFORMATION!");
        public static string TransformHow => T("Ngri të dy duart lart!", "Raise both arms!");
        public static string Form(int level) => level switch
        {
            2 => T("Zana e Yllit", "Star Fairy"),
            3 => T("Zana e Ylberit", "Rainbow Fairy"),
            _ => T("Mbretëresha e Zanave", "Fairy Queen"),
        };
        public static string FormPower(int level) => level switch
        {
            2 => T("Magjitë godasin dyfish!", "The spells strike twice as hard!"),
            3 => T("Mburoja dhe farat vijnë më shpejt!", "The shield and the seeds come back sooner!"),
            _ => T("Kristali mbushet më shpejt!", "The crystal fills sooner!"),
        };
        public static string Stardust => T("Pluhur ylli", "Stardust");

        // the end of a run
        public static string GardenFell => T("Hijet e mbuluan pemën…", "The shadows covered the tree…");
        public static string WavesSaved(int n) => T(n == 1 ? "Mbrojte 1 valë" : $"Mbrojte {n} vala", n == 1 ? "You saved 1 wave" : $"You saved {n} waves");
        public static string Dust(int n) => T($"+{n} pluhur ylli", $"+{n} stardust");
        public static string NewBest => T("REKORD I RI!", "NEW BEST!");
        public static string Best(int n) => T($"Më e mira: vala {n}", $"Best: wave {n}");
        public static string NewWings => T("Krahë të rinj!", "New wings!");
        public static string NewDress => T("Fustan i ri!", "A new dress!");
        public static string NewSpell(Spell s) => T($"Magji e re: {SpellName(s)}!", $"A new spell: {SpellName(s)}!");
        public static string PlayAgain => T("Ngri duart: luaj sërish!", "Raise your hands: play again!");
        public static string GardenGrew(int n) => T($"Kopshti ka {n} lule", $"The garden has {n} flowers");

        // the menu
        public static string PlayersRow => T("Lojtarë", "Players");
        public static string DifficultyRow => T("Vështirësia", "Difficulty");
        public static string ElementRow => T("Elementi", "Element");
        public static string DressRow => T("Fustani", "Dress");
        public static string WingsRow => T("Krahët", "Wings");
        public static string SoloMode => T("1 ZANË", "1 FAIRY");
        public static string TogetherMode => T("2 ZANA", "2 FAIRIES");
        public static string Difficulty(FairyDifficulty d) => d switch
        {
            FairyDifficulty.Easy => T("LEHTË", "EASY"),
            FairyDifficulty.Hard => T("VËSHTIRË", "HARD"),
            _ => T("MESATARE", "MEDIUM"),
        };
        public static string ElementName(Element e) => e switch
        {
            Element.Fire => T("ZJARR", "FIRE"),
            Element.Water => T("UJË", "WATER"),
            Element.Nature => T("NATYRË", "NATURE"),
            _ => T("DRITË", "LIGHT"),
        };
        public static string DustLocked(int dust) => $"✦ {dust}";
        public static string MenuHint => T("↑ ↓ zgjidh    ← → ndrysho", "↑ ↓ choose    ← → change");
        public static string TimePlayed(float seconds)
        {
            int m = (int)(seconds / 60f), h = m / 60;
            m %= 60;
            return h > 0 ? T($"{h} orë e {m} minuta", $"{h} h {m} min") : T($"{m} minuta", $"{m} min");
        }

        // two players
        public static string Player(int slot) => T($"Zana {slot + 1}", $"Fairy {slot + 1}");
        public static string StandStill => T("Qëndro pa lëvizur…", "Hold still…");
        public static string RaiseYourHands => T("Ngri duart lart për t'u nisur!", "Raise both hands to start!");
        public static string Ready => T("Gati!", "Ready!");
        public static string StepApart => T("Largohuni pak nga njëra-tjetra", "Move a little apart");
        public static string WaitingFor(int slot) => T($"{Player(slot)}, ngri duart!", $"{Player(slot)}, raise your hands!");
        public static string PlayingAloneIn(int seconds) => T($"ose luan vetëm pas {seconds}…", $"or you play alone in {seconds}…");
        public static string ComeBackPlayer(int slot) => T($"{Player(slot)}, kthehu para kamerës!", $"{Player(slot)}, come back in front of the camera!");
        public static string TogetherSub => T("Dy zana mbrojnë pemën bashkë", "Two fairies defend the tree together");
    }
}
