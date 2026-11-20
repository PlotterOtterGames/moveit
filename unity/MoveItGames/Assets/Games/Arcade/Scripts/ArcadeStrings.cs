using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games.Arcade
{
    /// <summary>Lunapark!'s words, Albanian and English (2026-11-09: the user, a native speaker, should review the
    /// Albanian).</summary>
    public static class ArcadeStrings
    {
        public static string Title => "Lunapark!";
        public static string TileDescription => T("Gjashtë lojëra të shpejta: godit, prit, kap, fluturo... vetëm ose me një shok",
                                                  "Six quick games: whack, slice, catch, fly... alone or with a friend");

        public static string Name(Machine m) => m switch
        {
            Machine.Whack => T("Godit!", "Whack!"),
            Machine.Slice => T("Fruta!", "Fruit!"),
            Machine.Wall => T("Muri!", "The Wall!"),
            Machine.Catch => T("Kap!", "Catch!"),
            Machine.Flap => T("Zogu!", "Birdie!"),
            _ => T("Topi!", "Keep It Up!"),
        };

        public static string HowTo(Machine m) => m switch
        {
            Machine.Whack => T("Urithët dalin nga vrimat rreth teje: godit me dorë! Mos prek iriqët.",
                               "Moles pop out of the holes around you: bonk them with a hand! Don't touch the hedgehogs."),
            Machine.Slice => T("Prit frutat me një lëvizje të shpejtë dore. Mos prit bombat!",
                               "Slice the fruit with a quick move of your hand. Don't slice the bombs!"),
            Machine.Wall => T("Një mur vjen drejt teje: bëj formën e vrimës që të kalosh!",
                              "A wall is coming at you: make the hole's shape to pass through!"),
            Machine.Catch => T("Hap anash që shporta të kapë ëmbëlsirat. Mos kap bombat! Prek balonat me dorë.",
                               "Step side to side to catch the sweets in your basket. Don't catch the bombs! Touch the balloons."),
            Machine.Flap => T("Përplas krahët që zogu të fluturojë. Mblidh yjet, shmang retë!",
                              "Flap your arms to fly the bird. Collect the stars, dodge the clouds!"),
            _ => T("Mbaje topin në ajër me kokë, duar, gjunjë e këmbë!",
                   "Keep the ball in the air with your head, hands, knees and feet!"),
        };

        public static string Hint(Machine m) => m switch
        {
            Machine.Whack => T("Godit urithët!", "Bonk the moles!"),
            Machine.Slice => T("Prit frutat!", "Slice the fruit!"),
            Machine.Wall => T("Bëj formën e vrimës!", "Make the hole's shape!"),
            Machine.Catch => T("Hap anash!", "Step aside!"),
            Machine.Flap => T("Përplas krahët!", "Flap your arms!"),
            _ => T("Mos e lër të bjerë!", "Don't let it drop!"),
        };

        public static string KeyHelp(Machine m) => m switch
        {
            Machine.Whack => T("Tastet Q W E · A D · Z C godasin vrimat", "Keys Q W E · A D · Z C bonk the holes"),
            Machine.Slice => T("Hapësira pret frutën më të afërt", "Space slices the nearest fruit"),
            Machine.Wall => T("Hapësira bën formën", "Space makes the shape"),
            Machine.Catch => T("← → lëviz shportën", "← → move the basket"),
            Machine.Flap => T("Hapësira përplas krahët", "Space flaps"),
            _ => T("← → lëviz nën top", "← → move under the ball"),
        };

        public static string PlayersRow => T("Lojtarë", "Players");
        public static string GameRow => T("Loja", "Game");
        public static string OnePlayer => T("1 LOJTAR", "1 PLAYER");
        public static string TwoPlayers => T("2 LOJTARË", "2 PLAYERS");
        public static string OneGame => T("NJË LOJË", "ONE GAME");
        public static string Party => T("FESTË · 5 LOJËRA", "PARTY · 5 GAMES");
        public static string ChooseHint => T("Hap anash ose ← → për të zgjedhur · Ngri duart për të luajtur",
                                             "Step aside or ← → to choose · Raise your hands to play");
        public static string ChooseKeys => T("← → zgjidh · ↑ ↓ menyja · OK luaj", "← → choose · ↑ ↓ menu · OK play");
        public static string BothStepIn => T("Dy lojtarë: dilni të dy para kamerës", "Two players: both step in front of the camera");
        public static string BothRaise => T("Ngrini duart të dy për të filluar", "Both raise your hands to start");
        public static string StandApart => T("Largohuni pak nga njëri-tjetri", "Stand a little further apart");
        public static string Player(int p) => T($"Lojtari {p + 1}", $"Player {p + 1}");
        public static string ComeBack(int p) => T($"Lojtari {p + 1}: kthehu para kamerës", $"Player {p + 1}: come back in front of the camera");
        public static string Best(int n) => T($"Rekordi: {n}", $"Best: {n}");
        public static string NoBest => T("Pa rekord ende", "No best yet");
        public static string Tickets => T("BILETA", "TICKETS");
        public static string TicketsWon(int n) => T($"+{n} bileta", $"+{n} tickets");
        public static string TicketsInAll(int n) => T($"gjithsej {n}", $"{n} in all");
        public static string ToNextPrize(int n) => T($"Edhe {n} bileta për lodrën tjetër", $"{n} more tickets for the next toy");
        public static string AllPrizes => T("I fitove të gjitha lodrat!", "You won every toy!");
        public static string Points => T("PIKË", "POINTS");
        public static string PointsOf(int n) => T($"{n} pikë", $"{n} points");
        public static string Time => T("KOHA", "TIME");
        public static string Streak(int n) => T($"Seri {n}!", $"Streak {n}!");
        public static string AgainOrBack => T("Ngri duart për të luajtur sërish · ← → kthehu në lunapark",
                                              "Raise your hands to play again · ← → back to the fun fair");
        public static string AgainKeys => T("OK luaj sërish · ← → kthehu", "OK play again · ← → back");
        public static string GameOf(int n) => T($"Loja {n} nga {ArcadeParty.Rounds}", $"Game {n} of {ArcadeParty.Rounds}");
        public static string NextGame(Machine m) => T($"Tjetra: {Name(m)}", $"Next: {Name(m)}");
        public static string RoundWinner(int p) => T($"Fitoi Lojtari {p + 1}!", $"Player {p + 1} wins!");
        public static string Draw => T("Barazim!", "A draw!");
        public static string PartyWinner(int p) => T($"Lojtari {p + 1} fitoi festën!", $"Player {p + 1} won the party!");
        public static string PartyTotal(int n) => T($"Gjithsej: {n} pikë", $"Total: {n} points");
        public static string Wins(int n) => T($"{n} fitore", $"{n} wins");
        public static string Perfect => T("Perfekt!", "Perfect!");
        public static string Through => T("Kalove!", "Through!");
        public static string Bonk => T("Bum!", "Bonk!");
        public static string Header => T("Kokë!", "Header!");
        public static string Combo(int n) => T($"{n} me një goditje!", $"{n} in one swipe!");

        public static string PlushName(int i) => i switch
        {
            0 => T("dinozaur", "dinosaur"),
            1 => T("arush", "teddy bear"),
            2 => T("lepurush", "bunny"),
            3 => T("mace", "kitten"),
            4 => T("njëbrirësh", "unicorn"),
            5 => T("oktapod", "octopus"),
            6 => T("panda", "panda"),
            _ => T("rosë", "duck"),
        };

        public static string WonPrize(int i) => T($"Fitove një {PlushName(i)} pelushi!", $"You won a plush {PlushName(i)}!");
    }
}
