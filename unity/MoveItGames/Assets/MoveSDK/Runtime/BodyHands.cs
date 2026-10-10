using UnityEngine;
using UnityEngine.InputSystem;

namespace MoveIt.Motion
{
    /// <summary>
    /// Maps the active player's hands into a body-relative "interaction zone" (0..1 in x and y, like the screen),
    /// so a child and an adult at any distance can both reach every corner of the screen.
    /// The zone follows the player: it is centered on the shoulders and scaled by torso length (the smooth track, so
    /// body jitter doesn't shake the cursors).
    ///
    /// <see cref="Hand.Position"/> moves smoothly every frame (<see cref="HandInterpolator"/> on the raw camera samples,
    /// half a camera frame behind the newest one). <see cref="Hand.Velocity"/> comes from the low-lag track, per sample
    /// (quick, for "was that a slash?"). A hand survives single frames the camera AI is unsure about (<see cref="HandPresence"/>).
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class BodyHands : MonoBehaviour
    {
        public struct Hand
        {
            public bool Tracked;
            /// <summary>Zone position: (0,0) bottom-left, (1,1) top-right. Can go slightly outside 0..1.</summary>
            public Vector2 Position;
            /// <summary>Zone units per second.</summary>
            public Vector2 Velocity;
            public float Speed => Velocity.magnitude;
            /// <summary>The latest usable camera sample's zone position on the low-lag track and its capture time (camera
            /// clock, s): for cursors that do their own smoothing, like Pikturo's brush.</summary>
            public Vector2 SamplePosition;
            public double SampleTime;
        }

        sealed class Track
        {
            public readonly HandPresence Presence = new HandPresence();
            public HandInterpolator Cursor;
            public Hand Sample;
        }

        public static BodyHands Instance { get; private set; }

        [SerializeField, Tooltip("Zone half-width around the shoulder center, in torso lengths.")]
        float zoneHalfWidth = 1.6f;
        [SerializeField, Tooltip("Zone bottom edge, torso lengths below the hip center.")]
        float zoneBelowHips = 0.3f;
        [SerializeField, Tooltip("Zone top edge, torso lengths above the shoulder center.")]
        float zoneAboveShoulders = 1.3f;
        [SerializeField, Tooltip("Push the point past the wrist toward the fingertips (fraction of forearm length).")]
        float handExtension = 0.3f;
        [SerializeField, Range(0f, 0.9f), Tooltip("Velocity smoothing (0 = raw, higher = smoother but laggier).")]
        float velocitySmoothing = 0.4f;
        [Header("Cursor (tools/motion/cursor_sim.py)")]
        [SerializeField, Range(0f, 1f), Tooltip("Camera frames behind the newest sample. 0 = guess ahead between samples (less " +
                                                "lag, uneven on fast moves), 1 = only real samples (smoothest, one frame more lag).")]
        float cursorDelay = 0.5f;
        [SerializeField, Tooltip("Hz. Lower = calmer cursor at rest.")]
        float cursorMinCutoff = 1f;
        [SerializeField, Tooltip("Per zone width per second. Higher = less lag on fast moves.")]
        float cursorBeta = 4f;
        [SerializeField, Tooltip("Hz, for the speed estimate inside the filter.")]
        float cursorDerivativeCutoff = 2f;
        [SerializeField, Tooltip("Use the mouse as the right hand while it is being moved (development without a camera).")]
        bool mouseFallback = true;

        public Hand Left { get; private set; }
        public Hand Right { get; private set; }
        public bool UsingMouse { get; private set; }

        double _lastSampleTime;
        float _unit;
        readonly Track _left = new Track(), _right = new Track();
        Vector2 _mousePrev;
        bool _mouseSeen;
        float _mouseActiveUntil;

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (mouseFallback && UpdateMouse()) return;
            UsingMouse = false;

            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            if (body == null)
            {
                Left = Right = default;
                Forget(_left);
                Forget(_right);
                _lastSampleTime = 0;
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            if (body.SampleTime != _lastSampleTime)
            {
                float dt = _lastSampleTime > 0 ? (float)(body.SampleTime - _lastSampleTime) : 0f;
                float torso = body.TorsoLength;
                if (!(dt > 0f && dt < Body.MinSpeedInterval) && torso >= 0.02f)
                {
                    _lastSampleTime = body.SampleTime;
                    _unit = _unit <= 0f ? torso : Mathf.Lerp(_unit, torso, 0.1f);
                    Sample(body, Joint.LeftElbow, Joint.LeftWrist, _left, now);
                    Sample(body, Joint.RightElbow, Joint.RightWrist, _right, now);
                }
            }

            float frameDt = Time.unscaledDeltaTime;
            Left = Output(_left, now, frameDt);
            Right = Output(_right, now, frameDt);
        }

        Hand Output(Track track, double now, float frameDt)
        {
            if (!track.Presence.Tracked(now) || track.Cursor == null || !track.Cursor.HasSample)
            {
                Forget(track);
                return default;
            }
            var hand = track.Sample;
            hand.Position = track.Cursor.Evaluate(now, frameDt, out _);
            return hand;
        }

        static void Forget(Track track)
        {
            track.Presence.Reset();
            track.Cursor?.Reset();
            track.Sample = default;
        }

        /// <summary>One camera sample of a hand: the cursor's raw position, and the low-lag one with its velocity.</summary>
        void Sample(Body body, Joint elbow, Joint wrist, Track track, double now)
        {
            // Unsure frames are skipped: the cursor glides on, and the hand is lost only if they go on (HandPresence).
            if (!track.Presence.Sample(body.Visibility[(int)wrist], now)) return;
            track.Cursor ??= new HandInterpolator(cursorMinCutoff, cursorBeta, cursorDerivativeCutoff)
            {
                Margin = cursorDelay, MaxExtrapolation = 0.08f, VelocitySmoothing = velocitySmoothing,
            };

            // The zone hangs on the smooth body: shoulder and hip jitter would otherwise shake both cursors.
            Vector2 shoulders = body.ShoulderCenter;
            float bottom = body.HipCenter.y - zoneBelowHips * _unit;
            float top = shoulders.y + zoneAboveShoulders * _unit;
            float width = 2f * zoneHalfWidth * _unit, height = Mathf.Max(0.01f, top - bottom);
            Vector2 Zone(Vector2 tip) => new Vector2(0.5f + (tip.x - shoulders.x) / width, (tip.y - bottom) / height);

            // The cursor smooths the raw sample itself (smoothing twice made fast moves uneven).
            track.Cursor.Sample(body.SampleTime, Zone(body.HandTip(body.Raw, elbow, wrist, handExtension)), false, now);

            // Speed from the last two low-lag samples: it follows starts and stops at once (Fruta's slashes).
            var previous = track.Sample;
            var hand = new Hand
            {
                Tracked = true,
                SamplePosition = Zone(body.HandTip(body.Fast, elbow, wrist, handExtension)),
                SampleTime = body.SampleTime,
            };
            float dt = (float)(hand.SampleTime - previous.SampleTime);
            if (previous.Tracked && dt > 0f && dt < 0.25f)
                hand.Velocity = Vector2.Lerp((hand.SamplePosition - previous.SamplePosition) / dt, previous.Velocity, velocitySmoothing);
            track.Sample = hand;
        }

        /// <summary>Mouse = right hand while it moves or a button is held. Returns true while active.</summary>
        bool UpdateMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return false;
            var pos = mouse.position.ReadValue();
            var zone = new Vector2(pos.x / Mathf.Max(1, Screen.width), pos.y / Mathf.Max(1, Screen.height));
            if (!_mouseSeen)
            {
                _mouseSeen = true;
                _mousePrev = zone;
            }
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            bool moved = (zone - _mousePrev).sqrMagnitude > 1e-8f;
            if (moved || mouse.leftButton.isPressed) _mouseActiveUntil = Time.unscaledTime + 1.5f;
            var velocity = Vector2.Lerp((zone - _mousePrev) / dt, Right.Velocity, velocitySmoothing);
            _mousePrev = zone;

            UsingMouse = Time.unscaledTime < _mouseActiveUntil;
            if (!UsingMouse) return false;
            Right = new Hand { Tracked = true, Position = zone, Velocity = velocity };
            Left = default;
            return true;
        }
    }
}
