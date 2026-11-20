using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MoveIt.Games.Fairy
{
    [Serializable]
    public sealed class GloomArt
    {
        [Tooltip("art/fairy's gloom kit: bodies and parts, one mesh each")] public GameObject kit;
        [Tooltip("art/fairy's gloom_kit.json: where each body takes its eyes, mouth, horns, wings, tail")] public TextAsset anchors;
        [Tooltip("Tundu/Gloom")] public Material gloom;
        [Tooltip("Tundu/Bubble: the shield round the tree")] public Material bubble;
        [Tooltip("Tundu/Fx, alpha, the HUD sheet: the dots over the creatures")] public Material runes;
        [Tooltip("Tundu/Fx, added on, the soft dot")] public Material glow;
    }

    /// <summary>
    /// Zana!'s creatures as drawn (2026-11-15; the garden's since 2026-11-16): each species' mesh put together once from
    /// the gloom kit (art/fairy): its body, its eyes and mouth twice (grumpy and happy: Tundu/Gloom shows one or the other
    /// as it's purified), horns or ears, wings (both sides, flapping in the shader round their hinge), a tail; then every
    /// creature drawn with its colours, hopping toward the tree, its flash when a spell hits, its purifying (grey to its
    /// own bright colours, the happy face). Over the bigger ones a dot for each hit they can still take; the boss's crown;
    /// the shield as a soap bubble round the tree.
    /// </summary>
    public sealed class GloomViews : MonoBehaviour
    {
        [Serializable]
        sealed class Anchor
        {
            public string name;
            public float[] eye, mouth, horn, wing, tail, crown;
            public float eyeSize = 1f, mouthSize = 1f, hornSize = 1f, wingSize = 1f, tailSize = 1f, height = 1f;
        }

        [Serializable]
        sealed class AnchorsJson
        {
            public Anchor[] bodies;
        }

        GloomArt _art;
        readonly Dictionary<string, Mesh> _parts = new Dictionary<string, Mesh>();
        readonly Dictionary<string, Anchor> _anchors = new Dictionary<string, Anchor>();
        readonly Dictionary<int, Mesh> _species = new Dictionary<int, Mesh>();
        readonly List<Object> _made = new List<Object>();
        readonly Dictionary<int, float> _purify = new Dictionary<int, float>();
        MaterialPropertyBlock _block;
        Mesh _sphere, _bubbleMesh;
        Dictionary<string, Rect> _sheet;
        Mesh _crown;
        readonly Vector4[] _colours = new Vector4[8];
        float _shieldShown;

        // the dots over the creatures: one dynamic mesh of camera-facing quads
        Mesh _quads;
        readonly List<Vector3> _bv = new List<Vector3>(512);
        readonly List<Vector2> _bu = new List<Vector2>(512);
        readonly List<Color32> _bc = new List<Color32>(512);
        readonly List<int> _bt = new List<int>(768);

        /// <summary>The creatures are drawn this much bigger than their size in the rules.</summary>
        public const float DrawScale = 1.9f;

        public static GloomViews Create(GloomArt art, Dictionary<string, Rect> sheet)
        {
            var go = new GameObject("Gloom views");
            var v = go.AddComponent<GloomViews>();
            v._art = art;
            v._sheet = sheet;
            v._block = new MaterialPropertyBlock();
            v._sphere = v.Keep(FairyArt.Sphere(20, 14));
            v._bubbleMesh = v.Keep(FairyArt.Sphere(32, 22));
            v._quads = v.Keep(new Mesh { name = "Creature dots", indexFormat = IndexFormat.UInt32 });
            v._quads.MarkDynamic();
            if (art.kit != null)
                foreach (var f in art.kit.GetComponentsInChildren<MeshFilter>(true))
                    v._parts[f.name] = f.sharedMesh;
            if (art.anchors != null)
            {
                var data = JsonUtility.FromJson<AnchorsJson>(art.anchors.text);
                if (data?.bodies != null)
                    foreach (var a in data.bodies) v._anchors[a.name] = a;
            }
            v._crown = v.Part("Crown");
            // the creatures lit per vertex (2026-11-16: per pixel, a busy wave's creatures were ~40 % of a hot box's
            // frame); PerfLab gloompix=1: per pixel as before. A copy: the asset stays as it is
            if (art.gloom != null)
            {
                v._material = v.Keep(new Material(art.gloom) { name = "Gloom (lit per vertex)" });
                if (PerfSwitches.Get("gloompix") != "1") v._material.EnableKeyword("_VERTEX_LIT");
            }
            return v;
        }

        Material _material;

        T Keep<T>(T o) where T : Object
        {
            _made.Add(o);
            return o;
        }

        void OnDestroy()
        {
            foreach (var o in _made)
                if (o != null) Destroy(o);
        }

        Mesh Part(string name) => _parts.TryGetValue(name, out var m) ? m : null;

        static Vector3 V(float[] a, Vector3 fallback) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;

        /// <summary>A new run: its species' meshes are made again (<see cref="Warm"/>).</summary>
        public void NewRun()
        {
            foreach (var m in _species.Values)
                if (m != null && m != _sphere) Destroy(m);
            _species.Clear();
            _purify.Clear();
        }

        // ------------------------------------------------------------------------------------------------ a species' mesh

        /// <summary>Puts together the next of <paramref name="run"/>'s species not made yet (one a frame, before its creatures
        /// come: made on first sight, a species cost a hitch of tens of ms on the box); true while any were left.</summary>
        public bool Warm(GardenRun run)
        {
            foreach (var sp in run.AllSpecies)
                if (!_species.ContainsKey(sp.Id))
                {
                    SpeciesMesh(sp);
                    return true;
                }
            return false;
        }

        /// <summary>The mesh of <paramref name="sp"/>: its parts put together (made once a run).</summary>
        public Mesh SpeciesMesh(Species sp)
        {
            if (sp == null) return _sphere;
            if (_species.TryGetValue(sp.Id, out var made) && made != null) return made;
            var bodyName = sp.Body.ToString();
            var body = Part($"Body_{bodyName}");
            if (body == null)
            {
                _species[sp.Id] = _sphere;
                return _sphere;
            }
            _anchors.TryGetValue(bodyName, out var a);
            a ??= new Anchor();
            var parts = new List<(Mesh mesh, Matrix4x4 m, int kind, Vector3 hinge, int side)>
            {
                (body, Matrix4x4.identity, 0, Vector3.zero, 0),
            };
            var eye = V(a.eye, new Vector3(0f, 0.15f, 0.42f));
            var e1 = Part($"Eyes_{sp.Eyes}_gloom");
            var e2 = Part($"Eyes_{sp.Eyes}_happy");
            var em = Matrix4x4.TRS(eye, Quaternion.identity, Vector3.one * a.eyeSize);
            if (e1 != null) parts.Add((e1, em, 1, eye, 0));
            if (e2 != null) parts.Add((e2, em, 2, eye, 0));
            var mouth = V(a.mouth, new Vector3(0f, -0.12f, 0.44f));
            var m1 = Part($"Mouth_{sp.Mouth}_gloom");
            var m2 = Part($"Mouth_{sp.Mouth}_happy");
            var mm = Matrix4x4.TRS(mouth, Quaternion.identity, Vector3.one * a.mouthSize);
            if (m1 != null) parts.Add((m1, mm, 1, mouth, 0));
            if (m2 != null) parts.Add((m2, mm, 2, mouth, 0));
            if (sp.Horns >= 0)
            {
                var h = Part($"Horns_{sp.Horns}");
                if (h != null) parts.Add((h, Matrix4x4.TRS(V(a.horn, new Vector3(0f, 0.45f, 0f)), Quaternion.identity, Vector3.one * a.hornSize), 0, Vector3.zero, 0));
            }
            if (sp.Wings >= 0)
            {
                var w = Part($"Wing_{sp.Wings}");
                var hinge = V(a.wing, new Vector3(0.32f, 0.1f, -0.15f));
                if (w != null)
                {
                    // the kit's wing is one side's, reaching out along x: which way depends on the FBX import's mirror,
                    // so each copy is turned to reach away from the body on its own side
                    float reach = w.bounds.center.x < 0f ? -1f : 1f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var at = new Vector3(Mathf.Abs(hinge.x) * side, hinge.y, hinge.z);
                        parts.Add((w, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(side * reach * a.wingSize, a.wingSize, a.wingSize)), 3, at, side));
                    }
                }
            }
            if (sp.Tail >= 0)
            {
                var t = Part($"Tail_{sp.Tail}");
                if (t != null) parts.Add((t, Matrix4x4.TRS(V(a.tail, new Vector3(0f, -0.2f, -0.4f)), Quaternion.identity, Vector3.one * a.tailSize), 0, Vector3.zero, 0));
            }
            var mesh = Combine(parts, $"Species {sp.Id} ({bodyName})");
            _species[sp.Id] = mesh;
            return mesh;
        }

        /// <summary>The parts merged into one mesh: uv1 (the region, the baked shade) kept; uv2 = the hinge (wings) or the
        /// middle (faces); colour r = flap (how far out on a wing), g = side (0 left, 1 right), b = face (0.5 grumpy, 1
        /// happy, 0 always). Mirrored parts turned the right way out.</summary>
        static Mesh Combine(List<(Mesh mesh, Matrix4x4 m, int kind, Vector3 hinge, int side)> parts, string name)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var regions = new List<Vector2>();
            var hinges = new List<Vector2>();
            var colours = new List<Color>();
            var tris = new List<int>();
            var tmpV = new List<Vector3>();
            var tmpN = new List<Vector3>();
            var tmpR = new List<Vector2>();
            foreach (var (mesh, m, kind, hinge, side) in parts)
            {
                int start = verts.Count;
                mesh.GetVertices(tmpV);
                mesh.GetNormals(tmpN);
                mesh.GetUVs(1, tmpR);
                var nm = m.inverse.transpose;
                float reach = 0.01f;
                foreach (var p in tmpV) reach = Mathf.Max(reach, Mathf.Abs(p.x));
                for (int i = 0; i < tmpV.Count; i++)
                {
                    var p = m.MultiplyPoint3x4(tmpV[i]);
                    verts.Add(p);
                    normals.Add(i < tmpN.Count ? nm.MultiplyVector(tmpN[i]).normalized : Vector3.up);
                    regions.Add(i < tmpR.Count ? tmpR[i] : new Vector2(0f, 1f));
                    hinges.Add(new Vector2(hinge.x, hinge.y));
                    float flap = kind == 3 ? Mathf.Clamp01(Mathf.Abs(tmpV[i].x) / reach) : 0f;
                    float face = kind == 1 ? 0.5f : kind == 2 ? 1f : 0f;
                    colours.Add(new Color(flap, side > 0 ? 1f : 0f, face, 1f));
                }
                var t = mesh.triangles;
                bool flip = m.determinant < 0f;
                for (int i = 0; i < t.Length; i += 3)
                {
                    tris.Add(start + t[i]);
                    tris.Add(start + (flip ? t[i + 2] : t[i + 1]));
                    tris.Add(start + (flip ? t[i + 1] : t[i + 2]));
                }
            }
            var mesh2 = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh2.SetVertices(verts);
            mesh2.SetNormals(normals);
            mesh2.SetUVs(1, regions);
            mesh2.SetUVs(2, hinges);
            mesh2.SetColors(colours);
            mesh2.SetTriangles(tris, 0);
            mesh2.RecalculateBounds();
            return mesh2;
        }

        // ------------------------------------------------------------------------------------------------ colours

        static Color Hsv(float h, float s, float v) => Color.HSVToRGB(Mathf.Repeat(h, 1f), Mathf.Clamp01(s), Mathf.Clamp01(v));

        /// <summary>The species' own bright colours (purified); Tundu/Gloom greys them while it's gloomy. Regions: body,
        /// belly, accent, eye white, pupil, mouth, wing, cheek.</summary>
        void Colours(Species sp)
        {
            float h = sp?.Hue ?? 0.8f, a = sp?.Accent ?? 0.1f, s = sp?.Saturation ?? 0.6f;
            // the shader reads the table as linear colours (SetVectorArray passes them as they are)
            _colours[0] = (Vector4)Hsv(h, s * 0.85f, 1f).linear;
            _colours[1] = (Vector4)Hsv(h, s * 0.38f, 1f).linear;
            _colours[2] = (Vector4)Hsv(a, 0.75f, 1f).linear;
            _colours[3] = new Vector4(1f, 1f, 1f, 1f);
            _colours[4] = (Vector4)new Color(0.12f, 0.1f, 0.22f).linear;
            _colours[5] = (Vector4)new Color(0.55f, 0.18f, 0.3f).linear;
            _colours[6] = (Vector4)Hsv(a + 0.05f, 0.45f, 1f).linear;
            _colours[7] = (Vector4)new Color(1f, 0.62f, 0.72f).linear;
            for (int i = 0; i < 8; i++)
            {
                var c = new Color(_colours[i].x, _colours[i].y, _colours[i].z).linear;
                _colours[i] = new Vector4(c.r, c.g, c.b, 1f);
            }
        }

        // ------------------------------------------------------------------------------------------------ every frame

        /// <summary>Draws every creature of <paramref name="run"/> (garden metres are the world's), the dots over them toward
        /// <paramref name="cam"/>, and the shield round the tree while it's up.</summary>
        public void Draw(GardenRun run, Camera cam, float dt)
        {
            if (run == null) return;
            BeginQuads(cam);
            bool creatures = PerfSwitches.Get("creatures") != "0";  // PerfLab on the box: what the creatures cost
            bool frozen = run.FrozenTime > 0f;
            foreach (var c in run.Creatures)
            {
                if (c.State == CreatureState.Gone) continue;
                if (creatures) DrawCreature(c, frozen, dt);
                if (c.Alive && c.MaxHp > 1) AddDots(c);
            }
            EndQuads();
            DrawShield(run, dt);
        }

        void DrawCreature(Creature c, bool frozen, float dt)
        {
            if (_material == null) return;
            var mesh = SpeciesMesh(c.Species);
            // purifying eases in (the light has to reach it first)
            if (!_purify.TryGetValue(c.Id, out float p)) p = 0f;
            p = c.State == CreatureState.Purified ? Mathf.MoveTowards(p, 1f, dt * 2.2f) : 0f;
            _purify[c.Id] = p;
            Colours(c.Species);
            _block.Clear();
            _block.SetVectorArray("_Colors", _colours);
            _block.SetFloat("_Purify", p);
            _block.SetFloat("_Flash", frozen && c.Alive ? Mathf.Max(c.Flash, 0.35f) : c.Flash);
            _block.SetFloat("_Phase", c.Phase);
            _block.SetFloat("_FlapSpeed", frozen && c.Alive ? 0f : c.Flying ? 15f : 8f);
            _block.SetFloat("_Fade", c.State == CreatureState.Purified ? Mathf.Clamp01(2.2f - c.StateTime) : 1f);
            float size = c.Size * DrawScale;
            // hopping toward the tree (still while frozen or dizzy), a squash as it lands, a pop when hit
            float hop = 0f, squash = 1f;
            if (c.Alive && !c.Flying && !frozen && c.Stunned <= 0f)
            {
                float k = Mathf.Abs(Mathf.Sin(c.Age * (c.IsBoss ? 3.5f : 7f) + c.Phase));
                hop = k * 0.16f * size;
                squash = 1f + (0.12f - 0.24f * k) * (c.IsBoss ? 0.5f : 1f);
            }
            else if (c.Stunned > 0f) squash = 1f + 0.12f * Mathf.Sin(c.Stunned * 40f);
            float pop = 1f + 0.25f * c.Flash;
            var scale = new Vector3(size * pop / Mathf.Sqrt(squash), size * pop * squash, size * pop / Mathf.Sqrt(squash));
            // facing the camera (its front is its +z), turned toward the tree it walks to
            float turn = c.State == CreatureState.Purified ? 0f : c.Side * 32f;
            var rot = Quaternion.Euler(0f, turn + Mathf.Sin(c.Age * 0.9f + c.Phase) * 8f, Mathf.Sin(c.Age * 1.3f + c.Phase) * 4f) *
                      Quaternion.LookRotation(Vector3.back, Vector3.up);
            var at = c.Position + Vector3.up * (hop + (c.Flying ? 0f : size * 0.42f));
            var m = Matrix4x4.TRS(at, rot, scale);
            var rp = new RenderParams(_material) { matProps = _block, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMesh(rp, mesh, 0, m);
            if (c.IsBoss && _crown != null && _anchors.TryGetValue(c.Species != null ? c.Species.Body.ToString() : "", out var a))
                Graphics.RenderMesh(rp, _crown, 0, m * Matrix4x4.Translate(V(a.crown, new Vector3(0f, 0.6f, 0f))));
        }

        /// <summary>Over a creature that takes more than one hit: a dot for each hit left (a boss: a row of ten, its share
        /// left).</summary>
        void AddDots(Creature c)
        {
            if (_sheet == null || !_sheet.TryGetValue("dot", out var dot)) return;
            int n = c.IsBoss ? Mathf.CeilToInt(10f * c.Hp / c.MaxHp) : Mathf.Min(c.Hp, 8);
            int slots = c.IsBoss ? 10 : Mathf.Min(c.MaxHp, 8);
            float size = c.IsBoss ? 0.32f : 0.24f;
            float lift = c.Size * DrawScale * (c.Flying ? 0.65f : 1.1f) + 0.35f;
            var centre = c.Position + Vector3.up * lift;
            for (int i = 0; i < slots; i++)
            {
                float u = slots > 1 ? i / (float)(slots - 1) - 0.5f : 0f;
                var at = centre + _camRight * u * size * 1.25f * slots;
                Quad(at, size * 1.25f, dot, new Color(0.18f, 0.1f, 0.3f, 0.85f));
                if (i < n) Quad(at, size, dot, c.IsBoss ? new Color(1f, 0.82f, 0.3f, 1f) : new Color(1f, 0.55f, 0.85f, 1f));
            }
        }

        void DrawShield(GardenRun run, float dt)
        {
            float want = run.ShieldTime > 0f ? Mathf.Clamp01(run.ShieldTime / 0.6f) : 0f;
            _shieldShown = Mathf.MoveTowards(_shieldShown, want, dt * 4f);
            if (_shieldShown <= 0.01f || _art.bubble == null) return;
            _block.Clear();
            _block.SetFloat("_Fade", _shieldShown);
            var rp = new RenderParams(_art.bubble) { matProps = _block, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            float wobble = 1f + 0.03f * Mathf.Sin(Time.time * 4f);
            float r = GardenRun.ShieldRadius * (0.7f + 0.3f * _shieldShown);
            var size = new Vector3(r * wobble, (GardenRun.CanopyY + 2.6f) * 0.62f / wobble, r * 0.8f * wobble);
            Graphics.RenderMesh(rp, _bubbleMesh, 0, Matrix4x4.TRS(GardenRun.Tree + Vector3.up * size.y * 0.75f, Quaternion.identity, size));
        }

        // ------------------------------------------------------------------------------------------------ the dots

        Vector3 _camRight = Vector3.right, _camUp = Vector3.up;

        void BeginQuads(Camera cam)
        {
            _bv.Clear();
            _bu.Clear();
            _bc.Clear();
            _bt.Clear();
            if (cam == null) return;
            _camRight = cam.transform.right;
            _camUp = cam.transform.up;
        }

        void Quad(Vector3 c, float size, Rect uv, Color colour)
        {
            var r = _camRight * size * 0.5f;
            var u = _camUp * size * 0.5f;
            int i = _bv.Count;
            _bv.Add(c - r - u);
            _bv.Add(c - r + u);
            _bv.Add(c + r + u);
            _bv.Add(c + r - u);
            _bu.Add(new Vector2(uv.xMin, uv.yMin));
            _bu.Add(new Vector2(uv.xMin, uv.yMax));
            _bu.Add(new Vector2(uv.xMax, uv.yMax));
            _bu.Add(new Vector2(uv.xMax, uv.yMin));
            var c32 = (Color32)colour;
            for (int k = 0; k < 4; k++) _bc.Add(c32);
            _bt.Add(i);
            _bt.Add(i + 1);
            _bt.Add(i + 2);
            _bt.Add(i);
            _bt.Add(i + 2);
            _bt.Add(i + 3);
        }

        void EndQuads()
        {
            _quads.Clear();
            if (_bv.Count == 0 || _art.runes == null) return;
            _quads.SetVertices(_bv);
            _quads.SetUVs(0, _bu);
            _quads.SetColors(_bc);
            _quads.SetTriangles(_bt, 0, false);
            _quads.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            var rp = new RenderParams(_art.runes) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e6f) };
            Graphics.RenderMesh(rp, _quads, 0, Matrix4x4.identity);
        }
    }
}
