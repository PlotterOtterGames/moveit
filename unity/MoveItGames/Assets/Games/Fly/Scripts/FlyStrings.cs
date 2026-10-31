using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Fly
{
    /// <summary>Fluturo!-only UI text (shared text lives in <see cref="CommonStrings"/>). The Albanian needs the user's review.</summary>
    public static class FlyStrings
    {
        public static string Title => T("Fluturo!", "Fly!");
        public static string HowTo => T("Fluturo nëpër qytet dhe godit robotët me grusht: sa më larg, aq më mirë!",
                                        "Fly through the city and punch the robots: as far as you can!");
        public static string KeyboardHelp => T("shigjetat = fluturo, hapësira = grusht", "arrows = fly, space = punch");
        public static string JumpHint => T("Kërce për t'u ngritur lart", "Jump to fly up");
        public static string TurnHowTo => T("Në kryqëzim, hap anash për t'u kthyer në rrugën tjetër", "At a crossroads, step aside to turn into the next street");
        public static string TurnHint => T("Kthehu majtas ose djathtas!", "Turn left or right!");
        public static string District(int n) => T($"Lagjja {n}", $"District {n}");
        public static string SaveTheCity => T("Shpëto qytetin nga robotët!", "Save the city from the robots!");
        public static string Faster => T("Më shpejt, më shumë robotë!", "Faster, more robots!");
        public static string Stop => T("Ndal! Të rrethuan!", "Stop! You're surrounded!");
        public static string StopHow => T("Godit robotët që vijnë nga të gjitha anët!", "Punch the robots coming from every side!");
        public static string RushWon => T("Bravo!", "Well done!");
        public static string Clash => T("Grusht më grusht!", "Fist to fist!");
        public static string ClashHow => T("Godit sa më shpejt! Kush godet më shpejt, fiton!", "Punch as fast as you can! The faster puncher wins!");
        public static string ClashGo => T("GODIT!", "PUNCH!");
        public static string Flurry => T("Më shpejt!", "Faster!");
        public static string KnockOut => T("NOKAUT!", "KNOCKOUT!");
        public static string ClashLost => T("Ai ishte më i shpejtë! Edhe një herë!", "It was faster! Again!");
        public static string DistrictSaved(int n) => T($"Lagjja {n} u shpëtua!", $"District {n} saved!");
        public static string NextDistrict => T("Fluturo te lagjja tjetër!", "Fly on to the next district!");
        public static string Combo(int n) => T($"Kombo x{n}!", $"Combo x{n}!");
        public static string BestCombo => T("Kombo", "Combo");
        public static string PunchIt => T("Godite!", "Punch it!");
        public static string Dodge => T("Shmangu!", "Dodge!");
        public static string PunchSparks => T("Ktheja shkëndijën me grusht!", "Punch the spark back!");
        public static string BossComing => T("Kujdes! Roboti gjigant!", "Watch out! The giant robot!");
        public static string PunchFireballs => T("Kthe topat e zjarrit me grusht!", "Punch the fireballs back!");
        public static string FinalClash => T("Përballja e fundit!", "The final clash!");
        public static string Swarm => T("Tufë dronësh!", "Drone swarm!");
        public static string FullSwarm => T("E gjithë tufa!", "The whole swarm!");
        public static string NewRecord => T("REKORD I RI!", "NEW RECORD!");
        public static string Smash => T("Bam!", "Pow!");
        public static string Ouch => T("Oj!", "Ouch!");
        public static string Back => T("Mbrapsht!", "Right back!");
        public static string HeartBack => T("+1 zemër", "+1 heart");
        public static string Robots => T("Robotë", "Robots");
        public static string Stars => T("Yje", "Stars");
        public static string Boss => T("Roboti gjigant", "Giant robot");
        public static string Champion => T("Kampioni", "The champion");
        public static string You => T("Ti", "You");
        public static string Reached(int n) => T($"Arrite në lagjen {n}", $"You reached district {n}");

        // ---- new enemies: a card the first time a run meets one
        public static string EnemyName(EnemyKind kind) => kind switch
        {
            EnemyKind.Drone => T("Dronët", "Drones"),
            EnemyKind.Armoured => T("Roboti me helmetë", "The helmet robot"),
            EnemyKind.Thrower => T("Hedhësi portokalli", "The orange thrower"),
            EnemyKind.Dart => T("Roboti i shpejtë", "The quick robot"),
            EnemyKind.Champion => T("Kampioni", "The champion"),
            EnemyKind.Boss => T("Roboti gjigant", "The giant robot"),
            _ => T("Robotët", "Robots"),
        };
        public static string EnemyHow(EnemyKind kind) => kind switch
        {
            EnemyKind.Drone => T("Godit dronët kur fluturon: çdo dron jep një yll", "Punch drones as you fly: each one drops a star"),
            EnemyKind.Armoured => T("Do dy grushta!", "Takes two punches!"),
            EnemyKind.Thrower => T("Ktheja shkëndijat me grusht", "Punch its sparks back at it"),
            EnemyKind.Dart => T("Vjen dy herë më shpejt!", "Comes twice as fast!"),
            EnemyKind.Champion => T("Godit më shpejt se ai!", "Punch faster than it does!"),
            EnemyKind.Boss => T("Ktheji topat e zjarrit, pastaj përballu me të!", "Punch its fireballs back, then face it!"),
            _ => T("Godit kë të vjen afër", "Punch whoever comes close"),
        };

        // ---- progress between runs
        public static string Missions => T("Misionet", "Missions");
        public static string MissionDone => T("Mision i kryer!", "Mission done!");
        public static string MissionText(Mission m) => m.Kind switch
        {
            MissionKind.SmashRobots => T($"Shkatërro {m.Target} robotë", $"Smash {m.Target} robots"),
            MissionKind.SmashDrones => T($"Godit {m.Target} dronë", $"Punch {m.Target} drones"),
            MissionKind.WinClashes => m.Target == 1 ? T("Fito një përballje", "Win a clash") : T($"Fito {m.Target} përballje", $"Win {m.Target} clashes"),
            MissionKind.ReachDistrict => T($"Arri në lagjen {m.Target}", $"Reach district {m.Target}"),
            MissionKind.CollectStars => T($"Mblidh {m.Target} yje", $"Collect {m.Target} stars"),
            MissionKind.Combo => T($"Bëj kombo x{m.Target}", $"Make a x{m.Target} combo"),
            MissionKind.QuickClash => T($"Fito një përballje për {m.Target} sekonda", $"Win a clash in {m.Target} seconds"),
            MissionKind.BeatBoss => m.Target == 1 ? T("Mund robotin gjigant", "Beat the giant robot") : T($"Mund robotin gjigant {m.Target} herë", $"Beat the giant robot {m.Target} times"),
            MissionKind.FullSwarm => m.Target == 1 ? T("Godit një tufë të tërë dronësh", "Punch a whole drone swarm") : T($"Godit {m.Target} tufa të tëra dronësh", $"Punch {m.Target} whole drone swarms"),
            MissionKind.ReturnSparks => T($"Kthe {m.Target} shkëndija ose topa zjarri", $"Punch back {m.Target} sparks or fireballs"),
            MissionKind.CleanRush => m.Target == 1 ? T("Kalo një sulm pa humbur zemër", "Beat a rush without losing a heart") : T($"Kalo {m.Target} sulme pa humbur zemër", $"Beat {m.Target} rushes without losing a heart"),
            _ => "",
        };
        public static string InOneRun => T("(në një lojë)", "(in one game)");
        public static string StarsTotal(int n) => T($"★ {n} yje", $"★ {n} stars");
        public static string NextOutfit(int have, int need) => T($"Veshja tjetër: {have} / {need} ★", $"Next outfit: {have} / {need} ★");
        public static string AllOutfits => T("Të gjitha veshjet i ke!", "You have every outfit!");
        public static string Outfit(int i, int of) => T($"Veshja: {OutfitName(i)} ({i + 1}/{of})   ← →", $"Outfit: {OutfitName(i)} ({i + 1}/{of})   ← →");
        public static string NewOutfit(int i) => T($"Veshje e re: {OutfitName(i)}!", $"New outfit: {OutfitName(i)}!");
        public static string OutfitName(int i) => i switch
        {
            1 => T("Qielli", "Sky"),
            2 => T("Gjethja", "Leaf"),
            3 => T("Dielli", "Sun"),
            4 => T("Zjarri", "Fire"),
            5 => T("Nata", "Night"),
            6 => T("Rozë", "Pink"),
            7 => T("Ylberi", "Rainbow"),
            _ => T("Klasike", "Classic"),
        };
        public static string StarsEarned(int run, int missions) => missions > 0
            ? T($"+{run} ★  +{missions} ★ nga misionet", $"+{run} ★  +{missions} ★ from missions")
            : T($"+{run} ★", $"+{run} ★");
        public static string BestDistrict(int n) => T($"Më larg: lagjja {n}", $"Furthest: district {n}");
    }
}
