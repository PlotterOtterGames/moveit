using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games
{
    /// <summary>
    /// One of Tundu's chibi characters (art/chibi: a big round head with bean eyes, a small body, mitten hands, big
    /// sneakers; about 1.52 m tall at scale 1, feet at the object's origin, facing +Z), dressed in a <see cref="ChibiLook"/>:
    /// its pieces merged into one skinned mesh (one draw call; colours per region with a property block, Tundu/Athlete).
    /// Games pose it themselves through the bones: <see cref="AimArm"/> points an arm, <see cref="SwingLeg"/> swings a leg,
    /// <see cref="TurnHead"/> turns the head, all from the rest pose (arms hanging a little out). Faces: <see cref="SetFace"/>
    /// (a smile, a grin, happy closed eyes, sad), blinking by itself; <see cref="Flash"/>.
    /// The model and its material are loaded from Resources/Chibi (made by art/chibi/build.py --export).
    /// </summary>
    public sealed class Chibi : MonoBehaviour
    {
        public enum Face { Smile, Grin, Happy, Sad }

        static GameObject _model, _lowModel;
        static bool _lowLoaded;
        // other figures with the chibis' bones and regions (Resources/Chibi/<name>: Zana!'s tall fairies, "Heroine")
        static readonly Dictionary<string, GameObject> Models = new Dictionary<string, GameObject>();
        static Material _material;
        static readonly Dictionary<string, Mesh> Merged = new Dictionary<string, Mesh>();

        ChibiLook _look;
        SkinnedMeshRenderer _renderer, _lowRenderer;
        bool _low;
        string[] _pieces;                       // the look's pieces, outermost first
        Dictionary<string, Transform> _boneMap; // this character's bones by name
        MaterialPropertyBlock _block;
        readonly Vector4[] _regions = new Vector4[24];

        Transform _hips, _spine, _head, _eyes, _eyesHappy, _mouth, _mouthOpen, _mouthSad;
        readonly Transform[] _upper = new Transform[2], _fore = new Transform[2], _hand = new Transform[2];
        readonly Transform[] _thigh = new Transform[2], _shin = new Transform[2], _foot = new Transform[2];
        // the rest pose, in this object's frame: each bone's rotation, and the limbs' directions
        readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();
        readonly Vector3[] _armRest = new Vector3[2];
        readonly Quaternion[] _upperRest = new Quaternion[2], _thighRest = new Quaternion[2], _shinRest = new Quaternion[2], _footRest = new Quaternion[2];
        float _thighLength, _shinLength;
        Quaternion _headRest, _spineRest = Quaternion.identity, _hipsRest = Quaternion.identity;
        float _armLength;
        Material[] _override;

        Face _face = Face.Smile;
        float _nextBlink, _blinkUntil;
        Color _flash;
        float _flashAmount;
        // the property block goes to the renderers only when it changed (it went every frame: ~2 KB of uniforms per
        // character and frame through the driver)
        bool _blockDirty = true;
        float _appliedFlash = -1f;

        // The materials (2026-11-13, measured on the hot box in Volejboll!'s split screen: the chibis' cost was their
        // pixels, not their skinning). The full model: [0] the open pieces (clothes: two-sided, their insides show), [1]
        // the closed ones (head, hair, shoes, most hats: back faces culled, every hidden back face was shaded before), lit
        // per pixel. The low-detail one (small on the screen): one submesh, two-sided, lit per vertex (a second submesh
        // would cost a draw call for ~2 %, and Gara!'s split screen is short of CPU). PerfLab on the box:
        // chibilit=pixel|vertex lights all of them one way, chibicull=off|all culls nothing or everything. Switches read
        // once a frame for all of them.
        static Material[] _pixelPair, _vertexPair, _pixelOne, _vertexOne;
        static int _switchFrame = -1;
        static string _litSwitch = "", _cullSwitch = "";
        Material[] _appliedFull, _appliedLow;

        public ChibiLook Look => _look;
        /// <summary>The renderer drawing it now (the low-detail one while <see cref="Low"/>).</summary>
        public Renderer Renderer => _low && _lowRenderer != null ? _lowRenderer : _renderer;

        /// <summary>
        /// Draws the low-detail copy instead: the same pieces from Resources/Chibi/Chibi_Low (art/chibi/build.py --low:
        /// about a third of the triangles, 13k instead of 34k for a dressed character), skinned to the same bones, so
        /// posing, faces and colours carry on. For characters small on the screen: Tenis!'s far team, and all four in the
        /// two players' split screen on the TV box (each drawn twice). Made the first time it's asked for; without that
        /// model the full one stays.
        /// </summary>
        public bool Low
        {
            get => _low;
            set
            {
                if (value == _low) return;
                if (value && _lowRenderer == null && !BuildLow()) return;
                _low = value;
                if (_renderer != null) _renderer.enabled = !_low;
                if (_lowRenderer != null) _lowRenderer.enabled = _low;
            }
        }

        /// <summary>Shoulder to the middle of the mitten, metres at this object's scale.</summary>
        public float ArmLength => _armLength * transform.lossyScale.x;

        public static bool Ready
        {
            get
            {
                if (_model == null) _model = Resources.Load<GameObject>("Chibi/Chibi");
                if (_material == null) _material = Resources.Load<Material>("Chibi/ChibiMaterial");
                return _model != null && _material != null;
            }
        }

        /// <summary>The figure <paramref name="model"/> draws with: the chibi, or another made with its bones and regions
        /// (Resources/Chibi/&lt;model&gt;; art/fairy/heroine.py's "Heroine").</summary>
        static GameObject ModelOf(string model)
        {
            if (string.IsNullOrEmpty(model) || model == ChibiLook.ChibiModel) return _model;
            if (!Models.TryGetValue(model, out var m))
            {
                m = Resources.Load<GameObject>($"Chibi/{model}");
                if (m == null) Debug.LogError($"[Chibi] Resources/Chibi/{model} is missing");
                Models[model] = m;
            }
            return m;
        }

        /// <summary>A new chibi in <paramref name="look"/> under <paramref name="parent"/>, standing in its rest pose.</summary>
        public static Chibi Create(string name, ChibiLook look, Transform parent)
        {
            var model = Ready ? ModelOf(look.Model) : null;
            if (model == null)
            {
                Debug.LogError("[Chibi] Resources/Chibi/Chibi (the model) or ChibiMaterial is missing: run art/chibi/build.py --export");
                var empty = new GameObject(name).AddComponent<Chibi>();
                empty.transform.SetParent(parent, false);
                return empty;
            }
            var go = Instantiate(model, parent, false);
            go.name = name;
            var c = go.AddComponent<Chibi>();
            c.Build(look);
            return c;
        }

        void Build(ChibiLook look)
        {
            _look = look;
            FindBones();
            var all = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // the bones by name, from the pieces' own bone lists (a piece object may share a bone's name: "Head")
            _boneMap = new Dictionary<string, Transform>();
            foreach (var smr in all)
                foreach (var b in smr.bones)
                    if (b != null) _boneMap[b.name] = b;
            var parts = new List<SkinnedMeshRenderer>();
            // outermost first: drawn in that order, the depth test skips what they cover
            var pieces = look.Pieces();
            System.Array.Reverse(pieces);
            _pieces = pieces;
            foreach (string piece in pieces)
            {
                var smr = System.Array.Find(all, r => r.name == piece);
                if (smr != null) parts.Add(smr);
                else Debug.LogWarning($"[Chibi] No piece {piece}");
            }
            string key = (look.IsChibi ? "" : look.Model + ":") + string.Join("+", pieces);
            if (!Merged.TryGetValue(key, out var mesh) || mesh == null)
            {
                mesh = Merge(parts, Same, key, split: true);
                Merged[key] = mesh;
            }
            _renderer = NewRenderer("Mesh", mesh, BonesOf(parts, Same));
            // off at once: Destroy waits for the frame's end, and a camera drawing this frame (Superboks!'s portraits) drew
            // every piece of the wardrobe, uncoloured, over the merged figure
            foreach (var smr in all)
            {
                smr.gameObject.SetActive(false);
                Destroy(smr.gameObject);
            }
            var animator = GetComponent<Animator>();
            if (animator != null) Destroy(animator);
            _block = new MaterialPropertyBlock();
            Recolour(look);

            // the rest pose in this object's frame
            foreach (var t in GetComponentsInChildren<Transform>(true)) _rest[t] = t.localRotation;
            var inv = Quaternion.Inverse(transform.rotation);
            for (int s = 0; s < 2; s++)
            {
                if (_upper[s] == null) continue;
                _armRest[s] = transform.InverseTransformDirection(_hand[s].position - _upper[s].position).normalized;
                _upperRest[s] = inv * _upper[s].rotation;
                if (_thigh[s] != null) _thighRest[s] = inv * _thigh[s].rotation;
                if (_shin[s] != null) _shinRest[s] = inv * _shin[s].rotation;
                if (_foot[s] != null) _footRest[s] = inv * _foot[s].rotation;
                if (_thigh[s] != null && _shin[s] != null && _foot[s] != null)
                {
                    float scale = Mathf.Max(1e-4f, transform.lossyScale.x);
                    _thighLength = Vector3.Distance(_thigh[s].position, _shin[s].position) / scale;
                    _shinLength = Vector3.Distance(_shin[s].position, _foot[s].position) / scale;
                }
                _armLength = Vector3.Distance(_upper[s].position, HandCenter(s)) / Mathf.Max(1e-4f, transform.lossyScale.x);
            }
            if (_head != null) _headRest = inv * _head.rotation;
            if (_spine != null) _spineRest = inv * _spine.rotation;
            if (_hips != null) _hipsRest = inv * _hips.rotation;
            SetFace(Face.Smile);
            _nextBlink = Time.time + Random.Range(1f, 4f);
        }

        SkinnedMeshRenderer NewRenderer(string name, Mesh mesh, Transform[] bones)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            var r = holder.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = mesh;
            r.bones = bones;
            r.rootBone = _hips;
            // the full model per pixel with its closed pieces culled, the low one per vertex (ApplySwitches keeps them so)
            r.sharedMaterials = mesh.subMeshCount > 1 ? Materials(false, true) : Materials(true, false);
            r.localBounds = _look != null && !_look.IsChibi
                ? new Bounds(new Vector3(0f, 0.4f, 0f), new Vector3(2.4f, 3.4f, 2.4f))   // a tall figure round her hips
                : new Bounds(new Vector3(0f, 0.3f, 0f), new Vector3(2.2f, 2.4f, 2.2f));
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            r.updateWhenOffscreen = false;
            return r;
        }

        /// <summary>The low-detail copy (see <see cref="Low"/>): the same pieces from Chibi_Low, their bones found by name
        /// among this character's.</summary>
        bool BuildLow()
        {
            if (_look != null && !_look.IsChibi) return false;   // only the chibi has a low-detail copy
            if (!_lowLoaded)
            {
                _lowLoaded = true;
                _lowModel = Resources.Load<GameObject>("Chibi/Chibi_Low");
                if (_lowModel == null) Debug.LogWarning("[Chibi] Resources/Chibi/Chibi_Low is missing (art/chibi/build.py --low): full detail only");
            }
            if (_lowModel == null || _renderer == null || _pieces == null) return false;
            var all = _lowModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var parts = new List<SkinnedMeshRenderer>();
            foreach (string piece in _pieces)
            {
                var smr = System.Array.Find(all, r => r.name == piece);
                if (smr != null) parts.Add(smr);
            }
            Transform Resolve(Transform t) => t != null && _boneMap.TryGetValue(t.name, out var b) ? b : null;
            foreach (var smr in parts)
                foreach (var b in smr.bones)
                    if (Resolve(b) == null)
                    {
                        Debug.LogWarning($"[Chibi] Chibi_Low's bone {(b != null ? b.name : "null")} isn't in Chibi: full detail only");
                        return false;
                    }
            string key = "low:" + string.Join("+", _pieces);
            if (!Merged.TryGetValue(key, out var mesh) || mesh == null)
            {
                mesh = Merge(parts, Resolve, key, split: false);
                Merged[key] = mesh;
            }
            _lowRenderer = NewRenderer("Mesh (low)", mesh, BonesOf(parts, Resolve));
            _lowRenderer.enabled = _low;
            ApplyBlock();
            return true;
        }

        static Transform Same(Transform t) => t;

        void FindBones()
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) map[t.name] = t;
            Transform B(string n) => map.TryGetValue(n, out var t) ? t : map.TryGetValue(n.Replace('.', '_'), out t) ? t : null;
            _hips = B("Hips");
            _spine = B("Spine");
            _head = B("Head");
            _eyes = B("Eyes");
            _eyesHappy = B("EyesHappy");
            _mouth = B("Mouth");
            _mouthOpen = B("MouthOpen");
            _mouthSad = B("MouthSad");
            for (int s = 0; s < 2; s++)
            {
                string side = s == 0 ? "L" : "R";
                _upper[s] = B($"UpperArm.{side}");
                _fore[s] = B($"Forearm.{side}");
                _hand[s] = B($"Hand.{side}");
                _thigh[s] = B($"Thigh.{side}");
                _shin[s] = B($"Shin.{side}");
                _foot[s] = B($"Foot.{side}");
            }
        }

        /// <summary>The pieces' bones (this character's: <paramref name="resolve"/> maps a piece's bone to it), each once,
        /// in the order the pieces name them.</summary>
        static Transform[] BonesOf(List<SkinnedMeshRenderer> parts, System.Func<Transform, Transform> resolve)
        {
            var list = new List<Transform>();
            foreach (var smr in parts)
                foreach (var b in smr.bones)
                {
                    var t = resolve(b);
                    if (!list.Contains(t)) list.Add(t);
                }
            return list.ToArray();
        }

        /// <summary>The pieces as one mesh: vertices, normals, both UV channels (the second = region, occlusion),
        /// triangles (with <paramref name="split"/> submesh 0 the open pieces, 1 the closed ones: <see cref="IsClosed"/>;
        /// else one) and skin weights, the bones in <see cref="BonesOf"/> order.</summary>
        static Mesh Merge(List<SkinnedMeshRenderer> parts, System.Func<Transform, Transform> resolve, string key, bool split)
        {
            var bones = BonesOf(parts, resolve);
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < bones.Length; i++) index[bones[i]] = i;
            var bind = new Matrix4x4[bones.Length];
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv1 = new List<Vector2>();
            var tris = new List<int>();
            var closedTris = new List<int>();
            var perVertex = new List<byte>();
            var weights = new List<BoneWeight1>();
            var tv = new List<Vector3>();
            var tn = new List<Vector3>();
            var tu = new List<Vector2>();
            var tt = new List<int>();
            foreach (var smr in parts)
            {
                var m = smr.sharedMesh;
                var map = new int[smr.bones.Length];
                for (int i = 0; i < smr.bones.Length; i++)
                {
                    map[i] = index[resolve(smr.bones[i])];
                    bind[map[i]] = m.bindposes[i];
                }
                int start = verts.Count;
                m.GetVertices(tv);
                verts.AddRange(tv);
                m.GetNormals(tn);
                normals.AddRange(tn);
                m.GetUVs(1, tu);
                if (tu.Count != tv.Count) tu = new List<Vector2>(new Vector2[tv.Count]);
                uv1.AddRange(tu);
                var into = split && IsClosed(m) ? closedTris : tris;
                for (int sub = 0; sub < m.subMeshCount; sub++)
                {
                    m.GetTriangles(tt, sub);
                    foreach (int i in tt) into.Add(start + i);
                }
                var counts = m.GetBonesPerVertex();
                var all = m.GetAllBoneWeights();
                perVertex.AddRange(counts);
                foreach (var w in all) weights.Add(new BoneWeight1 { boneIndex = map[w.boneIndex], weight = w.weight });
            }
            var mesh = new Mesh { name = $"Chibi {key}" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(1, uv1);
            mesh.subMeshCount = split ? 2 : 1;
            mesh.SetTriangles(tris, 0);
            if (split) mesh.SetTriangles(closedTris, 1);
            using (var c = new NativeArray<byte>(perVertex.ToArray(), Allocator.Temp))
            using (var w = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp))
                mesh.SetBoneWeights(c, w);
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Its materials and colours applied now rather than in its LateUpdate: for a camera that draws it in the
        /// frame it was made (Superboks!'s portraits drew new opponents black: their colours weren't set yet).</summary>
        public void ApplyNow()
        {
            ReadSwitches();
            ApplySwitches();
            ApplyBlock();
        }

        /// <summary>New colours (the same pieces).</summary>
        public void Recolour(ChibiLook look)
        {
            _look = look;
            look.Fill(_regions);
            ApplyBlock();
        }

        void ApplyBlock()
        {
            if (_renderer == null) return;
            _block.SetVectorArray("_Regions", _regions);
            _block.SetColor("_FlashColor", new Color(_flash.r, _flash.g, _flash.b, _flashAmount));
            if (_rim.HasValue) _block.SetColor("_RimColor", _rim.Value);
            _renderer.SetPropertyBlock(_block);
            if (_lowRenderer != null) _lowRenderer.SetPropertyBlock(_block);
            _blockDirty = false;
            _appliedFlash = _flashAmount;
        }

        // ---------------------------------------------------------------- the materials

        /// <summary>A renderer's materials, lit per pixel or per vertex: for a mesh with the closed pieces in a second
        /// submesh (<paramref name="split"/>) the open pieces' and the closed ones' (back faces culled), else one.</summary>
        static Material[] Materials(bool vertex, bool split)
        {
            if (_pixelPair == null)
            {
                var closed = new Material(_material) { name = "ChibiMaterial (closed)" };
                closed.SetFloat("_Cull", (float)CullMode.Back);
                var open = new Material(_material) { name = "ChibiMaterial (lit per vertex)" };
                open.EnableKeyword("_VERTEX_LIT");
                var closedVertex = new Material(closed) { name = "ChibiMaterial (closed, lit per vertex)" };
                closedVertex.EnableKeyword("_VERTEX_LIT");
                _pixelPair = new[] { _material, closed };
                _vertexPair = new[] { open, closedVertex };
                _pixelOne = new[] { _material };
                _vertexOne = new[] { open };
            }
            return split ? (vertex ? _vertexPair : _pixelPair) : (vertex ? _vertexOne : _pixelOne);
        }

        static void ReadSwitches()
        {
            if (_switchFrame == Time.frameCount) return;
            _switchFrame = Time.frameCount;
            _litSwitch = PerfSwitches.Get("chibilit");
            string cull = PerfSwitches.Get("chibicull");
            if (cull == _cullSwitch || _pixelPair == null) return;
            _cullSwitch = cull;
            float open = cull == "all" ? (float)CullMode.Back : (float)CullMode.Off;
            float closed = cull == "off" ? (float)CullMode.Off : (float)CullMode.Back;
            foreach (var pair in new[] { _pixelPair, _vertexPair })
            {
                pair[0].SetFloat("_Cull", open);
                pair[1].SetFloat("_Cull", closed);
            }
        }

        /// <summary>The full model lit per vertex too (Superboks!'s boxers, big on the screen: their pixels lit per pixel
        /// were 12-15 % of the box's frame, 2026-11-19); PerfLab chibilit=pixel lights it per pixel again.</summary>
        public bool VertexLit
        {
            get => _vertexLit;
            set
            {
                _vertexLit = value;
                ApplySwitches();
            }
        }

        bool _vertexLit;

        void ApplySwitches()
        {
            var full = Materials(_litSwitch == "vertex" || _vertexLit && _litSwitch != "pixel", true);
            if (full != _appliedFull && _renderer != null)
            {
                _appliedFull = full;
                _renderer.sharedMaterials = full;
            }
            var low = _override ?? Materials(_litSwitch != "pixel", false);
            if (low != _appliedLow && _lowRenderer != null)
            {
                _appliedLow = low;
                _lowRenderer.sharedMaterials = low;
            }
        }

        static readonly Dictionary<Mesh, bool> ClosedMeshes = new Dictionary<Mesh, bool>();

        /// <summary>
        /// Whether a piece is a closed surface (every edge between two triangles, once the vertices split for the regions
        /// and normals are joined by position): its back faces never show, so they can be culled. Clothes have open hems
        /// and necks (two-sided, the inside shows); the head, hair, shoes and most hats are closed (2026-11-13: in the low
        /// and the full model alike).
        /// </summary>
        static bool IsClosed(Mesh mesh)
        {
            if (ClosedMeshes.TryGetValue(mesh, out bool known)) return known;
            var verts = new List<Vector3>();
            mesh.GetVertices(verts);
            var weld = new int[verts.Count];
            var byPosition = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < verts.Count; i++)
            {
                var key = Vector3Int.RoundToInt(verts[i] * 1e5f);   // 0.01 mm
                if (!byPosition.TryGetValue(key, out int first)) byPosition[key] = first = i;
                weld[i] = first;
            }
            var edges = new Dictionary<long, int>();
            var tris = new List<int>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                mesh.GetTriangles(tris, sub);
                for (int t = 0; t + 2 < tris.Count; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = weld[tris[t + e]], b = weld[tris[t + (e + 1) % 3]];
                        if (a == b) continue;
                        long edge = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        edges.TryGetValue(edge, out int n);
                        edges[edge] = n + 1;
                    }
            }
            bool closed = edges.Count > 0;
            foreach (var n in edges.Values)
                if (n == 1)
                {
                    closed = false;
                    break;
                }
            ClosedMeshes[mesh] = closed;
            return closed;
        }

        // ---------------------------------------------------------------- posing (games call these every frame)

        /// <summary>Back to the rest pose: arms hanging a little out, legs straight, the head forward.</summary>
        public void ResetPose()
        {
            foreach (var kv in _rest)
                if (kv.Key != null && kv.Key != _eyes && kv.Key != _eyesHappy && kv.Key != _mouth && kv.Key != _mouthOpen && kv.Key != _mouthSad)
                    kv.Key.localRotation = kv.Value;
        }

        /// <summary>The shoulder joint of an arm (0 = the character's left, 1 = its right), world.</summary>
        public Vector3 Shoulder(int side) => _upper[side] != null ? _upper[side].position : transform.position;

        /// <summary>The middle of a mitten (where it holds a handle), world.</summary>
        public Vector3 HandCenter(int side)
        {
            if (_hand[side] == null) return transform.position;
            var d = (_hand[side].position - _fore[side].position).normalized;
            return _hand[side].position + d * (0.058f * transform.lossyScale.x);
        }

        /// <summary>Points an arm (straight; 0 = the character's left, 1 = its right) along <paramref name="direction"/>
        /// in this object's frame (+Z the way it faces, +X its right, +Y up).</summary>
        public void AimArm(int side, Vector3 direction)
        {
            if (_upper[side] == null || direction.sqrMagnitude < 1e-8f) return;
            var turn = Quaternion.FromToRotation(_armRest[side], direction.normalized);
            _upper[side].rotation = transform.rotation * turn * _upperRest[side];
            if (_fore[side] != null) _fore[side].localRotation = _rest[_fore[side]];
            if (_hand[side] != null) _hand[side].localRotation = _rest[_hand[side]];
        }

        /// <summary>Swings a leg forward (+) or back (-) round the hip, degrees.</summary>
        public void SwingLeg(int side, float degrees)
        {
            if (_thigh[side] == null) return;
            _thigh[side].rotation = transform.rotation * Quaternion.Euler(-degrees, 0f, 0f) * _thighRest[side];
        }

        /// <summary>
        /// A bent leg (a crouch, a landing, a dig): the thigh swung forward (+) round the hip by <paramref name="thigh"/>
        /// degrees and the knee bent back by <paramref name="knee"/> (0, 0 = straight), the foot kept flat. The body sinks by
        /// <see cref="LegDrop"/> to keep the foot on the ground (the game lowers the character).
        /// </summary>
        public void BendLeg(int side, float thigh, float knee)
        {
            if (_thigh[side] == null) return;
            _thigh[side].rotation = transform.rotation * Quaternion.Euler(-thigh, 0f, 0f) * _thighRest[side];
            if (_shin[side] != null) _shin[side].rotation = transform.rotation * Quaternion.Euler(knee - thigh, 0f, 0f) * _shinRest[side];
            if (_foot[side] != null) _foot[side].rotation = transform.rotation * _footRest[side];
        }

        /// <summary>
        /// A leg in the air (a fairy's flight, a leap): like <see cref="BendLeg"/>, the whole leg also turned out (+) or in
        /// (-) round the hip by <paramref name="outward"/> degrees, and the foot pointed down by <paramref name="point"/>
        /// degrees from square to the shin (0 = square, ~50 = a pointed toe). Side 0 is the left.
        /// </summary>
        public void FlyLeg(int side, float thigh, float knee, float outward, float point)
        {
            if (_thigh[side] == null) return;
            var turn = transform.rotation * Quaternion.Euler(0f, 0f, side == 0 ? -outward : outward);
            _thigh[side].rotation = turn * Quaternion.Euler(-thigh, 0f, 0f) * _thighRest[side];
            if (_shin[side] != null) _shin[side].rotation = turn * Quaternion.Euler(knee - thigh, 0f, 0f) * _shinRest[side];
            if (_foot[side] != null) _foot[side].rotation = turn * Quaternion.Euler(knee - thigh + point, 0f, 0f) * _footRest[side];
        }

        /// <summary>How much lower the hips are (metres at this object's scale) with a leg bent like
        /// <see cref="BendLeg"/>.</summary>
        public float LegDrop(float thigh, float knee) =>
            (_thighLength * (1f - Mathf.Cos(thigh * Mathf.Deg2Rad)) + _shinLength * (1f - Mathf.Cos((thigh - knee) * Mathf.Deg2Rad))) *
            transform.lossyScale.x;

        /// <summary>
        /// Bends an arm (0 = the character's left, 1 = its right) so the middle of its mitten reaches <paramref name="target"/>
        /// (world; as near as the arm reaches), the elbow bending toward <paramref name="pole"/> (a world direction from the
        /// shoulder): two-bone IK on the upper arm and the forearm from the rest pose (Superboks!'s guard and punches).
        /// </summary>
        public void ReachArm(int side, Vector3 target, Vector3 pole)
        {
            if (_upper[side] == null || _fore[side] == null || _hand[side] == null) return;
            _upper[side].localRotation = _rest[_upper[side]];
            _fore[side].localRotation = _rest[_fore[side]];
            _hand[side].localRotation = _rest[_hand[side]];
            Vector3 s = _upper[side].position, e = _fore[side].position, h = HandCenter(side);
            float a = Vector3.Distance(s, e), b = Vector3.Distance(e, h);
            var to = target - s;
            var dir = to.sqrMagnitude > 1e-8f ? to.normalized : transform.forward;
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 1e-3f, (a + b) * 0.999f);
            float x = (a * a - b * b + d * d) / (2f * d);
            float y = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            var bend = pole - dir * Vector3.Dot(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = -transform.up;
            var elbow = s + dir * x + bend.normalized * y;
            _upper[side].rotation = Quaternion.FromToRotation(e - s, elbow - s) * _upper[side].rotation;
            Vector3 e2 = _fore[side].position, h2 = HandCenter(side);
            _fore[side].rotation = Quaternion.FromToRotation(h2 - e2, s + dir * d - e2) * _fore[side].rotation;
        }

        /// <summary>The hand bone of an arm (0 = the left): things held follow it.</summary>
        public Transform Hand(int side) => _hand[side];

        /// <summary>The way the forearm points (world, from the elbow to the hand).</summary>
        public Vector3 ForearmDirection(int side) =>
            _hand[side] != null && _fore[side] != null ? (_hand[side].position - _fore[side].position).normalized : transform.forward;

        /// <summary>Turns the upper body round the spine from the rest pose (degrees: yaw + = toward its right, pitch + =
        /// bowing forward, roll + = leaning to its right).</summary>
        public void TurnSpine(float yaw, float pitch = 0f, float roll = 0f)
        {
            if (_spine == null) return;
            _spine.rotation = transform.rotation * Quaternion.Euler(pitch, yaw, -roll) * _spineRest;
        }

        /// <summary>Turns the whole body round the hips from the rest pose (the legs too): degrees as
        /// <see cref="TurnSpine"/>.</summary>
        public void TurnHips(float yaw, float pitch = 0f, float roll = 0f)
        {
            if (_hips == null) return;
            _hips.rotation = transform.rotation * Quaternion.Euler(pitch, yaw, -roll) * _hipsRest;
        }

        /// <summary>
        /// Materials drawn instead of the chibi's own, on its low-detail copy (one submesh: every material draws all of it):
        /// Superboks!'s see-through hero is a depth material and a colour one (Tundu/AthleteGhost), its colours from the
        /// same property block. Setting it turns <see cref="Low"/> on; null = its own again.
        /// </summary>
        public Material[] OverrideMaterials
        {
            get => _override;
            set
            {
                _override = value;
                if (value != null) Low = true;
                _appliedLow = null;
                ApplySwitches();
            }
        }

        /// <summary>The head's middle (world): 0.295 m (at scale 1) up the neck from the head bone's root, wherever the
        /// spine has bent it (Superboks!'s effects and camera aim at it).</summary>
        public Vector3 HeadPosition
        {
            get
            {
                float scale = transform.lossyScale.y;
                if (_head == null) return transform.position + transform.up * 1.175f * scale;
                var along = _spine != null ? (_head.position - _spine.position).normalized : transform.up;
                return _head.position + along * 0.295f * scale;
            }
        }

        /// <summary>Turns the head (degrees: yaw + = toward its right, pitch + = down).</summary>
        public void TurnHead(float yaw, float pitch = 0f)
        {
            if (_head == null) return;
            _head.rotation = transform.rotation * Quaternion.Euler(pitch, yaw, 0f) * _headRest;
        }

        // ---------------------------------------------------------------- face and flash

        public void SetFace(Face face)
        {
            _face = face;
            Show(_eyes, face is Face.Smile or Face.Grin or Face.Sad);
            Show(_eyesHappy, face == Face.Happy);
            Show(_mouth, face == Face.Smile);
            Show(_mouthOpen, face is Face.Grin or Face.Happy);
            Show(_mouthSad, face == Face.Sad);
        }

        static void Show(Transform bone, bool on)
        {
            if (bone != null) bone.localScale = on ? Vector3.one : Vector3.one * 0.001f;
        }

        /// <summary>The rim light (alpha = strength) instead of the material's (Yjet!: a cool light from the nebulae all
        /// round the astronaut); null = the material's own.</summary>
        public Color? Rim
        {
            get => _rim;
            set
            {
                _rim = value;
                _blockDirty = true;
            }
        }

        Color? _rim;

        public void Flash(Color color, float amount = 1f)
        {
            _flash = color;
            _flashAmount = amount;
            _blockDirty = true;
        }

        void LateUpdate()
        {
            // blinking: the open eyes squashed flat for a moment every few seconds
            if (_eyes != null && _face != Face.Happy)
            {
                if (Time.time >= _nextBlink)
                {
                    _blinkUntil = Time.time + 0.11f;
                    _nextBlink = Time.time + Random.Range(2.2f, 5.5f);
                }
                _eyes.localScale = Time.time < _blinkUntil ? new Vector3(1f, 0.12f, 1f) : Vector3.one;
            }
            if (_flashAmount > 0f) _flashAmount = Mathf.Max(0f, _flashAmount - Time.deltaTime * 3f);
            ReadSwitches();
            ApplySwitches();
            if (_blockDirty || _flashAmount != _appliedFlash) ApplyBlock();
        }
    }
}
