using UnityEngine;

namespace MoveIt.Games.Boxing
{
    /// <summary>
    /// How everyone and everything in Superboks! looks: the hero's outfits (opened by stars), the gloves (each stage
    /// champion's, Wii Sports' silver-gloves prize), the climb's twelve opponents and the referee (chibis: art/chibi with
    /// the boxers' pieces), and each stage's light (Tundu/Arena's globals, the sun, the air). The gloves' regions are
    /// art/boxing/boxing.py's REGIONS (keep in step).
    /// </summary>
    public static class BoxingLooks
    {
        // ------------------------------------------------------------------ the boxing kit's colour regions (the gloves, the belt)

        public const int Glove = 13, GloveCuff = 14, GloveTrim = 15, BeltStrap = 16, BeltPlate = 17, BeltGem = 18;

        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        /// <summary>A kit's colour table (linear, gloss in alpha) from region colours (sRGB hex) and a gloss.</summary>
        public static Vector4[] Table(params (int region, string hex, float gloss)[] colours)
        {
            var t = new Vector4[24];
            for (int i = 0; i < t.Length; i++) t[i] = new Vector4(0.5f, 0.5f, 0.5f, 0.1f);
            foreach (var (region, hex, gloss) in colours)
            {
                var l = C(hex).linear;
                t[region] = new Vector4(l.r, l.g, l.b, gloss);
            }
            return t;
        }

        /// <summary>The champions' belt (gold plate, a red gem on a black strap).</summary>
        public static Vector4[] BeltTable() =>
            Table((BeltStrap, "#1E1A2C", 0.3f), (BeltPlate, "#FFC93C", 0.85f), (BeltGem, "#E8403A", 0.9f));

        // ------------------------------------------------------------------ the hero

        /// <summary>The hero's outfits: suit, trunks (and boots, mask), emblem, cape. The first is the hero's own.</summary>
        public static readonly (string name, string suit, string trunks, string emblem, string cape)[] Outfits =
        {
            ("Klasik", "#2E6FE8", "#E8403A", "#FFC93C", "#E8403A"),
            ("Zjarr", "#E8403A", "#FFC93C", "#FFC93C", "#FF8A3D"),
            ("Natë", "#1E2A5C", "#B9BDCC", "#FFC93C", "#7B4FB5"),
            ("Pyll", "#3E9A4E", "#FFC93C", "#FFFFFF", "#2A6A38"),
            ("Akull", "#E8F2FF", "#31C6F4", "#31C6F4", "#31C6F4"),
            ("Luan", "#1E1A2C", "#FFC93C", "#FFC93C", "#FFC93C"),
            ("Galaktik", "#7B4FB5", "#FF5AD2", "#7DF2FF", "#FF5AD2"),
            ("Kampion", "#FFC93C", "#E8403A", "#E8403A", "#FF5A8A"),
        };

        public static ChibiLook Hero(int outfit)
        {
            var o = Outfits[Mathf.Clamp(outfit, 0, Outfits.Length - 1)];
            return new ChibiLook { Name = "Hero", Hair = "Spiky", Top = "HeroSuit", Bottom = "Trunks", Shoes = "SpaceBoots", Accessory = "HeroMask" }
                .Paint("hair", "#3A2418").Paint("top", o.suit).Paint("top_accent", o.emblem).Paint("top_trim", o.trunks)
                .Paint("bottom", o.trunks).Paint("bottom_accent", o.emblem).Paint("shoes", o.trunks).Paint("shoes_accent", o.emblem)
                .Paint("soles", "#2A2D3E").Paint("acc", o.trunks).Paint("acc_accent", o.emblem).Paint("back", o.cape)
                .Paint("back_accent", o.emblem);
        }

        public static Color Cape(int outfit) => C(Outfits[Mathf.Clamp(outfit, 0, Outfits.Length - 1)].cape);

        /// <summary>The see-through hero's glow: the suit's colour, lighter.</summary>
        public static Color Glow(int outfit) => Color.Lerp(C(Outfits[Mathf.Clamp(outfit, 0, Outfits.Length - 1)].suit), Color.white, 0.55f);

        // ------------------------------------------------------------------ gloves

        /// <summary>The gloves: the hero's own red ones, then each stage champion's (their colours), the last the villain's.</summary>
        public static readonly (string glove, string cuff, string trim)[] Gloves =
        {
            ("#E8403A", "#F4F2F8", "#FFC93C"),
            ("#FFC93C", "#1E1A2C", "#E8403A"),
            ("#B9BDCC", "#56607A", "#FFC93C"),
            ("#E8F2FF", "#31C6F4", "#7DF2FF"),
            ("#7B4FB5", "#FFC93C", "#FFE15A"),
        };

