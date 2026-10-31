using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Fly
{
    /// <summary>
    /// The city the hero flies through: the street grid of <see cref="Route"/>, drawn block by block around the hero
    /// (<see cref="CityBuilder"/>: one mesh and one material per 80 m block, built when it comes into range and recycled
    /// when it falls far behind), and clouds high above. The hero never moves: the city is moved and turned so the
    /// hero's track sits at the origin heading +z, which keeps every game rule (<see cref="FlyRun"/>) in the flight frame
    /// and lets the curved-world bend and the light stay fixed to the view. <see cref="FlyGame"/> advances the route.
    /// </summary>
    public sealed class FlyWorld : MonoBehaviour
    {
        [SerializeField, Tooltip("Tundu/City with the city texture array: the whole city.")] Material cityMaterial;
        [SerializeField, Tooltip("Tundu/Toon, white, flat (no world bend): clouds.")] Material cloudMaterial;
        [SerializeField, Range(1, 4), Tooltip("Blocks kept around the hero each way (2 = 5 x 5).")] int radius = 2;
        [SerializeField, Range(0, 32)] int clouds = 14;
        [SerializeField, Tooltip("Blocks built per frame once playing (the first ones are built at once).")] int buildsPerFrame = 1;

        /// <summary>How high the hero flies above the street (the game uses the same value).</summary>
        public const float FlyHeight = 9f;

        /// <summary>The streets the hero flies; seeded, so the city is the same every time.</summary>
        public FlyRoute Route { get; } = new FlyRoute(seed: 7);
        /// <summary>Blocks in the scene now, and their vertices (for the performance log).</summary>
        public int Blocks => _cells.Count;
        public int Vertices { get; private set; }
        /// <summary>The slowest block build since <see cref="ResetStats"/> (ms): blocks are built while flying.</summary>
        public float WorstBuildMs { get; private set; }
        public void ResetStats() => WorstBuildMs = 0f;
        readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();

        sealed class Cell
        {
            public int I, J, Vertices;
            public GameObject Go;
            public Mesh Mesh;
        }

        readonly Dictionary<long, Cell> _cells = new Dictionary<long, Cell>();
        readonly Stack<Cell> _free = new Stack<Cell>();
        readonly List<long> _drop = new List<long>();
        readonly CityMesh _mesh = new CityMesh();
        CityBuilder _builder;
        Transform _city;
        Transform[] _clouds = new Transform[0];
        readonly List<Mesh> _cloudMeshes = new List<Mesh>();
        bool _started;

        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        /// <summary>The city's material (a copy of the scene's: the time of day tints it while playing, see
        /// <see cref="FlyTheme"/>).</summary>
        public Material CityMaterial => cityMaterial;

        void Awake()
        {
            if (cityMaterial != null) cityMaterial = new Material(cityMaterial);
            _builder = new CityBuilder(Route);
            _city = new GameObject("City").transform;
            _city.SetParent(transform, false);
        }

        void Start()
        {
            BuildClouds();
            Place();
            EnsureBlocks(int.MaxValue);
            _started = true;
        }

        void LateUpdate()
        {
            Place();
            EnsureBlocks(_started ? buildsPerFrame : int.MaxValue);
            DriftClouds(Time.deltaTime);
        }

        void OnDestroy()
        {
            foreach (var c in _cells.Values) Destroy(c.Mesh);
            foreach (var c in _free) Destroy(c.Mesh);
            foreach (var m in _cloudMeshes) Destroy(m);
            if (cityMaterial != null) Destroy(cityMaterial);
        }

        /// <summary>The city turned and moved so the route's track is at the origin, heading +z.</summary>
        void Place()
        {
            var p = Route.Position;
            var turn = Quaternion.Euler(0f, -Route.Heading, 0f);
            _city.SetPositionAndRotation(turn * new Vector3(-p.x, 0f, -p.y), turn);
        }

        // ---------------------------------------------------------------- blocks

        void EnsureBlocks(int maxBuilds)
        {
            var p = Route.Position;
            int ci = Mathf.FloorToInt(p.x / Route.Pitch), cj = Mathf.FloorToInt(p.y / Route.Pitch);
            _drop.Clear();
            foreach (var pair in _cells)
                if (Mathf.Abs(pair.Value.I - ci) > radius + 1 || Mathf.Abs(pair.Value.J - cj) > radius + 1) _drop.Add(pair.Key);
            foreach (long key in _drop)
            {
                var c = _cells[key];
                c.Go.SetActive(false);
                Vertices -= c.Vertices;
                _free.Push(c);
                _cells.Remove(key);
            }
            // nearest first: ring by ring outward (the far ring is deep in the fog when it appears)
            int built = 0;
            for (int ring = 0; ring <= radius; ring++)
            for (int di = -ring; di <= ring; di++)
            for (int dj = -ring; dj <= ring; dj++)
            {
                if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != ring || _cells.ContainsKey(Key(ci + di, cj + dj))) continue;
                if (built++ >= maxBuilds) return;
                BuildBlock(ci + di, cj + dj);
            }
        }

        void BuildBlock(int i, int j)
        {
            _watch.Restart();
            var c = _free.Count > 0 ? _free.Pop() : NewCell();
            _builder.Build(_mesh, i, j);
            var mesh = c.Mesh;
            mesh.Clear();
            mesh.indexFormat = _mesh.VertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_mesh.Positions);
            mesh.SetNormals(_mesh.Normals);
            mesh.SetColors(_mesh.Colors);
            mesh.SetUVs(0, _mesh.Uvs);
            mesh.SetUVs(1, _mesh.Tiles);
            mesh.SetTriangles(_mesh.Indices, 0, true);
            c.I = i;
            c.J = j;
            c.Vertices = _mesh.VertexCount;
            Vertices += c.Vertices;
            c.Go.name = $"Block {i},{j}";
            c.Go.transform.localPosition = new Vector3(i * Route.Pitch, 0f, j * Route.Pitch);
            c.Go.SetActive(true);
            _cells[Key(i, j)] = c;
            if (_started) WorstBuildMs = Mathf.Max(WorstBuildMs, (float)_watch.Elapsed.TotalMilliseconds);
        }

        Cell NewCell()
        {
            var go = new GameObject("Block");
            go.transform.SetParent(_city, false);
            var mesh = new Mesh { name = "City block" };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = cityMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return new Cell { Go = go, Mesh = mesh };
        }

        // ---------------------------------------------------------------- clouds

        /// <summary>Puffy clouds high over the roofs (never at the hero's height: one there would fill the screen),
        /// drifting with the wind, moved ahead of the hero once far behind.</summary>
        void BuildClouds()
        {
            var rng = new System.Random(5);
            for (int k = 0; k < 4; k++) _cloudMeshes.Add(CloudMesh(rng));
            _clouds = new Transform[clouds];
            for (int k = 0; k < clouds; k++)
            {
                var go = new GameObject("Cloud");
                go.transform.SetParent(_city, false);
                go.AddComponent<MeshFilter>().sharedMesh = _cloudMeshes[k % _cloudMeshes.Count];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = cloudMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.transform.localScale = Vector3.one * Range(rng, 0.8f, 1.5f);
                go.transform.localRotation = Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);
                var p = Route.Position;
                go.transform.localPosition = new Vector3(p.x + Range(rng, -200f, 200f), CloudHeight(rng), p.y + Range(rng, -200f, 200f));
                _clouds[k] = go.transform;
            }
        }

        static float CloudHeight(System.Random rng) => FlyHeight + Range(rng, 15f, 26f);

        void DriftClouds(float dt)
        {
            var p = Route.Position;
            var ahead = new Vector3(Mathf.Sin(Route.Heading * Mathf.Deg2Rad), 0f, Mathf.Cos(Route.Heading * Mathf.Deg2Rad));
            var side = new Vector3(ahead.z, 0f, -ahead.x);
            foreach (var c in _clouds)
            {
                var local = c.localPosition + new Vector3(1.4f * dt, 0f, 0.5f * dt);   // the wind
                var flat = new Vector2(local.x - p.x, local.z - p.y);
                if (flat.sqrMagnitude > 230f * 230f)
                {
                    float fwd = Random.Range(150f, 220f), across = Random.Range(-150f, 150f);
                    local = new Vector3(p.x, FlyHeight + Random.Range(15f, 26f), p.y) + ahead * fwd + side * across;
                }
                c.localPosition = local;
            }
        }

        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>A cloud: a few smooth puffs with flat bottoms, merged into one mesh.</summary>
        static Mesh CloudMesh(System.Random rng)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            Sphere(out var sv, out var st);
            int puffs = 4 + rng.Next(3);
            for (int k = 0; k < puffs; k++)
            {
                float s = Range(rng, 3.2f, 6f);
                var centre = new Vector3((k - (puffs - 1) * 0.5f) * 3.6f + Range(rng, -0.8f, 0.8f), Range(rng, -0.4f, 1.2f), Range(rng, -1.5f, 1.5f));
                var scale = new Vector3(s * 1.25f, s * 0.8f, s);
                int start = verts.Count;
                foreach (var v in sv)
                {
                    var p = Vector3.Scale(v, scale);
                    p.y = Mathf.Max(p.y, -scale.y * 0.18f);   // flat bottoms, like the classic cartoon cloud
                    verts.Add(centre + p);
                    normals.Add(v.y < -0.36f ? Vector3.down : Vector3.Scale(v, new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z)).normalized);
                }
                foreach (int t in st) tris.Add(start + t);
            }
            var mesh = new Mesh { name = "Cloud" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0, true);
            return mesh;
        }

        /// <summary>A smooth unit-diameter sphere (an icosahedron subdivided twice): 162 vertices.</summary>
        static void Sphere(out List<Vector3> sphereVertices, out List<int> sphereTriangles)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var vertices = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].normalized;
            var triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int level = 0; level < 2; level++)
            {
                var next = new List<int>(triangles.Count * 4);
                var middle = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (middle.TryGetValue(key, out int m)) return m;
                    vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
                    return middle[key] = vertices.Count - 1;
                }
                for (int k = 0; k < triangles.Count; k += 3)
                {
                    int a = triangles[k], b = triangles[k + 1], c = triangles[k + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                triangles = next;
            }
            for (int i = 0; i < vertices.Count; i++) vertices[i] *= 0.5f;
            sphereVertices = vertices;
            sphereTriangles = triangles;
        }
    }
}
