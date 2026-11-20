using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Fairy
{
    [System.Serializable]
    public sealed class FairyWorldArt
    {
        [Tooltip("art/fairy's garden kit (one mesh each, vertex colours, the FairyTree among them)")] public GameObject props;
        [Tooltip("Tundu/Realm _GROUND with the detail map: the meadow and its paths")] public Material ground;
        [Tooltip("Tundu/Realm, instanced: the props")] public Material prop;
        [Tooltip("Tundu/Realm, instanced, not culled: the grass")] public Material grass;
        [Tooltip("Tundu/Realm, instanced, little haze: the clouds")] public Material cloud;
        [Tooltip("Tundu/Realm _SKY: the sky dome")] public Material sky;
        [Tooltip("Tundu/Fx, added on, the soft dot: glows")] public Material glow;
        [Tooltip("Tundu/Fx, alpha, the soft dot: the shadows under the creatures and the fairies")] public Material shadow;
    }

    /// <summary>
    /// Zana!'s garden as drawn (the look's overhaul of 2026-11-17: a storybook garden in the late-afternoon sun), seen
    /// from the front like a stage, in layers: the meadow in front with grass, flowers and stones, the two cobbled paths
    /// coming in from the left and the right (where the gloom's creatures walk) to the fairy tree in the middle (its door
    /// and windows toward the camera), trees framing the paths' ends, the forest on rolling hills behind, a castle on a far
    /// hill, mountains melting into the haze, puffy clouds, the sky (a dome of vertex colours: blue above, a pink band at
    /// the horizon opposite the low sun). Everything is art/fairy's kit, lit per vertex by Tundu/Realm (no textures but
    /// the meadow's one detail read), drawn instanced (a draw a kind, only what the camera sees). The props' shadows are
    /// painted into the meadow; the creatures' and the fairies' are soft dots (<see cref="Shadow"/>). The garden grows: a
    /// flower, a mushroom, a crystal or a lantern round the tree for every wave ever saved
    /// (<see cref="GardenProgress.Decorations"/>), popping in as a wave is saved. The tree's hurt shows: the gloom
    /// (<c>_FairyGloom</c>) creeps over the garden as it loses health.
    /// </summary>
    public sealed class GardenWorld : MonoBehaviour
    {
        // the light and the colours of the garden (sRGB)
        /// <summary>Where the sunlight goes (from the upper left, a little from the camera's side: faces toward the camera
        /// lit, the right sides in the cool shade).</summary>
        public static readonly Vector3 SunTravel = new Vector3(0.68f, -0.6f, 0.42f).normalized;
        static readonly Color SkyTop = H("#3F8DE6"), SkyHigh = H("#7DB9F2"), SkyLow = H("#C9DDF6"), SkyBand = H("#FFD3DC"),
                              SkyHorizon = H("#FFE6D2"), SkyBelow = H("#E9D4E4");
        static readonly Color HazeNear = H("#FFE6DA"), HazeFar = H("#C8C3F0");
        static readonly Color AmbientSky = H("#A8C6FF"), AmbientBounce = H("#79BE6A");
        const float CastleX = -60f, CastleZ = 125f;

        FairyWorldArt _art;
        Material _sky, _ground, _props, _grass, _clouds, _far, _small, _treeMat;
        float _treeLife = -1f;
        Mesh _groundMesh, _domeMesh, _shadowMesh;
        readonly List<Object> _made = new List<Object>();
        readonly Dictionary<string, int> _kindOf = new Dictionary<string, int>();
        readonly List<Mesh> _kinds = new List<Mesh>();
        readonly List<Material> _kindMaterial = new List<Material>();
        readonly List<Vector4> _kindBounds = new List<Vector4>();
        // how far away each kind is still drawn (small things are specks beyond a few metres: tiny triangles cost the
        // box's GPU more than their pixels), and which kinds are far scenery (PerfLab far=0)
        readonly List<float> _kindReach = new List<float>();
        readonly List<bool> _kindFar = new List<bool>();
        readonly List<List<Matrix4x4>> _batches = new List<List<Matrix4x4>>();
        readonly List<(int kind, Matrix4x4 m, Vector4 sphere)> _scenery = new List<(int, Matrix4x4, Vector4)>();
        readonly List<(int kind, Vector3 at, float size, float yaw)> _spots = new List<(int, Vector3, float, float)>();
        // the props' shadows painted into the meadow: where, how wide, how dark
        readonly List<(Vector2 at, float radius, float dark)> _shades = new List<(Vector2, float, float)>();
        readonly Plane[] _planes = new Plane[6];
        System.Random _rng;
        int _tree = -1;
        int _decorations, _shownFrom;
        float _grow;
        float _gloom = -1f;
        MeshRenderer _groundRenderer;
        bool _lite;

        // the soft shadows under the creatures and the fairies: one dynamic mesh
        readonly List<Vector3> _sv = new List<Vector3>(512);
        readonly List<Vector2> _su = new List<Vector2>(512);
        readonly List<Color32> _sc = new List<Color32>(512);
        readonly List<int> _st = new List<int>(768);

        public Color SunColour { get; private set; } = Color.white;
        public float SunIntensity { get; private set; } = 1f;
        /// <summary>The tree's windows, lanterns and lights (for their glows), in the world, and each one's glow size.</summary>
        public readonly List<Vector3> Lights = new List<Vector3>();
        public readonly List<float> LightSizes = new List<float>();
        /// <summary>Where the decorations that just grew stand (for their sparkles); cleared by the game.</summary>
        public readonly List<Vector3> Grown = new List<Vector3>();
        public int Decorations => _decorations;

        public static GardenWorld Create(FairyWorldArt art)
        {
            var go = new GameObject("Garden");
            var w = go.AddComponent<GardenWorld>();
            w._art = art;
            // copies, so the assets stay as they are
            w._sky = w.Copy(art.sky, "Sky dome");
            w._ground = w.Copy(art.ground, "Ground");
            w._props = w.Copy(art.prop, "Props");
            w._grass = w.Copy(art.grass, "Grass") ?? w._props;
            w._clouds = w.Copy(art.cloud, "Clouds") ?? w._props;
            // the mountains and the castle: in the haze, but not lost in it; they, the far trees, the grass and the small
            // things the light without its rim, one wave of wind (on the box every vertex runs the whole light)
            w._far = w.Copy(art.prop, "Far away");
            if (w._far != null)
            {
                w._far.SetFloat("_FogScale", 0.58f);
                w._far.EnableKeyword("_LITE");
            }
            else w._far = w._props;
            // the spirit tree its own copy: its light (the portal, the veins, the canopy's glow) fades as it's hurt
            w._treeMat = w.Copy(art.prop, "Spirit tree") ?? w._props;
            w._small = w.Copy(art.prop, "Small things");
            if (w._small != null) w._small.EnableKeyword("_LITE");
            else w._small = w._props;
            if (w._grass != null && w._grass != w._props) w._grass.EnableKeyword("_LITE");
            RenderSettings.skybox = null;
            w._rng = new System.Random(20261117);
            w.Build();
            w.ApplyLook();
            return w;
        }

        Material Copy(Material m, string name)
        {
            if (m == null) return null;
            var c = new Material(m) { name = name };
            _made.Add(c);
            return c;
        }

        void OnDestroy()
        {
            foreach (var o in _made)
                if (o != null) Destroy(o);
            Shader.SetGlobalFloat("_FairyGloom", 0f);
        }

        int Kind(string name, Material material = null)
        {
            if (_kindOf.TryGetValue(name, out int k)) return k;
            Mesh mesh = null;
            if (_art.props != null)
                foreach (var f in _art.props.GetComponentsInChildren<MeshFilter>(true))
                    if (f.name == name)
                    {
                        mesh = f.sharedMesh;
                        break;
                    }
            if (mesh == null)
            {
                _kindOf[name] = -1;
                return -1;
            }
            k = _kinds.Count;
            _kinds.Add(mesh);
            bool lite = name.StartsWith("FlowerPatch") || name.StartsWith("Rock") || name == "PathStone" || name == "Pebbles" ||
                        name == "Fern" || name.StartsWith("Mushroom") && name != "MushroomGlow";
            _kindMaterial.Add(material ?? (name.StartsWith("Grass") ? _grass : name.StartsWith("Cloud") ? _clouds
                                           : name == "FairyTree" ? _treeMat
                                           : name == "Mountains" || name == "Castle" ? _far : name.EndsWith("Far") ? _far
                                           : lite ? _small : _props));
            var b = mesh.bounds;
            _kindBounds.Add(new Vector4(b.center.x, b.center.y, b.center.z, b.extents.magnitude));
            bool small = name.StartsWith("Flower") || name.StartsWith("Grass") || name == "Pebbles" || name == "PathStone" || name.StartsWith("Mushroom");
            _kindReach.Add(name.StartsWith("Flower") || name == "GrassFlowers" ? 15f : small ? 22f : 1e5f);
            _kindFar.Add(name.EndsWith("Far") || name.StartsWith("Cloud") || name == "Mountains" || name == "Castle" || name == "Pine");
            _batches.Add(new List<Matrix4x4>(256));
            _kindOf[name] = k;
            return k;
        }

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        // ------------------------------------------------------------------------------------------------ the layout

        /// <summary>The paths' middle line (z) at <paramref name="x"/>: from the tree out to both edges, wobbling a
        /// little.</summary>
        public static float PathZ(float x) => GardenRun.TreeZ - 0.1f + Mathf.Sin(x * 0.19f + 0.4f) * 0.35f;

        /// <summary>How much of the path is at (x, z): 1 on it, 0 off it (soft over 0.45 m).</summary>
        static float PathAt(float x, float z)
        {
            float half = 1.25f + 0.12f * Mathf.Sin(x * 0.7f);
            float band = Mathf.Clamp01((half - Mathf.Abs(z - PathZ(x))) / 0.45f + 0.5f);
            band *= Mathf.Clamp01((Mathf.Abs(x) - 1.6f) / 0.6f);
            // a round of cobbles before the tree's door, where the paths meet
            float plaza = Mathf.Clamp01((2.35f - Vector2.Distance(new Vector2(x, z), new Vector2(0f, GardenRun.TreeZ - 0.6f))) / 0.45f + 0.5f);
            return Mathf.Max(band, plaza);
        }

        static bool NearPath(float x, float z, float margin) => Mathf.Abs(z - PathZ(x)) < 1.3f + margin && Mathf.Abs(x) > 1.4f;

        /// <summary>The meadow's height: flat where the game is played, rolling up into hills behind and at the sides.</summary>
        public static float Height(float x, float z)
        {
            float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(16f, 70f, z));
            float sides = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(16f, 50f, Mathf.Abs(x)));
            float rolling = 0.6f + 0.4f * Mathf.Sin(x * 0.075f + 1.3f) * Mathf.Cos(z * 0.05f + 0.4f);
            float hills = (back * 12f + sides * 5f) * rolling;
            // the castle's hill, far back on the left (seen past the tree's crown, no trees in the way)
            float castle = 10f * Mathf.Exp(-((x - CastleX) * (x - CastleX) + (z - CastleZ) * (z - CastleZ)) / (2f * 24f * 24f));
            float bumps = Mathf.Sin(x * 0.31f) * Mathf.Cos(z * 0.27f) * 0.25f * Mathf.InverseLerp(10f, 22f, Mathf.Max(Mathf.Abs(x), z));
            return hills + castle + bumps;
        }

        void Build()
        {
            // the tree, its door toward the camera (the props face -Y in Blender: +z here; turned round)
            _tree = Kind("FairyTree");
            if (_tree >= 0)
            {
                var m = Matrix4x4.TRS(GardenRun.Tree, Quaternion.Euler(0f, 180f, 0f), Vector3.one);
                Add(_tree, m);
                foreach (var (local, size) in GlowPoints(_kinds[_tree]))
                {
                    Lights.Add(m.MultiplyPoint3x4(local));
                    LightSizes.Add(size);
                }
                Shade(GardenRun.Tree.x + 0.4f, GardenRun.Tree.z + 0.4f, 5.2f, 0.5f);
                Shade(GardenRun.Tree.x, GardenRun.Tree.z, 2.4f, 0.35f);
            }
            BuildFar();
            BuildForest();
            BuildMeadow();
            BuildDecorationSpots();
            BuildGround();
            _domeMesh = Keep(Dome());
            long tris = 0, grassTris = 0;
            foreach (var (kind, _, _) in _scenery)
            {
                long t = _kinds[kind].triangles.Length / 3;
                tris += t;
                if (_kindMaterial[kind] == _grass) grassTris += t;
            }
            Debug.Log($"[Fairy] The garden: {_scenery.Count} things, {tris / 1000}k triangles ({grassTris / 1000}k grass), the meadow {_groundMesh.vertexCount} vertices, {Lights.Count} lights");
            _shadowMesh = Keep(new Mesh { name = "Shadows", indexFormat = IndexFormat.UInt32 });
            _shadowMesh.MarkDynamic();
        }

        Mesh Keep(Mesh m)
        {
            _made.Add(m);
            return m;
        }

        /// <summary>Mountains in the haze, a castle on a far hill, clouds.</summary>
        void BuildFar()
        {
            Place("Mountains", new Vector3(10f, -3f, 235f), new Vector3(1f, 1.35f, 1f), 180f);
            Place("Mountains", new Vector3(-60f, -6f, 185f), new Vector3(0.7f, 0.9f, 1f), 180f);
            Place("Castle", new Vector3(CastleX, Height(CastleX, CastleZ) - 0.6f, CastleZ), 1.1f, 205f);
            var clouds = new[] { "Cloud1", "Cloud2", "Cloud3" };
            var spots = new[]
            {
                new Vector3(-150f, 62f, 215f), new Vector3(-62f, 78f, 240f), new Vector3(30f, 70f, 235f), new Vector3(120f, 58f, 220f),
                new Vector3(190f, 74f, 205f), new Vector3(-100f, 40f, 175f), new Vector3(0f, 96f, 225f),
            };
            for (int i = 0; i < spots.Length; i++)
                Place(clouds[i % 3], spots[i], new Vector3(1f, 1f, 1f) * R(1.3f, 2.2f), 180f + R(-25f, 25f));
        }

        /// <summary>The trees: a few full ones framing the paths' ends, the forest on the hills behind (in groups, with gaps
        /// where the hills and the sky show), far ones in fewer triangles.</summary>
        void BuildForest()
        {
            // framing the paths' ends, both sides (behind the paths, never on them, out at the picture's sides)
            var frame = new (string kind, float x, float z, float size)[]
            {
                ("TreeBlossom", -15f, 11f, 1.1f), ("TreeRound", -21.5f, 15f, 1.3f), ("TreeRound", 15.5f, 11.5f, 1.15f),
                ("TreeBlossom", 21f, 15.5f, 1.2f), ("Pine", -27f, 20f, 1.4f), ("Pine", 27f, 21f, 1.35f), ("TreeTall", 12f, 19f, 1f),
            };
            foreach (var (kind, x, z, size) in frame)
            {
                Place(kind, new Vector3(x, Height(x, z), z), size, R(0f, 360f));
                Shade(x + 1.2f, z + 0.8f, 2.6f * size, 0.45f);
            }
            // the forest behind: groups on the hills at the sides and far back, gaps between them (the hills show, and
            // the castle far back on the left: nothing on the line to it)
            var far = new[] { "TreeRoundFar", "TreeRoundFar", "TreeBlossomFar", "TreeTallFar", "PineFar", "PineFar" };
            var groups = new (float x, float z, int n, float spread)[]
            {
                (-36f, 27f, 4, 8f), (38f, 25f, 4, 8f), (-52f, 42f, 4, 10f), (54f, 40f, 4, 10f), (-72f, 30f, 2, 9f), (74f, 30f, 2, 9f),
                (22f, 50f, 3, 9f), (-64f, 68f, 3, 12f), (62f, 66f, 3, 12f), (8f, 82f, 3, 14f), (36f, 92f, 2, 14f),
            };
            float castleLine = CastleX / (CastleZ + 6.5f);
            foreach (var (gx, gz, n, spread) in groups)
                for (int i = 0; i < n; i++)
                {
                    float x = gx + R(-spread, spread), z = gz + R(-spread * 0.5f, spread * 0.5f);
                    if (Mathf.Abs(x - castleLine * (z + 6.5f)) < 9f) continue;
                    Place(far[_rng.Next(far.Length)], new Vector3(x, Height(x, z) - 0.2f, z), R(1.2f, 1.7f), R(0f, 360f));
                }
        }

        /// <summary>The meadow: stones along the paths, grass, flowers, bushes, rocks and mushrooms (never on the paths,
        /// never between the camera and the tree's door), a frame of ferns and tall grass in the bottom corners.</summary>
        void BuildMeadow()
        {
            // stones edging both paths (low: the creatures walk past them), pebbles here and there, out past the
            // screen's edges
            for (int side = -1; side <= 1; side += 2)
                for (int edge = -1; edge <= 1; edge += 2)
                    for (float x = 2.7f; x < 13f; x += R(0.7f, 1.0f))
                    {
                        float px = side * x, pz = PathZ(px) + edge * R(1.22f, 1.36f);
                        if (edge < 0 && x < 3.2f) continue;
                        Place("PathStone", new Vector3(px, 0.0f, pz), new Vector3(R(0.55f, 0.8f), R(0.9f, 1.5f), R(0.5f, 0.7f)), R(0f, 360f));
                        if (_rng.NextDouble() < 0.18) Place("Pebbles", new Vector3(px + R(-0.4f, 0.4f), 0f, PathZ(px) + edge * R(1.5f, 1.8f)), 1f, R(0f, 360f));
                    }
            // the paths' edges: little tufts of grass and flowers on both sides (low: the creatures walk past them)
            for (int side = -1; side <= 1; side += 2)
                for (float x = 2.4f; x < 13f; x += R(0.55f, 0.9f))
                    for (int edge = -1; edge <= 1; edge += 2)
                    {
                        float px = side * x, pz = PathZ(px) + edge * R(1.55f, 1.95f);
                        if (pz < GardenRun.TreeZ - 2.2f && x < 3.2f) continue;
                        double roll = _rng.NextDouble();
                        string kind = roll < 0.8 ? "GrassTuft" : roll < 0.91 ? "GrassFlowers" : roll < 0.96 ? (edge > 0 ? "FlowerPatch" : "FlowerPatchBlue") : "GrassTuft";
                        Place(kind, new Vector3(px, 0f, pz), R(0.65f, 0.95f), R(0f, 360f));
                    }
            // grass over the meadow, kept short (it mustn't hide a creature), the tall kind only well away from the
            // paths; none on the paths or before the door, few right in front of the camera (they'd look huge there)
            float cx = 0f, cz = 0f;
            for (int i = 0; i < 520; i++)
            {
                // in clumps: a new clump every few tufts, the tufts round it
                if (i % 5 == 0)
                {
                    cz = Mathf.Lerp(-0.8f, 17f, Mathf.Pow((float)_rng.NextDouble(), 1.25f));
                    cx = R(-1f, 1f) * Mathf.Lerp(6.5f, 24f, Mathf.InverseLerp(-0.8f, 17f, cz));
                }
                float z = cz + R(-0.5f, 0.5f), x = cx + R(-0.7f, 0.7f);
                if (NearPath(x, z, 0.35f) || PathAt(x, z) > 0.05f) continue;
                if (Mathf.Abs(x) < 2.6f && z < GardenRun.TreeZ) continue;
                if (z < 1.5f && _rng.NextDouble() < 0.5) continue;
                float off = Mathf.Abs(z - PathZ(x));
                double roll = _rng.NextDouble();
                string kind = roll < 0.72 ? "GrassTuft" : roll < 0.88 && off > 2.8f ? "GrassTall" : "GrassFlowers";
                float size = kind == "GrassTall" ? R(0.6f, 0.85f) : R(0.7f, 1.05f);
                if (z < 1.5f) size *= 0.8f;
                Place(kind, new Vector3(x, Height(x, z), z), size, R(0f, 360f));
            }
            // flower patches dotted about
            for (int i = 0; i < 15; i++)
            {
                float z = R(-0.5f, 15f), x = R(-1f, 1f) * Mathf.Lerp(6f, 20f, Mathf.InverseLerp(-0.5f, 15f, z));
                if (NearPath(x, z, 0.4f) || (Mathf.Abs(x) < 2.6f && z < GardenRun.TreeZ)) continue;
                Place(i % 3 == 0 ? "FlowerPatchBlue" : "FlowerPatch", new Vector3(x, Height(x, z), z), R(0.9f, 1.4f), R(0f, 360f));
            }
            // bushes, rocks and mushrooms at the sides and behind the paths
            var sides = new[] { "Bush", "BushFlower", "Rock", "RockSmall", "Mushroom", "MushroomTrio", "Bush", "RockSmall" };
            for (int i = 0; i < 26; i++)
            {
                float x = R(5f, 22f) * (i % 2 == 0 ? -1f : 1f);
                float z = R(-0.5f, 14f);
                if (NearPath(x, z, 0.8f)) z = z < GardenRun.TreeZ ? R(-0.5f, GardenRun.TreeZ - 2.6f) : R(GardenRun.TreeZ + 2.6f, 14f);
                // in front of the paths only far out at the sides (the paths' ends must show)
                if (z < GardenRun.TreeZ && Mathf.Abs(x) < 9f) x = Mathf.Sign(x) * R(9f, 16f);
                string kind = sides[_rng.Next(sides.Length)];
                float size = kind.StartsWith("Rock") ? R(0.8f, 1.4f) : kind.StartsWith("Bush") ? R(0.85f, 1.3f) : R(0.8f, 1.2f);
                Place(kind, new Vector3(x, Height(x, z), z), size, R(0f, 360f));
                Shade(x + 0.2f, z + 0.15f, (kind.StartsWith("Bush") ? 1.1f : 0.6f) * size, 0.4f);
            }
            // the bottom corners: ferns close to the camera (low: they frame the picture, the paths show above them)
            Place("Fern", new Vector3(-4.8f, 0f, -0.4f), 0.62f, 20f);
            Place("Fern", new Vector3(5f, 0f, -0.2f), 0.58f, 200f);
            Place("Fern", new Vector3(-6.6f, 0f, 1.3f), 0.7f, 120f);
            Place("Fern", new Vector3(6.8f, 0f, 1.5f), 0.66f, 300f);
        }

        /// <summary>The decorations' spots, nearest the tree first, both sides by turns: beside the trunk and behind the
        /// paths (never between the camera and the fairy, never in front of the door, never on a path).</summary>
        void BuildDecorationSpots()
        {
            var decor = new[] { "FlowerDaisy", "FlowerTulip", "MushroomGlow", "FlowerBell", "CrystalCluster", "Lantern", "BushFlower", "MushroomTrio" };
            for (int i = 0; i < GardenProgress.MaxDecorations; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float ring = 2.8f + i * 0.17f;
                float a = R(-0.2f, 1.35f);
                var at = GardenRun.Tree + new Vector3(side * ring * Mathf.Cos(a * 0.6f), 0f, ring * Mathf.Sin(a));
                if (at.z < GardenRun.TreeZ - 0.4f && Mathf.Abs(at.x) < 2.8f) at.x = side * 3f;
                // off the paths: behind them
                if (NearPath(at.x, at.z, 0.2f)) at.z = PathZ(at.x) + R(1.6f, 2.8f);
                int kind = Kind(decor[i % decor.Length]);
                if (kind >= 0) _spots.Add((kind, at, R(0.5f, 0.8f), R(-40f, 40f) + 180f));
            }
        }

        void Place(string name, Vector3 at, float size, float yaw) => Place(name, at, Vector3.one * size, yaw);

        void Place(string name, Vector3 at, Vector3 size, float yaw)
        {
            int k = Kind(name);
            if (k >= 0) Add(k, Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), size));
        }

        void Add(int kind, Matrix4x4 m)
        {
            var b = _kindBounds[kind];
            var c = m.MultiplyPoint3x4(new Vector3(b.x, b.y, b.z));
            float scale = Mathf.Max(m.GetColumn(0).magnitude, Mathf.Max(m.GetColumn(1).magnitude, m.GetColumn(2).magnitude));
            _scenery.Add((kind, m, new Vector4(c.x, c.y, c.z, b.w * scale)));
        }

        /// <summary>A soft shadow painted into the meadow (the sun's from the upper left: under things and a little to the
        /// right and back).</summary>
        void Shade(float x, float z, float radius, float dark) => _shades.Add((new Vector2(x, z), radius, dark));

        /// <summary>The kit's glowing bits of a mesh (its vertices whose glow is full) gathered into points: each with how
        /// big a glow suits it.</summary>
        static List<(Vector3 local, float size)> GlowPoints(Mesh mesh)
        {
            var points = new List<(Vector3 sum, int n, Vector3 min, Vector3 max)>();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            mesh.GetVertices(verts);
            mesh.GetUVs(0, uvs);
            for (int i = 0; i < verts.Count && i < uvs.Count; i++)
            {
                if (uvs[i].y < 0.95f) continue;
                var p = verts[i];
                int found = -1;
                for (int k = 0; k < points.Count; k++)
                    if ((points[k].sum / points[k].n - p).sqrMagnitude < 0.45f * 0.45f)
                    {
                        found = k;
                        break;
                    }
                if (found < 0) points.Add((p, 1, p, p));
                else
                {
                    var q = points[found];
                    points[found] = (q.sum + p, q.n + 1, Vector3.Min(q.min, p), Vector3.Max(q.max, p));
                }
            }
            var result = new List<(Vector3, float)>();
            foreach (var (sum, n, min, max) in points)
                result.Add((sum / n, Mathf.Clamp((max - min).magnitude * 2.2f, 0.35f, 1.6f)));
            return result;
        }

        // ------------------------------------------------------------------------------------------------ the meadow's mesh

        static Color H(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx), Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx), fy);
        }

        /// <summary>The grass's own colour at (x, z): two greens in big soft patches, a sunny glade round the tree, a
        /// little cooler far off.</summary>
        static Color GrassColour(float x, float z)
        {
            float n = Noise(x * 0.09f + 11f, z * 0.09f), n2 = Noise(x * 0.31f, z * 0.31f + 4f);
            var c = Color.Lerp(H("#5DB85A"), H("#8ACF5C"), n * 0.8f + n2 * 0.2f);
            float d = Vector2.Distance(new Vector2(x, z), new Vector2(0f, GardenRun.TreeZ));
            c = Color.Lerp(c, H("#A2DA66"), Mathf.Clamp01(1f - d / 7f) * 0.45f);
            // the hills behind: big soft patches of deeper and sunnier greens, and fields of flowers (a storybook's)
            float far = Mathf.InverseLerp(18f, 45f, Mathf.Max(z, Mathf.Abs(x) * 0.9f));
            if (far > 0f)
            {
                float patch = Noise(x * 0.035f + 3f, z * 0.035f + 7f);
                var hills = Color.Lerp(H("#3E9A62"), H("#9BD460"), Mathf.SmoothStep(0.25f, 0.75f, patch));
                float field = Noise(x * 0.05f + 40f, z * 0.05f + 2f);
                if (field > 0.68f) hills = Color.Lerp(hills, field > 0.8f ? H("#F6A8CC") : H("#F3E27A"), Mathf.SmoothStep(0.68f, 0.76f, field) * 0.7f);
                c = Color.Lerp(c, hills, far);
            }
            return c;
        }

        /// <summary>How far out (z, and 1.3x that to the sides) the meadow has its detail; beyond, the hills.</summary>
        const float NearGround = 26f;

        /// <summary>Row or column places: fine where the game is played, coarser out to the edges.</summary>
        static List<float> Steps(float from, float to, float fineFrom, float fineTo, float fine, float grow)
        {
            var list = new List<float>();
            float v = from, step = fine;
            while (v < to)
            {
                list.Add(v);
                if (v >= fineFrom && v < fineTo) step = fine;
                else
                {
                    float off = v < fineFrom ? fineFrom - v : v - fineTo;
                    step = fine + off * grow;
                }
                v += step;
            }
            list.Add(to);
            return list;
        }

        void BuildGround()
        {
            var xs = new List<float>();
            foreach (var x in Steps(0f, 120f, 0f, 15f, 0.42f, 0.09f)) xs.Add(x);
            var cols = new List<float>();
            for (int i = xs.Count - 1; i > 0; i--) cols.Add(-xs[i]);
            cols.AddRange(xs);
            var rows = Steps(-9f, 150f, -1.5f, 9.5f, 0.4f, 0.08f);
            int nc = cols.Count, nr = rows.Count;
            var verts = new Vector3[nc * nr];
            var colours = new Color[nc * nr];
            var uvs = new Vector2[nc * nr];
            for (int r = 0; r < nr; r++)
                for (int k = 0; k < nc; k++)
                {
                    float x = cols[k], z = rows[r];
                    int i = r * nc + k;
                    verts[i] = new Vector3(x, Height(x, z), z);
                    float path = PathAt(x, z);
                    // the props' shadows: soft, darkest under them
                    float shadow = 0f;
                    foreach (var (at, radius, dark) in _shades)
                    {
                        float d = Vector2.Distance(new Vector2(x, z), at) / radius;
                        if (d < 1f) shadow = Mathf.Max(shadow, dark * (1f - d * d) * (1f - d * d));
                    }
                    var c = GrassColour(x, z).linear;   // vertex colours reach the shader as they are: linear
                    c.a = 1f - shadow;
                    colours[i] = c;
                    uvs[i] = new Vector2(path, 0f);
                }
            // two parts: the near meadow (the paths, the grass's dabs: one detail read a pixel) and the hills beyond it, in the
            // haze, lit at the vertices only like the props (on the box the meadow's pixels were its biggest cost)
            var near = new List<int>((nr - 1) * (nc - 1) * 6);
            var far = new List<int>((nr - 1) * (nc - 1) * 3);
            for (int r = 0; r < nr - 1; r++)
                for (int k = 0; k < nc - 1; k++)
                {
                    int a = r * nc + k, b = a + 1, d = a + nc, e = d + 1;
                    bool detail = rows[r + 1] <= NearGround && Mathf.Max(Mathf.Abs(cols[k]), Mathf.Abs(cols[k + 1])) <= NearGround * 1.3f;
                    var list = detail ? near : far;
                    list.Add(a); list.Add(d); list.Add(b);
                    list.Add(b); list.Add(d); list.Add(e);
                }
            _groundMesh = Keep(new Mesh { name = "Garden ground", indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 });
            _groundMesh.vertices = verts;
            _groundMesh.colors = colours;
            _groundMesh.uv = uvs;
            _groundMesh.subMeshCount = 2;
            _groundMesh.SetTriangles(near, 0);
            _groundMesh.SetTriangles(far, 1);
            _groundMesh.RecalculateNormals();
            _groundMesh.RecalculateBounds();
            var go = new GameObject("Ground");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _groundMesh;
            var mr = go.AddComponent<MeshRenderer>();
            _groundRenderer = mr;
            var hills = Copy(_art.prop, "Hills");
            if (hills != null)
            {
                hills.EnableKeyword("_LITE");
                hills.SetFloat("_Sway", 0f);
                hills.enableInstancing = false;
            }
            mr.sharedMaterials = new[] { _ground, hills ?? _ground };
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ------------------------------------------------------------------------------------------------ the sky

        /// <summary>The sky's colour toward <paramref name="d"/>: deep blue above, lighter lower down, a pink band and a
        /// warm glow at the horizon (the low sun is behind the camera), lilac haze below it.</summary>
        static Color SkyColour(Vector3 d)
        {
            float e = d.y;
            Color c;
            if (e < 0f) c = Color.Lerp(SkyHorizon, SkyBelow, Mathf.Clamp01(-e * 4f));
            else if (e < 0.06f) c = Color.Lerp(SkyHorizon, SkyBand, e / 0.06f);
            else if (e < 0.2f) c = Color.Lerp(SkyBand, SkyLow, (e - 0.06f) / 0.14f);
            else if (e < 0.5f) c = Color.Lerp(SkyLow, SkyHigh, (e - 0.2f) / 0.3f);
            else c = Color.Lerp(SkyHigh, SkyTop, (e - 0.5f) / 0.5f);
            // warmer toward the sun's side
            float sun = Mathf.Clamp01(Vector3.Dot(d, -SunTravel));
            c = Color.Lerp(c, H("#FFF0D0"), sun * sun * 0.5f * (1f - Mathf.Clamp01(e * 1.5f)));
            return c;
        }

        static Mesh Dome()
        {
            // rings close together near the horizon (where the colours change), fewer up high
            float[] elevations = { -40f, -12f, -3f, 0f, 1.5f, 3f, 5f, 8f, 12f, 17f, 24f, 33f, 45f, 60f, 75f, 90f };
            const int seg = 40;
            var verts = new List<Vector3>();
            var colours = new List<Color>();
            var tris = new List<int>();
            foreach (var el in elevations)
                for (int k = 0; k <= seg; k++)
                {
                    float az = k / (float)seg * Mathf.PI * 2f, e = el * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Cos(e) * Mathf.Sin(az), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Cos(az));
                    verts.Add(d);
                    colours.Add(SkyColour(d).linear);
                }
            for (int r = 0; r < elevations.Length - 1; r++)
                for (int k = 0; k < seg; k++)
                {
                    int a = r * (seg + 1) + k, b = a + 1, c = a + seg + 1, d = c + 1;
                    // seen from inside
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            var m = new Mesh { name = "Sky dome" };
            m.SetVertices(verts);
            m.SetColors(colours);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------------------------------------ the look

        void ApplyLook()
        {
            float g = Mathf.Max(0f, _gloom);
            Shader.SetGlobalColor("_FairySky", Grey(AmbientSky, g));
            Shader.SetGlobalColor("_FairyBounce", Grey(AmbientBounce, g));
            Shader.SetGlobalColor("_FairyHaze", Grey(HazeNear, g));
            Shader.SetGlobalColor("_FairyHazeFar", Grey(HazeFar, g));
            // haze from 12 m, at most 90 % by 170 m, thinner up high: the forest behind goes soft and lilac
            Shader.SetGlobalVector("_FairyFog", new Vector4(12f, 170f, 0.9f, 0.01f));
            Shader.SetGlobalVector("_FairyWind", new Vector4(0.8f, 0.6f, 0.06f, 1.1f));
            // the fairy (Tundu/Athlete) takes the garden's light through Unity's ambient and fog
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Grey(HazeNear, g);
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 260f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Grey(H("#B4CCFF"), g);
            RenderSettings.ambientEquatorColor = Grey(H("#FFD8D4"), g);
            RenderSettings.ambientGroundColor = Grey(H("#84C278"), g);
            SunColour = Grey(H("#FFE6C2"), g * 0.8f);
            SunIntensity = Mathf.Lerp(1.12f, 0.9f, g);
        }

        static Color Grey(Color c, float g)
        {
            float l = c.r * 0.3f + c.g * 0.55f + c.b * 0.15f;
            var grey = new Color(l * 0.62f + 0.06f, l * 0.55f + 0.05f, l * 0.72f + 0.1f);
            return Color.Lerp(c, grey, g * 0.6f);
        }

        /// <summary>The gloom over the garden (0 = all colour .. 1 = grey), eased: the tree's hurt shows in the garden.</summary>
        /// <summary>The spirit tree's health (0 .. 1): its own light glows full when it's well and fades as it's hurt.</summary>
        public void SetTreeLife(float health, float dt)
        {
            float before = _treeLife;
            _treeLife = before < 0f ? Mathf.Clamp01(health) : Mathf.MoveTowards(_treeLife, Mathf.Clamp01(health), dt * 0.6f);
            if (_treeMat != null && _treeMat != _props && Mathf.Abs(before - _treeLife) > 1e-3f)
                _treeMat.SetFloat("_Glow", Mathf.Lerp(0.6f, 1.75f, _treeLife));
        }

        public void SetGloom(float target, float dt)
        {
            float before = _gloom;
            _gloom = Mathf.MoveTowards(Mathf.Max(0f, _gloom), Mathf.Clamp01(target), dt * 0.5f);
            if (before < 0f || Mathf.Abs(before - _gloom) > 1e-4f) ApplyLook();
            Shader.SetGlobalFloat("_FairyGloom", _gloom);
        }

        // ------------------------------------------------------------------------------------------------ the garden grows

        /// <summary>Shows <paramref name="count"/> decorations at once (a run's start).</summary>
        public void SetDecorations(int count)
        {
            _decorations = Mathf.Clamp(count, 0, _spots.Count);
            _shownFrom = _decorations;
            _grow = 1f;
        }

        /// <summary>The garden grows to <paramref name="count"/> decorations, the new ones popping up.</summary>
        public void Grow(int count)
        {
            count = Mathf.Clamp(count, 0, _spots.Count);
            if (count <= _decorations) return;
            _shownFrom = _decorations;
            for (int i = _decorations; i < count; i++) Grown.Add(_spots[i].at + Vector3.up * 0.6f);
            _decorations = count;
            _grow = 0f;
        }

        // ------------------------------------------------------------------------------------------------ shadows

        /// <summary>A soft round shadow on the meadow under something at <paramref name="at"/> (its ground point), this
        /// wide and this dark (gathered for the frame, drawn by <see cref="Draw"/>).</summary>
        public void Shadow(Vector3 at, float radius, float strength)
        {
            if (strength <= 0.01f || radius <= 0.01f) return;
            float y = Height(at.x, at.z) + 0.03f;
            float rx = radius, rz = radius * 0.8f;
            int i = _sv.Count;
            _sv.Add(new Vector3(at.x - rx, y, at.z - rz));
            _sv.Add(new Vector3(at.x - rx, y, at.z + rz));
            _sv.Add(new Vector3(at.x + rx, y, at.z + rz));
            _sv.Add(new Vector3(at.x + rx, y, at.z - rz));
            _su.Add(new Vector2(0f, 0f));
            _su.Add(new Vector2(0f, 1f));
            _su.Add(new Vector2(1f, 1f));
            _su.Add(new Vector2(1f, 0f));
            var c = (Color32)new Color(0.16f, 0.12f, 0.32f, Mathf.Clamp01(strength));
            for (int k = 0; k < 4; k++) _sc.Add(c);
            _st.Add(i); _st.Add(i + 1); _st.Add(i + 2);
            _st.Add(i); _st.Add(i + 2); _st.Add(i + 3);
        }

        void DrawShadows()
        {
            _shadowMesh.Clear();
            if (_sv.Count > 0 && _art.shadow != null && PerfSwitches.Get("shadows") != "0")
            {
                _shadowMesh.SetVertices(_sv);
                _shadowMesh.SetUVs(0, _su);
                _shadowMesh.SetColors(_sc);
                _shadowMesh.SetTriangles(_st, 0, false);
                var bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
                _shadowMesh.bounds = bounds;
                Graphics.RenderMesh(new RenderParams(_art.shadow) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = bounds },
                    _shadowMesh, 0, Matrix4x4.identity);
            }
            _sv.Clear();
            _su.Clear();
            _sc.Clear();
            _st.Clear();
        }

        // ------------------------------------------------------------------------------------------------ every frame

        /// <summary>Draws the sky, the tree, the forest, the meadow's things and the decorations (instanced, one draw a
        /// kind, only what the camera sees), and the frame's shadows.</summary>
        public void Draw(Camera cam, float dt)
        {
            _grow = Mathf.Min(1f, _grow + dt * 1.6f);
            if (_sky != null && cam != null && PerfSwitches.Get("sky") != "0")
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = SkyHorizon;
                Graphics.RenderMesh(new RenderParams(_sky) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false },
                    _domeMesh, 0, Matrix4x4.TRS(cam.transform.position, Quaternion.identity, Vector3.one * 50f));
            }
            DrawShadows();
            if (_props == null || _kinds.Count == 0 || PerfSwitches.Get("props") == "0") return;
            bool cull = cam != null && PerfSwitches.Get("propcull") != "0";
            if (cull) GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            foreach (var b in _batches) b.Clear();
            bool grass = PerfSwitches.Get("grass") != "0", far = PerfSwitches.Get("far") != "0", tree = PerfSwitches.Get("tree") != "0";
            bool flowers = PerfSwitches.Get("flowers") != "0";
            var eye = cam != null ? cam.transform.position : Vector3.zero;
            foreach (var (kind, m, sphere) in _scenery)
            {
                if (cull && !InView(sphere)) continue;
                if (!grass && _kindMaterial[kind] == _grass || !far && _kindFar[kind] || !tree && kind == _tree) continue;
                float reach = _kindReach[kind];
                if (reach < 1e4f && (!flowers || (new Vector3(sphere.x, sphere.y, sphere.z) - eye).sqrMagnitude > reach * reach)) continue;
                _batches[kind].Add(m);
            }
            if (_groundRenderer != null) _groundRenderer.enabled = PerfSwitches.Get("ground") != "0";
            // the lite light for every garden material (PerfLab realm=lite: what the rim and the full wind cost)
            bool lite = PerfSwitches.Get("realm") == "lite";
            if (lite != _lite)
            {
                _lite = lite;
                foreach (var mat in new[] { _props, _clouds })
                    if (mat != null)
                    {
                        if (lite) mat.EnableKeyword("_LITE");
                        else mat.DisableKeyword("_LITE");
                    }
            }
            for (int i = 0; i < _decorations && i < _spots.Count; i++)
            {
                var (kind, at, size, yaw) = _spots[i];
                // the new ones pop up with a little overshoot
                float k = i >= _shownFrom ? Mathf.Clamp01(_grow * 1.4f - (i - _shownFrom) * 0.15f) : 1f;
                if (k <= 0f) continue;
                float s = size * (k < 1f ? Mathf.Sin(k * Mathf.PI * 0.75f) / Mathf.Sin(Mathf.PI * 0.75f) : 1f);
                _batches[kind].Add(Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * s));
            }
            for (int i = 0; i < _kinds.Count; i++)
            {
                var list = _batches[i];
                if (list.Count == 0) continue;
                var rp = new RenderParams(_kindMaterial[i]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMeshInstanced(rp, _kinds[i], 0, list);
            }
        }

        bool InView(Vector4 sphere)
        {
            var c = new Vector3(sphere.x, sphere.y, sphere.z);
            for (int i = 0; i < 6; i++)
                if (_planes[i].GetDistanceToPoint(c) < -sphere.w) return false;
            return true;
        }
    }
}
