using System;
using System.Collections.Generic;
using System.Globalization;
using MoveIt.Games.Sports;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace MoveIt.Games.Boxing
{
    /// <summary>The art (art/boxing's Arena.fbx and Boxing.fbx, tools/boxing's sheet) and the materials, set by the scene
    /// builder.</summary>
    [Serializable]
    public sealed class BoxingArt
    {
        [Tooltip("Boxing.fbx: the boxers' gloves, the champions' belt.")] public Mesh glove, belt;
        [Tooltip("Arena.fbx: every stage's pieces and rings (art/boxing/arena.py).")] public Mesh[] kit;
        [Tooltip("arena_seats_<stage>.txt: the crowd's seats (gym, roof).")] public TextAsset[] seats;
        [Tooltip("Tundu/Athlete: the gloves, the belt, coloured by region.")] public Material athlete;
        [Tooltip("Tundu/Arena, its _LITE and _SKY copies, Tundu/ArenaGlow.")] public Material arena, arenaLite, arenaSky, arenaGlow;
        [Tooltip("Tundu/Fx additive with the soft dot: the stars in the sky, flashes, glows.")] public Material glow;
        [Tooltip("Tundu/Crowd: the crowds (Sporte!'s atlas).")] public Material crowd;
        [Tooltip("Tundu/AthleteGhost, depth and colour: the see-through hero.")] public Material ghostDepth, ghostColour;
        [Tooltip("Tundu/Athlete two-sided: capes.")] public Material cape;
        [Tooltip("Tundu/Fx with the HUD sheet (alpha): the comic bursts in the world.")] public Material burst;
        [Tooltip("tools/boxing/art.py's sheet and its rectangles.")] public Texture2D sheet;
        public TextAsset sheetJson;
    }

    /// <summary>
    /// The four places of the climb (art/boxing/arena.py's kit, lit at the vertices by Tundu/Arena): the gym at night, the
    /// roof at sunset, the mountain's summit, the villain's arena among the stars. A stage is its ring and a few big
    /// pieces (near, far, sky, beams), its crowd (Sporte!'s <see cref="Fans"/> on the kit's seats), its light
    /// (<see cref="BoxingLooks.Theme"/>: the shader's globals, the sun, the air), and a few things that move (the roof's
    /// blinking beacon, the stars twinkling, flashes popping in the crowd).
    /// </summary>
    public sealed class BoxingWorld : MonoBehaviour
    {
        public const float RingHeight = 0.62f, Posts = 2.3f, Edge = 2.6f;
        static readonly string[] Prefix = { "Gym", "Roof", "Mountain", "Stars" };

        BoxingArt _art;
        Light _sun;
        Camera _cam;
        Transform _stage;
        Transform _beacon;
        public Fans Crowd { get; private set; }
        public int Stage { get; private set; } = -1;
        /// <summary>Where people sit (for the flashes popping in the crowd).</summary>
        public readonly List<Vector3> Seats = new List<Vector3>();
        Mesh _twinkles;
        Material _crowd;

        public void Init(BoxingArt art, Light sun, Camera cam)
        {
            _art = art;
            _sun = sun;
            _cam = cam;
            // the kit's world is still: no shader global left over from another game bends it
            Shader.SetGlobalFloat("_TunduCurve", 0f);
        }

        /// <summary>Builds stage <paramref name="stage"/> (and lights it), the last one gone.</summary>
        public void Build(int stage)
        {
            stage = Mathf.Clamp(stage, 0, Prefix.Length - 1);
            if (stage == Stage && _stage != null) return;
            Stage = stage;
            if (_stage != null) Destroy(_stage.gameObject);
            _beacon = null;
            Crowd = null;
            Seats.Clear();
            _stage = new GameObject($"Stage {stage}").transform;
            _stage.SetParent(transform, false);
            Light(BoxingLooks.Stages[stage]);
            string prefix = Prefix[stage];
            int vertices = 0;
            if (_art.kit != null)
                foreach (var mesh in _art.kit)
                {
                    if (mesh == null) continue;
                    string n = mesh.name;
                    if (!(n.StartsWith(prefix + "_", StringComparison.Ordinal) || n == "Ring_" + prefix)) continue;
                    var material = n.EndsWith("_Sky", StringComparison.Ordinal) ? _art.arenaSky
                        : n.EndsWith("_Far", StringComparison.Ordinal) ? _art.arenaLite
                        : n.EndsWith("_Beams", StringComparison.Ordinal) ? _art.arenaGlow
                        : _art.arena;
                    var r = Show(n, _stage, mesh, material);
                    // the dome follows the camera in its shader: never culled away
                    if (n.EndsWith("_Sky", StringComparison.Ordinal)) r.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
                    if (n == "Roof_Beacon") _beacon = r.transform;
                    vertices += mesh.vertexCount;
                }
            // the crowd on the kit's seats
            var seats = SeatsOf(stage);
            if (seats != null && _art.crowd != null)
            {
                // the crowd in the stage's light (Tundu/Crowd draws them in its own tint: full daylight by default)
                if (_crowd == null) _crowd = new Material(_art.crowd) { name = "Crowd (stage)" };
                _crowd.SetColor("_Tint", BoxingLooks.Stages[stage].CrowdTint);
                _crowd.SetColor("_ShadeColor", BoxingLooks.Stages[stage].CrowdTint * 0.72f);
                Crowd = Fans.Build(_stage, seats.text, _crowd, 7 + stage, BoxingLooks.Stages[stage].Crowd);
                foreach (var s in Venue.ParseSeats(seats.text)) Seats.Add(new Vector3(s.x, s.y + 0.9f, s.z));
            }
            // the stars: points of light twinkling round the arena
            if (stage == 3) Twinkles(new Random(5), 420, 160f);
            Debug.Log($"[Boxing] Stage {stage} ({prefix}): {vertices} vertices, {(Crowd != null ? Crowd.People : 0)} people");
        }

        TextAsset SeatsOf(int stage)
        {
            if (_art.seats == null) return null;
            string want = $"arena_seats_{Prefix[stage].ToLowerInvariant()}";
            foreach (var t in _art.seats) if (t != null && t.name == want) return t;
            return null;
        }

        static MeshRenderer Show(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>The stage's light: Tundu/Arena's globals, the sun, the ambient and fog the boxers take, the camera's
        /// background.</summary>
        void Light(BoxingLooks.Theme t)
        {
            static Vector4 L(Color c, float a = 1f)
            {
                var l = c.linear;
                return new Vector4(l.r, l.g, l.b, a);
            }
            Shader.SetGlobalVector("_ArenaShade", L(t.Shade));
            Shader.SetGlobalVector("_ArenaSky", L(t.Sky));
            Shader.SetGlobalVector("_ArenaBounce", L(t.Bounce));
            Shader.SetGlobalVector("_ArenaRim", L(t.Rim, t.RimStrength));
            Shader.SetGlobalVector("_ArenaRimShade", L(t.RimShade));
            Shader.SetGlobalVector("_ArenaHaze", L(t.Haze));
            Shader.SetGlobalVector("_ArenaHazeFar", L(t.HazeFar));
            Shader.SetGlobalVector("_ArenaFog", t.Fog);
            Shader.SetGlobalFloat("_ArenaNight", t.Night);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = t.Haze;
            RenderSettings.fogStartDistance = t.Fog.x;
            RenderSettings.fogEndDistance = t.Fog.y * 1.6f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = t.Ambient;
            if (_sun != null)
            {
                _sun.color = t.Sun;
                _sun.intensity = t.SunIntensity;
                _sun.transform.rotation = Quaternion.Euler(t.SunPitch, t.SunYaw, 0f);
                _sun.shadows = LightShadows.None;
            }
            if (_cam != null)
            {
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = t.Background;
            }
        }

        void Twinkles(Random rng, int count, float radius)
        {
            if (_art.glow == null) return;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var c = new List<Color>();
            var tris = new List<int>();
            for (int i = 0; i < count; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2.0);
                float e = Mathf.Lerp(-0.35f, 1.3f, (float)rng.NextDouble());
                var d = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
                var at = d * radius;
                float size = 0.4f + (float)rng.NextDouble() * 1.3f;
                var right = Vector3.Cross(Vector3.up, d).normalized * size;
                if (right.sqrMagnitude < 1e-4f) right = Vector3.right * size;
                var up = Vector3.Cross(d, right).normalized * size;
                int b = v.Count;
                v.Add(at - right - up);
                v.Add(at - right + up);
                v.Add(at + right + up);
                v.Add(at + right - up);
                uv.Add(new Vector2(0f, 0f));
                uv.Add(new Vector2(0f, 1f));
                uv.Add(new Vector2(1f, 1f));
                uv.Add(new Vector2(1f, 0f));
                var colour = rng.NextDouble() < 0.75 ? Color.white : new Color(0.62f, 0.9f, 1f);
                colour.a = 0.35f + (float)rng.NextDouble() * 0.6f;
                for (int k = 0; k < 4; k++) c.Add(colour);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            _twinkles = new Mesh { name = "Twinkles" };
            _twinkles.SetVertices(v);
            _twinkles.SetUVs(0, uv);
            _twinkles.SetColors(c);
            _twinkles.SetTriangles(tris, 0);
            _twinkles.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 3f);
            Show("Twinkles", _stage, _twinkles, _art.glow);
        }

        void Update()
        {
            if (_beacon != null) _beacon.localScale = Vector3.one * (Mathf.Repeat(Time.time, 1.4f) < 0.45f ? 1f : 0.02f);
        }

        /// <summary>The crowd roars (a knockdown, the super punch, the win).</summary>
        public void CheerCrowd(float amount = 1f) => Crowd?.Cheer(amount);

        /// <summary>A seat somewhere in the crowd (flashes), or null without a crowd.</summary>
        public Vector3? RandomSeat(System.Random rng) => Seats.Count == 0 ? (Vector3?)null : Seats[rng.Next(Seats.Count)];

        void OnDestroy()
        {
            if (_twinkles != null) Destroy(_twinkles);
            if (_crowd != null) Destroy(_crowd);
        }

        public static string Seat(float x, float y, float z, float yaw) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1:0.00} {2:0.00} {3:0}\n", x, y, z, yaw);
    }
}
