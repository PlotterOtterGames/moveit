using MoveIt.Motion;
using UnityEngine;
using UnityEngine.Rendering;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Sword
{
    /// <summary>
    /// The player as a see-through figure seen from behind, holding a toy sword (like the semi-transparent Mii in
    /// Wii Sports Resort's Swordplay). Built from simple shapes and posed every frame from the camera's joints.
    ///
    /// Pose: the joints are placed around the hips like a mirror (raise your right hand and the figure's right hand,
    /// on the screen's right, goes up), scaled so the torso is always <see cref="torsoLength"/> (a child and an adult
    /// get the same figure). Up/down and sideways come from the picture, depth (reaching toward the TV = forward)
    /// from the camera AI's 3D joints. Every joint runs through a <see cref="HandInterpolator"/> on the camera's clock,
    /// so the figure moves smoothly every frame instead of in 30 Hz steps.
    ///
    /// The sword follows the sword arm's forearm (elbow to wrist), leaning a little into the scene: the arm is the
    /// sword. A round shield rides on the other hand, facing the knights.
    /// </summary>
    public sealed class GhostAvatar : MonoBehaviour
    {
        [Header("Look")]
        [SerializeField, Tooltip("Tundu/Ghost, colour material (queue 3000).")] Material ghostMaterial;
        [SerializeField, Tooltip("Tundu/Ghost, depth material (colour mask 0, z write, queue 2999).")] Material depthMaterial;
        [SerializeField] Material bladeMaterial;
        [SerializeField] Material guardMaterial;
        [SerializeField] Material gripMaterial;
        [SerializeField, Tooltip("Tundu/Fx additive: the swoosh behind the tip.")] Material trailMaterial;
        [SerializeField] Material shieldMaterial;
        [SerializeField] Material shieldRimMaterial;

        [Header("Body")]
        [SerializeField, Tooltip("Metres from the shoulders to the hips: everyone gets the same figure.")]
        float torsoLength = 0.52f;
        [SerializeField] float hipHeight = 0.95f;
        [SerializeField, Range(0f, 1.5f), Tooltip("How much of the camera AI's depth is used (it is noisier than up/down).")]
        float depthAmount = 0.8f;

        [Header("Sword")]
        [SerializeField] float bladeLength = 0.95f;
        [SerializeField, Range(0f, 1f), Tooltip("Leans the sword into the scene, toward the knights.")]
        float swordLean = 0.45f;
        [SerializeField, Tooltip("Degrees per second the sword can turn at most.")]
        float maxTurnDegrees = 1500f;

        [Header("Shield")]
        [SerializeField, Tooltip("Metres.")] float shieldRadius = 0.3f;

        [Header("Smoothing (like BodyHands' cursor)")]
        [SerializeField, Range(0f, 1f)] float delayFrames = 0.5f;
        [SerializeField] float minCutoff = 1f;
        [SerializeField, Tooltip("Per metre per second.")] float beta = 2.5f;

        /// <summary>The sword is in the right hand (the left one when false).</summary>
        public bool RightHanded { get; set; } = true;
        /// <summary>The camera sees the player (false: a resting pose).</summary>
        public bool Tracked { get; private set; }
        public Vector3 HiltWorld { get; private set; }
        public Vector3 TipWorld { get; private set; }
        public Vector3 ChestWorld => transform.TransformPoint(Vector3.Lerp(Mid(Joint.LeftShoulder, Joint.RightShoulder), Mid(Joint.LeftHip, Joint.RightHip), 0.35f));
        public Vector3 ShoulderWorld => transform.TransformPoint(Mid(Joint.LeftShoulder, Joint.RightShoulder));
        public Vector3 HeadWorld => transform.TransformPoint(_head);
        public Transform SwordTransform => _sword;
        public Vector3 ShieldCenterWorld => _shield.position;
        public float ShieldRadius => shieldRadius;

        /// <summary>Keyboard play: the sword arm is moved by the game (hand, elbow in the figure's space). Null = the camera.</summary>
        public (Vector3 hand, Vector3 elbow)? Puppet { get; set; }
        /// <summary>Keyboard play: the shield arm, the same way.</summary>
        public (Vector3 hand, Vector3 elbow)? ShieldPuppet { get; set; }

        const int Joints = Skeleton.JointCount;
        readonly HandInterpolator[] _tracks = new HandInterpolator[Joints];
        readonly Vector3[] _pose = new Vector3[Joints];
        readonly float[] _visibility = new float[Joints];
        double _lastSample;
        Vector3 _head, _swordDir = Vector3.up;
        Material _ghost, _ghostHead;
        float _flashUntil, _flashLength;
        Color _flashColor;

        sealed class Part
        {
            public Transform T;
            public Renderer R;
            public Joint A, B;
            public float Radius;
        }

        Part[] _limbs;
        Transform _torso, _headT, _neck, _sword, _shield;
        TrailRenderer _trail;

        void Awake()
        {
            for (int i = 0; i < Joints; i++)
            {
                _tracks[i] = new HandInterpolator(minCutoff, beta, 2f) { Margin = delayFrames, MaxExtrapolation = 0.08f };
                _visibility[i] = 1f;
            }
            RestPose();
            Build();
        }

        void OnDestroy()
        {
            if (_ghost != null) Destroy(_ghost);
            if (_ghostHead != null) Destroy(_ghostHead);
        }

        // ---------------------------------------------------------------- pose

        void Update()
        {
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            double now = Time.unscaledTimeAsDouble;
            float dt = Time.unscaledDeltaTime;
            Tracked = body != null && body.IsTracking && body.TorsoLength > 0.02f;
            if (Tracked)
            {
                if (body.SampleTime != _lastSample)
                {
                    _lastSample = body.SampleTime;
                    Sample(body, now);
                }
                for (int i = 0; i < Joints; i++)
                    if (_tracks[i].HasSample) _pose[i] = _tracks[i].Evaluate(now, dt, out _);
            }
            else
            {
                foreach (var t in _tracks) t.Reset();
                RelaxToRest(dt);
            }

            if (Puppet is { } puppet)
            {
                _pose[(int)SwordWrist] = puppet.hand;
                _pose[(int)SwordElbow] = puppet.elbow;
            }
            if (ShieldPuppet is { } shield)
            {
                _pose[(int)ShieldWrist] = shield.hand;
                _pose[(int)ShieldElbow] = shield.elbow;
            }
            Apply();
            UpdateFlash();
        }

        Joint SwordWrist => RightHanded ? Joint.RightWrist : Joint.LeftWrist;
        Joint SwordElbow => RightHanded ? Joint.RightElbow : Joint.LeftElbow;
        Joint ShieldWrist => RightHanded ? Joint.LeftWrist : Joint.RightWrist;
        Joint ShieldElbow => RightHanded ? Joint.LeftElbow : Joint.RightElbow;

        /// <summary>One camera sample: every joint around the hips, mirrored, scaled to the figure.</summary>
        void Sample(Body body, double now)
        {
            Vector2 hips = body.HipCenter;              // the smooth body anchors the figure (no shaking)
            float k = torsoLength / body.TorsoLength;
            float kz = body.HasWorld && body.WorldTorsoLength > 0.05f ? torsoLength / body.WorldTorsoLength * depthAmount : 0f;
            float hipsZ = body.HasWorld ? body.WorldHipCenter.z : 0f;
            for (int i = 0; i < Joints; i++)
            {
                Vector3 raw = body.Raw[i];
                float z = kz > 0f ? -(body.World[i].z - hipsZ) * kz : 0f;   // toward the TV = forward, into the scene
                _tracks[i].Sample(body.SampleTime, new Vector3((raw.x - hips.x) * k, (raw.y - hips.y) * k + hipHeight, z), false, now);
                _visibility[i] = Mathf.Lerp(_visibility[i], body.Visibility[i], 0.25f);
            }
        }

        static readonly Vector3[] Rest =
        {
            new Vector3(0f, 1.58f, 0.02f), new Vector3(-0.03f, 1.62f, 0f), new Vector3(0.03f, 1.62f, 0f),
            new Vector3(-0.08f, 1.6f, -0.03f), new Vector3(0.08f, 1.6f, -0.03f),
            new Vector3(-0.19f, 1.42f, 0f), new Vector3(0.19f, 1.42f, 0f),
            new Vector3(-0.27f, 1.1f, 0.12f), new Vector3(0.26f, 1.08f, 0.1f),
            new Vector3(-0.2f, 1.2f, 0.3f), new Vector3(0.3f, 1.32f, 0.28f),
            new Vector3(-0.11f, 0.95f, 0f), new Vector3(0.11f, 0.95f, 0f),
            new Vector3(-0.12f, 0.52f, 0.02f), new Vector3(0.12f, 0.52f, 0.02f),
            new Vector3(-0.12f, 0.08f, 0f), new Vector3(0.12f, 0.08f, 0f),
        };

        void RestPose()
        {
            for (int i = 0; i < Joints; i++) _pose[i] = Mirror(i, Rest[i]);
        }

        /// <summary>Waiting: ease into a ready stance (sword up, shield in front), so a lost player doesn't freeze mid-swing.</summary>
        void RelaxToRest(float dt)
        {
            float k = 1f - Mathf.Exp(-4f * dt);
            for (int i = 0; i < Joints; i++)
            {
                _pose[i] = Vector3.Lerp(_pose[i], Mirror(i, Rest[i]), k);
                _visibility[i] = Mathf.Lerp(_visibility[i], 1f, k);
            }
        }

        /// <summary>The rest pose holds the sword in the right hand; for a left-handed player the arms swap.</summary>
        Vector3 Mirror(int i, Vector3 p)
        {
            if (RightHanded || i < (int)Joint.LeftShoulder || i > (int)Joint.RightWrist) return p;
            int other = (i - (int)Joint.LeftShoulder) % 2 == 0 ? i + 1 : i - 1;
            var q = Rest[other];
            return new Vector3(-q.x, q.y, q.z);
        }

        Vector3 P(Joint j) => _pose[(int)j];
        Vector3 Mid(Joint a, Joint b) => (P(a) + P(b)) * 0.5f;

        void Apply()
        {
            foreach (var limb in _limbs)
            {
                bool seen = Mathf.Min(_visibility[(int)limb.A], _visibility[(int)limb.B]) > 0.25f;
                limb.R.enabled = seen;
                if (!seen) continue;
                if (limb.A != limb.B) Place(limb.T, P(limb.A), P(limb.B), limb.Radius);
                else
                {
                    limb.T.localPosition = P(limb.A);
                    limb.T.localScale = Vector3.one * (limb.Radius * 2f);
                }
            }

            Vector3 shoulders = Mid(Joint.LeftShoulder, Joint.RightShoulder), hips = Mid(Joint.LeftHip, Joint.RightHip);
            float width = Mathf.Max(0.18f, (P(Joint.LeftShoulder) - P(Joint.RightShoulder)).magnitude);
            _torso.localPosition = (shoulders + hips) * 0.5f;
            var up = shoulders - hips;
            var side = P(Joint.RightShoulder) - P(Joint.LeftShoulder);
            var facing = Vector3.Cross(side, up);
            _torso.localRotation = facing.sqrMagnitude > 1e-6f && up.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing, up) : Quaternion.identity;
            _torso.localScale = new Vector3(width * 1.05f, up.magnitude * 0.62f, 0.24f);

            var ears = Mid(Joint.LeftEar, Joint.RightEar);
            _head = Vector3.Lerp(ears, P(Joint.Nose), 0.25f) + Vector3.up * 0.04f;
            if (_visibility[(int)Joint.LeftEar] + _visibility[(int)Joint.RightEar] < 0.6f) _head = shoulders + up.normalized * 0.26f;
            _headT.localPosition = _head;
            _headT.localScale = Vector3.one * 0.24f;
            Place(_neck, shoulders, _head, 0.05f);

            // The sword: from the fist along the forearm, leaning toward the knights.
            Vector3 hand = P(SwordWrist), forearm = hand - P(SwordElbow);
            // Turning is capped: a forearm pointing at the TV is short and its direction unstable, and the sword flipped
            // across the screen in one frame (tip jumps of 20-35 screen heights/s = phantom swings). A real wrist flick
            // turns it well under this speed.
            if (forearm.sqrMagnitude > 0.0025f)
                _swordDir = Vector3.RotateTowards(_swordDir, (forearm.normalized + Vector3.forward * swordLean).normalized,
                                                  maxTurnDegrees * Mathf.Deg2Rad * Time.unscaledDeltaTime, 0f);
            Vector3 grip = hand + (forearm.sqrMagnitude > 1e-6f ? forearm.normalized * 0.05f : Vector3.zero);
            _sword.localPosition = grip;
            _sword.localRotation = Quaternion.FromToRotation(Vector3.up, _swordDir);
            HiltWorld = transform.TransformPoint(grip + _swordDir * 0.1f);
            TipWorld = transform.TransformPoint(grip + _swordDir * (0.1f + bladeLength));

            // The shield: just in front of the other hand, facing the knights, tipped a little with the forearm.
            Vector3 shieldHand = P(ShieldWrist), shieldArm = shieldHand - P(ShieldElbow);
            Vector3 along = shieldArm.sqrMagnitude > 1e-6f ? shieldArm.normalized : Vector3.up;
            _shield.localPosition = shieldHand + Vector3.forward * 0.07f;
            _shield.localRotation = Quaternion.FromToRotation(Vector3.up, (Vector3.forward + along * 0.25f).normalized);
        }

        static void Place(Transform t, Vector3 a, Vector3 b, float radius)
        {
            var d = b - a;
            float length = d.magnitude;
            t.localPosition = (a + b) * 0.5f;
            t.localRotation = length > 1e-4f ? Quaternion.FromToRotation(Vector3.up, d / length) : Quaternion.identity;
            t.localScale = new Vector3(radius * 2f, (length + radius * 2f) * 0.5f, radius * 2f);
        }

        /// <summary>The trail behind the tip: on while swinging.</summary>
        public void SetSwinging(bool swinging) => _trail.emitting = swinging;

        /// <summary>Tints the whole figure for a moment (red: hit, gold: a good block).</summary>
        public void Flash(Color color, float seconds)
        {
            _flashColor = color;
            _flashLength = seconds;
            _flashUntil = Time.time + seconds;
        }

        void UpdateFlash()
        {
            if (_ghost == null) return;
            float left = _flashUntil - Time.time;
            var c = _flashColor;
            c.a = left > 0f ? 0.75f * left / Mathf.Max(0.01f, _flashLength) : 0f;
            _ghost.SetColor("_Flash", c);
            _ghostHead.SetColor("_Flash", c);
        }

        // ---------------------------------------------------------------- building

        void Build()
        {
            _ghost = ghostMaterial != null ? new Material(ghostMaterial) : null;
            var materials = _ghost != null && depthMaterial != null ? new[] { depthMaterial, _ghost } : null;

            Transform Shape(PrimitiveType type, string name, Transform parent, Material[] mats)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                Destroy(go.GetComponent<Collider>());
                var r = go.GetComponent<Renderer>();
                if (mats != null) r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.transform.SetParent(parent, false);
                return go.transform;
            }

            (Joint a, Joint b, float r, string name)[] limbs =
            {
                (Joint.LeftShoulder, Joint.LeftElbow, 0.055f, "Upper arm L"), (Joint.LeftElbow, Joint.LeftWrist, 0.045f, "Forearm L"),
                (Joint.RightShoulder, Joint.RightElbow, 0.055f, "Upper arm R"), (Joint.RightElbow, Joint.RightWrist, 0.045f, "Forearm R"),
                (Joint.LeftWrist, Joint.LeftWrist, 0.06f, "Hand L"), (Joint.RightWrist, Joint.RightWrist, 0.06f, "Hand R"),
                (Joint.LeftShoulder, Joint.RightShoulder, 0.06f, "Shoulders"),
                (Joint.LeftHip, Joint.LeftKnee, 0.075f, "Thigh L"), (Joint.LeftKnee, Joint.LeftAnkle, 0.06f, "Shin L"),
                (Joint.RightHip, Joint.RightKnee, 0.075f, "Thigh R"), (Joint.RightKnee, Joint.RightAnkle, 0.06f, "Shin R"),
            };
            _limbs = new Part[limbs.Length];
            for (int i = 0; i < limbs.Length; i++)
            {
                var (a, b, r, name) = limbs[i];
                var t = Shape(a == b ? PrimitiveType.Sphere : PrimitiveType.Capsule, name, transform, materials);
                _limbs[i] = new Part { T = t, R = t.GetComponent<Renderer>(), A = a, B = b, Radius = r };
            }
            _torso = Shape(PrimitiveType.Sphere, "Torso", transform, materials);
            // the head is fainter: it sits right in front of the knight
            _ghostHead = ghostMaterial != null ? new Material(ghostMaterial) : null;
            if (_ghostHead != null)
            {
                var c = _ghostHead.GetColor("_BaseColor");
                _ghostHead.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.16f));
                var rim = _ghostHead.GetColor("_RimColor");
                _ghostHead.SetColor("_RimColor", new Color(rim.r, rim.g, rim.b, 0.4f));
            }
            _headT = Shape(PrimitiveType.Sphere, "Head", transform, _ghostHead != null && depthMaterial != null ? new[] { depthMaterial, _ghostHead } : null);
            _neck = Shape(PrimitiveType.Capsule, "Neck", transform, materials);

            // A toy sword: grip, a round guard, a broad blade with a rounded tip.
            _sword = new GameObject("Sword").transform;
            _sword.SetParent(transform, false);
            void Piece(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 scale, bool shadow = true)
            {
                var p = Shape(type, name, _sword, m != null ? new[] { m } : null);
                p.localPosition = pos;
                p.localScale = scale;
                p.GetComponent<Renderer>().shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            Piece(PrimitiveType.Cylinder, "Grip", gripMaterial, new Vector3(0f, 0f, 0f), new Vector3(0.045f, 0.09f, 0.045f));
            Piece(PrimitiveType.Sphere, "Pommel", guardMaterial, new Vector3(0f, -0.1f, 0f), Vector3.one * 0.07f);
            Piece(PrimitiveType.Cube, "Guard", guardMaterial, new Vector3(0f, 0.1f, 0f), new Vector3(0.26f, 0.05f, 0.07f));
            Piece(PrimitiveType.Cube, "Blade", bladeMaterial, new Vector3(0f, 0.1f + bladeLength * 0.47f, 0f), new Vector3(0.085f, bladeLength * 0.94f, 0.025f));
            Piece(PrimitiveType.Sphere, "Tip", bladeMaterial, new Vector3(0f, 0.1f + bladeLength * 0.94f, 0f), new Vector3(0.085f, 0.1f, 0.025f));

            // A round shield: a rim, the face and a sun emblem (the cylinder's axis is the shield's facing).
            _shield = new GameObject("Shield").transform;
            _shield.SetParent(transform, false);
            void ShieldPiece(string name, Material m, float y, float diameter, float thickness)
            {
                var p = Shape(PrimitiveType.Cylinder, name, _shield, m != null ? new[] { m } : null);
                p.localPosition = new Vector3(0f, y, 0f);
                p.localScale = new Vector3(diameter, thickness, diameter);
                p.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            }
            // (the face is thicker than the rim, so it shows on both sides with the rim as a ring around it)
            ShieldPiece("Rim", shieldRimMaterial, 0f, shieldRadius * 2f, 0.01f);
            ShieldPiece("Face", shieldMaterial, 0f, shieldRadius * 1.82f, 0.016f);
            ShieldPiece("Emblem", guardMaterial, 0.018f, shieldRadius * 0.8f, 0.006f);
            ShieldPiece("Emblem (inside)", guardMaterial, -0.018f, shieldRadius * 0.8f, 0.006f);   // the player sees this side

            var tip = new GameObject("Trail").transform;
            tip.SetParent(_sword, false);
            tip.localPosition = new Vector3(0f, 0.1f + bladeLength * 0.8f, 0f);
            _trail = tip.gameObject.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = trailMaterial;
            _trail.time = 0.14f;
            _trail.minVertexDistance = 0.02f;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.32f), new Keyframe(1f, 0f));
            _trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(0.2f, 0.78f, 0.96f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            _trail.shadowCastingMode = ShadowCastingMode.Off;
            _trail.emitting = false;
            Apply();
        }
    }
}
