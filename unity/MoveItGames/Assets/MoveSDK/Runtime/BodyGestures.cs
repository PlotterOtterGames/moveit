using System;
using UnityEngine;

namespace MoveIt.Motion
{
    public enum Gesture
    {
        PlayerFound,
        PlayerLost,
        Calibrated,
        LaneLeft,
        LaneCenter,
        LaneRight,
        Jump,
        CrouchStart,
        CrouchEnd,
        HandsUp,
    }

    /// <summary>
    /// Turns the active <see cref="Body"/> into game actions. Everything is measured relative to the player's
    /// own calibrated body (torso length as the unit), so the same thresholds work for a child and an adult
    /// at any distance from the camera.
    ///
    /// Calibration happens automatically when a player stands still for <see cref="stillTime"/> seconds.
    /// </summary>
    [DefaultExecutionOrder(-75)]
    public sealed class BodyGestures : MonoBehaviour
    {
        [Header("Calibration")]
        [SerializeField, Tooltip("Seconds the player must stand still to calibrate.")]
        float stillTime = 0.8f;

        [SerializeField, Tooltip("Max torso movement (torso lengths per second) that still counts as standing still.")]
        float stillSpeed = 0.6f;

        [Header("Lanes (torso lengths from the calibrated center)")]
        [SerializeField] float laneEnter = 0.55f;
        [SerializeField] float laneExit = 0.35f;
        [SerializeField, Tooltip("Anticipation: a clear sidestep switches lanes this many seconds early (its speed × this).")]
        float laneLookAhead = 0.1f;
        [SerializeField, Tooltip("Anticipation adds at most this much (torso lengths), so a lean alone can't switch lanes.")]
        float laneLookAheadMax = 0.2f;
        [SerializeField, Tooltip("Anticipation only starts once the player is this far out (torso lengths).")]
        float laneLookAheadFrom = 0.3f;

        [Header("Jump (torso lengths above standing height)")]
        [SerializeField] float jumpRise = 0.2f;
        [SerializeField, Tooltip("Minimum upward speed, torso lengths per second.")]
        float jumpMinSpeed = 0.9f;
        [SerializeField] float jumpReArm = 0.08f;
        [SerializeField] float jumpCooldown = 0.35f;
        [SerializeField, Tooltip("Take-off detection: upward speed (torso lengths/s) that starts a jump early.")]
        float jumpTakeoffSpeed = 1.6f;
        [SerializeField, Tooltip("Take-off detection: minimum rise above standing height (torso lengths).")]
        float jumpTakeoffRise = 0.06f;
        [SerializeField, Tooltip("A rise that starts low (knees bent, or a crouch) is a jump only this fast (torso lengths/s): " +
                                 "real jumps start with a dip, standing up from a crouch is slower at the top.")]
        float jumpFromDipSpeed = 2.2f;

        [Header("Crouch (torso lengths below standing height)")]
        [SerializeField] float crouchEnter = 0.28f;
        [SerializeField] float crouchExit = 0.15f;
        [SerializeField, Tooltip("Ignore crouches right after a jump (knees bend when landing).")]
        float crouchIgnoreAfterJump = 0.25f;
        [SerializeField, Tooltip("Onset detection: downward speed (torso lengths/s) that starts a crouch early. " +
                                 "Faster than the knee dip before a jump, so hops aren't read as crouches.")]
        float crouchOnsetSpeed = 2f;
        [SerializeField, Tooltip("Onset detection: minimum drop below standing height (torso lengths).")]
        float crouchOnsetDrop = 0.15f;

        [Header("Hands up")]
        [SerializeField, Tooltip("Wrists must be this far above the nose (torso lengths).")]
        float handsUpAboveNose = 0.15f;
        [SerializeField] float handsUpHold = 0.25f;

        [Header("Debug")]
        [SerializeField, Tooltip("Write every gesture to the Console.")]
        bool logGestures = true;

        [Header("Baseline drift")]
        [SerializeField, Tooltip("How fast the standing height follows slow changes (e.g. stepping closer), per second.")]
        float baselineFollowRate = 0.25f;

        public event Action<Gesture> OnGesture;

        public Body Body { get; private set; }
        public bool HasPlayer => Body != null;
        public bool IsCalibrated { get; private set; }
        public float CalibrationProgress => IsCalibrated ? 1f : Mathf.Clamp01(_stillTimer / stillTime);