        public static Vector4[] GloveTable(int gloves)
        {
            var g = Gloves[Mathf.Clamp(gloves, 0, Gloves.Length - 1)];
            return Table((Glove, g.glove, 0.55f), (GloveCuff, g.cuff, 0.25f), (GloveTrim, g.trim, 0.4f));
        }

        // ------------------------------------------------------------------ the opponents and the referee

        /// <summary>The look of the opponent <paramref name="id"/> (<see cref="OpponentProfile.Id"/>), its gloves' colours,
        /// its cape (none = no cape), and its colour in the HUD.</summary>
        public static (ChibiLook look, Vector4[] gloves, Color? cape) Opponent(string id)
        {
            switch (id)
            {
                case "beni":
                    return (new ChibiLook { Name = id, Hair = "Short", Hat = "Headgear", Top = "Tee", Bottom = "Trunks", Shoes = "HighTops" }
                            .Paint("hair", "#5A3420").Paint("hat", "#E8403A").Paint("hat_accent", "#F4F2F8").Paint("top", "#F4F2F8")
                            .Paint("top_trim", "#E8403A").Paint("bottom", "#2E6FE8").Paint("bottom_accent", "#F4F2F8")
                            .Paint("shoes", "#F4F2F8").Paint("shoes_accent", "#2E6FE8"),
                        Gl("#E8403A", "#F4F2F8", "#2E6FE8"), null);
                case "rina":
                    return (new ChibiLook { Name = id, Hair = "Pony", Hat = "Headband", Top = "Tee", Bottom = "Trunks", Shoes = "HighTops" }
                            .Paint("hair", "#2A1A14").Paint("hair_accent", "#FF5A8A").Paint("hat", "#FF5A8A").Paint("hat_accent", "#FFFFFF")
                            .Paint("top", "#FF9AC4").Paint("top_trim", "#FF5A8A").Paint("bottom", "#7B4FB5").Paint("bottom_accent", "#FF9AC4")
                            .Paint("shoes", "#7B4FB5").Paint("shoes_accent", "#FF9AC4"),
                        Gl("#FF5A8A", "#FFFFFF", "#7B4FB5"), null);
                case "leka":
                    return (new ChibiLook { Name = id, Hair = "Shaggy", Hat = "Headband", Top = "Polo", Bottom = "Trunks", Shoes = "HighTops" }
                            .Paint("hair", "#1E1A2C").Paint("hat", "#FFC93C").Paint("hat_accent", "#E8403A").Paint("top", "#1E1A2C")
                            .Paint("top_trim", "#FFC93C").Paint("bottom", "#FFC93C").Paint("bottom_accent", "#1E1A2C")
                            .Paint("shoes", "#1E1A2C").Paint("shoes_accent", "#FFC93C"),
                        Gl("#FFC93C", "#1E1A2C", "#E8403A"), null);
                case "robotik":
                    return (Robot(id, "#8C98AE", "#56607A", "#FFC93C"), Gl("#B9BDCC", "#56607A", "#FFC93C"), null);
                case "magneta":
                    return (Robot(id, "#E8403A", "#2E6FE8", "#F4F2F8"), Gl("#E8403A", "#2E6FE8", "#F4F2F8"), null);
                case "robobos":
                    return (Robot(id, "#3A3E5C", "#FFC93C", "#FF5A4E").Copy().Also(l => l.Back = "Jetpack").Paint("back", "#56607A").Paint("back_accent", "#FFC93C"),
                        Gl("#3A3E5C", "#FFC93C", "#FF5A4E"), null);
                case "ujku":
                    return (new ChibiLook { Name = id, Hair = "Shaggy", Hat = "CatHood", Top = "ZipHoodie", Bottom = "Trunks", Shoes = "HighTops" }
                            .Paint("hair", "#6B6F80").Paint("hat", "#8C90A0").Paint("hat_accent", "#E8E6EE").Paint("top", "#5A5E70")
                            .Paint("top_trim", "#3A3E4C").Paint("bottom", "#3A3E4C").Paint("bottom_accent", "#B9BDCC").Paint("shoes", "#3A3E4C")
                            .Paint("shoes_accent", "#B9BDCC"),
                        Gl("#5A5E70", "#E8E6EE", "#31C6F4"), null);
                case "shqiponja":
                    return (new ChibiLook { Name = id, Hair = "Spiky", Top = "Jacket", Bottom = "Trunks", Shoes = "HighTops", Back = "AngelWings" }
                            .Paint("hair", "#F4F2F8").Paint("top", "#7A4526").Paint("top_accent", "#F4F2F8").Paint("top_trim", "#FFC93C")
                            .Paint("bottom", "#7A4526").Paint("bottom_accent", "#FFC93C").Paint("shoes", "#FFC93C").Paint("shoes_accent", "#7A4526")
                            .Paint("back", "#8A5A36").Paint("back_accent", "#F4F2F8"),
                        Gl("#7A4526", "#FFC93C", "#F4F2F8"), null);
                case "jeti":
                    return (new ChibiLook { Name = id, Hair = "Fluffy", Hat = null, Ears = "BearEars", Top = "BearHoodie", Bottom = "Pants", Shoes = "HighTops" }
                            .Paint("skin", "#D6E8FF").Paint("blush", "#A9C8F2").Paint("hair", "#FFFFFF").Paint("ears", "#F4F2F8")
                            .Paint("ears_inner", "#A9C8F2").Paint("top", "#F4F2F8").Paint("top_accent", "#D6E8FF").Paint("top_trim", "#E8E6EE")
                            .Paint("bottom", "#F4F2F8").Paint("bottom_accent", "#D6E8FF").Paint("shoes", "#A9C8F2").Paint("shoes_accent", "#FFFFFF"),
                        Gl("#31C6F4", "#F4F2F8", "#FFFFFF"), null);
                case "gugu":
                    return (Alien(id, "#8EE07A", "#7B4FB5", "#FF5AD2"), Gl("#8EE07A", "#7B4FB5", "#FF5AD2"), null);
                case "hija":
                    return (new ChibiLook { Name = id, Hair = "Spiky", Top = "HeroSuit", Bottom = "Trunks", Shoes = "SpaceBoots", Accessory = "HeroMask" }
                            .Paint("skin", "#9A8FB8").Paint("blush", "#7B6FA0").Paint("hair", "#1E1A2C").Paint("top", "#1E1A2C")
                            .Paint("top_accent", "#7B4FB5").Paint("top_trim", "#FF5AD2").Paint("bottom", "#3A2E5C").Paint("bottom_accent", "#7B4FB5")
                            .Paint("shoes", "#3A2E5C").Paint("shoes_accent", "#7B4FB5").Paint("acc", "#FF5AD2").Paint("acc_accent", "#7B4FB5"),
                        Gl("#1E1A2C", "#7B4FB5", "#FF5AD2"), C("#2E1450"));
                case "erresira":
                    return (new ChibiLook { Name = id, Hair = "Shaggy", Ears = "DevilHorns", Top = "Jacket", Bottom = "Pants", Shoes = "HighTops", Accessory = "RoundGlasses" }
                            .Paint("skin", "#E6D6FF").Paint("blush", "#C9A8F2").Paint("hair", "#F4F2F8").Paint("ears", "#5B2E8C")
                            .Paint("ears_inner", "#2E1450").Paint("top", "#2E1450").Paint("top_accent", "#7B4FB5").Paint("top_trim", "#FF5AD2")
                            .Paint("bottom", "#1E1A2C").Paint("bottom_accent", "#2E1450").Paint("shoes", "#1E1A2C").Paint("shoes_accent", "#FF5AD2")
                            .Paint("acc", "#FF5AD2").Paint("acc_accent", "#7DF2FF"),
                        Gl("#2E1450", "#FF5AD2", "#7DF2FF"), C("#5A1E8C"));
                default:
                    return (ChibiLook.CapKid(), Gl("#2E6FE8", "#FFFFFF", "#FFC93C"), null);
            }
        }

