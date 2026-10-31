using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Sports
{
    /// <summary>Sporte!'s text, Albanian first (the user reviews it), English for development.</summary>
    public static class SportsStrings
    {
        public static string Title => T("Sporte!", "Sports!");
        public static string Choose => T("Zgjidh sportin", "Choose a sport");
        public static string Place(int n) => T($"Vendi {n}", $"Place {n}");
        public static string You => T("Ti", "You");
        public static string Seconds(float s) => T($"{s:0.00} s", $"{s:0.00} s");
        public static string NewRecord => T("Rekord i ri!", "New record!");

        // ---- 100 metra
        public static string SprintTitle => T("100 metra", "100 Metres");
        public static string SprintHowTo => T("Rri pa lëvizur te nisja. Pas krismës vrapo në vend sa më shpejt: gjunjët lart, krahët tundi! Në fund përkulu përpara.",
                                              "Keep still at the start. At the gun, run on the spot as fast as you can: knees up, pump your arms! At the end, lean forward.");
        public static string RunKeys => T("shtyp ← → me radhë për të vrapuar,  ↓ përkulu", "press ← → in turn to run,  ↓ lean");
        public static string Lean => T("PËRKULU!", "LEAN!");
        public static string Go => T("VRAPO!", "GO!");
        public static string Reaction(float s) => T($"Reagimi {s:0.00} s", $"Reaction {s:0.00} s");
        public static string Speed => T("SHPEJTËSIA", "SPEED");
        public static string PhotoFinish => T("FOTO-FINISH", "PHOTO FINISH");
        public static string Lane(int n) => T($"Korsia {n}", $"Lane {n}");
        public static string PersonalBest => T("REKORDI YT", "YOUR BEST");
        public static string OnYourMarks => T("NË VEND…", "ON YOUR MARKS…");
        public static string SetReady => T("GATI…", "SET…");
        public static string FalseStart => T("NISJE E PARAKOHSHME!", "FALSE START!");
        public static string RaceResult(float time, int place) => T($"Koha: {time:0.00} s    Vendi {place}", $"Time: {time:0.00} s    Place {place}");
        public static string RunFaster => T("Vrapo më shpejt!", "Run faster!");


        // ---- Futboll
        public static string FootballTitle => T("Futboll", "Football");
        public static string FootballHowTo => T("Godit topin me këmbë kur të vjen, ose kur të pret para teje. Pastaj je portier: ndalo topin me duar dhe me trup!",
                                                "Kick the ball as it comes to you, or when it waits in front of you. Then you're in goal: stop the ball with your hands and body!");
        public static string FootballKeys => T("← → drejto,  ↑ ngrije,  hapësira godit,  si portier ← → lëviz", "← → aim,  ↑ lift,  space kick,  in goal ← → move");
        public static string Us => T("TI", "YOU");
        public static string Them => T("YJET", "STARS");
        public static string Round(int n, int of) => T($"Raundi {n}/{of}", $"Round {n}/{of}");
        public static string Attack => T("SULMO!", "ATTACK!");
        public static string AttackSub => T("Godit kur të vjen pasimi!", "Kick as the pass arrives!");
        public static string FreeKick => T("GODITJE E LIRË!", "FREE KICK!");
        public static string FreeKickSub => T("Ngrije topin mbi mur!", "Lift it over the wall!");
        public static string Penalty => T("PENALLTI!", "PENALTY!");
        public static string PenaltySub => T("Vetëm ti dhe portieri.", "Just you and the keeper.");
        public static string Defend => T("MBRO PORTËN!", "SAVE IT!");
        public static string DefendSub => T("Ndalo topin me duar ose me trup!", "Stop it with your hands or body!");
        public static string PenaltyAgainst => T("Penallti kundër nesh!", "A penalty against us!");
        public static string Missed => T("E humbe topin!", "You missed it!");
        public static string Post => T("SHTYLLA!", "THE POST!");
        public static string Wall => T("MURI!", "THE WALL!");
        public static string TheyScored => T("GOL I TYRE", "THEY SCORED");
        public static string MatchWin => T("FITORE!", "YOU WIN!");
        public static string MatchDraw => T("BARAZIM", "A DRAW");
        public static string MatchLose => T("HUMBJE", "YOU LOST");
        public static string KickReady => T("Godit topin!", "Kick the ball!");
        public static string FootballResult(int goals, int saves) => T($"Gola: {goals}    Pritje: {saves}", $"Goals: {goals}    Saves: {saves}");
        public static string Goal => T("GOL!", "GOAL!");
        public static string Saved => T("PRITJE!", "SAVE!");
        public static string KeeperSaved => T("E priti portieri!", "The keeper saved it!");
        public static string Wide => T("JASHTË!", "WIDE!");
        public static string KickNow => T("GODIT!", "KICK!");
        public static string Score(int us, int them) => T($"Ti {us} - {them} Ata", $"You {us} - {them} Them");

        // ---- Volejboll
        public static string VolleyTitle => T("Volejboll", "Volleyball");
        public static string VolleyHowTo => T("Kur vjen topi te ti, ngri krahët shpejt për ta kthyer. Kur shoku ta ngre, kërce ose godit poshtë fort!",
                                              "When the ball comes to you, swing your arms up to bump it. When your partner sets it up, jump or hit down hard!");
        public static string VolleyKeys => T("hapësira pret / godet", "space bump / spike");
        public static string VolleyResult(int us, int them) => T($"Rezultati: {us} - {them}", $"Score: {us} - {them}");
        public static string Serve => T("SHËRBE!", "SERVE!");
        public static string Bump => T("PRITE!", "BUMP!");
        public static string Spike => T("GODIT!", "SPIKE!");
        public static string PerfectSpike => T("GODITJE E PËRSOSUR!", "PERFECT SPIKE!");
        public static string YourServe => T("SHËRBIMI YT", "YOUR SERVE");
        public static string FirstTo(int n) => T($"Kush shënon {n} fiton", $"First to {n}");
        public static string PointUs => T("PIKË!", "POINT!");
        public static string PointThem => T("PIKË PËR TA", "THEIR POINT");
        public static string MatchWon => T("FITORE!", "YOU WIN!");
        public static string MatchLost => T("HUMBJE", "YOU LOST");
    }
}
