namespace MoveIt.Games.Volley
{
    /// <summary>
    /// Who plays Volejboll!, dressed for the beach from the shared character kit (<see cref="ChibiLook"/>, art/chibi): tees
    /// and shorts, caps, bucket hats and sunglasses. The player's team wears red (the red-cap kid of Tenis! and their
    /// cat-eared partner), player 2's team sky blue (the headphones kid); each of the computer's seven pairs wears its own
    /// colour (both of a pair alike), none of them red or sky blue, so the teams always tell apart.
    /// </summary>
    public static class VolleyLooks
    {
        const string Red = "#E8403A", Sky = "#31C6F4";

        /// <summary>Player 1 (and the one player): the red-cap kid in a red tee.</summary>
        public static ChibiLook Player() => new ChibiLook { Name = "Player", Hair = "Short", Hat = "CapBack", Top = "Tee", Bottom = "Shorts" }
            .Paint("hair", "#3A2418").Paint("hat", Red).Paint("hat_accent", "#FFFFFF").Paint("top", Red).Paint("top_trim", "#FFFFFF")
            .Paint("bottom", "#24305E").Paint("bottom_accent", "#24305E").Paint("shoes", "#FFFFFF").Paint("shoes_accent", Red);

        /// <summary>The one player's second toy (their partner), or player 1's in versus: a cat-eared girl in the same red.</summary>
        public static ChibiLook Partner() => new ChibiLook { Name = "Partner", Hair = "Fluffy", Ears = "CatEars", Top = "TeeStar", Bottom = "Skirt" }
            .Paint("hair", "#5A3424").Paint("ears", "#5A3424").Paint("ears_inner", "#FFB3C7").Paint("top", Red).Paint("top_accent", "#FFC93C")
            .Paint("top_trim", "#FFFFFF").Paint("bottom", "#FFFFFF").Paint("bottom_accent", Red).Paint("shoes", "#FFFFFF").Paint("shoes_accent", Red);

        /// <summary>Player 2: the headphones kid in sky blue.</summary>
        public static ChibiLook Player2() => new ChibiLook { Name = "Player2", Hair = "Spiky", Accessory = "Headphones", Top = "Tee", Bottom = "Shorts" }
            .Paint("hair", "#3A2418").Paint("acc", "#FFFFFF").Paint("acc_accent", "#1E8FC0").Paint("top", Sky).Paint("top_trim", "#FFFFFF")
            .Paint("bottom", "#24305E").Paint("bottom_accent", "#24305E").Paint("shoes", Sky).Paint("shoes_accent", "#FFFFFF").Paint("skin", "#E8B896");

        /// <summary>Player 2's second toy (versus): a bunny-eared girl in sky blue.</summary>
        public static ChibiLook Partner2() => new ChibiLook { Name = "Partner2", Hair = "Buns", Ears = "BunnyEars", Top = "TeeStar", Bottom = "Skirt" }
            .Paint("hair", "#2A1A14").Paint("hair_accent", "#FFFFFF").Paint("ears", "#FFFFFF").Paint("ears_inner", "#FFB3C7").Paint("top", Sky)
            .Paint("top_accent", "#FFFFFF").Paint("top_trim", "#1E8FC0").Paint("bottom", "#FFFFFF").Paint("bottom_accent", Sky)
            .Paint("shoes", "#FFFFFF").Paint("shoes_accent", Sky);

        /// <summary>Each pair's colour (its shirts), and the scoreboard's chip.</summary>
        public static readonly string[] PairColours = { "#FFC93C", "#7B61FF", "#5DB04E", "#FF8A3D", "#FF5AA8", "#2EC4B6", "#2E6FE8" };

        /// <summary>The two players of computer pair <paramref name="opponent"/> (<see cref="VolleyLevel.Names"/> in pairs).</summary>
        public static (ChibiLook a, ChibiLook b) Pair(int opponent)
        {
            int o = ((opponent % VolleyLevel.Pairs) + VolleyLevel.Pairs) % VolleyLevel.Pairs;
            string c = PairColours[o];
            return (Look(o * 2, c), Look(o * 2 + 1, c));
        }

