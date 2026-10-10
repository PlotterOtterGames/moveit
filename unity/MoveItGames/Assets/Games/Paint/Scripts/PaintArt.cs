using System;
using UnityEngine;

namespace MoveIt.Games.Paint
{
    /// <summary>Stamp shapes in the brush atlas (one 128-px cell each, white with alpha coverage).</summary>
    public enum BrushShape { Soft, Round, Star, Heart, Flower, Footprint, Sparkle, Bubble }

    /// <summary>
    /// Pikturo!'s small textures, drawn in code at start-up (like the rest of Tundu's art, no image files to keep in
    /// sync): the brush atlas, cursor rings, rounded buttons and the tool icons. Shapes are tested per sample with
    /// 4×4 supersampling, so edges are smooth at any size.
    /// </summary>
    public static class PaintArt
    {
        public const int Cell = 128;
        public static readonly int ShapeCount = Enum.GetValues(typeof(BrushShape)).Length;

        static Texture2D _atlas, _disc, _ring, _rounded;

        /// <summary>The stamp shapes side by side (alpha = coverage). <see cref="ShapeUv"/> gives a cell.</summary>
        public static Texture2D BrushAtlas => _atlas != null ? _atlas : _atlas = MakeAtlas();
        /// <summary>White anti-aliased disc (colour swatches, brush preview).</summary>
        public static Texture2D Disc => _disc != null ? _disc : _disc = Make(96, s => s.Circle(0.5f, 0.5f, 0.48f, Color.white));
        /// <summary>White ring (cursor).</summary>
        public static Texture2D Ring => _ring != null ? _ring : _ring = Make(128, s => s.Ring(0.5f, 0.5f, 0.47f, 0.40f, Color.white));
        /// <summary>White rounded square, drawn with a GUIStyle border so it stretches without bending the corners.</summary>
        public static Texture2D Rounded => _rounded != null ? _rounded : _rounded = Make(64, s => s.RoundRect(0f, 0f, 1f, 1f, 0.3f, Color.white));

        public static Rect ShapeUv(BrushShape shape) => new Rect((int)shape / (float)ShapeCount, 0f, 1f / ShapeCount, 1f);

        // ---------------------------------------------------------------- brush atlas

