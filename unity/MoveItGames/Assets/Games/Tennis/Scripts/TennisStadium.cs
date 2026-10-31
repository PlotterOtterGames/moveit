using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// The stadium, built in code: a blue hard court with white lines on a green apron, the net with its posts and tape,
    /// colourful ad boards, an umpire on a tall chair, the players' benches, stands on both sides and behind the far
    /// baseline with a crowd that jumps up when a point is won (<see cref="Cheer"/>), a striped awning, flags, trees and
    /// hills. Everything that doesn't move is one mesh (Tundu/Toon with vertex colours), the crowd a few more (each
    /// section bobs on its own), the net one see-through quad: a dozen draw calls in all.
    /// </summary>
    public sealed class TennisStadium : MonoBehaviour
    {
        public static readonly Color Sun = Hex(0xFFC93C), Coral = Hex(0xFF5A4E), Sky = Hex(0x31C6F4), Grape = Hex(0x7B61FF),
                                     Leaf = Hex(0x6FD66A), Night = Hex(0x141938);
        static readonly Color CourtBlue = Hex(0x3A78D0), Apron = Hex(0x3E9E62), Grass = Hex(0x86CF6E), Line = Hex(0xF7F8FF),
                              Stone = Hex(0xDCD7EE), StoneDark = Hex(0xBDB6DA), Wood = Hex(0xB98250), Trunk = Hex(0x8A5A3C),
                              TreeDark = Hex(0x4FAF57), Skin = Hex(0xF6CBA8), Navy = Hex(0x26305E);
        static readonly Color[] Crowd = { Sun, Coral, Sky, Grape, Leaf, Hex(0xFF9AC8), Hex(0xFF8A3C), Color.white, Hex(0x4C6BE0) };
        static readonly Color[] Skins = { Hex(0xF6CBA8), Hex(0xE7B48E), Hex(0xC98E66), Hex(0x9A6544), Hex(0xFBDDC4) };
        static readonly Color[] Hairs = { Hex(0x2B1D16), Hex(0x5A3A22), Hex(0x8C5A2E), Hex(0xE2B655), Hex(0x111111), Hex(0xB2452E) };

        sealed class Section
        {
            public Transform T;
            public float Phase;
        }

        readonly List<Section> _sections = new List<Section>();
        float _cheer, _t;

        public static TennisStadium Build(Material toon, Material net, int seed = 7)
        {
            var go = new GameObject("Stadium");
            var s = go.AddComponent<TennisStadium>();
            s.Make(toon, net, new System.Random(seed));
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

        void Make(Material toon, Material netMaterial, System.Random rng)
        {
            var m = new PartMesh { Srgb = true };
            void Box(Vector3 center, Vector3 size, Color c, float gloss = 0.1f, Vector3 euler = default) =>
                m.Add(PrimitiveType.Cube, center, size, c, gloss, euler);

            // ground, the apron, the court
            Box(new Vector3(0f, -0.06f, 6f), new Vector3(90f, 0.1f, 110f), Grass);
            Box(new Vector3(0f, -0.015f, 0f), new Vector3((Court.HalfWidth + 4f) * 2f, 0.02f, (Court.HalfLength + 6f) * 2f), Apron);
            Box(new Vector3(0f, -0.01f, 0f), new Vector3(Court.HalfWidth * 2f + 0.3f, 0.02f, Court.HalfLength * 2f + 0.3f), CourtBlue);

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
                m.Add(PrimitiveType.Cylinder, new Vector3(s * Court.PostX, Court.NetPost * 0.5f, 0f), new Vector3(0.1f, Court.NetPost * 0.5f, 0.1f), Navy, 0.5f);
                m.Add(PrimitiveType.Sphere, new Vector3(s * Court.PostX, Court.NetPost + 0.02f, 0f), Vector3.one * 0.12f, Line, 0.5f);
            }
            const int pieces = 12;
            for (int i = 0; i < pieces; i++)
            {
                float x0 = Mathf.Lerp(-Court.PostX, Court.PostX, i / (float)pieces), x1 = Mathf.Lerp(-Court.PostX, Court.PostX, (i + 1) / (float)pieces);
                float h0 = Court.NetHeight(x0), h1 = Court.NetHeight(x1);
                float angle = Mathf.Atan2(h1 - h0, x1 - x0) * Mathf.Rad2Deg;
                Box(new Vector3((x0 + x1) * 0.5f, (h0 + h1) * 0.5f - 0.02f, 0f), new Vector3(x1 - x0 + 0.01f, 0.07f, 0.03f), Line, 0.3f, new Vector3(0f, 0f, angle));
            }
            Box(new Vector3(0f, 0.03f, 0f), new Vector3(Court.PostX * 2f, 0.06f, 0.025f), Navy);

            // ad boards around the court in the palette's colours
            Color[] boards = { Coral, Sun, Sky, Grape, Leaf };
            float bx = Court.HalfWidth + 4f, bz = Court.HalfLength + 6f;
            for (int i = 0; i < 6; i++)
            {
                float x = -bx + bx * 2f * (i + 0.5f) / 6f;
                Box(new Vector3(x, 0.45f, bz), new Vector3(bx * 2f / 6f - 0.08f, 0.9f, 0.15f), boards[i % boards.Length], 0.4f);
                Box(new Vector3(x, 0.5f, bz - 0.08f), new Vector3(bx * 2f / 6f * 0.55f, 0.22f, 0.02f), Color.white, 0.4f);   // a "logo"
            }
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 7; i++)
                {
                    float z = -bz + 4f + (bz * 2f - 4f) * (i + 0.5f) / 7f;
                    Box(new Vector3(s * bx, 0.45f, z), new Vector3(0.15f, 0.9f, (bz * 2f - 4f) / 7f - 0.08f), boards[(i + 2) % boards.Length], 0.4f);
                    Box(new Vector3(s * (bx - 0.08f), 0.5f, z), new Vector3(0.02f, 0.22f, (bz * 2f - 4f) / 7f * 0.55f), Color.white, 0.4f);
                }

            UmpireChair(m, new Vector3(Court.PostX + 1.3f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                // the players' benches with a towel and a bag
                var bench = new Vector3(-(Court.PostX + 1.3f), 0f, s * 2.6f);
                Box(bench + new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.08f, 2.2f), Wood, 0.3f);
                Box(bench + new Vector3(-0.25f, 0.8f, 0f), new Vector3(0.08f, 0.6f, 2.2f), Wood, 0.3f);
                foreach (float z in new[] { -0.9f, 0.9f }) Box(bench + new Vector3(0f, 0.22f, z), new Vector3(0.5f, 0.44f, 0.08f), StoneDark);
                Box(bench + new Vector3(0.05f, 0.52f, s * 0.5f), new Vector3(0.4f, 0.06f, 0.5f), s < 0 ? Sky : Coral, 0.2f);
                Box(bench + new Vector3(0.6f, 0.2f, -s * 0.4f), new Vector3(0.35f, 0.4f, 0.8f), s < 0 ? Sun : Grape, 0.4f);
            }

            // stands on both sides, and behind the far baseline, with an awning over the far one
            var crowd = new List<(Vector3 pos, float facing)>();
            const int rows = 5;
            foreach (float s in new[] { -1f, 1f })
            {
                for (int r = 0; r < rows; r++)
                {
                    float x0 = bx + 1.2f + r * 1.1f;
                    float top = 0.7f + r * 0.6f;
                    Box(new Vector3(s * (x0 + 4f), top * 0.5f, 2f), new Vector3(8f, top, 32f), r % 2 == 0 ? Stone : StoneDark);
                    Box(new Vector3(s * (x0 + 0.3f), top + 0.06f, 2f), new Vector3(0.5f, 0.12f, 32f), Crowd[r % 3 == 0 ? 2 : r % 3 == 1 ? 0 : 1], 0.3f);
                    for (float z = -13.5f; z <= 17.5f; z += 0.72f)
                        if (rng.NextDouble() < 0.72) crowd.Add((new Vector3(s * (x0 + 0.35f), top, z + (float)(rng.NextDouble() - 0.5) * 0.15f), -s));
                }
            }
            for (int r = 0; r < rows; r++)
            {
                float z0 = bz + 1.4f + r * 1.1f;
                float top = 0.7f + r * 0.6f;
                Box(new Vector3(0f, top * 0.5f, z0 + 4f), new Vector3(2f * (bx + 7.3f), top, 8f), r % 2 == 0 ? Stone : StoneDark);
                Box(new Vector3(0f, top + 0.06f, z0 + 0.3f), new Vector3(2f * (bx + 7.3f), 0.12f, 0.5f), Crowd[r % 3 == 0 ? 3 : r % 3 == 1 ? 0 : 2], 0.3f);
                for (float x = -bx - 6f; x <= bx + 6f; x += 0.72f)
                    if (rng.NextDouble() < 0.72) crowd.Add((new Vector3(x + (float)(rng.NextDouble() - 0.5) * 0.15f, top, z0 + 0.35f), 0f));
            }
            // the awning: stripes in sun and coral on posts
            float awningZ = bz + 1.4f + rows * 1.1f;
            for (int i = 0; i < 14; i++)
            {
                float x = -bx - 6.5f + (2f * bx + 13f) * (i + 0.5f) / 14f;
                Box(new Vector3(x, 5.6f, awningZ - 2.5f), new Vector3((2f * bx + 13f) / 14f, 0.06f, 6.5f), i % 2 == 0 ? Sun : Coral, 0.3f, new Vector3(-12f, 0f, 0f));
            }
            foreach (float x in new[] { -bx - 6f, -bx / 2f, bx / 2f, bx + 6f })
            {
                m.Add(PrimitiveType.Cylinder, new Vector3(x, 2.9f, awningZ - 5.5f), new Vector3(0.12f, 2.9f, 0.12f), Line, 0.4f);
                m.Add(PrimitiveType.Cylinder, new Vector3(x, 3.9f, awningZ + 0.6f), new Vector3(0.12f, 3.9f, 0.12f), Line, 0.4f);
            }
            // flags on the awning
            for (int i = 0; i < 7; i++)
            {
                float x = -bx - 4f + (2f * bx + 8f) * i / 6f;
                m.Add(PrimitiveType.Cylinder, new Vector3(x, 7.4f, awningZ - 0.2f), new Vector3(0.06f, 1.1f, 0.06f), Line, 0.4f);
                Box(new Vector3(x + 0.45f, 8.1f, awningZ - 0.2f), new Vector3(0.9f, 0.55f, 0.03f), Crowd[i % 5], 0.3f);
            }

            // trees and hills behind
            for (int i = 0; i < 16; i++)
            {
                float x = -40f + 80f * i / 15f + (float)(rng.NextDouble() - 0.5) * 3f;
                float z = awningZ + 6f + (float)rng.NextDouble() * 8f;
                if (Mathf.Abs(x) < 18f) z += 4f;
                float h = 4f + (float)rng.NextDouble() * 3f;
                m.Add(PrimitiveType.Cylinder, new Vector3(x, h * 0.35f, z), new Vector3(0.5f, h * 0.35f, 0.5f), Trunk, 0.1f);
                m.Add(PrimitiveType.Sphere, new Vector3(x, h * 0.85f, z), new Vector3(h * 0.75f, h * 0.7f, h * 0.75f), i % 2 == 0 ? Leaf : TreeDark, 0.15f);
            }
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 6; i++)
                {
                    float z = -14f + i * 7f + (float)rng.NextDouble() * 2f;
                    float x = s * (bx + 14f + (float)rng.NextDouble() * 4f), h = 4.5f + (float)rng.NextDouble() * 2.5f;
                    m.Add(PrimitiveType.Cylinder, new Vector3(x, h * 0.35f, z), new Vector3(0.5f, h * 0.35f, 0.5f), Trunk, 0.1f);
                    m.Add(PrimitiveType.Sphere, new Vector3(x, h * 0.85f, z), new Vector3(h * 0.75f, h * 0.7f, h * 0.75f), i % 2 == 0 ? TreeDark : Leaf, 0.15f);
                }
            m.Add(PrimitiveType.Sphere, new Vector3(-30f, -6f, 95f), new Vector3(90f, 30f, 30f), Hex(0x9ED98A), 0f);
            m.Add(PrimitiveType.Sphere, new Vector3(40f, -8f, 105f), new Vector3(110f, 34f, 30f), Hex(0xB2E39B), 0f);

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
        }

        /// <summary>The crowd in sections of about eight metres (each its own mesh, so they can bob out of step).</summary>
        void BuildCrowd(List<(Vector3 pos, float facing)> people, Material toon, System.Random rng)
        {
            var sections = new Dictionary<int, PartMesh>();
            var head = TennisArt.LowSphere();
            foreach (var (pos, facing) in people)
            {
                int key = facing == 0f ? 100 + Mathf.FloorToInt((pos.x + 30f) / 8f) : (facing < 0f ? 200 : 300) + Mathf.FloorToInt((pos.z + 20f) / 8f);
                if (!sections.TryGetValue(key, out var m)) sections[key] = m = new PartMesh { Srgb = true };
                var shirt = Crowd[rng.Next(Crowd.Length)];
                var skin = Skins[rng.Next(Skins.Length)];
                var hair = Hairs[rng.Next(Hairs.Length)];
                float yaw = facing == 0f ? 180f : facing > 0f ? 90f : -90f;
                var turn = Quaternion.Euler(0f, yaw, 0f);
                float size = 0.85f + (float)rng.NextDouble() * 0.3f;
                m.Add(PrimitiveType.Cube, Matrix4x4.TRS(pos + Vector3.up * 0.32f * size, turn, new Vector3(0.38f, 0.5f, 0.3f) * size), shirt, 0.1f);
                m.Add(head, Matrix4x4.TRS(pos + Vector3.up * 0.75f * size, turn, Vector3.one * 0.3f * size), skin, 0.1f);
                m.Add(head, Matrix4x4.TRS(pos + Vector3.up * 0.82f * size + turn * new Vector3(0f, 0f, -0.03f), turn, new Vector3(0.32f, 0.2f, 0.32f) * size), hair, 0.2f);
            }
            foreach (var pair in sections)
            {
                var root = new GameObject("Crowd").transform;
                root.SetParent(transform, false);
                var r = PartMesh.Show("People", root, pair.Value.Build("Tennis crowd"), toon);
                r.receiveShadows = false;
                _sections.Add(new Section { T = root, Phase = (float)rng.NextDouble() * 6.28f });
            }
        }

        static void UmpireChair(PartMesh m, Vector3 at)
        {
            var white = Line;
            foreach (var (x, z) in new[] { (-0.35f, -0.35f), (0.35f, -0.35f), (-0.35f, 0.35f), (0.35f, 0.35f) })
                m.Add(PrimitiveType.Cylinder, at + new Vector3(x, 1f, z), new Vector3(0.07f, 1f, 0.07f), white, 0.4f);
            for (int i = 1; i <= 4; i++) m.Add(PrimitiveType.Cube, at + new Vector3(-0.36f, i * 0.4f, 0f), new Vector3(0.05f, 0.05f, 0.7f), white, 0.4f);   // the ladder
            m.Add(PrimitiveType.Cube, at + new Vector3(0f, 2.05f, 0f), new Vector3(0.9f, 0.1f, 0.9f), Wood, 0.3f);
            m.Add(PrimitiveType.Cube, at + new Vector3(0.42f, 2.5f, 0f), new Vector3(0.08f, 0.8f, 0.9f), Wood, 0.3f);
            m.Add(PrimitiveType.Cube, at + new Vector3(0.05f, 2.2f, 0f), new Vector3(0.6f, 0.2f, 1.1f), Sun, 0.3f);   // a parasol-yellow seat panel
            // the umpire: navy blazer, a white cap, looking at the court
            m.Add(PrimitiveType.Capsule, at + new Vector3(0.05f, 2.55f, 0f), new Vector3(0.4f, 0.32f, 0.36f), Navy, 0.2f);
            m.Add(PrimitiveType.Sphere, at + new Vector3(0f, 3.05f, 0f), Vector3.one * 0.36f, Skin, 0.1f);
            m.Add(PrimitiveType.Sphere, at + new Vector3(0.02f, 3.16f, 0f), new Vector3(0.38f, 0.2f, 0.38f), white, 0.3f);
            m.Add(PrimitiveType.Cube, at + new Vector3(-0.17f, 3.1f, 0f), new Vector3(0.18f, 0.03f, 0.3f), white, 0.3f);   // the cap's peak
            m.Add(PrimitiveType.Sphere, at + new Vector3(-0.16f, 3.07f, -0.08f), Vector3.one * 0.05f, Night, 0.6f);
            m.Add(PrimitiveType.Sphere, at + new Vector3(-0.16f, 3.07f, 0.08f), Vector3.one * 0.05f, Night, 0.6f);
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
}
