namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// Who plays Tenis!: the player's pair and the ladder's ten (one per <see cref="TennisLevel"/> name, in pairs),
    /// dressed from the shared character kit (<see cref="ChibiLook"/>, art/chibi). The player is the red-cap kid of the
    /// reference sheet, their partner at the net wears the same red; nobody on the ladder wears red, so the teams always
    /// tell apart.
    /// </summary>
    public static class TennisLooks
    {
        public static ChibiLook Player() => ChibiLook.CapKid();

        /// <summary>The player's partner at the net: a cat-eared girl in the team's red.</summary>
        public static ChibiLook Partner() => new ChibiLook { Name = "Partner", Hair = "Fluffy", Ears = "CatEars", Top = "Polo", Bottom = "Skirt" }
            .Paint("hair", "#5A3424").Paint("ears", "#5A3424").Paint("ears_inner", "#FFB3C7").Paint("top", "#E8403A")
            .Paint("top_trim", "#FFFFFF").Paint("bottom", "#FFFFFF").Paint("bottom_accent", "#E8403A")
            .Paint("shoes", "#FFFFFF").Paint("shoes_accent", "#E8403A");

        /// <summary>Player 2 (two players): a kid with headphones in sky blue, a colour nobody on the ladder wears, so the
        /// two players' toys always tell apart.</summary>
        public static ChibiLook Player2() => new ChibiLook { Name = "Player2", Hair = "Spiky", Accessory = "Headphones", Top = "TrackJacket" }
            .Paint("hair", "#3A2418").Paint("acc", "#FFFFFF").Paint("acc_accent", "#1E8FC0").Paint("top", "#31C6F4")
            .Paint("top_accent", "#31C6F4").Paint("top_trim", "#FFFFFF").Paint("bottom", "#24305E").Paint("bottom_accent", "#24305E")
            .Paint("shoes", "#31C6F4").Paint("shoes_accent", "#FFFFFF").Paint("soles", "#F4F4F8").Paint("skin", "#E8B896");

        /// <summary>Player 2's partner (versus: their team's net player): the cat-eared girl in player 2's sky blue.</summary>
        public static ChibiLook Partner2() => new ChibiLook { Name = "Partner2", Hair = "Fluffy", Ears = "CatEars", Top = "Polo", Bottom = "Skirt" }
            .Paint("hair", "#2A1A14").Paint("ears", "#2A1A14").Paint("ears_inner", "#FFB3C7").Paint("top", "#31C6F4")
            .Paint("top_trim", "#FFFFFF").Paint("bottom", "#FFFFFF").Paint("bottom_accent", "#31C6F4")
            .Paint("shoes", "#FFFFFF").Paint("shoes_accent", "#31C6F4");

        /// <summary>The computer's side for a match: its player (singles: no net player) or its pair's back and net player.</summary>
        public static (ChibiLook back, ChibiLook net) Opponents(TennisLevel level) =>
            (Opponent(level.NameIndex), level.Singles ? null : Opponent(level.PartnerIndex));

        /// <summary>The <paramref name="index"/>-th of the computer's fourteen players (<see cref="TennisLevel.Names"/>: 0 = Ana
        /// ... 9 = Gent, 10 = Sara ... 13 = Dion, 2026-11-07: seven pairs for a tournament's eight teams).</summary>
        public static ChibiLook Opponent(int index)
        {
            int n = TennisLevel.Names.Length;
            switch (((index % n) + n) % n)
            {
                case 10:   // Sara
                    return new ChibiLook { Name = "Sara", Hair = "Buns", Ears = "UnicornHorn", Top = "Hoodie", Bottom = "Skirt" }
                        .Paint("hair", "#2A1A14").Paint("hair_accent", "#FF8FD0").Paint("ears", "#F6C33B").Paint("ears_inner", "#FFFFFF")
                        .Paint("top", "#D63A9C").Paint("top_trim", "#A8287A").Paint("bottom", "#FFFFFF").Paint("bottom_accent", "#D63A9C")
                        .Paint("shoes", "#FFFFFF").Paint("shoes_accent", "#D63A9C").Paint("skin", "#E8B896");
                case 11:   // Arbi
                    return new ChibiLook { Name = "Arbi", Hair = "Short", Hat = "CapBack", Accessory = "RoundGlasses", Top = "Polo" }
                        .Paint("hair", "#3A2418").Paint("hat", "#F4F4F8").Paint("hat_accent", "#24305E").Paint("acc", "#1E1A2C")
                        .Paint("top", "#24305E").Paint("top_trim", "#FFFFFF").Paint("bottom", "#F4F4F8").Paint("bottom_accent", "#24305E")
                        .Paint("shoes", "#24305E").Paint("shoes_accent", "#FFFFFF").Paint("skin", "#C98E66");
                case 12:   // Ema
                    return new ChibiLook { Name = "Ema", Hair = "Shaggy", Back = "AngelWings", Top = "Tee", Bottom = "Skirt" }
                        .Paint("hair", "#8C5A2E").Paint("back", "#FFFFFF").Paint("back_accent", "#FFE9A8").Paint("top", "#A6D83A")
                        .Paint("top_trim", "#7FAE24").Paint("bottom", "#FFFFFF").Paint("bottom_accent", "#A6D83A")
                        .Paint("shoes", "#A6D83A").Paint("shoes_accent", "#FFFFFF");
                case 13:   // Dion
                    return new ChibiLook { Name = "Dion", Hair = "Spiky", Top = "BearHoodie", Bottom = "Shorts", Shoes = "HighTops" }
                        .Paint("hair", "#1E1612").Paint("top", "#A0703C").Paint("top_accent", "#6B4423").Paint("top_trim", "#F2E2C4")
                        .Paint("bottom", "#3A3E5C").Paint("bottom_accent", "#3A3E5C").Paint("shoes", "#F4F4F8").Paint("shoes_accent", "#A0703C")
                        .Paint("skin", "#D9A07A");
                case 0:   // Ana
                    return new ChibiLook { Name = "Ana", Hair = "Buns", Top = "Polo", Bottom = "Skirt" }
                        .Paint("hair", "#4A2A1E").Paint("hair_accent", "#FF8FB0").Paint("top", "#FF9AC4").Paint("top_trim", "#FFFFFF")
                        .Paint("bottom", "#FFFFFF").Paint("bottom_accent", "#FF9AC4").Paint("shoes", "#FFFFFF").Paint("shoes_accent", "#FF9AC4");
                case 1:   // Beni
                    return new ChibiLook { Name = "Beni", Hair = "Spiky", Hat = "Cap", Top = "Tee" }
                        .Paint("hair", "#1E1612").Paint("hat", "#2E6FE8").Paint("hat_accent", "#FFFFFF").Paint("top", "#2E6FE8")
                        .Paint("top_trim", "#FFFFFF").Paint("bottom", "#FFFFFF").Paint("bottom_accent", "#2E6FE8")
                        .Paint("shoes", "#2E6FE8").Paint("shoes_accent", "#FFFFFF").Paint("skin", "#E8B896");
                case 2:   // Era
                    return new ChibiLook { Name = "Era", Hair = "Fluffy", Ears = "BunnyEars", Top = "TeeStar", Bottom = "Skirt" }
                        .Paint("hair", "#C9A4FF").Paint("ears", "#FFFFFF").Paint("ears_inner", "#FFB3C7").Paint("top", "#7B61FF")
                        .Paint("top_accent", "#FFC93C").Paint("top_trim", "#5A44CC").Paint("bottom", "#FFFFFF").Paint("bottom_accent", "#7B61FF")
                        .Paint("shoes", "#7B61FF").Paint("shoes_accent", "#FFFFFF");
                case 3:   // Dini
                    return new ChibiLook { Name = "Dini", Hair = "Short", Accessory = "Headphones", Top = "TrackJacket" }
                        .Paint("hair", "#2A1A14").Paint("acc", "#3FA650").Paint("acc_accent", "#1B1726").Paint("top", "#3FA650")
                        .Paint("top_accent", "#3FA650").Paint("top_trim", "#FFFFFF").Paint("bottom", "#1E1A2C").Paint("bottom_accent", "#1E1A2C")
                        .Paint("shoes", "#FFFFFF").Paint("shoes_accent", "#3FA650").Paint("skin", "#C98E66");
                case 4:   // Lea
                    return new ChibiLook { Name = "Lea", Hair = "Shaggy", Accessory = "StarClip", Top = "HoodieStar" }
                        .Paint("hair", "#F2D35C").Paint("acc", "#FF8FB0").Paint("top", "#FFC93C").Paint("top_accent", "#FFFFFF")
                        .Paint("top_trim", "#F2B320").Paint("bottom", "#24305E").Paint("bottom_accent", "#24305E")
                        .Paint("shoes", "#FFC93C").Paint("shoes_accent", "#FFFFFF");
                case 5:   // Toni
                    return new ChibiLook { Name = "Toni", Hair = "Spiky", Hat = "Bucket", Top = "Polo" }
                        .Paint("hair", "#7A4526").Paint("hat", "#F2E2C4").Paint("hat_accent", "#FF8A3D").Paint("top", "#FF8A3D")
                        .Paint("top_trim", "#FFFFFF").Paint("bottom", "#C9B48E").Paint("bottom_accent", "#C9B48E")
                        .Paint("shoes", "#FFFFFF").Paint("shoes_accent", "#FF8A3D");
                case 6:   // Mira
                    return new ChibiLook { Name = "Mira", Hair = "Buns", Accessory = "HeartGlasses", Top = "Hoodie", Bottom = "Skirt" }
                        .Paint("hair", "#1E1612").Paint("hair_accent", "#3FD6B0").Paint("acc", "#FF5A8A").Paint("top", "#7DE3C4")
                        .Paint("top_trim", "#4FC4A2").Paint("bottom", "#FFFFFF").Paint("bottom_accent", "#7DE3C4")
                        .Paint("shoes", "#7DE3C4").Paint("shoes_accent", "#FFFFFF").Paint("skin", "#D9A07A");
                case 7:   // Leka
                    return new ChibiLook { Name = "Leka", Hair = "Short", Hat = "Crown", Top = "Checkered", Bottom = "Pants", Shoes = "HighTops" }
                        .Paint("hair", "#2A1A14").Paint("hat", "#F6C33B").Paint("hat_accent", "#2E6FE8").Paint("top", "#1E1A2C")
                        .Paint("top_accent", "#F7F5F9").Paint("top_trim", "#1E1A2C").Paint("bottom", "#3A3E5C")
                        .Paint("shoes", "#F7F5F9").Paint("shoes_accent", "#1E1A2C");
                case 8:   // Nora
                    return new ChibiLook { Name = "Nora", Hair = "Fluffy", Hat = "FrogHat", Top = "ZipHoodie", Back = "RaccoonTail" }
                        .Paint("hair", "#F2F2F7").Paint("hat", "#5DB04E").Paint("hat_accent", "#FFFFFF").Paint("top", "#4E9A44")
                        .Paint("top_trim", "#2F6A2A").Paint("bottom", "#2F6A2A").Paint("bottom_accent", "#2F6A2A")
                        .Paint("back", "#8A7D78").Paint("back_accent", "#3A3438").Paint("shoes", "#2F6A2A").Paint("shoes_accent", "#FFFFFF");
                default:  // Gent
                    return new ChibiLook { Name = "Gent", Hair = "Spiky", Ears = "DevilHorns", Back = "DevilTail", Top = "Jacket", Bottom = "Pants" }
                        .Paint("hair", "#1E1612").Paint("ears", "#5B2E8C").Paint("ears_inner", "#2E1450").Paint("back", "#5B2E8C")
                        .Paint("back_accent", "#7B4FB5").Paint("top", "#26203A").Paint("top_accent", "#7B4FB5").Paint("top_trim", "#A27BFF")
                        .Paint("bottom", "#26203A").Paint("shoes", "#26203A").Paint("shoes_accent", "#FFFFFF").Paint("skin", "#E8B896");
            }
        }
    }
}
