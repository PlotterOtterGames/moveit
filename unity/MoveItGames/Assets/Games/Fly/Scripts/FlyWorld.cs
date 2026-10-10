using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Fly
{
    /// <summary>
    /// The city the hero flies over: a long street of colourful toy buildings on both sides, parks with round trees,
    /// and clouds at the hero's height drifting past. All built from simple shapes at start and recycled as they fall
    /// behind, so the world seems endless. The ground and buildings use the curved-world toon material (the horizon bends
    /// away); clouds don't bend. Each stretch of street and each cloud is baked into one mesh per colour (one material per
    /// colour, so the SRP batcher draws them cheaply on the box). <see cref="Speed"/> is set by the game (0 = paused).
    /// </summary>
    public sealed class FlyWorld : MonoBehaviour
    {
        [SerializeField, Tooltip("Tundu/Toon with _Curve 1 (bends with the world): ground, buildings, trees.")]
        Material cityMaterial;
        [SerializeField, Tooltip("Tundu/Toon with _Curve 0: clouds.")] Material cloudMaterial;
        [SerializeField, Tooltip("Metres of street before it repeats.")] float length = 180f;
        [SerializeField] int clouds = 14;

        public float Speed { get; set; }
        /// <summary>How high the hero flies above the street (the game uses the same value).</summary>
        public const float FlyHeight = 9f;

        static readonly Color[] Walls =
        {
            new Color(1f, 0.79f, 0.24f), new Color(1f, 0.55f, 0.5f), new Color(0.48f, 0.74f, 1f), new Color(0.6f, 0.52f, 1f),
            new Color(0.55f, 0.86f, 0.5f), new Color(0.98f, 0.92f, 0.8f), new Color(1f, 0.68f, 0.35f),
        };
        static readonly Color Roof = new Color(0.32f, 0.3f, 0.46f), Window = new Color(0.86f, 0.95f, 1f);
        static readonly Color Road = new Color(0.42f, 0.44f, 0.52f), Grass = new Color(0.52f, 0.82f, 0.42f);
        static readonly Color Line = new Color(1f, 0.95f, 0.75f), Trunk = new Color(0.62f, 0.42f, 0.26f), Leaves = new Color(0.36f, 0.72f, 0.36f);

        readonly List<Transform> _blocks = new List<Transform>();
        readonly List<Transform> _clouds = new List<Transform>();
        readonly Dictionary<(Material, Color), Material> _materials = new Dictionary<(Material, Color), Material>();
        readonly List<Mesh> _meshes = new List<Mesh>();

        void Start()
        {
            var rng = new System.Random(5);
            const float blockLength = 15f;
            for (float z = -20f; z < length - 20f; z += blockLength) _blocks.Add(BuildBlock(z, blockLength, rng));
            for (int i = 0; i < clouds; i++)
            {
                var c = BuildCloud(rng);
                c.position = new Vector3(Range(rng, -16f, 16f), FlyHeight + Range(rng, -5f, 6f), Range(rng, 10f, length - 20f));
                _clouds.Add(c);
            }
        }

        void Update()
        {
            float step = Speed * Time.deltaTime;
            if (step <= 0f) return;
            foreach (var b in _blocks)
            {
                b.position += Vector3.back * step;
                if (b.position.z < -35f) b.position += Vector3.forward * (_blocks.Count * 15f);
            }
            foreach (var c in _clouds)
            {
                // clouds pass a little slower than the ground (they drift with the wind)
                c.position += Vector3.back * (step * 0.8f);
                if (c.position.z < -12f)
                {
                    c.position = new Vector3(Random.Range(-16f, 16f), FlyHeight + Random.Range(-5f, 6f), length - 20f + Random.Range(0f, 20f));
                    // never right in the hero's face: clouds in the middle stay above or below
                    if (Mathf.Abs(c.position.x) < 4f && Mathf.Abs(c.position.y - FlyHeight) < 2.5f) c.position += Vector3.up * 5f;
                }
            }
        }

        void OnDestroy()
        {
            foreach (var m in _materials.Values) Destroy(m);
            foreach (var m in _meshes) Destroy(m);
        }

        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        Material Paint(Material mat, Color color)
        {
            if (_materials.TryGetValue((mat, color), out var m)) return m;
            m = new Material(mat) { name = $"{mat.name} {ColorUtility.ToHtmlStringRGB(color)}" };
            m.SetColor("_BaseColor", color);
            _materials[(mat, color)] = m;
            return m;
        }

        void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Paint(mat, color);
        }

        /// <summary>Merges all the shapes under <paramref name="root"/> into one mesh per material on the root itself.</summary>
        void Bake(Transform root)
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var toWorldInverse = root.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mat = mf.GetComponent<Renderer>().sharedMaterial;
                if (!groups.TryGetValue(mat, out var list)) groups[mat] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toWorldInverse * mf.transform.localToWorldMatrix });
            }
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            foreach (var group in groups)
            {
                var mesh = new Mesh { name = root.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Value.ToArray(), true, true);
                _meshes.Add(mesh);
                var part = new GameObject(group.Key.name).transform;
                part.SetParent(root, false);
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = part.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = group.Key;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        /// <summary>One stretch of street: road with a centre line, pavements, buildings or a park on each side.</summary>
        Transform BuildBlock(float z, float blockLength, System.Random rng)
        {
            var block = new GameObject("Block").transform;
            block.SetParent(transform, false);
            block.localPosition = new Vector3(0f, 0f, z);
            Part(block, PrimitiveType.Cube, new Vector3(0f, -0.05f, 0f), new Vector3(12f, 0.1f, blockLength), cityMaterial, Road);
            Part(block, PrimitiveType.Cube, new Vector3(0f, 0.01f, 0f), new Vector3(0.25f, 0.02f, blockLength * 0.45f), cityMaterial, Line);
            foreach (float side in new[] { -1f, 1f })
            {
                Part(block, PrimitiveType.Cube, new Vector3(side * 30f, -0.05f, 0f), new Vector3(48f, 0.1f, blockLength), cityMaterial, Grass);
                bool park = rng.NextDouble() < 0.22;
                if (park)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var at = new Vector3(side * Range(rng, 8f, 20f), 0f, Range(rng, -6f, 6f));
                        float s = Range(rng, 1.4f, 2.2f);
                        Part(block, PrimitiveType.Cylinder, at + Vector3.up * s * 0.6f, new Vector3(0.35f, s * 0.6f, 0.35f), cityMaterial, Trunk);
                        Part(block, PrimitiveType.Sphere, at + Vector3.up * s * 1.6f, Vector3.one * s * 1.5f, cityMaterial, Leaves);
                    }
                    continue;
                }
                // two or three buildings along this side, the nearest row facing the street
                float zz = -blockLength * 0.5f + 1f;
                while (zz < blockLength * 0.5f - 3f)
                {
                    float w = Range(rng, 4f, 7f), d = Range(rng, 6f, 10f), h = Range(rng, 5f, 16f);
                    if (zz + w > blockLength * 0.5f) break;
                    float x = side * (7.5f + d * 0.5f + Range(rng, 0f, 1.5f));
                    var wall = Walls[rng.Next(Walls.Length)];
                    var house = new GameObject("Building").transform;
                    house.SetParent(block, false);
                    house.localPosition = new Vector3(x, 0f, zz + w * 0.5f);
                    Part(house, PrimitiveType.Cube, new Vector3(0f, h * 0.5f, 0f), new Vector3(d, h, w), cityMaterial, wall);
                    Part(house, PrimitiveType.Cube, new Vector3(0f, h + 0.2f, 0f), new Vector3(d + 0.4f, 0.4f, w + 0.4f), cityMaterial, Roof);
                    // rows of windows on the street side
                    for (float wy = 2f; wy < h - 1f; wy += 2.6f)
                        Part(house, PrimitiveType.Cube, new Vector3(-side * (d * 0.5f + 0.02f), wy, 0f), new Vector3(0.05f, 1.1f, w * 0.75f), cityMaterial, Window);
                    if (rng.NextDouble() < 0.3)   // a rooftop water tank or antenna
                        Part(house, PrimitiveType.Cylinder, new Vector3(0f, h + 1.2f, 0f), new Vector3(1.2f, 0.8f, 1.2f), cityMaterial, Roof);
                    zz += w + Range(rng, 0.6f, 1.8f);
                }
            }
            Bake(block);
            return block;
        }

        Transform BuildCloud(System.Random rng)
        {
            var cloud = new GameObject("Cloud").transform;
            cloud.SetParent(transform, false);
            int puffs = 3 + rng.Next(3);
            for (int i = 0; i < puffs; i++)
            {
                float s = Range(rng, 1.6f, 3.2f);
                Part(cloud, PrimitiveType.Sphere, new Vector3((i - puffs * 0.5f) * 1.5f, Range(rng, -0.3f, 0.5f), Range(rng, -0.6f, 0.6f)),
                     new Vector3(s * 1.3f, s * 0.8f, s), cloudMaterial, Color.white);
            }
            Bake(cloud);
            return cloud;
        }
    }
}
