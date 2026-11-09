using MoveIt.Games.Tennis;
using UnityEngine;

namespace MoveIt.Games.Kart
{
    /// <summary>A kart's paint: the colours of art/kart's regions (Tundu/Athlete's table, the kart's own meaning).</summary>
    public struct KartPaint
    {
        public Color Body, Accent, Seat;

        public KartPaint(string body, string accent, string seat = "#24305E")
        {
            ColorUtility.TryParseHtmlString(body, out Body);
            ColorUtility.TryParseHtmlString(accent, out Accent);
            ColorUtility.TryParseHtmlString(seat, out Seat);
        }
    }

    /// <summary>
    /// Who races Gara! and in what: the chibis (the same cast as Tenis!: <see cref="TennisLooks"/>) and their karts' paint.
    /// The player drives a red kart (the red-cap kid), player 2 a sky-blue one (the headphones kid); each of the computer's
    /// ten drivers has their own colour, none red or sky blue, so the players' karts always stand out.
    /// </summary>
    public static class KartLooks
    {
        // art/kart/kart.py's regions, in order
        public const int Body = 0, Accent = 1, Trim = 2, Seat = 3, Dark = 4, Metal = 5, Tyre = 6, Rim = 7, Lights = 8, Plate = 9, Hub = 10,
                         Glow = 11, Regions = 12;

        public static ChibiLook Player() => TennisLooks.Player();
        public static ChibiLook Player2() => ChibiLook.HeadphonesKid().Paint("top", "#31C6F4").Paint("acc", "#FFFFFF").Paint("acc_accent", "#1E8FC0")
            .Paint("shoes", "#31C6F4");

        public static KartPaint PlayerPaint => new KartPaint("#E8403A", "#FFC93C");
        public static KartPaint Player2Paint => new KartPaint("#31C6F4", "#FFFFFF", "#1E2A4A");

        /// <summary>The computer's driver <paramref name="index"/> (<see cref="KartLevel.Names"/>: Tenis!'s cast).</summary>
        public static ChibiLook Driver(int index) => TennisLooks.Opponent(Wrap(index));

        static readonly KartPaint[] DriverPaints =
        {
            new KartPaint("#FFC93C", "#FF5A4E"),            // Ana: sun yellow
            new KartPaint("#7B61FF", "#FFC93C"),            // Beni: grape
            new KartPaint("#5DB04E", "#FFFFFF"),            // Era: leaf
            new KartPaint("#FF8A3D", "#24305E"),            // Dini: orange
            new KartPaint("#FF5AA8", "#FFFFFF"),            // Lea: pink
            new KartPaint("#2EC4B6", "#FFC93C"),            // Toni: teal
            new KartPaint("#2E6FE8", "#FFC93C", "#F4F4F8"), // Mira: blue
            new KartPaint("#8C5A2E", "#FFC93C"),            // Leka: chocolate
            new KartPaint("#B9E86A", "#7B61FF"),            // Nora: lime
            new KartPaint("#C9A4FF", "#FFFFFF"),            // Gent: lilac
        };

        public static KartPaint DriverPaint(int index) => DriverPaints[Wrap(index)];

        static int Wrap(int i) => ((i % KartLevel.Drivers) + KartLevel.Drivers) % KartLevel.Drivers;

        /// <summary>The kart's colour table (linear colour, gloss in alpha) for Tundu/Athlete.</summary>
        public static void Fill(KartPaint p, Vector4[] table)
        {
            void Set(int region, Color c, float gloss) => table[region] = new Vector4(c.linear.r, c.linear.g, c.linear.b, gloss);
            Set(Body, p.Body, 0.85f);
            Set(Accent, p.Accent, 0.7f);
            Set(Trim, Color.white, 0.4f);
            Set(Seat, p.Seat, 0.25f);
            Set(Dark, new Color(0.165f, 0.176f, 0.243f), 0.25f);
            Set(Metal, new Color(0.73f, 0.74f, 0.8f), 0.95f);
            Set(Tyre, new Color(0.15f, 0.16f, 0.2f), 0.06f);
            Set(Rim, new Color(0.96f, 0.96f, 0.97f), 0.6f);
            Set(Lights, new Color(1f, 0.97f, 0.8f), 1f);
            Set(Plate, Color.white, 0.3f);
            Set(Hub, p.Accent, 0.8f);
            Set(Glow, new Color(1f, 0.54f, 0.24f), 0f);
        }
    }
}
