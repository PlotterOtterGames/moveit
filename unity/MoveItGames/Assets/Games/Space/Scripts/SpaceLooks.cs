using UnityEngine;

namespace MoveIt.Games.Space
{
    /// <summary>
    /// The astronauts: the chibi (art/chibi) in its space suit, padded trousers, boots and jetpack (the game adds the glass
    /// helmet). Player 1 wears the suit chosen in the menu (<see cref="Suits"/>, unlocked by stars), player 2 the sky-blue
    /// one, like player 2 in the other games.
    /// </summary>
    public static class SpaceLooks
    {
        /// <summary>A suit's colours: the suit, its accents (collar, cuffs, knee pads, buttons), the boots' soles, the
        /// jetpack and its metal.</summary>
        public struct Suit
        {
            public string Body, Accent, Trim, Pack, PackMetal;
        }

        public static readonly Suit[] Suits =
        {
            new Suit { Body = "#F4F2F8", Accent = "#FF5A4E", Trim = "#B9BDCC", Pack = "#DDE1EC", PackMetal = "#5A6178" },
            new Suit { Body = "#BFE9FF", Accent = "#2E6FE8", Trim = "#F4F2F8", Pack = "#F4F2F8", PackMetal = "#2E6FE8" },
            new Suit { Body = "#B9F0B4", Accent = "#3FA650", Trim = "#FFC93C", Pack = "#FFF1D6", PackMetal = "#3FA650" },
            new Suit { Body = "#D9C8FF", Accent = "#7B61FF", Trim = "#FF8FB0", Pack = "#F4F2F8", PackMetal = "#7B61FF" },
            new Suit { Body = "#FFE08A", Accent = "#FF5A4E", Trim = "#FFFFFF", Pack = "#FFC93C", PackMetal = "#B44E2E" },
            new Suit { Body = "#2A2F66", Accent = "#FFC93C", Trim = "#5A6178", Pack = "#3A3E5C", PackMetal = "#FFC93C" },
        };

        /// <summary>Player 1: short dark hair (pressed flat under the helmet, like under a hat: it fits the glass), the chosen suit.</summary>
        public static ChibiLook Player(int suit) => Dress(new ChibiLook { Name = "Astronaut", Hair = "Short_Hat" }.Paint("hair", "#2A1A14"), Suits[Mathf.Clamp(suit, 0, Suits.Length - 1)]);

        /// <summary>Player 2: brown hair in buns, the sky-blue suit.</summary>
        public static ChibiLook Player2() => Dress(new ChibiLook { Name = "Astronaut2", Hair = "Buns_Hat" }.Paint("hair", "#6B3D22").Paint("hair_accent", "#31C6F4"), Suits[1]);

        public static Color Accent(int suit) => Hex(Suits[Mathf.Clamp(suit, 0, Suits.Length - 1)].Accent);

        static ChibiLook Dress(ChibiLook look, Suit s)
        {
            look.Top = "SpaceSuit";
            look.Bottom = "SpacePants";
            look.Shoes = "SpaceBoots";
            look.Back = "Jetpack";
            return look.Paint("top", s.Body).Paint("top_accent", s.Accent).Paint("top_trim", s.Trim)
                .Paint("bottom", s.Body).Paint("bottom_accent", s.Accent)
                .Paint("shoes", s.Body).Paint("shoes_accent", s.Accent).Paint("soles", "#3A3E5C")
                .Paint("back", s.Pack).Paint("back_accent", s.PackMetal);
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
    }
}
