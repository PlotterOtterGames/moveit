using System;
using System.Collections.Generic;

namespace MoveIt.Games.Dress
{
    /// <summary>The wardrobe's categories, in the order of the left column.</summary>
    public enum Slot { Top, Bottom, Hair, Head, Face, Back, Hand }

    /// <summary>Fashion-show themes (Dress to Impress / Style Savvy style): every item fits some of them.</summary>
    public enum Theme { Princess, Fairy, Mermaid, Hero, Summer, Winter, Party }

    /// <summary>The "magic" around the player (top row button).</summary>
    public enum Effect { None, Sparkles, Hearts, Butterflies, Bubbles, Snow, Stars }

    /// <summary>Special fabrics unlocked with the stars from fashion shows; they join every item's colour cycle.</summary>
    public enum Finish { Plain, Rainbow, Glitter, Gold, Galaxy }

    public sealed class DressItem
    {
        public string Id;
        public Slot Slot;
        public Theme[] Themes;
        /// <summary>Colourways (indices into <see cref="DressCatalog.Palette"/>), the first is the default.</summary>
        public int[] Colors;
        public string NameSq, NameEn;

        public bool Fits(Theme theme) => Array.IndexOf(Themes, theme) >= 0;
    }

    /// <summary>
    /// Every item of Vishu! (art: tools/dress/outfits.py, ids must match), its colourways and themes. No engine
    /// types here, so the rules can be tested without Unity (tools/logic-tests).
    /// </summary>
    public static class DressCatalog
    {
        public const int SlotCount = 7;
        public const int ThemeCount = 7;

        /// <summary>Fabric and hair colours, "#RRGGBB".</summary>
        public static readonly string[] Palette =
        {
            "#FF7EC8", "#FF4F8B", "#FF6B5A", "#E8304A", "#FF9F1C", "#FFD23F", "#63D6A6", "#3DBE6A", "#2EC9D0",
            //  0 pink   1 rose     2 coral    3 red      4 orange   5 yellow   6 mint     7 green    8 aqua
            "#6EC3FF", "#3A6BFF", "#B08CFF", "#7B4DDB", "#F8F5FF", "#2E2638", "#FFC94A", "#D9DEE8",
            //  9 sky   10 blue    11 lilac   12 purple  13 white   14 black   15 gold    16 silver
            "#F4C95D", "#9B6235", "#5B3A24", "#262030", "#E37B3B",
            // 17 blonde 18 brown 19 dark brown 20 black hair 21 ginger
        };

        public const int Pink = 0, Rose = 1, Coral = 2, Red = 3, Orange = 4, Yellow = 5, Mint = 6, Green = 7, Aqua = 8,
            Sky = 9, Blue = 10, Lilac = 11, Purple = 12, White = 13, Black = 14, Gold = 15, Silver = 16,
            Blonde = 17, Brown = 18, DarkBrown = 19, HairBlack = 20, Ginger = 21;

        /// <summary>Colours that suit each theme (judges like them).</summary>
        public static readonly int[][] ThemeColors =
        {
            new[] { Pink, Rose, Lilac, Gold, White, Sky },        // princess
            new[] { Mint, Green, Pink, Lilac, Sky },              // fairy
            new[] { Aqua, Sky, Lilac, Mint, Purple, Coral },      // mermaid
            new[] { Red, Blue, Yellow, Black, Gold },             // hero
            new[] { Yellow, Orange, Coral, Aqua, Pink },          // summer
            new[] { White, Sky, Blue, Lilac, Silver },            // winter
            new[] { Purple, Lilac, Pink, Gold, Black, Rose },     // party
        };

        /// <summary>Total stars that unlock each finish (Rainbow, Glitter, Gold, Galaxy).</summary>
        public static readonly int[] FinishStars = { 8, 20, 36, 56 };

        static readonly int[] HairColors = { Blonde, Brown, DarkBrown, HairBlack, Ginger, Pink, Lilac, Aqua, Silver };

        static DressItem I(string id, Slot slot, string sq, string en, int[] colors, params Theme[] themes) =>
            new DressItem { Id = id, Slot = slot, NameSq = sq, NameEn = en, Colors = colors, Themes = themes };

        static int[] Hair(int first)
        {
            var list = new List<int> { first };
            foreach (int c in HairColors) if (c != first) list.Add(c);
            return list.ToArray();
        }

