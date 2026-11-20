using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>
    /// An IMGUI interface's pictures (icons, bubbles, panels, frames) collected into one mesh and drawn in one go inside
    /// OnGUI, instead of one IMGUI draw each. On the box every IMGUI draw costs the main thread (IMGUI's layout of it) and
    /// the render thread (the Mali driver's work per draw): Vishu!'s ~140 picture draws a frame were ~30 % of its main
    /// thread and most of its render thread (2026-11-12). Words can join it too (<see cref="Text"/>: glyphs from a font's
    /// texture, after the pictures), else they stay IMGUI's.
    ///
    /// Drawn inside OnGUI's repaint, before the words, so it writes what IMGUI would (the texture's sample times
    /// GUI.color, nothing converted, IMGUI's blending): a game collects its pictures in a first pass over its HUD code, draws them
    /// (<see cref="Draw"/>), then runs the HUD code again for the words. Coordinates are IMGUI's (y down) before
    /// GUI.matrix; texture coordinates like <see cref="GUI.DrawTextureWithTexCoords(Rect, Texture, Rect)"/>'s (the rect's
    /// top shows the uv rect's top). Pictures come from two textures: the interface sheet and a shapes texture
    /// (<see cref="Image"/>, <see cref="Shape"/>).
    /// </summary>
    public sealed class HudBatch : IDisposable
    {
        readonly Mesh _mesh;
        readonly Material _material;
        readonly List<Vector3> _verts = new List<Vector3>(1024);
        readonly List<Vector3> _uvs = new List<Vector3>(1024);
        readonly List<Color32> _colours = new List<Color32>(1024);
        readonly List<int> _tris = new List<int>(1536);

        public HudBatch(Texture sheet, Texture shapes)
        {
            var shader = Resources.Load<Shader>("TunduHudBatch");
            _material = new Material(shader) { name = "HUD batch", hideFlags = HideFlags.HideAndDontSave };
            _material.SetTexture("_MainTex", sheet);
            _material.SetTexture("_ShapeTex", shapes);
            _mesh = new Mesh { name = "HUD batch", hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
        }

        public int Quads => _verts.Count / 4;

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_material);
        }

        public void Clear()
        {
            _verts.Clear();
            _uvs.Clear();
            _colours.Clear();
            _tris.Clear();
            _texts.Clear();
        }

        /// <summary>A picture from the interface sheet (a negative width or height mirrors it, like IMGUI).</summary>
        public void Image(Rect r, Rect uv, Color tint) => Quad(r, uv, 0f, tint);

        /// <summary>A picture from the interface sheet turned <paramref name="degrees"/> about its rectangle's centre
        /// (clockwise on the screen, like a z rotation in GUI.matrix around that point).</summary>
        public void Image(Rect r, Rect uv, Color tint, float degrees)
        {
            if (degrees == 0f)
            {
                Quad(r, uv, 0f, tint);
                return;
            }
            var c = r.center;
            float a = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            Vector2 Turn(float x, float y)
            {
                float dx = x - c.x, dy = y - c.y;
                return new Vector2(c.x + dx * cos - dy * sin, c.y + dx * sin + dy * cos);
            }
            Quad(Turn(r.xMin, r.yMin), Turn(r.xMax, r.yMin), Turn(r.xMax, r.yMax), Turn(r.xMin, r.yMax), uv, 0f, tint);
        }

        /// <summary>A piece of the shapes texture.</summary>
        public void Shape(Rect r, Rect uv, Color tint) => Quad(r, uv, 1f, tint);

        /// <summary>
        /// A shape stretched over <paramref name="r"/> in nine pieces, like an IMGUI style's background with a border:
        /// corners <paramref name="border"/> units on the screen, <paramref name="uvBorder"/> of the texture. Without its
        /// <paramref name="middle"/>: a frame (a clear middle would still be blended over everything inside it).
        /// </summary>
        public void Nine(Rect r, float border, Rect uv, Vector2 uvBorder, Color tint, bool middle = true)
        {
            float[] xs = { r.xMin, r.xMin + border, r.xMax - border, r.xMax };
            float[] ys = { r.yMin, r.yMin + border, r.yMax - border, r.yMax };
            float[] us = { uv.xMin, uv.xMin + uvBorder.x, uv.xMax - uvBorder.x, uv.xMax };
            // the screen's top row shows the texture's top band
            float[] vs = { uv.yMax, uv.yMax - uvBorder.y, uv.yMin + uvBorder.y, uv.yMin };
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                {
                    if (xs[i + 1] <= xs[i] || ys[j + 1] <= ys[j] || (!middle && i == 1 && j == 1)) continue;
                    Quad(new Rect(xs[i], ys[j], xs[i + 1] - xs[i], ys[j + 1] - ys[j]),
                         Rect.MinMaxRect(us[i], vs[j + 1], us[i + 1], vs[j]), 1f, tint);
                }
        }

        // ------------------------------------------------------------------------------------------------ words

        readonly List<(Rect r, string text, int size, FontStyle style, Color colour, TextAnchor anchor, RectOffset padding, float grow)> _texts =
            new List<(Rect, string, int, FontStyle, Color, TextAnchor, RectOffset, float)>();
        Font _font;

        // The font's line as IMGUI lays it out (Unity's LegacyRuntime font, Arial's metrics): the ascender and descender
        // as shares of the size; a line is centred by them, not by its glyphs.
        const float Ascender = 0.905f, Descender = 0.212f;

        /// <summary>
        /// Words drawn by the batch, after all its pictures (in the order they came): one line of <paramref name="font"/>'s
        /// glyphs, rasterised at the size they take on the screen and laid out in <paramref name="r"/> like an IMGUI label
        /// with that <paramref name="anchor"/> and <paramref name="padding"/> (the style's). <paramref name="grow"/> scales
        /// the line about its anchor without rasterising another size (a pop).
        /// </summary>
        public void Text(Font font, Rect r, string text, int size, FontStyle style, Color colour, TextAnchor anchor,
                         RectOffset padding = null, float grow = 1f)
        {
            if (font == null || string.IsNullOrEmpty(text) || colour.a <= 0f) return;
            _font = font;
            _texts.Add((r, text, size, style, colour, anchor, padding, grow));
        }

        /// <summary>The words' glyphs, once every text's characters are in the font's texture (asking for some can
        /// rebuild it, which moves the glyphs asked for before: IMGUI's widths are measured first, which asks for the
        /// characters at the HUD's size). <paramref name="scale"/>: screen pixels per HUD unit.</summary>
        void AddTexts(float scale)
        {
            if (_texts.Count == 0 || _font == null) return;
            _lineWidths.Clear();
            foreach (var t in _texts) _lineWidths.Add(ImguiWidth(t.text, t.size, t.style));
            foreach (var t in _texts) _font.RequestCharactersInTexture(t.text, Px(t.size, scale), t.style);
            for (int i = 0; i < _texts.Count; i++) Glyphs(_texts[i], _lineWidths[i], scale);
            _texts.Clear();
        }

        readonly List<float> _lineWidths = new List<float>(64);
        readonly Dictionary<(string, int, FontStyle), float> _imguiWidths = new Dictionary<(string, int, FontStyle), float>();
        GUIStyle _measure;
        Font _measured;

        /// <summary>
        /// How wide IMGUI lays the line out, in HUD units: IMGUI places a label's glyphs by their advances at the HUD's
        /// size (before GUI.matrix scales them to the screen), and its synthesised bold a little wider than
        /// GetCharacterInfo's advances say. Laid out by those advances at the screen's size the batch's lines came out ~10 %
        /// narrower than IMGUI's on the box (Zana!'s menu at a 0.75 screen scale, 2026-11-17 night; the glyphs themselves
        /// the same height). Remembered per line (the HUD's lines rarely change).
        /// </summary>
        float ImguiWidth(string text, int size, FontStyle style)
        {
            if (_measured != _font)
            {
                _imguiWidths.Clear();
                _measured = _font;
            }
            var key = (text, size, style);
            if (_imguiWidths.TryGetValue(key, out float w)) return w;
            _measure ??= new GUIStyle { wordWrap = false, padding = new RectOffset(), margin = new RectOffset(), border = new RectOffset() };
            _measure.font = _font;
            _measure.fontSize = size;
            _measure.fontStyle = style;
            w = _measure.CalcSize(new GUIContent(text)).x;
            if (_imguiWidths.Count >= 1024) _imguiWidths.Clear();
            _imguiWidths[key] = w;
            return w;
        }

        static int Px(int size, float scale) => Mathf.Max(4, Mathf.RoundToInt(size * scale));

        void Glyphs((Rect r, string text, int size, FontStyle style, Color colour, TextAnchor anchor, RectOffset padding, float grow) t,
                    float imguiWidth, float scale)
        {
            int px = Px(t.size, scale);
            float width = 0f;
            foreach (char c in t.text)
                if (_font.GetCharacterInfo(c, out var ci, px, t.style)) width += ci.advance;
            // IMGUI's line width: the advances spread to it (the glyphs keep their size)
            float spread = width > 0f && imguiWidth > 0f ? imguiWidth * scale / width : 1f;
            width *= spread;
            // IMGUI writes a label's colour as linear (text, unlike its pictures): as given, the words came out paler
            // (ZJARR's (255, 188, 139) as (255, 223, 195) on the box)
            var colour = t.colour.linear;
            // the box the line is aligned in (the style's padding inside the rect), in screen pixels
            var r = t.r;
            if (t.padding != null)
                r = new Rect(r.x + t.padding.left, r.y + t.padding.top, r.width - t.padding.horizontal, r.height - t.padding.vertical);
            float k = t.grow;
            float w = width * k, ascent = Ascender * px * k, height = (Ascender + Descender) * px * k;
            float x0 = r.x * scale, x1 = r.xMax * scale, y0 = r.y * scale, y1 = r.yMax * scale;
            int column = (int)t.anchor % 3, row = (int)t.anchor / 3;
            float x = column == 0 ? x0 : column == 1 ? (x0 + x1 - w) * 0.5f : x1 - w;
            float top = row == 0 ? y0 : row == 1 ? (y0 + y1 - height) * 0.5f : y1 - height;
            // whole pixels on the screen: the glyphs were rasterised for them
            x = Mathf.Round(x);
            float baseline = Mathf.Round(top + ascent);
            float inv = 1f / scale;
            foreach (char c in t.text)
            {
                if (!_font.GetCharacterInfo(c, out var ci, px, t.style)) continue;
                if (ci.maxX > ci.minX && ci.maxY > ci.minY)
                {
                    // each glyph on a whole pixel (the spread advances aren't whole), unless the line is popping
                    float gx = k == 1f ? Mathf.Round(x) : x;
                    float left = (gx + ci.minX * k) * inv, right = (gx + ci.maxX * k) * inv;
                    float up = (baseline - ci.maxY * k) * inv, down = (baseline - ci.minY * k) * inv;
                    Quad(new Vector2(left, up), new Vector2(right, up), new Vector2(right, down), new Vector2(left, down),
                         ci.uvTopLeft, ci.uvTopRight, ci.uvBottomRight, ci.uvBottomLeft, 2f, colour);
                }
                x += ci.advance * spread * k;
            }
        }

        // ------------------------------------------------------------------------------------------------ quads

        void Quad(Rect r, Rect uv, float layer, Color tint)
        {
            float x0 = r.x, x1 = r.x + r.width, y0 = r.y, y1 = r.y + r.height;
            Quad(new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1), uv, layer, tint);
        }

        /// <summary>A quad from its corners: top left (the uv rect's top left), top right, bottom right, bottom left.</summary>
        void Quad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Rect uv, float layer, Color tint) =>
            Quad(p0, p1, p2, p3, new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin),
                 new Vector2(uv.xMin, uv.yMin), layer, tint);

        /// <summary>A quad from its corners and each corner's texture coordinates (a font's glyphs can lie turned).</summary>
        void Quad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, float layer, Color tint)
        {
            int n = _verts.Count;
            _verts.Add(p0);
            _verts.Add(p1);
            _verts.Add(p2);
            _verts.Add(p3);
            _uvs.Add(new Vector3(uv0.x, uv0.y, layer));
            _uvs.Add(new Vector3(uv1.x, uv1.y, layer));
            _uvs.Add(new Vector3(uv2.x, uv2.y, layer));
            _uvs.Add(new Vector3(uv3.x, uv3.y, layer));
            Color32 c = tint;
            _colours.Add(c);
            _colours.Add(c);
            _colours.Add(c);
            _colours.Add(c);
            _tris.Add(n);
            _tris.Add(n + 1);
            _tris.Add(n + 2);
            _tris.Add(n);
            _tris.Add(n + 2);
            _tris.Add(n + 3);
        }

        /// <summary>Draws what was collected, in the order it came (call during OnGUI's repaint, with GUI.matrix).</summary>
        public void Draw(Matrix4x4 guiMatrix)
        {
            AddTexts(guiMatrix.m11);
            if (_verts.Count == 0) return;
            if (_font != null) _material.SetTexture("_FontTex", _font.material.mainTexture);
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colours);
            _mesh.SetTriangles(_tris, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10f));
            if (!_material.SetPass(0)) return;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);   // IMGUI's screen: y down
            Graphics.DrawMeshNow(_mesh, guiMatrix);
            GL.PopMatrix();
        }
    }
}