        /// <summary>-1 = left, 0 = center, 1 = right (as seen on screen; the screen is a mirror).</summary>
        public int Lane { get; private set; }
        /// <summary>Sideways offset from the calibrated center, torso lengths (+ = right).</summary>
        public float LateralOffset { get; private set; }
        /// <summary>Sideways speed, torso lengths per second (+ = right).</summary>
        public float LateralSpeed { get; private set; }
        /// <summary>Height offset from standing, torso lengths (+ = up).</summary>
        public float VerticalOffset { get; private set; }
        public float VerticalSpeed { get; private set; }
        public bool IsJumping { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool AreHandsUp { get; private set; }

        // Calibrated baseline in view space, exposed for debug drawing.
        public float BaselineX { get; private set; }
        public float BaselineY { get; private set; }
        public float BaselineTorso { get; private set; }
        public float LaneEnter => laneEnter;
        public float JumpRise => jumpRise;
        public float CrouchEnter => crouchEnter;

        double _lastSampleTime;
        Vector3 _prevCenter;
        float _stillTimer;
        Vector3 _stillSumCenter;
        float _stillSumTorso;
        int _stillSamples;
        double _lastJumpTime = -10, _jumpEndTime = -10;
        bool _rising;
        float _riseStartOffset;
        float _handsTimer;
        bool _handsLatched;

        /// <summary>Forget the calibration; it re-runs as soon as the player stands still.</summary>
        public void Recalibrate()
        {
            IsCalibrated = false;
            ResetMotionState();
        }

        /// <summary>Make the player's current position the new center (call when a round starts).</summary>
        public void Recenter()
        {
            if (Body == null || !IsCalibrated) return;
            BaselineX = Body.FastTorsoCenter.x;
            if (!IsJumping && !IsCrouching) BaselineY = Body.FastTorsoCenter.y;
            SetLane(0, emit: false);
        }

        void Update()
        {
            var system = MotionSystem.Instance;
            var body = system != null ? system.ActiveBody : null;

            if (body != Body)
            {
                bool lost = Body != null && body == null;
                Body = body;
                IsCalibrated = false;
                ResetMotionState();
                if (lost) Emit(Gesture.PlayerLost);
                if (body != null) Emit(Gesture.PlayerFound);
            }
            if (body == null || body.SampleTime == _lastSampleTime) return;

            float dt = _lastSampleTime > 0 ? (float)(body.SampleTime - _lastSampleTime) : 0f;
            if (dt > 0f && dt < Body.MinSpeedInterval) return;
            _lastSampleTime = body.SampleTime;
            // Gestures read the low-lag track: they react sooner than the smooth track used for drawing.
            var center = body.FastTorsoCenter;
            float torso = body.FastTorsoLength;
            if (dt <= 0f || dt > 0.5f || torso < 0.03f)
            {
                _prevCenter = center;
                return;
            }

            if (!IsCalibrated) UpdateCalibration(body, center, torso, dt);
            else UpdateGestures(body, center, torso, dt);
            _prevCenter = center;
        }

        void UpdateCalibration(Body body, Vector3 center, float torso, float dt)
        {
            float speed = Vector2.Distance(center, _prevCenter) / dt / torso;
            if (!body.ShouldersVisible || speed > stillSpeed)
            {
                _stillTimer = 0f;
                _stillSumCenter = Vector3.zero;
                _stillSumTorso = 0f;
                _stillSamples = 0;
                return;
            }

            _stillTimer += dt;
            _stillSumCenter += center;
            _stillSumTorso += torso;
            _stillSamples++;
            if (_stillTimer < stillTime) return;

            var avg = _stillSumCenter / _stillSamples;
            BaselineX = avg.x;
            BaselineY = avg.y;
            BaselineTorso = _stillSumTorso / _stillSamples;
            IsCalibrated = true;
            Emit(Gesture.Calibrated);
        }

        void UpdateGestures(Body body, Vector3 center, float torso, float dt)
        {
            double now = body.SampleTime;
            float unit = BaselineTorso;

            LateralOffset = (center.x - BaselineX) / unit;
            VerticalOffset = (center.y - BaselineY) / unit;
            LateralSpeed = Mathf.Lerp(LateralSpeed, (center.x - _prevCenter.x) / dt / unit, 0.7f);
            VerticalSpeed = Mathf.Lerp(VerticalSpeed, (center.y - _prevCenter.y) / dt / unit, 0.7f);

            // Lanes, with hysteresis so standing on a border doesn't flicker. A clear sidestep that is already on its
            // way is judged where it will be in laneLookAhead seconds: it switches ~60-100 ms sooner. The look-ahead
            // is capped and only starts once the player is well off-center, so leaning or swaying never switches.
            float side = LateralOffset;
            if (Mathf.Abs(side) > laneLookAheadFrom)
                side += Mathf.Clamp(LateralSpeed * laneLookAhead, -laneLookAheadMax, laneLookAheadMax);
            int lane = Lane;
            if (Lane == 0)
            {
                if (side > laneEnter) lane = 1;
                else if (side < -laneEnter) lane = -1;
            }
            else if (Lane == 1)
            {
                if (side < -laneEnter) lane = -1;
                else if (side < laneExit) lane = 0;
            }
            else
            {
                if (side > laneEnter) lane = 1;
                else if (side > -laneExit) lane = 0;
            }
            SetLane(lane, emit: true);

            // Remember where an upward movement started: real jumps start with a knee dip, and standing up from a
            // crouch also starts low. Both are told apart by speed near standing height: a jump is still accelerating
            // there, standing up is already slowing down.
            bool risingNow = VerticalSpeed > 0.3f;
            if (risingNow && !_rising) _riseStartOffset = VerticalOffset;
            _rising = risingNow;
            bool fromDip = _riseStartOffset < -0.1f;

            // Jump, detected at take-off. The "high enough and still rising" rule stays as a fallback for slow jumps.
            // A dip deep enough to count as a crouch doesn't block the jump: the crouch simply ends here.
            bool canJump = !IsJumping && now - _lastJumpTime > jumpCooldown;
            bool takeoff = VerticalOffset > jumpTakeoffRise && VerticalSpeed > (fromDip ? jumpFromDipSpeed : jumpTakeoffSpeed);
            bool high = !fromDip && VerticalOffset > jumpRise && VerticalSpeed > jumpMinSpeed;
            if (canJump && (takeoff || high))
            {
                if (IsCrouching)
                {
                    IsCrouching = false;
                    Emit(Gesture.CrouchEnd);
                }
                IsJumping = true;
                _lastJumpTime = now;
                Emit(Gesture.Jump);
            }
            else if (IsJumping && VerticalOffset < jumpReArm && now - _lastJumpTime > 0.15)
            {
                IsJumping = false;
                _jumpEndTime = now;
            }

            // Crouch, detected at onset (fast drop) or once low enough.
            bool canCrouch = !IsCrouching && !IsJumping && now - _jumpEndTime > crouchIgnoreAfterJump;
            bool dropping = VerticalSpeed < -crouchOnsetSpeed && VerticalOffset < -crouchOnsetDrop;
            if (canCrouch && (VerticalOffset < -crouchEnter || dropping))
            {
                IsCrouching = true;
                Emit(Gesture.CrouchStart);
            }
            else if (IsCrouching && VerticalOffset > -crouchExit)
            {
                IsCrouching = false;
                Emit(Gesture.CrouchEnd);
            }

            // Both hands above the head.
            var fast = body.Fast;
            float handsLine = fast[(int)Joint.Nose].y + handsUpAboveNose * unit;
            bool wristsVisible = body.IsVisible(Joint.LeftWrist) && body.IsVisible(Joint.RightWrist);
            AreHandsUp = wristsVisible && fast[(int)Joint.LeftWrist].y > handsLine && fast[(int)Joint.RightWrist].y > handsLine;
            if (AreHandsUp)
            {
                _handsTimer += dt;
                if (!_handsLatched && _handsTimer >= handsUpHold)
                {
                    _handsLatched = true;
                    Emit(Gesture.HandsUp);
                }
            }
            else
            {
                _handsTimer = 0f;
                float shoulderY = body.FastShoulderCenter.y;
                if (fast[(int)Joint.LeftWrist].y < shoulderY || fast[(int)Joint.RightWrist].y < shoulderY) _handsLatched = false;
            }

            // Follow slow drift of standing height and body size while the player is neutral.
            if (!IsJumping && !IsCrouching && Mathf.Abs(VerticalSpeed) < 0.3f && Mathf.Abs(VerticalOffset) < 0.15f)
            {
                float k = baselineFollowRate * dt;
                BaselineY = Mathf.Lerp(BaselineY, center.y, k);
                BaselineTorso = Mathf.Lerp(BaselineTorso, torso, k);
            }
        }

        void SetLane(int lane, bool emit)
        {
            if (lane == Lane) return;
            Lane = lane;
            if (emit) Emit(lane < 0 ? Gesture.LaneLeft : lane > 0 ? Gesture.LaneRight : Gesture.LaneCenter);
        }

        void ResetMotionState()
        {
            _lastSampleTime = 0;
            _stillTimer = 0f;
            _stillSumCenter = Vector3.zero;
            _stillSumTorso = 0f;
            _stillSamples = 0;
            _handsTimer = 0f;
            _handsLatched = false;
            Lane = 0;
            LateralOffset = LateralSpeed = VerticalOffset = VerticalSpeed = 0f;
            IsJumping = IsCrouching = AreHandsUp = false;
            _rising = false;
            _riseStartOffset = 0f;
            _lastJumpTime = _jumpEndTime = -10;
        }

        void Emit(Gesture g)
        {
            if (logGestures) Debug.Log($"[Gesture] {g}  lane={Lane} lateral={LateralOffset:0.00} vertical={VerticalOffset:0.00}");
            OnGesture?.Invoke(g);
        }
    }
}
