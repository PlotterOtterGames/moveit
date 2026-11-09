using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// The stadium, built in code after Wii Sports Tennis's grass court: calm mown stripes in two greens, cream lines,
    /// low dark green walls round the court, the net with its posts and tape, an umpire on a tall chair, the players'
    /// benches, beige stands on both sides and behind the far baseline with a crowd in soft colours that jumps up when a
    /// point is won (<see cref="Cheer"/>), trees and hills in the haze. Its colours are a little darker than they are seen
    /// (<see cref="Lit"/>: the sun and the sky light add ~15 %). Everything that doesn't move is one mesh (Tundu/Toon with
    /// vertex colours), the crowd a few more (each section bobs on its own), the net one see-through quad: a dozen draw
    /// calls in all.
    /// </summary>
    public sealed class TennisStadium : MonoBehaviour
    {
        /// <summary>The brand's colours for effects and the HUD (not the stadium's).</summary>
        public static readonly Color Sun = Hex(0xFFC93C), Coral = Hex(0xFF5A4E), Sky = Hex(0x31C6F4), Grape = Hex(0x7B61FF),
                                     Leaf = Hex(0x6FD66A), Night = Hex(0x141938);

        // the court's palette, sampled from Wii Sports Tennis (as seen) and taken back to what the light adds
        static readonly Color GrassLight = Lit(0x6E963F), GrassDark = Lit(0x62873A), Lawn = Lit(0x98B457),
                              Line = Lit(0xF1EFDD), Wall = Lit(0x335F35), WallTop = Lit(0x587334), Stand = Lit(0xC4C58C),
                              StandDark = Lit(0xB0B27B), StandStep = Lit(0x9C9E6C), Wood = Lit(0x8E6E48), Trunk = Lit(0x6A4E34),
                              TreeDark = Lit(0x4C672F), TreeLight = Lit(0x5E7C38), Hill = Lit(0xA4BE6C), HillFar = Lit(0xB5CA86),
                              Post = Lit(0x2E4834), NetBand = Lit(0x26352A), Umpire = Lit(0x2F3E5C), Skin = Lit(0xF2C9A6);
        static readonly Color[] Crowd =
        {
            Lit(0xC65B52), Lit(0xD8B04E), Lit(0x5C84B5), Lit(0x8C76B5), Lit(0x79A35A), Lit(0xE3DCCB), Lit(0x6D7287), Lit(0xB98A5E), Lit(0xD28AA8),
        };
        static readonly Color[] Skins = { Lit(0xF2C9A6), Lit(0xE3B08A), Lit(0xC48B63), Lit(0x966342), Lit(0xF7D9C0) };
        static readonly Color[] Hairs = { Lit(0x2B1D16), Lit(0x5A3A22), Lit(0x8C5A2E), Lit(0xD6AE58), Lit(0x161616), Lit(0xA3482F) };

        /// <summary>How much the scene's light brightens a sunlit surface (the sun at 1 plus the sky's fill).</summary>
        public const float LightGain = 1.15f;

        sealed class Section
        {
            public Transform T;
            public float Phase;
        }

        readonly List<Section> _sections = new List<Section>();
        float _cheer, _t;
        GameObject _nearEnd;

        /// <summary>The stands, crowd, trees and hills behind the near baseline: only the far team's half of a split
        /// screen (two players, versus) looks that way, so they're drawn only then.</summary>
        public void ShowNearEnd(bool on)
        {
            if (_nearEnd != null && _nearEnd.activeSelf != on) _nearEnd.SetActive(on);
        }

        /// <summary>The stadium; with <paramref name="shadow"/> (Tundu/Fx with <see cref="TennisArt.ShadowAtlas"/>) the sun's
        /// shadows of what stands still, cast along <paramref name="sun"/> (the way the light travels).</summary>
        public static TennisStadium Build(Material toon, Material net, Material shadow = null, Vector3? sun = null, int seed = 7)
        {
            var go = new GameObject("Stadium");
            var s = go.AddComponent<TennisStadium>();
            s.Make(toon, net, new System.Random(seed));
            if (shadow != null) s.BuildShadows(shadow, sun ?? new Vector3(-0.321f, -0.766f, 0.557f));
            return s;
        }

        /// <summary>The crowd jumps up for a moment (0..1 how much).</summary>
        public void Cheer(float amount = 1f) => _cheer = Mathf.Max(_cheer, amount);

        void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            _cheer = Mathf.Max(0f, _cheer - dt * 0.6f);
            foreach (var s in _sections)
            {
                // a slow murmur of movement, and a jump when they cheer
                float idle = Mathf.Sin(_t * 1.7f + s.Phase) * 0.015f;
                float jump = Mathf.Abs(Mathf.Sin(_t * 9f + s.Phase)) * 0.25f * _cheer;
                s.T.localPosition = new Vector3(0f, idle + jump, 0f);
            }
        }

        // ------------------------------------------------------------------------------------------------ building

        /// <summary>The walls stand this far outside the doubles sidelines and behind the baselines (m).</summary>
        const float WallSide = 4f, WallBack = 6f, WallHeight = 1.1f;

        void Make(Material toon, Material netMaterial, System.Random rng)
        {
            var m = new PartMesh { Srgb = true };
            void Box(Vector3 center, Vector3 size, Color c, float gloss = 0.05f, Vector3 euler = default) =>
                m.Add(PrimitiveType.Cube, center, size, c, gloss, euler);

            float bx = Court.HalfWidth + WallSide, bz = Court.HalfLength + WallBack;
            // the court's grass inside the walls (mown stripes across, worn where players stand), and the lawn outside as a
            // frame round it: a lawn under the whole court had every court pixel shaded twice (the GPU is the box's
            // limit, 2026-11-06)
            Grass(m, bx, bz);
            const float lawnLeft = -55f, lawnRight = 55f, lawnNear = -59f, lawnFar = 71f;
            Box(new Vector3((lawnLeft - bx) * 0.5f, -0.06f, (lawnNear + lawnFar) * 0.5f), new Vector3(-bx - lawnLeft, 0.1f, lawnFar - lawnNear), Lawn);
            Box(new Vector3((lawnRight + bx) * 0.5f, -0.06f, (lawnNear + lawnFar) * 0.5f), new Vector3(lawnRight - bx, 0.1f, lawnFar - lawnNear), Lawn);
            Box(new Vector3(0f, -0.06f, (lawnNear - bz) * 0.5f), new Vector3(bx * 2f, 0.1f, -bz - lawnNear), Lawn);
            Box(new Vector3(0f, -0.06f, (lawnFar + bz) * 0.5f), new Vector3(bx * 2f, 0.1f, lawnFar - bz), Lawn);

            // the lines (wider than real ones, 8 cm, so they read on a TV)
            const float w = 0.08f, y = 0.002f;
            void LineX(float z, float half) => Box(new Vector3(0f, y, z), new Vector3(half * 2f + w, 0.004f, w), Line);
            void LineZ(float x, float from, float to) => Box(new Vector3(x, y, (from + to) * 0.5f), new Vector3(w, 0.004f, Mathf.Abs(to - from) + w), Line);
            foreach (float s in new[] { -1f, 1f })
            {
                LineX(s * Court.HalfLength, Court.HalfWidth);
                LineX(s * Court.ServiceLine, Court.SinglesHalfWidth);
                LineZ(s * Court.HalfWidth, -Court.HalfLength, Court.HalfLength);
                LineZ(s * Court.SinglesHalfWidth, -Court.HalfLength, Court.HalfLength);
                Box(new Vector3(0f, y, s * (Court.HalfLength - 0.15f)), new Vector3(w, 0.004f, 0.3f), Line);   // centre mark
            }
            LineZ(0f, -Court.ServiceLine, Court.ServiceLine);

            // the net: posts, the white tape along its top (in short pieces, following the sag), a dark band at the foot
            foreach (float s in new[] { -1f, 1f })
            {
                m.Add(PrimitiveType.Cylinder, new Vector3(s * Court.PostX, Court.NetPost * 0.5f, 0f), new Vector3(0.1f, Court.NetPost * 0.5f, 0.1f), Post, 0.3f);
                m.Add(PrimitiveType.Sphere, new Vector3(s * Court.PostX, Court.NetPost + 0.02f, 0f), Vector3.one * 0.12f, Line, 0.3f);
            }
            const int pieces = 12;
            for (int i = 0; i < pieces; i++)
            {
                float x0 = Mathf.Lerp(-Court.PostX, Court.PostX, i / (float)pieces), x1 = Mathf.Lerp(-Court.PostX, Court.PostX, (i + 1) / (float)pieces);
                float h0 = Court.NetHeight(x0), h1 = Court.NetHeight(x1);
                float angle = Mathf.Atan2(h1 - h0, x1 - x0) * Mathf.Rad2Deg;
                Box(new Vector3((x0 + x1) * 0.5f, (h0 + h1) * 0.5f - 0.02f, 0f), new Vector3(x1 - x0 + 0.01f, 0.07f, 0.03f), Line, 0.2f, new Vector3(0f, 0f, angle));
            }
            Box(new Vector3(0f, 0.03f, 0f), new Vector3(Court.PostX * 2f, 0.06f, 0.025f), NetBand);

            // low dark green walls round the court with a lighter cap (Wii's court has no ad boards)
            foreach (float s in new[] { -1f, 1f })
            {
                Box(new Vector3(0f, WallHeight * 0.5f, s * bz), new Vector3(bx * 2f + 0.3f, WallHeight, 0.3f), Wall);
                Box(new Vector3(0f, WallHeight + 0.04f, s * bz), new Vector3(bx * 2f + 0.4f, 0.08f, 0.4f), WallTop);
                Box(new Vector3(s * bx, WallHeight * 0.5f, 0f), new Vector3(0.3f, WallHeight, bz * 2f), Wall);
                Box(new Vector3(s * bx, WallHeight + 0.04f, 0f), new Vector3(0.4f, 0.08f, bz * 2f), WallTop);
            }

            UmpireChair(m, new Vector3(Court.PostX + 1.3f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                // the players' benches with a towel and a bag
                var bench = new Vector3(-(Court.PostX + 1.3f), 0f, s * 2.6f);
                Box(bench + new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.08f, 2.2f), Wood, 0.2f);
                Box(bench + new Vector3(-0.25f, 0.8f, 0f), new Vector3(0.08f, 0.6f, 2.2f), Wood, 0.2f);
                foreach (float z in new[] { -0.9f, 0.9f }) Box(bench + new Vector3(0f, 0.22f, z), new Vector3(0.5f, 0.44f, 0.08f), Post);
                Box(bench + new Vector3(0.05f, 0.52f, s * 0.5f), new Vector3(0.4f, 0.06f, 0.5f), Crowd[5], 0.1f);
                Box(bench + new Vector3(0.6f, 0.2f, -s * 0.4f), new Vector3(0.35f, 0.4f, 0.8f), s < 0 ? Crowd[0] : Crowd[2], 0.2f);
            }

            // beige stands on both sides and behind the far baseline, rising in steps
            var crowd = new List<(Vector3 pos, float facing)>();
            const int rows = 5;
            foreach (float s in new[] { -1f, 1f })
            {
                for (int r = 0; r < rows; r++)
                {
                    float x0 = bx + 1.2f + r * 1.1f;
                    float top = 0.9f + r * 0.6f;
                    Box(new Vector3(s * (x0 + 4f), top * 0.5f, 2f), new Vector3(8f, top, 34f), r % 2 == 0 ? Stand : StandDark);
                    Box(new Vector3(s * (x0 + 0.3f), top + 0.03f, 2f), new Vector3(0.5f, 0.06f, 34f), StandStep, 0.1f);
                    for (float z = -14.5f; z <= 18.5f; z += 0.72f)
                        if (rng.NextDouble() < 0.72) crowd.Add((new Vector3(s * (x0 + 0.35f), top, z + (float)(rng.NextDouble() - 0.5) * 0.15f), -s));
                }
            }
            for (int r = 0; r < rows; r++)
            {
                float z0 = bz + 1.4f + r * 1.1f;
                float top = 0.9f + r * 0.6f;
                Box(new Vector3(0f, top * 0.5f, z0 + 4f), new Vector3(2f * (bx + 7.3f), top, 8f), r % 2 == 0 ? Stand : StandDark);
                Box(new Vector3(0f, top + 0.03f, z0 + 0.3f), new Vector3(2f * (bx + 7.3f), 0.06f, 0.5f), StandStep, 0.1f);
                for (float x = -bx - 6f; x <= bx + 6f; x += 0.72f)
                    if (rng.NextDouble() < 0.72) crowd.Add((new Vector3(x + (float)(rng.NextDouble() - 0.5) * 0.15f, top, z0 + 0.35f), 0f));
            }
            float backZ = bz + 1.4f + rows * 1.1f;
            Box(new Vector3(0f, 2.2f, backZ + 0.3f), new Vector3(2f * (bx + 7.3f), 4.4f, 0.4f), Wall);   // the stand's back wall

            // trees and hills behind, soft in the haze
            for (int i = 0; i < 18; i++)
            {
                float x = -42f + 84f * i / 17f + (float)(rng.NextDouble() - 0.5) * 3f;
                float z = backZ + 4f + (float)rng.NextDouble() * 6f;
                float h = 5f + (float)rng.NextDouble() * 3f;
                m.Add(PrimitiveType.Cylinder, new Vector3(x, h * 0.35f, z), new Vector3(0.5f, h * 0.35f, 0.5f), Trunk, 0.05f);
                Tree(m, new Vector3(x, h * 0.85f, z), new Vector3(h * 0.8f, h * 0.75f, h * 0.8f), i % 2 == 0 ? TreeDark : TreeLight);
            }
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 6; i++)
                {
                    float z = -14f + i * 7f + (float)rng.NextDouble() * 2f;
                    float x = s * (bx + 14f + (float)rng.NextDouble() * 4f), h = 4.5f + (float)rng.NextDouble() * 2.5f;
                    m.Add(PrimitiveType.Cylinder, new Vector3(x, h * 0.35f, z), new Vector3(0.5f, h * 0.35f, 0.5f), Trunk, 0.05f);
                    Tree(m, new Vector3(x, h * 0.85f, z), new Vector3(h * 0.75f, h * 0.7f, h * 0.75f), i % 2 == 0 ? TreeDark : TreeLight);
                }
            m.Add(PrimitiveType.Sphere, new Vector3(-30f, -6f, 95f), new Vector3(90f, 30f, 30f), Hill, 0f);
            m.Add(PrimitiveType.Sphere, new Vector3(40f, -8f, 105f), new Vector3(110f, 34f, 30f), HillFar, 0f);

            var court = PartMesh.Show("Court", transform, m.Build("Tennis stadium"), toon);
            court.receiveShadows = true;
            court.gameObject.isStatic = true;

            var net = new GameObject("Net");
            net.transform.SetParent(transform, false);
            net.AddComponent<MeshFilter>().sharedMesh = TennisArt.Net(Court.PostX, Court.NetCenter, Court.NetPost, 0.1f);
            var nr = net.AddComponent<MeshRenderer>();
            nr.sharedMaterial = netMaterial;
            nr.shadowCastingMode = ShadowCastingMode.Off;
            nr.receiveShadows = false;

            BuildCrowd(crowd, toon, rng);
            BuildNearEnd(toon, bx, bz, rows);
        }

        // worn grass: lighter and yellower where it's played on, sandy where it's played on most (Wii's and Wimbledon's
        // courts by late in a tournament)
        static readonly Color GrassWorn = Lit(0x93A458), GrassBare = Lit(0xAFA06E);

        /// <summary>
        /// The grass inside the walls (2026-11-07: a grid instead of 24 flat stripes, so the grass can vary across it): the
        /// mown stripes across the court in two greens, worn lighter and browner where players stand most (behind the
        /// baselines' middles, along the baselines, where the net players wait, at the service T), a little darker at the
        /// walls' foot, soft patches of greener and paler grass. A colour per vertex: nothing more to draw.
        /// </summary>
        static void Grass(PartMesh m, float bx, float bz)
        {
            const int stripes = 24, rows = 3, columns = 24;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            for (int s = 0; s < stripes; s++)
            {
                float z0 = Mathf.Lerp(-bz, bz, s / (float)stripes), z1 = Mathf.Lerp(-bz, bz, (s + 1) / (float)stripes);
                var stripe = s % 2 == 0 ? GrassLight : GrassDark;
                int first = v.Count;   // each stripe its own vertices: the colour changes sharply between stripes
                for (int r = 0; r <= rows; r++)
                {
                    float z = Mathf.Lerp(z0, z1, r / (float)rows);
                    for (int k = 0; k <= columns; k++)
                    {
                        float x = Mathf.Lerp(-bx, bx, k / (float)columns);
                        v.Add(new Vector3(x, -0.002f, z));
                        n.Add(Vector3.up);
                        c.Add(Worn(stripe, x, z, bx, bz));
                    }
                }
                for (int r = 0; r < rows; r++)
                    for (int k = 0; k < columns; k++)
                    {
                        int a = first + r * (columns + 1) + k, b = a + columns + 1;
                        t.Add(a); t.Add(b); t.Add(a + 1);
                        t.Add(a + 1); t.Add(b); t.Add(b + 1);
                    }
            }
            m.Add(v, n, c, t);
        }

        /// <summary>The grass's colour at (x, z): its stripe's, worn where it's played on, darker by the walls.</summary>
        static Color Worn(Color grass, float x, float z, float bx, float bz)
        {
            float wear = 0f;
            foreach (float s in new[] { -1f, 1f })
            {
                wear += 0.95f * Blob(x, z, 0f, s * (Court.HalfLength + 0.8f), 1.9f, 1.25f);     // behind the baseline's middle
                wear += 0.35f * Blob(x, z, 0f, s * (Court.HalfLength + 0.25f), 4.6f, 0.65f);    // along the baseline
                wear += 0.2f * (Blob(x, z, -1.6f, s * TennisMatch.NetDepth, 0.9f, 0.8f) + Blob(x, z, 1.6f, s * TennisMatch.NetDepth, 0.9f, 0.8f));
                wear += 0.14f * Blob(x, z, 0f, s * Court.ServiceLine, 0.8f, 0.6f);               // the service T
            }
            wear = Mathf.Clamp01(wear);
            var c = Color.Lerp(grass, GrassWorn, Mathf.Clamp01(wear * 1.35f) * 0.8f);
            c = Color.Lerp(c, GrassBare, Mathf.Clamp01((wear - 0.5f) * 2f) * 0.65f);
            float patch = Mathf.PerlinNoise(x * 0.21f + 7.1f, z * 0.21f + 3.3f) - 0.5f;   // soft patches
            c *= 1f + patch * 0.1f;
            float wall = Mathf.Min(bx - Mathf.Abs(x), bz - Mathf.Abs(z));                   // the walls' foot
            c *= Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(wall / 0.9f));
            c.a = 0.05f;   // (gloss)
            return c;
        }

        static float Blob(float x, float z, float cx, float cz, float sx, float sz)
        {
            float dx = (x - cx) / sx, dz = (z - cz) / sz;
            return Mathf.Exp(-(dx * dx + dz * dz));
        }

        /// <summary>
        /// The sun's shadows of everything that stands still, laid on the grass as soft see-through shapes (one mesh, one
        /// draw call, 2026-11-07): the net's (faint: a net lets the light through) with its tape's darker line, the posts',
        /// the umpire's on its tall chair, the benches', and the low walls' along the near end and the right side (the
        /// sun is behind the camera, on the right) and the far wall's on the lawn.
        /// </summary>
        void BuildShadows(Material shadow, Vector3 sun)
        {
            float down = Mathf.Max(0.2f, -sun.y);
            var slant = new Vector2(sun.x, sun.z) / down;   // ground metres per metre of height
            float bx = Court.HalfWidth + WallSide, bz = Court.HalfLength + WallBack;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var c = new List<Color>();
            var t = new List<int>();
            var dark = new Color(0.04f, 0.1f, 0.06f, 1f);
            const float y = 0.008f;   // over the lines, under the moving shadows
            var solid = new Vector2(1f / 6f, 0.5f);
            // a quad of the ground (corners in order round it), each corner its own strength, the atlas's solid cell
            void Quad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float a0, float a1, float a2, float a3, float height = y)
            {
                int i = v.Count;
                foreach (var (p, a) in new[] { (p0, a0), (p1, a1), (p2, a2), (p3, a3) })
                {
                    v.Add(new Vector3(p.x, height, p.y));
                    uv.Add(solid);
                    c.Add(new Color(dark.r, dark.g, dark.b, a));
                }
                // (Tundu/Fx draws both sides: one winding)
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            // a shape from the atlas (a soft blob or a body) along a to b, so wide
            void Shape(Vector2 a, Vector2 b, float width, bool body, float alpha)
            {
                var d = b - a;
                var side = new Vector2(-d.y, d.x).normalized * (width * 0.5f);
                int i = v.Count;
                float u0 = body ? 2f / 3f : 1f / 3f, u1 = body ? 1f : 2f / 3f;
                v.Add(new Vector3(a.x - side.x, y, a.y - side.y)); uv.Add(new Vector2(u0, 0f));
                v.Add(new Vector3(a.x + side.x, y, a.y + side.y)); uv.Add(new Vector2(u1, 0f));
                v.Add(new Vector3(b.x + side.x, y, b.y + side.y)); uv.Add(new Vector2(u1, 1f));
                v.Add(new Vector3(b.x - side.x, y, b.y - side.y)); uv.Add(new Vector2(u0, 1f));
                for (int k = 0; k < 4; k++) c.Add(new Color(dark.r, dark.g, dark.b, alpha));
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            // the net: from its foot to where its top falls, in pieces along the sag; the tape a darker line at the end
            const int pieces = 16;
            for (int k = 0; k < pieces; k++)
            {
                float x0 = Mathf.Lerp(-Court.PostX, Court.PostX, k / (float)pieces), x1 = Mathf.Lerp(-Court.PostX, Court.PostX, (k + 1) / (float)pieces);
                float h0 = Court.NetHeight(x0), h1 = Court.NetHeight(x1);
                Vector2 b0 = new Vector2(x0, 0f), b1 = new Vector2(x1, 0f);
                Vector2 m0 = b0 + slant * (h0 - 0.07f), m1 = b1 + slant * (h1 - 0.07f), t0 = b0 + slant * h0, t1 = b1 + slant * h1;
                Quad(b0, b1, m1, m0, 0.2f, 0.2f, 0.2f, 0.2f);
                Quad(m0, m1, t1, t0, 0.45f, 0.45f, 0.45f, 0.45f);
            }
            // the posts
            foreach (float s in new[] { -1f, 1f })
            {
                var foot = new Vector2(s * Court.PostX, 0f);
                var top = foot + slant * Court.NetPost;
                var across = new Vector2(-slant.y, slant.x).normalized * 0.06f;
                Quad(foot - across, foot + across, top + across, top - across, 0.55f, 0.55f, 0.5f, 0.5f);
            }
            // the umpire on the tall chair (the atlas's body: the head's round shadow at the end), the benches
            var chair = new Vector2(Court.PostX + 1.3f, 0f);
            Shape(chair - slant.normalized * 0.4f, chair + slant * 3.2f, 1.1f, true, 0.55f);
            foreach (float s in new[] { -1f, 1f })
            {
                var bench = new Vector2(-(Court.PostX + 1.3f), s * 2.6f);
                Shape(bench + new Vector2(0f, -1.3f) + slant * 0.3f, bench + new Vector2(0f, 1.3f) + slant * 0.3f, 1.2f, false, 0.6f);
            }
            // the walls (1.1 m): the near one's falls onto the grass behind the near baseline, the right one's along the
            // right side, the far one's onto the lawn behind it; the edge away from the wall soft
            float lenZ = slant.y * WallHeight, lenX = -slant.x * WallHeight;
            if (lenZ > 0.05f)
            {
                float z0 = -bz + 0.15f, z1 = z0 + lenZ;
                Quad(new Vector2(-bx, z0), new Vector2(bx, z0), new Vector2(bx, z1), new Vector2(-bx, z1), 0.42f, 0.42f, 0f, 0f);
                float f0 = bz + 0.2f, f1 = f0 + lenZ;
                Quad(new Vector2(-bx, f0), new Vector2(bx, f0), new Vector2(bx, f1), new Vector2(-bx, f1), 0.4f, 0.4f, 0f, 0f, -0.004f);
            }
            if (lenX > 0.05f)
            {
                float x0 = bx - 0.15f, x1 = x0 - lenX;
                Quad(new Vector2(x0, -bz), new Vector2(x0, bz), new Vector2(x1, bz), new Vector2(x1, -bz), 0.42f, 0.42f, 0f, 0f);
            }

            var mesh = new Mesh { name = "Court shadows" };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            var shadows = PartMesh.Show("Court shadows", transform, mesh, shadow);
            shadows.gameObject.isStatic = true;
        }

        /// <summary>The far end's stands mirrored behind the near baseline (its own objects, off until a split screen needs
        /// them), with trees and hills behind; its own random numbers, so the rest of the stadium looks as it always has.</summary>
        void BuildNearEnd(Material toon, float bx, float bz, int rows)
        {
            var rng = new System.Random(91);
            var m = new PartMesh { Srgb = true };
            var crowd = new List<(Vector3 pos, float facing)>();
            for (int r = 0; r < rows; r++)
            {
                float z0 = bz + 1.4f + r * 1.1f;
                float top = 0.9f + r * 0.6f;
                m.Add(PrimitiveType.Cube, new Vector3(0f, top * 0.5f, -(z0 + 4f)), new Vector3(2f * (bx + 7.3f), top, 8f), r % 2 == 0 ? Stand : StandDark, 0.05f);
                m.Add(PrimitiveType.Cube, new Vector3(0f, top + 0.03f, -(z0 + 0.3f)), new Vector3(2f * (bx + 7.3f), 0.06f, 0.5f), StandStep, 0.1f);
                for (float x = -bx - 6f; x <= bx + 6f; x += 0.72f)
                    if (rng.NextDouble() < 0.72) crowd.Add((new Vector3(x + (float)(rng.NextDouble() - 0.5) * 0.15f, top, -(z0 + 0.35f)), NearEndFacing));
            }
            float backZ = bz + 1.4f + rows * 1.1f;
            m.Add(PrimitiveType.Cube, new Vector3(0f, 2.2f, -(backZ + 0.3f)), new Vector3(2f * (bx + 7.3f), 4.4f, 0.4f), Wall, 0.05f);
            for (int i = 0; i < 18; i++)
            {
                float x = -42f + 84f * i / 17f + (float)(rng.NextDouble() - 0.5) * 3f;
                float z = -(backZ + 4f + (float)rng.NextDouble() * 6f);
                float h = 5f + (float)rng.NextDouble() * 3f;
                m.Add(PrimitiveType.Cylinder, new Vector3(x, h * 0.35f, z), new Vector3(0.5f, h * 0.35f, 0.5f), Trunk, 0.05f);
                Tree(m, new Vector3(x, h * 0.85f, z), new Vector3(h * 0.8f, h * 0.75f, h * 0.8f), i % 2 == 0 ? TreeDark : TreeLight);
            }
            m.Add(PrimitiveType.Sphere, new Vector3(30f, -6f, -95f), new Vector3(90f, 30f, 30f), Hill, 0f);
            m.Add(PrimitiveType.Sphere, new Vector3(-40f, -8f, -105f), new Vector3(110f, 34f, 30f), HillFar, 0f);
            _nearEnd = new GameObject("Near end");
            _nearEnd.transform.SetParent(transform, false);
            var stands = PartMesh.Show("Near stands", _nearEnd.transform, m.Build("Tennis near end"), toon);
            stands.receiveShadows = false;
            BuildCrowd(crowd, toon, rng, _nearEnd.transform);
            _nearEnd.SetActive(false);
        }

        /// <summary>A tree's crown: a 12-segment ball (168 triangles; PartMesh's sphere for a shape this big has 572), soft in
        /// the haze 30-50 m away (30 trees, 48 with the split screen's near end, 2026-11-06).</summary>
        static void Tree(PartMesh m, Vector3 at, Vector3 size, Color color) =>
            m.Add(PartMesh.Ball(12), Matrix4x4.TRS(at, Quaternion.identity, size), color, 0.05f);

        /// <summary>The crowd's facing for the near end's stand (they look toward the far end).</summary>
        const float NearEndFacing = 2f;

        /// <summary>The crowd in sections of about eight metres (each its own mesh, so they can bob out of step).</summary>
        void BuildCrowd(List<(Vector3 pos, float facing)> people, Material toon, System.Random rng, Transform parent = null)
        {
            var sections = new Dictionary<int, PartMesh>();
            var head = TennisArt.LowSphere();
            foreach (var (pos, facing) in people)
            {
                int key = facing == 0f || facing == NearEndFacing ? 100 + Mathf.FloorToInt((pos.x + 30f) / 8f)
                    : (facing < 0f ? 200 : 300) + Mathf.FloorToInt((pos.z + 20f) / 8f);
                if (!sections.TryGetValue(key, out var m)) sections[key] = m = new PartMesh { Srgb = true };
                var shirt = Crowd[rng.Next(Crowd.Length)];
                var skin = Skins[rng.Next(Skins.Length)];
                var hair = Hairs[rng.Next(Hairs.Length)];
                float yaw = facing == NearEndFacing ? 0f : facing == 0f ? 180f : facing > 0f ? 90f : -90f;
                var turn = Quaternion.Euler(0f, yaw, 0f);
                float size = 0.85f + (float)rng.NextDouble() * 0.3f;
                m.Add(PrimitiveType.Cube, Matrix4x4.TRS(pos + Vector3.up * 0.32f * size, turn, new Vector3(0.38f, 0.5f, 0.3f) * size), shirt, 0.05f);
                // the head with its hair on top (one shape: the hair was a second ball, 36 more triangles a spectator)
                m.Add(head, Matrix4x4.TRS(pos + Vector3.up * 0.76f * size, turn, Vector3.one * 0.31f * size), skin, 0.05f, hair, 0.3f);
            }
            foreach (var pair in sections)
            {
                var root = new GameObject("Crowd").transform;
                root.SetParent(parent != null ? parent : transform, false);
                var r = PartMesh.Show("People", root, pair.Value.Build("Tennis crowd"), toon);
                r.receiveShadows = false;
                _sections.Add(new Section { T = root, Phase = (float)rng.NextDouble() * 6.28f });
            }
        }

        static void UmpireChair(PartMesh m, Vector3 at)
        {
            var white = Line;
            foreach (var (x, z) in new[] { (-0.35f, -0.35f), (0.35f, -0.35f), (-0.35f, 0.35f), (0.35f, 0.35f) })
                m.Add(PrimitiveType.Cylinder, at + new Vector3(x, 1f, z), new Vector3(0.07f, 1f, 0.07f), white, 0.2f);
            for (int i = 1; i <= 4; i++) m.Add(PrimitiveType.Cube, at + new Vector3(-0.36f, i * 0.4f, 0f), new Vector3(0.05f, 0.05f, 0.7f), white, 0.2f);   // the ladder
            m.Add(PrimitiveType.Cube, at + new Vector3(0f, 2.05f, 0f), new Vector3(0.9f, 0.1f, 0.9f), Wood, 0.2f);
            m.Add(PrimitiveType.Cube, at + new Vector3(0.42f, 2.5f, 0f), new Vector3(0.08f, 0.8f, 0.9f), Wood, 0.2f);
            m.Add(PrimitiveType.Cube, at + new Vector3(0.05f, 2.2f, 0f), new Vector3(0.6f, 0.2f, 1.1f), Wall, 0.2f);
            // the umpire: navy blazer, a white cap, looking at the court
            m.Add(PrimitiveType.Capsule, at + new Vector3(0.05f, 2.55f, 0f), new Vector3(0.4f, 0.32f, 0.36f), Umpire, 0.1f);
            m.Add(PrimitiveType.Sphere, at + new Vector3(0f, 3.05f, 0f), Vector3.one * 0.36f, Skin, 0.05f);
            m.Add(PrimitiveType.Sphere, at + new Vector3(0.02f, 3.16f, 0f), new Vector3(0.38f, 0.2f, 0.38f), white, 0.2f);
            m.Add(PrimitiveType.Cube, at + new Vector3(-0.17f, 3.1f, 0f), new Vector3(0.18f, 0.03f, 0.3f), white, 0.2f);   // the cap's peak
            m.Add(PrimitiveType.Sphere, at + new Vector3(-0.16f, 3.07f, -0.08f), Vector3.one * 0.05f, Night, 0.3f);
            m.Add(PrimitiveType.Sphere, at + new Vector3(-0.16f, 3.07f, 0.08f), Vector3.one * 0.05f, Night, 0.3f);
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        /// <summary>A colour as it should look in the sun (sRGB), as the vertex colour that gives it: the light's gain taken
        /// off in linear light.</summary>
        static Color Lit(int rgb)
        {
            var c = Hex(rgb).linear;
            return new Color(c.r / LightGain, c.g / LightGain, c.b / LightGain).gamma;
        }
    }
}
