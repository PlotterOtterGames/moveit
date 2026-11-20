using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// Zana!'s meshes made in code: the fairies' wing panels (eight shapes, each a fan from its hinge with the gradient,
    /// the rim and the veins in its uvs for Tundu/Wing), spheres, quads, and the HUD sheet (tools/fairy/art.py's
    /// fairy_ui.png and its rectangles).
    /// </summary>
    public static class FairyArt
    {
        // ---------------------------------------------------------------- wings

        /// <summary>
        /// One wing panel in its own plane (x out from the hinge at the origin, y up), <paramref name="upper"/> the big
        /// one, else the lower; the right side (the left is drawn mirrored). uv0: x = how far round the outline (0..1,
        /// the veins), y = 0 at the hinge .. 1 at the rim (the gradient and the glowing edge). The shapes: 0 butterfly,
        /// 1 dragonfly, 2 petals, 3 hearts, 4 stars, 5 crystal, 6 feathers, 7 rainbow.
        /// </summary>
        public static Mesh WingPanel(int shape, bool upper)
        {
            var outline = Outline(shape, upper);
            int n = outline.Count;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            verts.Add(Vector3.zero);
            uvs.Add(new Vector2(0.5f, 0f));
            // two rings: halfway out and the outline itself (the gradient and the rim glow need the inner ring)
            for (int i = 0; i < n; i++)
            {
                var p = outline[i];
                float u = i / (float)(n - 1);
                verts.Add(new Vector3(p.x * 0.55f, p.y * 0.55f, 0f));
                uvs.Add(new Vector2(u, 0.55f));
                verts.Add(new Vector3(p.x, p.y, 0f));
                uvs.Add(new Vector2(u, 1f));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = 1 + i * 2, b = a + 2;
                tris.Add(0); tris.Add(a); tris.Add(b);
                tris.Add(a); tris.Add(a + 1); tris.Add(b + 1);
                tris.Add(a); tris.Add(b + 1); tris.Add(b);
            }
            var mesh = new Mesh { name = $"Wing {shape} {(upper ? "upper" : "lower")}" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>sin(pi t), never below 0: at t = 1 the float sine is a hair under zero, and a fractional power of it
        /// is NaN (a wing with no bounds).</summary>
        static float Sin01(float t) => Mathf.Max(0f, Mathf.Sin(t * Mathf.PI));

        /// <summary>The panel's outline from the hinge round and back (m, a fairy's wing ~0.5 m).</summary>
        static List<Vector2> Outline(int shape, bool upper)
        {
            var pts = new List<Vector2>();
            // an outline as a radius round the hinge from angle a0 to a1 (degrees, 0 = straight out, up positive)
            void Polar(float a0, float a1, Func<float, float> radius, int steps = 40)
            {
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float a = Mathf.Lerp(a0, a1, t) * Mathf.Deg2Rad;
                    float r = radius(t);
                    pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
            float s = upper ? 1f : 0.68f;
            switch (shape)
            {
                case 1: // dragonfly: long and narrow
                    Polar(upper ? 2f : -32f, upper ? 26f : -8f, t => s * 0.62f * Mathf.Pow(Sin01(t), 0.35f) + 0.02f);
                    break;
                case 2: // petals: pointed
                    Polar(upper ? -5f : -62f, upper ? 70f : -8f, t => s * 0.5f * Mathf.Pow(Sin01(t), 0.8f) * (1f + 0.25f * Mathf.Sin(t * Mathf.PI)) + 0.02f);
                    break;
                case 3: // hearts: two lobes on the upper wing
                    Polar(upper ? -5f : -60f, upper ? 75f : -10f, t =>
                    {
                        float lobes = upper ? 0.18f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) : 0f;
                        return s * (0.36f + lobes) * Mathf.Pow(Sin01(t), 0.45f) + 0.02f;
                    });
                    break;
                case 4: // stars: three points
                    Polar(upper ? -5f : -60f, upper ? 75f : -8f, t => s * (0.38f + 0.11f * Mathf.Pow(Mathf.Abs(Mathf.Cos(t * Mathf.PI * 3f)), 4f)) * Mathf.Pow(Sin01(t), 0.4f) + 0.02f);
                    break;
                case 5: // crystal: facets
                    Polar(upper ? -5f : -60f, upper ? 75f : -8f, t =>
                    {
                        float k = Mathf.Floor(t * 5f) / 5f;
                        float f = Mathf.Lerp(0.85f, 1f, Mathf.Abs(t * 5f - Mathf.Floor(t * 5f) - 0.5f) * 2f);
                        return s * 0.47f * Mathf.Pow(Sin01(t), 0.35f) * f * (0.9f + 0.1f * k) + 0.02f;
                    }, 30);
                    break;
                case 6: // feathers: a scalloped edge
                    Polar(upper ? -10f : -60f, upper ? 70f : -12f, t => s * (0.46f + 0.04f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 7f))) * Mathf.Pow(Sin01(t), 0.4f) + 0.02f, 56);
                    break;
                case 7: // rainbow: big and round
                    Polar(upper ? -8f : -66f, upper ? 80f : -8f, t => s * 0.54f * Mathf.Pow(Sin01(t), 0.4f) + 0.02f);
                    break;
                default: // butterfly: round lobes
                    Polar(upper ? -5f : -62f, upper ? 72f : -6f, t => s * 0.46f * Mathf.Pow(Sin01(t), 0.42f) * (1f + 0.12f * Mathf.Sin(t * Mathf.PI * 2f)) + 0.02f);
                    break;
            }
            return pts;
        }

        // ---------------------------------------------------------------- simple meshes

        /// <summary>A unit sphere (radius 1).</summary>
        public static Mesh Sphere(int segments = 24, int rings = 16)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float v = j / (float)rings;
                float lat = Mathf.PI * (0.5f - v);
                for (int i = 0; i <= segments; i++)
                {
                    float u = i / (float)segments;
                    float lon = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    verts.Add(n);
                    normals.Add(n);
                    uvs.Add(new Vector2(u, 1f - v));
                }
            }
            int row = segments + 1;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < segments; i++)
                {
                    int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            var mesh = new Mesh { name = "Sphere" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A quad facing +Z (1 x 1, its middle at the origin), uvs 0..1.</summary>
        public static Mesh Quad()
        {
            var mesh = new Mesh { name = "Quad" };
            mesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A soft strip for ribbons (the spell trails): 64 x 8, white, alpha highest down the middle.</summary>
        public static Texture2D RibbonTexture()
        {
            var tex = new Texture2D(8, 64, TextureFormat.RGBA32, false, true) { name = "Ribbon", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[8 * 64];
            for (int y = 0; y < 64; y++)
            {
                float v = Mathf.Abs(y / 63f * 2f - 1f);
                float core = Mathf.Clamp01(1f - v * 3.2f);
                float glow = Mathf.Pow(1f - v, 2.2f);
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(glow * 0.55f + core) * 255f);
                byte w = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.75f + core * 0.25f) * 255f);
                for (int x = 0; x < 8; x++) px[y * 8 + x] = new Color32(w, w, w, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ---------------------------------------------------------------- the HUD sheet

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
        }

        /// <summary>The HUD sheet's pictures by name: uv rectangles (0..1, y up).</summary>
        public static Dictionary<string, Rect> Sheet(TextAsset json)
        {
            var map = new Dictionary<string, Rect>();
            if (json == null) return map;
            var data = JsonUtility.FromJson<SheetJson>(json.text);
            if (data?.sprites == null || data.size == null || data.size.Length < 2) return map;
            float w = data.size[0], h = data.size[1];
            foreach (var s in data.sprites)
            {
                if (s.rect == null || s.rect.Length < 4) continue;
                float x = (s.rect[0] + 0.5f) / w, top = s.rect[1] + 0.5f, width = (s.rect[2] - 1f) / w, height = s.rect[3] - 1f;
                map[s.name] = new Rect(x, 1f - (top + height) / h, width, height / h);
            }
            return map;
        }

        /// <summary>A spell's picture in the sheet.</summary>
        public static string SpellSprite(Spell s) => s switch
        {
            Spell.Shield => "spell_shield",
            Spell.Seeds => "spell_seeds",
            Spell.Freeze => "spell_freeze",
            Spell.Heal => "spell_heal",
            Spell.Lightning => "spell_lightning",
            Spell.Rainbow => "spell_rainbow",
            _ => "spell_burst",
        };
    }
}