        /// <summary>All items, by slot, in the order of the right column.</summary>
        public static readonly DressItem[][] Items =
        {
            new[]
            {
                I("top_princess", Slot.Top, "Bluzë princeshe", "Princess bodice", new[] { Pink, Lilac, Sky, Yellow, White, Rose }, Theme.Princess),
                I("top_fairy", Slot.Top, "Bluzë zane", "Fairy top", new[] { Mint, Pink, Lilac, Sky, Yellow, Green }, Theme.Fairy),
                I("top_mermaid", Slot.Top, "Bluzë sirene", "Mermaid top", new[] { Aqua, Lilac, Pink, Sky, Mint, Purple }, Theme.Mermaid, Theme.Summer),
                I("top_hero", Slot.Top, "Kostum heroine", "Hero suit", new[] { Blue, Red, Purple, Black, Pink, Yellow }, Theme.Hero),
                I("top_winter", Slot.Top, "Pallto dimri", "Winter coat", new[] { Sky, White, Lilac, Pink, Red, Blue }, Theme.Winter),
                I("top_party", Slot.Top, "Bluzë me xixa", "Sequin top", new[] { Lilac, Pink, Gold, Silver, Black, Aqua }, Theme.Party),
            },
            new[]
            {
                I("bottom_ballgown", Slot.Bottom, "Fund ballo", "Ball gown", new[] { Pink, Lilac, Sky, Yellow, White, Rose }, Theme.Princess),
                I("bottom_petals", Slot.Bottom, "Fund me petale", "Petal skirt", new[] { Pink, Mint, Lilac, Orange, Sky, Yellow }, Theme.Fairy, Theme.Summer),
                I("bottom_tail", Slot.Bottom, "Bisht sirene", "Mermaid tail", new[] { Aqua, Lilac, Pink, Mint, Purple, Sky }, Theme.Mermaid),
                I("bottom_hero", Slot.Bottom, "Fund heroine", "Hero skirt", new[] { Red, Blue, Purple, Black, Pink, Yellow }, Theme.Hero),
                I("bottom_tutu", Slot.Bottom, "Tutu", "Tutu", new[] { Pink, Lilac, Sky, White, Mint, Yellow }, Theme.Party, Theme.Fairy),
                I("bottom_winter", Slot.Bottom, "Fund dimri", "Winter skirt", new[] { Sky, White, Lilac, Pink, Blue, Red }, Theme.Winter),
            },
            new[]
            {
                I("hair_long", Slot.Hair, "Flokë të gjata", "Long waves", Hair(Blonde), Theme.Princess),
                I("hair_buns", Slot.Hair, "Dy topuza", "Space buns", Hair(Pink), Theme.Party),
                I("hair_braid", Slot.Hair, "Gërshet", "Braid", Hair(Silver), Theme.Winter, Theme.Princess),
                I("hair_curly", Slot.Hair, "Kaçurrela", "Curls", Hair(DarkBrown), Theme.Summer, Theme.Party),
                I("hair_ponytail", Slot.Hair, "Bisht kali", "Ponytail", Hair(Brown), Theme.Hero),
                I("hair_mermaid", Slot.Hair, "Flokë sirene", "Mermaid hair", Hair(Ginger), Theme.Mermaid),
            },
            new[]
            {
                I("head_crown", Slot.Head, "Kurorë", "Crown", new[] { Gold, Silver, Pink, Lilac }, Theme.Princess),
                I("head_tiara", Slot.Head, "Diademë bore", "Snow tiara", new[] { Silver, Sky, Lilac, Pink, Gold }, Theme.Winter, Theme.Princess),
                I("head_flowers", Slot.Head, "Kurorë me lule", "Flower crown", new[] { Pink, Lilac, Yellow, Sky, Coral, White }, Theme.Fairy, Theme.Summer),
                I("head_catears", Slot.Head, "Veshë maceje", "Cat ears", new[] { Black, White, Pink, Lilac, Brown, Orange }, Theme.Party),
                I("head_unicorn", Slot.Head, "Bri njëbrirëshi", "Unicorn horn", new[] { Gold, Pink, Lilac, Sky, White }, Theme.Fairy, Theme.Party),
                I("head_sunhat", Slot.Head, "Kapelë dielli", "Sun hat", new[] { Coral, Pink, Sky, Lilac, Mint, Yellow }, Theme.Summer),
            },
            new[]
            {
                I("face_hearts", Slot.Face, "Syze zemra", "Heart glasses", new[] { Rose, Pink, Red, Lilac, Yellow, Sky }, Theme.Party, Theme.Summer),
                I("face_stars", Slot.Face, "Syze yje", "Star glasses", new[] { Yellow, Pink, Sky, Lilac, Mint, Coral }, Theme.Party),
                I("face_sunglasses", Slot.Face, "Syze dielli", "Sunglasses", new[] { Pink, Black, White, Red, Sky, Gold }, Theme.Summer, Theme.Hero),
                I("face_mask", Slot.Face, "Maskë balloje", "Masquerade mask", new[] { Purple, Pink, Gold, Black, Sky, Red }, Theme.Princess, Theme.Party),
                I("face_hero", Slot.Face, "Maskë heroine", "Hero mask", new[] { Red, Black, Blue, Purple, Pink, Yellow }, Theme.Hero),
                I("face_cheeks", Slot.Face, "Zemra në faqe", "Cheek hearts", new[] { Pink, Rose, Coral, Lilac, Orange }, Theme.Fairy, Theme.Princess),
            },
            new[]
            {
                I("back_fairy", Slot.Back, "Krahë zane", "Fairy wings", new[] { Mint, Sky, Pink, Lilac, Yellow, White }, Theme.Fairy),
                I("back_butterfly", Slot.Back, "Krahë flutureje", "Butterfly wings", new[] { Lilac, Sky, Orange, Pink, Mint, Yellow }, Theme.Fairy, Theme.Summer),
                I("back_angel", Slot.Back, "Krahë engjëlli", "Angel wings", new[] { White, Pink, Sky, Lilac, Yellow }, Theme.Winter, Theme.Princess),
                I("back_cape", Slot.Back, "Pelerinë heroine", "Hero cape", new[] { Red, Blue, Purple, Black, Yellow, Pink }, Theme.Hero),
                I("back_cloak", Slot.Back, "Mantel mbretëror", "Royal cloak", new[] { Purple, Red, Blue, Pink, Sky, Black }, Theme.Princess, Theme.Winter),
                I("back_dragon", Slot.Back, "Krahë dragoi", "Dragon wings", new[] { Purple, Green, Red, Blue, Pink, Black }, Theme.Hero, Theme.Party),
            },
            new[]
            {
                I("hand_wand", Slot.Hand, "Shkop magjik", "Magic wand", new[] { Yellow, Pink, Lilac, Sky, Mint, White }, Theme.Fairy, Theme.Princess),
                I("hand_bag", Slot.Hand, "Çantë", "Handbag", new[] { Pink, Red, Lilac, Black, White, Sky }, Theme.Party, Theme.Summer),
                I("hand_bouquet", Slot.Hand, "Buqetë", "Bouquet", new[] { Pink, White, Lilac, Sky, Yellow }, Theme.Princess, Theme.Summer),
                I("hand_mic", Slot.Hand, "Mikrofon", "Microphone", new[] { Purple, Pink, Gold, Black, Sky, Red }, Theme.Party),
                I("hand_icecream", Slot.Hand, "Akullore", "Ice cream", new[] { Pink, Mint, Orange, Lilac, Sky, Brown }, Theme.Summer),
                I("hand_balloon", Slot.Hand, "Tullumbace", "Balloon", new[] { Red, Pink, Lilac, Sky, Yellow, Mint }, Theme.Party),
            },
        };