        static Texture2D MakeAtlas()
        {
            var tex = new Texture2D(Cell * ShapeCount, Cell, TextureFormat.RGBA32, false, true)
            {
                name = "Brush shapes", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[tex.width * tex.height];
            for (int s = 0; s < ShapeCount; s++)
            {
                var shape = (BrushShape)s;
                for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    float a = shape == BrushShape.Soft ? SoftFalloff(x, y) : Supersample(x, y, Cell, (u, v) => Inside(shape, u, v));
                    pixels[y * tex.width + s * Cell + x] = new Color32(255, 255, 255, (byte)(a * 255f + 0.5f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static float SoftFalloff(int x, int y)
        {
            float u = (x + 0.5f) / Cell * 2f - 1f, v = (y + 0.5f) / Cell * 2f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);
            return d >= 1f ? 0f : Mathf.Pow(1f - d * d, 2f);   // smooth to zero at the edge
        }

        /// <summary>Shape tests in cell space: u, v in [-1, 1], v up.</summary>
        static bool Inside(BrushShape shape, float u, float v)
        {
            switch (shape)
            {
                case BrushShape.Round:
                    return u * u + v * v <= 0.92f * 0.92f;
                case BrushShape.Star:
                    return InStar(u, v + 0.06f, 0.95f, 0.42f, 5);
                case BrushShape.Heart:
                    return InHeart(u * 0.62f, v * 0.62f + 0.55f);
                case BrushShape.Flower:
                {
                    if (u * u + v * v < 0.16f * 0.16f) return false;   // little hole in the middle
                    if (u * u + v * v < 0.42f * 0.42f) return true;
                    for (int k = 0; k < 5; k++)
                    {
                        float a = Mathf.PI / 2f + k * Mathf.PI * 2f / 5f;
                        float cx = 0.5f * Mathf.Cos(a), cy = 0.5f * Mathf.Sin(a);
                        if ((u - cx) * (u - cx) + (v - cy) * (v - cy) < 0.38f * 0.38f) return true;
                    }
                    return false;
                }
                case BrushShape.Footprint:   // a dino's three toes and a round pad
                    return InEllipse(u, v, 0f, -0.32f, 0.42f, 0.5f, 0f)
                        || InEllipse(u, v, -0.5f, 0.42f, 0.17f, 0.4f, -28f)
                        || InEllipse(u, v, 0f, 0.6f, 0.17f, 0.4f, 0f)
                        || InEllipse(u, v, 0.5f, 0.42f, 0.17f, 0.4f, 28f);
                case BrushShape.Sparkle:     // four-point twinkle
                {
                    float au = Mathf.Abs(u), av = Mathf.Abs(v);
                    return Mathf.Sqrt(au) + Mathf.Sqrt(av) <= Mathf.Sqrt(0.95f) || au * au + av * av < 0.12f * 0.12f;
                }
                case BrushShape.Bubble:
                {
                    float d = Mathf.Sqrt(u * u + v * v);
                    return (d <= 0.92f && d >= 0.74f) || InEllipse(u, v, -0.3f, 0.32f, 0.12f, 0.2f, 40f);
                }
                default:
                    return false;
            }
        }

        static bool InStar(float x, float y, float outer, float inner, int points)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > outer) return false;
            if (r < inner) return true;
            float a = Mathf.Atan2(x, y);                       // 0 = straight up
            float sector = Mathf.PI * 2f / points;
            float t = Mathf.Repeat(a + sector / 2f, sector) - sector / 2f;   // angle from the nearest tip
            // edge from the tip (outer, angle 0) to the valley (inner, half a sector) as a straight line
            var tip = new Vector2(0f, outer);
            var valley = new Vector2(inner * Mathf.Sin(sector / 2f), inner * Mathf.Cos(sector / 2f));
            var p = new Vector2(Mathf.Abs(r * Mathf.Sin(t)), r * Mathf.Cos(t));
            var edge = valley - tip;
            return edge.x * (p.y - tip.y) - edge.y * (p.x - tip.x) <= 0f;
        }

        static bool InHeart(float x, float y)
        {
            x = Mathf.Abs(x);
            if (y + x > 1f) return (x - 0.25f) * (x - 0.25f) + (y - 0.75f) * (y - 0.75f) <= 0.125f;
            return y >= x && y >= 0f;   // the lower point
        }

        static bool InEllipse(float x, float y, float cx, float cy, float rx, float ry, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float dx = x - cx, dy = y - cy;
            float u = dx * c + dy * s, v = -dx * s + dy * c;
            return (u * u) / (rx * rx) + (v * v) / (ry * ry) <= 1f;
        }

        static float Supersample(int x, int y, int size, Func<float, float, bool> inside)
        {
            int hits = 0;
            for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 4; sx++)
            {
                float u = (x + (sx + 0.5f) / 4f) / size * 2f - 1f;
                float v = (y + (sy + 0.5f) / 4f) / size * 2f - 1f;
                if (inside(u, v)) hits++;
            }
            return hits / 16f;
        }

        // ---------------------------------------------------------------- icons

        public enum Icon { Rainbow, Spray, Neon, Glitter, Eraser, Fill, Undo, Size, Symmetry, Page, Clear, Camera, Paper, Photo, Hand }

        static Texture2D[] _icons;

        /// <summary>96-px icons. Tool icons that show the current colour are white where the colour goes (tint them).</summary>
        public static Texture2D IconTexture(Icon icon)
        {
            _icons ??= new Texture2D[Enum.GetValues(typeof(Icon)).Length];
            ref var tex = ref _icons[(int)icon];
            if (tex == null) tex = Make(96, s => DrawIcon(icon, s));
            return tex;
        }

        static readonly Color Ink = new Color(0.08f, 0.1f, 0.22f);

        static void DrawIcon(Icon icon, Sketch s)
        {
            var w = Color.white;
            switch (icon)
            {
                case Icon.Rainbow:
                {
                    Color[] bands = { new Color(1f, 0.33f, 0.3f), new Color(1f, 0.62f, 0.1f), new Color(1f, 0.85f, 0.2f),
                                      new Color(0.4f, 0.85f, 0.4f), new Color(0.2f, 0.7f, 1f), new Color(0.5f, 0.4f, 1f) };
                    for (int i = 0; i < bands.Length; i++)
                        s.Ring(0.5f, 0.22f, 0.46f - i * 0.055f, 0.40f - i * 0.055f, bands[i], 0f, 180f);
                    break;
                }
                case Icon.Spray:
                {
                    var rng = new System.Random(3);
                    for (int i = 0; i < 70; i++)
                    {
                        double a = rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(rng.NextDouble()) * 0.4;
                        s.Circle(0.5f + (float)(r * Math.Cos(a)), 0.5f + (float)(r * Math.Sin(a)), 0.02f + (float)rng.NextDouble() * 0.025f, w);
                    }
                    break;
                }
                case Icon.Neon:
                    for (int i = 0; i < 6; i++) s.Circle(0.5f, 0.5f, 0.46f - i * 0.05f, new Color(1f, 1f, 1f, 0.16f));
                    s.Circle(0.5f, 0.5f, 0.13f, w);
                    break;
                case Icon.Glitter:
                    s.Shape(BrushShape.Sparkle, 0.4f, 0.58f, 0.34f, w);
                    s.Shape(BrushShape.Sparkle, 0.75f, 0.28f, 0.18f, w);
                    s.Shape(BrushShape.Sparkle, 0.2f, 0.22f, 0.12f, w);
                    break;
                case Icon.Eraser:
                    s.Quad(new Vector2(0.12f, 0.42f), new Vector2(0.45f, 0.1f), new Vector2(0.9f, 0.55f), new Vector2(0.57f, 0.87f), new Color(1f, 0.55f, 0.65f));
                    s.Quad(new Vector2(0.12f, 0.42f), new Vector2(0.45f, 0.1f), new Vector2(0.6f, 0.25f), new Vector2(0.27f, 0.57f), w);
                    break;
                case Icon.Fill:   // a tipped bucket pouring a drop (the drop is white: tinted with the colour)
                    s.Quad(new Vector2(0.18f, 0.62f), new Vector2(0.52f, 0.92f), new Vector2(0.78f, 0.6f), new Vector2(0.42f, 0.32f), new Color(0.75f, 0.8f, 0.9f));
                    s.Ellipse(0.48f, 0.76f, 0.24f, 0.08f, 41f, new Color(0.45f, 0.5f, 0.62f));
                    s.Circle(0.3f, 0.22f, 0.13f, w);
                    s.Triangle(new Vector2(0.19f, 0.27f), new Vector2(0.41f, 0.27f), new Vector2(0.38f, 0.48f), w);
                    break;
                case Icon.Undo:
                    s.Ring(0.55f, 0.45f, 0.32f, 0.2f, w, -90f, 160f);
                    s.Triangle(new Vector2(0.06f, 0.55f), new Vector2(0.42f, 0.55f), new Vector2(0.24f, 0.78f), w);
                    break;
                case Icon.Size:
                    s.Circle(0.16f, 0.5f, 0.08f, w);
                    s.Circle(0.4f, 0.5f, 0.13f, w);
                    s.Circle(0.73f, 0.5f, 0.2f, w);
                    break;
                case Icon.Symmetry:
                    for (int i = 0; i < 5; i++) s.RoundRect(0.47f, 0.06f + i * 0.19f, 0.53f, 0.15f + i * 0.19f, 0.02f, w);
                    s.Shape(BrushShape.Heart, 0.25f, 0.5f, 0.2f, new Color(1f, 0.45f, 0.5f));
                    s.Shape(BrushShape.Heart, 0.75f, 0.5f, 0.2f, new Color(1f, 0.45f, 0.5f));
                    break;
                case Icon.Page:
                    s.RoundRect(0.18f, 0.06f, 0.82f, 0.94f, 0.06f, w);
                    s.Ring(0.5f, 0.55f, 0.2f, 0.15f, Ink);
                    s.Circle(0.43f, 0.6f, 0.025f, Ink);
                    s.Circle(0.57f, 0.6f, 0.025f, Ink);
                    s.Ring(0.5f, 0.55f, 0.11f, 0.08f, Ink, 200f, 340f);
                    break;
                case Icon.Clear:   // a bin
                    s.RoundRect(0.22f, 0.08f, 0.78f, 0.72f, 0.08f, w);
                    s.RoundRect(0.14f, 0.74f, 0.86f, 0.84f, 0.04f, w);
                    s.RoundRect(0.4f, 0.84f, 0.6f, 0.93f, 0.03f, w);
                    for (int i = 0; i < 3; i++) s.RoundRect(0.33f + i * 0.13f, 0.18f, 0.39f + i * 0.13f, 0.62f, 0.02f, Ink);
                    break;
                case Icon.Camera:
                    s.RoundRect(0.06f, 0.18f, 0.94f, 0.76f, 0.12f, w);
                    s.RoundRect(0.32f, 0.7f, 0.62f, 0.86f, 0.05f, w);
                    s.Circle(0.5f, 0.47f, 0.2f, Ink);
                    s.Circle(0.5f, 0.47f, 0.12f, w);
                    break;
                case Icon.Paper:
                    s.RoundRect(0.12f, 0.12f, 0.88f, 0.88f, 0.08f, w);
                    s.Shape(BrushShape.Flower, 0.5f, 0.5f, 0.28f, new Color(1f, 0.55f, 0.3f));
                    break;
                case Icon.Hand:    // an open right hand, palm to the viewer (thumb on the left), with a brush dot
                    s.RoundRect(0.28f, 0.1f, 0.74f, 0.56f, 0.12f, w);
                    s.RoundRect(0.29f, 0.45f, 0.39f, 0.84f, 0.05f, w);
                    s.RoundRect(0.41f, 0.45f, 0.51f, 0.92f, 0.05f, w);
                    s.RoundRect(0.53f, 0.45f, 0.63f, 0.88f, 0.05f, w);
                    s.RoundRect(0.65f, 0.42f, 0.74f, 0.76f, 0.045f, w);
                    s.Ellipse(0.22f, 0.36f, 0.07f, 0.17f, 35f, w);
                    break;
                case Icon.Photo:   // a framed picture: sky, sun, hills
                    s.RoundRect(0.06f, 0.14f, 0.94f, 0.86f, 0.08f, w);
                    s.RoundRect(0.13f, 0.21f, 0.87f, 0.79f, 0.04f, new Color(0.45f, 0.75f, 1f));
                    s.Circle(0.68f, 0.6f, 0.09f, new Color(1f, 0.85f, 0.2f));
                    s.Triangle(new Vector2(0.13f, 0.21f), new Vector2(0.66f, 0.21f), new Vector2(0.38f, 0.55f), new Color(0.4f, 0.8f, 0.4f));
                    s.Triangle(new Vector2(0.45f, 0.21f), new Vector2(0.87f, 0.21f), new Vector2(0.7f, 0.42f), new Color(0.3f, 0.65f, 0.35f));
                    break;
            }
        }

        // ---------------------------------------------------------------- tiny rasterizer

        static Texture2D Make(int size, Action<Sketch> draw)
        {
            var sketch = new Sketch(size);
            draw(sketch);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(sketch.Pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Draws filled shapes onto a square image (0..1 coordinates, y up), each anti-aliased by supersampling.</summary>
        sealed class Sketch
        {
            public readonly Color[] Pixels;
            readonly int _size;

            public Sketch(int size)
            {
                _size = size;
                Pixels = new Color[size * size];
            }

            void Fill(Func<float, float, bool> inside, Color color, float x0 = 0f, float y0 = 0f, float x1 = 1f, float y1 = 1f)
            {
                int ix0 = Mathf.Max(0, (int)(x0 * _size) - 1), iy0 = Mathf.Max(0, (int)(y0 * _size) - 1);
                int ix1 = Mathf.Min(_size - 1, (int)(x1 * _size) + 1), iy1 = Mathf.Min(_size - 1, (int)(y1 * _size) + 1);
                for (int y = iy0; y <= iy1; y++)
                for (int x = ix0; x <= ix1; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        if (inside((x + (sx + 0.5f) / 4f) / _size, (y + (sy + 0.5f) / 4f) / _size)) hits++;
                    if (hits == 0) continue;
                    float a = color.a * hits / 16f;
                    ref var dst = ref Pixels[y * _size + x];
                    float outA = a + dst.a * (1f - a);
                    var rgb = outA > 0f ? ((Color)color * a + dst * dst.a * (1f - a)) / outA : Color.clear;
                    dst = new Color(rgb.r, rgb.g, rgb.b, outA);
                }
            }

            public void Circle(float cx, float cy, float r, Color c) =>
                Fill((x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r, c, cx - r, cy - r, cx + r, cy + r);

            /// <summary>Ring between two radii, optionally only between two angles (degrees, counter-clockwise from +x).</summary>
            public void Ring(float cx, float cy, float outer, float inner, Color c, float fromDeg = 0f, float toDeg = 360f) =>
                Fill((x, y) =>
                {
                    float d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d2 > outer * outer || d2 < inner * inner) return false;
                    if (toDeg - fromDeg >= 360f) return true;
                    float a = Mathf.Repeat(Mathf.Atan2(y - cy, x - cx) * Mathf.Rad2Deg - fromDeg, 360f);
                    return a <= toDeg - fromDeg;
                }, c, cx - outer, cy - outer, cx + outer, cy + outer);

            public void Ellipse(float cx, float cy, float rx, float ry, float degrees, Color c) =>
                Fill((x, y) => InEllipse(x, y, cx, cy, rx, ry, degrees), c);

            public void RoundRect(float x0, float y0, float x1, float y1, float r, Color c) =>
                Fill((x, y) =>
                {
                    if (x < x0 || x > x1 || y < y0 || y > y1) return false;
                    float dx = Mathf.Max(x0 + r - x, 0f, x - (x1 - r)), dy = Mathf.Max(y0 + r - y, 0f, y - (y1 - r));
                    return dx * dx + dy * dy <= r * r;
                }, c, x0, y0, x1, y1);

            public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color) => Polygon(color, a, b, c);

            public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color) => Polygon(color, a, b, c, d);

            /// <summary>A brush-atlas shape centred at (cx, cy) with radius r.</summary>
            public void Shape(BrushShape shape, float cx, float cy, float r, Color c) =>
                Fill((x, y) => Inside(shape, (x - cx) / r, (y - cy) / r), c, cx - r, cy - r, cx + r, cy + r);

            void Polygon(Color color, params Vector2[] p) =>
                Fill((x, y) =>
                {
                    bool inside = false;   // even-odd rule
                    for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                        if ((p[i].y > y) != (p[j].y > y) && x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x)
                            inside = !inside;
                    return inside;
                }, color);
        }
    }
}
