using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Runner
{
    /// <summary>What the scene builder hands the valley: art/runner's kit and the materials (assets, so their shaders are
    /// in the build; the world makes its variants from them at runtime).</summary>
    [Serializable]
    public sealed class RunnerArt
    {
        [Tooltip("art/runner/valley.py's kit (Valley.fbx: one mesh each)")] public GameObject kit;
        [Tooltip("Tundu/Valley: the props (variants made from it at runtime)")] public Material prop;
        [Tooltip("Tundu/Valley _GROUND with the detail map: the valley floor")] public Material ground;
        [Tooltip("Tundu/Valley _SKY: the sky dome")] public Material sky;
        [Tooltip("Tundu/ValleyFx, alpha, the soft dot: shadows")] public Material shadow;
        [Tooltip("Tundu/ValleyFx, added, the soft dot: stars, glows")] public Material glow;
    }

    /// <summary>
    /// Vrapo!'s valley as drawn (the console polish of 2026-11-18): the runner stays at z = 0 and the world comes toward
    /// it. The floor in 10 m tiles (the earthen path with its three worn trails, the grass, banks rising to the valley's
    /// sandstone walls), each tile carrying its scenery in bands: a verge of low plants hugging the path, big leaves and
    /// bushes walling it in, tree ferns, palms, cycads and monkey-puzzle trees beyond, far forest clumps on the banks.
    /// Far away and still: the volcano, the mountains, drifting clouds, the sky. Everything is art/runner's kit lit per
    /// vertex by Tundu/Valley and drawn instanced (a draw a kind, only within each band's reach, roughly in view).
    ///
    /// The time of day (Alto's Adventure's idea: the same valley in morning light, noon, golden hour, sunset, night with
    /// stars and fireflies, dawn) blends the light's colour and direction, the shade, the sky, the haze and the glow of
    /// glowing things as the run goes on (<see cref="SetTime"/>): purely cosmetic and nearly free (globals, no new art).
    /// Soft round shadows under everything that stands on the ground (no real-time shadows on the box): one dynamic mesh.
    /// </summary>
    public sealed class RunnerWorld : MonoBehaviour
    {
        public const float TileLength = 10f;
        public const float LaneWidth = 2.2f;
        /// <summary>The world starts this far behind the runner (the title view looks back at it) and reaches this far ahead.</summary>
        public const float Behind = 75f, Ahead = 135f;
        public const float Curve = 0.0009f;

        // ------------------------------------------------------------------------------------------------ the look

        /// <summary>One time of day (sRGB colours).</summary>
        struct Look
        {
            public Color Sun, Shade, Sky, Bounce, Rim, RimShade, Haze, HazeFar, SkyTop, SkyHorizon, SkyGlow;
            public float SunIntensity, RimStrength, GlowSize, Night;
            public Vector3 SunFrom;          // where the light comes from
            public Vector4 Fog;              // start, end, most, height falloff
        }

        static Color H(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        static Look L(string sun, float intensity, string shade, string sky, string bounce, string rim, float rimStrength,
                      string rimShade, string haze, string hazeFar, string top, string horizon, string glow, float glowSize,
                      float night, Vector3 from, Vector4 fog) => new Look
        {
            Sun = H(sun), SunIntensity = intensity, Shade = H(shade), Sky = H(sky), Bounce = H(bounce), Rim = H(rim),
            RimStrength = rimStrength, RimShade = H(rimShade), Haze = H(haze), HazeFar = H(hazeFar), SkyTop = H(top),
            SkyHorizon = H(horizon), SkyGlow = H(glow), GlowSize = glowSize, Night = night, SunFrom = from.normalized, Fog = fog,
        };

        /// <summary>The times of day in the order a run goes through them (looping): morning, noon, golden hour, sunset,
        /// night, dawn.</summary>
        static readonly Look[] Looks =
        {
            L("#FFE8C6", 1.08f, "#7782C6", "#A8C6FF", "#8DC66E", "#FFE4B0", 0.42f, "#B6B0FF", "#F7E4D4", "#C6DAF0",
              "#5BA4EA", "#FCE2C4", "#FFE0AE", 14f, 0f, new Vector3(-0.55f, 0.62f, -0.56f), new Vector4(30f, 220f, 0.82f, 0.012f)),
            L("#FFF6E6", 1.16f, "#6A7BC4", "#A6CAFF", "#8ACB6A", "#FFFFFF", 0.32f, "#B8C4FF", "#DCEBF7", "#B8D5F2",
              "#3D90E6", "#CFE8FA", "#FFF4D6", 18f, 0f, new Vector3(-0.32f, 0.84f, -0.43f), new Vector4(35f, 240f, 0.78f, 0.012f)),
            L("#FFC888", 1.1f, "#7868B6", "#E6C8B8", "#A6B65C", "#FFC07A", 0.6f, "#C8A0FF", "#FFD8AA", "#E6B6A6",
              "#6A8CD6", "#FFD49C", "#FFB45E", 9f, 0f, new Vector3(-0.86f, 0.36f, 0.12f), new Vector4(28f, 210f, 0.85f, 0.012f)),
            L("#FF9C6C", 0.96f, "#664E9C", "#E8A6BE", "#8E8A58", "#FF9A5A", 0.7f, "#A68CFF", "#F7AA90", "#C48CB8",
              "#4A4A9C", "#FFA07C", "#FF7C4C", 7f, 0.15f, new Vector3(-0.58f, 0.16f, 0.8f), new Vector4(25f, 190f, 0.88f, 0.012f)),
            L("#A2BCFF", 0.58f, "#26306C", "#4E5EAE", "#2C4A58", "#8EB4FF", 0.55f, "#5C68D6", "#1E2A5C", "#2A3672",
              "#0A1236", "#2C3C7C", "#B8CCFF", 520f, 1f, new Vector3(0.48f, 0.62f, 0.62f), new Vector4(20f, 170f, 0.9f, 0.012f)),
            L("#FFC2A0", 0.92f, "#6C68B6", "#C8B8F0", "#7E9E6E", "#FFC8B0", 0.5f, "#A8A0FF", "#F2C8D0", "#B8B8E8",
              "#5E7ED2", "#FFC8B8", "#FFCEA8", 10f, 0.05f, new Vector3(0.7f, 0.22f, 0.62f), new Vector4(25f, 200f, 0.85f, 0.012f)),
        };

        public static readonly string[] TimeNames = { "morning", "day", "golden", "sunset", "night", "dawn" };

        /// <summary>Seconds each time of day holds, and the blend into the next.</summary>
        public const float TimeHold = 20f, TimeBlend = 6f;

        RunnerArt _art;
        Light _sun;
        Material _props, _lite, _solid, _solidLite, _far, _clouds, _ground, _groundLite, _sky, _shadow, _stars;
        Material[] _groundNear, _groundAway;
        Mesh _dome, _starMesh, _shadowMesh;
        readonly List<Object> _made = new List<Object>();

        // ------------------------------------------------------------------------------------------------ kinds

        sealed class Kind
        {
            public string Name;
            public Mesh Mesh;
            public Material Material;
            public float Reach;                 // drawn only this far ahead (m)
            public float Radius;                // a bounding radius (m, at scale 1)
            public float Shadow;                // the soft shadow's radius under it (0 = none)
            public int Band;                    // 0 the verge, 1 the edge, 2 beside the path, 3 far away
            public Kind FarKind;                // a lighter version for far away (art/runner's ...Far), or null
            public readonly List<Matrix4x4> Batch = new List<Matrix4x4>(128);
        }

        readonly Dictionary<string, Kind> _kinds = new Dictionary<string, Kind>();
        readonly List<Kind> _kindList = new List<Kind>();

        static readonly string[] Verge = { "Verge", "Verge2", "Verge", "FlowerBed", "FlowerBed2", "GrassTall", "GrassFlowers",
                                           "GrassTuft", "RockSmall", "Mushrooms", "Pebbles", "BushFlower", "FlowerPatch",
                                           "FlowerPatchB", "Verge2" };
        static readonly string[] Edge = { "GiantLeaf", "GiantLeaf2", "FernBig", "Bush", "BushBerry", "Horsetail", "Verge2",
                                          "RockSmall2", "GiantLeaf", "Fern", "BushFlower" };
        static readonly string[] Mid = { "TreeFern", "TreeFern2", "Cycad", "Cycad2", "Palm", "TreeRound", "RockBig", "RockBig2",
                                         "Araucaria", "TreeFern", "Palm", "GiantLeaf2" };
        static readonly string[] Far = { "ForestFar", "ForestFar2" };
        static readonly string[] BankGrass = { "GrassTuft", "GrassTall" };
        static readonly string[] Bank = { "Bush", "BushBerry", "GiantLeaf2", "FernBig", "RockBig", "Bush", "GrassTall", "Cycad" };

        /// <summary>Kinds drawn with both sides (single-sided leaves and grass blades).</summary>
        static bool Solid(string name) => name == "Egg" || name == "EggGold" || name.StartsWith("Cloud") || name == "Volcano" ||
                                          name == "Mountains" || name == "Cliff" || name.StartsWith("ForestFar");

        static bool Lite(string name) => name.StartsWith("Grass") || name.StartsWith("FlowerPatch") || name == "Pebbles" ||
                                         name == "Mushrooms" || name.StartsWith("RockSmall") || name.StartsWith("ForestFar") ||
                                         name == "Cliff" || name == "Horsetail";

        Kind KindOf(string name)
        {
            if (_kinds.TryGetValue(name, out var k)) return k;
            Mesh mesh = MeshOf(name);
            k = new Kind { Name = name, Mesh = mesh };
            if (mesh != null)
            {
                k.Radius = mesh.bounds.extents.magnitude;
                k.Material = name == "Volcano" || name == "Mountains" ? _far
                    : name.StartsWith("Cloud") ? _clouds
                    : Solid(name) ? (Lite(name) ? _solidLite : _solid)
                    : Lite(name) || name.EndsWith("Far") ? _lite : _props;
            }
            bool small = Array.IndexOf(Verge, name) >= 0;
            bool edge = Array.IndexOf(Edge, name) >= 0;
            k.Reach = name.StartsWith("ForestFar") || name == "Cliff" ? 1e4f : small ? 62f : edge ? 85f : 125f;
            k.Band = k.Reach > 1e3f ? 3 : small ? 0 : edge ? 1 : 2;
            if (mesh != null && !name.EndsWith("Far") && MeshOf(name + "Far") != null) k.FarKind = KindOf(name + "Far");
            k.Shadow = name.StartsWith("Tree") || name == "Palm" || name == "Araucaria" ? 1.6f
                : name.StartsWith("RockBig") ? 1.9f : name.StartsWith("Cycad") ? 1.1f
                : name.StartsWith("Bush") || name.StartsWith("GiantLeaf") ? 0.95f : 0f;
            _kinds[name] = k;
            _kindList.Add(k);
            return k;
        }

        /// <summary>A kit piece's mesh by name (null if the kit lacks it).</summary>
        public Mesh MeshOf(string name)
        {
            if (_art?.kit == null) return null;
            foreach (var f in _art.kit.GetComponentsInChildren<MeshFilter>(true))
                if (f.name == name) return f.sharedMesh;
            return null;
        }

        /// <summary>The material a kit piece is drawn with (obstacles and eggs, made into GameObjects by the game).</summary>
        public Material MaterialOf(string name) => KindOf(name).Material ?? _props;

        // ------------------------------------------------------------------------------------------------ the floor

        sealed class Tile
        {
            public Transform Root;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public bool Near;                   // drawn with the detailed floor near the path (else all _LITE)
            public readonly List<(Kind kind, Matrix4x4 local, Vector3 at)> Things = new List<(Kind, Matrix4x4, Vector3)>(32);
        }

        readonly List<Tile> _tiles = new List<Tile>();
        readonly List<Mesh> _groundMeshes = new List<Mesh>();

        sealed class Wall
        {
            public float Z;
            public float Side;
        }

        readonly List<Wall> _walls = new List<Wall>();
        const float WallLength = 40f, WallX = 33f;
        Kind _cliff;

        /// <summary>The valley floor's height at x (m): the path and its verges flat, gentle banks from 9 m, steeper toward
        /// the walls.</summary>
        public static float FloorHeight(float x)
        {
            float ax = Mathf.Abs(x);
            if (ax < 9f) return 0f;
            float t = ax - 9f;
            return 0.022f * t * t + 0.03f * t;
        }

        static float PathAmount(float x) => Mathf.Clamp01((3.95f - Mathf.Abs(x)) / 0.4f);

        static float TrailAmount(float x)
        {
            float d = Mathf.Min(Mathf.Abs(x + LaneWidth), Mathf.Min(Mathf.Abs(x), Mathf.Abs(x - LaneWidth)));
            return Mathf.Clamp01(1f - d / 0.75f);
        }

        static readonly float[] Columns =
        {
            0f, 0.4f, 0.75f, 1.1f, 1.45f, 1.8f, 2.2f, 2.6f, 2.95f, 3.3f, 3.6f, 3.95f, 4.4f, 5.2f, 6.5f, 8f, 10f, 13f, 17f, 22f,
            28f, 36f, 48f, 70f,
        };

        /// <summary>Where the floor's detail (the path's pebbles and ragged edge, the grass's dabs) ends: the tile's first
        /// part (submesh 0) reaches this far to each side, the banks beyond are submesh 1 (always lit at the vertices);
        /// tiles further than <see cref="DetailFar"/> draw both parts lit at the vertices (Tundu/Valley _GROUND _LITE).
        /// The shader fades the detail out by the same distances (DETAIL_SIDE, DETAIL_FAR): no seam.</summary>
        public const float DetailSide = 6.5f, DetailFar = 40f;

        /// <summary>A floor tile's mesh: columns dense at the path, rows a metre apart (the bend stays smooth); the grass's
        /// colour drifting in big soft patches (four variants, picked as tiles come round again). Two parts: the path and
        /// its verges (to <see cref="DetailSide"/>), the banks.</summary>
        Mesh GroundMesh(int variant)
        {
            var xs = new List<float>();
            for (int i = Columns.Length - 1; i > 0; i--) xs.Add(-Columns[i]);
            xs.AddRange(Columns);
            int rows = Mathf.RoundToInt(TileLength) + 1;
            var verts = new List<Vector3>(xs.Count * rows);
            var normals = new List<Vector3>(xs.Count * rows);
            var colours = new List<Color>(xs.Count * rows);
            var uv0 = new List<Vector2>(xs.Count * rows);
            var uv1 = new List<Vector2>(xs.Count * rows);
            var grassA = H("#4C9A43");
            var grassB = H("#7AB84C");
            var bank = H("#3C8243");
            float seed = variant * 37.7f;
            for (int r = 0; r < rows; r++)
            {
                float z = -TileLength / 2f + r * (TileLength / (rows - 1));
                for (int c = 0; c < xs.Count; c++)
                {
                    float x = xs[c];
                    float y = FloorHeight(x);
                    verts.Add(new Vector3(x, y, z));
                    float slope = (FloorHeight(x + 0.5f) - FloorHeight(x - 0.5f));
                    normals.Add(new Vector3(-slope, 1f, 0f).normalized);
                    float patch = Mathf.PerlinNoise(x * 0.09f + seed, (z + seed * 3f) * 0.07f);
                    var g = Color.Lerp(grassA, grassB, Mathf.SmoothStep(0.2f, 0.8f, patch));
                    if (Mathf.Abs(x) > 9f) g = Color.Lerp(g, bank, Mathf.Clamp01((Mathf.Abs(x) - 9f) / 22f));
                    // a darker rim of grass right along the path (its plants' shade)
                    float ax = Mathf.Abs(x);
                    float ao = ax > 3.9f && ax < 6f ? 0.72f + 0.28f * Mathf.Clamp01((ax - 4.2f) / 1.6f) : 1f;
                    var lin = g.linear;
                    colours.Add(new Color(lin.r, lin.g, lin.b, ao));
                    uv0.Add(new Vector2(PathAmount(x), TrailAmount(x)));
                    uv1.Add(new Vector2(x, z));
                }
            }
            var inner = new List<int>();
            var outer = new List<int>();
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < xs.Count - 1; c++)
                {
                    int a = r * xs.Count + c, b = a + 1, d = a + xs.Count, e = d + 1;
                    var tris = Mathf.Abs(xs[c]) <= DetailSide + 0.01f && Mathf.Abs(xs[c + 1]) <= DetailSide + 0.01f ? inner : outer;
                    tris.Add(a); tris.Add(d); tris.Add(e);
                    tris.Add(a); tris.Add(e); tris.Add(b);
                }
            var mesh = new Mesh { name = $"Valley floor {variant}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colours);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(inner, 0);
            mesh.SetTriangles(outer, 1);
            // the bend moves it far from its flat bounds: never culled by them
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(160f, 200f, TileLength + 2f));
            _made.Add(mesh);
            return mesh;
        }

        /// <summary>Lays a tile's scenery out afresh (when it's built and each time it comes round again).</summary>
        void Scatter(Tile t)
        {
            t.Things.Clear();
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < 4; k++)
                    Place(t, Verge, side * Random.Range(4.15f, 5.3f), -5f + (k + Random.Range(0f, 0.85f)) * 2.5f, 0.9f, 1.25f);
                for (int k = 0; k < 3; k++)
                    Place(t, Edge, side * Random.Range(5.6f, 8.4f), -5f + (k + Random.Range(0f, 0.9f)) * 3.33f, 0.9f, 1.3f);
                for (int k = 0; k < 3; k++)
                    Place(t, Mid, side * Random.Range(8.6f, 18f), -5f + (k + Random.Range(0f, 0.9f)) * 3.33f, 0.85f, 1.2f);
                // the banks between the trees: bushes, big leaves, rocks, ferns, tufts (no bare green slopes)
                for (int k = 0; k < 2; k++)
                    Place(t, Bank, side * Random.Range(9.5f, 15f), Random.Range(-5f, 5f), 1f, 1.5f);
                // the far clumps: one a side a tile (they're 14-18 m wide: two every 10 m overlapped three times over)
                {
                    float x = side * Random.Range(16f, 28f);
                    var kind = KindOf(Far[Random.Range(0, Far.Length)]);
                    if (kind.Mesh == null) continue;
                    var at = new Vector3(x, FloorHeight(x) - 0.3f, Random.Range(-4.5f, 4.5f));
                    t.Things.Add((kind, Matrix4x4.TRS(at, Quaternion.Euler(0f, (side > 0 ? 90f : -90f) + Random.Range(-20f, 20f), 0f),
                        Vector3.one * Random.Range(0.9f, 1.4f)), at));
                }
                Place(t, BankGrass, side * Random.Range(9f, 16f), Random.Range(-5f, 5f), 1f, 1.4f);
            }
        }

        void Place(Tile t, string[] names, float x, float z, float s0, float s1)
        {
            var kind = KindOf(names[Random.Range(0, names.Length)]);
            if (kind.Mesh == null) return;
            var at = new Vector3(x, FloorHeight(x), z);
            t.Things.Add((kind, Matrix4x4.TRS(at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.one * Random.Range(s0, s1)), at));
        }

        // ------------------------------------------------------------------------------------------------ making it

        public static RunnerWorld Create(RunnerArt art, Light sun)
        {
            var go = new GameObject("Valley");
            var w = go.AddComponent<RunnerWorld>();
            w._art = art;
            w._sun = sun;
            w.Build();
            return w;
        }

        Material Copy(Material m, string name)
        {
            if (m == null) return null;
            var c = new Material(m) { name = name };
            _made.Add(c);
            return c;
        }

        void Build()
        {
            _props = Copy(_art.prop, "Valley props");
            if (_props != null) _props.SetFloat("_Cull", 0f);
            _lite = Copy(_art.prop, "Valley small and far");
            if (_lite != null)
            {
                _lite.SetFloat("_Cull", 0f);
                _lite.EnableKeyword("_LITE");
            }
            _solid = Copy(_art.prop, "Valley closed shapes");
            _solidLite = Copy(_art.prop, "Valley far closed shapes");
            _solidLite?.EnableKeyword("_LITE");
            _far = Copy(_art.prop, "Valley backdrop");
            if (_far != null)
            {
                _far.EnableKeyword("_LITE");
                _far.SetFloat("_Curve", 0.25f);
                _far.SetFloat("_FogScale", 0.75f);
            }
            _clouds = Copy(_art.prop, "Valley clouds");
            if (_clouds != null)
            {
                _clouds.SetFloat("_Curve", 0f);
                _clouds.SetFloat("_FogScale", 0.35f);
                _clouds.EnableKeyword("_LITE");
            }
            _ground = Copy(_art.ground, "Valley floor");
            _groundLite = Copy(_art.ground, "Valley floor, far and banks");
            _groundLite?.EnableKeyword("_LITE");
            // the floor after everything standing on it (its pixels under the plants are never worked out), the sky after
            // everything (only where nothing else is: it's drawn at the far plane)
            if (_ground != null) _ground.renderQueue = 2060;
            if (_groundLite != null) _groundLite.renderQueue = 2060;
            _groundNear = new[] { _ground, _groundLite };
            _groundAway = new[] { _groundLite, _groundLite };
            _sky = Copy(_art.sky, "Valley sky");
            if (_sky != null) _sky.renderQueue = 2450;
            _shadow = Copy(_art.shadow, "Valley shadows");
            _stars = Copy(_art.glow, "Valley stars");
            if (_stars != null) _stars.SetFloat("_Curve", 0f);

            for (int v = 0; v < 4; v++) _groundMeshes.Add(GroundMesh(v));
            int count = Mathf.CeilToInt((Behind + Ahead) / TileLength) + 1;
            for (int i = 0; i < count; i++)
            {
                var root = new GameObject("Floor").transform;
                root.SetParent(transform, false);
                root.position = new Vector3(0f, 0f, -Behind + TileLength * (i + 0.5f));
                var filter = root.gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = _groundMeshes[Random.Range(0, _groundMeshes.Count)];
                var renderer = root.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = _groundAway;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var tile = new Tile { Root = root, Filter = filter, Renderer = renderer };
                Scatter(tile);
                _tiles.Add(tile);
            }
            _cliff = KindOf("Cliff");
            for (int i = 0; i < Mathf.CeilToInt((Behind + Ahead + 60f) / WallLength); i++)
                foreach (float side in new[] { -1f, 1f })
                    _walls.Add(new Wall { Z = -Behind + WallLength * (i + 0.5f), Side = side });
            BuildBackdrop();
            _dome = Dome();
            _starMesh = StarField();
            _shadowMesh = new Mesh { name = "Valley shadows" };
            _shadowMesh.MarkDynamic();
            _made.Add(_shadowMesh);
            SetTime(0f, true);
        }

        void OnDestroy()
        {
            foreach (var o in _made)
                if (o != null) Destroy(o);
            Shader.SetGlobalFloat("_TunduCurve", 0f);
            // globals outlive the scene: Tundu/Hero elsewhere (Gara!'s dino) takes its own colours when these are clear
            foreach (var g in new[] { "_ValleySky", "_ValleyShade", "_ValleyBounce", "_ValleyRim" }) Shader.SetGlobalColor(g, Color.clear);
            Shader.SetGlobalFloat("_ValleyNight", 0f);
        }

        // ------------------------------------------------------------------------------------------------ far away

        readonly List<(Kind kind, Matrix4x4 m)> _backdrop = new List<(Kind, Matrix4x4)>();
        readonly List<(Kind kind, Vector3 at, float scale, float yaw, float drift)> _cloudList =
            new List<(Kind, Vector3, float, float, float)>();

        /// <summary>Where the volcano's crater is (for its smoke), bent like the backdrop.</summary>
        public Vector3 Crater { get; private set; }

        void BuildBackdrop()
        {
            var volcano = KindOf("Volcano");
            var mountains = KindOf("Mountains");
            if (volcano.Mesh != null)
            {
                var at = new Vector3(-62f, -8f, 300f);
                _backdrop.Add((volcano, Matrix4x4.TRS(at, Quaternion.Euler(0f, 20f, 0f), Vector3.one)));
                float d = at.z + 6.5f;
                Crater = at + new Vector3(0f, 70f - Curve * 0.25f * d * d, 0f);
            }
            if (mountains.Mesh != null)
            {
                _backdrop.Add((mountains, Matrix4x4.TRS(new Vector3(45f, -14f, 360f), Quaternion.Euler(0f, 180f, 0f), Vector3.one * 1.15f)));
                _backdrop.Add((mountains, Matrix4x4.TRS(new Vector3(-150f, -16f, 340f), Quaternion.Euler(0f, 160f, 0f), Vector3.one)));
                _backdrop.Add((mountains, Matrix4x4.TRS(new Vector3(180f, -18f, 300f), Quaternion.Euler(0f, 200f, 0f), Vector3.one)));
                // behind the runner (the title view looks back): flat there, no bend
                _backdrop.Add((mountains, Matrix4x4.TRS(new Vector3(0f, -6f, -220f), Quaternion.identity, Vector3.one * 1.2f)));
            }
            for (int i = 0; i < 9; i++)
            {
                var kind = KindOf($"Cloud{i % 3 + 1}");
                if (kind.Mesh == null) continue;
                var at = new Vector3(Random.Range(-140f, 140f), Random.Range(32f, 62f), Random.Range(110f, 260f));
                _cloudList.Add((kind, at, Random.Range(2.2f, 4.2f), Random.Range(-30f, 30f), Random.Range(0.6f, 1.4f)));
            }
            for (int i = 0; i < 3; i++)
            {
                var kind = KindOf($"Cloud{i % 3 + 1}");
                if (kind.Mesh == null) continue;
                var at = new Vector3(Random.Range(-90f, 90f), Random.Range(28f, 45f), Random.Range(-160f, -90f));
                _cloudList.Add((kind, at, Random.Range(2.4f, 3.6f), Random.Range(-30f, 30f), Random.Range(0.6f, 1.4f)));
            }
        }

        static Mesh Dome()
        {
            const int seg = 32, rings = 14;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                // from a little under the horizon to the top
                float el = Mathf.Lerp(-0.25f, Mathf.PI / 2f, r / (float)rings);
                for (int s = 0; s <= seg; s++)
                {
                    float az = s / (float)seg * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + 1, c = a + seg + 1, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            var mesh = new Mesh { name = "Valley sky" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            return mesh;
        }

        /// <summary>The night's stars: small quads on the sky (one mesh, added light, faded in with the night).</summary>
        static Mesh StarField()
        {
            var rng = new System.Random(20261118);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();
            for (int i = 0; i < 260; i++)
            {
                float az = (float)rng.NextDouble() * Mathf.PI * 2f;
                float el = Mathf.Asin(0.08f + 0.92f * (float)rng.NextDouble());
                var d = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
                var c = d * 380f;
                var right = Vector3.Cross(d, Vector3.up).normalized;
                if (right.sqrMagnitude < 0.01f) right = Vector3.right;
                var up = Vector3.Cross(right, d);
                float s = 0.8f + 2.2f * (float)Math.Pow(rng.NextDouble(), 3.0);
                int n = verts.Count;
                verts.Add(c + (-right - up) * s); uvs.Add(new Vector2(0, 0));
                verts.Add(c + (right - up) * s); uvs.Add(new Vector2(1, 0));
                verts.Add(c + (right + up) * s); uvs.Add(new Vector2(1, 1));
                verts.Add(c + (-right + up) * s); uvs.Add(new Vector2(0, 1));
                var col = Color.Lerp(new Color(0.75f, 0.85f, 1f), new Color(1f, 0.92f, 0.75f), (float)rng.NextDouble());
                col.a = 0.5f + 0.5f * (float)rng.NextDouble();
                for (int k = 0; k < 4; k++) cols.Add(col);
                tris.Add(n); tris.Add(n + 1); tris.Add(n + 2);
                tris.Add(n); tris.Add(n + 2); tris.Add(n + 3);
            }
            var mesh = new Mesh { name = "Valley stars" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            return mesh;
        }

        // ------------------------------------------------------------------------------------------------ the time of day

        float _time;
        Look _look;

        /// <summary>The current time of day's index (0 morning .. 5 dawn) and how far into the blend to the next (0..1).</summary>
        public int TimeIndex { get; private set; }
        public float TimeBlendAmount { get; private set; }
        /// <summary>0 by day .. 1 at night (fireflies, glowing eggs).</summary>
        public float Night => _look.Night;
        /// <summary>The light's colour now (the game's fx tint by it).</summary>
        public Color SunColour => _look.Sun;

        /// <summary>Sets the time of day by seconds since the morning (the run's time; the title holds a moment).
        /// <paramref name="now"/>: no easing (a new run).</summary>
        public void SetTime(float seconds, bool now = false)
        {
            _time = seconds;
            float cycle = TimeHold + TimeBlend;
            float t = Mathf.Repeat(seconds, cycle * Looks.Length);
            int i = Mathf.FloorToInt(t / cycle);
            float into = t - i * cycle;
            float k = into < TimeHold ? 0f : Mathf.SmoothStep(0f, 1f, (into - TimeHold) / TimeBlend);
            TimeIndex = i;
            TimeBlendAmount = k;
            _look = Blend(Looks[i], Looks[(i + 1) % Looks.Length], k);
            Apply();
        }

        static Look Blend(Look a, Look b, float k)
        {
            if (k <= 0f) return a;
            return new Look
            {
                Sun = Color.Lerp(a.Sun, b.Sun, k), SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, k),
                Shade = Color.Lerp(a.Shade, b.Shade, k), Sky = Color.Lerp(a.Sky, b.Sky, k), Bounce = Color.Lerp(a.Bounce, b.Bounce, k),
                Rim = Color.Lerp(a.Rim, b.Rim, k), RimStrength = Mathf.Lerp(a.RimStrength, b.RimStrength, k),
                RimShade = Color.Lerp(a.RimShade, b.RimShade, k), Haze = Color.Lerp(a.Haze, b.Haze, k),
                HazeFar = Color.Lerp(a.HazeFar, b.HazeFar, k), SkyTop = Color.Lerp(a.SkyTop, b.SkyTop, k),
                SkyHorizon = Color.Lerp(a.SkyHorizon, b.SkyHorizon, k), SkyGlow = Color.Lerp(a.SkyGlow, b.SkyGlow, k),
                GlowSize = Mathf.Exp(Mathf.Lerp(Mathf.Log(a.GlowSize), Mathf.Log(b.GlowSize), k)),
                Night = Mathf.Lerp(a.Night, b.Night, k), SunFrom = Vector3.Slerp(a.SunFrom, b.SunFrom, k).normalized,
                Fog = Vector4.Lerp(a.Fog, b.Fog, k),
            };
        }

        void Apply()
        {
            var l = _look;
            Shader.SetGlobalFloat("_TunduCurve", Curve);
            Shader.SetGlobalColor("_ValleyShade", l.Shade);
            Shader.SetGlobalColor("_ValleySky", l.Sky);
            Shader.SetGlobalColor("_ValleyBounce", l.Bounce);
            var rim = l.Rim;
            rim.a = l.RimStrength;
            Shader.SetGlobalColor("_ValleyRim", rim);
            Shader.SetGlobalColor("_ValleyRimShade", l.RimShade);
            Shader.SetGlobalColor("_ValleyHaze", l.Haze);
            Shader.SetGlobalColor("_ValleyHazeFar", l.HazeFar);
            Shader.SetGlobalVector("_ValleyFog", l.Fog);
            Shader.SetGlobalVector("_ValleyWind", new Vector4(0.75f, 0.55f, 0.07f, 1.2f));
            Shader.SetGlobalFloat("_ValleyNight", l.Night);
            Shader.SetGlobalColor("_ValleySkyTop", l.SkyTop);
            Shader.SetGlobalColor("_ValleySkyHorizon", l.SkyHorizon);
            var glow = l.SkyGlow;
            glow.a = l.GlowSize;
            // the glow's colour is a strength: SetGlobalColor would take its alpha (a size) as a colour's
            Shader.SetGlobalVector("_ValleySkyGlow", new Vector4(glow.linear.r, glow.linear.g, glow.linear.b, l.GlowSize));
            Shader.SetGlobalVector("_ValleySun", l.SunFrom);
            if (_sun != null)
            {
                _sun.transform.rotation = Quaternion.LookRotation(-l.SunFrom, Vector3.up);
                _sun.color = l.Sun;
                _sun.intensity = l.SunIntensity;
            }
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = l.Sky;
            RenderSettings.ambientEquatorColor = l.Haze;
            RenderSettings.ambientGroundColor = l.Bounce;
            if (_stars != null) _stars.SetColor("_BaseColor", new Color(1f, 1f, 1f, Mathf.Clamp01(l.Night * 1.2f - 0.1f)));
        }

        // ------------------------------------------------------------------------------------------------ every frame

        /// <summary>Moves the floor, the walls and their scenery <paramref name="move"/> metres toward the camera.</summary>
        public void Move(float move)
        {
            if (move == 0f) return;
            float span = _tiles.Count * TileLength;
            foreach (var t in _tiles)
            {
                var p = t.Root.position;
                p.z -= move;
                if (p.z + TileLength / 2f < -Behind)
                {
                    p.z += span;
                    t.Filter.sharedMesh = _groundMeshes[Random.Range(0, _groundMeshes.Count)];
                    Scatter(t);
                }
                t.Root.position = p;
            }
            float wallSpan = _walls.Count / 2 * WallLength;
            foreach (var w in _walls)
            {
                w.Z -= move;
                if (w.Z + WallLength / 2f < -Behind) w.Z += wallSpan;
            }
        }

        readonly Plane[] _planes = new Plane[6];
        readonly int[] _order = new int[64];
        readonly float[] _orderKey = new float[64];
        /// <summary>From here on things are drawn in their lighter versions (m from the camera): the trees (art/runner's
        /// hand-made ...Far versions), the plants beside the path and at its edge (built again at half the detail).</summary>
        public const float LodDistance = 42f, LodDistanceEdge = 30f, LodDistanceVerge = 24f;
        bool _groundShown = true;

        /// <summary>Draws everything for this frame (call once, after the world has moved): the sky, the stars, the
        /// backdrop, the clouds, the walls, the scenery, the shadows.</summary>
        public void Draw(Camera cam, float dt)
        {
            if (cam == null) return;
            var eye = cam.transform.position;
            if (_sky != null && PerfSwitches.Get("sky") != "0")
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = _look.SkyHorizon;
                var rp = new RenderParams(_sky) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMesh(rp, _dome, 0, Matrix4x4.TRS(eye, Quaternion.identity, Vector3.one * 400f));
                if (_stars != null && _look.Night > 0.08f)
                    Graphics.RenderMesh(new RenderParams(_stars) { shadowCastingMode = ShadowCastingMode.Off }, _starMesh, 0,
                        Matrix4x4.Translate(eye));
            }
            foreach (var k in _kindList) k.Batch.Clear();
            bool far = PerfSwitches.Get("far") != "0", props = PerfSwitches.Get("props") != "0";
            // PerfLab: each band of scenery on its own (verge=0, edge=0, mid=0), the floor (ground=0)
            bool verge = PerfSwitches.Get("verge") != "0", edgeBand = PerfSwitches.Get("edge") != "0", mid = PerfSwitches.Get("mid") != "0";
            bool ground = PerfSwitches.Get("ground") != "0";
            if (ground != _groundShown)
            {
                _groundShown = ground;
                foreach (var t in _tiles) t.Renderer.enabled = ground;
            }
            // the detailed floor only where its detail shows (PerfLab detail=0: all lit at the vertices, detail=1: all detailed)
            string detail = PerfSwitches.Get("detail");
            foreach (var t in _tiles)
            {
                bool near = detail == "1" || detail != "0" && Mathf.Abs(t.Root.position.z - eye.z) - TileLength / 2f < DetailFar;
                if (near == t.Near) continue;
                t.Near = near;
                t.Renderer.sharedMaterials = near ? _groundNear : _groundAway;
            }
            if (far)
            {
                foreach (var (kind, m) in _backdrop) kind.Batch.Add(m);
                for (int i = 0; i < _cloudList.Count; i++)
                {
                    var (kind, at, scale, yaw, drift) = _cloudList[i];
                    at.x += dt * drift;
                    if (at.x > 160f) at.x = -160f;
                    _cloudList[i] = (kind, at, scale, yaw, drift);
                    kind.Batch.Add(Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale));
                }
                if (_cliff?.Mesh != null)
                    foreach (var w in _walls)
                    {
                        if (w.Z > Ahead + 30f) continue;
                        var at = new Vector3(w.Side * WallX, FloorHeight(WallX) - 1.2f, w.Z);
                        // the cliff's face looks toward -x as built: the right wall as it is, the left one turned round
                        _cliff.Batch.Add(Matrix4x4.TRS(at, Quaternion.Euler(0f, w.Side < 0 ? 180f : 0f, 0f), Vector3.one));
                    }
            }
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            string lod = PerfSwitches.Get("lod");
            // the tiles nearest the camera first: each kind's instances are drawn front to back (what's hidden behind the
            // near plants is rejected before its pixels are worked out)
            for (int i = 0; i < _tiles.Count; i++)
            {
                _order[i] = i;
                _orderKey[i] = Mathf.Abs(_tiles[i].Root.position.z - eye.z);
            }
            Array.Sort(_orderKey, _order, 0, _tiles.Count);
            if (props)
                for (int ti = 0; ti < _tiles.Count; ti++)
                {
                    var t = _tiles[_order[ti]];
                    var origin = t.Root.position;
                    // any way the camera looks (the title and the crash look back at Dino): near enough, then in view
                    if (Mathf.Abs(origin.z - eye.z) > 140f) continue;
                    foreach (var (kind, local, at) in t.Things)
                    {
                        float z = origin.z + at.z;
                        if (Mathf.Abs(z - eye.z) > kind.Reach) continue;
                        if (!far && kind.Reach > 1e3f) continue;
                        if (!verge && kind.Band == 0 || !edgeBand && kind.Band == 1 || !mid && kind.Band == 2) continue;
                        var world = Matrix4x4.Translate(origin) * local;
                        // roughly in view (the bend only pushes things down: the flat frustum keeps everything it shows)
                        var c = new Vector3(origin.x + at.x, at.y + kind.Radius * 0.5f, z);
                        if (!InView(c, kind.Radius * 1.4f)) continue;
                        // the lighter versions further on (the box's GPU runs the whole light for every vertex); PerfLab
                        // lod=0: none, lod=trees: only the trees'
                        var draw = kind;
                        if (kind.FarKind != null && lod != "0" && (lod != "trees" || kind.Band == 2))
                        {
                            float from = kind.Band == 0 ? LodDistanceVerge : kind.Band == 1 ? LodDistanceEdge : LodDistance;
                            if (Mathf.Abs(z - eye.z) > from) draw = kind.FarKind;
                        }
                        draw.Batch.Add(world);
                    }
                }
            foreach (var k in _kindList)
            {
                if (k.Batch.Count == 0 || k.Mesh == null || k.Material == null) continue;
                var rp = new RenderParams(k.Material)
                {
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off,
                };
                Graphics.RenderMeshInstanced(rp, k.Mesh, 0, k.Batch);
            }
            // the scenery's own soft shadows, near only (they're tiny further on)
            if (props && PerfSwitches.Get("shadows") != "0")
                foreach (var t in _tiles)
                {
                    var origin = t.Root.position;
                    if (Mathf.Abs(origin.z - eye.z) > 55f) continue;
                    foreach (var (kind, local, at) in t.Things)
                        if (kind.Shadow > 0f)
                            Shadow(origin + at, kind.Shadow * local.lossyScale.x, 0.32f);
                }
            DrawShadows();
        }

        bool InView(Vector3 c, float r)
        {
            for (int i = 0; i < 6; i++)
                if (_planes[i].GetDistanceToPoint(c) < -r) return false;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ shadows

        readonly List<Vector3> _sv = new List<Vector3>(512);
        readonly List<Vector2> _su = new List<Vector2>(512);
        readonly List<Color32> _sc = new List<Color32>(512);
        readonly List<int> _st = new List<int>(768);

        /// <summary>A soft round shadow on the ground under something (radius m, darkness 0..1), drawn this frame.</summary>
        public void Shadow(Vector3 at, float radius, float dark, float stretch = 1f)
        {
            if (radius <= 0.01f || dark <= 0.005f) return;
            float y = FloorHeight(at.x) + 0.03f;
            float rx = radius, rz = radius * stretch;
            int n = _sv.Count;
            _sv.Add(new Vector3(at.x - rx, y, at.z - rz));
            _sv.Add(new Vector3(at.x + rx, y, at.z - rz));
            _sv.Add(new Vector3(at.x + rx, y, at.z + rz));
            _sv.Add(new Vector3(at.x - rx, y, at.z + rz));
            _su.Add(new Vector2(0, 0)); _su.Add(new Vector2(1, 0)); _su.Add(new Vector2(1, 1)); _su.Add(new Vector2(0, 1));
            var c = new Color32(255, 255, 255, (byte)Mathf.Clamp(dark * 255f, 0f, 255f));
            _sc.Add(c); _sc.Add(c); _sc.Add(c); _sc.Add(c);
            _st.Add(n); _st.Add(n + 2); _st.Add(n + 1);
            _st.Add(n); _st.Add(n + 3); _st.Add(n + 2);
        }

        void DrawShadows()
        {
            if (_shadowMesh == null || _shadow == null) return;
            _shadowMesh.Clear();
            if (_sv.Count > 0)
            {
                _shadowMesh.SetVertices(_sv);
                _shadowMesh.SetUVs(0, _su);
                _shadowMesh.SetColors(_sc);
                _shadowMesh.SetTriangles(_st, 0, false);
                _shadowMesh.bounds = new Bounds(Vector3.zero, new Vector3(1e4f, 1e4f, 1e4f));
                Graphics.RenderMesh(new RenderParams(_shadow) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false },
                    _shadowMesh, 0, Matrix4x4.identity);
            }
            _sv.Clear();
            _su.Clear();
            _sc.Clear();
            _st.Clear();
        }
    }
}
