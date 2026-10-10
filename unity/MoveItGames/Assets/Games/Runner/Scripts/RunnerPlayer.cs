using UnityEngine;

namespace MoveIt.Games.Runner
{
    /// <summary>
    /// The runner character: lane movement, jump arc and slide. Stays at z = 0; the world moves.
    /// With an <see cref="Animator"/> (Dino, from art/runner) it plays Idle / Run / Jump / Slide / Hit / Cheer and
    /// leans into lane changes; without one, the visual (a capsule) is squashed for slides.
    /// </summary>
    public sealed class RunnerPlayer : MonoBehaviour
    {
        public enum Mood { Idle, Running, Crashed, Cheering }

        [SerializeField] Transform visual;
        [SerializeField, Tooltip("Optional: the character's animator (states Idle, Run, Jump, Slide, Hit, Cheer).")]
        Animator animator;
        [SerializeField, Tooltip("Optional: dust puffs on landing and lane changes.")]
        RunnerFx fx;
        [SerializeField, Tooltip("Minimum lane change speed, m/s (the end of the move).")]
        float laneChangeSpeed = 16f;
        [SerializeField, Tooltip("Ease-out rate, 1/s: the move starts fast so the character reacts in the first frame.")]
        float laneSnap = 18f;
        [SerializeField] float jumpHeight = 1.6f;
        [SerializeField] float jumpDuration = 0.8f;
        [SerializeField, Tooltip("A short crouch still slides at least this long.")]
        float minSlideTime = 0.6f;

        [Header("Animation")]
        [SerializeField, Tooltip("Run speed (m/s) at which the Run clip plays at normal speed.")]
        float runClipSpeed = 9f;
        [SerializeField, Tooltip("Length of the Jump clip, seconds (it is stretched to the jump).")]
        float jumpClipLength = 0.8f;
        [SerializeField, Tooltip("Lean into lane changes, degrees per metre still to go.")]
        float laneLean = 14f;

        public const float StandHeight = 1.8f;
        public const float SlideHeight = 0.8f;
        public const float HalfWidth = 0.35f;

        public float LaneWidth { get; set; } = 2.2f;
        /// <summary>Current run speed (m/s), so the run animation keeps up with the world.</summary>
        public float RunSpeed { get; set; }
        public float X => transform.position.x;
        public float FeetY { get; private set; }
        public float Height => IsSliding ? SlideHeight : StandHeight;
        public bool InAir => _jumpTime >= 0f;
        public bool IsSliding { get; private set; }

        float _jumpTime = -1f;
        float _slideUntil;
        float _targetX;
        bool _fastFall;
        Mood _mood;
        bool _faceCamera;
        float _yaw;
        string _state;
        float _stretch = 1f, _stretchVelocity;   // squash & stretch spring (1 = normal height)

        /// <summary>What the character does outside the run: idle (optionally turned toward the camera), crash, cheer.</summary>
        public void Show(Mood mood, bool faceCamera = false)
        {
            _mood = mood;
            _faceCamera = faceCamera;
            if (animator == null) return;
            switch (mood)
            {
                case Mood.Idle: Play("Idle", 1f, 0.2f); break;
                case Mood.Crashed: Play("Hit", 1f, 0.05f); break;
                case Mood.Cheering: Play("Cheer", 1f, 0.2f); break;
            }
        }

        public void ResetPlayer()
        {
            _jumpTime = -1f;
            _slideUntil = 0f;
            _targetX = 0f;
            FeetY = 0f;
            IsSliding = false;
            transform.position = Vector3.zero;
            UpdateVisual();
        }

        public void Tick(float dt, int lane, bool jump, bool crouch)
        {
            _mood = Mood.Running;
            _faceCamera = false;
            var pos = transform.position;
            float target = lane * LaneWidth;
            if (!Mathf.Approximately(target, _targetX))
            {
                GameAudio.Sfx("whoosh", 0.55f);
                if (fx != null && !InAir) fx.LanePuff(transform.position, Mathf.Sign(target - _targetX));
            }
            _targetX = target;
            pos.x = Mathf.MoveTowards(pos.x, _targetX, Mathf.Max(laneChangeSpeed, Mathf.Abs(_targetX - pos.x) * laneSnap) * dt);
            transform.position = pos;

            if (jump && !InAir)
            {
                _jumpTime = 0f;
                _slideUntil = 0f;
                GameAudio.Sfx("jump");
                _stretchVelocity += 4f;          // stretch up on take-off
            }

            _fastFall = false;
            if (InAir)
            {
                // Crouching in the air drops you faster, like a swipe-down in Subway Surfers.
                _fastFall = crouch;
                _jumpTime += crouch ? dt * 2.5f : dt;
                float p = _jumpTime / jumpDuration;
                if (p >= 1f)
                {
                    _jumpTime = -1f;
                    FeetY = 0f;
                    GameAudio.Sfx("land", 0.8f);
                    _stretchVelocity -= 5f;      // squash on landing
                    if (fx != null) fx.Land(transform.position);
                }
                else FeetY = 4f * jumpHeight * p * (1f - p);
            }

            if (crouch && !InAir) _slideUntil = Time.time + minSlideTime;
            bool wasSliding = IsSliding;
            IsSliding = !InAir && Time.time < _slideUntil;
            if (IsSliding && !wasSliding) GameAudio.Sfx("slide", 0.8f);
            UpdateVisual();
            UpdateAnimation();
        }

        void Update()
        {
            if (visual == null || animator == null) return;
            // Squash & stretch: a springy height that keeps the volume (wider when squashed).
            float dt = Time.deltaTime;
            _stretchVelocity += ((1f - _stretch) * 300f - _stretchVelocity * 16f) * dt;
            _stretch = Mathf.Clamp(_stretch + _stretchVelocity * dt, 0.7f, 1.3f);
            float side = 1f / Mathf.Sqrt(_stretch);
            visual.localScale = new Vector3(side, _stretch, side);
            // Turn toward the camera when waiting or cheering, lean into lane changes while running.
            float target = _faceCamera ? 180f : Mathf.Clamp((_targetX - X) * laneLean, -25f, 25f);
            _yaw = Mathf.LerpAngle(_yaw, target, 1f - Mathf.Exp(-(_faceCamera ? 6f : 14f) * Time.deltaTime));
            visual.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        void UpdateAnimation()
        {
            if (animator == null) return;
            if (InAir) Play("Jump", jumpClipLength / jumpDuration * (_fastFall ? 2.5f : 1f), 0.05f);
            else if (IsSliding) Play("Slide", 1f, 0.08f);
            else Play("Run", Mathf.Max(0.6f, RunSpeed / runClipSpeed), 0.1f);
        }

        void Play(string state, float speed, float fade)
        {
            animator.speed = speed;
            if (_state == state) return;
            _state = state;
            animator.CrossFadeInFixedTime(state, fade);
        }

        void UpdateVisual()
        {
            if (visual == null) return;
            if (animator != null)
            {
                visual.localPosition = new Vector3(0f, FeetY, 0f);   // the clips show the slide and the tuck
                return;
            }
            float h = Height;
            visual.localScale = new Vector3(0.8f, h / 2f, 0.8f);  // Unity capsule is 2 m tall at scale 1
            visual.localPosition = new Vector3(0f, FeetY + h / 2f, 0f);
        }
    }
}