        /// <summary>Which effects suit which themes (they count like an item in the show).</summary>
        public static bool EffectFits(Effect effect, Theme theme) => effect switch
        {
            Effect.Sparkles => theme == Theme.Princess || theme == Theme.Fairy,
            Effect.Hearts => theme == Theme.Party || theme == Theme.Princess,
            Effect.Butterflies => theme == Theme.Fairy || theme == Theme.Summer,
            Effect.Bubbles => theme == Theme.Mermaid || theme == Theme.Summer,
            Effect.Snow => theme == Theme.Winter,
            Effect.Stars => theme == Theme.Hero || theme == Theme.Party,
            _ => false,
        };

        public static IEnumerable<DressItem> All
        {
            get
            {
                foreach (var slot in Items)
                    foreach (var item in slot) yield return item;
            }
        }

        public static DressItem Find(string id)
        {
            foreach (var item in All)
                if (item.Id == id) return item;
            return null;
        }

        /// <summary>How many finishes the stars unlock.</summary>
        public static int FinishesUnlocked(int stars)
        {
            int n = 0;
            while (n < FinishStars.Length && stars >= FinishStars[n]) n++;
            return n;
        }

        /// <summary>"#RRGGBB" -> 0..1 components.</summary>
        public static void Rgb(int index, out float r, out float g, out float b)
        {
            string hex = Palette[Math.Max(0, Math.Min(Palette.Length - 1, index))];
            int v = Convert.ToInt32(hex.Substring(1), 16);
            r = ((v >> 16) & 255) / 255f;
            g = ((v >> 8) & 255) / 255f;
            b = (v & 255) / 255f;
        }
    }
}