        /// <summary>The referee: a striped shirt (art/chibi's RefShirt), black trousers, a bow tie.</summary>
        public static ChibiLook Referee() =>
            new ChibiLook { Name = "Referee", Hair = "Short", Top = "RefShirt", Bottom = "Pants", Shoes = "Sneakers" }
                .Paint("hair", "#4A3020").Paint("top", "#F4F2F8").Paint("top_accent", "#22202E").Paint("top_trim", "#22202E")
                .Paint("bottom", "#22202E").Paint("bottom_accent", "#22202E").Paint("shoes", "#22202E").Paint("shoes_accent", "#F4F2F8")
                .Paint("soles", "#F4F2F8");

        static Vector4[] Gl(string glove, string cuff, string trim) => Table((Glove, glove, 0.55f), (GloveCuff, cuff, 0.25f), (GloveTrim, trim, 0.4f));

        static ChibiLook Robot(string id, string body, string trim, string light) =>
            new ChibiLook { Name = id, Hair = null, Hat = "RobotHead", Top = "SpaceSuit", Bottom = "SpacePants", Shoes = "SpaceBoots" }
                .Paint("skin", "#C9D3E2").Paint("blush", "#9FB4D6").Paint("hat", body).Paint("hat_accent", light).Paint("top", body)
                .Paint("top_accent", light).Paint("top_trim", trim).Paint("bottom", body).Paint("bottom_accent", light).Paint("shoes", trim)
                .Paint("shoes_accent", light).Paint("soles", "#2E3446");

