using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// One of Sporte!'s athletes: the toy athlete of art/sports/athlete.py dressed in an <see cref="AthleteLook"/> (its
    /// pieces merged into one skinned mesh, one draw call; colours per region with a property block), playing the clips
    /// (<see cref="AthleteData.Clips"/>) with cross-fades, and on top of them: blinking, the head turned toward
    /// something, hands reaching for a point (two-bone IK: a keeper's gloves), a face held for a while, a flash, fading.
    /// Facing +Z, feet at the object's origin.
    /// </summary>
    public sealed class Athlete : MonoBehaviour
    {
        static GameObject _prefab;
        static Material _material, _blobMaterial;
        static Material[] _solid, _fading;
        static Mesh _blobMesh;
        static readonly Dictionary<string, AnimationClip> ClipsByName = new Dictionary<string, AnimationClip>();
        static readonly Dictionary<string, Mesh> Merged = new Dictionary<string, Mesh>();

        /// <summary>
        /// The pieces that are closed shells (no open edge in the export, checked 2026-10-30): drawn as a second submesh
        /// with their back faces culled. The rest (the kit's hems, the gloves' cuffs, the hairline) stays two-sided.
        /// </summary>
        static readonly HashSet<string> ClosedPieces = new HashSet<string> { "Body", "Face", "Hands", "Shoes", "BareFeet", "Headband" };

        /// <summary>The model, its material and clips (the scene's game passes them once).</summary>
        public static void Register(GameObject prefab, Material material, AnimationClip[] clips)
        {
            _prefab = prefab;
            _material = material;
            _solid = _fading = null;
            ClipsByName.Clear();
            if (clips != null)
                foreach (var c in clips)
                    if (c != null) ClipsByName[c.name] = c;
        }

        public static bool Ready => _prefab != null && _material != null;

        public enum Face { Clip, Happy, Joy, Effort, Sad, Neutral }

        sealed class Layer
        {
            public string Name;
            public AnimationClipPlayable Playable;
            public int Input;
            public float Weight, FadeTime;
            public bool Out, Loop;
            public float Length;
        }

        AthleteLook _look;
        SkinnedMeshRenderer _renderer;
        MaterialPropertyBlock _block;
        readonly Vector4[] _regions = new Vector4[24];
        Animator _animator;
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        const int Inputs = 4;
        readonly Layer[] _inputs = new Layer[Inputs];
        readonly List<Layer> _layers = new List<Layer>();
        string _after;
        float _afterFade;

        Transform _hips, _spine, _chest, _neck, _head, _eyes, _eyesShut, _mouth, _mouthOpen, _mouthSad;
        readonly Transform[] _upper = new Transform[2], _fore = new Transform[2], _hand = new Transform[2];
        readonly Transform[] _thigh = new Transform[2], _shin = new Transform[2], _foot = new Transform[2];
        Vector3 _headForward;

        float _nextBlink, _blinkUntil;
        Face _face;
        float _faceUntil;
        Color _flash;
        float _flashUntil, _flashLength;
        float _fade = 1f;
        Transform _blob;
        Vector2 _blobSize;
        float _standHips;

        Vector3? _lookAt;
        float _lookWeight, _lookTarget;
        readonly Vector3[] _reach = new Vector3[2];
        readonly Vector3[] _reachHint = new Vector3[2];
        readonly float[] _reachWeight = new float[2], _reachTarget = new float[2];

        public AthleteLook Look => _look;
        public string Playing => _layers.Count > 0 ? _layers[_layers.Count - 1].Name : null;
        public Transform Hips => _hips;
        public Transform HeadBone => _head;
        public Transform ChestBone => _chest;
        public Renderer Renderer => _renderer;

        // ---------------------------------------------------------------- making one

        /// <summary>A new athlete in <paramref name="look"/> under <paramref name="parent"/>, standing (Idle).</summary>
        public static Athlete Create(string name, AthleteLook look, Transform parent)
        {
            if (!Ready)
            {
                Debug.LogError("[Athlete] Register the model first (SportsGame passes it).");
                var empty = new GameObject(name).AddComponent<Athlete>();
                empty.transform.SetParent(parent, false);
                return empty;
            }
            var go = Instantiate(_prefab, parent, false);
            go.name = name;
            var a = go.AddComponent<Athlete>();
            a.Build(look);
            return a;
        }

        void Build(AthleteLook look)
        {
            _look = look;
            FindBones();
            _headForward = Quaternion.Inverse(_head.rotation) * transform.forward;
            var parts = new List<SkinnedMeshRenderer>();
            var all = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // Outermost first (hair, shoes, the kit, then face and body): drawn in that order, the depth test skips the
            // parts of the body they cover instead of shading them and painting over them
            var pieces = look.Pieces();
            System.Array.Reverse(pieces);
            foreach (string piece in pieces)
            {
                var smr = System.Array.Find(all, r => r.name == piece);
                if (smr != null) parts.Add(smr);
                else Debug.LogWarning($"[Athlete] No piece {piece}");
            }
            string key = string.Join("+", look.Pieces());
            if (!Merged.TryGetValue(key, out var mesh) || mesh == null)
            {
                mesh = Merge(parts, key);
                Merged[key] = mesh;
            }
            var bones = BonesOf(parts);
            var holder = new GameObject("Mesh");
            holder.transform.SetParent(transform, false);
            _renderer = holder.AddComponent<SkinnedMeshRenderer>();
            _renderer.sharedMesh = mesh;
            _renderer.bones = bones;
            _renderer.rootBone = _hips;
            UseMaterial();
            _renderer.localBounds = new Bounds(Vector3.zero, new Vector3(3.4f, 3.4f, 3.4f));
            _renderer.shadowCastingMode = ShadowCastingMode.On;
            _renderer.receiveShadows = true;
            _renderer.updateWhenOffscreen = false;
            foreach (var smr in all) Destroy(smr.gameObject);
            _block = new MaterialPropertyBlock();
            Recolour(look);
            if (!BoxGraphics.Current.Shadows) MakeBlobShadow(mesh.bounds);

            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = gameObject.AddComponent<Animator>();
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create($"Athlete {name}");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Athlete", _animator);
            _mixer = AnimationMixerPlayable.Create(_graph, Inputs);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            Play("Idle", 0f, from: Random.value * 2f);
            _nextBlink = Time.time + Random.Range(1f, 4f);
        }

        void FindBones()
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) map[t.name] = t;
            Transform B(string n) => map.TryGetValue(n, out var t) ? t : map.TryGetValue(n.Replace('.', '_'), out t) ? t : null;
            _hips = B("Hips");
            _spine = B("Spine");
            _chest = B("Chest");
            _neck = B("Neck");
            _head = B("Head");
            _eyes = B("Eyes");
            _eyesShut = B("EyesShut");
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

        static Transform[] BonesOf(List<SkinnedMeshRenderer> parts)
        {
            var list = new List<Transform>();
            foreach (var smr in parts)
                foreach (var b in smr.bones)
                    if (!list.Contains(b)) list.Add(b);
            return list.ToArray();
        }

        /// <summary>The pieces as one mesh: vertices, normals, both UV channels (the second = region, occlusion),
        /// triangles (submesh 0 the open pieces, 1 the <see cref="ClosedPieces"/>) and skin weights, the bones in
        /// <see cref="BonesOf"/> order.</summary>
        static Mesh Merge(List<SkinnedMeshRenderer> parts, string key)
        {
            var bones = BonesOf(parts);
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < bones.Length; i++) index[bones[i]] = i;
            var bind = new Matrix4x4[bones.Length];
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv0 = new List<Vector2>();
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
                    map[i] = index[smr.bones[i]];
                    bind[map[i]] = m.bindposes[i];
                }
                int start = verts.Count;
                m.GetVertices(tv);
                verts.AddRange(tv);
                m.GetNormals(tn);
                normals.AddRange(tn);
                m.GetUVs(0, tu);
                if (tu.Count != tv.Count) tu = new List<Vector2>(new Vector2[tv.Count]);
                uv0.AddRange(tu);
                m.GetUVs(1, tu);
                if (tu.Count != tv.Count) tu = new List<Vector2>(new Vector2[tv.Count]);
                uv1.AddRange(tu);
                var into = ClosedPieces.Contains(smr.name) ? closedTris : tris;
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
            var mesh = new Mesh { name = $"Athlete {key}" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tris, 0);
            mesh.SetTriangles(closedTris, 1);
            using (var c = new NativeArray<byte>(perVertex.ToArray(), Allocator.Temp))
            using (var w = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp))
                mesh.SetBoneWeights(c, w);
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>New colours (a team change, a magic kit).</summary>
        public void Recolour(AthleteLook look)
        {
            _look = look;
            look.Fill(_regions);
            ApplyBlock();
        }

        void ApplyBlock()
        {
            if (_renderer == null) return;
            _block.SetVectorArray("_Regions", _regions);
            var flash = _flash;
            flash.a = Time.time < _flashUntil ? _flash.a * Mathf.Clamp01((_flashUntil - Time.time) / Mathf.Max(0.01f, _flashLength)) : 0f;
            _block.SetColor("_FlashColor", flash);
            _block.SetFloat("_Fade", _fade);
            _renderer.SetPropertyBlock(_block);
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        // ---------------------------------------------------------------- clips

        public static bool Has(string clip) => ClipsByName.ContainsKey(clip);

        /// <summary>
        /// Plays a clip, cross-fading over <paramref name="fade"/> seconds (0 = at once). The same clip again only
        /// changes its speed unless <paramref name="restart"/>. <paramref name="from"/> = seconds into it.
        /// </summary>
        public void Play(string clip, float fade = 0.15f, float speed = 1f, float from = 0f, bool restart = false)
        {
            _after = null;
            if (!_graph.IsValid()) return;
            if (!ClipsByName.TryGetValue(clip, out var c))
            {
                Debug.LogWarning($"[Athlete] No clip {clip}");
                return;
            }
            var top = _layers.Count > 0 ? _layers[_layers.Count - 1] : null;
            if (top != null && top.Name == clip && !restart && !top.Out)
            {
                top.Playable.SetSpeed(speed);
                return;
            }
            if (fade <= 0f)
            {
                while (_layers.Count > 0) Drop(_layers[0]);
            }
            int input = System.Array.IndexOf(_inputs, null);
            if (input < 0)
            {
                // all inputs busy: drop the faintest
                Layer faint = null;
                foreach (var l in _layers)
                    if (faint == null || l.Weight < faint.Weight) faint = l;
                Drop(faint);
                input = System.Array.IndexOf(_inputs, null);
            }
            foreach (var l in _layers)
            {
                l.Out = true;
                l.FadeTime = Mathf.Max(0.01f, fade);
            }
            var p = AnimationClipPlayable.Create(_graph, c);
            p.SetApplyFootIK(false);
            p.SetApplyPlayableIK(false);
            p.SetTime(from);
            p.SetSpeed(speed);
            _graph.Connect(p, 0, _mixer, input);
            var layer = new Layer
            {
                Name = clip, Playable = p, Input = input, Weight = fade <= 0f ? 1f : 0f, FadeTime = Mathf.Max(0.01f, fade),
                Loop = c.isLooping, Length = c.length,
            };
            _inputs[input] = layer;
            _layers.Add(layer);
            Weigh();
        }

        /// <summary>Plays a one-off clip, then <paramref name="then"/> when it ends.</summary>
        public void PlayThen(string clip, string then, float fade = 0.1f, float speed = 1f, float thenFade = 0.25f, float from = 0f)
        {
            Play(clip, fade, speed, from, restart: true);
            _after = then;
            _afterFade = thenFade;
        }

        public void SetSpeed(float speed)
        {
            if (_layers.Count > 0) _layers[_layers.Count - 1].Playable.SetSpeed(speed);
        }

        /// <summary>Seconds into the clip playing now.</summary>
        public float ClipTime => _layers.Count > 0 ? (float)_layers[_layers.Count - 1].Playable.GetTime() : 0f;

        /// <summary>A one-off clip playing now has reached its end.</summary>
        public bool ClipDone => _layers.Count > 0 && !_layers[_layers.Count - 1].Loop &&
                                _layers[_layers.Count - 1].Playable.GetTime() >= _layers[_layers.Count - 1].Length;

        void Drop(Layer l)
        {
            if (l == null) return;
            _graph.Disconnect(_mixer, l.Input);
            if (l.Playable.IsValid()) l.Playable.Destroy();
            _inputs[l.Input] = null;
            _layers.Remove(l);
        }

        void Weigh()
        {
            float total = 0f;
            foreach (var l in _layers) total += l.Weight;
            for (int i = 0; i < Inputs; i++)
            {
                var l = _inputs[i];
                _mixer.SetInputWeight(i, l == null || total <= 0f ? 0f : l.Weight / total);
            }
        }

        void Update()
        {
            if (!_graph.IsValid()) return;
            float dt = Time.deltaTime;
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                var l = _layers[i];
                l.Weight = Mathf.Clamp01(l.Weight + (l.Out ? -dt : dt) / l.FadeTime);
                if (l.Out && l.Weight <= 0f) Drop(l);
            }
            if (_layers.Count > 0 && _layers[_layers.Count - 1].Weight <= 0f) _layers[_layers.Count - 1].Weight = 1f;
            Weigh();
            if (_after != null && ClipDone)
            {
                string next = _after;
                _after = null;
                Play(next, _afterFade);
            }
            _lookWeight = Mathf.MoveTowards(_lookWeight, _lookTarget, dt * 3f);
            for (int s = 0; s < 2; s++) _reachWeight[s] = Mathf.MoveTowards(_reachWeight[s], _reachTarget[s], dt * 6f);
            if (Time.time < _flashUntil + 0.1f) ApplyBlock();
        }

        // ---------------------------------------------------------------- on top of the clips

        /// <summary>Turns the head toward a point (null: straight again).</summary>
        public void LookAt(Vector3? point)
        {
            if (point.HasValue) _lookAt = point;
            _lookTarget = point.HasValue ? 1f : 0f;
        }

        /// <summary>A hand reaches for a point (world space; null lets go). Left = 0, right = 1.</summary>
        public void Reach(int side, Vector3? point, Vector3 elbowHint = default)
        {
            if (point.HasValue)
            {
                _reach[side] = point.Value;
                _reachHint[side] = elbowHint == default ? (transform.right * (side == 0 ? -1f : 1f) - transform.up * 0.5f) : elbowHint;
            }
            _reachTarget[side] = point.HasValue ? 1f : 0f;
        }

        /// <summary>Holds a face for a while (Face.Clip = the clip's own again).</summary>
        public void ShowFace(Face face, float seconds = 1.5f)
        {
            _face = face;
            _faceUntil = Time.time + seconds;
        }

        public void Flash(Color colour, float seconds = 0.3f)
        {
            _flash = colour;
            _flashLength = seconds;
            _flashUntil = Time.time + seconds;
            ApplyBlock();
        }

        /// <summary>1 = solid, 0 = gone (dithered: no sorting).</summary>
        public float Fade
        {
            get => _fade;
            set
            {
                if (Mathf.Approximately(_fade, value)) return;
                _fade = value;
                UseMaterial();
                ApplyBlock();
            }
        }

        /// <summary>The materials for the two submeshes (open pieces two-sided, closed ones culled), with the dither
        /// (Tundu/Athlete's _FADE) only while fading: the dither's discard costs the GPU its early depth test.</summary>
        void UseMaterial()
        {
            if (_renderer == null || _material == null) return;
            bool fading = _fade < 0.999f;
            if (_solid == null)
            {
                _solid = new[] { _material, Variant(true, false) };
                _fading = new[] { Variant(false, true), Variant(true, true) };
            }
            var wanted = fading ? _fading : _solid;
            if (_renderer.sharedMaterials.Length != 2 || _renderer.sharedMaterials[0] != wanted[0]) _renderer.sharedMaterials = wanted;
        }

        static Material Variant(bool closed, bool fade)
        {
            var m = new Material(_material) { name = _material.name + (closed ? " (closed)" : "") + (fade ? " (fade)" : "") };
            if (closed) m.SetFloat("_Cull", (float)CullMode.Back);
            if (fade) m.EnableKeyword("_FADE");
            return m;
        }

        /// <summary>
        /// Where the TV box draws no real-time shadows (<see cref="BoxGraphics"/>: Sporte!'s cost ~14 ms a frame there), a
        /// soft dark blob under the hips keeps the athlete on the ground; it shrinks as they leave it.
        /// </summary>
        void MakeBlobShadow(Bounds bounds)
        {
            if (_blobMesh == null) _blobMesh = BlobMesh();
            if (_blobMaterial == null) _blobMaterial = BlobMaterial();
            if (_blobMaterial == null || _hips == null) return;
            var go = new GameObject("Blob Shadow");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _blobMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _blobMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            _blob = go.transform;
            float height = Mathf.Max(0.5f, bounds.size.y);
            _blobSize = new Vector2(height * 0.5f, height * 0.36f);
            _standHips = _hips.position.y - transform.position.y;
        }

        static Mesh BlobMesh()
        {
            var m = new Mesh { name = "Blob Shadow" };
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Tundu/Fx with a soft round fall-off, the colour of the other games' blob shadows.</summary>
        static Material BlobMaterial()
        {
            var shader = Shader.Find("Tundu/Fx");
            if (shader == null) return null;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Blob Shadow", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / c;
                float k = Mathf.Clamp01(1f - d);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(k * k * (3f - 2f * k) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var m = new Material(shader) { name = "Athlete Blob Shadow" };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(0.1f, 0.12f, 0.3f, 0.42f));
            return m;
        }

        void LateUpdate()
        {
            if (_head == null) return;
            // the head turns toward what it watches (within 70 degrees)
            if (_lookWeight > 0.001f && _lookAt.HasValue)
            {
                var fwd = _head.rotation * _headForward;
                var want = _lookAt.Value - _head.position;
                var turn = Quaternion.FromToRotation(fwd, want.normalized);
                turn.ToAngleAxis(out float angle, out var axis);
                if (angle > 180f) angle -= 360f;
                angle = Mathf.Clamp(angle, -70f, 70f) * _lookWeight;
                _head.rotation = Quaternion.AngleAxis(angle * 0.7f, axis) * _head.rotation;
                if (_neck != null) _neck.rotation = Quaternion.AngleAxis(angle * 0.3f, axis) * _neck.rotation;
            }
            for (int s = 0; s < 2; s++)
                if (_reachWeight[s] > 0.001f && _upper[s] != null)
                    TwoBone(_upper[s], _fore[s], _hand[s], _reach[s], _reachHint[s], _reachWeight[s]);
            Expressions();
            if (_blob != null)
            {
                // on the ground under the hips (a dive takes it along), smaller as they jump
                var foot = transform.position;
                var hips = _hips.position;
                float k = 1f - 0.45f * Mathf.Clamp01((hips.y - foot.y - _standHips) / 1.2f);
                _blob.position = new Vector3(hips.x, foot.y + 0.02f, hips.z);
                _blob.localScale = new Vector3(_blobSize.x * k, 1f, _blobSize.y * k);
            }
        }

        static void TwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint, float weight)
        {
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
            var t = Vector3.Lerp(c, target, weight);
            var d = t - a;
            float dist = Mathf.Clamp(d.magnitude, 1e-4f, l1 + l2 - 1e-4f);
            var dn = d.normalized;
            float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            var h = hint - dn * Vector3.Dot(hint, dn);
            if (h.sqrMagnitude < 1e-6f) h = (b - a) - dn * Vector3.Dot(b - a, dn);
            h.Normalize();
            var elbow = a + (dn * cosA + h * Mathf.Sqrt(1f - cosA * cosA)) * l1;
            upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
            b = lower.position;
            c = end.position;
            lower.rotation = Quaternion.FromToRotation(c - b, a + dn * dist - b) * lower.rotation;
        }

        void Expressions()
        {
            if (_eyes == null) return;
            if (Time.time < _faceUntil && _face != Face.Clip)
            {
                // eyes, shut eyes, smile, open mouth, sad mouth
                var (e, sh, sm, op, sd) = _face switch
                {
                    Face.Happy => (0, 1, 0, 1, 0),
                    Face.Joy => (1, 0, 0, 1, 0),
                    Face.Effort => (1, 0, 0, 1, 0),
                    Face.Sad => (1, 0, 0, 0, 1),
                    _ => (1, 0, 1, 0, 0),
                };
                Show(_eyes, e);
                Show(_eyesShut, sh);
                Show(_mouth, sm);
                Show(_mouthOpen, op);
                Show(_mouthSad, sd);
            }
            // blinking: only while the eyes are open
            if (Time.time >= _nextBlink)
            {
                _blinkUntil = Time.time + 0.11f;
                _nextBlink = Time.time + Random.Range(2.2f, 5.5f);
            }
            if (Time.time < _blinkUntil && _eyes.localScale.x > 0.5f)
            {
                Show(_eyes, 0);
                Show(_eyesShut, 1);
            }
        }

        static void Show(Transform bone, int on)
        {
            if (bone != null) bone.localScale = on != 0 ? Vector3.one : Vector3.one * 0.001f;
        }

        // ---------------------------------------------------------------- where things are

        /// <summary>The middle of a fist (left = 0, right = 1).</summary>
        public Vector3 HandPosition(int side)
        {
            var h = _hand[side];
            if (h == null) return transform.position + Vector3.up;
            var along = h.position - _fore[side].position;
            return h.position + along.normalized * 0.075f;
        }

        public Vector3 FootPosition(int side) => _foot[side] != null ? _foot[side].position : transform.position;
        public Vector3 HeadPosition => _head != null ? _head.position + _head.rotation * Vector3.zero + Vector3.up * 0.13f : transform.position + Vector3.up * 1.66f;
        public Vector3 ChestPosition => _chest != null ? _chest.position : transform.position + Vector3.up * 1.2f;
        public Transform Hand(int side) => _hand[side];
    }
}
