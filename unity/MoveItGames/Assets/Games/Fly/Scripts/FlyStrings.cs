using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Fly
{
    /// <summary>Fluturo!-only UI text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's review.</summary>
    public static class FlyStrings
    {
        public static string Title => T("Fluturo!", "Fly!");
        public static string HowTo => T("Hap anash dhe ulu për të fluturuar, godit robotët me grusht!",
                                        "Step aside and duck to fly, punch the robots!");
        public static string KeyboardHelp => T("shigjetat = fluturo, hapësira = grusht", "arrows = fly, space = punch");
        public static string JumpHint => T("Kërce për t'u ngritur lart", "Jump to fly up");
        public static string Level(int n) => T($"Niveli {n}", $"Level {n}");
        public static string SaveTheCity => T("Shpëto qytetin nga robotët!", "Save the city from the robots!");
        public static string PunchIt => T("Godite!", "Punch it!");
        public static string GetCloser => T("Afrohu robotit!", "Fly closer to the robot!");
        public static string Dodge => T("Shmangu!", "Dodge!");
        public static string BossComing => T("Kujdes! Roboti gjigant!", "Watch out! The giant robot!");
        public static string PunchFireballs => T("Kthe topat e zjarrit me grusht!", "Punch the fireballs back!");
        public static string LevelClear => T("Qyteti u shpëtua!", "The city is saved!");
        public static string NextLevel => T("Vjen niveli tjetër…", "Next level coming…");
        public static string YouWin => T("FITORE! Je hero!", "YOU WIN! You're a hero!");
        public static string Smash => T("Bam!", "Pow!");
        public static string Ouch => T("Oj!", "Ouch!");
        public static string Back => T("Mbrapsht!", "Right back!");
        public static string HeartBack => T("+1 zemër", "+1 heart");
        public static string Robots => T("Robotë", "Robots");
        public static string Stars => T("Yje", "Stars");
        public static string Levels => T("Nivele", "Levels");
        public static string Boss => T("Roboti gjigant", "Giant robot");
    }
}
