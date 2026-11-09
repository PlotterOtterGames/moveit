using UnityEngine;

namespace MoveIt.Games.Volley
{
    /// <summary>
    /// Volejboll!'s pictures drawn in code: the three moves as little figures (the waiting screen teaches them, and the one
    /// a coming ball wants shows over the toy that plays it, so a child who can't read yet sees what to do), and the net's
    /// mesh between the posts.
    /// </summary>
    public static class VolleyArt
    {
        /// <summary>A figure making <paramref name="move"/>, white on clear, <paramref name="size"/> px square: hands
        /// together low (the bump), both up (the set), one swinging down past a ball (the hit).</summary>
        public static Texture2D MoveIcon(Move move, int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = $"Move {move}", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float k = size / 100f;
            // in a 100 x 100 box, y down
            var segments = new System.Collections.Generic.List<(Vector2 a, Vector2 b, float w)>
            {
                (new Vector2(50f, 40f), new Vector2(50f, 66f), 6f),    // the body
                (new Vector2(50f, 66f), new Vector2(40f, 92f), 5.5f),  // the legs
                (new Vector2(50f, 66f), new Vector2(60f, 92f), 5.5f),
            };
            var dots = new System.Collections.Generic.List<(Vector2 c, float r, bool ring)> { (new Vector2(50f, 25f), 11f, false) };
            switch (move)
            {
                case Move.Bump:
                    segments.Add((new Vector2(44f, 44f), new Vector2(48.5f, 68f), 5f));
                    segments.Add((new Vector2(56f, 44f), new Vector2(51.5f, 68f), 5f));
                    dots.Add((new Vector2(50f, 77f), 7.5f, true));
                    break;
                case Move.Set:
                    segments.Add((new Vector2(44f, 43f), new Vector2(33f, 17f), 5f));
                    segments.Add((new Vector2(56f, 43f), new Vector2(67f, 17f), 5f));
                    dots.Add((new Vector2(50f, 8f), 7.5f, true));
                    break;
                default:
                    segments.Add((new Vector2(44f, 43f), new Vector2(36f, 64f), 5f));
                    segments.Add((new Vector2(56f, 43f), new Vector2(72f, 26f), 5f));
                    dots.Add((new Vector2(82f, 16f), 7.5f, true));
                    // the swing's streaks
                    segments.Add((new Vector2(62f, 12f), new Vector2(70f, 7f), 2.2f));
                    segments.Add((new Vector2(86f, 32f), new Vector2(92f, 38f), 2.2f));
                    break;
            }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / k, (size - 1 - y + 0.5f) / k);
                float a = 0f;
                foreach (var (sa, sb, w) in segments) a = Mathf.Max(a, Mathf.Clamp01((w * 0.5f + 0.6f - Segment(p, sa, sb)) * k));
                foreach (var (c, r, ring) in dots)
                {
                    float d = (p - c).magnitude;
                    a = Mathf.Max(a, ring ? Mathf.Clamp01((1.6f + 0.6f - Mathf.Abs(d - r)) * k) : Mathf.Clamp01((r + 0.6f - d) * k));
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>The net's mesh between the posts (x ±<paramref name="half"/>), from <paramref name="bottom"/> to
        /// <paramref name="top"/>, its uv in cells of <paramref name="cell"/> m (Tundu/Net draws the cords).</summary>
        public static Mesh NetMesh(float half, float bottom, float top, float cell)
        {
            var mesh = new Mesh { name = "Volleyball net" };
            mesh.SetVertices(new[] { new Vector3(-half, bottom, 0f), new Vector3(-half, top, 0f), new Vector3(half, top, 0f), new Vector3(half, bottom, 0f) });
            float u = 2f * half / cell, v = (top - bottom) / cell;
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(0f, v), new Vector2(u, v), new Vector2(u, 0f) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
