using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>
    /// How a chibi looks (art/chibi): one piece per slot (a hairstyle, a hat, animal ears or horns, a top, a bottom, shoes,
    /// an accessory, something on the back; null = none) and the colour of every region (<see cref="ChibiData.Regions"/>,
    /// sRGB like the brand's). Under a hat the hairstyle is pressed down (its "_Hat" or, under the cat hood, "_Hood"
    /// version), so a hat never has hair poking through. Build one with the fluent setters:
    /// <c>new ChibiLook { Hair = "Spiky", Top = "Jacket" }.Paint("hair", "#7A4526").Paint("top", "#2E6FE8")</c>.
    /// </summary>
    public sealed class ChibiLook
    {
        public string Name = "";
        /// <summary>Spiky, Fluffy, Shaggy, Buns, Short (or null: bald).</summary>
        public string Hair = "Spiky";
        /// <summary>Cap, CapBack, CapStar, Bucket, Crown, FrogHat, CatHood.</summary>
        public string Hat;
        /// <summary>CatEars, BunnyEars, BearEars, DevilHorns, UnicornHorn.</summary>
        public string Ears;
        /// <summary>Hoodie, HoodieStar, BearHoodie, ZipHoodie, Jacket, TrackJacket, Checkered, Tee, TeeStar, Polo.</summary>
        public string Top = "Tee";
        /// <summary>Shorts, Pants, Skirt.</summary>
        public string Bottom = "Shorts";
        /// <summary>Sneakers, HighTops.</summary>
        public string Shoes = "Sneakers";
        /// <summary>Glasses, RoundGlasses, HeartGlasses, Headphones, StarClip, Mask.</summary>
        public string Accessory;
        /// <summary>AngelWings, DevilWings, CatTail, DevilTail, RaccoonTail, BunnyTail.</summary>
        public string Back;

        /// <summary>The regions' colours (sRGB), by <see cref="ChibiData.Regions"/> index.</summary>
        public Color[] Colors = new Color[24];

        static readonly Dictionary<string, int> RegionIndex = new Dictionary<string, int>();

        /// <summary>How shiny each region is (the shader's gloss).</summary>
        static readonly Dictionary<string, float> Gloss = new Dictionary<string, float>
        {
            ["skin"] = 0.08f, ["blush"] = 0f, ["eyes"] = 0.9f, ["eye_shine"] = 0f, ["mouth"] = 0.1f, ["tongue"] = 0.3f,
            ["hair"] = 0.4f, ["hair_accent"] = 0.3f, ["top"] = 0.12f, ["top_accent"] = 0.15f, ["top_trim"] = 0.1f,
            ["bottom"] = 0.1f, ["bottom_accent"] = 0.1f, ["shoes"] = 0.45f, ["shoes_accent"] = 0.3f, ["soles"] = 0.15f,
            ["hat"] = 0.35f, ["hat_accent"] = 0.4f, ["ears"] = 0.2f, ["ears_inner"] = 0.1f, ["acc"] = 0.55f,
            ["acc_accent"] = 0.3f, ["back"] = 0.2f, ["back_accent"] = 0.2f,
        };

        public ChibiLook()
        {
            // the plain one of the reference sheet: white clothes, brown hair
            Paint("skin", "#FFD9C2").Paint("blush", "#FFAFA0").Paint("eyes", "#1B1726").Paint("eye_shine", "#FFFFFF")
                .Paint("mouth", "#4A1E2A").Paint("tongue", "#F2707A").Paint("hair", "#6B3D22").Paint("hair_accent", "#FF5A8A")
                .Paint("top", "#F7F5F9").Paint("top_accent", "#FFC93C").Paint("top_trim", "#E4E0EA")
                .Paint("bottom", "#F7F5F9").Paint("bottom_accent", "#E4E0EA").Paint("shoes", "#FFFFFF")
                .Paint("shoes_accent", "#EEEEF2").Paint("soles", "#E6E3EC").Paint("hat", "#E8403A").Paint("hat_accent", "#FFFFFF")
                .Paint("ears", "#FFFFFF").Paint("ears_inner", "#FFB3C7").Paint("acc", "#1B1726").Paint("acc_accent", "#3B7BE8")
                .Paint("back", "#FFFFFF").Paint("back_accent", "#CFE6FF");
        }

        static int Region(string name)
        {
            if (RegionIndex.Count == 0)
                for (int i = 0; i < ChibiData.Regions.Length; i++) RegionIndex[ChibiData.Regions[i]] = i;
            return RegionIndex.TryGetValue(name, out int r) ? r : -1;
        }

        /// <summary>Sets a region's colour (a hex string like the brand's, "#RRGGBB").</summary>
        public ChibiLook Paint(string region, string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return Paint(region, c);
        }

        public ChibiLook Paint(string region, Color c)
        {
            int r = Region(region);
            if (r >= 0) Colors[r] = c;
            else Debug.LogWarning($"[Chibi] No region {region}");
            return this;
        }

        public Color Color(string region) => Colors[Region(region)];

        public ChibiLook Copy()
        {
            var c = (ChibiLook)MemberwiseClone();
            c.Colors = (Color[])Colors.Clone();
            return c;
        }

        /// <summary>The pieces of the model to merge: the body, the face's expressions, and what is worn.</summary>
        public string[] Pieces()
        {
            var list = new List<string> { "Head", "Body", "Eyes", "EyesHappy", "Mouth", "MouthOpen", "MouthSad" };
            if (!string.IsNullOrEmpty(Hair))
            {
                string hair = $"Hair_{Hair}";
                if (Hat == "CatHood") hair += "_Hood";
                else if (!string.IsNullOrEmpty(Hat) && Hat != "Crown") hair += "_Hat";
                list.Add(hair);
            }
            foreach (var p in new[] { Hat, Ears, Top, Bottom, Shoes, Accessory, Back })
                if (!string.IsNullOrEmpty(p)) list.Add(p);
            return list.ToArray();
        }

        /// <summary>The shader's colour table: linear colour, gloss in alpha.</summary>
        public void Fill(Vector4[] regions)
        {
            for (int i = 0; i < ChibiData.Regions.Length && i < regions.Length; i++)
            {
                var l = Colors[i].linear;
                regions[i] = new Vector4(l.r, l.g, l.b, Gloss.TryGetValue(ChibiData.Regions[i], out float g) ? g : 0.1f);
            }
        }

        // ---------------------------------------------------------------- the reference sheet's characters

        /// <summary>Red cap worn backwards, a red track jacket with white stripes, navy shorts, red sneakers.</summary>
        public static ChibiLook CapKid() => new ChibiLook { Name = "CapKid", Hair = "Short", Hat = "CapBack", Top = "TrackJacket" }
            .Paint("hair", "#2A1A14").Paint("hat", "#E8403A").Paint("hat_accent", "#FFFFFF").Paint("top", "#E8403A")
            .Paint("top_accent", "#E8403A").Paint("top_trim", "#FFFFFF").Paint("bottom", "#24305E").Paint("bottom_accent", "#24305E")
            .Paint("shoes", "#E8403A").Paint("shoes_accent", "#FFFFFF").Paint("soles", "#F4F4F8");

        /// <summary>Pink fluffy hair under a white cat hood, a pink star hoodie, pink shorts and shoes.</summary>
        public static ChibiLook CatGirl() => new ChibiLook { Name = "CatGirl", Hair = "Fluffy", Hat = "CatHood", Top = "HoodieStar" }
            .Paint("hair", "#FF8FC0").Paint("hat", "#FFF6F8").Paint("hat_accent", "#FFB3C7").Paint("top", "#FF9AC4")
            .Paint("top_accent", "#FFFFFF").Paint("top_trim", "#FFE3EE").Paint("bottom", "#FF9AC4").Paint("bottom_accent", "#FFB8D6")
            .Paint("shoes", "#FFB8D6").Paint("shoes_accent", "#FFFFFF");

        /// <summary>Purple hair, horns, bat wings and a devil's tail, a black star tee, a purple skirt, high-tops.</summary>
        public static ChibiLook DevilGirl() => new ChibiLook
            {
                Name = "DevilGirl", Hair = "Shaggy", Ears = "DevilHorns", Back = "DevilWings", Top = "TeeStar", Bottom = "Skirt",
                Shoes = "HighTops",
            }
            .Paint("hair", "#A27BFF").Paint("ears", "#5B2E8C").Paint("ears_inner", "#2E1450").Paint("back", "#3A1E5C")
            .Paint("back_accent", "#7B4FB5").Paint("top", "#26203A").Paint("top_accent", "#A27BFF").Paint("top_trim", "#3A3054")
            .Paint("bottom", "#7B4FB5").Paint("bottom_accent", "#26203A").Paint("shoes", "#26203A").Paint("shoes_accent", "#FFFFFF");

        /// <summary>Brown spiky hair, blue headphones, a blue and black jacket, black pants, blue sneakers.</summary>
        public static ChibiLook HeadphonesKid() => new ChibiLook { Name = "HeadphonesKid", Hair = "Spiky", Accessory = "Headphones", Top = "Jacket", Bottom = "Pants" }
            .Paint("hair", "#7A4526").Paint("acc", "#2457C8").Paint("acc_accent", "#1B1726").Paint("top", "#2E6FE8")
            .Paint("top_accent", "#1E1A2C").Paint("top_trim", "#1E1A2C").Paint("bottom", "#1E1A2C").Paint("bottom_accent", "#1E1A2C")
            .Paint("shoes", "#2E6FE8").Paint("shoes_accent", "#FFFFFF");
    }
}