        static ChibiLook Alien(string id, string skin, string suit, string light) =>
            new ChibiLook { Name = id, Hair = null, Ears = "Antennae", Top = "SpaceSuit", Bottom = "SpacePants", Shoes = "SpaceBoots" }
                .Paint("skin", skin).Paint("blush", Darker(skin)).Paint("ears", Darker(skin)).Paint("ears_inner", light).Paint("top", suit)
                .Paint("top_accent", light).Paint("top_trim", Darker(suit)).Paint("bottom", suit).Paint("bottom_accent", light)
                .Paint("shoes", Darker(suit)).Paint("shoes_accent", light).Paint("soles", "#2E1450");

        static string Darker(string hex) => "#" + ColorUtility.ToHtmlStringRGB(C(hex) * 0.78f);

        static ChibiLook Also(this ChibiLook look, System.Action<ChibiLook> change)
        {
            change(look);
            return look;
        }

        // ------------------------------------------------------------------ the stages' light

        /// <summary>A stage's light: the sun (the key light, from the camera's side so the opponent's face is lit) and the
        /// air, and Tundu/Arena's globals (see the shader). Colours in sRGB.</summary>
        public sealed class Theme
        {
            public Color Sun, Ambient, Shade, Sky, Bounce, Rim, RimShade, Haze, HazeFar, Background;
            public float SunIntensity = 1.1f, SunPitch = 45f, SunYaw = 15f, RimStrength = 0.35f, Night;
            /// <summary>Tundu/Arena's haze: start, end (m), most, how fast it thins with height.</summary>
            public Vector4 Fog = new Vector4(30f, 160f, 0.6f, 0.02f);
            /// <summary>The crowd: the share of the seats taken, and its light (Tundu/Crowd's tint).</summary>
            public float Crowd = 0.85f;
            public Color CrowdTint = Color.white;
        }

        public static readonly Theme[] Stages =
        {
            // the gym at night: the rig's warm light on the ring, plum shadows, the hall fading into the dark
            new Theme
            {
                Sun = C("#FFE6C8"), SunIntensity = 1.15f, SunPitch = 58f, SunYaw = 18f, Ambient = C("#8A7AA8"),
                Shade = C("#4A3A6A"), Sky = C("#8A7AB8"), Bounce = C("#6A4A42"), Rim = C("#FFD8A0"), RimShade = C("#6A5AA8"), RimStrength = 0.35f,
                Haze = C("#2A2238"), HazeFar = C("#160F24"), Fog = new Vector4(12f, 34f, 0.6f, 0.06f), Night = 1f, Background = C("#120E1E"),
                Crowd = 0.92f, CrowdTint = C("#B4A2C4"),
            },
            // the roof at sunset: a warm orange light from the side, violet shadows, the sunset's glow on every edge
            new Theme
            {
                Sun = C("#FFC8A0"), SunIntensity = 1.12f, SunPitch = 32f, SunYaw = -28f, Ambient = C("#B89AB0"),
                Shade = C("#5A4A8A"), Sky = C("#A08ACA"), Bounce = C("#8A6A78"), Rim = C("#FFAA7A"), RimShade = C("#7A5AA0"), RimStrength = 0.5f,
                Haze = C("#E8A08A"), HazeFar = C("#B87A9A"), Fog = new Vector4(26f, 140f, 0.62f, 0.015f), Night = 0.6f, Background = C("#3A2A6A"),
                Crowd = 0.9f, CrowdTint = C("#F0C4B4"),
            },
            // the mountain by day: a bright white sun, blue shadows, the snow's light from below, a pale haze far away (the
            // fill lights lower than the snow suggests: with them near white, the snow, the canvas and the yeti were one haze)
            new Theme
            {
                Sun = C("#FFF8EC"), SunIntensity = 1.25f, SunPitch = 46f, SunYaw = -32f, Ambient = C("#9FB4D8"),
                Shade = C("#5A74B8"), Sky = C("#A8C8F4"), Bounce = C("#B8C8E4"), Rim = C("#FFFFFF"), RimShade = C("#8AA8E0"), RimStrength = 0.3f,
                Haze = C("#D8EEFF"), HazeFar = C("#BFE0FF"), Fog = new Vector4(40f, 220f, 0.75f, 0.01f), Night = 0f, Background = C("#6AAEF2"),
            },
            // the stars: a cool white light, violet shadows, neon rims, the deep dark of space
            new Theme
            {
                Sun = C("#ECE4FF"), SunIntensity = 1.1f, SunPitch = 40f, SunYaw = 26f, Ambient = C("#7A70B0"),
                Shade = C("#3A2E7A"), Sky = C("#6A5AB8"), Bounce = C("#2A1E5C"), Rim = C("#7DF2FF"), RimShade = C("#FF5AD2"), RimStrength = 0.6f,
                Haze = C("#1A1438"), HazeFar = C("#0A0820"), Fog = new Vector4(40f, 200f, 0.4f, 0f), Night = 1f, Background = C("#05060F"),
            },
        };
    }
}
