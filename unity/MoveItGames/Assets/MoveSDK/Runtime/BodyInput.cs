using UnityEngine;
using UnityEngine.InputSystem;

namespace MoveIt.Motion
{
    /// <summary>What a game reads. Body motion and keyboard/TV-remote produce the same actions.</summary>
    public interface IBodyInput
    {
        bool HasPlayer { get; }
        bool IsCalibrated { get; }
        /// <summary>-1 left, 0 center, 1 right.</summary>
        int Lane { get; }
        /// <summary>True only in the frame the jump happened.</summary>
        bool JumpTriggered { get; }
        /// <summary>True while crouching (or while the down key is held).</summary>
        bool Crouching { get; }
        /// <summary>"Confirm/start": both hands raised, or Enter.</summary>
        bool ConfirmTriggered { get; }
    }

    /// <summary>
    /// Merges <see cref="BodyGestures"/> with keyboard input so every game is playable without a camera.
    /// Keys: ←/→ or A/D lanes, Space/↑/W jump, ↓/S crouch (hold), Enter confirm.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class BodyInput : MonoBehaviour, IBodyInput
    {
        public static BodyInput Instance { get; private set; }

        [SerializeField] BodyGestures gestures;
        [SerializeField] bool keyboardEnabled = true;

        public BodyGestures Gestures => gestures;
        public bool HasPlayer => gestures != null && gestures.HasPlayer;
        public bool IsCalibrated => gestures != null && gestures.IsCalibrated;
        public int Lane { get; private set; }
        public bool JumpTriggered { get; private set; }
        public bool Crouching { get; private set; }
        public bool ConfirmTriggered { get; private set; }
        /// <summary>True if the most recent gameplay input came from the keyboard.</summary>
        public bool UsingKeyboard { get; private set; }

        bool _pendingJump, _pendingConfirm, _pendingLane;

        void Awake()
        {
            // A duplicate from a newly loaded scene is destroyed together with its MotionSystem; keep the first.
            if (Instance == null) Instance = this;
            if (gestures == null) gestures = FindAnyObjectByType<BodyGestures>();
        }

        void OnEnable()
        {
            if (gestures != null) gestures.OnGesture += HandleGesture;
        }

        void OnDisable()
        {
            if (gestures != null) gestures.OnGesture -= HandleGesture;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Put the lane back to center, e.g. when a new round starts.</summary>
        public void ResetLane() => Lane = 0;

        void HandleGesture(Gesture g)
        {
            switch (g)
            {
                case Gesture.Jump: _pendingJump = true; break;
                case Gesture.HandsUp: _pendingConfirm = true; break;
                case Gesture.LaneLeft:
                case Gesture.LaneCenter:
                case Gesture.LaneRight: _pendingLane = true; break;
            }
        }

        void Update()
        {
            JumpTriggered = _pendingJump;
            ConfirmTriggered = _pendingConfirm;
            if (_pendingLane) Lane = gestures.Lane;
            if (_pendingJump || _pendingLane) UsingKeyboard = false;
            _pendingJump = _pendingConfirm = _pendingLane = false;

            bool keyCrouch = false;
            var kb = keyboardEnabled ? Keyboard.current : null;
            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) KeyLane(-1);
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) KeyLane(+1);
                if (kb.spaceKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)
                {
                    JumpTriggered = true;
                    UsingKeyboard = true;
                }
                keyCrouch = kb.downArrowKey.isPressed || kb.sKey.isPressed;
                if (keyCrouch) UsingKeyboard = true;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) ConfirmTriggered = true;
            }

            Crouching = keyCrouch || (gestures != null && gestures.IsCrouching);
        }

        void KeyLane(int step)
        {
            Lane = Mathf.Clamp(Lane + step, -1, 1);
            UsingKeyboard = true;
        }
    }
}
