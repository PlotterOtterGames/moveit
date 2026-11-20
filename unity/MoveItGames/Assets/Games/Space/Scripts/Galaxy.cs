using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Space
{
    /// <summary>A colour without the engine (the galaxy is generated in tests too): 0..1 each.</summary>
    public struct Rgb
    {
        public float R, G, B;
        public Rgb(float r, float g, float b) { R = r; G = g; B = b; }

        /// <summary>A colour from hue (0..1, wrapping), saturation and value.</summary>
        public static Rgb Hsv(float h, float s, float v)
        {
            h = (h % 1f + 1f) % 1f;
            s = Mathf.Clamp01(s);
            v = Mathf.Clamp01(v);
            float c = v * s, x = c * (1f - Mathf.Abs(h * 6f % 2f - 1f)), m = v - c;
            float r, g, b;
            switch ((int)(h * 6f) % 6)
            {
                case 0: r = c; g = x; b = 0f; break;
                case 1: r = x; g = c; b = 0f; break;
                case 2: r = 0f; g = c; b = x; break;
                case 3: r = 0f; g = x; b = c; break;
                case 4: r = x; g = 0f; b = c; break;
                default: r = c; g = 0f; b = x; break;
            }
            return new Rgb(r + m, g + m, b + m);
        }

        public static Rgb Lerp(Rgb a, Rgb b, float k) => new Rgb(a.R + (b.R - a.R) * k, a.G + (b.G - a.G) * k, a.B + (b.B - a.B) * k);
    }

    public enum PlanetType { Home, Ocean, Continents, Desert, Ice, Lava, Gas, Jungle, Crystal, Crater, Toxic, Candy, Rainbow }

    public enum StarKind { Yellow, Orange, Red, White, Blue }

    /// <summary>How a planet looks (the drawing makes its colour ramp and material from it): one of the shared height
    /// maps turned and maybe mirrored, a colour ramp over its heights (alpha = glow), oceans, clouds, rings, moons.</summary>
    public sealed class PlanetLook
    {
        /// <summary>Which height map (Assets/Games/Space/Art/height_*.png: continents, islands, craters, cells, swirls,
        /// dunes, bands, spots), turned by <see cref="Shift"/> of its width, mirrored when <see cref="Flip"/>.</summary>
        public int Height;
        public float Shift;
        public bool Flip;
        /// <summary>The colour ramp: keys (height 0..1, colour, glow 0..1), from the lowest to the highest.</summary>
        public readonly List<(float at, Rgb colour, float glow)> Ramp = new List<(float, Rgb, float)>();
        /// <summary>Heights below this are water (glossy).</summary>
        public float Sea = -1f;
        /// <summary>Gas giant bands (0 = none .. 1 = all bands): the latitude drawn through the ramp.</summary>
        public float Bands;
        public Rgb Atmosphere;
        public float AtmosphereStrength = 1f;
        /// <summary>Clouds: how much they show (0 = none), their tint.</summary>
        public float Clouds;
        public Rgb CloudTint = new Rgb(1f, 1f, 1f);
        public bool Rings;
        public Rgb RingTint;
        public float RingTilt;
        /// <summary>Moons: their sizes (a share of the planet's radius).</summary>
        public readonly List<float> Moons = new List<float>();
        /// <summary>How fast it turns (map widths a second).</summary>
        public float Spin;
        /// <summary>Its main colour (the map's dots, the card).</summary>
        public Rgb Main;
    }

    public sealed class Planet
    {
        public int Id, System, IndexInSystem;
        public string Name;
        public PlanetType Type;
        /// <summary>Rare finds get a mark on their card.</summary>
        public bool Rare;
        /// <summary>Its radius (m), its orbit round the star (m), where on it (radians), how high over the plane (m).</summary>
        public float Radius, Orbit, Angle, Lift;
        public PlanetLook Look;

        /// <summary>Its place in its system (the star at the origin; x across, y along: the flight's plane).</summary>
        public Vector2 Position => new Vector2(Mathf.Cos(Angle), Mathf.Sin(Angle)) * Orbit;
    }

    public sealed class StarSystem
    {
        public int Id;
        public string Name;
        /// <summary>Where on the galaxy's map (the disc of radius 1, the heart at the middle).</summary>
        public Vector2 Map;
        public StarKind Star;
        public float StarRadius;
        /// <summary>The sky's turn (degrees) and tint hue for this system: every system feels its own.</summary>
        public float SkyTurn, SkyHue;
        /// <summary>An asteroid belt round the star at this radius (m; 0 = none): between two planets' orbits.</summary>
        public float Belt;
        public readonly List<int> Links = new List<int>();
        public readonly List<Planet> Planets = new List<Planet>();
        /// <summary>How far out the system goes (m): flying past it turns you back.</summary>
        public float Edge;
    }

    /// <summary>
    /// Yjet!'s galaxy (2026-11-14, the exploration rework; user: "a No Man's Sky game: just explore and find planets;
    /// unique planets, many, beautiful without much manual work; very flowing from planet to planet; finished when all are
    /// found; some hours; a sense of a map"): star systems along a spiral galaxy's two arms, linked to their neighbours
    /// (the map's routes), each with 3-6 planets generated from one seed: a type by the star and the orbit (hot ones
    /// inside, gas giants and ice outside, now and then a candy, crystal or rare rainbow world), a name, a look (one of
    /// eight height maps, a colour ramp from the type's palette shifted in hue, clouds, rings, moons). The same seed gives
    /// the same galaxy on every box. Pure logic (tests): the drawing turns <see cref="PlanetLook"/> into materials.
    /// </summary>
    public sealed class Galaxy
    {
        public const int DefaultSeed = 20261114;
        public const int Systems = 80;
        public readonly List<StarSystem> All = new List<StarSystem>();
        public readonly List<Planet> Planets = new List<Planet>();
        /// <summary>Where the trip starts: the outer end of an arm (the galaxy's heart is the way on).</summary>
        public int Home { get; private set; }
        public int Seed { get; }

        readonly System.Random _rng;
        readonly HashSet<string> _names = new HashSet<string>();

        public Galaxy(int seed = DefaultSeed)
        {
            Seed = seed;
            _rng = new System.Random(seed);
            Place();
            Link();
            for (int i = 0; i < All.Count; i++) Fill(All[i], i == Home);
        }

        float R() => (float)_rng.NextDouble();
        float R(float a, float b) => a + (b - a) * R();

        // ------------------------------------------------------------------------------------------------- the map

        /// <summary>The systems along two spiral arms (a log spiral, a little scatter), never too close to each other.</summary>
        void Place()
        {
            var spots = new List<Vector2>();
            int tries = 0;
            while (spots.Count < Systems && tries++ < 20000)
            {
                int arm = spots.Count % 2;
                float t = R();
                float radius = 0.16f + 0.8f * t;
                float angle = arm * Mathf.PI + t * 4.2f + R(-0.28f, 0.28f);
                var p = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                p += new Vector2(R(-1f, 1f), R(-1f, 1f)) * 0.035f;
                bool near = false;
                foreach (var q in spots)
                    if (Vector2.Distance(p, q) < 0.088f) near = true;
                if (!near) spots.Add(p);
            }
            for (int i = 0; i < spots.Count; i++)
            {
                var kind = (StarKind)Pick(new[] { 0.34f, 0.24f, 0.16f, 0.14f, 0.12f });
                All.Add(new StarSystem
                {
                    Id = i, Map = spots[i], Star = kind, Name = Unique(StarName),
                    StarRadius = kind switch { StarKind.Red => 90f, StarKind.Orange => 110f, StarKind.Yellow => 125f, StarKind.White => 140f, _ => 160f },
                    SkyTurn = R(0f, 360f), SkyHue = R(-0.18f, 0.18f), Belt = R() < 0.35f ? 1f : 0f,
                });
            }
            // the trip starts at the outermost system
            float far = -1f;
            foreach (var s in All)
                if (s.Map.magnitude > far)
                {
                    far = s.Map.magnitude;
                    Home = s.Id;
                }
        }

        /// <summary>Routes: every system to its two nearest neighbours, then the nearest pair between parts until all join.</summary>
        void Link()
        {
            foreach (var s in All)
            {
                var near = new List<StarSystem>(All);
                near.Remove(s);
                near.Sort((a, b) => Vector2.Distance(a.Map, s.Map).CompareTo(Vector2.Distance(b.Map, s.Map)));
                for (int k = 0; k < 2 && k < near.Count; k++) Join(s, near[k]);
            }
            while (true)
            {
                var part = Reach(0);
                if (part.Count == All.Count) break;
                StarSystem a = null, b = null;
                float best = float.MaxValue;
                foreach (var x in All)
                {
                    if (!part.Contains(x.Id)) continue;
                    foreach (var y in All)
                    {
                        if (part.Contains(y.Id)) continue;
                        float d = Vector2.Distance(x.Map, y.Map);
                        if (d < best)
                        {
                            best = d;
                            a = x;
                            b = y;
                        }
                    }
                }
                Join(a, b);
            }
        }

        static void Join(StarSystem a, StarSystem b)
        {
            if (a == b || a.Links.Contains(b.Id)) return;
            a.Links.Add(b.Id);
            b.Links.Add(a.Id);
        }

        /// <summary>The systems reachable from <paramref name="from"/> along routes.</summary>
        public HashSet<int> Reach(int from)
        {
            var seen = new HashSet<int> { from };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
                foreach (int n in All[queue.Dequeue()].Links)
                    if (seen.Add(n)) queue.Enqueue(n);
            return seen;
        }

        // ------------------------------------------------------------------------------------------------- planets

        void Fill(StarSystem s, bool home)
        {
            int count = 3 + Pick(new[] { 0.25f, 0.35f, 0.25f, 0.15f });
            // orbits spaced out from the star (each planet on its own ring), angles spread round it
            float orbit = s.StarRadius * 3.2f + R(100f, 180f);
            float angle = R(0f, Mathf.PI * 2f);
            for (int k = 0; k < count; k++)
            {
                float u = (float)k / Mathf.Max(1, count - 1);
                var type = home && k == 1 ? PlanetType.Home : TypeFor(s, u);
                float radius = type switch
                {
                    PlanetType.Gas => R(130f, 200f),
                    PlanetType.Rainbow => R(115f, 170f),
                    PlanetType.Crater => R(40f, 64f),
                    PlanetType.Home => 96f,
                    _ => R(56f, 110f),
                };
                // skip round by 70-160 degrees each time: planets on all sides of the star
                angle += R(1.2f, 2.8f);
                var p = new Planet
                {
                    Id = Planets.Count, System = s.Id, IndexInSystem = k, Type = type, Radius = radius,
                    Orbit = orbit + radius, Angle = angle, Lift = R(-60f, 60f),
                    Rare = type == PlanetType.Rainbow,
                };
                orbit += radius * 2f + R(110f, 200f);
                p.Name = type == PlanetType.Home ? "Toka e Vogël" : Unique(PlanetName);
                p.Look = Looks(type);
                // rare rings on a planet that usually has none
                if (!p.Look.Rings && type != PlanetType.Home && R() < 0.1f)
                {
                    p.Look.Rings = true;
                    p.Rare = true;
                }
                s.Planets.Add(p);
                Planets.Add(p);
            }
            s.Edge = orbit + 420f;
            // an asteroid belt (some systems) in the gap between two orbits
            if (s.Belt > 0f && s.Planets.Count > 1)
            {
                int k = _rng.Next(s.Planets.Count - 1);
                var a = s.Planets[k];
                var b = s.Planets[k + 1];
                s.Belt = ((a.Orbit + a.Radius) + (b.Orbit - b.Radius)) * 0.5f;
            }
            else s.Belt = 0f;
        }

        PlanetType TypeFor(StarSystem s, float u)
        {
            // a few worlds anywhere: candy, crystal, toxic, the rare rainbow
            float fun = R();
            if (fun < 0.035f) return PlanetType.Rainbow;
            if (fun < 0.11f) return PlanetType.Candy;
            if (fun < 0.18f) return PlanetType.Crystal;
            // hot stars make more of the inner worlds hot, cool ones more of them ice
            float warmth = s.Star switch { StarKind.Blue => 0.25f, StarKind.White => 0.12f, StarKind.Red => -0.2f, StarKind.Orange => -0.06f, _ => 0f };
            float zone = u - warmth + R(-0.18f, 0.18f);
            if (zone < 0.18f) return Pick(new[] { PlanetType.Lava, PlanetType.Desert, PlanetType.Crater }, new[] { 0.5f, 0.3f, 0.2f });
            if (zone < 0.42f) return Pick(new[] { PlanetType.Desert, PlanetType.Continents, PlanetType.Toxic, PlanetType.Crater }, new[] { 0.18f, 0.32f, 0.25f, 0.25f });
            if (zone < 0.66f) return Pick(new[] { PlanetType.Continents, PlanetType.Ocean, PlanetType.Jungle, PlanetType.Crater }, new[] { 0.25f, 0.3f, 0.3f, 0.15f });
            if (zone < 0.85f) return Pick(new[] { PlanetType.Gas, PlanetType.Ice, PlanetType.Ocean }, new[] { 0.45f, 0.3f, 0.25f });
            return Pick(new[] { PlanetType.Ice, PlanetType.Gas, PlanetType.Crater }, new[] { 0.4f, 0.4f, 0.2f });
        }

        int Pick(float[] weights)
        {
            float total = 0f;
            foreach (float w in weights) total += w;
            float x = R() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                x -= weights[i];
                if (x <= 0f) return i;
            }
            return weights.Length - 1;
        }

        T Pick<T>(T[] items, float[] weights) => items[Pick(weights)];

        /// <summary>A type's look: its height map, its palette with a hue of its own, its sky and extras.</summary>
        PlanetLook Looks(PlanetType type)
        {
            var l = new PlanetLook { Shift = R(), Flip = R() < 0.5f, Spin = R(0.002f, 0.007f) };
            float hue = R(-1f, 1f);
            void Key(float at, Rgb c, float glow = 0f) => l.Ramp.Add((at, c, glow));
            switch (type)
            {
                case PlanetType.Home:
                    l.Height = 0;
                    l.Sea = 0.5f;
                    Key(0f, Rgb.Hsv(0.6f, 0.85f, 0.4f)); Key(0.45f, Rgb.Hsv(0.56f, 0.7f, 0.7f)); Key(0.5f, Rgb.Hsv(0.14f, 0.35f, 0.9f));
                    Key(0.54f, Rgb.Hsv(0.28f, 0.6f, 0.62f)); Key(0.72f, Rgb.Hsv(0.24f, 0.5f, 0.5f)); Key(0.86f, Rgb.Hsv(0.08f, 0.35f, 0.62f)); Key(0.95f, new Rgb(0.97f, 0.97f, 1f));
                    l.Atmosphere = Rgb.Hsv(0.56f, 0.5f, 1f);
                    l.Clouds = 0.85f;
                    l.Moons.Add(0.27f);
                    break;
                case PlanetType.Ocean:
                {
                    l.Height = R() < 0.5f ? 1 : 0;
                    l.Sea = R(0.66f, 0.78f);
                    float h = 0.58f + hue * 0.1f;
                    Key(0f, Rgb.Hsv(h, 0.9f, 0.3f)); Key(l.Sea - 0.18f, Rgb.Hsv(h - 0.03f, 0.8f, 0.55f)); Key(l.Sea - 0.02f, Rgb.Hsv(h - 0.08f, 0.55f, 0.85f));
                    Key(l.Sea, Rgb.Hsv(0.13f, 0.35f, 0.92f)); Key(l.Sea + 0.06f, Rgb.Hsv(0.3f + hue * 0.08f, 0.55f, 0.65f)); Key(1f, Rgb.Hsv(0.25f, 0.35f, 0.8f));
                    l.Atmosphere = Rgb.Hsv(h, 0.45f, 1f);
                    l.Clouds = R(0.45f, 0.9f);
                    break;
                }
                case PlanetType.Continents:
                {
                    l.Height = 0;
                    l.Sea = R(0.42f, 0.55f);
                    float land = 0.28f + hue * 0.1f;
                    Key(0f, Rgb.Hsv(0.6f + hue * 0.05f, 0.85f, 0.38f)); Key(l.Sea - 0.04f, Rgb.Hsv(0.55f, 0.7f, 0.7f)); Key(l.Sea, Rgb.Hsv(0.13f, 0.35f, 0.9f));
                    Key(l.Sea + 0.05f, Rgb.Hsv(land, 0.6f, 0.62f)); Key(l.Sea + 0.25f, Rgb.Hsv(land - 0.12f, 0.45f, 0.55f)); Key(0.88f, Rgb.Hsv(0.07f, 0.3f, 0.6f));
                    Key(0.94f, new Rgb(0.96f, 0.96f, 1f));
                    l.Atmosphere = Rgb.Hsv(0.56f + hue * 0.05f, 0.45f, 1f);
                    l.Clouds = R(0.5f, 0.85f);
                    break;
                }
                case PlanetType.Desert:
                {
                    l.Height = R() < 0.65f ? 5 : 4;
                    float h = 0.07f + hue * 0.05f;
                    Key(0f, Rgb.Hsv(h - 0.02f, 0.7f, 0.42f)); Key(0.35f, Rgb.Hsv(h, 0.65f, 0.7f)); Key(0.7f, Rgb.Hsv(h + 0.03f, 0.5f, 0.88f)); Key(1f, Rgb.Hsv(h + 0.05f, 0.25f, 0.96f));
                    l.Atmosphere = Rgb.Hsv(h + 0.02f, 0.5f, 1f);
                    l.AtmosphereStrength = 0.8f;
                    l.Clouds = R() < 0.3f ? 0.3f : 0f;
                    break;
                }
                case PlanetType.Ice:
                {
                    l.Height = R() < 0.5f ? 3 : 6;
                    float h = 0.55f + hue * 0.06f;
                    Key(0f, Rgb.Hsv(h + 0.02f, 0.7f, 0.5f)); Key(0.3f, Rgb.Hsv(h, 0.45f, 0.82f)); Key(0.6f, Rgb.Hsv(h - 0.02f, 0.18f, 0.96f)); Key(1f, new Rgb(1f, 1f, 1f));
                    l.Atmosphere = Rgb.Hsv(h, 0.35f, 1f);
                    l.Clouds = R() < 0.4f ? 0.4f : 0f;
                    break;
                }
                case PlanetType.Lava:
                {
                    l.Height = 3;
                    float h = 0.03f + hue * 0.03f;
                    Key(0f, Rgb.Hsv(h + 0.08f, 0.9f, 1f), 1f); Key(0.1f, Rgb.Hsv(h, 0.95f, 0.95f), 0.8f); Key(0.2f, Rgb.Hsv(h, 0.8f, 0.3f), 0.1f);
                    Key(0.5f, Rgb.Hsv(0.02f, 0.4f, 0.18f)); Key(1f, Rgb.Hsv(0.05f, 0.25f, 0.35f));
                    l.Atmosphere = Rgb.Hsv(h + 0.02f, 0.8f, 1f);
                    l.AtmosphereStrength = 0.9f;
                    break;
                }
                case PlanetType.Gas:
                {
                    l.Height = 6;
                    l.Bands = R(0.65f, 1f);
                    float h = R();
                    float s = R(0.35f, 0.65f);
                    Key(0f, Rgb.Hsv(h, s, 0.6f)); Key(0.25f, Rgb.Hsv(h + 0.04f, s * 0.6f, 0.9f)); Key(0.5f, Rgb.Hsv(h - 0.05f, s, 0.75f));
                    Key(0.75f, Rgb.Hsv(h + 0.08f, s * 0.5f, 0.95f)); Key(1f, Rgb.Hsv(h, s * 0.8f, 0.7f));
                    l.Atmosphere = Rgb.Hsv(h, 0.4f, 1f);
                    l.Rings = R() < 0.55f;
                    l.RingTint = Rgb.Hsv(h + 0.05f, 0.35f, 1f);
                    l.RingTilt = R(-28f, 28f);
                    int moons = Pick(new[] { 0.2f, 0.35f, 0.3f, 0.15f });
                    for (int i = 0; i < moons; i++) l.Moons.Add(R(0.12f, 0.22f));
                    break;
                }
                case PlanetType.Jungle:
                {
                    l.Height = R() < 0.5f ? 4 : 0;
                    l.Sea = R(0.2f, 0.3f);
                    float g = 0.32f + hue * 0.06f;
                    Key(0f, Rgb.Hsv(0.52f, 0.7f, 0.5f)); Key(l.Sea, Rgb.Hsv(0.45f, 0.6f, 0.6f)); Key(l.Sea + 0.04f, Rgb.Hsv(g + 0.04f, 0.8f, 0.35f));
                    Key(0.6f, Rgb.Hsv(g, 0.75f, 0.55f)); Key(1f, Rgb.Hsv(g - 0.06f, 0.6f, 0.75f));
                    l.Atmosphere = Rgb.Hsv(0.4f, 0.45f, 1f);
                    l.Clouds = R(0.6f, 0.95f);
                    break;
                }
                case PlanetType.Crystal:
                {
                    l.Height = 7;
                    float h = R(0.72f, 0.95f);
                    Key(0f, Rgb.Hsv(h, 0.55f, 0.35f)); Key(0.55f, Rgb.Hsv(h + 0.05f, 0.5f, 0.65f)); Key(0.78f, Rgb.Hsv(h - 0.2f, 0.6f, 0.95f), 0.6f);
                    Key(1f, Rgb.Hsv(h - 0.35f, 0.4f, 1f), 1f);
                    l.Atmosphere = Rgb.Hsv(h, 0.4f, 1f);
                    l.Rings = R() < 0.3f;
                    l.RingTint = Rgb.Hsv(h - 0.3f, 0.3f, 1f);
                    l.RingTilt = R(-20f, 20f);
                    break;
                }
                case PlanetType.Crater:
                {
                    l.Height = 2;
                    float h = R(0.05f, 0.12f);
                    float s = R(0.05f, 0.25f);
                    Key(0f, Rgb.Hsv(h, s, 0.35f)); Key(0.5f, Rgb.Hsv(h, s * 0.8f, 0.62f)); Key(1f, Rgb.Hsv(h, s * 0.5f, 0.85f));
                    l.Atmosphere = Rgb.Hsv(0.65f, 0.2f, 1f);
                    l.AtmosphereStrength = 0.45f;
                    if (R() < 0.3f) l.Moons.Add(R(0.2f, 0.3f));
                    break;
                }
                case PlanetType.Toxic:
                {
                    l.Height = 4;
                    l.Sea = R(0.45f, 0.6f);
                    float h = 0.2f + hue * 0.05f;
                    Key(0f, Rgb.Hsv(h, 0.9f, 0.45f), 0.25f); Key(l.Sea - 0.03f, Rgb.Hsv(h + 0.02f, 0.85f, 0.85f), 0.2f); Key(l.Sea, Rgb.Hsv(0.1f, 0.4f, 0.5f));
                    Key(1f, Rgb.Hsv(0.12f, 0.3f, 0.7f));
                    l.Atmosphere = Rgb.Hsv(h, 0.7f, 1f);
                    l.Clouds = R(0.4f, 0.7f);
                    l.CloudTint = Rgb.Hsv(h, 0.4f, 1f);
                    break;
                }
                case PlanetType.Candy:
                {
                    l.Height = 4;
                    float h = R();
                    Key(0f, Rgb.Hsv(h, 0.35f, 1f)); Key(0.33f, Rgb.Hsv(h + 0.33f, 0.3f, 1f)); Key(0.66f, Rgb.Hsv(h + 0.66f, 0.35f, 1f)); Key(1f, new Rgb(1f, 1f, 1f));
                    l.Atmosphere = Rgb.Hsv(h, 0.35f, 1f);
                    l.Clouds = R() < 0.5f ? 0.5f : 0f;
                    l.CloudTint = Rgb.Hsv(h + 0.5f, 0.15f, 1f);
                    break;
                }
                case PlanetType.Rainbow:
                    l.Height = 6;
                    l.Bands = 1f;
                    for (int i = 0; i <= 6; i++) Key(i / 6f, Rgb.Hsv(i / 6f * 0.85f, 0.65f, 1f), 0.15f);
                    l.Atmosphere = new Rgb(1f, 1f, 1f);
                    l.AtmosphereStrength = 1.3f;
                    l.Rings = true;
                    l.RingTint = new Rgb(1f, 0.9f, 1f);
                    l.RingTilt = R(-25f, 25f);
                    l.Moons.Add(0.15f);
                    l.Moons.Add(0.11f);
                    break;
            }
            // the main colour: the ramp's middle
            l.Main = RampAt(l, l.Sea > 0f ? Mathf.Min(0.95f, l.Sea + 0.15f) : 0.5f);
            return l;
        }

        /// <summary>The ramp's colour at height <paramref name="h"/>.</summary>
        public static Rgb RampAt(PlanetLook l, float h)
        {
            var ramp = l.Ramp;
            if (ramp.Count == 0) return new Rgb(0.5f, 0.5f, 0.5f);
            if (h <= ramp[0].at) return ramp[0].colour;
            for (int i = 1; i < ramp.Count; i++)
                if (h <= ramp[i].at)
                {
                    float k = (h - ramp[i - 1].at) / Mathf.Max(1e-4f, ramp[i].at - ramp[i - 1].at);
                    return Rgb.Lerp(ramp[i - 1].colour, ramp[i].colour, k);
                }
            return ramp[ramp.Count - 1].colour;
        }

        // ------------------------------------------------------------------------------------------------- names

        // syllables an Albanian child reads easily (no x, no q), put together into names that sound like places
        static readonly string[] Starts = { "Ka", "Lo", "Mi", "Ze", "Tu", "Ri", "Ve", "No", "Sa", "Bi", "Do", "Fe", "Gu", "Hi", "Ju", "Ko", "La", "Me", "Ni", "Pa", "Re", "Si", "Ta", "Va", "Zo", "Ar", "El", "Or", "Il", "Um", "An", "Er", "Dri", "Bre", "Kri", "Fla", "Gro", "Pli", "Sto", "Tri" };
        static readonly string[] Middles = { "ra", "li", "no", "ve", "ta", "mi", "su", "ka", "do", "re", "la", "zo", "ni", "pe", "ro", "be", "lu", "ma", "ri", "se" };
        static readonly string[] Ends = { "na", "ra", "ta", "ni", "ro", "la", "sa", "do", "ku", "ne", "ri", "s", "n", "r", "l", "ka", "ja", "ma", "va", "ia" };
        static readonly string[] Suffixes = { " B", " C", " D", " Prima", " II", " III", " e Vogël", " e Madhe" };

        string PlanetName()
        {
            string n = Starts[_rng.Next(Starts.Length)] + (R() < 0.55f ? Middles[_rng.Next(Middles.Length)] : "") + Ends[_rng.Next(Ends.Length)];
            if (R() < 0.18f) n += Suffixes[_rng.Next(Suffixes.Length)];
            return n;
        }

        string StarName() => Starts[_rng.Next(Starts.Length)] + Middles[_rng.Next(Middles.Length)] + Ends[_rng.Next(Ends.Length)];

        string Unique(Func<string> make)
        {
            for (int i = 0; i < 200; i++)
            {
                string n = make();
                if (_names.Add(n)) return n;
            }
            string fallback = make() + " " + _names.Count;
            _names.Add(fallback);
            return fallback;
        }
    }
}
