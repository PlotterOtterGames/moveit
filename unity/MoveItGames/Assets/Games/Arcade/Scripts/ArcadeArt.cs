using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>One picture on Lunapark!'s sheet: where it is (UVs), its size in the sheet's pixels, its pivot (0..1 from
    /// the top left, as drawn) and how many pixels make one of its drawing units (tools/arcade/art.py), so the game
    /// sizes every sprite by a length in the world: "this mole is 0.3 torso lengths a unit".</summary>
    public sealed class ArcadeSprite
    {
        public string Name;
        public Rect Uv;
        public Vector2 Size;
        public Vector2 Pivot;
        public float Ppt;
    }

    /// <summary>
    /// Lunapark!'s art (Games/Arcade/Art, made by tools/arcade/art.py): the sprite sheet and its named rectangles, and the
    /// funhouse wall board. Missing names fall back to a white square (so a renamed sprite shows, never throws).
    /// </summary>
    public sealed class ArcadeArt
    {
        [Serializable]
        sealed class SheetJson
        {
            public int[] size;
            public SpriteJson[] sprites;
        }

        [Serializable]
        sealed class SpriteJson
        {
            public string name;
            public int[] rect;
            public float[] pivot;
            public float ppt;
        }

        readonly Dictionary<string, ArcadeSprite> _sprites = new Dictionary<string, ArcadeSprite>();
        public Texture2D Sheet { get; private set; }
        public Texture2D Wall { get; private set; }
        public int Count => _sprites.Count;
        ArcadeSprite _missing;

        public static ArcadeArt Load(TextAsset json, Texture2D sheet, Texture2D wall)
        {
            var art = new ArcadeArt { Sheet = sheet, Wall = wall };
            if (json == null || sheet == null)
            {
                Debug.LogWarning("[Arcade] The sprite sheet is missing: run tools/arcade/art.py");
                return art;
            }
            var data = JsonUtility.FromJson<SheetJson>(json.text);
            float w = data.size[0], h = data.size[1];
            foreach (var s in data.sprites)
            {
                float x = s.rect[0], y = s.rect[1], sw = s.rect[2], sh = s.rect[3];
                art._sprites[s.name] = new ArcadeSprite
                {
                    Name = s.name,
                    // the sheet's rows go down from the top, a texture's v up from the bottom
                    Uv = new Rect(x / w, 1f - (y + sh) / h, sw / w, sh / h),
                    Size = new Vector2(sw, sh),
                    Pivot = s.pivot != null && s.pivot.Length == 2 ? new Vector2(s.pivot[0], s.pivot[1]) : new Vector2(0.5f, 0.5f),
                    Ppt = s.ppt > 0f ? s.ppt : Mathf.Min(sw, sh) * 0.5f,
                };
            }
            return art;
        }

        public ArcadeSprite this[string name]
        {
            get
            {
                if (_sprites.TryGetValue(name, out var s)) return s;
                if (_missing == null)
                {
                    var white = _sprites.TryGetValue("white", out var wt) ? wt.Uv : new Rect(0f, 0f, 0f, 0f);
                    _missing = new ArcadeSprite { Name = "missing", Uv = white, Size = new Vector2(64f, 64f), Pivot = new Vector2(0.5f, 0.5f), Ppt = 32f };
                }
                return _missing;
            }
        }

        public bool Has(string name) => _sprites.ContainsKey(name);

        /// <summary>Draws a sprite in IMGUI (HUD icons): fitted into <paramref name="r"/> keeping its shape.</summary>
        public void Gui(Rect r, string name, Color? tint = null)
        {
            if (Sheet == null) return;
            var s = this[name];
            float k = Mathf.Min(r.width / s.Size.x, r.height / s.Size.y);
            var fit = new Rect(r.center.x - s.Size.x * k / 2f, r.center.y - s.Size.y * k / 2f, s.Size.x * k, s.Size.y * k);
            var old = GUI.color;
            if (tint.HasValue) GUI.color = tint.Value;
            GUI.DrawTextureWithTexCoords(fit, Sheet, s.Uv);
            GUI.color = old;
        }
    }

    /// <summary>
    /// Sprites drawn as one dynamic mesh (one draw call a layer: the box's GPU pays per draw and per layer, see
    /// CLAUDE.md): add quads every frame (in the order they should be drawn), <see cref="Apply"/> once. World units are
    /// the HUD's pixels (1080 high, y up). Tints are given in sRGB and stored linear (the project is linear and the
    /// shader reads vertex colours as they are).
    /// </summary>
    public sealed class SpriteBatch
    {
        readonly Mesh _mesh;
        readonly List<Vector3> _verts = new List<Vector3>(2048);
        readonly List<Vector2> _uvs = new List<Vector2>(2048);
        readonly List<Color> _colours = new List<Color>(2048);
        readonly List<int> _tris = new List<int>(3072);
        readonly MeshRenderer _renderer;
        public float Z;

        public SpriteBatch(string name, Material material, Transform parent, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            Z = z;
        }

        public int Quads => _verts.Count / 4;
        public bool Visible { set => _renderer.enabled = value; }

        public void Clear()
        {
            _verts.Clear();
            _uvs.Clear();
            _colours.Clear();
            _tris.Clear();
        }

        /// <summary>
        /// A sprite at <paramref name="at"/> (where its pivot goes), one of its drawing units = <paramref name="unit"/>
        /// world pixels, turned <paramref name="angle"/> degrees counterclockwise around the pivot, scaled
        /// <paramref name="sx"/> by <paramref name="sy"/> (negative mirrors).
        /// </summary>
        public void Draw(ArcadeSprite s, Vector2 at, float unit, float angle = 0f, Color? tint = null, float sx = 1f, float sy = 1f)
        {
            float k = unit / s.Ppt;
            float w = s.Size.x * k * sx, h = s.Size.y * k * sy;
            float x0 = -s.Pivot.x * w, x1 = (1f - s.Pivot.x) * w, y1 = s.Pivot.y * h, y0 = -(1f - s.Pivot.y) * h;
            float r = angle * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
            Vector2 P(float x, float y) => new Vector2(at.x + x * c - y * sn, at.y + x * sn + y * c);
            var col = Lin(tint ?? Color.white);
            Quad(P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1), s.Uv, col, col, col, col);
        }

        /// <summary>A sprite stretched between two points (a trail's segment, a beam): <paramref name="width"/> across.</summary>
        public void Stretch(ArcadeSprite s, Vector2 a, Vector2 b, float width, Color ca, Color cb)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-4f) return;
            var n = new Vector2(-d.y, d.x).normalized * (width * 0.5f);
            Quad(a - n, b - n, b + n, a + n, s.Uv, Lin(ca), Lin(cb), Lin(cb), Lin(ca));
        }

        /// <summary>A panel sprite stretched over <paramref name="r"/> in nine pieces: its outer 30 % (of the sprite) keep
        /// their shape as corners <paramref name="border"/> world pixels wide, the middle stretches.</summary>
        public void Nine(ArcadeSprite s, Rect r, float border, Color tint)
        {
            border = Mathf.Min(border, r.width * 0.5f, r.height * 0.5f);
            var c = Lin(tint);
            float ub = 0.3f * s.Uv.width, vb = 0.3f * s.Uv.height;
            float[] xs = { r.xMin, r.xMin + border, r.xMax - border, r.xMax };
            float[] ys = { r.yMin, r.yMin + border, r.yMax - border, r.yMax };
            float[] us = { s.Uv.xMin, s.Uv.xMin + ub, s.Uv.xMax - ub, s.Uv.xMax };
            float[] vs = { s.Uv.yMin, s.Uv.yMin + vb, s.Uv.yMax - vb, s.Uv.yMax };
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                    Quad(new Vector2(xs[i], ys[j]), new Vector2(xs[i + 1], ys[j]), new Vector2(xs[i + 1], ys[j + 1]), new Vector2(xs[i], ys[j + 1]),
                         Rect.MinMaxRect(us[i], vs[j], us[i + 1], vs[j + 1]), c, c, c, c);
        }

        /// <summary>A rectangle in a gradient (bottom colour, top colour) from a white part of the sheet.</summary>
        public void Gradient(ArcadeSprite white, Rect r, Color bottom, Color top)
        {
            var uv = new Rect(white.Uv.center, Vector2.zero);
            Color b = Lin(bottom), t = Lin(top);
            Quad(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), uv, b, b, t, t);
        }

        /// <summary>Corners a (bottom left), b (bottom right), c (top right), d (top left); colours already linear.</summary>
        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Rect uv, Color ca, Color cb, Color cc, Color cd)
        {
            int i = _verts.Count;
            _verts.Add(new Vector3(a.x, a.y, Z));
            _verts.Add(new Vector3(b.x, b.y, Z));
            _verts.Add(new Vector3(c.x, c.y, Z));
            _verts.Add(new Vector3(d.x, d.y, Z));
            _uvs.Add(new Vector2(uv.xMin, uv.yMin));
            _uvs.Add(new Vector2(uv.xMax, uv.yMin));
            _uvs.Add(new Vector2(uv.xMax, uv.yMax));
            _uvs.Add(new Vector2(uv.xMin, uv.yMax));
            _colours.Add(ca);
            _colours.Add(cb);
            _colours.Add(cc);
            _colours.Add(cd);
            _tris.Add(i);
            _tris.Add(i + 2);
            _tris.Add(i + 1);
            _tris.Add(i);
            _tris.Add(i + 3);
            _tris.Add(i + 2);
        }

        public void Apply()
        {
            _mesh.Clear();
            if (_verts.Count == 0) return;
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colours);
            _mesh.SetTriangles(_tris, 0, false);
            _mesh.bounds = new Bounds(new Vector3(0f, 0f, Z), new Vector3(1e5f, 1e5f, 10f));
        }

        public static Color Lin(Color c)
        {
            var l = c.linear;
            l.a = c.a;
            return l;
        }
    }
}
