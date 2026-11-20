using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// The fairies (2026-11-15; tall and slender since the user's "tall thin fairies like in winx and other fairy
    /// cartoons"): art/fairy/heroine.py's figure (the chibis' bones and regions, drawn by <see cref="Chibi"/> as model
    /// "Heroine") in a crop top or a bodice, a mini or petal skirt, knee boots, a tiara and a choker, with big hair and
    /// glowing wings made in code (<see cref="FairyArt.WingPanel"/>). Stardust opens new dresses and wings (<see cref="GardenProgress"/>); player 2
    /// wears sky blue with her hair in a ponytail, like player 2 in the other games. Original looks.
    /// </summary>
    public static class FairyLooks
    {
        /// <summary>A dress: which top and skirt of art/fairy/heroine.py, and its colours (vivid on purpose: the user wanted
        /// the cartoon fairies' colours, 2026-11-15).</summary>
        public struct Dress
        {
            public string Name, NameEn;
            public string TopPiece, BottomPiece;
            public string Top, TopAccent, Trim, Skirt, SkirtAccent, Shoes, Cuff, Gem;
        }

        public static readonly Dress[] Dresses =
        {
            new Dress { Name = "Trëndafili", NameEn = "Rose", TopPiece = "CropTop", BottomPiece = "MiniSkirt", Top = "#FF2E93", TopAccent = "#7DF2FF", Trim = "#FFE15A", Skirt = "#FF5AB8", SkirtAccent = "#FFD1EC", Shoes = "#FF2E93", Cuff = "#FFE15A", Gem = "#7DF2FF" },
            new Dress { Name = "Deti", NameEn = "Sea", TopPiece = "Bodice", BottomPiece = "PetalSkirt", Top = "#14C8B4", TopAccent = "#B8FFF0", Trim = "#FFFFFF", Skirt = "#2E7BFF", SkirtAccent = "#7DF2FF", Shoes = "#2E7BFF", Cuff = "#7DF2FF", Gem = "#FF5AB8" },
            new Dress { Name = "Livadhi", NameEn = "Meadow", TopPiece = "CropTop", BottomPiece = "PetalSkirt", Top = "#2FD158", TopAccent = "#FFE15A", Trim = "#FFFFFF", Skirt = "#9BEB3C", SkirtAccent = "#FFF27A", Shoes = "#22B04A", Cuff = "#FFE15A", Gem = "#FF5AB8" },
            new Dress { Name = "Jargavani", NameEn = "Lilac", TopPiece = "Bodice", BottomPiece = "MiniSkirt", Top = "#9A4DFF", TopAccent = "#F0D8FF", Trim = "#FFE15A", Skirt = "#D35BFF", SkirtAccent = "#FFFFFF", Shoes = "#7E35F0", Cuff = "#FFE15A", Gem = "#7DF2FF" },
            new Dress { Name = "Dielli", NameEn = "Sunshine", TopPiece = "CropTop", BottomPiece = "MiniSkirt", Top = "#FFB300", TopAccent = "#FF2E93", Trim = "#FFFFFF", Skirt = "#FF7A1A", SkirtAccent = "#FFE7A0", Shoes = "#FF7A1A", Cuff = "#FFE15A", Gem = "#FF2E93" },
            new Dress { Name = "Nata e Yjeve", NameEn = "Starry Night", TopPiece = "Bodice", BottomPiece = "PetalSkirt", Top = "#3A2FD6", TopAccent = "#FFE15A", Trim = "#9BE8FF", Skirt = "#5B3BFF", SkirtAccent = "#9BE8FF", Shoes = "#3A2FD6", Cuff = "#FFE15A", Gem = "#FFE15A" },
        };

        /// <summary>A pair of wings: its shape (<see cref="FairyArt.WingPanel"/>), its colours (at the root, at the edge,
        /// the glowing rim), how much it sparkles and how big.</summary>
        public struct Wings
        {
            public string Name, NameEn;
            public int Shape;
            public Color Root, Edge, Rim;
            public float Glitter, Size;
        }

        static Color C(string hex, float a = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a = a;
            return c;
        }

        public static readonly Wings[] AllWings =
        {
            new Wings { Name = "Flutura", NameEn = "Butterfly", Shape = 0, Root = C("#FFD0EC", 0.5f), Edge = C("#FF3FA8", 0.88f), Rim = C("#FFFFFF"), Glitter = 0.4f, Size = 1f },
            new Wings { Name = "Pilivesa", NameEn = "Dragonfly", Shape = 1, Root = C("#D0F8FF", 0.45f), Edge = C("#1ED4FF", 0.88f), Rim = C("#E0FFFF"), Glitter = 0.5f, Size = 1.05f },
            new Wings { Name = "Petalet", NameEn = "Petals", Shape = 2, Root = C("#FFF0B8", 0.5f), Edge = C("#FFA800", 0.9f), Rim = C("#FFFFFF"), Glitter = 0.5f, Size = 1.05f },
            new Wings { Name = "Zemrat", NameEn = "Hearts", Shape = 3, Root = C("#FFD0E4", 0.5f), Edge = C("#FF1E6E", 0.9f), Rim = C("#FFE15A"), Glitter = 0.6f, Size = 1.1f },
            new Wings { Name = "Yjet", NameEn = "Stars", Shape = 4, Root = C("#DCD4FF", 0.5f), Edge = C("#6A3CFF", 0.9f), Rim = C("#FFE15A"), Glitter = 0.8f, Size = 1.12f },
            new Wings { Name = "Kristali", NameEn = "Crystal", Shape = 5, Root = C("#E0FFFF", 0.42f), Edge = C("#22F0FF", 0.85f), Rim = C("#FFFFFF"), Glitter = 1f, Size = 1.15f },
            new Wings { Name = "Pendët", NameEn = "Feathers", Shape = 6, Root = C("#FFFFFF", 0.55f), Edge = C("#FF8ACB", 0.9f), Rim = C("#FFC93C"), Glitter = 0.7f, Size = 1.18f },
            new Wings { Name = "Ylberi", NameEn = "Rainbow", Shape = 7, Root = C("#FFFFFF", 0.6f), Edge = C("#FF6FB5", 0.9f), Rim = C("#FFFFFF"), Glitter = 1f, Size = 1.25f },
        };

        /// <summary>Player 1: long golden waves, sky-blue eyes, the dress chosen (art/fairy/heroine.py's tall fairy).</summary>
        public static ChibiLook Player(int dress) =>
            Wear(Fairy("Fairy", "Wavy", "#FFC12E", "#FF5AA8", "#19B6E8", "#FFD9C6", "#FF9AA8"), Dresses[Mathf.Clamp(dress, 0, Dresses.Length - 1)]);

        /// <summary>Player 2: a dark violet ponytail, violet eyes, the sea dress.</summary>
        public static ChibiLook Player2() => Wear(Fairy("Fairy2", "Pony", "#3A1E5C", "#4DF0D0", "#8B4DFF", "#C98E6A", "#E8707E"), Dresses[1]);

        /// <summary>A pixie (a freed one, one in a bubble): a bob in a colour of her own (drawn small).</summary>
        public static ChibiLook Pixie(int k) =>
            Wear(Fairy("Pixie", "Bob", k % 3 == 0 ? "#FFE08A" : k % 3 == 1 ? "#FF5AB0" : "#B98BFF", "#FFE15A", "#3CCB5A", "#FFE2D2", "#FF9AB8"),
                 Dresses[(k + 2) % Dresses.Length]);

        static ChibiLook Fairy(string name, string hair, string hairColour, string hairAccent, string eyes, string skin, string blush) =>
            new ChibiLook { Name = name, Model = "Heroine", Hair = hair, Ears = null, Back = null, Accessory = "Choker" }
                .Paint("hair", hairColour).Paint("hair_accent", hairAccent).Paint("eyes", eyes).Paint("skin", skin).Paint("blush", blush)
                .Paint("eye_shine", "#FFFFFF").Paint("ears", "#2A1630").Paint("mouth", "#F2557E").Paint("tongue", "#A3263F");

        static ChibiLook Wear(ChibiLook look, Dress d)
        {
            look.Top = d.TopPiece;
            look.Bottom = d.BottomPiece;
            look.Shoes = "Boots";
            look.Hat = "Tiara";
            return look.Paint("top", d.Top).Paint("top_accent", d.TopAccent).Paint("top_trim", d.Trim)
                .Paint("bottom", d.Skirt).Paint("bottom_accent", d.SkirtAccent)
                .Paint("shoes", d.Shoes).Paint("shoes_accent", d.Cuff).Paint("soles", d.Cuff)
                .Paint("hat", "#FFE15A").Paint("hat_accent", d.Gem).Paint("acc", d.Top).Paint("acc_accent", d.Gem);
        }

        public static Color DressColour(int dress) => C(Dresses[Mathf.Clamp(dress, 0, Dresses.Length - 1)].Top);

        /// <summary>An element's colours: its spells' main colour and a second one (the menu, beams, sparkles).</summary>
        public static Color ElementColour(Element e) => e switch
        {
            Element.Fire => new Color(1f, 0.52f, 0.18f),
            Element.Water => new Color(0.25f, 0.72f, 1f),
            Element.Nature => new Color(0.4f, 0.95f, 0.35f),
            _ => new Color(1f, 0.86f, 0.36f),
        };

        public static Color ElementSecond(Element e) => e switch
        {
            Element.Fire => new Color(1f, 0.85f, 0.3f),
            Element.Water => new Color(0.75f, 1f, 1f),
            Element.Nature => new Color(1f, 0.6f, 0.85f),
            _ => new Color(1f, 1f, 0.92f),
        };

        /// <summary>A spell's colour: its shape's dots on the screen as they light up, its flash when cast.</summary>
        public static Color SpellColour(Spell s) => s switch
        {
            Spell.Shield => new Color(0.4f, 0.8f, 1f),
            Spell.Seeds => new Color(0.5f, 0.95f, 0.4f),
            Spell.Freeze => new Color(0.65f, 0.95f, 1f),
            Spell.Heal => new Color(1f, 0.45f, 0.72f),
            Spell.Lightning => new Color(1f, 0.93f, 0.4f),
            Spell.Rainbow => new Color(0.85f, 0.6f, 1f),
            _ => new Color(1f, 0.78f, 0.3f),
        };
    }
}
