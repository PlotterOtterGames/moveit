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
            // 6 x 4 (was 8 x 5): ~600 spectators' heads and hair, a few pixels each, were ~65k vertices, as much as the
            // four players together; the box's GPU pays per vertex, twice with the two-player split screen (2026-11-05)
            const int segments = 6, rings = 4;
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
                px[y * size + x] = new Color32(32, 42, 34, a);   // dark green-grey, like the walls
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// The tournament's cup as a 3D shape (the champion screen, 2026-11-07), 1 m tall, standing on y 0: a dark plinth,
        /// a gold foot, stem and bowl turned round the vertical (a lathe), a lip, two looped handles. Vertex colours (rgb
        /// linear, alpha = gloss) for Tundu/Toon's merged-parts options: one draw call, ~1500 triangles.
        /// </summary>
        public static Mesh CupMesh()
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var c = new System.Collections.Generic.List<Color>();
            var t = new System.Collections.Generic.List<int>();
            var gold = new Color(1f, 0.74f, 0.2f).linear;
            gold.a = 0.95f;
            var plinth = new Color(0.14f, 0.19f, 0.37f).linear;
            plinth.a = 0.35f;

            // a grid of rings: vertex (i, k) at index start + i * (around + 1) + k, rings going up (or round), k going round
            // to the viewer's right seen from outside: clockwise from outside is (a, b, a + 1) (Unity's front faces)
            void Grid(int start, int rings, int around)
            {
                for (int i = 0; i < rings - 1; i++)
                    for (int k = 0; k < around; k++)
                    {
                        int a = start + i * (around + 1) + k, b = a + around + 1;
                        t.Add(a); t.Add(b); t.Add(a + 1);
                        t.Add(a + 1); t.Add(b); t.Add(b + 1);
                    }
            }

            // a profile (radius, height) turned round the vertical; its normals from the profile (to the right of its way:
            // out on the way up, up on the way in, in on the way down inside the bowl)
            void Lathe(Vector2[] profile, Color colour, int segments = 28)
            {
                int start = v.Count;
                for (int i = 0; i < profile.Length; i++)
                {
                    var tangent = profile[Mathf.Min(i + 1, profile.Length - 1)] - profile[Mathf.Max(i - 1, 0)];
                    var normal = new Vector2(tangent.y, -tangent.x).normalized;
                    for (int s = 0; s <= segments; s++)
                    {
                        float a = s * Mathf.PI * 2f / segments;
                        float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                        v.Add(new Vector3(cos * profile[i].x, profile[i].y, sin * profile[i].x));
                        n.Add(new Vector3(cos * normal.x, normal.y, sin * normal.x));
                        c.Add(colour);
                    }
                }
                Grid(start, profile.Length, segments);
            }

            Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.27f, 0f), new Vector2(0.28f, 0.02f), new Vector2(0.28f, 0.11f), new Vector2(0.25f, 0.13f),
                new Vector2(0.18f, 0.14f),
            }, plinth);
            Lathe(new[]
            {
                new Vector2(0.18f, 0.14f), new Vector2(0.2f, 0.16f), new Vector2(0.16f, 0.2f), new Vector2(0.08f, 0.23f), new Vector2(0.05f, 0.28f),
                new Vector2(0.045f, 0.4f), new Vector2(0.06f, 0.44f), new Vector2(0.1f, 0.47f), new Vector2(0.07f, 0.5f), new Vector2(0.12f, 0.53f),
                new Vector2(0.22f, 0.58f), new Vector2(0.3f, 0.66f), new Vector2(0.34f, 0.76f), new Vector2(0.355f, 0.88f), new Vector2(0.37f, 0.94f),
                new Vector2(0.38f, 0.96f), new Vector2(0.35f, 0.97f), new Vector2(0.32f, 0.9f), new Vector2(0.27f, 0.75f), new Vector2(0f, 0.7f),
            }, gold);

            // the handles: loops of tube on both sides of the bowl (the same winding on both: the rings go round the same
            // way seen from each one's outside)
            const int along = 14, around = 8;
            foreach (float side in new[] { -1f, 1f })
            {
                int start = v.Count;
                for (int i = 0; i <= along; i++)
                {
                    float a = Mathf.Lerp(-1.7f, 1.7f, i / (float)along);   // round the loop's outer side, bottom to top
                    var centre = new Vector3(side * (0.34f + Mathf.Cos(a) * 0.14f), 0.76f + Mathf.Sin(a) * 0.15f, 0f);
                    var tangent = new Vector3(-side * Mathf.Sin(a) * 0.14f, Mathf.Cos(a) * 0.15f, 0f).normalized;
                    var n1 = Vector3.Cross(tangent, Vector3.forward).normalized;
                    for (int k = 0; k <= around; k++)
                    {
                        float b = k * Mathf.PI * 2f / around;
                        var offset = n1 * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                        v.Add(centre + offset * 0.035f);
                        n.Add(offset);
                        c.Add(gold);
                    }
                }
                Grid(start, along + 1, around);
            }

            var mesh = new Mesh { name = "Cup" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The shadows' atlas (<see cref="TennisFx"/>, the stadium's fixed shadows): three cells side by side, white with
        /// the shape in alpha: solid (sampled at its middle: strips whose softness is in their vertices), a soft round blob,
        /// and a toy's shadow along v (its feet at v 0: a rounded body widening to the big round head at v 1), each with a
        /// clear border so mipmaps don't bleed between them.
        /// </summary>
        public static Texture2D ShadowAtlas(int cell = 64)
        {
            int w = cell * 3, h = cell * 2;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Shadow atlas", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int c = x / cell;
                float u = (x % cell + 0.5f) / cell * 2f - 1f;   // -1..1 across the cell
                float v = (y + 0.5f) / h;                       // 0..1 along it
                float a;
                if (c == 0) a = Mathf.Abs(u) < 0.7f && v > 0.15f && v < 0.85f ? 1f : 0f;
                else if (c == 1)
                {
                    float r = Mathf.Sqrt(u * u + (v * 2f - 1f) * (v * 2f - 1f));
                    a = Soft(1f - r, 0.55f);
                }
                else
                {
                    // a toy seen from the sun: the big round head's shadow near the far end, nearly as wide as the cell
                    // (the chibi's head is ~0.5 m across), the body narrowing from the shoulders to the feet at v ~0.1
                    float du = u / 0.92f, dv = (v - 0.7f) / 0.17f;
                    float head = Soft(1f - Mathf.Sqrt(du * du + dv * dv), 0.35f);
                    float half = Mathf.Lerp(0.4f, 0.66f, Mathf.Clamp01((v - 0.1f) / 0.5f));
                    float body = v > 0.06f && v < 0.66f ? Soft(1f - Mathf.Abs(u) / half, 0.45f) * Mathf.SmoothStep(0f, 1f, (v - 0.06f) / 0.1f) : 0f;
                    a = Mathf.Max(head, body);
                }
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>A soft edge: 0 at <paramref name="d"/> 0 rising to 1 over <paramref name="width"/>.</summary>
        static float Soft(float d, float width) => Mathf.SmoothStep(0f, 1f, d / width);

        /// <summary>
        /// The grass's detail (Tundu/Toon's world-space detail on the stadium: one channel, ~0.5 average, tiled every few
        /// metres): soft patches of lighter and darker grass and faint streaks along the mowing, so the court isn't a flat
        /// colour. Value noise in a few octaves, made to tile.
        /// </summary>
        public static Texture2D GrassDetail(int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.R8, true) { name = "Grass detail", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new byte[size * size];
            var rng = new System.Random(11);
            const int lattice = 64;
            var grid = new float[lattice * lattice];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)rng.NextDouble();
            float Noise(float x, float y, int cx, int cy)
            {
                // value noise on a lattice of cx x cy cells that wraps (the texture tiles)
                x *= cx; y *= cy;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                fx = fx * fx * (3f - 2f * fx);
                fy = fy * fy * (3f - 2f * fy);
                float G(int gx, int gy) => grid[(((gy % cy) + cy) % cy) * lattice + (((gx % cx) + cx) % cx)];
                return Mathf.Lerp(Mathf.Lerp(G(x0, y0), G(x0 + 1, y0), fx), Mathf.Lerp(G(x0, y0 + 1), G(x0 + 1, y0 + 1), fx), fy);
            }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float n = 0.5f * Noise(u, v, 4, 4) + 0.3f * Noise(u, v, 12, 12) + 0.2f * Noise(u, v, 40, 40);
                // streaks along the mowing (court z = the texture's v): noise twelve times finer across than along
                float streak = Noise(u, v, 48, 4);
                float value = 0.5f + (n - 0.5f) * 0.9f + (streak - 0.5f) * 0.25f;
                px[y * size + x] = (byte)(Mathf.Clamp01(value) * 255f);
            }
            tex.SetPixelData(px, 0);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// A gold cup (the HUD's tournaments won): a bowl widening upward with two handles, a stem and a base, lit from the
        /// left; 4x4 samples a pixel for smooth edges.
        /// </summary>
        public static Texture2D Cup(int size = 96)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Cup", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            Color gold = new Color(1f, 0.79f, 0.24f), light = new Color(1f, 0.92f, 0.6f), dark = new Color(0.86f, 0.58f, 0.12f);
            const int n = 4;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cover = 0f;
                Color sum = Color.clear;
                for (int sy = 0; sy < n; sy++)
                for (int sx = 0; sx < n; sx++)
                {
                    float u = (x + (sx + 0.5f) / n) / size, v = (y + (sy + 0.5f) / n) / size;   // v up
                    if (!InCup(u, v, out float shade)) continue;
                    cover += 1f;
                    sum += shade < 0f ? Color.Lerp(gold, light, -shade) : Color.Lerp(gold, dark, shade);
                }
                if (cover <= 0f) { px[y * size + x] = new Color32(0, 0, 0, 0); continue; }
                var c = sum / cover;
                c.a = cover / (n * n);
                px[y * size + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Whether (u, v) (0..1, v up) is inside the cup, and its shade there (-1 lit .. +1 dark).</summary>
        static bool InCup(float u, float v, out float shade)
        {
            float dx = u - 0.5f;
            shade = Mathf.Clamp(dx * 3.2f, -1f, 1f);
            // the bowl: from the stem (v 0.44) widening to its rim (v 0.9)
            if (v >= 0.44f && v <= 0.9f)
            {
                float k = (v - 0.44f) / 0.46f;
                float half = 0.08f + 0.24f * Mathf.Sqrt(k);
                if (Mathf.Abs(dx) <= half) return true;
            }
            // the handles: rings beside the bowl
            foreach (float side in new[] { -1f, 1f })
            {
                float hx = u - (0.5f + side * 0.31f), hy = v - 0.7f;
                float d = Mathf.Sqrt(hx * hx * 1.4f + hy * hy);
                if (d >= 0.085f && d <= 0.14f && hx * side > -0.02f) { shade = side * 0.6f; return true; }
            }
            if (Mathf.Abs(dx) <= 0.045f && v >= 0.22f && v <= 0.46f) return true;                     // the stem
            if (Mathf.Abs(dx) <= 0.2f && v >= 0.16f && v <= 0.24f) return true;                       // the collar
            if (Mathf.Abs(dx) <= 0.26f && v >= 0.06f && v <= 0.16f) { shade *= 0.5f; shade += 0.3f; return true; }   // the base
            return false;
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
