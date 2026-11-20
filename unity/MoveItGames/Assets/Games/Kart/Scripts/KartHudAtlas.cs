using System;
using UnityEngine;

namespace MoveIt.Games.Kart
{
    /// <summary>
    /// Gara!'s race HUD pictures in one texture: the item icons, the move figures, the steering wheel, the minimap (the
    /// track's, written in when a race is set up), a dot, a white block, a circle and a star (the stars' count: the box's
    /// font may have no star). With it the HUD's pictures are one
    /// mesh (<see cref="HudBatch"/>) instead of an IMGUI draw each: the split screen's two HUDs and the minimap were ~25
    /// draws a frame on a box whose render thread was short of time for its draw calls (2026-11-13). Cells are 8 texels
    /// apart, so the mipmaps the HUD is drawn with (at 0.5-0.8 of the pictures' size) don't bleed into each other.
    /// </summary>
    public sealed class KartHudAtlas : IDisposable
    {
        const int Width = 1024, Height = 512, MapSize = 256;

        public readonly Texture2D Texture;
        /// <summary>Texture rectangles (0..1, like <see cref="GUI.DrawTextureWithTexCoords(Rect, Texture, Rect)"/>'s).</summary>
        public readonly Rect[] Items = new Rect[5], Figures = new Rect[4];
        public readonly Rect Wheel, Map, Dot, White, Circle, Star;

        readonly Color32[] _px = new Color32[Width * Height];
        readonly RectInt _mapCell;

        /// <param name="items">The item icons by <see cref="KartItem"/> (readable; [0] unused).</param>
        /// <param name="figures">The move figures by <see cref="KartArt.Figure"/> (readable).</param>
        /// <param name="wheel">The steering wheel (readable).</param>
        public KartHudAtlas(Texture2D[] items, Texture2D[] figures, Texture2D wheel)
        {
            Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true)
            {
                name = "Kart HUD atlas", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            // the 128-texel pictures along the bottom (the four items, three figures), the rest in a row above them
            int x = 0;
            for (int i = 1; i < items.Length && i < Items.Length; i++, x += 136) Items[i] = Put(items[i], x, 0);
            for (int i = 0; i < figures.Length && i < Figures.Length; i++)
            {
                if (x + 128 > Width) x = 0;
                Figures[i] = Put(figures[i], x, x == 0 ? 136 : 0);
                x += 136;
            }
            Wheel = Put(wheel, 136, 136);
            _mapCell = new RectInt(336, 136, MapSize, MapSize);
            Map = UvOf(_mapCell);
            Circle = Disc(600, 136, 128);
            Dot = Disc(736, 136, 64);
            // a white block, sampled well inside so its edges never blur in
            Fill(new RectInt(808, 136, 16, 16), new Color32(255, 255, 255, 255));
            White = UvOf(new RectInt(812, 140, 8, 8));
            Star = StarShape(832, 136, 64);
            Texture.SetPixels32(_px);
            Texture.Apply(true, false);
        }

        /// <summary>Writes the track's minimap (readable, <see cref="MapSize"/> square) into its cell.</summary>
        public void SetMap(Texture2D map)
        {
            if (map == null) return;
            Fill(_mapCell, new Color32(0, 0, 0, 0));
            Copy(map, _mapCell.x, _mapCell.y, Mathf.Min(MapSize, map.width), Mathf.Min(MapSize, map.height));
            Texture.SetPixels32(_px);
            Texture.Apply(true, false);
        }

        public void Dispose()
        {
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
        }

        Rect Put(Texture2D source, int x, int y)
        {
            if (source == null) return default;
            Copy(source, x, y, source.width, source.height);
            return UvOf(new RectInt(x, y, source.width, source.height));
        }

        void Copy(Texture2D source, int x, int y, int w, int h)
        {
            var from = source.GetPixels32();
            for (int row = 0; row < h; row++)
                Array.Copy(from, row * source.width, _px, (y + row) * Width + x, w);
        }

        /// <summary>A white disc with an antialiased edge (as <c>TennisArt.Dot</c> draws one): the round item slot, the
        /// drift's ring, the minimap's dots.</summary>
        Rect Disc(int x, int y, int size)
        {
            float r = size * 0.5f;
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float d = Mathf.Sqrt((i + 0.5f - r) * (i + 0.5f - r) + (j + 0.5f - r) * (j + 0.5f - r));
                _px[(y + j) * Width + x + i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - 1.5f - d) * 255f));
            }
            return UvOf(new RectInt(x, y, size, size));
        }

        /// <summary>A white five-pointed star (the ★ glyph's shape), antialiased by 4 x 4 samples a texel.</summary>
        Rect StarShape(int x, int y, int size)
        {
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;   // the first point up (texel rows go up)
                float radius = (i % 2 == 0 ? 0.5f : 0.2f) * (size - 4);
                points[i] = new Vector2(size * 0.5f + Mathf.Cos(a) * radius, size * 0.5f - 1f + Mathf.Sin(a) * radius);
            }
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                int inside = 0;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                    if (Inside(points, new Vector2(i + (sx + 0.5f) / 4f, j + (sy + 0.5f) / 4f))) inside++;
                _px[(y + j) * Width + x + i] = new Color32(255, 255, 255, (byte)(inside * 255 / 16));
            }
            return UvOf(new RectInt(x, y, size, size));
        }

        static bool Inside(Vector2[] polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        void Fill(RectInt cell, Color32 colour)
        {
            for (int j = cell.yMin; j < cell.yMax; j++)
            for (int i = cell.xMin; i < cell.xMax; i++)
                _px[j * Width + i] = colour;
        }

        static Rect UvOf(RectInt cell) =>
            new Rect(cell.x / (float)Width, cell.y / (float)Height, cell.width / (float)Width, cell.height / (float)Height);
    }
}
