using UnityEngine;

namespace MoveIt.Games.Volley
{
    /// <summary>
    /// A volleyball toy: one of Tundu's chibis (<see cref="Chibi"/>, dressed by <see cref="VolleyLooks"/>) animated from
    /// its <see cref="VolleyPlayer"/> every frame, posed in code like Tenis!'s: running with banked turns, the ready stance
    /// on bent knees, getting ready for the coming ball (<see cref="VolleyPlayer.Expect"/>: the arms together low and a
    /// crouch for a bump, the arms up beside the head for a set, the run-up and a big cartoon jump timed so its top is
    /// when the set comes down for a spike), then the touch itself as the rules make it (<see cref="VolleyPlayer.Touches"/>:
    /// the bump's lift, the set's push up, the spike's arm swinging through the ball, the jump serve), a dive for a ball
    /// out of reach or a dig, a cheer or a sulk between points. Its local +z faces the net, +x is its right; the root is at
    /// its feet. The player's toys hit with the hand the player hit with (the screen is a mirror).
    /// </summary>
    public sealed class VolleyPlayerView : MonoBehaviour
    {
        /// <summary>The chibi's size (its art is 1.52 m tall). The rules' heights (<see cref="VolleyMatch.SetHeight"/>, the
        /// net) are for this.</summary>
        public const float Scale = 1f;
        /// <summary>Half the jump's time in the air (s), and a jump's top above the ground for a ball met at a height: the
        /// mitten, up and forward at the top of the jump, reaches the ball.</summary>
        const float JumpHalf = 0.38f, Reach = 1.2f;
        /// <summary>A block: a quicker, lower jump at the net with both arms straight up (s, m).</summary>
        const float BlockHalf = 0.3f, BlockJump = 0.45f;
        /// <summary>A tip's jump is this much of a smash's (the tell: a little hop, the hand forward).</summary>
        const float TipJump = 0.5f;
        /// <summary>How fast the arms turn outside a touch (degrees a second).</summary>
        const float MaxTurn = 720f;

        Transform _model;
        Chibi _chibi;
        VolleyPlayer _player;
        int _lastTouches = -1;
        Touch _touch = Touch.None;
        double _touchTime = -10, _jumpApex = -10;
        float _jump;
        Vector3 _touchPoint;
        bool _touchRight = true;
        float _touchPower;
        readonly Vector3[] _arm = { V(-0.3f, -0.75f, 0.55f), V(0.3f, -0.75f, 0.55f) };
        float _height, _lean, _bank, _tilt, _thigh, _knee, _t, _stride, _headYaw, _headPitch, _grinUntil;
        Vector3 _lastPosition;

        /// <summary>Where its feet are (world; above the sand while it jumps) and the top of its head (for its shadow).</summary>
        public Vector3 FeetWorld => _model.position;
        public Vector3 HeadWorld => _model.TransformPoint(new Vector3(0f, 1.5f * Scale, 0f));
        public Chibi Character => _chibi;
        /// <summary>What the head turns toward (the ball), or null.</summary>
        public Vector3? LookAt { get; set; }
        /// <summary>A jump is under way (tests, the shadow).</summary>
        public float JumpHeight { get; private set; }

        /// <summary>The chibi drawn in low detail (a third of the triangles), for a toy small on the screen.</summary>
        public bool Low
        {
            get => _chibi != null && _chibi.Low;
            set { if (_chibi != null) _chibi.Low = value; }
        }

        public static VolleyPlayerView Create(string name, ChibiLook look)
        {
            var go = new GameObject(name);
            var v = go.AddComponent<VolleyPlayerView>();
            v._model = new GameObject("Model").transform;
            v._model.SetParent(go.transform, false);
            v._chibi = Chibi.Create("Chibi", look, v._model);
            v._chibi.transform.localScale = Vector3.one * Scale;
            v._lastPosition = go.transform.position;
            return v;
        }

        /// <summary>A flash over the toy and a grin for a moment (a touch).</summary>
        public void Flash(Color color, float amount = 1f)
        {
            if (_chibi != null) _chibi.Flash(color, amount * 0.6f);
            _grinUntil = Time.time + 0.8f;
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z).normalized;

        // the computer's hitter's look as it comes in (its right arm hits): a smash's arm drawn back high, a tip's hand
        // forward and up
        static readonly Vector3 SmashArm = V(0.3f, 0.6f, -0.75f), SmashOther = V(-0.3f, 0.55f, 0.78f);
        static readonly Vector3 TipArm = V(0.15f, 0.72f, 0.68f), TipOther = V(-0.3f, 0.2f, 0.93f);

