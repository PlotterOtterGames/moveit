using UnityEngine;

namespace MoveIt.Games.Sports
{
    /// <summary>The garments an athlete wears (pieces of the model, see art/sports/athlete.py).</summary>
    public enum Wear
    {
        /// <summary>A football shirt with sleeves, shorts, long socks, boots.</summary>
        Football,
        /// <summary>A vest, short running shorts, short socks, spikes, a headband if wanted.</summary>
        Track,
        /// <summary>A vest, shorts, bare feet.</summary>
        Beach,
    }

    /// <summary>
    /// How an athlete looks: the colour of every region the model has (<see cref="AthleteData.Regions"/>), the kit, a
    /// hairstyle, gloves, a headband. Colours are sRGB like the brand's; <see cref="Athlete"/> converts them.
    /// </summary>
    public sealed class AthleteLook
    {
        public Color Skin = new Color(0.95f, 0.78f, 0.63f);
        public Color Hair = new Color(0.29f, 0.18f, 0.12f);
        public Color Shirt = new Color(0.19f, 0.78f, 0.96f), ShirtAccent = Color.white;
        public Color Shorts = Color.white, ShortsAccent = new Color(0.19f, 0.78f, 0.96f);
        public Color Socks = new Color(0.19f, 0.78f, 0.96f), SocksAccent = Color.white;
        public Color Shoes = new Color(1f, 0.35f, 0.31f), Soles = Color.white;
        public Color Gloves = new Color(1f, 0.79f, 0.24f), GlovesAccent = new Color(0.08f, 0.1f, 0.22f);
        public Color Headband = new Color(1f, 0.35f, 0.31f);
        public Wear Wear = Wear.Football;
        public string HairStyle = "Crop";
        public bool WearsGloves, WearsHeadband;

        public static readonly string[] HairStyles = { "Crop", "Buzz", "Spiky", "Afro", "Ponytail", "Bun", "Bob", "Pigtails" };

        /// <summary>Skin tones from light to dark (Albania's families are mostly the lighter half).</summary>
        public static readonly Color[] SkinTones =
        {
            new Color(0.98f, 0.84f, 0.72f), new Color(0.95f, 0.78f, 0.63f), new Color(0.91f, 0.71f, 0.56f),
            new Color(0.84f, 0.62f, 0.46f), new Color(0.71f, 0.5f, 0.36f), new Color(0.55f, 0.36f, 0.24f),
        };

        public static readonly Color[] HairColours =
        {
            new Color(0.14f, 0.09f, 0.07f), new Color(0.24f, 0.15f, 0.1f), new Color(0.36f, 0.22f, 0.13f),
            new Color(0.55f, 0.33f, 0.17f), new Color(0.82f, 0.62f, 0.33f), new Color(0.72f, 0.38f, 0.2f),
        };

        public AthleteLook Copy() => (AthleteLook)MemberwiseClone();

        /// <summary>A team's shirt, shorts and socks (accents white unless given).</summary>
        public AthleteLook Team(Color shirt, Color shorts, Color socks, Color? accent = null)
        {
            Shirt = shirt;
            Shorts = shorts;
            Socks = socks;
            ShirtAccent = SocksAccent = accent ?? Color.white;
            ShortsAccent = shirt;
            return this;
        }

        /// <summary>A person of their own (skin, hair, style) from a seed: the same seed, the same person.</summary>
        public static AthleteLook Person(int seed, Wear wear, string[] styles = null)
        {
            var rng = new System.Random(seed * 7919 + 17);
            styles ??= HairStyles;
            var look = new AthleteLook
            {
                Wear = wear,
                Skin = SkinTones[rng.Next(SkinTones.Length)],
                Hair = HairColours[rng.Next(HairColours.Length)],
                HairStyle = styles[rng.Next(styles.Length)],
            };
            if (look.HairStyle == "Afro" && look.Hair.r > 0.5f) look.Hair = HairColours[1];
            return look;
        }

        /// <summary>The region colours for the shader: linear rgb, gloss in alpha, in <see cref="AthleteData.Regions"/>
        /// order.</summary>
        public void Fill(Vector4[] regions)
        {
            Color blush = Color.Lerp(Skin, new Color(1f, 0.5f, 0.48f), 0.45f);
            Set(regions, 0, Skin, 0.06f);
            Set(regions, 1, Shirt, 0.10f);
            Set(regions, 2, ShirtAccent, 0.10f);
            Set(regions, 3, Shorts, 0.10f);
            Set(regions, 4, ShortsAccent, 0.10f);
            Set(regions, 5, Socks, 0.06f);
            Set(regions, 6, SocksAccent, 0.06f);
            Set(regions, 7, Shoes, 0.45f);
            Set(regions, 8, Soles, 0.15f);
            Set(regions, 9, Hair, 0.35f);
            Set(regions, 10, new Color(0.11f, 0.09f, 0.19f), 0.9f);
            Set(regions, 11, Color.white, 0f);
            Set(regions, 12, new Color(0.45f, 0.17f, 0.18f), 0.1f);
            Set(regions, 13, new Color(0.34f, 0.1f, 0.15f), 0.1f);
            Set(regions, 14, new Color(0.11f, 0.09f, 0.19f), 0.1f);
            Set(regions, 15, blush, 0.05f);
            Set(regions, 16, Gloves, 0.25f);
            Set(regions, 17, GlovesAccent, 0.2f);
            Set(regions, 18, Headband, 0.08f);
            Set(regions, 19, new Color(0.94f, 0.46f, 0.47f), 0.3f);
        }

        static void Set(Vector4[] regions, int i, Color c, float gloss)
        {
            var l = c.linear;
            regions[i] = new Vector4(l.r, l.g, l.b, gloss);
        }

        /// <summary>The model's pieces this look wears.</summary>
        public string[] Pieces()
        {
            var list = new System.Collections.Generic.List<string> { "Body", "Face", "Shirt", WearsGloves ? "Gloves" : "Hands" };
            switch (Wear)
            {
                case Wear.Football:
                    list.Add("Sleeves");
                    list.Add("Shorts");
                    list.Add("SocksLong");
                    list.Add("Shoes");
                    break;
                case Wear.Track:
                    list.Add("RunShorts");
                    list.Add("SocksShort");
                    list.Add("Shoes");
                    break;
                case Wear.Beach:
                    list.Add("Shorts");
                    list.Add("BareFeet");
                    break;
            }
            if (WearsHeadband) list.Add("Headband");
            if (!string.IsNullOrEmpty(HairStyle)) list.Add("Hair_" + HairStyle);
            return list.ToArray();
        }
    }
}
