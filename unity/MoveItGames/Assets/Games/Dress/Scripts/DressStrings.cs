using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Dress
{
    /// <summary>Vishu!'s text, Albanian first. New strings: the user (a native speaker) reviews them.</summary>
    public static class DressStrings
    {
        public static string Title => T("Vishu!", "Dress Up!");
        public static string Tagline => T("Vishu me rroba magjike, pastaj dil në sfilatë!", "Dress up in magic clothes, then walk the runway!");
        public static string HowTo => T("Mbaj dorën mbi një flluskë për ta zgjedhur", "Hold your hand on a bubble to choose it");
        public static string HowToColor => T("Zgjidh të njëjtën rrobë sërish dhe ndërron ngjyrë", "Choose the same thing again to change its colour");
        public static string WaitingCamera => T("Po hapet kamera…", "Starting the camera…");
        public static string NoPlayer => StepIn;

        public static string Magic => T("Magji", "Magic");
        public static string Surprise => T("Surprizë", "Surprise");
        public static string Show => T("Sfilatë", "Show");
        public static string Ready => T("Gati!", "Ready!");
        public static string Photo => T("Foto", "Photo");
        public static string RemoveAll => T("Hiq", "Clear");
        public static string Nothing => T("Asgjë", "Nothing");
        public static string AllRemoved => T("U hoqën të gjitha", "Everything off");
        public static string PhotoSaved(int count) => T($"Foto u ruajt! ({count} foto)", $"Photo saved! ({count} photos)");
        public static string NewColor(string item) => T($"{item}: ngjyrë e re!", $"{item}: a new colour!");
        public static string MagicOn(string effect) => T($"Magji: {effect}", $"Magic: {effect}");
        public static string SurpriseLook(string theme) => T($"Surprizë: {theme}!", $"Surprise: {theme}!");

        // ---- the fashion show
        public static string ShowTitle => T("Sfilatë!", "Fashion show!");
        public static string ThemeIs(string theme) => T($"Tema: {theme}", $"Theme: {theme}");
        public static string DressFor(int seconds) => T($"Vishu për temën! Ke {seconds} sekonda", $"Dress for the theme! You have {seconds} seconds");
        public static string ReadyHint => T("Kur të jesh gati, zgjidh \"Gati!\" lart", "When you are ready, choose \"Ready!\" at the top");
        public static string OnStage => T("Në skenë!", "On stage!");
        public static string StrikePose => T("Bëj këtë pozë!", "Strike this pose!");
        public static string Beautiful => T("Bukur!", "Beautiful!");
        public static string Wonderful => T("Shkëlqyeshëm!", "Wonderful!");
        public static string NeverMind => T("S'ka gjë!", "Never mind!");
        public static string JudgesSay => T("Gjyqtarët thonë…", "The judges say…");
        public static string Total(int stars) => T($"Gjithsej {stars} yje!", $"{stars} stars in total!");
        public static string StarsTo(int stars, string finish) => T($"Edhe {stars} yje dhe hapet: {finish}", $"{stars} more stars to unlock: {finish}");
        public static string AllFinishes => T("I hape të gjitha rrobat magjike!", "You unlocked every magic fabric!");
        public static string Unlocked(string finish) => T($"U hap: {finish}!", $"Unlocked: {finish}!");
        public static string UnlockedHint => T("Zgjidh një rrobë disa herë derisa të shfaqet", "Choose a piece a few times until it appears");
        public static string AnotherShow => T("Edhe një sfilatë", "Another show");
        public static string BackToMirror => T("Te pasqyra", "Back to the mirror");
        public static string Seconds(int s) => T($"{s} s", $"{s} s");

        public static string PoseName(ShowPose pose) => pose switch
        {
            ShowPose.Star => T("Yll!", "Star!"),
            ShowPose.Hips => T("Duart në mes!", "Hands on hips!"),
            ShowPose.Heart => T("Zemër lart!", "Heart up high!"),
            ShowPose.Wave => T("Përshëndet!", "Wave!"),
            ShowPose.Super => T("Superheroinë!", "Superhero!"),
            _ => T("Aeroplan!", "Airplane!"),
        };

        public static string JudgeName(Judge judge) => judge switch
        {
            Judge.Cat => "Mimi",
            Judge.Bunny => "Lumi",
            _ => "Dino",
        };

        public static string RemarkText(Remark remark, Theme theme) => remark switch
        {
            Remark.Theme => theme switch
            {
                Theme.Princess => T("Si një princeshë e vërtetë!", "A real princess!"),
                Theme.Fairy => T("Si një zanë e vërtetë!", "A real fairy!"),
                Theme.Mermaid => T("Si një sirenë e vërtetë!", "A real mermaid!"),
                Theme.Hero => T("Një superheroinë e vërtetë!", "A real superhero!"),
                Theme.Summer => T("Gati për pushimet e verës!", "Ready for the summer holidays!"),
                Theme.Winter => T("Mbretëresha e dimrit!", "The queen of winter!"),
                _ => T("Ylli i festës!", "The star of the party!"),
            },
            Remark.ThemeMore => T("Vish më shumë gjëra për temën!", "Wear more things for the theme!"),
            Remark.Colors => T("Ngjyrat shkojnë shumë bukur!", "The colours go so well!"),
            Remark.ColorsMore => T("Provo ngjyra të tjera!", "Try other colours!"),
            Remark.Complete => T("Asgjë nuk mungon!", "Nothing is missing!"),
            Remark.AddMore => T("Shto edhe krahë a një kurorë!", "Add wings or a crown too!"),
            Remark.Poses => T("Çfarë pozash!", "What poses!"),
            Remark.PosesMore => T("Më shumë poza herën tjetër!", "More poses next time!"),
            _ => T("Sa magjike!", "So magical!"),
        };

        public static string ItemName(DressItem item) => T(item.NameSq, item.NameEn);

        public static string SlotName(Slot slot) => slot switch
        {
            Slot.Top => T("Bluza", "Tops"),
            Slot.Bottom => T("Funde", "Skirts"),
            Slot.Hair => T("Flokë", "Hair"),
            Slot.Head => T("Kurora", "Headwear"),
            Slot.Face => T("Syze", "Face"),
            Slot.Back => T("Krahë", "Wings"),
            _ => T("Në dorë", "In hand"),
        };

        public static string ThemeName(Theme theme) => theme switch
        {
            Theme.Princess => T("Princeshë", "Princess"),
            Theme.Fairy => T("Zanë", "Fairy"),
            Theme.Mermaid => T("Sirenë", "Mermaid"),
            Theme.Hero => T("Superheroinë", "Superhero"),
            Theme.Summer => T("Verë", "Summer"),
            Theme.Winter => T("Dimër", "Winter"),
            _ => T("Festë", "Party"),
        };

        public static string EffectName(Effect effect) => effect switch
        {
            Effect.Sparkles => T("Xixëllima", "Sparkles"),
            Effect.Hearts => T("Zemra", "Hearts"),
            Effect.Butterflies => T("Flutura", "Butterflies"),
            Effect.Bubbles => T("Flluska", "Bubbles"),
            Effect.Snow => T("Borë", "Snow"),
            Effect.Stars => T("Yje", "Stars"),
            _ => T("Pa magji", "No magic"),
        };

        public static string FinishName(Finish finish) => finish switch
        {
            Finish.Rainbow => T("Ylber", "Rainbow"),
            Finish.Glitter => T("Xixa", "Glitter"),
            Finish.Gold => T("Ar", "Gold"),
            Finish.Galaxy => T("Galaktikë", "Galaxy"),
            _ => "",
        };
    }
}
