using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Space
{
    [System.Serializable]
    public sealed class SpaceWorldArt
    {
        [Tooltip("art/space's props (the belts' rocks)")] public GameObject props;
        [Tooltip("Tundu/ProcPlanet: every planet and moon (its look in a property block)")] public Material planet;
        [Tooltip("Tundu/Halo: the planets' air, the stars' coronas")] public Material halo;
        [Tooltip("Tundu/Star: the suns (a copy per kind of star)")] public Material star;
        [Tooltip("Tundu/PlanetLayer: clouds and rings")] public Material layer;
        [Tooltip("Tundu/Dust: the specks round the camera")] public Material dust;
        [Tooltip("Tundu/SkyPanorama: the sky (a copy turned and tinted for each system)")] public Material sky;
        [Tooltip("Tundu/Glass")] public Material glass;
        [Tooltip("Tundu/Fx, added on, vertex colours: the jetpack's flames")] public Material flame;
        [Tooltip("Tundu/Fx, added on, the soft dot: glows and streaks")] public Material glow;
        [Tooltip("Tundu/Toon with the palette: the belts' rocks")] public Material toon;
        [Tooltip("tools/space/heights.py's eight height maps")] public Texture2DArray heights;
        public Texture2D clouds, rings, ui;
        public TextAsset uiJson;
    }

    /// <summary>
    /// Yjet!'s world (2026-11-14, the exploration rework): one star system at a time, drawn from the galaxy's rules
    /// (<see cref="Galaxy"/>): its sun (a boiling sphere and a corona), its planets (each one of the eight height maps
    /// coloured through its own ramp: <see cref="Ramps"/>, a row a planet, made here from the galaxy's looks; with its
    /// clouds, rings, moons and a glow of air), an asteroid belt round the sun in some systems, the sky turned and tinted
    /// so every system feels its own, and dust specks round the camera that make the speed felt. On the flight's plane:
    /// the explorer's (x, y) is the world's (x, z); planets sit up to 40 m over or under it.
    /// </summary>
    public sealed class SpaceWorld : MonoBehaviour
    {
        /// <summary>A planet as drawn: its pivot (place and axial tilt: the rings and the air hang on it), the body and the
        /// clouds turning on it, its moons.</summary>
        public sealed class PlanetView
        {
            public Planet Planet;
            public Transform Pivot, Body, Clouds;
            public Vector3 Centre;
            public float Spin, CloudSpin;
            public readonly List<(Transform t, float radius, float speed, float phase, Quaternion plane)> Moons =
                new List<(Transform, float, float, float, Quaternion)>();
        }

        SpaceWorldArt _art;
        Galaxy _galaxy;
        Texture2D _ramps;
        int _rampRows;
        Mesh _sphere, _sphereMid, _sphereLow, _quad, _haloQuad, _ring, _dust, _flame, _dome;
        readonly Mesh[] _rocks = new Mesh[3];
        Dictionary<string, Rect> _sheet;
        readonly List<Object> _made = new List<Object>();
        readonly Dictionary<StarKind, Material> _stars = new Dictionary<StarKind, Material>();
        Material _sky, _ringMaterial, _planet;
        Transform _system;
        MaterialPropertyBlock _block;
        float _time;
        readonly List<PlanetView> _views = new List<PlanetView>();
        // the belt's rocks: where, their tumble, size, which mesh; those near the camera drawn instanced each frame
        readonly List<(Vector3 at, Vector3 axis, float spin, float size, int mesh)> _belt = new List<(Vector3, Vector3, float, float, int)>();
        readonly List<Matrix4x4>[] _rockM = { new List<Matrix4x4>(256), new List<Matrix4x4>(256), new List<Matrix4x4>(256) };

        public StarSystem System { get; private set; }
        public IReadOnlyList<PlanetView> Views => _views;
        public Mesh Dome => _dome;
        public Mesh FlameMesh => _flame;
        public Dictionary<string, Rect> Sheet => _sheet;
        /// <summary>The star's light (the astronauts' sun takes it).</summary>
        public Color StarLight { get; private set; } = Color.white;

        public static SpaceWorld Create(SpaceWorldArt art, Galaxy galaxy)
        {
            var go = new GameObject("Space world");
            var w = go.AddComponent<SpaceWorld>();
            w.Build(art, galaxy);
            return w;
        }

        void OnDestroy()
        {
            foreach (var o in _made)
                if (o != null) Destroy(o);
        }

        T Keep<T>(T thing) where T : Object
        {
            _made.Add(thing);
            return thing;
        }

        void Build(SpaceWorldArt art, Galaxy galaxy)
        {
            _art = art;
            _galaxy = galaxy;
            _block = new MaterialPropertyBlock();
            _sphere = Keep(SpaceArt.Sphere(72, 44));
            _sphereMid = Keep(SpaceArt.Sphere(48, 28));
            _sphereLow = Keep(SpaceArt.Sphere(28, 16));
            _quad = Keep(SpaceArt.Quad());
            _haloQuad = Keep(SpaceArt.Quad());
            // a halo is turned to the camera in its shader: its bounds must hold it from any side
            _haloQuad.bounds = new Bounds(Vector3.zero, Vector3.one);
            _ring = Keep(SpaceArt.FlatRing(1.35f, 2.35f, 128));
            _flame = Keep(SpaceArt.Flame());
            _dome = Keep(SpaceArt.Sphere(32, 20));
            _dust = Keep(DustMesh(900, 11));
            _sheet = SpaceArt.Sheet(art.uiJson);
            for (int i = 0; i < 3; i++) _rocks[i] = Prop($"Rock{i}");
            _ramps = Keep(Ramps(galaxy, out _rampRows));
            if (art.planet != null)
            {
                // a copy: the runtime ramps never touch the material asset (in the editor that would be saved)
                _planet = Keep(new Material(art.planet) { name = "Planets" });
                _planet.SetTexture("_Heights", art.heights);
                _planet.SetTexture("_Ramps", _ramps);
            }
            if (art.layer != null)
            {
                // rings are seen from both sides: their own copy (the culling is the material's, not a property's)
                _ringMaterial = Keep(new Material(art.layer) { name = "Rings" });
                _ringMaterial.SetFloat("_Cull", 0f);
                _ringMaterial.SetFloat("_TwoSided", 1f);
                _ringMaterial.SetFloat("_StarLit", 1f);
                if (art.rings != null) _ringMaterial.SetTexture("_BaseMap", art.rings);
            }
            if (art.sky != null)
            {
                _sky = Keep(new Material(art.sky) { name = "Sky (this system)" });
                RenderSettings.skybox = _sky;
            }
            foreach (StarKind kind in global::System.Enum.GetValues(typeof(StarKind)))
            {
                if (art.star == null) break;
                var (edge, core, _) = StarColours(kind);
                var m = Keep(new Material(art.star) { name = $"Star {kind}" });
                m.SetTexture("_Heights", art.heights);
                m.SetColor("_Color", edge);
                m.SetColor("_Core", core);
                _stars[kind] = m;
            }
            if (art.dust != null)
            {
                var go = new GameObject("Dust");
                go.transform.SetParent(transform, false);
                Render(go, _dust, art.dust);
            }
        }

        /// <summary>A star's colours: its edge, its middle, the light it gives.</summary>
        public static (Color edge, Color core, Color light) StarColours(StarKind kind) => kind switch
        {
            StarKind.Red => (new Color(1f, 0.32f, 0.2f) * 1.3f, new Color(1f, 0.72f, 0.5f) * 1.4f, new Color(1f, 0.72f, 0.6f)),
            StarKind.Orange => (new Color(1f, 0.5f, 0.15f) * 1.3f, new Color(1f, 0.86f, 0.6f) * 1.5f, new Color(1f, 0.86f, 0.7f)),
            StarKind.Yellow => (new Color(1f, 0.72f, 0.25f) * 1.4f, new Color(1f, 0.97f, 0.82f) * 1.6f, new Color(1f, 0.96f, 0.86f)),
            StarKind.White => (new Color(0.85f, 0.9f, 1f) * 1.4f, new Color(1f, 1f, 1f) * 1.7f, new Color(0.96f, 0.98f, 1f)),
            _ => (new Color(0.4f, 0.6f, 1f) * 1.5f, new Color(0.85f, 0.93f, 1f) * 1.7f, new Color(0.82f, 0.9f, 1f)),
        };

        Mesh Prop(string name)
        {
            if (_art.props == null) return null;
            foreach (var f in _art.props.GetComponentsInChildren<MeshFilter>(true))
                if (f.name == name) return f.sharedMesh;
            return null;
        }

        static MeshRenderer Render(GameObject go, Mesh mesh, Material material)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        // ------------------------------------------------------------------------------------------------ the ramps

        /// <summary>Every planet's colour ramp as a row of one texture (256 across: alpha = glow), then a few grey ones for
        /// the moons. sRGB: the looks' colours are picked as the eye sees them.</summary>
        static Texture2D Ramps(Galaxy galaxy, out int rows)
        {
            int count = galaxy.Planets.Count + MoonRamps;
            rows = Mathf.NextPowerOfTwo(count);
            var tex = new Texture2D(256, rows, TextureFormat.RGBA32, false, false)
            {
                name = "Planet ramps", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[256 * rows];
            foreach (var p in galaxy.Planets) Row(px, p.Id, p.Look.Ramp);
            var moons = new[]
            {
                (new Rgb(0.32f, 0.31f, 0.33f), new Rgb(0.82f, 0.8f, 0.78f)),
                (new Rgb(0.38f, 0.32f, 0.27f), new Rgb(0.86f, 0.78f, 0.66f)),
                (new Rgb(0.28f, 0.32f, 0.4f), new Rgb(0.78f, 0.84f, 0.92f)),
                (new Rgb(0.42f, 0.27f, 0.24f), new Rgb(0.9f, 0.7f, 0.6f)),
            };
            for (int m = 0; m < MoonRamps; m++)
                Row(px, galaxy.Planets.Count + m, new List<(float, Rgb, float)> { (0f, moons[m].Item1, 0f), (1f, moons[m].Item2, 0f) });
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        const int MoonRamps = 4;

        static void Row(Color32[] px, int row, List<(float at, Rgb colour, float glow)> ramp)
        {
            for (int x = 0; x < 256; x++)
            {
                float h = x / 255f;
                Rgb c = ramp[ramp.Count - 1].colour;
                float g = ramp[ramp.Count - 1].glow;
                if (h <= ramp[0].at)
                {
                    c = ramp[0].colour;
                    g = ramp[0].glow;
                }
                else
                    for (int i = 1; i < ramp.Count; i++)
                        if (h <= ramp[i].at)
                        {
                            float k = (h - ramp[i - 1].at) / Mathf.Max(1e-4f, ramp[i].at - ramp[i - 1].at);
                            c = Rgb.Lerp(ramp[i - 1].colour, ramp[i].colour, k);
                            g = Mathf.Lerp(ramp[i - 1].glow, ramp[i].glow, k);
                            break;
                        }
                px[row * 256 + x] = new Color32(B(c.R), B(c.G), B(c.B), B(g));
            }
        }

        static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        float RowV(int row) => (row + 0.5f) / _rampRows;

        // ------------------------------------------------------------------------------------------------ a system

        /// <summary>The world of system <paramref name="s"/> (the last one's taken away).</summary>
        public void Show(StarSystem s)
        {
            if (_system != null) Destroy(_system.gameObject);
            _views.Clear();
            _belt.Clear();
            System = s;
            _system = new GameObject(s.Name).transform;
            _system.SetParent(transform, false);
            var rng = new global::System.Random(s.Id * 7919 + 17);
            float R() => (float)rng.NextDouble();
            // the sun and its corona
            var (edge, _, light) = StarColours(s.Star);
            StarLight = light;
            var sun = new GameObject("Sun");
            sun.transform.SetParent(_system, false);
            sun.transform.localScale = Vector3.one * s.StarRadius;
            if (_stars.TryGetValue(s.Star, out var starMat)) Render(sun, _sphereMid, starMat);
            Halo(_system, Vector3.zero, s.StarRadius, edge * 0.9f, 3.0f, 2.0f, true);
            // the planets
            foreach (var p in s.Planets) _views.Add(PlanetAt(p, R));
            // an asteroid belt round the sun: rocks over and under the flight's plane (never on it: it's flown through)
            if (s.Belt > 0f && _rocks[0] != null)
            {
                int n = Mathf.RoundToInt(s.Belt * 2f * Mathf.PI / 9f);
                for (int i = 0; i < n; i++)
                {
                    float a = R() * Mathf.PI * 2f, r = s.Belt + (R() - 0.5f) * 90f;
                    float y = (R() < 0.5f ? -1f : 1f) * Mathf.Lerp(5f, 30f, R() * R());
                    var axis = new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f).normalized;
                    _belt.Add((new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), axis, Mathf.Lerp(8f, 40f, R()), Mathf.Lerp(0.8f, 5.5f, R() * R()), rng.Next(3)));
                }
            }
            if (_sky != null)
            {
                _sky.SetFloat("_Turn", s.SkyTurn);
                _sky.SetFloat("_Hue", s.SkyHue);
                _sky.SetFloat("_Saturation", 1.15f);
            }
            Shader.SetGlobalVector("_TunduStar", new Vector4(0f, 0f, 0f, 1f));
            Shader.SetGlobalColor("_TunduStarColor", light);
        }

        PlanetView PlanetAt(Planet p, global::System.Func<float> R)
        {
            var look = p.Look;
            var v = new PlanetView { Planet = p, Centre = CentreOf(p) };
            var pivot = new GameObject(p.Name).transform;
            pivot.SetParent(_system, false);
            pivot.localPosition = v.Centre;
            float tilt = (R() - 0.5f) * 44f;
            pivot.localRotation = Quaternion.Euler(0f, R() * 360f, tilt);
            v.Pivot = pivot;
            // the body: the shared material, this planet's look in a property block
            var body = new GameObject("Body");
            body.transform.SetParent(pivot, false);
            body.transform.localScale = Vector3.one * p.Radius;
            var r = Render(body, _sphere, _planet);
            _block.Clear();
            _block.SetVector("_Planet", new Vector4(look.Height, RowV(p.Id), look.Shift, look.Flip ? -1f : 1f));
            float latitude = p.Type == PlanetType.Rainbow ? 1f : p.Type == PlanetType.Gas ? 0.22f : 0f;
            float relief = p.Type switch
            {
                PlanetType.Gas or PlanetType.Rainbow => 0.006f,
                PlanetType.Crater => 0.07f,
                PlanetType.Crystal => 0.05f,
                PlanetType.Desert => 0.045f,
                PlanetType.Ice or PlanetType.Lava => 0.05f,
                _ => 0.04f,
            };
            float glow = p.Type == PlanetType.Lava ? 1.4f : p.Type == PlanetType.Crystal ? 1.1f : 0.8f;
            _block.SetVector("_Surface", new Vector4(look.Sea, latitude, relief, glow));
            var air = C(look.Atmosphere);
            _block.SetColor("_Atmo", new Color(air.r, air.g, air.b, 0.9f * look.AtmosphereStrength));
            r.SetPropertyBlock(_block);
            v.Body = body.transform;
            v.Spin = look.Spin * 360f;
            // clouds turning a little faster than the ground
            if (look.Clouds > 0f && _art.layer != null && _art.clouds != null)
            {
                var clouds = new GameObject("Clouds");
                clouds.transform.SetParent(pivot, false);
                clouds.transform.localScale = Vector3.one * (p.Radius * 1.022f);
                var cr = Render(clouds, _sphereMid, _art.layer);
                _block.Clear();
                _block.SetTexture("_BaseMap", _art.clouds);
                var tint = C(look.CloudTint);
                _block.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(look.Clouds * 1.05f)));
                _block.SetVector("_BaseMap_ST", new Vector4(1f, 1f, R(), 0f));
                _block.SetFloat("_Ambient", 0.3f);
                _block.SetFloat("_Spin", 0f);
                _block.SetFloat("_TwoSided", 0f);
                _block.SetFloat("_StarLit", 1f);
                cr.SetPropertyBlock(_block);
                v.Clouds = clouds.transform;
                v.CloudSpin = v.Spin * 1.6f + 1.2f;
            }
            // rings
            if (look.Rings && _ringMaterial != null && _art.rings != null)
            {
                var ring = new GameObject("Rings");
                ring.transform.SetParent(pivot, false);
                ring.transform.localScale = Vector3.one * p.Radius;
                ring.transform.localRotation = Quaternion.Euler(look.RingTilt * 0.5f, 0f, 0f);
                var rr = Render(ring, _ring, _ringMaterial);
                _block.Clear();
                var tint = C(look.RingTint);
                _block.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 0.95f));
                rr.SetPropertyBlock(_block);
            }
            // the air round it, lit on its sunny side
            Halo(pivot, Vector3.zero, p.Radius, air * (0.55f + 0.35f * look.AtmosphereStrength), 1.5f, 7.5f, false);
            // moons on steep orbits (they cross the flight's plane only now and then)
            for (int i = 0; i < look.Moons.Count; i++)
            {
                var moon = new GameObject($"Moon {i + 1}");
                moon.transform.SetParent(_system, false);
                float size = Mathf.Max(3f, p.Radius * look.Moons[i]);
                moon.transform.localScale = Vector3.one * size;
                var mr = Render(moon, _sphereLow, _planet);
                _block.Clear();
                _block.SetVector("_Planet", new Vector4(2f, RowV(_galaxy.Planets.Count + (p.Id + i) % MoonRamps), R(), 1f));
                _block.SetVector("_Surface", new Vector4(-1f, 0f, 0.08f, 0f));
                _block.SetColor("_Atmo", new Color(0.6f, 0.7f, 0.9f, 0.25f));
                mr.SetPropertyBlock(_block);
                var plane = Quaternion.Euler(Mathf.Lerp(55f, 82f, R()) * (R() < 0.5f ? -1f : 1f), R() * 360f, 0f);
                v.Moons.Add((moon.transform, p.Radius * (2.1f + i * 0.75f), Mathf.Lerp(0.08f, 0.2f, R()) / (1f + i * 0.5f), R() * Mathf.PI * 2f, plane));
            }
            return v;
        }

        void Halo(Transform parent, Vector3 at, float radius, Color colour, float size, float falloff, bool corona)
        {
            if (_art.halo == null) return;
            var go = new GameObject(corona ? "Corona" : "Air");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * (radius * size * 2f);
            var r = Render(go, _haloQuad, _art.halo);
            _block.Clear();
            _block.SetColor("_Color", colour);
            _block.SetFloat("_Size", size);
            _block.SetFloat("_Falloff", falloff);
            _block.SetFloat("_Corona", corona ? 1f : 0f);
            _block.SetFloat("_Radius", radius);
            r.SetPropertyBlock(_block);
        }

        static Color C(Rgb c) => new Color(c.R, c.G, c.B);

        /// <summary>Where a planet's middle is in the world.</summary>
        public static Vector3 CentreOf(Planet p) => new Vector3(p.Position.x, p.Lift, p.Position.y);

        /// <summary>The flight's plane point in the world.</summary>
        public static Vector3 World(Vector2 plane, float height = 0f) => new Vector3(plane.x, height, plane.y);

        public PlanetView ViewOf(Planet p)
        {
            foreach (var v in _views)
                if (v.Planet == p) return v;
            return null;
        }

        // ------------------------------------------------------------------------------------------------ every frame

        /// <summary>Turns the planets and their clouds, moves the moons, draws the belt's rocks near the camera, and sets the
        /// dust's streaks from the flight's velocity.</summary>
        public void Draw(float dt, Camera cam, Vector3 velocity)
        {
            _time += dt;
            foreach (var v in _views)
            {
                v.Body.localRotation = Quaternion.Euler(0f, -_time * v.Spin, 0f);
                if (v.Clouds != null) v.Clouds.localRotation = Quaternion.Euler(0f, -_time * v.CloudSpin, 0f);
                foreach (var (t, radius, speed, phase, plane) in v.Moons)
                {
                    float a = phase + _time * speed;
                    t.localPosition = v.Centre + plane * new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                    t.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                }
            }
            Shader.SetGlobalVector("_DustVelocity", velocity);
            if (cam == null || _belt.Count == 0 || _art.toon == null) return;
            var c = cam.transform.position;
            foreach (var l in _rockM) l.Clear();
            foreach (var (at, axis, spin, size, mesh) in _belt)
            {
                var d = at - c;
                if (d.sqrMagnitude > 520f * 520f || _rockM[mesh].Count >= 1000) continue;
                _rockM[mesh].Add(Matrix4x4.TRS(at, Quaternion.AngleAxis(_time * spin, axis), Vector3.one * size));
            }
            var rp = new RenderParams(_art.toon) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            for (int i = 0; i < 3; i++)
                if (_rockM[i].Count > 0 && _rocks[i] != null) Graphics.RenderMeshInstanced(rp, _rocks[i], 0, _rockM[i]);
        }

        // ------------------------------------------------------------------------------------------------ dust

        static Mesh DustMesh(int count, int seed)
        {
            var rng = new global::System.Random(seed);
            var verts = new List<Vector3>(count * 4);
            var uvs = new List<Vector2>(count * 4);
            var colours = new List<Color>(count * 4);
            var tris = new List<int>(count * 6);
            Vector2[] corners = { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f) };
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
                float b = 0.35f + 0.65f * (float)rng.NextDouble();
                int v = verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    verts.Add(p);
                    uvs.Add(corners[k]);
                    colours.Add(new Color(b, b, b, 1f));
                }
                tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
                tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
            }
            var mesh = new Mesh { name = "Dust", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }

        // ------------------------------------------------------------------------------------------------ glows

        // soft camera-facing dots and streaks, added on, one mesh a frame (one draw call): the scan's beams, sparkles,
        // the target's marker light (the box has no bloom: this is it)
        Mesh _glowMesh;
        readonly List<Vector3> _gVerts = new List<Vector3>(512);
        readonly List<Color32> _gColours = new List<Color32>(512);
        readonly List<Vector2> _gUvs = new List<Vector2>(512);
        readonly List<int> _gTris = new List<int>(768);
        Vector3 _camPos, _camRight = Vector3.right, _camUp = Vector3.up;

        public void BeginGlows(Camera cam)
        {
            _gVerts.Clear();
            _gColours.Clear();
            _gUvs.Clear();
            _gTris.Clear();
            if (cam == null) return;
            _camPos = cam.transform.position;
            _camRight = cam.transform.right;
            _camUp = cam.transform.up;
        }

        /// <summary>A soft round glow <paramref name="size"/> m across.</summary>
        public void Glow(Vector3 at, float size, Color colour, float alpha)
        {
            if (alpha <= 0.002f) return;
            var r = _camRight * size * 0.5f;
            var u = _camUp * size * 0.5f;
            Quad(at - r - u, at - r + u, at + r + u, at + r - u, colour, alpha);
        }

        /// <summary>A soft streak from <paramref name="at"/> to <paramref name="to"/>, <paramref name="width"/> m wide.</summary>
        public void Streak(Vector3 at, Vector3 to, float width, Color colour, float alpha)
        {
            if (alpha <= 0.002f) return;
            var dir = to - at;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            var side = Vector3.Cross(dir, (_camPos - at).normalized);
            if (side.sqrMagnitude < 1e-6f) return;
            side = side.normalized * width * 0.5f;
            var a0 = at - dir * width * 0.5f;
            var b0 = to + dir * width * 0.5f;
            Quad(a0 - side, b0 - side, b0 + side, a0 + side, colour, alpha);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, float alpha)
        {
            int i = _gVerts.Count;
            _gVerts.Add(a);
            _gVerts.Add(b);
            _gVerts.Add(c);
            _gVerts.Add(d);
            _gUvs.Add(new Vector2(0f, 0f));
            _gUvs.Add(new Vector2(0f, 1f));
            _gUvs.Add(new Vector2(1f, 1f));
            _gUvs.Add(new Vector2(1f, 0f));
            var c32 = (Color32)new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(alpha));
            for (int k = 0; k < 4; k++) _gColours.Add(c32);
            _gTris.Add(i);
            _gTris.Add(i + 1);
            _gTris.Add(i + 2);
            _gTris.Add(i);
            _gTris.Add(i + 2);
            _gTris.Add(i + 3);
        }

        public void DrawGlows()
        {
            if (_art.glow == null) return;
            if (_glowMesh == null)
            {
                _glowMesh = Keep(new Mesh { name = "Glows", indexFormat = IndexFormat.UInt32 });
                _glowMesh.MarkDynamic();
            }
            _glowMesh.Clear();
            if (_gVerts.Count == 0) return;
            _glowMesh.SetVertices(_gVerts);
            _glowMesh.SetColors(_gColours);
            _glowMesh.SetUVs(0, _gUvs);
            _glowMesh.SetTriangles(_gTris, 0, false);
            _glowMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            var rp = new RenderParams(_art.glow) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e6f) };
            Graphics.RenderMesh(rp, _glowMesh, 0, Matrix4x4.identity);
        }
    }
}
