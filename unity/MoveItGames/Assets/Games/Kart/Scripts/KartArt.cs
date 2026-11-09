using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Kart
{
    /// <summary>
    /// Gara!'s pictures drawn in code: the four items' icons (in colour), the steering wheel the HUD turns with the
    /// player's hands, the moves as little figures (hold the wheel, jump, hands up) for the how-to cards, the boost pads'
    /// arrows, the minimap's track outline. Every shape anti-aliased; textures made once per scene.
    /// </summary>
    public static class KartArt
    {
        /// <summary>A little 2D painter: shapes in a 100 x 100 box (y down), each layer over the last.</summary>
        sealed class Canvas
        {
            readonly int _size;
            readonly Color[] _px;
            readonly float _k;

            public Canvas(int size)
            {
                _size = size;
                _px = new Color[size * size];
                _k = size / 100f;
            }

            /// <summary>Paints <paramref name="colour"/> where <paramref name="distance"/> (signed, box units, - inside) is
            /// inside, with an antialiased edge.</summary>
            public void Fill(Func<Vector2, float> distance, Color colour)
            {
                for (int y = 0; y < _size; y++)
                for (int x = 0; x < _size; x++)
                {
                    var p = new Vector2((x + 0.5f) / _k, (_size - 1 - y + 0.5f) / _k);
                    float a = Mathf.Clamp01(0.5f - distance(p) * _k) * colour.a;
                    if (a <= 0f) continue;
                    int i = y * _size + x;
                    var under = _px[i];
                    float outA = a + under.a * (1f - a);
                    var rgb = outA > 1e-5f ? (new Color(colour.r, colour.g, colour.b) * a + new Color(under.r, under.g, under.b) * under.a * (1f - a)) / outA : Color.clear;
                    _px[i] = new Color(rgb.r, rgb.g, rgb.b, outA);
                }
            }

            public Texture2D Texture(string name)
            {
                var tex = new Texture2D(_size, _size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels(_px);
                tex.Apply(true, true);
                return tex;
            }
        }

        // signed distances
        static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
        static float Ring(Vector2 p, Vector2 c, float r, float w) => Mathf.Abs((p - c).magnitude - r) - w * 0.5f;
        static float Segment(Vector2 p, Vector2 a, Vector2 b, float w)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude - w * 0.5f;
        }
        static float Box(Vector2 p, Vector2 c, Vector2 half, float round = 0f)
        {
            var d = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + Vector2.one * round;
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - round;
        }
        static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float e0 = Edge(p, a, b), e1 = Edge(p, b, c), e2 = Edge(p, c, a);
            float inside = Mathf.Max(e0, Mathf.Max(e1, e2));
            return inside;
        }
        static float Edge(Vector2 p, Vector2 a, Vector2 b)
        {
            var n = new Vector2(b.y - a.y, a.x - b.x).normalized;
            return Vector2.Dot(p - a, n);
        }
        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        // ------------------------------------------------------------------------------------------------ items

        public static Texture2D ItemIcon(KartItem item, int size = 128)
        {
            var c = new Canvas(size);
            var dark = new Color(0.08f, 0.1f, 0.22f, 0.9f);
            switch (item)
            {
                case KartItem.Rocket:
                {
                    // a fat rocket going up-right with a flame
                    var dir = new Vector2(0.7f, -0.7f).normalized;
                    var side = new Vector2(-dir.y, dir.x);
                    var tail = new Vector2(34f, 66f);
                    var nose = tail + dir * 44f;
                    c.Fill(p => Segment(p, tail - dir * 6f, tail - dir * 22f, 18f), Hex("#FFC93C"));       // the flame
                    c.Fill(p => Segment(p, tail - dir * 6f, tail - dir * 14f, 9f), Hex("#FFF4C8"));
                    c.Fill(p => Triangle(p, tail + side * 16f, tail + side * 6f + dir * 14f, tail - dir * 4f + side * 18f), Hex("#2E6FE8"));
                    c.Fill(p => Triangle(p, tail - side * 16f, tail - dir * 4f - side * 18f, tail - side * 6f + dir * 14f), Hex("#2E6FE8"));
                    c.Fill(p => Segment(p, tail, nose - dir * 8f, 26f), Hex("#FF5A4E"));                 // the body
                    c.Fill(p => Circle(p, nose - dir * 9f, 12.5f), Hex("#FF5A4E"));
                    c.Fill(p => Circle(p, tail + dir * 20f, 7.5f), Color.white);                          // the window
                    c.Fill(p => Circle(p, tail + dir * 20f, 4.5f), Hex("#31C6F4"));
                    break;
                }
                case KartItem.Paint:
                {
                    // a round bomb with a lit fuse and paint splashes
                    c.Fill(p => Circle(p, new Vector2(46f, 58f), 30f), Hex("#7B61FF"));
                    c.Fill(p => Circle(p, new Vector2(36f, 48f), 9f), new Color(1f, 1f, 1f, 0.45f));
                    c.Fill(p => Box(p, new Vector2(60f, 30f), new Vector2(7f, 6f), 2f), Hex("#2A2D3E"));
                    c.Fill(p => Segment(p, new Vector2(64f, 26f), new Vector2(74f, 14f), 4f), Hex("#B49D7A"));
                    c.Fill(p => Circle(p, new Vector2(77f, 11f), 6f), Hex("#FFC93C"));
                    c.Fill(p => Circle(p, new Vector2(77f, 11f), 3f), Color.white);
                    foreach (var (x, y, r, col) in new[] { (80f, 70f, 6f, "#FF5AA8"), (72f, 84f, 4f, "#FFC93C"), (18f, 84f, 5f, "#5DB04E"), (12f, 66f, 3.5f, "#31C6F4") })
                        c.Fill(p => Circle(p, new Vector2(x, y), r), Hex(col));
                    break;
                }
                case KartItem.Shield:
                {
                    // a bubble with a shine
                    c.Fill(p => Circle(p, new Vector2(50f, 52f), 38f), new Color(0.49f, 0.86f, 1f, 0.55f));
                    c.Fill(p => Ring(p, new Vector2(50f, 52f), 36f, 6f), Hex("#31C6F4"));
                    c.Fill(p => Segment(p, new Vector2(30f, 40f), new Vector2(40f, 28f), 7f), new Color(1f, 1f, 1f, 0.85f));
                    c.Fill(p => Circle(p, new Vector2(66f, 70f), 5f), new Color(1f, 1f, 1f, 0.6f));
                    break;
                }
                case KartItem.Honey:
                {
                    // a honey pot dripping
                    c.Fill(p => Box(p, new Vector2(50f, 62f), new Vector2(28f, 26f), 14f), Hex("#FFB02E"));
                    c.Fill(p => Box(p, new Vector2(50f, 34f), new Vector2(24f, 6f), 3f), Hex("#B9771E"));
                    c.Fill(p => Box(p, new Vector2(50f, 40f), new Vector2(26f, 6f), 4f), Hex("#FFC93C"));
                    c.Fill(p => Segment(p, new Vector2(38f, 44f), new Vector2(38f, 58f), 7f), Hex("#FFC93C"));
                    c.Fill(p => Circle(p, new Vector2(38f, 60f), 5f), Hex("#FFC93C"));
                    c.Fill(p => Box(p, new Vector2(50f, 68f), new Vector2(14f, 8f), 3f), new Color(1f, 1f, 1f, 0.85f));
                    break;
                }
                default:
                    c.Fill(p => Circle(p, new Vector2(50f, 50f), 30f), dark);
                    break;
            }
            return c.Texture($"Item {item}");
        }

        // ------------------------------------------------------------------------------------------------ the wheel

        /// <summary>The steering wheel the HUD shows (white: tinted when drawn): a thick ring, three spokes, a hub and two
        /// grips at ten to two.</summary>
        public static Texture2D Wheel(int size = 192)
        {
            var c = new Canvas(size);
            var centre = new Vector2(50f, 50f);
            c.Fill(p => Ring(p, centre, 40f, 11f), Color.white);
            for (int k = 0; k < 3; k++)
            {
                float a = Mathf.Deg2Rad * (90f + 120f * k);
                var end = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 38f;
                c.Fill(p => Segment(p, centre, end, 7f), Color.white);
            }
            c.Fill(p => Circle(p, centre, 11f), Color.white);
            // the grips (marks the hands hold)
            foreach (float deg in new[] { 145f, 35f })
            {
                float a = Mathf.Deg2Rad * deg;
                var g = centre + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * 40f;
                c.Fill(p => Circle(p, g, 8.5f), new Color(0.75f, 0.78f, 0.85f));
            }
            return c.Texture("Wheel");
        }

        // ------------------------------------------------------------------------------------------------ the moves

        public enum Figure { Wheel, Jump, HandsUp, Lean }

        /// <summary>A figure making a move, white on clear (the how-to cards and hints).</summary>
        public static Texture2D MoveFigure(Figure figure, int size = 128)
        {
            var c = new Canvas(size);
            var col = Color.white;
            float lift = figure == Figure.Jump ? -10f : 0f;
            var head = new Vector2(50f, 22f + lift);
            var neck = new Vector2(50f, 36f + lift);
            var hips = new Vector2(50f, 62f + lift);
            c.Fill(p => Circle(p, head, 10.5f), col);
            c.Fill(p => Segment(p, neck, hips, 7f), col);
            if (figure == Figure.Jump)
            {
                c.Fill(p => Segment(p, hips, new Vector2(40f, 74f + lift), 6f), col);
                c.Fill(p => Segment(p, new Vector2(40f, 74f + lift), new Vector2(44f, 86f + lift), 6f), col);
                c.Fill(p => Segment(p, hips, new Vector2(60f, 74f + lift), 6f), col);
                c.Fill(p => Segment(p, new Vector2(60f, 74f + lift), new Vector2(56f, 86f + lift), 6f), col);
                // the ground and motion marks
                c.Fill(p => Segment(p, new Vector2(30f, 96f), new Vector2(70f, 96f), 3f), new Color(1f, 1f, 1f, 0.6f));
                c.Fill(p => Segment(p, new Vector2(30f, 82f), new Vector2(30f, 90f), 3f), new Color(1f, 1f, 1f, 0.6f));
                c.Fill(p => Segment(p, new Vector2(70f, 82f), new Vector2(70f, 90f), 3f), new Color(1f, 1f, 1f, 0.6f));
            }
            else
            {
                c.Fill(p => Segment(p, hips, new Vector2(41f, 92f), 6.5f), col);
                c.Fill(p => Segment(p, hips, new Vector2(59f, 92f), 6.5f), col);
            }
            switch (figure)
            {
                case Figure.Wheel:
                case Figure.Jump:
                {
                    // both hands out in front on a wheel, the wheel turned a little
                    var w = new Vector2(50f, 46f + lift);
                    c.Fill(p => Ring(p, w, 15f, 4.5f), col);
                    var l = w + new Vector2(-13f, -7f);
                    var r = w + new Vector2(13f, 7f);
                    c.Fill(p => Segment(p, neck + new Vector2(-6f, 2f), l, 5.5f), col);
                    c.Fill(p => Segment(p, neck + new Vector2(6f, 2f), r, 5.5f), col);
                    if (figure == Figure.Wheel)
                    {
                        // the turn: two arcs of arrows
                        c.Fill(p => Ring(p, w, 24f, 3f) + Mathf.Max(0f, Edge(p, w + new Vector2(-30f, -4f), w + new Vector2(30f, -4f))), new Color(1f, 0.79f, 0.24f));
                        c.Fill(p => Triangle(p, w + new Vector2(20f, -20f), w + new Vector2(30f, -10f), w + new Vector2(18f, -6f)), new Color(1f, 0.79f, 0.24f));
                    }
                    break;
                }
                case Figure.HandsUp:
                    c.Fill(p => Segment(p, neck + new Vector2(-5f, 2f), new Vector2(32f, 10f), 5.5f), col);
                    c.Fill(p => Segment(p, neck + new Vector2(5f, 2f), new Vector2(68f, 10f), 5.5f), col);
                    c.Fill(p => Circle(p, new Vector2(31f, 7f), 4.5f), col);
                    c.Fill(p => Circle(p, new Vector2(69f, 7f), 4.5f), col);
                    break;
                default:
                    c.Fill(p => Segment(p, neck, new Vector2(36f, 58f), 5.5f), col);
                    c.Fill(p => Segment(p, neck, new Vector2(64f, 58f), 5.5f), col);
                    break;
            }
            return c.Texture($"Figure {figure}");
        }

        // ------------------------------------------------------------------------------------------------ the road

        /// <summary>A boost pad's arrows (white chevrons on clear, tiling along v: the material scrolls them).</summary>
        public static Texture2D PadArrows(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Pad arrows", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size;
                // a chevron pointing up (along v): v - |u| * 0.9 in a band
                float d = Mathf.Repeat(v - Mathf.Abs(u) * 0.9f, 1f);
                float band = Mathf.Clamp01((0.18f - Mathf.Abs(d - 0.45f)) * size * 0.5f);
                float edge = Mathf.Clamp01((0.47f - Mathf.Abs(u)) * size * 0.5f);
                byte a = (byte)(255f * band * edge);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>The track seen from above (white line on clear) for the minimap, with the map's bounds.</summary>
        public static Texture2D Minimap(KartTrack track, int size, out Rect bounds)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (float s = 0f; s < track.Length; s += 4f)
            {
                var p = track.Point(s);
                x0 = Mathf.Min(x0, p.x);
                x1 = Mathf.Max(x1, p.x);
                z0 = Mathf.Min(z0, p.y);
                z1 = Mathf.Max(z1, p.y);
            }
            float span = Mathf.Max(x1 - x0, z1 - z0) * 1.12f;
            var mid = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            bounds = new Rect(mid.x - span * 0.5f, mid.y - span * 0.5f, span, span);
            var pts = new List<Vector2>();
            for (float s = 0f; s <= track.Length + 2f; s += 2f)
            {
                var p = track.Point(s);
                pts.Add(new Vector2((p.x - bounds.x) / span * size, (p.y - bounds.y) / span * size));
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Minimap", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float width = size * 0.03f;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                int minX = Mathf.Max(0, (int)(Mathf.Min(a.x, b.x) - width - 2)), maxX = Mathf.Min(size - 1, (int)(Mathf.Max(a.x, b.x) + width + 2));
                int minY = Mathf.Max(0, (int)(Mathf.Min(a.y, b.y) - width - 2)), maxY = Mathf.Min(size - 1, (int)(Mathf.Max(a.y, b.y) + width + 2));
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float d = Segment(new Vector2(x + 0.5f, y + 0.5f), a, b, width * 2f);
                    byte alpha = (byte)(255f * Mathf.Clamp01(0.5f - d));
                    int k = y * size + x;
                    if (alpha > px[k].a) px[k] = new Color32(255, 255, 255, alpha);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>A soft round blob (white, alpha falling off): the item bubble's halo, shadows.</summary>
        public static Texture2D Glow(int size = 64)
        {
            var c = new Canvas(size);
            for (int i = 0; i < 6; i++)
            {
                float r = 48f - i * 7f;
                c.Fill(p => Circle(p, new Vector2(50f, 50f), r), new Color(1f, 1f, 1f, 0.22f));
            }
            return c.Texture("Glow");
        }
    }
}