        /// <summary>Called by the game every frame with the rules' toy and the match clock; <paramref name="rally"/>: the
        /// ball is in play (the ready stance), else the toy stands at ease.</summary>
        public void Animate(VolleyPlayer p, double now, float dt, bool rally)
        {
            _t += dt;
            var world = p.World;
            var velocity = dt > 0f ? (world - _lastPosition) / dt : Vector3.zero;
            if (velocity.sqrMagnitude > 900f) velocity = Vector3.zero;   // a jump to a new place between points
            _lastPosition = world;
            transform.SetPositionAndRotation(world, Quaternion.Euler(0f, p.Team == 0 ? 0f : 180f, 0f));
            var local = transform.InverseTransformDirection(velocity);
            float speed = new Vector2(local.x, local.z).magnitude;
            float ease = 1f - Mathf.Exp(-10f * dt);

            if (p != _player)
            {
                // a new match (the toys stay): its clock starts again
                _player = p;
                _lastTouches = p.Touches;
                _touchTime = _jumpApex = -10;
                _touch = Touch.None;
            }
            if (p.Touches != _lastTouches)
            {
                _lastTouches = p.Touches;
                _touch = p.TouchKind;
                _touchTime = p.TouchTime;
                _touchPoint = p.TouchPoint;
                _touchRight = p.TouchRight;
                _touchPower = p.TouchPower;
                _grinUntil = Time.time + 0.8f;
                if (_touch == Touch.Serve)
                {
                    // a little jump serve, its top at the hit
                    _jumpApex = now + 0.06;
                    _jump = Mathf.Clamp(_touchPoint.y - Reach, 0.15f, 0.6f) * Scale;
                }
                else if (_touch is Touch.Spike or Touch.Push && Mathf.Abs((float)(now - _jumpApex)) > JumpHalf)
                {
                    // a hit with no run-up jump under way (a push at the second touch, a late one): a hop with it
                    _jumpApex = now + 0.08;
                    _jump = Mathf.Clamp(_touchPoint.y - Reach, 0.1f, 0.5f) * Scale;
                }
            }
            // the run-up: a jump timed so its top is when the ball arrives, as high as it comes down (its height set at
            // take-off: a tip that shows itself in the air keeps the big jump it started as a smash)
            bool tipLook = p.Attack == Attack.Tip && now >= p.TellAt;
            if (p.Expect == Move.Hit && !p.Serving)
            {
                _jumpApex = p.ExpectTime;
                if (now < _jumpApex - JumpHalf) _jump = Mathf.Clamp(p.ExpectPoint.y - Reach, 0.3f, 1.3f) * Scale * (tipLook ? TipJump : 1f);
            }

            // ---------------------------------------------------------------- the pose this frame
            float side = _touchRight ? 1f : -1f;
            int hitting = _touchRight ? 1 : 0;
            Vector3 left, right;
            float thigh, knee, lean = Mathf.Clamp(local.z * 2f, -10f, 14f), tilt = 0f, extra = 0f;
            bool exact = false;
            if (rally)
            {
                left = V(-0.3f, -0.72f, 0.6f);
                right = V(0.3f, -0.72f, 0.6f);
                thigh = 22f;
                knee = 40f;
                extra = 0.012f * Mathf.Sin(_t * 6f);
            }
            else
            {
                left = V(-0.35f, -0.94f, 0.08f);
                right = V(0.35f, -0.94f, 0.08f);
                thigh = knee = 0f;
            }

            // getting ready for the coming ball
            float until = (float)(p.ExpectTime - now);
            if (p.Expect == Move.Bump)
            {
                float k = Mathf.SmoothStep(0f, 1f, 1f - until / 0.7f);
                left = Vector3.Slerp(left, V(0.12f, -0.6f, 0.79f), k);
                right = Vector3.Slerp(right, V(-0.12f, -0.6f, 0.79f), k);
                thigh = Mathf.Lerp(thigh, 42f, k);
                knee = Mathf.Lerp(knee, 80f, k);
            }
            else if (p.Expect == Move.Set)
            {
                float k = Mathf.SmoothStep(0f, 1f, 1f - until / 0.8f);
                left = Vector3.Slerp(left, V(-0.36f, 0.86f, 0.36f), k);
                right = Vector3.Slerp(right, V(0.36f, 0.86f, 0.36f), k);
                thigh = Mathf.Lerp(thigh, 18f, k);
                knee = Mathf.Lerp(knee, 34f, k);
            }
            else if (p.Expect == Move.Hit && p.Serving)
            {
                // the toss is up: the tossing hand up after it, the hitting arm back
                left = V(-0.12f, 0.9f, 0.42f);
                right = V(0.3f, 0.72f, -0.62f);
                thigh = 10f;
                knee = 20f;
            }
            else if (p.Expect == Move.Hit && p.Attack is Attack.Smash or Attack.Tip)
            {
                // the computer's hitter shows its attack as it comes in: a smash with the hitting arm drawn back high, a
                // tip with the hand forward and up (a disguised tip looks exactly like a smash until its moment; the arms
                // turn there at their capped speed)
                left = tipLook ? TipOther : SmashOther;
                right = tipLook ? TipArm : SmashArm;
            }
            else if (p.Serving)
            {
                // holding the ball in front at the belly
                left = V(0.15f, -0.5f, 0.85f);
                right = V(-0.15f, -0.5f, 0.85f);
                thigh = knee = 6f;
            }

            // the jump: the run-up's crouch, in the air, landing on bent knees (a block: a quick hop at the net as the hitter
            // hits, both arms straight up; its arms come up already in the crouch)
            bool block = p.BlockAt >= 0 && now < p.BlockAt + BlockHalf + 0.3;
            double apex = block ? p.BlockAt : _jumpApex;
            float half = block ? BlockHalf : JumpHalf, height = block ? BlockJump * Scale : _jump;
            var blockLeft = V(-0.16f, 0.96f, 0.24f);
            var blockRight = V(0.16f, 0.96f, 0.24f);
            float air = 0f;
            float since = (float)(now - (apex - half));
            if (block && since < -0.18f)
            {
                // waiting at the net: the hands up, ready
                left = blockLeft;
                right = blockRight;
                thigh = Mathf.Lerp(thigh, 25f, 0.5f);
                knee = Mathf.Lerp(knee, 45f, 0.5f);
            }
            if (since > -0.18f && since < 2f * half + 0.25f)
            {
                if (since < 0f)
                {
                    // the crouch before take-off
                    float c = Mathf.SmoothStep(0f, 1f, (since + 0.18f) / 0.18f);
                    thigh = Mathf.Lerp(thigh, 45f, c);
                    knee = Mathf.Lerp(knee, 85f, c);
                    left = block ? blockLeft : Vector3.Slerp(left, V(-0.25f, -0.55f, -0.8f), c);
                    right = block ? blockRight : Vector3.Slerp(right, V(0.25f, -0.55f, -0.8f), c);
                }
                else if (since <= 2f * half)
                {
                    float u = since / half - 1f;   // -1 take-off, 0 the top, 1 landing
                    air = height * (1f - u * u);
                    thigh = 12f;
                    knee = 30f + 25f * Mathf.Abs(u);
                    if (block)
                    {
                        left = blockLeft;
                        right = blockRight;
                    }
                    // before the hit (the last touch was before this jump began): the hitting arm drawn back up, the
                    // other one up at the ball (a tip: the hand forward)
                    else if (_touchTime < _jumpApex - JumpHalf - 0.05)
                    {
                        float r = p.Controller < 0 ? 1f : side;
                        var cocked = tipLook ? TipArm : V(0.25f * r, 0.75f, -0.6f);
                        var aim = tipLook ? TipOther : V(-0.2f * r, 0.85f, 0.5f);
                        left = r > 0f ? aim : cocked;
                        right = r > 0f ? cocked : aim;
                    }
                }
                else
                {
                    // landing: knees bend and come back
                    float l = 1f - (since - 2f * half) / 0.25f;
                    thigh = Mathf.Lerp(thigh, 40f, l);
                    knee = Mathf.Lerp(knee, 75f, l);
                }
            }

            // the touch, as the rules made it (its moment: now or a moment ago)
            float after = (float)(now - _touchTime);
            if (after >= -0.02f && after < 0.6f)
            {
                switch (_touch)
                {
                    case Touch.Bump:
                    {
                        float lift = Mathf.SmoothStep(0f, 1f, after / 0.25f);
                        left = V(0.1f, Mathf.Lerp(-0.55f, -0.18f, lift), 0.9f);
                        right = V(-0.1f, Mathf.Lerp(-0.55f, -0.18f, lift), 0.9f);
                        thigh = Mathf.Lerp(42f, 18f, lift);
                        knee = Mathf.Lerp(80f, 32f, lift);
                        exact = after < 0.3f;
                        break;
                    }
                    case Touch.Set:
                    {
                        float push = Mathf.SmoothStep(0f, 1f, after / 0.18f), down = Mathf.SmoothStep(0f, 1f, (after - 0.3f) / 0.3f);
                        left = Vector3.Slerp(V(-0.36f, 0.86f, 0.36f), V(-0.22f, 0.97f, 0.12f), push);
                        right = Vector3.Slerp(V(0.36f, 0.86f, 0.36f), V(0.22f, 0.97f, 0.12f), push);
                        left = Vector3.Slerp(left, V(-0.3f, -0.72f, 0.6f), down);
                        right = Vector3.Slerp(right, V(0.3f, -0.72f, 0.6f), down);
                        thigh = Mathf.Lerp(30f, 8f, push);
                        knee = Mathf.Lerp(55f, 15f, push);
                        exact = after < 0.3f;
                        break;
                    }
                    case Touch.Spike:
                    case Touch.Push:
                    {
                        // the hitting arm through the ball and down (a push: up and forward, softly)
                        var shoulder = _model.InverseTransformPoint(_chibi.Shoulder(hitting));
                        var toBall = _model.InverseTransformPoint(_touchPoint) - shoulder;
                        if (toBall.sqrMagnitude < 1e-4f) toBall = new Vector3(0.2f * side, 0.8f, 0.5f);
                        var follow = _touch == Touch.Spike ? V(0.12f * side, -0.55f, 0.83f) : V(0.15f * side, 0.55f, 0.82f);
                        float swing = Mathf.Clamp01(after / Mathf.Lerp(0.2f, 0.12f, _touchPower));
                        var arm = after < 0.05f ? toBall.normalized : Vector3.Slerp(toBall.normalized, follow, swing);
                        var other = V(-0.25f * side, Mathf.Lerp(0.6f, -0.6f, swing), 0.6f);
                        if (hitting == 1)
                        {
                            right = arm;
                            left = other;
                        }
                        else
                        {
                            left = arm;
                            right = other;
                        }
                        lean += 12f * swing * (_touch == Touch.Spike ? 1f : 0.4f);
                        exact = after < 0.35f;
                        break;
                    }
                    case Touch.Serve:
                    {
                        float swing = Mathf.Clamp01(after / 0.15f);
                        var arm = Vector3.Slerp(V(0.25f * side, 0.85f, 0.4f), V(0.1f * side, -0.35f, 0.93f), swing);
                        var other = V(-0.2f * side, Mathf.Lerp(0.8f, -0.5f, swing), 0.5f);
                        if (hitting == 1)
                        {
                            right = arm;
                            left = other;
                        }
                        else
                        {
                            left = arm;
                            right = other;
                        }
                        lean += 10f * swing;
                        exact = after < 0.3f;
                        break;
                    }
                    case Touch.Block:
                    {
                        // the ball off the hands: they push forward and down over the net
                        float k = Mathf.SmoothStep(0f, 1f, after / 0.15f);
                        left = Vector3.Slerp(V(-0.16f, 0.96f, 0.24f), V(-0.14f, 0.5f, 0.85f), k);
                        right = Vector3.Slerp(V(0.16f, 0.96f, 0.24f), V(0.14f, 0.5f, 0.85f), k);
                        lean += 10f * k;
                        exact = after < 0.3f;
                        break;
                    }
                    case Touch.Shank:
                    {
                        // off the arms the wrong way: arms flung out, a stumble back
                        float k = Mathf.SmoothStep(0f, 1f, after / 0.2f);
                        left = Vector3.Slerp(V(0.1f, 0.6f, 0.8f), V(-0.75f, -0.25f, 0.6f), k);
                        right = Vector3.Slerp(V(-0.1f, 0.6f, 0.8f), V(0.75f, -0.25f, 0.6f), k);
                        lean -= 14f * k;
                        exact = after < 0.25f;
                        break;
                    }
                }
            }

            // a dive for the ball: tipped over toward it, arms stretched, then back up
            if (p.DiveAt > 0 && now > p.DiveAt - 0.3 && now < p.DiveAt + 1.1)
            {
                float k = (float)(now - (p.DiveAt - 0.3));
                float d = k < 0.3f ? Mathf.SmoothStep(0f, 1f, k / 0.3f) : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.75f) / 0.6f);
                var to = LookAt.HasValue ? transform.InverseTransformDirection(LookAt.Value - transform.position) : Vector3.forward;
                var flat = new Vector2(to.x, to.z);
                flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector2.up;
                lean += 62f * d * Mathf.Max(0.25f, flat.y);
                tilt -= 55f * d * flat.x;
                extra -= 0.35f * d;
                var stretch = V(flat.x, -0.15f, Mathf.Max(0.3f, flat.y));
                left = Vector3.Slerp(left, stretch + Vector3.left * 0.15f, d);
                right = Vector3.Slerp(right, stretch + Vector3.right * 0.15f, d);
                thigh = Mathf.Lerp(thigh, 30f, d);
                knee = Mathf.Lerp(knee, 50f, d);
                air = 0f;
            }