        /// <summary>One of the fourteen in their pair's colour <paramref name="c"/>: a girl and a boy each pair, every one
        /// with their own hair, hat or glasses.</summary>
        static ChibiLook Look(int index, string c)
        {
            switch (index)
            {
                case 0:   // Drita
                    return new ChibiLook { Name = "Drita", Hair = "Buns", Accessory = "HeartGlasses", Top = "Tee", Bottom = "Skirt" }
                        .Paint("hair", "#4A2A1E").Paint("hair_accent", "#FF8FB0").Paint("acc", "#FF5A8A").Paint("top", c).Paint("top_trim", "#FFFFFF")
                        .Paint("bottom", "#FFFFFF").Paint("bottom_accent", c).Paint("shoes", "#FFFFFF").Paint("shoes_accent", c);
                case 1:   // Ilir
                    return new ChibiLook { Name = "Ilir", Hair = "Spiky", Hat = "Bucket", Top = "Tee", Bottom = "Shorts" }
                        .Paint("hair", "#1E1612").Paint("hat", "#FFFFFF").Paint("hat_accent", c).Paint("top", c).Paint("top_trim", "#FFFFFF")
                        .Paint("bottom", "#3A3E5C").Paint("bottom_accent", "#3A3E5C").Paint("shoes", c).Paint("shoes_accent", "#FFFFFF").Paint("skin", "#E8B896");
                case 2:   // Besa
                    return new ChibiLook { Name = "Besa", Hair = "Fluffy", Accessory = "StarClip", Top = "TeeStar", Bottom = "Skirt" }
                        .Paint("hair", "#C9A4FF").Paint("acc", "#FFC93C").Paint("top", c).Paint("top_accent", "#FFC93C").Paint("top_trim", "#FFFFFF")
                        .Paint("bottom", "#FFFFFF").Paint("bottom_accent", c).Paint("shoes", c).Paint("shoes_accent", "#FFFFFF");
                case 3:   // Arben
                    return new ChibiLook { Name = "Arben", Hair = "Short", Hat = "Cap", Accessory = "Glasses", Top = "Tee", Bottom = "Shorts" }
                        .Paint("hair", "#2A1A14").Paint("hat", c).Paint("hat_accent", "#FFFFFF").Paint("acc", "#1E1A2C").Paint("top", c)
                        .Paint("top_trim", "#FFFFFF").Paint("bottom", "#F4F4F8").Paint("bottom_accent", c).Paint("shoes", "#FFFFFF")
                        .Paint("shoes_accent", c).Paint("skin", "#C98E66");
                case 4:   // Jona
                    return new ChibiLook { Name = "Jona", Hair = "Shaggy", Hat = "Bucket", Top = "Tee", Bottom = "Skirt" }
                        .Paint("hair", "#8C5A2E").Paint("hat", "#F2E2C4").Paint("hat_accent", c).Paint("top", c).Paint("top_trim", "#FFFFFF")
                        .Paint("bottom", "#FFFFFF").Paint("bottom_accent", c).Paint("shoes", c).Paint("shoes_accent", "#FFFFFF");
                case 5:   // Luan
                    return new ChibiLook { Name = "Luan", Hair = "Spiky", Accessory = "RoundGlasses", Top = "Polo", Bottom = "Shorts" }
                        .Paint("hair", "#7A4526").Paint("acc", "#1E1A2C").Paint("top", c).Paint("top_trim", "#FFFFFF").Paint("bottom", "#C9B48E")
                        .Paint("bottom_accent", "#C9B48E").Paint("shoes", "#FFFFFF").Paint("shoes_accent", c);
                case 6:   // Rina
                    return new ChibiLook { Name = "Rina", Hair = "Buns", Hat = "CapStar", Top = "Tee", Bottom = "Shorts" }
                        .Paint("hair", "#1E1612").Paint("hair_accent", "#FFC93C").Paint("hat", c).Paint("hat_accent", "#FFFFFF").Paint("top", c)
                        .Paint("top_trim", "#FFFFFF").Paint("bottom", "#FFFFFF").Paint("bottom_accent", c).Paint("shoes", c).Paint("shoes_accent", "#FFFFFF")
                        .Paint("skin", "#D9A07A");
                case 7:   // Altin
                    return new ChibiLook { Name = "Altin", Hair = "Short", Accessory = "Headphones", Top = "Tee", Bottom = "Shorts" }
                        .Paint("hair", "#2A1A14").Paint("acc", c).Paint("acc_accent", "#1B1726").Paint("top", c).Paint("top_trim", "#FFFFFF")
                        .Paint("bottom", "#1E1A2C").Paint("bottom_accent", "#1E1A2C").Paint("shoes", "#FFFFFF").Paint("shoes_accent", c).Paint("skin", "#C98E66");
                case 8:   // Dea
                    return new ChibiLook { Name = "Dea", Hair = "Fluffy", Ears = "BunnyEars", Top = "Tee", Bottom = "Skirt" }
                        .Paint("hair", "#F2D35C").Paint("ears", "#FFFFFF").Paint("ears_inner", "#FFB3C7").Paint("top", c).Paint("top_trim", "#FFFFFF")
                        .Paint("bottom", "#FFFFFF").Paint("bottom_accent", c).Paint("shoes", "#FFFFFF").Paint("shoes_accent", c);
                case 9:   // Erion
                    return new ChibiLook { Name = "Erion", Hair = "Spiky", Hat = "CapBack", Top = "Tee", Bottom = "Shorts" }
                        .Paint("hair", "#1E1612").Paint("hat", "#FFFFFF").Paint("hat_accent", c).Paint("top", c).Paint("top_trim", "#FFFFFF")
                        .Paint("bottom", "#3A3E5C").Paint("bottom_accent", "#3A3E5C").Paint("shoes", c).Paint("shoes_accent", "#FFFFFF");
                case 10:  // Kejsi
                    return new ChibiLook { Name = "Kejsi", Hair = "Shaggy", Accessory = "Glasses", Top = "TeeStar", Bottom = "Skirt" }
                        .Paint("hair", "#1E1612").Paint("acc", "#FF5A8A").Paint("top", c).Paint("top_accent", "#FFFFFF").Paint("top_trim", "#FFFFFF")
                        .Paint("bottom", "#FFFFFF").Paint("bottom_accent", c).Paint("shoes", c).Paint("shoes_accent", "#FFFFFF").Paint("skin", "#E8B896");
                case 11:  // Bledi
                    return new ChibiLook { Name = "Bledi", Hair = "Short", Hat = "Bucket", Accessory = "RoundGlasses", Top = "Tee", Bottom = "Shorts" }
                        .Paint("hair", "#3A2418").Paint("hat", c).Paint("hat_accent", "#FFFFFF").Paint("acc", "#1E1A2C").Paint("top", c)
                        .Paint("top_trim", "#FFFFFF").Paint("bottom", "#F4F4F8").Paint("bottom_accent", c).Paint("shoes", "#FFFFFF").Paint("shoes_accent", c);
                case 12:  // Ana
                    return new ChibiLook { Name = "Ana", Hair = "Buns", Hat = "Cap", Top = "Polo", Bottom = "Skirt" }
                        .Paint("hair", "#4A2A1E").Paint("hair_accent", "#FFFFFF").Paint("hat", c).Paint("hat_accent", "#FFFFFF").Paint("top", c)
                        .Paint("top_trim", "#FFFFFF").Paint("bottom", "#FFFFFF").Paint("bottom_accent", c).Paint("shoes", "#FFFFFF").Paint("shoes_accent", c);
                default:  // Driton
                    return new ChibiLook { Name = "Driton", Hair = "Spiky", Accessory = "Glasses", Top = "Tee", Bottom = "Shorts" }
                        .Paint("hair", "#2A1A14").Paint("acc", "#1E1A2C").Paint("top", c).Paint("top_trim", "#FFFFFF").Paint("bottom", "#24305E")
                        .Paint("bottom_accent", "#24305E").Paint("shoes", c).Paint("shoes_accent", "#FFFFFF").Paint("skin", "#D9A07A");
            }
        }
    }
}
