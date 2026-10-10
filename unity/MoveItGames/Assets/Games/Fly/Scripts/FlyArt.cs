using UnityEngine;

namespace MoveIt.Games.Fly
{
    /// <summary>Shapes drawn in code: the reach ring, a star, and the HUD heart.</summary>
    public static class FlyArt
    {
        /// <summary>A flat ring in the XY plane, outer radius 0.5, inner <paramref name="inner"/>.</summary>
        public static Mesh Ring(float inner = 0.4f, int segments = 48)
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
            return Make("Ring", v, tris, false);
        }

        /// <summary>A chunky five-pointed star, 1 unit across, facing -z (the camera), <paramref name="depth"/> thick.</summary>
        public static Mesh Star(float depth = 0.25f, float inner = 0.45f)
        {
            const int points = 5;
            int rim = points * 2;
            // front centre, back centre, then the rim front and back
            var v = new Vector3[2 + rim * 2];
            v[0] = new Vector3(0f, 0f, -depth * 0.5f);
            v[1] = new Vector3(0f, 0f, depth * 0.5f);
            for (int i = 0; i < rim; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / points;
                float r = i % 2 == 0 ? 0.5f : 0.5f * inner;
                var p = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                v[2 + i] = p + new Vector3(0f, 0f, -depth * 0.2f);
                v[2 + rim + i] = p + new Vector3(0f, 0f, depth * 0.2f);
            }
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < rim; i++)
            {
                int j = (i + 1) % rim;
                tris.AddRange(new[] { 0, 2 + j, 2 + i });                 // front (toward -z)
                tris.AddRange(new[] { 1, 2 + rim + i, 2 + rim + j });     // back
                tris.AddRange(new[] { 2 + i, 2 + j, 2 + rim + j });       // sides
                tris.AddRange(new[] { 2 + i, 2 + rim + j, 2 + rim + i });
            }
            return Make("Star", v, tris.ToArray(), true);
        }

        static Mesh Make(string name, Vector3[] vertices, int[] triangles, bool normals)
        {
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(0.5f, 0.5f);   // the middle of a plain texture
            mesh.uv = uv;
            if (normals) mesh.RecalculateNormals();
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
                    float w = ((y + (sy + 0.5f) / 3f) / size - 0.45f) * 2.6f;
                    float a = u * u + w * w - 1f;
                    if (a * a * a - u * u * w * w * w <= 0f) inside++;
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 9));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
