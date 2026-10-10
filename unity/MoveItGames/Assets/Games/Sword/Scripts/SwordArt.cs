using UnityEngine;

namespace MoveIt.Games.Sword
{
    /// <summary>Shapes drawn in code: the shield guide (disc and ring), the "!" alert, and the HUD heart.</summary>
    public static class SwordArt
    {
        /// <summary>A flat disc in the XY plane, radius 0.5.</summary>
        public static Mesh Disc(int segments = 40)
        {
            var v = new Vector3[segments + 1];
            var tris = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f;
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segments + 1;
            }
            return Make("Disc", v, tris);
        }

        /// <summary>A flat ring in the XY plane, outer radius 0.5, inner <paramref name="inner"/>.</summary>
        public static Mesh Ring(float inner = 0.4f, int segments = 40)
        {
            var v = new Vector3[segments * 2];
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a));
                v[i * 2] = d * inner;
                v[i * 2 + 1] = d * 0.5f;
                int j = (i + 1) % segments;
                tris[i * 6] = i * 2;
                tris[i * 6 + 1] = i * 2 + 1;
                tris[i * 6 + 2] = j * 2 + 1;
                tris[i * 6 + 3] = i * 2;
                tris[i * 6 + 4] = j * 2 + 1;
                tris[i * 6 + 5] = j * 2;
            }
            return Make("Ring", v, tris);
        }

        /// <summary>"!" in the XY plane, 1 unit high.</summary>
        public static Mesh Alert()
        {
            var v = new[]
            {
                new Vector3(-0.09f, -0.12f), new Vector3(0.09f, -0.12f), new Vector3(0.12f, 0.5f), new Vector3(-0.12f, 0.5f),
                new Vector3(-0.09f, -0.5f), new Vector3(0.09f, -0.5f), new Vector3(0.09f, -0.32f), new Vector3(-0.09f, -0.32f),
            };
            return Make("Alert", v, new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 });
        }

        static Mesh Make(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(0.5f, 0.5f);   // the middle of a plain texture
            mesh.uv = uv;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A soft-edged heart, white (tinted when drawn).</summary>
        public static Texture2D Heart(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Heart", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // the classic heart curve (x² + y² − 1)³ − x² y³ ≤ 0, sampled 3×3 for smooth edges
                int inside = 0;
                for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                {
                    float u = ((x + (sx + 0.5f) / 3f) / size - 0.5f) * 2.6f;
                    float v = ((y + (sy + 0.5f) / 3f) / size - 0.45f) * 2.6f;
                    float a = u * u + v * v - 1f;
                    if (a * a * a - u * u * v * v * v <= 0f) inside++;
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 9));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
