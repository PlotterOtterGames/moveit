using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// A toy tennis player, Mii-like: a big round head with a face and one of five hairstyles, a headband, the team's
    /// shirt, shorts, socks and sneakers, and a racket. Built from simple shapes merged into six meshes (body, two legs,
    /// two arms, the racket) with vertex colours (Tundu/Toon). <see cref="TennisGame"/> moves it to its
    /// <see cref="CourtPlayer"/>'s place every frame and this animates what the rules ask for: the ready stance, running
    /// (legs, lean), pulling the racket back as a ball comes (<see cref="CourtPlayer.Preparing"/>, like the Miis), a
    /// forehand, backhand or overhead swing whose racket meets the ball at the contact moment the rules give, a dive for
    /// a ball out of reach, a cheer or a sulk between points. Its local +z faces the net, +x is its right; the root is at
    /// its feet.
    /// <para>The racket is its own object held in the hand, posed by three directions (<see cref="Pose"/>): the arm, the
    /// handle, and the way the strings face (toward the net when ready and at the hit, so the camera behind sees the
    /// strings). A swing always starts from the pose the arm is in (no jumps), and a quick one shortens its backswing.</para>
    /// </summary>
    public sealed class TennisPlayerView : MonoBehaviour
    {
        public struct Look
        {
            public Color Shirt, Accent, Shorts, Skin, Hair, Shoes;
            public int HairStyle;
        }

        public const float ShoulderHeight = 1.18f;
        const float ArmLength = 0.4f, HeadCenter = 0.47f;
        /// <summary>How fast the racket arm turns (degrees per second) outside the hit: quicker looks like a jump (at 60 fps
        /// the racket's head would move more than ~0.15 m a frame).</summary>
        const float MaxTurn = 600f;
        /// <summary>The hit itself (backswing to the ball) takes this long; the backswing before it as long as turning
        /// there takes at <see cref="WindUpTurn"/> degrees per second.</summary>
        const float HitSeconds = 0.12f, WindUpTurn = 450f;
        const float FollowSeconds = 0.25f, ReturnSeconds = 0.35f;

        /// <summary>The racket arm and the racket, in the model's frame: where the arm points (shoulder to hand), where the
        /// handle points (hand to the racket's head) and the way the strings face.</summary>
        struct Pose
        {
            public Vector3 Arm, Handle, Face;

            public static Pose Lerp(Pose a, Pose b, float t) => new Pose
            {
                Arm = Vector3.Slerp(a.Arm, b.Arm, t), Handle = Vector3.Slerp(a.Handle, b.Handle, t), Face = Vector3.Slerp(a.Face, b.Face, t),
            };

            /// <summary>Eases toward <paramref name="b"/> (<paramref name="follow"/> of the way), turning at most
            /// <paramref name="maxDegrees"/>.</summary>
            public static Pose Toward(Pose a, Pose b, float follow, float maxDegrees) => new Pose
            {
                Arm = Turn(a.Arm, b.Arm, follow, maxDegrees), Handle = Turn(a.Handle, b.Handle, follow, maxDegrees),
                Face = Turn(a.Face, b.Face, follow, maxDegrees * 2f),
            };

            static Vector3 Turn(Vector3 from, Vector3 to, float follow, float maxDegrees)
            {
                float step = Mathf.Min(Vector3.Angle(from, to) * follow, maxDegrees);
                return Vector3.RotateTowards(from, to, step * Mathf.Deg2Rad, 0f).normalized;
            }

            /// <summary>How far apart two poses are: the bigger turn of the arm and the handle (degrees).</summary>
            public static float Degrees(Pose a, Pose b) => Mathf.Max(Vector3.Angle(a.Arm, b.Arm), Vector3.Angle(a.Handle, b.Handle));
        }

        /// <summary>A pose for a right-handed player, mirrored by <paramref name="side"/> (-1 = left-handed).</summary>
        static Pose P(float side, Vector3 arm, Vector3 handle, Vector3 face) => new Pose
        {
            Arm = new Vector3(arm.x * side, arm.y, arm.z).normalized,
            Handle = new Vector3(handle.x * side, handle.y, handle.z).normalized,
            Face = new Vector3(face.x * side, face.y, face.z).normalized,
        };

        // racket in front, head up, strings to the net
        static Pose Ready(float s) => P(s, new Vector3(0.3f, -0.5f, 0.8f), new Vector3(-0.25f, 0.6f, 0.75f), new Vector3(0f, -0.6f, 0.8f));
        // the toss: racket up behind the shoulder
        static Pose ServeStance(float s) => P(s, new Vector3(0.5f, 0.2f, -0.3f), new Vector3(0.2f, 0.9f, -0.25f), new Vector3(0f, 0f, 1f));
        static Pose Cheer(float s) => P(s, new Vector3(0.3f, 0.95f, 0.1f), new Vector3(0.1f, 1f, 0.15f), new Vector3(0f, 0f, 1f));
        static Pose Sulk(float s) => P(s, new Vector3(0.15f, -0.98f, 0.12f), new Vector3(0.1f, -0.8f, 0.6f), new Vector3(1f, 0f, 0f));

        /// <summary>The backswing, the hit and the follow-through of a stroke.</summary>
        static (Pose back, Pose contact, Pose follow, float twistBack, float twistFollow) Stroke3(Stroke stroke, float s) => stroke switch
        {
            Stroke.Backhand => (P(s, new Vector3(-0.75f, -0.05f, -0.55f), new Vector3(-0.4f, 0.55f, -0.7f), new Vector3(-1f, 0f, 0.2f)),
                                P(s, new Vector3(-0.8f, -0.3f, 0.45f), new Vector3(-0.8f, 0.08f, 0.6f), new Vector3(0f, 0f, 1f)),
                                P(s, new Vector3(0.5f, 0.5f, 0.65f), new Vector3(0.55f, 0.8f, -0.15f), new Vector3(0f, 0f, 1f)),
                                s * 55f, -s * 25f),
            Stroke.Overhead => (P(s, new Vector3(0.4f, 0.45f, -0.6f), new Vector3(0.1f, -0.7f, -0.7f), new Vector3(1f, 0f, 0f)),
                                P(s, new Vector3(0.15f, 0.97f, 0.2f), new Vector3(0.05f, 0.85f, 0.5f), new Vector3(0f, -0.5f, 1f)),
                                P(s, new Vector3(-0.35f, -0.55f, 0.75f), new Vector3(-0.45f, -0.5f, 0.7f), new Vector3(0f, 0.5f, 1f)),
                                -s * 25f, s * 20f),
            _ => (P(s, new Vector3(0.8f, -0.15f, -0.55f), new Vector3(0.35f, 0.55f, -0.75f), new Vector3(1f, 0f, 0.2f)),
                  P(s, new Vector3(0.8f, -0.35f, 0.45f), new Vector3(0.8f, 0.08f, 0.6f), new Vector3(0f, 0f, 1f)),
                  P(s, new Vector3(-0.5f, 0.5f, 0.65f), new Vector3(-0.55f, 0.8f, -0.15f), new Vector3(0f, 0f, 1f)),
                  -s * 50f, s * 30f),
        };

        Transform _model, _armR, _armL, _legR, _legL, _racket, _racketTip;
        Renderer[] _renderers;
        MaterialPropertyBlock _block;
        TrailRenderer _trail;
        float _t, _flash, _bank, _twist, _height, _lean;
        Color _flashColor = Color.white;
        int _lastSwings = -1;
        // the swing under way: from the pose it started in, the backswing until _windUpEnd, the hit at _contact
        double _swingStart = -10, _windUpEnd = -10, _contact = -10;
        Pose _swingFrom, _pose;
        float _swingTwistFrom;
        Stroke _stroke;
        Vector3 _free = new Vector3(-0.3f, -0.75f, 0.45f);
        bool _poseSet;
        Vector3 _lastPosition;
        float _stride;

        /// <summary>The racket head's middle (world), for sparks at a hit.</summary>
        public Vector3 RacketWorld => _racketTip.position;
        /// <summary>A swing is being drawn (tests).</summary>
        public bool Swinging { get; private set; }

        public static TennisPlayerView Create(string name, Material actor, Material trail, Look look)
        {
            var go = new GameObject(name);
            var v = go.AddComponent<TennisPlayerView>();
            v.Build(actor, trail, look);
            return v;
        }

        public void Flash(Color color, float amount = 1f)
        {
            _flashColor = color;
            _flash = amount;
        }

        /// <summary>Called by the game every frame with the rules' player and the match clock.</summary>
        public void Animate(CourtPlayer p, double now, float dt, bool rightHanded)
        {
            _t += dt;
            _flash = Mathf.Max(0f, _flash - dt * 3f);
            float side = rightHanded ? 1f : -1f;

            // facing the net, banking into runs
            var world = new Vector3(p.Position.x, 0f, p.Position.y);
            var velocity = dt > 0f ? (world - _lastPosition) / dt : Vector3.zero;
            if (velocity.sqrMagnitude > 400f) velocity = Vector3.zero;   // a teleport between points
            _lastPosition = world;
            transform.SetPositionAndRotation(world, Quaternion.Euler(0f, p.Team == 0 ? 0f : 180f, 0f));
            var local = transform.InverseTransformDirection(velocity);
            float speed = new Vector2(local.x, local.z).magnitude;
            float ease = 1f - Mathf.Exp(-10f * dt);
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-local.x * 2.2f, -16f, 16f), ease);
            float lean = Mathf.Clamp(local.z * 2f, -10f, 14f);

            var shoulder = rightHanded ? _armR : _armL;
            if (!_poseSet)
            {
                _pose = Ready(side);
                _poseSet = true;
            }
            if (p.Swings != _lastSwings)
            {
                if (_lastSwings >= 0) StartSwing(now, p, side);
                _lastSwings = p.Swings;
            }

            // running legs (stride follows the speed)
            _stride += dt * Mathf.Lerp(0f, 16f, Mathf.Clamp01(speed / 6f));
            float legSwing = Mathf.Sin(_stride) * Mathf.Clamp01(speed / 2f) * 38f;
            float bob = Mathf.Abs(Mathf.Sin(_stride)) * Mathf.Clamp01(speed / 3f) * 0.06f;
            float crouch = 0.04f + 0.015f * Mathf.Sin(_t * 6f);   // ready: bouncing on its toes

            var free = new Vector3(-side * 0.3f, -0.75f, 0.45f);
            float tilt = 0f, jump = 0f;
            Swinging = now < _contact + FollowSeconds + ReturnSeconds;
            if (Swinging)
            {
                // exact along the swing (its first frame continues from where the arm was)
                SwingPose(now, side, out _pose, out _twist, ref free);
            }
            else
            {
                var target = Ready(side);
                float twist = 0f;
                if (p.Preparing)
                {
                    // the ball is coming, or tossed to serve: the racket goes back and waits (only the hit is left)
                    var stroke = Stroke3(p.PrepareStroke, side);
                    target = stroke.back;
                    twist = stroke.twistBack * 0.6f;
                }
                else if (p.Serving) target = ServeStance(side);
                if (p.Serving) free = new Vector3(-side * 0.15f, 0.05f, 0.98f);   // the ball held out in front

                if (p.DiveAt > 0 && now > p.DiveAt - 0.3 && now < p.DiveAt + 1.2)
                {
                    // a lunge toward the ball: tipped over sideways, racket stretched out, then back up
                    float k = (float)(now - (p.DiveAt - 0.3));
                    float d = k < 0.35f ? Mathf.SmoothStep(0f, 1f, k / 0.35f) : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.7f) / 0.5f);
                    float toward = Mathf.Sign(local.x == 0f ? side : local.x);
                    tilt = -toward * 55f * d;
                    var reach = P(1f, new Vector3(toward * 0.95f, 0.1f, 0.3f), new Vector3(toward * 0.9f, 0.15f, 0.4f), new Vector3(0f, 0f, 1f));
                    target = Pose.Lerp(target, reach, d);
                    crouch += 0.15f * d;
                }
                if (p.Mood > 0)
                {
                    // cheer: hops with the racket up
                    jump = Mathf.Abs(Mathf.Sin(_t * 7f)) * 0.22f;
                    target = Cheer(side);
                    free = new Vector3(-side * 0.35f, 0.9f, 0.1f);
                }
                else if (p.Mood < 0)
                {
                    // sulk: head down, arms hanging
                    target = Sulk(side);
                    free = new Vector3(-side * 0.15f, -0.98f, 0.05f);
                    lean = 14f;
                    crouch = 0.06f;
                }
                float follow = 1f - Mathf.Exp(-14f * dt);
                _pose = Pose.Toward(_pose, target, follow, MaxTurn * dt);
                _twist = Mathf.MoveTowards(_twist, Mathf.Lerp(_twist, twist, follow), 240f * dt);
            }
            float freeTurn = Vector3.Angle(_free, free) * (Swinging ? 1f - Mathf.Exp(-20f * dt) : ease);
            _free = Vector3.RotateTowards(_free, free.normalized, Mathf.Min(freeTurn, MaxTurn * dt) * Mathf.Deg2Rad, 0f).normalized;

            // the body's height and lean change smoothly too: a cheer's hop or a sulk switched on and off at once (the
            // end of a point, the next serve) made the whole player pop, racket and all
            _height = Mathf.MoveTowards(_height, -crouch + bob + jump, 3f * dt);
            _lean = Mathf.MoveTowards(_lean, lean, 120f * dt);
            _model.localPosition = new Vector3(0f, _height, 0f);
            _model.localRotation = Quaternion.Euler(_lean, _twist, _bank + tilt);
            _legL.localRotation = Quaternion.Euler(legSwing, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
            Aim(shoulder, _pose.Arm);
            Aim(rightHanded ? _armL : _armR, _free);
            // the racket in the playing hand: the handle from the fist, the strings facing the pose's way
            var hand = shoulder.localPosition + _pose.Arm * ArmLength;
            var faceUp = _pose.Face - _pose.Handle * Vector3.Dot(_pose.Face, _pose.Handle);
            if (faceUp.sqrMagnitude < 1e-4f) faceUp = Vector3.Cross(_pose.Handle, Vector3.right);
            _racket.localPosition = hand - _pose.Handle * 0.05f;
            _racket.localRotation = Quaternion.LookRotation(_pose.Handle, faceUp.normalized);
            _trail.emitting = Swinging && System.Math.Abs(now - _contact) < 0.16;
            ApplyFlash();
        }

        /// <summary>A new swing: from the pose the arm is in now, a backswing (held while an early swing waits for the
        /// ball; none when the racket is already back, as it is when the ball has been coming), the hit, the
        /// follow-through. A swing with too little time gets what it needs: its hit is drawn a little after the rules'
        /// (an on-time swing reaches the game ~0.1 s late through the camera anyway), never a jump.</summary>
        void StartSwing(double now, CourtPlayer p, float side)
        {
            _swingFrom = _pose;
            _swingTwistFrom = _twist;
            _stroke = p.SwingStroke;
            _swingStart = now;
            double windUp = Pose.Degrees(_pose, Stroke3(_stroke, side).back) / WindUpTurn;
            _contact = System.Math.Max(p.SwingContact, now + windUp + HitSeconds);
            _windUpEnd = _contact - HitSeconds;
            _trail.Clear();
        }

        void SwingPose(double now, float side, out Pose pose, out float twist, ref Vector3 free)
        {
            var (back, contact, follow, twistBack, twistFollow) = Stroke3(_stroke, side);
            if (now < _windUpEnd)
            {
                // the backswing (or holding it back while an early swing waits for the ball)
                float k = Mathf.SmoothStep(0f, 1f, (float)((now - _swingStart) / System.Math.Max(1e-3, _windUpEnd - _swingStart)));
                pose = Pose.Lerp(_swingFrom, back, k);
                twist = Mathf.Lerp(_swingTwistFrom, twistBack, k);
            }
            else if (now < _contact)
            {
                // the hit, evenly paced (an accelerating one moved the head ~0.4 m in its last frame)
                float k = (float)((now - _windUpEnd) / System.Math.Max(1e-3, _contact - _windUpEnd));
                k *= 0.6f + 0.4f * k;
                pose = Pose.Lerp(back, contact, k);
                twist = Mathf.Lerp(twistBack, 0f, k);
            }
            else if (now < _contact + FollowSeconds)
            {
                float x = (float)(now - _contact) / FollowSeconds;
                float k = 1f - (1f - x) * (1f - x);
                pose = Pose.Lerp(contact, follow, k);
                twist = Mathf.Lerp(0f, twistFollow, k);
            }
            else
            {
                float k = Mathf.SmoothStep(0f, 1f, (float)(now - _contact - FollowSeconds) / ReturnSeconds);
                pose = Pose.Lerp(follow, Ready(side), k);
                twist = Mathf.Lerp(twistFollow, 0f, k);
            }
            free = _stroke == Stroke.Overhead && now < _contact ? new Vector3(-side * 0.2f, 0.9f, 0.4f) : new Vector3(-side * 0.6f, -0.3f, 0.6f);
        }

        /// <summary>Points an arm (built along +z) along <paramref name="dir"/>. Its roll doesn't show (it's round).</summary>
        static void Aim(Transform arm, Vector3 dir)
        {
            dir.Normalize();
            var up = Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up;
            arm.localRotation = Quaternion.LookRotation(dir, up);
        }

        void ApplyFlash()
        {
            _block.SetColor("_FlashColor", new Color(_flashColor.r, _flashColor.g, _flashColor.b, _flash * 0.7f));
            foreach (var r in _renderers) r.SetPropertyBlock(_block);
        }

        // ------------------------------------------------------------------------------------------------ building


        static readonly Color Dark = new Color(0.12f, 0.12f, 0.2f), White = new Color(0.97f, 0.97f, 1f);

        void Build(Material actor, Material trailMaterial, Look look)
        {
            _block = new MaterialPropertyBlock();
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);

            Transform Pivot(string n, Transform parent, Vector3 pos)
            {
                var t = new GameObject(n).transform;
                t.SetParent(parent, false);
                t.localPosition = pos;
                return t;
            }

            var head = Matrix4x4.TRS(new Vector3(0f, 1.5f, 0f), Quaternion.identity, Vector3.one * 0.46f);
            Matrix4x4 H(Vector3 p, Vector3 s, Vector3 euler = default) => head * Matrix4x4.TRS(p, Quaternion.Euler(euler), s);
            var cheek = new Color(1f, 0.62f, 0.62f);
            var body = new PartMesh { Srgb = true }
                // a round shirt with a collar and a stripe, shorts
                .Add(PrimitiveType.Capsule, new Vector3(0f, 0.98f, 0f), new Vector3(0.44f, 0.3f, 0.32f), look.Shirt, 0.2f)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 1.2f, 0f), new Vector3(0.24f, 0.025f, 0.2f), look.Accent, 0.3f)
                .Add(PrimitiveType.Cube, new Vector3(0f, 0.98f, 0.155f), new Vector3(0.34f, 0.05f, 0.02f), look.Accent, 0.3f)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0.73f, 0f), new Vector3(0.42f, 0.09f, 0.3f), look.Shorts, 0.2f)
                .Add(PrimitiveType.Capsule, new Vector3(0f, 1.27f, 0f), new Vector3(0.11f, 0.06f, 0.11f), look.Skin, 0.1f)      // neck
                // the head: face, eyes with a sparkle, brows, nose, cheeks, a smile, ears
                .Add(PrimitiveType.Sphere, head, look.Skin, 0.12f)
                .Add(PrimitiveType.Sphere, H(new Vector3(-0.17f, 0.04f, 0.43f), new Vector3(0.1f, 0.15f, 0.08f)), Dark, 0.7f)
                .Add(PrimitiveType.Sphere, H(new Vector3(0.17f, 0.04f, 0.43f), new Vector3(0.1f, 0.15f, 0.08f)), Dark, 0.7f)
                .Add(PrimitiveType.Sphere, H(new Vector3(-0.15f, 0.08f, 0.47f), new Vector3(0.035f, 0.035f, 0.02f)), White, 0.9f)
                .Add(PrimitiveType.Sphere, H(new Vector3(0.19f, 0.08f, 0.47f), new Vector3(0.035f, 0.035f, 0.02f)), White, 0.9f)
                .Add(PrimitiveType.Cube, H(new Vector3(-0.17f, 0.17f, 0.43f), new Vector3(0.13f, 0.025f, 0.03f), new Vector3(0f, 0f, -8f)), look.Hair, 0.2f)
                .Add(PrimitiveType.Cube, H(new Vector3(0.17f, 0.17f, 0.43f), new Vector3(0.13f, 0.025f, 0.03f), new Vector3(0f, 0f, 8f)), look.Hair, 0.2f)
                .Add(PrimitiveType.Sphere, H(new Vector3(0f, -0.06f, 0.49f), new Vector3(0.08f, 0.06f, 0.06f)), look.Skin * 0.95f, 0.2f)
                .Add(PrimitiveType.Sphere, H(new Vector3(-0.28f, -0.11f, 0.38f), new Vector3(0.12f, 0.07f, 0.05f)), cheek, 0.1f)
                .Add(PrimitiveType.Sphere, H(new Vector3(0.28f, -0.11f, 0.38f), new Vector3(0.12f, 0.07f, 0.05f)), cheek, 0.1f)
                .Add(PrimitiveType.Capsule, H(new Vector3(0f, -0.19f, 0.44f), new Vector3(0.035f, 0.07f, 0.03f), new Vector3(0f, 0f, 90f)), new Color(0.55f, 0.2f, 0.25f), 0.3f)
                .Add(PrimitiveType.Sphere, H(new Vector3(-0.49f, -0.02f, 0f), new Vector3(0.12f, 0.17f, 0.1f)), look.Skin, 0.12f)
                .Add(PrimitiveType.Sphere, H(new Vector3(0.49f, -0.02f, 0f), new Vector3(0.12f, 0.17f, 0.1f)), look.Skin, 0.12f);
            AddHair(body, head, look);
            // the headband, across the forehead
            body.Add(PrimitiveType.Cylinder, H(new Vector3(0f, 0.27f, 0f), new Vector3(1.03f, 0.07f, 1.03f), new Vector3(-8f, 0f, 0f)), look.Accent, 0.4f);

            Mesh Leg(string n) => new PartMesh { Srgb = true }
                .Add(PrimitiveType.Capsule, new Vector3(0f, -0.2f, 0f), new Vector3(0.13f, 0.2f, 0.13f), look.Skin, 0.12f)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, -0.47f, 0f), new Vector3(0.12f, 0.06f, 0.12f), White, 0.3f)        // sock
                .Add(PrimitiveType.Sphere, new Vector3(0f, -0.57f, 0.05f), new Vector3(0.16f, 0.12f, 0.26f), look.Shoes, 0.6f)
                .Add(PrimitiveType.Cube, new Vector3(0f, -0.62f, 0.05f), new Vector3(0.15f, 0.03f, 0.26f), White, 0.3f)          // sole
                .Build(n);
            _legL = Pivot("Leg L", _model, new Vector3(-0.1f, 0.64f, 0f));
            _legR = Pivot("Leg R", _model, new Vector3(0.1f, 0.64f, 0f));

            // arms are built pointing along +z from the shoulder (the swing turns them)
            Mesh Arm(string n) => new PartMesh { Srgb = true }
                .Add(PrimitiveType.Sphere, new Vector3(0f, 0f, 0.02f), new Vector3(0.15f, 0.15f, 0.15f), look.Shirt, 0.2f)       // sleeve
                .Add(PrimitiveType.Capsule, new Vector3(0f, 0f, 0.2f), new Vector3(0.1f, 0.18f, 0.1f), look.Skin, 0.12f, new Vector3(90f, 0f, 0f))
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.32f), new Vector3(0.11f, 0.025f, 0.11f), White, 0.3f, new Vector3(90f, 0f, 0f))  // wristband
                .Add(PrimitiveType.Sphere, new Vector3(0f, 0f, ArmLength), Vector3.one * 0.12f, look.Skin, 0.12f)
                .Build(n);
            _armL = Pivot("Arm L", _model, new Vector3(-0.25f, ShoulderHeight, 0f));
            _armR = Pivot("Arm R", _model, new Vector3(0.25f, ShoulderHeight, 0f));

            // the racket, built along +z from the grip, its strings in the xz plane (they face +y): a dark grip, a throat,
            // an oval frame in the team's accent with light strings (a little thicker, so they show on both faces)
            var grip = new Color(0.2f, 0.2f, 0.3f);
            var frame = look.Accent;
            var racket = new PartMesh { Srgb = true }
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.1f), new Vector3(0.042f, 0.1f, 0.042f), grip, 0.4f, new Vector3(90f, 0f, 0f))
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.025f), new Vector3(0.05f, 0.015f, 0.05f), grip, 0.4f, new Vector3(90f, 0f, 0f))
                .Add(PrimitiveType.Cylinder, new Vector3(-0.03f, 0f, 0.25f), new Vector3(0.022f, 0.06f, 0.022f), frame, 0.8f, new Vector3(90f, -14f, 0f))
                .Add(PrimitiveType.Cylinder, new Vector3(0.03f, 0f, 0.25f), new Vector3(0.022f, 0.06f, 0.022f), frame, 0.8f, new Vector3(90f, 14f, 0f))
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0f, HeadCenter), new Vector3(0.27f, 0.012f, 0.36f), frame, 0.8f)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0f, HeadCenter), new Vector3(0.225f, 0.016f, 0.31f), new Color(0.95f, 0.95f, 0.88f), 0.25f)
                .Build("Tennis racket");
            _racket = Pivot("Racket", _model, Vector3.zero);
            _racketTip = Pivot("Racket head", _racket, new Vector3(0f, 0f, HeadCenter));

            MeshRenderer Show(string n, Transform parent, Mesh mesh)
            {
                var r = PartMesh.Show(n, parent, mesh, actor);
                r.shadowCastingMode = ShadowCastingMode.On;
                return r;
            }
            _renderers = new Renderer[]
            {
                Show("Body", _model, body.Build("Tennis body")),
                Show("Leg", _legL, Leg("Tennis leg L")),
                Show("Leg", _legR, Leg("Tennis leg R")),
                Show("Arm", _armL, Arm("Tennis arm L")),
                Show("Arm", _armR, Arm("Tennis arm R")),
                Show("Racket", _racket, racket),
            };

            var trailGo = new GameObject("Swoosh");
            trailGo.transform.SetParent(_racketTip, false);
            _trail = trailGo.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = trailMaterial;
            _trail.time = 0.16f;
            _trail.minVertexDistance = 0.03f;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 0f));
            _trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(look.Accent, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            _trail.shadowCastingMode = ShadowCastingMode.Off;
            _trail.emitting = false;
            _lastPosition = transform.position;
        }

        /// <summary>Five hairstyles: short and round, a bob, spikes, a ponytail, buns.</summary>
        static void AddHair(PartMesh m, Matrix4x4 head, Look look)
        {
            Matrix4x4 H(Vector3 p, Vector3 s, Vector3 euler = default) => head * Matrix4x4.TRS(p, Quaternion.Euler(euler), s);
            var c = look.Hair;
            // every style has a cap of hair over the top and back of the head
            m.Add(PrimitiveType.Sphere, H(new Vector3(0f, 0.12f, -0.05f), new Vector3(1.07f, 0.86f, 1.05f)), c, 0.35f);
            switch (look.HairStyle % 5)
            {
                case 0:   // short, a little fringe
                    m.Add(PrimitiveType.Sphere, H(new Vector3(0f, 0.3f, 0.3f), new Vector3(0.7f, 0.25f, 0.4f), new Vector3(-20f, 0f, 0f)), c, 0.35f);
                    break;
                case 1:   // a bob down to the chin
                    m.Add(PrimitiveType.Sphere, H(new Vector3(0f, -0.08f, -0.1f), new Vector3(1.14f, 0.85f, 1f)), c, 0.35f)
                     .Add(PrimitiveType.Sphere, H(new Vector3(0f, 0.3f, 0.32f), new Vector3(0.85f, 0.25f, 0.35f), new Vector3(-15f, 0f, 0f)), c, 0.35f);
                    break;
                case 2:   // spikes
                    for (int i = 0; i < 5; i++)
                    {
                        float a = (i - 2) * 22f;
                        m.Add(PrimitiveType.Capsule, H(Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 0.55f, 0f) + new Vector3(0f, 0f, 0.05f),
                                                       new Vector3(0.16f, 0.17f, 0.16f), new Vector3(-10f, 0f, a)), c, 0.4f);
                    }
                    break;
                case 3:   // a ponytail
                    m.Add(PrimitiveType.Capsule, H(new Vector3(0f, 0.1f, -0.6f), new Vector3(0.24f, 0.3f, 0.24f), new Vector3(35f, 0f, 0f)), c, 0.35f)
                     .Add(PrimitiveType.Sphere, H(new Vector3(0f, 0.3f, 0.32f), new Vector3(0.8f, 0.22f, 0.3f), new Vector3(-15f, 0f, 0f)), c, 0.35f);
                    break;
                default:  // two buns
                    m.Add(PrimitiveType.Sphere, H(new Vector3(-0.38f, 0.42f, -0.05f), Vector3.one * 0.32f), c, 0.35f)
                     .Add(PrimitiveType.Sphere, H(new Vector3(0.38f, 0.42f, -0.05f), Vector3.one * 0.32f), c, 0.35f);
                    break;
            }
        }

        void OnDestroy()
        {
            if (_renderers == null) return;
            foreach (var r in _renderers)
                if (r != null) Destroy(r.GetComponent<MeshFilter>().sharedMesh);
        }
    }
}
