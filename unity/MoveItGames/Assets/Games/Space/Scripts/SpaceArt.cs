using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Space
{
    /// <summary>
    /// Yjet!'s meshes made in code (spheres with equirectangular uvs for the planets, flat rings, tori for the star rings,
    /// the pose gates' outlines, the star field, the jetpack's flames, a disc for the landing target) and the HUD sheet
    /// (tools/space/art.py's space_ui.png with its rectangles).
    /// </summary>
    public static class SpaceArt
    {
        /// <summary>A unit sphere (radius 1) with uvs for an equirectangular map (the seam doubled, so it wraps cleanly).</summary>
        public static Mesh Sphere(int segments = 48, int rings = 28)
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
                    float lon = u * Mathf.PI * 2f - Mathf.PI;
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
            var mesh = new Mesh { name = "Planet sphere" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat ring in the XZ plane from <paramref name="inner"/> to <paramref name="outer"/>; uv x runs out
        /// across it (0 inside .. 1 outside), uv y round it.</summary>
        public static Mesh FlatRing(float inner, float outer, int segments = 96)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(d * inner);
                verts.Add(d * outer);
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, i / (float)segments));
                uvs.Add(new Vector2(1f, i / (float)segments));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
            }
            var mesh = new Mesh { name = "Flat ring" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat disc in the XZ plane, uvs 0..1 across it (the landing target).</summary>
        public static Mesh Disc(float radius, int segments = 48)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
                uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
            }
            for (int i = 1; i <= segments; i++)
            {
                tris.Add(0); tris.Add(i + 1); tris.Add(i);
            }
            var mesh = new Mesh { name = "Disc" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A torus standing up facing +Z (the astronaut flies through it): radius <paramref name="big"/>, tube
        /// <paramref name="small"/>.</summary>
        public static Mesh Torus(float big, float small, int segments = 40, int sides = 10)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2f;
                    var n = dir * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    verts.Add(dir * big + n * small);
                    normals.Add(n);
                }
            }
            int row = sides + 1;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * row + j, b = a + row;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            var mesh = new Mesh { name = "Ring" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A closed outline (points in the XY plane, metres) as a round tube, facing +Z: a pose gate's frame.</summary>
        public static Mesh Outline(IList<Vector2> outline, float radius, int sides = 8, string name = "Gate")
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            int n = outline.Count;
            for (int i = 0; i <= n; i++)
            {
                var p = outline[i % n];
                var prev = outline[(i - 1 + n) % n];
                var next = outline[(i + 1) % n];
                var tangent = ((Vector2)(next - prev)).normalized;
                var side = new Vector3(tangent.y, -tangent.x, 0f);   // in the plane, across the line
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2f;
                    var nn = side * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    verts.Add(new Vector3(p.x, p.y, 0f) + nn * radius);
                    normals.Add(nn);
                }
            }
            int row = sides + 1;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * row + j, b = a + row;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The star field: <paramref name="count"/> stars on a sphere of <paramref name="radius"/> round the origin, each a
        /// quad for Tundu/Stars (uv0 its corner, uv1 its size and twinkle phase, its colour: mostly white, some warm, some
        /// blue, a few bright ones), denser in a band across the sky like a milky way.
        /// </summary>
        public static Mesh StarField(int count, float radius, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var verts = new List<Vector3>(count * 4);
            var corners = new List<Vector2>(count * 4);
            var info = new List<Vector2>(count * 4);
            var colours = new List<Color>(count * 4);
            var tris = new List<int>(count * 6);
            var band = new Vector3(0.3f, 0.85f, 0.42f).normalized;
            Color[] tints = { new Color(1f, 1f, 1f), new Color(1f, 0.92f, 0.78f), new Color(0.78f, 0.88f, 1f), new Color(1f, 0.82f, 0.92f) };
            for (int i = 0; i < count; i++)
            {
                Vector3 d;
                do d = new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f);
                while (d.sqrMagnitude > 1f || d.sqrMagnitude < 0.01f);
                d.Normalize();
                // half the stars pulled toward the band
                if (i % 2 == 0) d = (d - band * Vector3.Dot(d, band) * 0.85f).normalized;
                float bright = R();
                float size = radius * (0.0022f + 0.0035f * bright * bright + (bright > 0.97f ? 0.006f : 0f));
                var tint = tints[rng.Next(tints.Length)];
                tint.a = 0.35f + 0.65f * bright;
                int b = verts.Count;
                foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                {
                    verts.Add(d * radius);
                    corners.Add(c);
                    info.Add(new Vector2(size, R()));
                    colours.Add(tint);
                }
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            var mesh = new Mesh { name = "Star field", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, info);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2.2f);
            return mesh;
        }

        /// <summary>A jetpack flame: a cone from the nozzle (the origin) down along -Y, one unit long, bright yellow at the
        /// nozzle fading through coral to nothing at the tip (vertex colours, for Tundu/Fx added on).</summary>
        public static Mesh Flame(int segments = 14)
        {
            var verts = new List<Vector3>();
            var colours = new List<Color>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float[] ys = { 0f, -0.18f, -0.5f, -1f };
            float[] rs = { 0.055f, 0.075f, 0.05f, 0.0f };
            Color[] cs = { new Color(1f, 0.98f, 0.75f, 1f), new Color(1f, 0.82f, 0.3f, 0.95f), new Color(1f, 0.42f, 0.22f, 0.55f), new Color(1f, 0.25f, 0.3f, 0f) };
            for (int r = 0; r < ys.Length; r++)
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * rs[r], ys[r], Mathf.Sin(a) * rs[r]));
                    colours.Add(cs[r]);
                    uvs.Add(new Vector2(0.5f, 0.5f));
                }
            int row = segments + 1;
            for (int r = 0; r < ys.Length - 1; r++)
                for (int i = 0; i < segments; i++)
                {
                    int a = r * row + i, b = a + row;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            var mesh = new Mesh { name = "Flame" };
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A quad facing +Z (1 x 1, its middle at the origin), uvs 0..1: billboards, signs.</summary>
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

        /// <summary>A quad showing one picture of the HUD sheet (its uv rectangle), facing +Z, <paramref name="size"/> m.</summary>
        public static Mesh Sign(Rect uv, float size)
        {
            var mesh = Quad();
            mesh.name = "Sign";
            mesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f) * size, new Vector3(0.5f, -0.5f, 0f) * size, new Vector3(0.5f, 0.5f, 0f) * size, new Vector3(-0.5f, 0.5f, 0f) * size });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax) });
            mesh.RecalculateBounds();
            return mesh;
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

        /// <summary>The HUD sheet's pictures by name: uv rectangles (0..1, y up, like
        /// <see cref="GUI.DrawTextureWithTexCoords(Rect, Texture, Rect)"/>'s).</summary>
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
                // the json's rectangles are pixels from the top; uvs from the bottom (a texel's inset against bleeding)
                float x = (s.rect[0] + 0.5f) / w, top = s.rect[1] + 0.5f, width = (s.rect[2] - 1f) / w, height = s.rect[3] - 1f;
                map[s.name] = new Rect(x, 1f - (top + height) / h, width, height / h);
            }
            return map;
        }
    }
}
