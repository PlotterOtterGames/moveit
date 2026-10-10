using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Paint
{
    /// <summary>Pikturo!-only UI text (shared text lives in <see cref="CommonStrings"/>). Albanian to be reviewed by a native speaker.</summary>
    public static class PaintStrings
    {
        public static string Title => T("Pikturo!", "Paint!");
        public static string CameraTitle => T("Pikturo mbi veten!", "Paint on yourself!");
        static string Side(bool right) => right ? "djathtë" : "majtë";
        static string SideEn(bool right) => right ? "right" : "left";
        public static string PaintWith(bool right) => T($"Pikturo me dorën e {Side(right)}", $"Paint with your {SideEn(right)} hand");
        public static string SwitchHowTo(bool right) =>
            T($"Ngri dorën e {Side(!right)} te gjoksi: pikturon. Ule: ndalon.", $"Raise your {SideEn(!right)} hand to your chest to paint, lower it to stop.");
        public static string RaiseToPaint(bool right) => T($"Ngri dorën e {Side(!right)} për të pikturuar", $"Raise your {SideEn(!right)} hand to paint");
        public static string HandButton(bool right) => T(right ? "Dora: D" : "Dora: M", right ? "Hand: R" : "Hand: L");
        public static string HandSwapped(bool right) => T($"Tani pikturon dora e {Side(right)}", $"Now your {SideEn(right)} hand paints");
        public static string HowTo3 => T("Ngjyrat djathtas, penelat majtas: mbaje dorën mbi to", "Colours on the right, brushes on the left: hold your hand on one");
        public static string CameraHowTo => T("Vizato një fustan, një kurorë, krahë fluture… mbi veten!", "Draw a dress, a crown, butterfly wings… on yourself!");
        public static string MouseHelp => T("Kamera nuk është aktive: pikturo me mausin", "Camera service not running: paint with the mouse");
        public static string WaitingCamera => T("Duke pritur figurën e kamerës…", "Waiting for the camera picture…");
        public static string StepIn => T("Dil para kamerës", "Step in front of the camera");
        public static string PhotoSaved(int n) => T($"Fotoja u ruajt! ({n} në galeri)", $"Photo saved! ({n} in the gallery)");
        public static string Cleared => T("Letër e re! (Kthe = e rikthen)", "Fresh page! (Undo brings it back)");
        public static string NothingToUndo => T("Nuk ka më për të kthyer", "Nothing more to undo");

        public static string Tool(PaintTool tool) => tool switch
        {
            PaintTool.Marker => T("Penel", "Brush"),
            PaintTool.Rainbow => T("Ylber", "Rainbow"),
            PaintTool.Spray => T("Spërkatëse", "Spray"),
            PaintTool.Neon => T("Neon", "Neon"),
            PaintTool.Glitter => T("Shkëlqim", "Glitter"),
            PaintTool.Stamp => T("Vula", "Stamps"),
            PaintTool.Eraser => T("Gomë", "Eraser"),
            _ => T("Kovë", "Fill"),
        };

        public static string Undo => T("Kthe", "Undo");
        public static string Size => T("Madhësia", "Size");
        public static string Symmetry => T("Simetri", "Mirror");
        public static string Page => T("Faqe", "Pages");
        public static string Clear => T("Fshi", "Clear");
        public static string CameraMode => T("Kamera", "Camera");
        public static string PaperMode => T("Letër", "Paper");
        public static string Photo => T("Foto", "Photo");
        public static string KeyboardHelp => T("Z kthe · C fshi · M simetri · P faqe · H dora · K kamera · F foto · ←→ ngjyra · ↑↓ penela",
                                                "Z undo · C clear · M mirror · P page · H hand · K camera · F photo · ←→ colour · ↑↓ brush");
    }
}
