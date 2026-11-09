using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// A tennis player: one of Tundu's chibis (<see cref="Chibi"/>, dressed in a <see cref="ChibiLook"/>) holding a racket
    /// in its mitten. <see cref="TennisGame"/> moves it to its
    /// <see cref="CourtPlayer"/>'s place every frame and this animates what the rules ask for: the ready stance, running
    /// (legs, lean), pulling the racket back as a ball comes (<see cref="CourtPlayer.Preparing"/>, like the Miis), a
    /// forehand, backhand or overhead swing whose racket meets the ball at the contact moment the rules give, a dive for
    /// a ball out of reach, a cheer or a sulk between points. The player's own toys work the same way (since 2026-11-06:
    /// they used to hold the racket where the camera put the hand, which shook with the pose model's noise and bent the
    /// racket at odd angles in the mitten): they swing whenever their player swings, the hit timed to the ball, any other
    /// swing straight from where the racket is (<see cref="CourtPlayer.SwingQuick"/>), each as quick as the player's hand
    /// was (<see cref="CourtPlayer.SwingPower"/>). Its local +z faces the net, +x is its right; the root is at its feet.
    /// <para>The racket is its own object held in the hand, posed by three directions (<see cref="Pose"/>): the arm, the
    /// handle, and the way the strings face (toward the net when ready and at the hit, so the camera behind sees the
    /// strings). A swing always starts from the pose the arm is in (no jumps), and a quick one shortens its backswing.</para>
    /// </summary>
    public sealed class TennisPlayerView : MonoBehaviour
    {
        const float HeadCenter = 0.47f;
        /// <summary>The racket is drawn this much bigger than its mesh: a toy's racket, big and readable on a TV, as long as
        /// the toy is tall like a Mii's (the user, 2026-11-06: "the racket bigger and the character smaller"; 2026-11-07:
        /// "bigger, and more like a tennis one with strings, not a ping-pong flat surface": 1.55 then).</summary>
        public const float RacketScale = 1.85f;
        /// <summary>The racket's head (m, drawn): across and along the handle, outside the frame (its shadow).</summary>
        public static Vector2 RacketHead => new Vector2(2f * (HeadA + FrameIn), 2f * (HeadB + FrameIn)) * RacketScale;
        /// <summary>The head's oval in the mesh (half across, half along the handle), the frame tube's half width in its
        /// plane and half thickness.</summary>
        const float HeadA = 0.135f, HeadB = 0.18f, FrameIn = 0.014f, FrameThick = 0.018f;
        /// <summary>From the mitten to the middle of the racket's head (m).</summary>
        public const float RacketReach = (HeadCenter - 0.05f) * RacketScale;
        /// <summary>A ball met further than this share of the arm and racket's full length makes the toy lunge toward it
        /// (sideways up to <see cref="MaxLunge"/> m, a hop up to <see cref="MaxHop"/> m), like a Mii stretching for it.</summary>
        const float Comfort = 0.92f, MaxLunge = 0.5f, MaxHop = 0.6f;
        /// <summary>How fast the racket arm turns (degrees per second) outside the hit: quicker looks like a jump (at 60 fps
        /// the racket's head would move more than ~0.15 m a frame).</summary>
        const float MaxTurn = 600f;
        /// <summary>The hit itself (backswing to the ball) takes from the first to the second for a gentle to a full-power
        /// swing (<see cref="CourtPlayer.SwingPower"/>; at most <see cref="LateHitSeconds"/> when the ball is there
        /// already: a player's swing reaches the game after the camera saw it); the backswing before it as long as
        /// turning there takes at <see cref="WindUpTurn"/> degrees per second.</summary>
        const float SlowHitSeconds = 0.14f, FastHitSeconds = 0.07f, LateHitSeconds = 0.08f, WindUpTurn = 450f;
        const float SlowFollowSeconds = 0.3f, FastFollowSeconds = 0.2f, ReturnSeconds = 0.35f;

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

        // ready: the racket held out on its side at the hip, its head forward and the strings up, so the camera behind
        // and above sees it beside the body (held in front of the chest, the chibi's big head hid it: 2026-11-06)
        static Pose Ready(float s) => P(s, new Vector3(0.5f, -0.72f, 0.48f), new Vector3(0.38f, 0.22f, 0.9f), new Vector3(0f, 1f, -0.2f));
        // the toss: racket up behind the shoulder
        static Pose ServeStance(float s) => P(s, new Vector3(0.5f, 0.2f, -0.3f), new Vector3(0.2f, 0.9f, -0.25f), new Vector3(0f, 0f, 1f));
        static Pose Cheer(float s) => P(s, new Vector3(0.3f, 0.95f, 0.1f), new Vector3(0.1f, 1f, 0.15f), new Vector3(0f, 0f, 1f));
        static Pose Sulk(float s) => P(s, new Vector3(0.15f, -0.98f, 0.12f), new Vector3(0.1f, -0.8f, 0.6f), new Vector3(1f, 0f, 0f));

        /// <summary>The backswing, the hit and the follow-through of a stroke.</summary>
        static (Pose back, Pose contact, Pose follow, float twistBack, float twistFollow) Stroke3(Stroke stroke, float s) => stroke switch
        {
            // up from low beside the body, past the front, the racket over the head (a player's swing straight up)
            Stroke.Scoop => (P(s, new Vector3(0.35f, -0.92f, 0.15f), new Vector3(0.25f, -0.9f, 0.35f), new Vector3(0f, 0.3f, 1f)),
                             P(s, new Vector3(0.25f, -0.5f, 0.83f), new Vector3(0.05f, 0.45f, 0.9f), new Vector3(0f, 0.75f, 0.66f)),
                             P(s, new Vector3(0.2f, 0.95f, 0.25f), new Vector3(0.1f, 0.75f, -0.65f), new Vector3(0f, 0.3f, 1f)),
                             -s * 15f, s * 10f),
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

        Transform _model, _racket, _racketTip;
        Mesh _racketOpen, _racketFilled;
        MeshFilter _racketFilter;
        Chibi _chibi;
        float _grinUntil, _headYaw, _headPitch;
        Renderer[] _renderers;
        MaterialPropertyBlock _block;
        TrailRenderer _trail;
        float _t, _flash, _bank, _twist, _height, _lean;
        Color _flashColor = Color.white;
        int _lastSwings = -1;
        CourtPlayer _player;
        // the swing under way: from the pose it started in, the backswing until _windUpEnd, the hit at _contact (a quick
        // one goes straight from where it started to the hit), its own tempo
        double _swingStart = -10, _windUpEnd = -10, _contact = -10;
        float _hitSeconds = SlowHitSeconds, _followSeconds = SlowFollowSeconds;
        bool _quick;
        Pose _swingFrom, _pose;
        float _swingTwistFrom;
        Stroke _stroke;
        // a hit reaches into the ball: where it is met (world)
        bool _reach;
        Vector3 _reachWorld;
        Vector3 _free = new Vector3(-0.3f, -0.75f, 0.45f);
        bool _poseSet;
        Vector3 _lastPosition;
        float _stride;

        /// <summary>The racket head's middle (world), for sparks at a hit and its shadow.</summary>
        public Vector3 RacketWorld => _racketTip.position;
        /// <summary>Where the toy's feet are (world; above the ground while it hops) and the top of its head, for its
        /// shadow (<see cref="TennisFx.UpdateShadows"/>): a lunge or a hop moves them.</summary>
        public Vector3 FeetWorld => _model.position;
        public Vector3 HeadWorld => _model.TransformPoint(new Vector3(0f, 1.5f * Scale, 0f));
        /// <summary>A swing is being drawn (tests).</summary>
        public bool Swinging { get; private set; }
        /// <summary>The chibi (tests).</summary>
        public Chibi Character => _chibi;
        /// <summary>What the head turns toward (the ball), or null: straight ahead.</summary>
        public Vector3? LookAt { get; set; }

        /// <summary>A player dressed in <paramref name="look"/>; with <paramref name="blob"/> (a soft dot material) a blob
        /// shadow under its feet, for when the scene draws no real-time shadows (the TV box).</summary>
        public static TennisPlayerView Create(string name, Material actor, Material trail, ChibiLook look, Material blob = null)
        {
            var go = new GameObject(name);
            var v = go.AddComponent<TennisPlayerView>();
            v.Build(actor, trail, look);
            if (blob != null) v.BuildBlob(blob);
            return v;
        }

        /// <summary>The chibi drawn in low detail (<see cref="Chibi.Low"/>: about a third of the triangles), for a toy small
        /// on the screen.</summary>
        public bool Low
        {
            get => _chibi != null && _chibi.Low;
            set
            {
                if (_chibi != null) _chibi.Low = value;
                if (_racketFilter != null) _racketFilter.sharedMesh = value ? _racketFilled : _racketOpen;
            }
        }

        /// <summary>A flash over the player (a hit) and a grin for a moment.</summary>
        public void Flash(Color color, float amount = 1f)
        {
            _flashColor = color;
            _flash = amount;
            if (_chibi != null) _chibi.Flash(color, amount * 0.6f);
            _grinUntil = Time.time + 0.8f;
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

            int racketArm = rightHanded ? 1 : 0;
            if (!_poseSet)
            {
                _pose = Ready(side);
                _poseSet = true;
            }
            if (p != _player)
            {
                // a new match (the toys of the players stay): its clock and its swing count start again, so the last
                // match's stroke is over and the new count isn't a swing
                _player = p;
                _contact = _windUpEnd = _swingStart = -10;
                _lastSwings = p.Swings;
            }
            if (p.Swings != _lastSwings)
            {
                // every swing the rules show: the computer's, and the player's (their hits timed to the ball, the rest at
                // once)
                if (_lastSwings >= 0) StartSwing(now, p, side);
                _lastSwings = p.Swings;
            }

            // running legs (stride follows the speed)
            _stride += dt * Mathf.Lerp(0f, 16f, Mathf.Clamp01(speed / 6f));
            float legSwing = Mathf.Sin(_stride) * Mathf.Clamp01(speed / 2f) * 38f;
            float bob = Mathf.Abs(Mathf.Sin(_stride)) * Mathf.Clamp01(speed / 3f) * 0.06f;
            float crouch = 0.04f + 0.015f * Mathf.Sin(_t * 6f);   // ready: bouncing on its toes

            // where the racket rests when no swing is drawn: ready in front; back on the ball's side while one comes (or
            // behind the head for the toss); the serve stance; a lunge, a cheer, a sulk
            var free = new Vector3(-side * 0.3f, -0.75f, 0.45f);
            float tilt = 0f, jump = 0f;
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

            Swinging = now < _contact + _followSeconds;
            // after the follow-through, back to the rest pose at a capped turn (the rest pose may change any moment: a
            // cheer, a ball coming; blending toward it made the racket jump when it did)
            float follow = 1f - Mathf.Exp(-(now < _contact + _followSeconds + ReturnSeconds ? 9f : 14f) * dt);
            if (Swinging) _twist = SwingTwist(now, side);
            else _twist = Mathf.MoveTowards(_twist, Mathf.Lerp(_twist, twist, follow), 240f * dt);

            // the body's height and lean change smoothly too: a cheer's hop or a sulk switched on and off at once (the
            // end of a point, the next serve) made the whole player pop, racket and all; a hit far or high lunges or hops
            // toward the ball
            _height = Mathf.MoveTowards(_height, -crouch + bob + jump, 3f * dt);
            _lean = Mathf.MoveTowards(_lean, lean, 120f * dt);
            var lunge = Swinging && _reach ? Lunge(now, racketArm) : Vector3.zero;
            _model.localPosition = new Vector3(0f, _height, 0f) + lunge;
            _model.localRotation = Quaternion.Euler(_lean, _twist, _bank + tilt - lunge.x * 30f);

            // the racket arm: exact along the swing (its first frame continues from where the arm was; a hit's racket
            // reaches into the ball), else back to rest
            if (Swinging) _pose = SwingPose(now, side, racketArm, ref free);
            else _pose = Pose.Toward(_pose, target, follow, MaxTurn * dt);
            float freeTurn = Vector3.Angle(_free, free) * (Swinging ? 1f - Mathf.Exp(-20f * dt) : ease);
            _free = Vector3.RotateTowards(_free, free.normalized, Mathf.Min(freeTurn, MaxTurn * dt) * Mathf.Deg2Rad, 0f).normalized;
            if (_blob != null)
            {
                // the blob stays on the ground: smaller as a hop lifts the toy, stretched toward a lunge (and under a
                // reaching one)
                float lift = Mathf.Clamp01((_height + lunge.y) * 3f);
                _blob.localPosition = new Vector3(Mathf.Sin(tilt * Mathf.Deg2Rad) * -0.35f + lunge.x, 0.012f, 0.03f + lunge.z);
                _blob.localScale = new Vector3(0.62f * (1f + Mathf.Abs(tilt) / 90f), 0.5f, 1f) * (Scale * (1f - 0.35f * lift));
            }
            _chibi.SwingLeg(0, -legSwing);
            _chibi.SwingLeg(1, legSwing);
            _chibi.AimArm(racketArm, _pose.Arm);
            _chibi.AimArm(1 - racketArm, _free);
            // the racket in the playing mitten: the handle from the fist, the strings facing the pose's way; its big head
            // never goes into the court (it is lifted to skim it)
            var hand = _model.InverseTransformPoint(_chibi.HandCenter(racketArm));
            var handle = AboveGround(_pose.Handle, _model.TransformPoint(hand));
            var faceUp = _pose.Face - handle * Vector3.Dot(_pose.Face, handle);
            if (faceUp.sqrMagnitude < 1e-4f) faceUp = Vector3.Cross(handle, Vector3.right);
            _racket.localPosition = hand - handle * (0.05f * RacketScale);
            _racket.localRotation = Quaternion.LookRotation(handle, faceUp.normalized);
            _trail.emitting = Swinging && System.Math.Abs(now - _contact) < 0.16;
            Face(p, dt);
            ApplyFlash();
        }

        /// <summary>The face (happy after a point won, sad after one lost, a grin at a hit, else a smile) and the head
        /// following the ball.</summary>
        void Face(CourtPlayer p, float dt)
        {
            _chibi.SetFace(p.Mood > 0 ? Chibi.Face.Happy : p.Mood < 0 ? Chibi.Face.Sad
                : Time.time < _grinUntil ? Chibi.Face.Grin : Chibi.Face.Smile);
            float yaw = 0f, pitch = p.Mood < 0 ? 18f : 0f;
            if (LookAt.HasValue && p.Mood == 0)
            {
                var head = _model.TransformPoint(new Vector3(0f, 1.17f * Scale, 0f));
                var d = _model.InverseTransformDirection(LookAt.Value - head);
                yaw = Mathf.Clamp(Mathf.Atan2(d.x, Mathf.Max(0.2f, d.z)) * Mathf.Rad2Deg, -50f, 50f);
                pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -22f, 18f);
            }
            float k = 1f - Mathf.Exp(-8f * dt);
            _headYaw = Mathf.Lerp(_headYaw, yaw, k);
            _headPitch = Mathf.Lerp(_headPitch, pitch, k);
            _chibi.TurnHead(_headYaw, _headPitch);
        }

        /// <summary>
        /// A new swing: from the pose the arm is in now, a backswing (held while an early swing waits for the ball; none
        /// when the racket is already back, as it is when the ball has been coming, nor for a quick one), the hit, the
        /// follow-through, each as quick as the swing's power. A swing with too little time gets what it needs: its hit
        /// is drawn a little after the rules' (an on-time swing reaches the game ~0.1 s late through the camera anyway, so
        /// its hit is drawn quick), never a jump.
        /// </summary>
        void StartSwing(double now, CourtPlayer p, float side)
        {
            _swingFrom = _pose;
            _swingTwistFrom = _twist;
            _stroke = p.SwingStroke;
            _swingStart = now;
            _quick = p.SwingQuick;
            _reach = p.SwingReaches;
            _reachWorld = p.SwingPoint;
            float power = Mathf.Clamp01(p.SwingPower);
            _hitSeconds = Mathf.Lerp(SlowHitSeconds, FastHitSeconds, power);
            _followSeconds = Mathf.Lerp(SlowFollowSeconds, FastFollowSeconds, power);
            double windUp = _quick ? 0.0 : Pose.Degrees(_pose, Stroke3(_stroke, side).back) / WindUpTurn;
            if (p.SwingHits && p.SwingContact <= now + windUp) _hitSeconds = Mathf.Min(_hitSeconds, LateHitSeconds);   // the ball is there
            _contact = System.Math.Max(p.SwingContact, now + windUp + _hitSeconds);
            _windUpEnd = _contact - _hitSeconds;
            _trail.Clear();
        }

        // the swing's phases: the backswing (or holding it back while an early swing waits for the ball), the hit (evenly
        // paced: an accelerating one moved the head ~0.4 m in its last frame), the follow-through
        float WindUpK(double now) => Mathf.SmoothStep(0f, 1f, (float)((now - _swingStart) / System.Math.Max(1e-3, _windUpEnd - _swingStart)));

        float HitK(double now)
        {
            float k = Mathf.Clamp01((float)((now - _windUpEnd) / System.Math.Max(1e-3, _contact - _windUpEnd)));
            return k * (0.6f + 0.4f * k);
        }

        float FollowK(double now)
        {
            float x = Mathf.Clamp01((float)(now - _contact) / _followSeconds);
            return 1f - (1f - x) * (1f - x);
        }

        /// <summary>The body's turn along the swing (degrees about the vertical).</summary>
        float SwingTwist(double now, float side)
        {
            var (_, _, _, twistBack, twistFollow) = Stroke3(_stroke, side);
            if (now < _windUpEnd) return Mathf.Lerp(_swingTwistFrom, twistBack, WindUpK(now));
            if (now < _contact) return Mathf.Lerp(_quick ? _swingTwistFrom : twistBack, 0f, HitK(now));
            return Mathf.Lerp(0f, twistFollow, FollowK(now));
        }

        /// <summary>
        /// The racket arm's pose now along the swing: the backswing, the hit, the follow-through (then
        /// <see cref="Animate"/> eases back to the rest pose). A hit reaches into the ball (<see cref="ReachPose"/>): the
        /// racket's head is where the ball is met at the contact moment, and the backswing and the follow-through turn
        /// round the shoulder from there, so the head sweeps through the ball. A quick swing's hit starts wherever the
        /// racket was.
        /// </summary>
        Pose SwingPose(double now, float side, int racketArm, ref Vector3 free)
        {
            var (back, contact, follow, _, _) = Stroke3(_stroke, side);
            if (_reach)
            {
                contact = ReachPose(racketArm, side, out float ballSide);
                if (_stroke != Stroke.Overhead)
                {
                    back = Yaw(contact, ballSide * 65f, -0.15f);
                    follow = Yaw(contact, -ballSide * 100f, 0.45f);
                }
            }
            var hitFrom = _quick ? _swingFrom : back;
            free = _stroke == Stroke.Overhead && now < _contact ? new Vector3(-side * 0.2f, 0.9f, 0.4f) : new Vector3(-side * 0.6f, -0.3f, 0.6f);
            if (now < _windUpEnd) return Pose.Lerp(_swingFrom, back, WindUpK(now));
            if (now < _contact) return Pose.Lerp(hitFrom, contact, HitK(now));
            return Pose.Lerp(contact, follow, FollowK(now));
        }

        /// <summary>
        /// The arm and the racket reaching into the ball (in the model's frame, as it stands this frame): two "bones", the
        /// straight arm from the shoulder to the mitten and the racket from the mitten to its head's middle, solved so the
        /// head is on the ball, the mitten below the line to it (behind and out for an overhead), the strings facing back
        /// over the net. Out of reach (after the lunge) it stretches straight toward the ball.
        /// </summary>
        Pose ReachPose(int racketArm, float side, out float ballSide)
        {
            var s = _model.InverseTransformPoint(_chibi.Shoulder(racketArm));
            var c = _model.InverseTransformPoint(_reachWorld);
            ballSide = Mathf.Abs(c.x) > 0.05f ? Mathf.Sign(c.x) : side;
            bool overhead = _stroke == Stroke.Overhead;
            var d = c - s;
            float a = _chibi.ArmLength, r = RacketReach, dist = d.magnitude;
            var dir = dist > 1e-4f ? d / dist : Vector3.forward;
            Vector3 arm, handle;
            if (dist >= a + r - 1e-3f || dist < 1e-3f) arm = handle = dir;
            else
            {
                var pole = overhead ? new Vector3(side * 0.4f, 0f, -1f) : new Vector3(ballSide * 0.2f, -1f, -0.35f);
                var across = pole - dir * Vector3.Dot(pole, dir);
                if (across.sqrMagnitude < 1e-6f) across = Vector3.Cross(dir, Vector3.right);
                across.Normalize();
                if (dist <= Mathf.Abs(r - a)) arm = across;   // the ball very close: the arm out, the racket back to it
                else
                {
                    float cos = Mathf.Clamp((a * a + dist * dist - r * r) / (2f * a * dist), -1f, 1f);
                    arm = dir * cos + across * Mathf.Sqrt(1f - cos * cos);
                }
                handle = (c - (s + arm * a)).normalized;
            }
            var face = new Vector3(0f, overhead ? -0.3f : 0.1f, 1f).normalized;
            return new Pose { Arm = arm.normalized, Handle = handle, Face = face };
        }

        /// <summary>A pose turned round the vertical through the shoulder (degrees: + = from the front toward the toy's
        /// right) and raised (or lowered) a little: the backswing and the follow-through of a reaching hit.</summary>
        static Pose Yaw(Pose p, float degrees, float raise)
        {
            var q = Quaternion.AngleAxis(degrees, Vector3.up);
            return new Pose
            {
                Arm = (q * p.Arm + Vector3.up * (raise * 0.6f)).normalized,
                Handle = (q * p.Handle + Vector3.up * raise).normalized,
                Face = q * p.Face,
            };
        }

        /// <summary>
        /// How far the toy lunges toward a ball met beyond its comfortable reach (<see cref="Comfort"/> of the arm and the
        /// racket): sideways or forward up to <see cref="MaxLunge"/> m, a hop up to <see cref="MaxHop"/> m for a high one;
        /// into it as the racket comes to the ball, back out over the follow-through (in the toy's frame).
        /// </summary>
        Vector3 Lunge(double now, int racketArm)
        {
            var shoulder = _model.InverseTransformPoint(_chibi.Shoulder(racketArm)) + new Vector3(0f, _height, 0f);
            var d = transform.InverseTransformPoint(_reachWorld) - shoulder;
            float over = d.magnitude - (_chibi.ArmLength + RacketReach) * Comfort;
            if (over <= 0f) return Vector3.zero;
            var shift = d.normalized * over;
            var flat = Vector3.ClampMagnitude(new Vector3(shift.x, 0f, shift.z), MaxLunge);
            float up = Mathf.Clamp(shift.y, -0.15f, MaxHop);
            double from = _windUpEnd - 0.12;
            float w = now < _contact
                ? Mathf.SmoothStep(0f, 1f, (float)((now - from) / System.Math.Max(1e-3, _contact - from)))
                : 1f - Mathf.SmoothStep(0f, 1f, (float)((now - _contact) / _followSeconds));
            return (flat + Vector3.up * up) * w;
        }

        /// <summary>The handle's way (model frame) lifted just enough that the racket's head stays above the court.</summary>
        Vector3 AboveGround(Vector3 handle, Vector3 handWorld)
        {
            const float Lowest = 0.2f;   // the head's middle (its rim goes a little lower when it stands on edge)
            var world = _model.TransformDirection(handle);
            float need = (Lowest - handWorld.y) / RacketReach;
            if (world.y >= need) return handle;
            float wy = Mathf.Clamp(need, -1f, 1f);
            var flat = new Vector3(world.x, 0f, world.z);
            if (flat.sqrMagnitude < 1e-6f) flat = transform.forward;
            world = flat.normalized * Mathf.Sqrt(1f - wy * wy) + Vector3.up * wy;
            return _model.InverseTransformDirection(world);
        }

        Transform _blob;

        void BuildBlob(Material dot)
        {
            var go = new GameObject("Blob shadow");
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = PartMesh.Primitive(PrimitiveType.Quad);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = dot;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", new Color(0.06f, 0.1f, 0.04f, 0.42f));
            r.SetPropertyBlock(block);
            _blob = go.transform;
        }

        void ApplyFlash()
        {
            _block.SetColor("_FlashColor", new Color(_flashColor.r, _flashColor.g, _flashColor.b, _flash * 0.7f));
            foreach (var r in _renderers) r.SetPropertyBlock(_block);
        }

        // ------------------------------------------------------------------------------------------------ building

        /// <summary>The chibi is drawn at this size of its art (1.52 m tall): ~1.2 m, a small toy with a big racket like
        /// Wii's Miis, so the court and the ball stay in view (2026-11-06: 0.92 was still "too big").</summary>
        public const float Scale = 0.78f;

        void Build(Material actor, Material trailMaterial, ChibiLook look)
        {
            _block = new MaterialPropertyBlock();
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);
            _chibi = Chibi.Create("Chibi", look, _model);
            _chibi.transform.localScale = Vector3.one * Scale;

            Transform Pivot(string n, Transform parent, Vector3 pos)
            {
                var t = new GameObject(n).transform;
                t.SetParent(parent, false);
                t.localPosition = pos;
                return t;
            }

            _racket = Pivot("Racket", _model, Vector3.zero);
            _racket.localScale = Vector3.one * RacketScale;
            _racketTip = Pivot("Racket head", _racket, new Vector3(0f, 0f, HeadCenter));
            // open strings for a toy drawn in detail; a far one (low detail) has its strings on a pale bed: thin open strings
            // would shimmer at its size
            _racketOpen = RacketMesh(open: true);
            _racketFilled = RacketMesh(open: false);
            var shown = PartMesh.Show("Racket", _racket, _racketOpen, actor);
            shown.shadowCastingMode = ShadowCastingMode.On;
            _racketFilter = shown.GetComponent<MeshFilter>();
            _renderers = new Renderer[] { shown };

            var trailGo = new GameObject("Swoosh");
            trailGo.transform.SetParent(_racketTip, false);
            _trail = trailGo.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = trailMaterial;
            _trail.time = 0.16f;
            _trail.minVertexDistance = 0.03f;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 0f));
            _trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(look.Color("top"), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            _trail.shadowCastingMode = ShadowCastingMode.Off;
            _trail.emitting = false;
            _lastPosition = transform.position;
        }

        void OnDestroy()
        {
            if (_racketOpen != null) Destroy(_racketOpen);
            if (_racketFilled != null) Destroy(_racketFilled);
        }

        /// <summary>
        /// The racket as one mesh, built along +z from the butt (the strings in the xz plane, facing +y): a dark grip with
        /// a butt cap, an open throat of two arms, an oval frame (a tube, a little flattened, in the Sun yellow: it shows
        /// against any outfit and the court) and the strings, a few thick ones in cream (2026-11-07, user: "more like a
        /// tennis one with strings, stylised, not a ping-pong flat surface"). <paramref name="open"/>: the court shows
        /// between the strings; else they lie on a pale bed (a far toy's: open strings that small would shimmer).
        /// </summary>
        static Mesh RacketMesh(bool open)
        {
            var grip = new Color(0.2f, 0.2f, 0.3f);
            var frame = TennisStadium.Sun;
            var strings = new Color(0.97f, 0.96f, 0.88f);
            var mesh = new PartMesh { Srgb = true }
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.1f), new Vector3(0.042f, 0.1f, 0.042f), grip, 0.4f, new Vector3(90f, 0f, 0f))
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.01f), new Vector3(0.054f, 0.012f, 0.054f), grip, 0.4f, new Vector3(90f, 0f, 0f));
            // the throat: from the top of the handle out to the frame on each side, leaving the gap open
            float throatZ = HeadCenter - HeadB * 0.86f, throatX = HeadA * Mathf.Sqrt(1f - 0.86f * 0.86f);
            var top = new Vector3(0f, 0f, 0.2f);
            foreach (float side in new[] { -1f, 1f })
            {
                var end = new Vector3(side * throatX, 0f, throatZ);
                var along = end - top;
                float degrees = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
                mesh.Add(PrimitiveType.Cylinder, (top + end) * 0.5f, new Vector3(0.02f, along.magnitude * 0.5f + 0.006f, 0.017f), frame, 0.8f,
                         new Vector3(90f, degrees, 0f));
            }
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            // the frame: a tube round the oval
            const int Around = 36, Section = 8;
            var frameColor = new Color(frame.r, frame.g, frame.b, 0.8f);
            for (int i = 0; i <= Around; i++)
            {
                float a = i * Mathf.PI * 2f / Around;
                var p = new Vector3(HeadA * Mathf.Cos(a), 0f, HeadCenter + HeadB * Mathf.Sin(a));
                var outward = new Vector3(Mathf.Cos(a) / HeadA, 0f, Mathf.Sin(a) / HeadB).normalized;
                for (int j = 0; j <= Section; j++)
                {
                    float b = j * Mathf.PI * 2f / Section;
                    v.Add(p + outward * (FrameIn * Mathf.Cos(b)) + Vector3.up * (FrameThick * Mathf.Sin(b)));
                    n.Add((outward * (Mathf.Cos(b) / FrameIn) + Vector3.up * (Mathf.Sin(b) / FrameThick)).normalized);
                    c.Add(frameColor);
                }
            }
            for (int i = 0; i < Around; i++)
            for (int j = 0; j < Section; j++)
            {
                int k = i * (Section + 1) + j, k2 = k + Section + 1;
                t.AddRange(new[] { k, k + 1, k2, k + 1, k2 + 1, k2 });
            }
            mesh.Add(v, n, c, t);
            // the strings: a few thick mains along the handle and crosses, ending inside the frame
            float ia = HeadA - FrameIn * 0.4f, ib = HeadB - FrameIn * 0.4f;
            const float Width = 0.016f, Thick = 0.007f;
            foreach (float x in new[] { -0.081f, -0.027f, 0.027f, 0.081f })
            {
                float half = ib * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (ia * ia)));
                mesh.Add(PrimitiveType.Cube, new Vector3(x, 0f, HeadCenter), new Vector3(Width, Thick, 2f * half), strings, 0.3f);
            }
            foreach (float dz in new[] { -0.116f, -0.058f, 0f, 0.058f, 0.116f })
            {
                float half = ia * Mathf.Sqrt(Mathf.Max(0f, 1f - dz * dz / (ib * ib)));
                mesh.Add(PrimitiveType.Cube, new Vector3(0f, 0.0015f, HeadCenter + dz), new Vector3(2f * half, Thick, Width), strings, 0.3f);
            }
            // a far toy's: the strings on a pale bed inside the frame
            if (!open)
                mesh.Add(PrimitiveType.Cylinder, new Vector3(0f, 0f, HeadCenter), new Vector3(2f * ia, 0.002f, 2f * ib), new Color(0.72f, 0.76f, 0.74f), 0.2f);
            return mesh.Build(open ? "Tennis racket" : "Tennis racket (far)");
        }
    }
}
