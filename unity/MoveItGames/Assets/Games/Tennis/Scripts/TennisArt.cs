using UnityEngine;

namespace MoveIt.Games.Tennis
{
    /// <summary>Shapes and textures drawn in code for Tenis!: a low-poly ball for the crowd's heads, a ring, the net's
    /// mesh texture, a round HUD dot.</summary>
    public static class TennisArt
    {
        static Mesh _lowSphere;

        /// <summary>A sphere of diameter 1 with few triangles (a crowd of hundreds of heads).</summary>
        public static Mesh LowSphere()
        {
            if (_lowSphere != null) return _lowSphere;
            const int segments = 8, rings = 5;
            var v = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float polar = Mathf.PI * r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float around = 2f * Mathf.PI * s / segments;
                    v.Add(new Vector3(Mathf.Sin(polar) * Mathf.Cos(around), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(around)) * 0.5f);
                }
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + segments + 1;
                if (r > 0) t.AddRange(new[] { a, a + 1, b });
                if (r < rings - 1) t.AddRange(new[] { a + 1, b + 1, b });
            }
            _lowSphere = new Mesh { name = "Low sphere" };
            _lowSphere.SetVertices(v);
            _lowSphere.SetNormals(v.ConvertAll(p => p.normalized));
            _lowSphere.SetTriangles(t, 0);
            return _lowSphere;
        }

        /// <summary>A flat ring in the XY plane (facing -z), outer radius 0.5, inner <paramref name="inner"/>.</summary>
        public static Mesh Ring(float inner = 0.4f, int segments = 48)
        {
            var v = new Vector3[segments * 2];
            var uv = new Vector2[segments * 2];
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a));
                v[i * 2] = d * inner;
                v[i * 2 + 1] = d * 0.5f;
                uv[i * 2] = uv[i * 2 + 1] = new Vector2(0.5f, 0.5f);
                int j = (i + 1) % segments;
                tris[i * 6] = i * 2;
                tris[i * 6 + 1] = i * 2 + 1;
                tris[i * 6 + 2] = j * 2 + 1;
                tris[i * 6 + 3] = i * 2;
                tris[i * 6 + 4] = j * 2 + 1;
                tris[i * 6 + 5] = j * 2;
            }
            var mesh = new Mesh { name = "Ring", vertices = v, uv = uv, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>One cell of the net: a dark cord along two edges (tiled across the net, with mipmaps it fades to a
        /// see-through grey far away).</summary>
        public static Texture2D NetCell(int size = 32)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Net cell", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                byte a = (byte)(Mathf.Clamp01(2.5f - d) * 230);
                px[y * size + x] = new Color32(28, 30, 52, a);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>A soft white dot (HUD: who serves).</summary>
        public static Texture2D Dot(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Dot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - 1.5f - d) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>The net's mesh: from post to post, sagging from <paramref name="post"/> high at the ends to
        /// <paramref name="centre"/> in the middle (two sided by the Fx shader), its UVs tiled every <paramref name="cell"/>
        /// metres.</summary>
        public static Mesh Net(float halfWidth, float centre, float post, float cell)
        {
            const int columns = 16;
            var v = new Vector3[(columns + 1) * 2];
            var uv = new Vector2[v.Length];
            var tris = new int[columns * 6];
            for (int i = 0; i <= columns; i++)
            {
                float x = Mathf.Lerp(-halfWidth, halfWidth, i / (float)columns);
                float k = Mathf.Abs(x) / halfWidth;
                float top = centre + (post - centre) * k * k - 0.04f;   // just under the tape
                v[i * 2] = new Vector3(x, 0.06f, 0f);
                v[i * 2 + 1] = new Vector3(x, top, 0f);
                uv[i * 2] = new Vector2(x / cell, 0f);
                uv[i * 2 + 1] = new Vector2(x / cell, top / cell);
                if (i == columns) continue;
                int a = i * 2;
                tris[i * 6] = a;
                tris[i * 6 + 1] = a + 1;
                tris[i * 6 + 2] = a + 3;
                tris[i * 6 + 3] = a;
                tris[i * 6 + 4] = a + 3;
                tris[i * 6 + 5] = a + 2;
            }
            var colors = new Color[v.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            var mesh = new Mesh { name = "Net", vertices = v, uv = uv, triangles = tris, colors = colors };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
