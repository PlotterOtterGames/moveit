using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MoveIt.Games.Sports;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace MoveIt.Games.Kart
{
    /// <summary>What a track is built from (set by the scene builder): materials, the scenery's models.</summary>
    [Serializable]
    public sealed class KartWorldArt
    {
        [Tooltip("Tundu/Toon, vertex colours, the simple path: the road, its kerbs, the barriers, the ramps.")] public Material road;
        [Tooltip("Tundu/Toon, vertex colours, the simple path with the world detail: the ground.")] public Material ground;
        [Tooltip("Tundu/Toon with the palette (art/kart/props.py, Vrapo!'s props): the scenery.")] public Material props;
        [Tooltip("Tundu/Toon, vertex colours, gloss and emission: the stars.")] public Material stars;
        [Tooltip("Tundu/Fx additive: the boost pads' arrows (scrolled).")] public Material pads;
        [Tooltip("Tundu/Water: the sea.")] public Material water;
        [Tooltip("Tundu/Crowd: the fans in the stands.")] public Material crowd;
        [Tooltip("art/kart/props.py: one mesh per prop.")] public GameObject kartProps;
        [Tooltip("Vrapo!'s dino valley: ferns, cycads, rocks, the volcano.")] public GameObject[] dinoProps;
        [Tooltip("Vrapo!'s dino and pterodactyl (animated).")] public GameObject dino, ptero;
        public RuntimeAnimatorController dinoController, pteroController;
    }

    /// <summary>
    /// Builds a <see cref="KartTrack"/> as a place (2026-11-08): the ground following the track's hills and rising into
    /// green hills (on the coast the land drops into the sea), the road as a ribbon a metre a step (asphalt with white edge
    /// lines, red and white kerbs, grass or sand shoulders, soft barriers in red and white blocks), the start line under its
    /// gantry, ramps (striped wedges), boost pads (scrolling arrows), gift boxes and stars that spin, scenery by the theme
    /// (the coast: palms, umbrellas, white houses, the lighthouse, the sea; the village: cypresses, olive trees, stone
    /// houses, the castle on its hill; the dino valley: Vrapo!'s ferns, cycads, rocks, volcano, its dino and pterodactyls),
    /// grandstands with Sporte!'s crowd by the start, mountains on the horizon. Everything static is drawn by few meshes in
    /// one material each (the TV box's GPU pays per draw and per pixel: no real-time shadows, the simple toon path).
    /// </summary>
    public sealed class KartWorld : MonoBehaviour
    {
        public KartTrack Track { get; private set; }
        public Fans Fans { get; private set; }
        /// <summary>The countdown lamps on the gantry (coloured by the game).</summary>
        public Renderer GantryLights { get; private set; }

        KartWorldArt _art;
        Dictionary<string, Mesh> _props;
        readonly List<(Transform t, int feature, int index, float phase)> _boxes = new List<(Transform, int, int, float)>();
        readonly List<(Transform t, int feature, int index)> _stars = new List<(Transform, int, int)>();
        readonly List<(Transform t, Vector3 centre, float radius, float speed, float height, float phase)> _flyers =
            new List<(Transform, Vector3, float, float, float, float)>();
        Vector2[] _coarse;
        float[] _coarseHeight;
        Mesh _starMesh;
        readonly List<Mesh> _meshes = new List<Mesh>();
        Random _rng;

        static readonly Color Asphalt = new Color(0.3f, 0.32f, 0.39f), AsphaltDark = new Color(0.27f, 0.29f, 0.36f);

        public static KartWorld Build(KartTrack track, KartWorldArt art, Transform parent, int seed = 1)
        {
            var go = new GameObject($"Track {track.Id}");
            go.transform.SetParent(parent, false);
            var w = go.AddComponent<KartWorld>();
            w.Make(track, art, seed);
            return w;
        }

        void Make(KartTrack track, KartWorldArt art, int seed)
        {
            Track = track;
            _art = art;
            _rng = new Random(seed * 7919 + (int)track.Theme);
            _props = new Dictionary<string, Mesh>();
            if (art.kartProps != null)
                foreach (var mf in art.kartProps.GetComponentsInChildren<MeshFilter>(true))
                    _props[mf.name] = mf.sharedMesh;
            if (art.dinoProps != null)
                foreach (var p in art.dinoProps)
                    if (p != null)
                    {
                        var mf = p.GetComponentInChildren<MeshFilter>(true);
                        if (mf != null) _props[p.name] = mf.sharedMesh;
                    }
            // the track every 3 m (nearest-point questions)
            int n = Mathf.CeilToInt(track.Length / 3f);
            _coarse = new Vector2[n];
            _coarseHeight = new float[n];
            for (int i = 0; i < n; i++)
            {
                _coarse[i] = track.Point(i * 3f);
                _coarseHeight[i] = track.Height(i * 3f);
            }

            BuildGround();
            BuildRoad();
            BuildStartLine();
            BuildRamps();
            BuildPads();
            BuildBoxesAndStars();
            BuildScenery();
            BuildBackdrop();
        }

        void OnDestroy()
        {
            foreach (var m in _meshes)
                if (m != null) Destroy(m);
        }

        // ------------------------------------------------------------------------------------------------ the ground

        /// <summary>The nearest point of the track to a plan point: its distance (m) and the road's height there.</summary>
        public float Nearest(Vector2 p, out float height)
        {
            float best = float.MaxValue;
            int at = 0;
            for (int i = 0; i < _coarse.Length; i++)
            {
                float d = (_coarse[i] - p).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    at = i;
                }
            }
            height = _coarseHeight[at];
            return Mathf.Sqrt(best);
        }

        /// <summary>The theme's land far from the track (m): gentle hills, the coast dropping into the sea.</summary>
        float Land(Vector2 p)
        {
            float hills = Mathf.PerlinNoise(p.x * 0.011f + 13.7f, p.y * 0.011f + 4.1f) * 14f + Mathf.PerlinNoise(p.x * 0.03f, p.y * 0.03f) * 4f;
            switch (Track.Theme)
            {
                case KartTheme.Coast:
                    // the sea south of the long straight: the land goes down past the beach
                    return SeaSide(p) ? Mathf.Lerp(0.4f, -6f, Mathf.InverseLerp(-28f, -90f, p.y)) : hills * 0.8f;
                case KartTheme.Village:
                    return hills * 1.2f + 2f;
                default:
                    return hills * 0.9f;
            }
        }

        bool SeaSide(Vector2 p) => Track.Theme == KartTheme.Coast && p.y < -28f;

        float GroundAt(Vector2 p)
        {
            float d = Nearest(p, out float h);
            float edge = Track.Wall + 1.5f;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge, edge + 45f, d));
            float near = h - 0.12f;
            return Mathf.Lerp(near, Mathf.Max(Land(p), SeaSide(p) ? -6f : h - 4f), k);
        }

        Color GroundColour(Vector2 p, float height, float distance)
        {
            float n = Mathf.PerlinNoise(p.x * 0.05f + 3f, p.y * 0.05f + 9f);
            Color grass, grass2, sand = new Color(0.95f, 0.85f, 0.62f);
            switch (Track.Theme)
            {
                case KartTheme.Coast:
                    grass = new Color(0.42f, 0.7f, 0.26f);
                    grass2 = new Color(0.33f, 0.6f, 0.22f);
                    if (SeaSide(p) || height < 0.6f && p.y < -15f) return Color.Lerp(sand, new Color(0.88f, 0.76f, 0.54f), n);
                    break;
                case KartTheme.Village:
                    grass = new Color(0.47f, 0.68f, 0.26f);
                    grass2 = new Color(0.38f, 0.58f, 0.22f);
                    break;
                default:
                    grass = new Color(0.36f, 0.66f, 0.24f);
                    grass2 = new Color(0.26f, 0.54f, 0.22f);
                    if (n > 0.7f) return Color.Lerp(grass2, new Color(0.72f, 0.56f, 0.38f), (n - 0.7f) * 2.5f);
                    break;
            }
            var c = Color.Lerp(grass, grass2, n);
            // the strip by the barrier a little darker (worn)
            if (distance < Track.Wall + 3f) c *= 0.93f;
            return c;
        }

        void BuildGround()
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var p in _coarse)
            {
                x0 = Mathf.Min(x0, p.x);
                x1 = Mathf.Max(x1, p.x);
                z0 = Mathf.Min(z0, p.y);
                z1 = Mathf.Max(z1, p.y);
            }
            const float margin = 170f, cell = 6f;
            x0 -= margin;
            x1 += margin;
            z0 -= margin;
            z1 += margin;
            int nx = Mathf.CeilToInt((x1 - x0) / cell) + 1, nz = Mathf.CeilToInt((z1 - z0) / cell) + 1;
            // in chunks (frustum culling) of 40 x 40 cells
            const int chunk = 40;
            for (int cz = 0; cz < nz - 1; cz += chunk)
                for (int cx = 0; cx < nx - 1; cx += chunk)
                {
                    int ex = Mathf.Min(nx - 1, cx + chunk), ez = Mathf.Min(nz - 1, cz + chunk);
                    var verts = new List<Vector3>();
                    var cols = new List<Color>();
                    var tris = new List<int>();
                    int w = ex - cx + 1;
                    for (int j = cz; j <= ez; j++)
                        for (int i = cx; i <= ex; i++)
                        {
                            var p = new Vector2(x0 + i * cell, z0 + j * cell);
                            float d = Nearest(p, out _);
                            float h = GroundAt(p);
                            verts.Add(new Vector3(p.x, h, p.y));
                            cols.Add(GroundColour(p, h, d));
                        }
                    for (int j = 0; j < ez - cz; j++)
                        for (int i = 0; i < ex - cx; i++)
                        {
                            int a = j * w + i, b = a + 1, c = a + w, d = c + 1;
                            tris.Add(a);
                            tris.Add(c);
                            tris.Add(d);
                            tris.Add(a);
                            tris.Add(d);
                            tris.Add(b);
                        }
                    Add("Ground", verts, cols, tris, _art.ground);
                }
            if (Track.Theme == KartTheme.Coast && _art.water != null)
            {
                // the sea: a big plane south of the coast, under the land's edge
                var sea = new GameObject("Sea");
                sea.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "Sea" };
                float sx0 = x0 - 600f, sx1 = x1 + 600f, sz0 = z0 - 900f, sz1 = -20f;
                mesh.SetVertices(new List<Vector3> { new Vector3(sx0, -0.6f, sz0), new Vector3(sx0, -0.6f, sz1), new Vector3(sx1, -0.6f, sz1), new Vector3(sx1, -0.6f, sz0) });
                mesh.SetUVs(0, new List<Vector2> { new Vector2(sx0, sz0) * 0.1f, new Vector2(sx0, sz1) * 0.1f, new Vector2(sx1, sz1) * 0.1f, new Vector2(sx1, sz0) * 0.1f });
                mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                _meshes.Add(mesh);
                sea.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = sea.AddComponent<MeshRenderer>();
                r.sharedMaterial = _art.water;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ------------------------------------------------------------------------------------------------ the road

        /// <summary>
        /// The road as a ribbon, a cross-section a metre: the barrier (outer face, top, inner face), the shoulder, the kerb,
        /// the road (two white edge lines), and the same on the other side. In chunks of 60 m (culled when out of view).
        /// </summary>
        void BuildRoad()
        {
            var t = Track;
            int n = t.Samples;
            float step = t.SampleStep;
            float half = t.HalfWidth, wall = t.Wall;
            const float kerb = 0.9f, line = 0.22f, lineIn = 0.45f, barrierH = 0.75f, barrierW = 0.55f;
            var shoulder = Track.Theme == KartTheme.Coast ? new Color(0.9f, 0.78f, 0.52f) : new Color(0.4f, 0.64f, 0.25f);
            var shoulder2 = Track.Theme == KartTheme.Coast ? new Color(0.84f, 0.71f, 0.46f) : new Color(0.35f, 0.58f, 0.22f);
            var coral = new Color(0.92f, 0.22f, 0.2f);
            // across the road: (offset d, height above the road, colour picker) from the left barrier's back to the right's
            const int chunkLen = 60;
            for (int c0 = 0; c0 < n; c0 += chunkLen)
            {
                int c1 = Mathf.Min(n, c0 + chunkLen);
                var verts = new List<Vector3>();
                var cols = new List<Color>();
                var tris = new List<int>();
                // strips: each a list of (d, up) across, its colour by the sample
                void Strip(float dA, float upA, float dB, float upB, Func<int, Color> colour, bool flat = true)
                {
                    int start = verts.Count;
                    for (int i = c0; i <= c1; i++)
                    {
                        float s = i * step;
                        var col = colour(i);
                        verts.Add(t.World(s, dA, upA));
                        verts.Add(t.World(s, dB, upB));
                        cols.Add(col);
                        cols.Add(col);
                    }
                    for (int i = 0; i < c1 - c0; i++)
                    {
                        int a = start + i * 2, b = a + 1, a2 = a + 2, b2 = a + 3;
                        // a (left / A) near, b (B) near: a quad facing up when A is left of B
                        tris.Add(a);
                        tris.Add(a2);
                        tris.Add(b2);
                        tris.Add(a);
                        tris.Add(b2);
                        tris.Add(b);
                    }
                }
                Color Road(int i) => ((i / 3) % 2 == 0) ? Asphalt : AsphaltDark;
                Color Kerb(int i) => ((i / 3) % 2 == 0) ? coral : Color.white;
                Color Shoulder(int i) => ((i / 5) % 2 == 0) ? shoulder : shoulder2;
                Color Barrier(int i) => ((i / 4) % 2 == 0) ? coral : Color.white;
                Color White(int i) => Color.white;
                // the road (left to right: + d to - d)
                Strip(half, 0.02f, half - lineIn, 0.02f, Road);
                Strip(half - lineIn, 0.025f, half - lineIn - line, 0.025f, White);
                Strip(half - lineIn - line, 0.02f, -(half - lineIn - line), 0.02f, Road);
                Strip(-(half - lineIn - line), 0.025f, -(half - lineIn), 0.025f, White);
                Strip(-(half - lineIn), 0.02f, -half, 0.02f, Road);
                // kerbs (raised a little)
                Strip(half + kerb, 0.04f, half, 0.06f, Kerb);
                Strip(-half, 0.06f, -half - kerb, 0.04f, Kerb);
                // shoulders
                Strip(wall, 0f, half + kerb, 0.03f, Shoulder);
                Strip(-half - kerb, 0.03f, -wall, 0f, Shoulder);
                // the barriers: inner face, top, outer face (each side)
                Strip(wall, barrierH, wall, 0f, Barrier);                                   // left, facing the road
                Strip(wall + barrierW, barrierH, wall, barrierH, Barrier);                  // left top
                Strip(wall + barrierW, 0f, wall + barrierW, barrierH, Barrier);             // left back
                Strip(-wall, 0f, -wall, barrierH, Barrier);                                 // right, facing the road
                Strip(-wall, barrierH, -wall - barrierW, barrierH, Barrier);                // right top
                Strip(-wall - barrierW, barrierH, -wall - barrierW, 0f, Barrier);           // right back
                Add($"Road {c0}", verts, cols, tris, _art.road);
            }
        }

        void BuildStartLine()
        {
            var t = Track;
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            int across = 16, rows = 2;
            float cell = t.Width / across, depth = 0.8f;
            for (int r = 0; r < rows; r++)
                for (int a = 0; a < across; a++)
                {
                    float d0 = t.HalfWidth - a * cell, d1 = d0 - cell;
                    float s0 = -r * depth, s1 = s0 - depth;
                    int b = verts.Count;
                    verts.Add(t.World(s1, d0, 0.035f));
                    verts.Add(t.World(s0, d0, 0.035f));
                    verts.Add(t.World(s0, d1, 0.035f));
                    verts.Add(t.World(s1, d1, 0.035f));
                    var col = (a + r) % 2 == 0 ? Color.white : new Color(0.12f, 0.13f, 0.19f);
                    for (int k = 0; k < 4; k++) cols.Add(col);
                    tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                }
            Add("Start line", verts, cols, tris, _art.road);
            // the gantry over it, along the road
            // (its checkered band and lamps on its front, the model's +z: turned to face the karts coming)
            var at = t.World(-1.2f, 0f);
            var rot = Quaternion.LookRotation(-Dir(0f), Vector3.up);
            Prop("Gantry", at, rot, 1f);
            GantryLights = Prop("GantryLights", at, rot, 1f)?.GetComponent<Renderer>();
            if (GantryLights != null)
            {
                // the lamps need their own colours: a Tundu/Toon of their own (the game sets its colour)
                GantryLights.material = new Material(_art.stars) { name = "Gantry lamps" };
                GantryLights.material.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.25f));
                GantryLights.material.SetFloat("_UseVertexColor", 0f);
            }
        }

        Vector3 Dir(float s)
        {
            var tan = Track.Tangent(s);
            return new Vector3(tan.x, Track.Slope(s), tan.y).normalized;
        }

        void BuildRamps()
        {
            var t = Track;
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            var sun = new Color(1f, 0.79f, 0.24f);
            var coral = new Color(1f, 0.35f, 0.31f);
            foreach (var f in t.Features)
            {
                if (f.Kind != FeatureKind.Ramp) continue;
                int steps = 8;
                float dl = f.D + f.Width * 0.5f, dr = f.D - f.Width * 0.5f;
                // the slope (chevrons by the across position), stepping up to the lip
                int across = 8;
                for (int k = 0; k < steps; k++)
                    for (int a = 0; a < across; a++)
                    {
                        float s0 = f.S - f.Length + f.Length * k / steps, s1 = f.S - f.Length + f.Length * (k + 1) / steps;
                        float h0 = KartRace.LipHeight * k / steps, h1 = KartRace.LipHeight * (k + 1) / steps;
                        float d0 = Mathf.Lerp(dl, dr, a / (float)across), d1 = Mathf.Lerp(dl, dr, (a + 1f) / across);
                        int b = verts.Count;
                        verts.Add(t.World(s0, d0, h0 + 0.03f));
                        verts.Add(t.World(s1, d0, h1 + 0.03f));
                        verts.Add(t.World(s1, d1, h1 + 0.03f));
                        verts.Add(t.World(s0, d1, h0 + 0.03f));
                        float mid = Mathf.Abs((a + 0.5f) / across - 0.5f);
                        var col = ((int)(k - mid * 6f + 8) % 2 == 0) ? sun : coral;
                        for (int q = 0; q < 4; q++) cols.Add(col);
                        tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    }
                // the lip's face (the back, toward the landing) and the sides
                int e = verts.Count;
                verts.Add(t.World(f.S, dl, KartRace.LipHeight + 0.03f));
                verts.Add(t.World(f.S, dl, 0f));
                verts.Add(t.World(f.S, dr, 0f));
                verts.Add(t.World(f.S, dr, KartRace.LipHeight + 0.03f));
                for (int q = 0; q < 4; q++) cols.Add(new Color(0.85f, 0.25f, 0.22f));
                tris.AddRange(new[] { e, e + 1, e + 2, e, e + 2, e + 3 });
                foreach (float d in new[] { dl, dr })
                {
                    int g = verts.Count;
                    verts.Add(t.World(f.S - f.Length, d, 0f));
                    verts.Add(t.World(f.S, d, 0f));
                    verts.Add(t.World(f.S, d, KartRace.LipHeight + 0.03f));
                    for (int q = 0; q < 3; q++) cols.Add(new Color(0.9f, 0.3f, 0.26f));
                    tris.AddRange(d == dl ? new[] { g, g + 1, g + 2 } : new[] { g, g + 2, g + 1 });
                }
            }
            if (verts.Count > 0) Add("Ramps", verts, cols, tris, _art.road);
        }

        void BuildPads()
        {
            var t = Track;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (var f in t.Features)
            {
                if (f.Kind != FeatureKind.Boost) continue;
                int steps = 5;
                for (int k = 0; k < steps; k++)
                {
                    float s0 = f.S + f.Length * k / steps, s1 = f.S + f.Length * (k + 1) / steps;
                    float dl = f.D + f.Width * 0.5f, dr = f.D - f.Width * 0.5f;
                    int b = verts.Count;
                    verts.Add(t.World(s0, dl, 0.05f));
                    verts.Add(t.World(s1, dl, 0.05f));
                    verts.Add(t.World(s1, dr, 0.05f));
                    verts.Add(t.World(s0, dr, 0.05f));
                    float v0 = f.Length * k / steps / 1.6f, v1 = f.Length * (k + 1) / steps / 1.6f;
                    uvs.Add(new Vector2(0f, v0));
                    uvs.Add(new Vector2(0f, v1));
                    uvs.Add(new Vector2(1f, v1));
                    uvs.Add(new Vector2(1f, v0));
                    tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                }
            }
            if (verts.Count == 0 || _art.pads == null) return;
            var mesh = new Mesh { name = "Pads" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            var go = new GameObject("Pads");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _art.pads;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void BuildBoxesAndStars()
        {
            var t = Track;
            _starMesh = MoveIt.Games.Fly.FlyArt.Star(0.22f, 0.48f);
            _meshes.Add(_starMesh);
            for (int i = 0; i < t.Features.Count; i++)
            {
                var f = t.Features[i];
                if (f.Kind == FeatureKind.Items)
                {
                    for (int b = 0; b < f.Count; b++)
                    {
                        var box = Prop("GiftBox", t.World(f.S, KartRace.BoxD(f, b), 1.2f), Quaternion.identity, 1f, batch: false);
                        if (box != null) _boxes.Add((box, i, b, b * 0.7f + i));
                    }
                }
                else if (f.Kind == FeatureKind.Stars && _art.stars != null)
                {
                    for (int k = 0; k < f.Count; k++)
                    {
                        var go = new GameObject($"Star {i}.{k}");
                        go.transform.SetParent(transform, false);
                        go.transform.position = t.World(KartRace.StarS(f, k), f.D, 1.0f);
                        go.transform.localScale = Vector3.one * 0.85f;
                        go.AddComponent<MeshFilter>().sharedMesh = _starMesh;
                        var r = go.AddComponent<MeshRenderer>();
                        r.sharedMaterial = _art.stars;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveShadows = false;
                        _stars.Add((go.transform, i, k));
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ the scenery

        void BuildScenery()
        {
            var t = Track;
            var root = new GameObject("Scenery").transform;
            root.SetParent(transform, false);
            (string name, float weight, float scale)[] near, far;
            switch (t.Theme)
            {
                case KartTheme.Coast:
                    near = new[] { ("PalmTree", 3f, 1f), ("PalmTree2", 2f, 1f), ("Bush", 2f, 1f), ("Flowers", 2f, 1f), ("Umbrella", 1.5f, 1f), ("TyreStack", 0.6f, 1f), ("RockA", 1f, 1f) };
                    far = new[] { ("PalmTree", 2f, 1.1f), ("HouseWhite", 2.5f, 1f), ("OliveTree", 2f, 1f), ("Cypress", 1f, 1f), ("RockB", 1.2f, 1.2f), ("RoundTree", 1f, 1f) };
                    break;
                case KartTheme.Village:
                    near = new[] { ("Cypress", 2.5f, 1f), ("OliveTree", 2f, 1f), ("Bush", 2f, 1f), ("Flowers", 2.5f, 1f), ("Fence", 1.5f, 1f), ("TyreStack", 0.6f, 1f) };
                    far = new[] { ("HouseStone", 3f, 1f), ("HouseWhite", 2f, 1f), ("Cypress", 2.5f, 1.1f), ("OliveTree", 2f, 1.1f), ("Pine", 1.5f, 1f), ("RoundTree", 1.2f, 1f) };
                    break;
                default:
                    near = new[] { ("Fern1", 3f, 1.6f), ("Fern2", 3f, 1.4f), ("Rock1", 1.5f, 1.4f), ("Cycad1", 2f, 1f), ("Bush", 1f, 1f), ("TyreStack", 0.6f, 1f) };
                    far = new[] { ("Cycad1", 2.5f, 1.2f), ("Cycad2", 2.5f, 1.2f), ("Rock2", 1.5f, 1.8f), ("Boulders", 1f, 1f), ("RoundTree", 1.5f, 1.1f), ("Pine", 1f, 1f) };
                    break;
            }
            // along both sides: near the barrier every ~10 m, further out every ~16 m
            for (float s = 6f; s < t.Length; s += 9f + (float)_rng.NextDouble() * 4f)
                foreach (int side in new[] { 1, -1 })
                {
                    if (NearFeature(s, 18f)) continue;
                    Place(root, near, s, side, t.Wall + 2.2f, t.Wall + 7f);
                    if (_rng.NextDouble() < 0.7) Place(root, far, s + 4f, side, t.Wall + 10f, t.Wall + 40f);
                }
            // pennants along the start straight, tyre stacks at the outside of the tight corners
            for (float s = -70f; s < 70f; s += 14f)
                foreach (int side in new[] { 1, -1 })
                    TryPlace(root, "Pennant", s, side * (t.Wall + 1.2f), 1f, Quaternion.LookRotation(Dir(s)));
            for (float s = 0f; s < t.Length; s += 6f)
            {
                float k = t.Curvature(s);
                if (Mathf.Abs(k) < 1f / 32f) continue;
                TryPlace(root, "TyreStack", s, -Mathf.Sign(k) * (t.Wall + 1.0f), 1f, Quaternion.identity);
            }
            // the grandstands by the start, the crowd in them
            BuildStands(root);
            // the theme's landmark inside the loop
            var middle = Middle();
            float ground = GroundAt(middle);
            switch (t.Theme)
            {
                case KartTheme.Coast:
                    // the lighthouse on the far end of the beach
                    PlaceAt(root, "Lighthouse", new Vector2(240f, -40f), 1.2f, 0f);
                    for (int i = 0; i < 10; i++)
                        PlaceAt(root, i % 2 == 0 ? "Umbrella" : "PalmTree", new Vector2(-60f + i * 26f + (float)_rng.NextDouble() * 6f, -34f - (float)_rng.NextDouble() * 6f),
                                1f, (float)_rng.NextDouble() * 360f);
                    break;
                case KartTheme.Village:
                    // the castle on the hill in the middle
                    var castle = new GameObject("Castle").transform;
                    castle.SetParent(root, false);
                    castle.position = new Vector3(middle.x, ground + 4f, middle.y);
                    PlaceUnder(castle, "CastleTower", new Vector3(0f, 0f, 0f), 1.4f, 0f);
                    PlaceUnder(castle, "CastleTower", new Vector3(26f, -1f, 10f), 1.1f, 40f);
                    PlaceUnder(castle, "CastleWall", new Vector3(13f, -1f, 5f), 1.4f, 21f);
                    PlaceUnder(castle, "CastleWall", new Vector3(-6f, -1f, 16f), 1.4f, 110f);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * 0.8f;
                        PlaceAt(root, i % 2 == 0 ? "HouseStone" : "HouseWhite", middle + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (38f + 6f * (i % 3)), 1f, a * Mathf.Rad2Deg + 90f);
                    }
                    break;
                default:
                    PlaceAt(root, "Volcano", middle + new Vector2(10f, 30f), 1.6f, 20f);
                    if (_art.dino != null)
                    {
                        // the dino by the start, cheering the karts past
                        var dino = Instantiate(_art.dino, root);
                        dino.name = "Dino";
                        dino.transform.position = t.World(8f, t.Wall + 4f) + Vector3.up * 0.0f;
                        dino.transform.rotation = Quaternion.LookRotation(-new Vector3(t.Left(8f).x, 0f, t.Left(8f).y));
                        dino.transform.localScale = Vector3.one * 1.6f;
                        Recolour(dino);
                        var anim = dino.GetComponentInChildren<Animator>();
                        if (anim != null && _art.dinoController != null)
                        {
                            anim.runtimeAnimatorController = _art.dinoController;
                            anim.Play("Cheer", 0, 0f);
                        }
                    }
                    if (_art.ptero != null)
                        for (int i = 0; i < 4; i++)
                        {
                            var p = Instantiate(_art.ptero, root);
                            p.name = $"Ptero {i}";
                            Recolour(p);
                            var anim = p.GetComponentInChildren<Animator>();
                            if (anim != null && _art.pteroController != null) anim.runtimeAnimatorController = _art.pteroController;
                            _flyers.Add((p.transform, new Vector3(middle.x + (float)_rng.NextDouble() * 60f - 30f, 0f, middle.y + (float)_rng.NextDouble() * 60f - 30f),
                                         60f + 30f * i, (i % 2 == 0 ? 1f : -1f) * (0.12f + 0.03f * i), 26f + 6f * i, i * 1.6f));
                        }
                    break;
            }
            // one draw call per material for the lot (they never move; static batching needs readable meshes)
            var still = new List<GameObject>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.sharedMesh.isReadable && mf.GetComponent<Animator>() == null && mf.GetComponentInParent<Animator>() == null)
                    still.Add(mf.gameObject);
            if (still.Count > 0) StaticBatchingUtility.Combine(still.ToArray(), root.gameObject);
        }

        void Recolour(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = _art.props;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        Vector2 Middle()
        {
            var sum = Vector2.zero;
            foreach (var p in _coarse) sum += p;
            return sum / _coarse.Length;
        }

        bool NearFeature(float s, float within)
        {
            if (Mathf.Abs(Track.Ahead(0f, s)) < 30f) return true;   // the start: the gantry, the stands
            foreach (var f in Track.Features)
                if (f.Kind == FeatureKind.Ramp && Mathf.Abs(Track.Ahead(f.S, s)) < within) return true;
            return false;
        }

        void Place(Transform root, (string name, float weight, float scale)[] choices, float s, int side, float from, float to)
        {
            float total = 0f;
            foreach (var c in choices) total += _props.ContainsKey(c.name) ? c.weight : 0f;
            if (total <= 0f) return;
            float r = (float)_rng.NextDouble() * total;
            foreach (var c in choices)
            {
                if (!_props.ContainsKey(c.name)) continue;
                if ((r -= c.weight) > 0f) continue;
                float d = side * Mathf.Lerp(from, to, (float)_rng.NextDouble());
                float scale = c.scale * (0.85f + 0.35f * (float)_rng.NextDouble());
                TryPlace(root, c.name, s, d, scale, Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f));
                return;
            }
        }

        /// <summary>A prop beside the road at (s, d), unless that spot is too close to another part of the track.</summary>
        void TryPlace(Transform root, string name, float s, float d, float scale, Quaternion rotation)
        {
            var p = Track.Point(s) + Track.Left(s) * d;
            if (Nearest(p, out _) < Track.Wall + 1.0f) return;
            var at = new Vector3(p.x, GroundAt(p) - 0.05f, p.y);
            var o = Prop(name, at, rotation, scale);
            if (o != null) o.SetParent(root, true);
        }

        void PlaceAt(Transform root, string name, Vector2 p, float scale, float yaw)
        {
            if (Nearest(p, out _) < Track.Wall + 4f) return;
            var o = Prop(name, new Vector3(p.x, GroundAt(p) - 0.1f, p.y), Quaternion.Euler(0f, yaw, 0f), scale);
            if (o != null) o.SetParent(root, true);
        }

        void PlaceUnder(Transform parent, string name, Vector3 local, float scale, float yaw)
        {
            var o = Prop(name, parent.TransformPoint(local), Quaternion.Euler(0f, yaw, 0f), scale);
            if (o != null) o.SetParent(parent, true);
        }

        Transform Prop(string name, Vector3 at, Quaternion rotation, float scale, bool batch = true)
        {
            if (!_props.TryGetValue(name, out var mesh) || mesh == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(at, rotation);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _art.props;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.isStatic = batch;
            return go.transform;
        }

        /// <summary>Two stands facing the start straight (outside its left and right barriers), fans on their steps.</summary>
        void BuildStands(Transform root)
        {
            if (!_props.ContainsKey("Grandstand")) return;
            var t = Track;
            var seats = new StringBuilder();
            foreach (var (s, side) in new[] { (-26f, 1), (-26f, -1), (30f, 1) })
            {
                var p = t.Point(s) + t.Left(s) * side * (t.Wall + 4.5f);
                if (Nearest(p, out _) < t.Wall + 3f) continue;
                var tan = t.Tangent(s);
                // the stand's front (its -y in Blender, +z here) faces the road
                var face = -new Vector3(t.Left(s).x, 0f, t.Left(s).y) * side;
                var rot = Quaternion.LookRotation(face, Vector3.up);
                float h = GroundAt(p);
                var stand = Prop("Grandstand", new Vector3(p.x, h, p.y), rot, 1f);
                if (stand == null) continue;
                stand.SetParent(root, true);
                // seats on the four steps (the props' layout: step i at depth 0.9 i, height 0.7 + 1.2 i)
                float yaw = Quaternion.LookRotation(face).eulerAngles.y;
                for (int step = 0; step < 4; step++)
                    for (float x = -11f; x <= 11f; x += 0.95f)
                    {
                        var local = new Vector3(x, 0.7f + step * 1.2f + 0.45f, -(step * 0.9f + 0.45f));
                        var w = stand.TransformPoint(new Vector3(local.x, local.y, local.z));
                        seats.Append(w.x.ToString("0.00", CultureInfo.InvariantCulture)).Append(' ')
                             .Append(w.y.ToString("0.00", CultureInfo.InvariantCulture)).Append(' ')
                             .Append(w.z.ToString("0.00", CultureInfo.InvariantCulture)).Append(' ')
                             .Append(yaw.ToString("0.0", CultureInfo.InvariantCulture)).Append('\n');
                    }
            }
            if (seats.Length > 0 && _art.crowd != null) Fans = Fans.Build(transform, seats.ToString(), _art.crowd, 11, 0.85f);
        }

        /// <summary>A ring of mountains round the horizon (soft in the haze): blue-grey with snow on the high ones; on the
        /// coast only on the land side.</summary>
        void BuildBackdrop()
        {
            var middle = Middle();
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            int peaks = 56;
            // just past the ground's edge (its corners), so the hills close the view where the ground ends
            float radius = 0f;
            foreach (var p in _coarse) radius = Mathf.Max(radius, (p - middle).magnitude);
            radius += 150f;
            var rock = Track.Theme == KartTheme.Dino ? new Color(0.48f, 0.5f, 0.58f) : new Color(0.52f, 0.6f, 0.68f);
            var green = Track.Theme == KartTheme.Coast ? new Color(0.48f, 0.62f, 0.5f) : new Color(0.45f, 0.6f, 0.45f);
            for (int i = 0; i < peaks; i++)
            {
                float a = 2f * Mathf.PI * i / peaks + (float)_rng.NextDouble() * 0.05f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var c = middle + dir * radius;
                if (Track.Theme == KartTheme.Coast && c.y < -60f) continue;   // the sea
                float h = 45f + (float)_rng.NextDouble() * 95f;
                float w = radius * Mathf.PI / peaks * 1.6f + (float)_rng.NextDouble() * 30f;
                var side = new Vector2(-dir.y, dir.x);
                var l = c - side * w;
                var r = c + side * w;
                var back = c + dir * 60f;
                int b = verts.Count;
                verts.Add(new Vector3(l.x, -10f, l.y));
                verts.Add(new Vector3(c.x, h, c.y));
                verts.Add(new Vector3(r.x, -10f, r.y));
                verts.Add(new Vector3(back.x, -10f, back.y));
                bool snow = h > 125f;
                cols.Add(green);
                cols.Add(snow ? Color.white : rock);
                cols.Add(green);
                cols.Add(rock);
                tris.AddRange(new[] { b + 2, b + 1, b, b + 2, b + 3, b + 1, b + 3, b, b + 1 });
            }
            if (verts.Count > 0) Add("Mountains", verts, cols, tris, _art.road);
        }

        // ------------------------------------------------------------------------------------------------ meshes

        void Add(string name, List<Vector3> verts, List<Color> cols, List<int> tris, Material material)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            _meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ------------------------------------------------------------------------------------------------ every frame

        /// <summary>The boxes and stars as the race has them (gone ones hidden), spinning; the pads' arrows run; the
        /// pterodactyls circle.</summary>
        public void Animate(KartRace race, float time)
        {
            foreach (var (t, feature, index, phase) in _boxes)
            {
                bool present = race == null || race.Present(feature, index);
                if (t.gameObject.activeSelf != present) t.gameObject.SetActive(present);
                if (!present) continue;
                var f = Track.Features[feature];
                t.position = Track.World(f.S, KartRace.BoxD(f, index), 1.2f + 0.15f * Mathf.Sin(time * 2.2f + phase));
                t.rotation = Quaternion.Euler(18f * Mathf.Sin(time * 1.3f + phase), time * 90f + phase * 40f, 12f);
            }
            foreach (var (t, feature, index) in _stars)
            {
                bool present = race == null || race.Present(feature, index);
                if (t.gameObject.activeSelf != present) t.gameObject.SetActive(present);
                if (present) t.rotation = Quaternion.Euler(0f, time * 160f + index * 30f, 0f);
            }
            if (_art.pads != null) _art.pads.mainTextureOffset = new Vector2(0f, -time * 1.6f);
            foreach (var (t, centre, radius, speed, height, phase) in _flyers)
            {
                float a = time * speed + phase;
                var p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                p.y = height + 3f * Mathf.Sin(time * 0.7f + phase);
                var ahead = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(speed);
                t.SetPositionAndRotation(p, Quaternion.LookRotation(ahead) * Quaternion.Euler(0f, 0f, -Mathf.Sign(speed) * 18f));
            }
        }
    }
}