            // between points: a cheer (hops, arms up) or a sulk
            if (p.Mood > 0)
            {
                extra += Mathf.Abs(Mathf.Sin(_t * 7f)) * 0.25f;
                left = V(-0.42f, 0.9f, 0.1f);
                right = V(0.42f, 0.9f, 0.1f);
                thigh = knee = 0f;
            }
            else if (p.Mood < 0)
            {
                left = V(-0.12f, -0.99f, 0.1f);
                right = V(0.12f, -0.99f, 0.1f);
                lean = 12f;
                thigh = 6f;
                knee = 10f;
            }
            JumpHeight = air;

            // ---------------------------------------------------------------- applied, smoothly
            float follow2 = 1f - Mathf.Exp(-14f * dt);
            for (int s = 0; s < 2; s++)
            {
                var target = s == 0 ? left : right;
                if (exact) _arm[s] = target;
                else
                {
                    float step = Mathf.Min(Vector3.Angle(_arm[s], target) * follow2, MaxTurn * dt);
                    _arm[s] = Vector3.RotateTowards(_arm[s], target, step * Mathf.Deg2Rad, 0f).normalized;
                }
                _chibi.AimArm(s, _arm[s]);
            }
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-local.x * 2.2f, -16f, 16f), ease);
            _lean = Mathf.MoveTowards(_lean, lean, 220f * dt);
            _tilt = Mathf.MoveTowards(_tilt, tilt, 260f * dt);
            bool running = speed > 0.8f && air <= 0.01f && p.Mood == 0;
            if (running)
            {
                _stride += dt * Mathf.Lerp(6f, 17f, Mathf.Clamp01(speed / 7f));
                float swingLeg = Mathf.Sin(_stride) * Mathf.Clamp01(speed / 2.5f) * 40f;
                _chibi.SwingLeg(0, -swingLeg);
                _chibi.SwingLeg(1, swingLeg);
                extra += Mathf.Abs(Mathf.Sin(_stride)) * Mathf.Clamp01(speed / 3f) * 0.06f;
                thigh = knee = 0f;
            }
            _thigh = Mathf.MoveTowards(_thigh, thigh, 400f * dt);
            _knee = Mathf.MoveTowards(_knee, knee, 700f * dt);
            if (!running)
            {
                _chibi.BendLeg(0, _thigh, _knee);
                _chibi.BendLeg(1, _thigh, _knee);
            }
            float drop = running ? 0f : _chibi.LegDrop(_thigh, _knee);
            _height = Mathf.MoveTowards(_height, extra, 3f * dt);
            _model.localPosition = new Vector3(0f, air + _height - drop, 0f);
            _model.localRotation = Quaternion.Euler(_lean, 0f, _bank + _tilt);
            Face(p, dt);
        }

        /// <summary>The face (happy after a point won, sad after one lost, a grin at a touch, else a smile) and the head
        /// following the ball, up to a high one.</summary>
        void Face(VolleyPlayer p, float dt)
        {
            _chibi.SetFace(p.Mood > 0 ? Chibi.Face.Happy : p.Mood < 0 ? Chibi.Face.Sad : Time.time < _grinUntil ? Chibi.Face.Grin : Chibi.Face.Smile);
            float yaw = 0f, pitch = p.Mood < 0 ? 20f : 0f;
            if (LookAt.HasValue && p.Mood == 0)
            {
                var head = _model.TransformPoint(new Vector3(0f, 1.17f * Scale, 0f));
                var d = _model.InverseTransformDirection(LookAt.Value - head);
                yaw = Mathf.Clamp(Mathf.Atan2(d.x, Mathf.Max(0.25f, d.z)) * Mathf.Rad2Deg, -55f, 55f);
                pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -38f, 20f);
            }
            float k = 1f - Mathf.Exp(-8f * dt);
            _headYaw = Mathf.Lerp(_headYaw, yaw, k);
            _headPitch = Mathf.Lerp(_headPitch, pitch, k);
            _chibi.TurnHead(_headYaw, _headPitch);
        }
    }
}
