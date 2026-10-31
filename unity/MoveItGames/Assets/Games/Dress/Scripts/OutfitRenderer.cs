using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.Rendering;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Dress
{
    /// <summary>
    /// Draws the outfit: every worn piece becomes a small grid of vertices placed by its rig (<see cref="OutfitRig"/>),
    /// all in one dynamic mesh, back to front by layer, so the whole outfit is one draw call with one material
    /// (Tundu/Outfit). Before it, the body's shape is drawn from the skeleton into a small mask texture
    /// (Tundu/BodyMask): wings, capes and long hair are hidden behind the body, tops and skirts behind the arms.
    /// The same mesh is drawn once more just before, as a soft shadow (Tundu/Outfit with _Shadow on).
    /// World units are the HUD's pixels (1080 high, y up).
    /// </summary>
    public sealed class OutfitRenderer
    {
        public const int MaskDownscale = 4;

        readonly OutfitArt _art;
        readonly Mesh _mesh, _maskMesh;
        readonly Material _material, _maskMaterial, _shadowMaterial;
        readonly GameObject _go;
        readonly RigFrame _frame = new RigFrame();
        readonly List<Vector3> _verts = new List<Vector3>(4096);
        readonly List<Vector2> _uvs = new List<Vector2>(4096);
        readonly List<Vector4> _extra = new List<Vector4>(4096);
        readonly List<Color> _colors = new List<Color>(4096);
        readonly List<int> _tris = new List<int>(12288);
        readonly List<Vector3> _mVerts = new List<Vector3>(1024);
        readonly List<Vector2> _mUvs = new List<Vector2>(1024);
        readonly List<Color> _mColors = new List<Color>(1024);
        readonly List<int> _mTris = new List<int>(3072);
        readonly List<Vector2> _poly = new List<Vector2>(64);
        RenderTexture _mask;
        CommandBuffer _cmd;
        static readonly int MaskId = Shader.PropertyToID("_TunduBodyMask"), MaskRectId = Shader.PropertyToID("_TunduBodyMaskRect");

        /// <summary>Glint points of the worn pieces this frame (body space, z = size), for the sparkle particles.</summary>
        public readonly List<Vector3> Glints = new List<Vector3>(128);
        /// <summary>Where the wand's star is (body space), if a wand is held.</summary>
        public Vector2? WandTip { get; private set; }
        /// <summary>A point on each slot's piece (body space, for the "poof" when it is put on), and whether it was drawn.</summary>
        public readonly Vector2[] SlotCenter = new Vector2[DressCatalog.SlotCount];
        public readonly bool[] SlotDrawn = new bool[DressCatalog.SlotCount];

        public bool RightHandHolds = true;

        public OutfitRenderer(OutfitArt art, Material outfit, Material mask, Transform parent)
        {
            _art = art;
            _material = new Material(outfit);
            _material.SetTexture("_MainTex", art.Atlas);
            _material.SetTexture("_MaskTex", art.Mask);
            _material.renderQueue = 3000;
            _maskMaterial = new Material(mask);
            _mesh = new Mesh { name = "Outfit" };
            _mesh.MarkDynamic();
            _maskMesh = new Mesh { name = "Body mask" };
            _maskMesh.MarkDynamic();
            _go = new GameObject("Outfit");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            // the soft shadow: after the camera picture (2990), before the clothes
            _shadowMaterial = new Material(_material) { renderQueue = 2995 };
            _shadowMaterial.SetFloat("_Shadow", 1f);
            var shadow = new GameObject("Outfit shadow");
            shadow.transform.SetParent(_go.transform, false);
            shadow.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var sr = shadow.AddComponent<MeshRenderer>();
            sr.sharedMaterial = _shadowMaterial;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
        }

        public Material Material => _material;

        public void Dispose()
        {
            Object.Destroy(_mesh);
            Object.Destroy(_maskMesh);
            Object.Destroy(_material);
            Object.Destroy(_maskMaterial);
            Object.Destroy(_shadowMaterial);
            if (_mask != null) _mask.Release();
            _cmd?.Release();
        }

        /// <summary>
        /// Builds this frame's outfit. The pose is in the camera picture's space (1000 units per picture height, y up);
        /// <paramref name="toWorld"/> puts that space on the screen (the framing). <paramref name="flash"/>: per slot,
        /// 0..1 white "poof" of a piece just put on; <paramref name="pop"/>: per slot, 0..1 growing in.
        /// <paramref name="doll"/>: draw the paper doll under the clothes.
        /// </summary>
        public void Build(DressLook look, OutfitPose pose, OutfitMotion motion, float[] flash, float[] pop, bool doll,
                          Matrix4x4 toWorld, float width)
        {
            _go.transform.localPosition = toWorld.GetColumn(3);
            _go.transform.localScale = new Vector3(toWorld.m00, toWorld.m11, 1f);
            float torso = Mathf.Clamp(pose.T * toWorld.m00, 120f, 600f);   // the torso on the screen
            _shadowMaterial.SetVector("_ShadowOffset", new Vector4(0.01f * torso, -0.028f * torso, 0f, 0f));
            _verts.Clear();
            _uvs.Clear();
            _extra.Clear();
            _colors.Clear();
            _tris.Clear();
            Glints.Clear();
            WandTip = null;
            for (int s = 0; s < SlotDrawn.Length; s++) SlotDrawn[s] = false;
            if (pose.Valid)
            {
                // facing away, wings, capes and the hair behind the head are in front of the clothes
                var order = Mathf.Cos(pose.Yaw) < 0f ? FromBehind : FromFront;
                foreach (var l in order)
                {
                    int layer = (int)l;
                    if ((Layer)layer == Layer.Doll)
                    {
                        if (doll) DrawItem("doll", Layer.Doll, -1, Color.white, Finish.Plain, 0f, 1f, pose, motion);
                        continue;
                    }
                    // the doll's own bob while no hair is chosen
                    if (doll && look.Worn(Slot.Hair) == null)
                        DrawItem("dollhair", (Layer)layer, -1, Color.white, Finish.Plain, 0f, 1f, pose, motion);
                    for (int s = 0; s < DressCatalog.SlotCount; s++)
                    {
                        var item = look.Worn((Slot)s);
                        if (item == null) continue;
                        DressCatalog.Rgb(look.ColorOf((Slot)s), out float r, out float g, out float b);
                        DrawItem(item.Id, (Layer)layer, s, new Color(r, g, b, 1f), look.FinishOf((Slot)s), flash[s], pop[s], pose, motion);
                    }
                }
            }
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetUVs(1, _extra);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0, false);
            _mesh.bounds = new Bounds(new Vector3(1000f, 500f, 0f), new Vector3(8000f, 8000f, 100f));
            BuildMask(pose, toWorld, width, doll);
        }

        static readonly Layer[] FromFront =
            { Layer.Back, Layer.Doll, Layer.Bottom, Layer.Top, Layer.Sleeve, Layer.HairFront, Layer.Head, Layer.Face, Layer.Hand };
        static readonly Layer[] FromBehind =
            { Layer.Doll, Layer.Bottom, Layer.Top, Layer.Sleeve, Layer.Back, Layer.HairFront, Layer.Head, Layer.Face, Layer.Hand };

        void DrawItem(string id, Layer layer, int slot, Color tint, Finish finish, float flash, float pop, OutfitPose pose, OutfitMotion motion)
        {
            var pieces = _art.Pieces(id);
            foreach (var def in pieces)
            {
                if (def.Layer != layer) continue;
                if (def.Rig == Rig.Hand)
                {
                    DrawPiece(def, slot, !RightHandHolds, tint, finish, flash, pop, pose, motion);
                    continue;
                }
                if (def.BothSides) DrawPiece(def, slot, true, tint, finish, flash, pop, pose, motion);
                DrawPiece(def, slot, false, tint, finish, flash, pop, pose, motion);
            }
        }

        void DrawPiece(PieceDef def, int slot, bool left, Color tint, Finish finish, float flash, float pop, OutfitPose pose, OutfitMotion motion)
        {
            if (!OutfitRig.Build(def, pose, motion, left, _frame)) return;
            float alpha = Visibility(def, pose, left);
            if (alpha <= 0.01f) return;
            tint.a = alpha;
            // behind the body while it faces the camera; seen from behind, wings and capes are in front of it
            float behind = Mathf.Clamp01(Mathf.Cos(def.Rig == Rig.Hair ? pose.HeadYaw : pose.Yaw) * 2.5f + 0.15f);
            float occlusion = def.Layer == Layer.Back ? Mathf.Max(0.02f, behind) : def.Layer == Layer.Bottom || def.Layer == Layer.Top ? 2f : 0f;
            var extra = new Vector4((int)finish, occlusion, flash, slot * 0.137f + (left ? 0.5f : 0f));
            // a piece being put on grows out of its middle
            Vector2 center = _frame.Map(def.TemplateAt(0.5f, 0.5f));
            float grow = pop >= 1f ? 1f : Back(pop);
            if (Turn.HasBack(_frame.Wrap, _frame.WrapYaw))
            {
                // the back of a turning dress or hat: the same art mirrored onto the back, a little darker
                _frame.BackSheet = true;
                Sheet(def, extra, new Color(tint.r * BackShade, tint.g * BackShade, tint.b * BackShade, tint.a), center, grow);
                _frame.BackSheet = false;
            }
            Sheet(def, extra, tint, center, grow);
            if (slot >= 0)
            {
                SlotCenter[slot] = center;
                SlotDrawn[slot] = true;
                for (int k = 0; k + 2 < def.Sparkles.Length; k += 3)
                {
                    Vector2 g = _frame.Map(new Vector2(def.Sparkles[k], def.Sparkles[k + 1]));
                    if (_frame.Hidden) continue;      // turned away
                    Vector2 g2 = _frame.Map(new Vector2(def.Sparkles[k] + def.Sparkles[k + 2], def.Sparkles[k + 1]));
                    Glints.Add(new Vector3(g.x, g.y, Mathf.Max(6f, (g2 - g).magnitude) * alpha));
                }
                if (def.Rig == Rig.Hand && def.Tip != Vector2.zero) WandTip = _frame.Map(def.Tip);
                if (def.Rig == Rig.Hand && def.Mode == HandMode.Float) String(OutfitMotion.Grip(pose, !left), _frame.Map(def.Knot), alpha, pose.T);
            }
        }

        const float BackShade = 0.86f;

        /// <summary>Columns across a section (s, -1..1) for pieces that wrap around a turning body: dense where the turn
        /// curves them the most, at the sides, plus the sheet's horizon and the cell's edges.</summary>
        static readonly float[] SectionCols = { -1f, -0.97f, -0.9f, -0.78f, -0.6f, -0.35f, -0.12f, 0.12f, 0.35f, 0.6f, 0.78f, 0.9f, 0.97f, 1f };
        readonly float[] _cols = new float[SectionCols.Length + 3];

        /// <summary>One sheet of a piece: its cell as a grid of vertices placed by the rig. A piece wrapped around the
        /// body gets its columns along its cross-section (exactly on its sides and where it turns out of view), so the
        /// turn bends it smoothly instead of in steps across the grid.</summary>
        void Sheet(PieceDef def, Vector4 extra, Color tint, Vector2 center, float grow)
        {
            def.GridSize(out int cols, out int rows);
            bool section = _frame.Wrap == WrapKind.Section;
            if (section) cols = _cols.Length;
            int start = _verts.Count;
            for (int r = 0; r < rows; r++)
            {
                float v = r / (rows - 1f);
                Vector2 left = def.TemplateAt(0f, v), right = def.TemplateAt(1f, v);
                float a = 1f;
                if (section)
                {
                    Turn.Section(_frame.WrapDef, left.y, _frame.WrapYaw, _frame.WrapDepth, _frame.WrapRadius, out a, out float k);
                    float horizon = Turn.Horizon(_frame.WrapYaw, k, _frame.BackSheet);
                    int n = 0;
                    _cols[n++] = Mathf.Min(left.x / a, -1.0001f);
                    bool placed = false;
                    foreach (float sc in SectionCols)
                    {
                        if (!placed && horizon <= sc) { _cols[n++] = horizon; placed = true; }
                        _cols[n++] = sc;
                    }
                    if (!placed) _cols[n++] = horizon;
                    _cols[n] = Mathf.Max(right.x / a, 1.0001f);
                }
                for (int c = 0; c < cols; c++)
                {
                    Vector2 tp;
                    float u;
                    if (section)
                    {
                        tp = new Vector2(Mathf.Clamp(_cols[c] * a, left.x, right.x), left.y);
                        u = (tp.x * def.Ppt + def.OriginX) / def.W;
                    }
                    else
                    {
                        u = c / (cols - 1f);
                        tp = def.TemplateAt(u, v);
                    }
                    Vector2 p = _frame.Map(tp);
                    if (grow < 1f) p = center + (p - center) * grow;
                    _verts.Add(new Vector3(p.x, p.y, 0f));
                    _uvs.Add(new Vector2((def.X + u * def.W) / _art.AtlasWidth, 1f - (def.Y + v * def.H) / _art.AtlasHeight));
                    _extra.Add(extra);
                    _colors.Add(tint);
                }
            }
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                {
                    int i = start + r * cols + c;
                    _tris.Add(i);
                    _tris.Add(i + cols);
                    _tris.Add(i + 1);
                    _tris.Add(i + 1);
                    _tris.Add(i + cols);
                    _tris.Add(i + cols + 1);
                }
        }

        /// <summary>A balloon's string: a thin white strip from the hand to the knot.</summary>
        void String(Vector2 from, Vector2 to, float alpha, float t)
        {
            Vector2 d = to - from;
            if (d.sqrMagnitude < 1f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * Mathf.Max(1.2f, 0.006f * t);
            int start = _verts.Count;
            const int Segments = 8;
            for (int k = 0; k <= Segments; k++)
            {
                float f = k / (float)Segments;
                Vector2 p = Vector2.Lerp(from, to, f) + n * (Mathf.Sin(f * Mathf.PI) * 3f);   // a little slack
                _verts.Add(new Vector3(p.x - n.x, p.y - n.y));
                _verts.Add(new Vector3(p.x + n.x, p.y + n.y));
                for (int e = 0; e < 2; e++)
                {
                    _uvs.Add(_art.WhiteUv);
                    _extra.Add(Vector4.zero);
                    _colors.Add(new Color(0.96f, 0.95f, 1f, 0.85f * alpha));
                }
            }
            for (int k = 0; k < Segments; k++)
            {
                int i = start + k * 2;
                _tris.Add(i);
                _tris.Add(i + 2);
                _tris.Add(i + 1);
                _tris.Add(i + 1);
                _tris.Add(i + 2);
                _tris.Add(i + 3);
            }
        }

        /// <summary>Sleeves, gloves and held things fade out when the camera can't see the arm; the doll never does.</summary>
        static float Visibility(PieceDef def, OutfitPose pose, bool left)
        {
            if (def.Layer == Layer.Doll) return 1f;
            return def.Rig switch
            {
                Rig.Arm => Mathf.Clamp01(pose.Seen[(int)(left ? Joint.LeftElbow : Joint.RightElbow)] * 1.5f),
                Rig.Hand => Mathf.Clamp01(pose.Seen[(int)(left ? Joint.LeftWrist : Joint.RightWrist)] * 1.5f),
                // glasses and masks need both eyes: they fade as the face turns to a profile
                Rig.Face => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1.2f, Mathf.Abs(pose.HeadYaw))),
                _ => 1f,
            };
        }

        static float Back(float t)
        {
            t = Mathf.Clamp01(t);
            const float s = 1.9f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }

        // ---------------------------------------------------------------- the body mask

        void BuildMask(OutfitPose pose, Matrix4x4 toWorld, float width, bool doll)
        {
            int w = Mathf.Max(16, Mathf.RoundToInt(width / MaskDownscale)), h = 1080 / MaskDownscale;
            if (_mask == null || _mask.width != w)
            {
                if (_mask != null) _mask.Release();
                _mask = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "Body mask", filterMode = FilterMode.Bilinear };
                _mask.Create();
            }
            _mVerts.Clear();
            _mUvs.Clear();
            _mColors.Clear();
            _mTris.Clear();
            if (pose.Valid)
            {
                var J = pose.J;
                float t = pose.T, feather = 0.05f * t;
                Color body = new Color(1f, 0f, 0f), arms = new Color(1f, 1f, 0f);
                // torso: as wide as the body looks turned (Turn.Silhouette), around its axis, a little wider than the joints
                Vector2 across = pose.Across;
                Vector2 down = pose.Down;
                float turned = Turn.Silhouette(pose.Yaw, OutfitRig.TorsoDepth);
                float sw = pose.FrontShoulderHalf * turned + 0.06f * t, hw = pose.FrontHipHalf * turned + 0.14f * t;
                _poly.Clear();
                _poly.Add(pose.ShoulderC - across * sw - down * (0.05f * t));
                _poly.Add(pose.ShoulderC + across * sw - down * (0.05f * t));
                _poly.Add(pose.HipC + across * hw + down * (0.12f * t));
                _poly.Add(pose.HipC - across * hw + down * (0.12f * t));
                Polygon(_poly, feather, body);
                // the head (the skull, a bit bigger than the face) and the neck
                float s = pose.HeadScale;
                Vector2 hx = new Vector2(Mathf.Cos(pose.HeadRoll), Mathf.Sin(pose.HeadRoll)), hy = new Vector2(-hx.y, hx.x);
                Vector2 hc = pose.SkullCenter - hy * (0.01f * s);
                _poly.Clear();
                for (int k = 0; k < 20; k++)
                {
                    float a = k / 20f * Mathf.PI * 2f;
                    _poly.Add(hc + hx * (Mathf.Cos(a) * 0.165f * s) + hy * (Mathf.Sin(a) * 0.215f * s));
                }
                Polygon(_poly, feather * 0.6f, body);
                Capsule(pose.ShoulderC, pose.HeadCenter - hy * (0.15f * s), 0.085f * s, feather, body);   // the neck
                // arms (in both channels) and legs
                foreach (bool right in new[] { false, true })
                {
                    Vector2 sh = J[(int)(right ? Joint.RightShoulder : Joint.LeftShoulder)];
                    Vector2 el = J[(int)(right ? Joint.RightElbow : Joint.LeftElbow)];
                    Vector2 wr = J[(int)(right ? Joint.RightWrist : Joint.LeftWrist)];
                    float seen = pose.Seen[(int)(right ? Joint.RightElbow : Joint.LeftElbow)];
                    if (seen > 0.2f)
                    {
                        Capsule(sh, el, 0.11f * t, feather, arms * seen);
                        Capsule(el, wr, 0.085f * t, feather, arms * seen);
                        // the hand (the paper doll's is a small mitten)
                        Capsule(wr, wr + (wr - el).normalized * ((doll ? 0.14f : 0.2f) * t), (doll ? 0.075f : 0.1f) * t, feather, arms * seen);
                    }
                    Vector2 hp = J[(int)(right ? Joint.RightHip : Joint.LeftHip)];
                    Vector2 kn = J[(int)(right ? Joint.RightKnee : Joint.LeftKnee)];
                    Vector2 an = J[(int)(right ? Joint.RightAnkle : Joint.LeftAnkle)];
                    Capsule(hp, kn, 0.15f * t, feather, body);
                    Capsule(kn, an, 0.11f * t, feather, body);
                }
            }
            _maskMesh.Clear();
            _maskMesh.SetVertices(_mVerts);
            _maskMesh.SetUVs(0, _mUvs);
            _maskMesh.SetColors(_mColors);
            _maskMesh.SetTriangles(_mTris, 0, false);

            _cmd ??= new CommandBuffer { name = "Vishu body mask" };
            _cmd.Clear();
            _cmd.SetRenderTarget(_mask);
            _cmd.ClearRenderTarget(false, true, Color.clear);
            _cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, width, 0f, 1080f, -10f, 10f));
            _cmd.DrawMesh(_maskMesh, toWorld, _maskMaterial, 0, 0);
            Graphics.ExecuteCommandBuffer(_cmd);
            Shader.SetGlobalTexture(MaskId, _mask);
            Shader.SetGlobalVector(MaskRectId, new Vector4(0f, 0f, 1f / width, 1f / 1080f));
        }

        /// <summary>A convex polygon with a soft rim <paramref name="feather"/> wide (centre uv 0, inner ring 0.7, rim 1).</summary>
        void Polygon(List<Vector2> pts, float feather, Color color)
        {
            Vector2 c = Vector2.zero;
            foreach (var p in pts) c += p;
            c /= pts.Count;
            int center = _mVerts.Count;
            AddMask(c, Vector2.zero, color);
            int n = pts.Count;
            for (int k = 0; k < n; k++)
            {
                Vector2 p = pts[k], d = p - c;
                float len = d.magnitude;
                Vector2 inner = len > feather ? c + d * ((len - feather) / len) : c;
                AddMask(inner, new Vector2(0.7f, 0f), color);
                AddMask(p, new Vector2(1f, 0f), color);
            }
            for (int k = 0; k < n; k++)
            {
                int a = center + 1 + 2 * k, b = center + 1 + 2 * ((k + 1) % n);
                _mTris.Add(center);
                _mTris.Add(a);
                _mTris.Add(b);
                _mTris.Add(a);
                _mTris.Add(a + 1);
                _mTris.Add(b);
                _mTris.Add(b);
                _mTris.Add(a + 1);
                _mTris.Add(b + 1);
            }
        }

        /// <summary>A capsule (a limb) from a to b, radius r, soft at the edge.</summary>
        void Capsule(Vector2 a, Vector2 b, float r, float feather, Color color)
        {
            Vector2 d = b - a;
            Vector2 along = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.up;
            Vector2 n = new Vector2(-along.y, along.x);
            _poly.Clear();
            const int Cap = 6;
            for (int k = 0; k <= Cap; k++)
            {
                float ang = Mathf.PI * k / Cap;
                _poly.Add(b + (n * Mathf.Cos(ang) + along * Mathf.Sin(ang)) * r);
            }
            for (int k = 0; k <= Cap; k++)
            {
                float ang = Mathf.PI * k / Cap;
                _poly.Add(a - (n * Mathf.Cos(ang) + along * Mathf.Sin(ang)) * r);
            }
            Polygon(_poly, Mathf.Min(feather, r * 0.8f), color);
        }

        void AddMask(Vector2 p, Vector2 uv, Color c)
        {
            _mVerts.Add(new Vector3(p.x, p.y, 0f));
            _mUvs.Add(uv);
            _mColors.Add(c);
        }
    }
}
