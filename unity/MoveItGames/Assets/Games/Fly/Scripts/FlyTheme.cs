using UnityEngine;

namespace MoveIt.Games.Fly
{
    /// <summary>
    /// The time of day of a district: every district saved moves the clock on (morning, golden afternoon, sunset, a
    /// violet evening, dawn, and round again), so an endless run keeps looking new (a runner's "reward" moment). It tints
    /// the sky, the sun, the fog, the ambient light and what the city's glass reflects; <see cref="Apply"/> blends from
    /// one to the next.
    /// </summary>
    public struct FlyTheme
    {
        public Color SkyTop, SkyHorizon, SkyBottom, SunGlow, Light, Ambient, Shade;
        public float LightIntensity;

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static readonly FlyTheme[] Day =
        {
            // morning (the scene as built)
            new FlyTheme { SkyTop = Hex("#5BB8F2"), SkyHorizon = Hex("#D2EEF6"), SkyBottom = Hex("#BDE3DC"), SunGlow = Hex("#FFEDB3"),
                           Light = new Color(1f, 0.96f, 0.88f), LightIntensity = 1f, Ambient = new Color(0.62f, 0.66f, 0.74f),
                           Shade = new Color(0.64f, 0.66f, 0.88f) },
            // golden afternoon
            new FlyTheme { SkyTop = Hex("#4E9FE6"), SkyHorizon = Hex("#FBE6B8"), SkyBottom = Hex("#E8D3B0"), SunGlow = Hex("#FFD27A"),
                           Light = new Color(1f, 0.88f, 0.68f), LightIntensity = 1.05f, Ambient = new Color(0.66f, 0.63f, 0.64f),
                           Shade = new Color(0.72f, 0.62f, 0.8f) },
            // sunset
            new FlyTheme { SkyTop = Hex("#6B6FD1"), SkyHorizon = Hex("#FFB48C"), SkyBottom = Hex("#E59A8C"), SunGlow = Hex("#FF8F5A"),
                           Light = new Color(1f, 0.72f, 0.52f), LightIntensity = 0.95f, Ambient = new Color(0.62f, 0.52f, 0.64f),
                           Shade = new Color(0.62f, 0.5f, 0.76f) },
            // violet evening (not dark: still a toy city)
            new FlyTheme { SkyTop = Hex("#3E4499"), SkyHorizon = Hex("#C9A0DA"), SkyBottom = Hex("#9C86C4"), SunGlow = Hex("#F2A5D6"),
                           Light = new Color(0.82f, 0.78f, 1f), LightIntensity = 0.82f, Ambient = new Color(0.5f, 0.5f, 0.7f),
                           Shade = new Color(0.48f, 0.48f, 0.78f) },
            // dawn
            new FlyTheme { SkyTop = Hex("#80B6F0"), SkyHorizon = Hex("#FFD9E2"), SkyBottom = Hex("#E8C9D6"), SunGlow = Hex("#FFC7A8"),
                           Light = new Color(1f, 0.87f, 0.86f), LightIntensity = 0.95f, Ambient = new Color(0.64f, 0.62f, 0.72f),
                           Shade = new Color(0.68f, 0.62f, 0.86f) },
        };

        /// <summary>District 1, 2, 3 ... → morning, afternoon, sunset, evening, dawn, morning ...</summary>
        public static FlyTheme For(int district) => Day[(Mathf.Max(1, district) - 1) % Day.Length];

        public static FlyTheme Lerp(FlyTheme a, FlyTheme b, float t) => new FlyTheme
        {
            SkyTop = Color.Lerp(a.SkyTop, b.SkyTop, t), SkyHorizon = Color.Lerp(a.SkyHorizon, b.SkyHorizon, t),
            SkyBottom = Color.Lerp(a.SkyBottom, b.SkyBottom, t), SunGlow = Color.Lerp(a.SunGlow, b.SunGlow, t),
            Light = Color.Lerp(a.Light, b.Light, t), LightIntensity = Mathf.Lerp(a.LightIntensity, b.LightIntensity, t),
            Ambient = Color.Lerp(a.Ambient, b.Ambient, t), Shade = Color.Lerp(a.Shade, b.Shade, t),
        };

        /// <summary>Sets the sky (a copy of the scene's, made by the game), the sun, fog, ambient and the city's material.</summary>
        public void Apply(Material sky, Light sun, Material city)
        {
            if (sky != null)
            {
                sky.SetColor("_TopColor", SkyTop);
                sky.SetColor("_HorizonColor", SkyHorizon);
                sky.SetColor("_BottomColor", SkyBottom);
                sky.SetColor("_SunColor", SunGlow);
            }
            if (sun != null)
            {
                sun.color = Light;
                sun.intensity = LightIntensity;
            }
            RenderSettings.fogColor = SkyHorizon;
            RenderSettings.ambientLight = Ambient;
            if (city != null)
            {
                city.SetColor("_SkyTop", SkyTop);
                city.SetColor("_SkyHorizon", SkyHorizon);
                city.SetColor("_ShadeColor", Shade);
            }
        }
    }
}
