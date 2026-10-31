using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>Shapes and little textures drawn in code for the activity games (Aventura!, Sporte!): rings, discs, a coin,
    /// a medal, a soft dot, a heart, a star, and strips that follow a path (a river, a track).</summary>
    public static class ArtShapes
    {
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

        /// <summary>A coin (an "adventure pin"): a gold rim, a sky face and a white star, standing in the XY plane, its
        /// face toward -z and +z, diameter <paramref name="size"/>. Vertex colours (Tundu/Toon with vertex colours).</summary>
        public static Mesh Coin(float size, Color face)
        {
            var gold = new Color(1f, 0.79f, 0.24f);
            var m = new PartMesh { Srgb = true }
                .Add(PrimitiveType.Cylinder, Vector3.zero, new Vector3(size, size * 0.06f, size), gold, 1.2f, new Vector3(90f, 0f, 0f))
                .Add(PrimitiveType.Cylinder, Vector3.zero, new Vector3(size * 0.78f, size * 0.075f, size * 0.78f), face, 0.8f, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 5; i++)
            {
                float a = 90f + i * 72f;
                var dir = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad), 0f);
                foreach (float z in new[] { -1f, 1f })
                    m.Add(PrimitiveType.Capsule, dir * size * 0.15f + new Vector3(0f, 0f, z * size * 0.08f),
                          new Vector3(size * 0.13f, size * 0.17f, size * 0.02f), Color.white, 0.6f, new Vector3(0f, 0f, a - 90f));
            }
            return m.Build("Coin");
        }

        /// <summary>A soft white dot (HUD).</summary>
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

        /// <summary>A medal for the HUD: a round disc with a darker rim, a raised star and a ribbon above it (white:
        /// GUI.color tints it).</summary>
        public static Texture2D Medal(int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Medal", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float cx = size * 0.5f, cy = size * 0.42f, r = size * 0.36f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                Color c = Color.clear;
                // the ribbon: two bands from the top down behind the disc
                float band = Mathf.Abs(Mathf.Abs(fx - cx) - size * 0.12f);
                if (fy > cy && band < size * 0.08f) c = new Color(0.25f, 0.45f, 0.95f, 1f) * (fx < cx ? 0.85f : 1f);
                if (d < r)
                {
                    float shade = d > r * 0.82f ? 0.72f : 1f;
                    // a five-pointed star in the middle, a little brighter
                    float a = Mathf.Atan2(fy - cy, fx - cx) + Mathf.PI / 2f;
                    float k = Mathf.Cos(Mathf.PI / 5f) / Mathf.Cos(Mathf.Repeat(a, 2f * Mathf.PI / 5f) - Mathf.PI / 5f);
                    float star = Mathf.Lerp(r * 0.24f, r * 0.58f, Mathf.Pow(Mathf.Max(0f, k - 0.809f) / 0.191f, 1.5f));
                    if (d < star) shade = 1.15f;
                    c = new Color(shade, shade, shade, 1f);
                    c.a = Mathf.Clamp01(r - d);
                }
                px[y * size + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>A heart for the HUD (white).</summary>
        public static Texture2D Heart(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Heart", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2.6f - 1.3f, v = (y + 0.5f) / size * 2.6f - 1.25f;
                float f = Mathf.Pow(u * u + v * v - 1f, 3f) - u * u * v * v * v;
                byte a = (byte)(Mathf.Clamp01(-f * 18f) * 255);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// A strip along a path: for each sample along it (<paramref name="along"/>), a row of points across
        /// (<paramref name="across"/> gives the point and its colour for row i, column k), joined into quads. UVs: x =
        /// column / columns, y = the distance along (metres). Vertex colour alpha comes from the colours given.
        /// </summary>
        public static Mesh Strip(string name, int rows, int columns, System.Func<int, int, (Vector3 p, Color c)> across, bool linearColours = true)
        {
            var v = new List<Vector3>(rows * columns);
            var col = new List<Color>(rows * columns);
            var uv = new List<Vector2>(rows * columns);
            var t = new List<int>((rows - 1) * (columns - 1) * 6);
            float along = 0f;
            Vector3 last = Vector3.zero;
            for (int i = 0; i < rows; i++)
            {
                var mid = across(i, columns / 2).p;
                if (i > 0) along += Vector3.Distance(new Vector3(mid.x, 0f, mid.z), new Vector3(last.x, 0f, last.z));
                last = mid;
                for (int k = 0; k < columns; k++)
                {
                    var (p, c) = across(i, k);
                    v.Add(p);
                    col.Add(linearColours ? new Color(c.linear.r, c.linear.g, c.linear.b, c.a) : c);
                    uv.Add(new Vector2(k / (float)(columns - 1), along));
                }
            }
            for (int i = 0; i < rows - 1; i++)
            for (int k = 0; k < columns - 1; k++)
            {
                int a = i * columns + k, b = a + columns;
                t.Add(a); t.Add(b); t.Add(a + 1);
                t.Add(a + 1); t.Add(b); t.Add(b + 1);
            }
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetColors(col);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
