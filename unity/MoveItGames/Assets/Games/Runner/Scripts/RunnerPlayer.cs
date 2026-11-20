using System;
using UnityEngine;

namespace MoveIt.Games.Runner
{
    /// <summary>
    /// The runner: lane movement, the jump's arc and the slide (the game's rules, unchanged), and Dino's performance
    /// (art/runner: the console polish of 2026-11-18): the clips for every moment (idling and waving on the title, ready
    /// at the line, running at the world's speed, jumps with a front flip now and then, the belly slide, the bonk and the
    /// dizzy sit, cheering and dancing for a new record), blinking, leaning into lane changes and forward with speed,
    /// squash and stretch, and the scarf's two tails streaming in the wind (a little spring chain each, on the bones).
    /// Stays at z = 0; the world moves.
    /// </summary>
    public sealed class RunnerPlayer : MonoBehaviour
    {
        public enum Mood { Title, Ready, Running, Crashed, Cheering, Dancing }

        [SerializeField] Transform visual;
        [SerializeField, Tooltip("The character's animator (Dino.controller: a state per clip).")]
        Animator animator;
        [SerializeField, Tooltip("Minimum lane change speed, m/s (the end of the move).")]
        float laneChangeSpeed = 16f;
        [SerializeField, Tooltip("Ease-out rate, 1/s: the move starts fast so the character reacts in the first frame.")]
        float laneSnap = 18f;
        [SerializeField] float jumpHeight = RunnerTrack.JumpHeight;
        [SerializeField] float jumpDuration = RunnerTrack.JumpTime;
        [SerializeField, Tooltip("A short crouch still slides at least this long.")]
        float minSlideTime = 0.6f;

        [Header("Animation")]
        [SerializeField, Tooltip("Run speed (m/s) at which the Run clip plays at normal speed.")]
        float runClipSpeed = 9.5f;
        [SerializeField, Tooltip("Length of the Jump clip, seconds (it is stretched to the jump).")]
        float jumpClipLength = 0.8f;
        [SerializeField, Tooltip("Lean into lane changes, degrees per metre still to go.")]
        float laneLean = 14f;
        [SerializeField, Tooltip("A jump is a front flip this often (a trick: it looks great, it changes nothing).")]
        float flipChance = 0.22f;

        public const float StandHeight = 1.8f;
        public const float SlideHeight = 0.8f;
        public const float HalfWidth = 0.35f;

        public float LaneWidth { get; set; } = RunnerWorld.LaneWidth;
        /// <summary>Current run speed (m/s), so the run animation keeps up with the world.</summary>
        public float RunSpeed { get; set; }
        public float X => transform.position.x;
        public float FeetY { get; private set; }
        public float Height => IsSliding ? SlideHeight : StandHeight;
        public bool InAir => _jumpTime >= 0f;
        public bool IsSliding { get; private set; }
        public Mood Current => _mood;

        /// <summary>Fx hooks: a foot hits the ground running (its x), the runner lands, takes off, changes lane (the
        /// direction), starts a slide.</summary>
        public event Action<Vector3> Footstep, Landed, TookOff, Slid;
        public event Action<Vector3, float> LaneChanged;

        float _jumpTime = -1f;
        float _slideUntil;
        float _targetX;
        bool _fastFall;
        Mood _mood;
        float _yaw, _roll, _pitch;
        string _state;
        float _stateTime;
        float _stretch = 1f, _stretchVelocity;   // squash & stretch spring (1 = normal height)
        float _lastRunPhase;
        float _titleClock;

        // the face (bones the clips scale to show or hide), blinking
        Transform[] _eyes;
        float _nextBlink = 2f, _blinkUntil;

        // the scarf's tails: a spring chain each
        sealed class Chain
        {
            public Transform[] Bones;
            public Vector3[] Rest;          // each bone's rest direction, local
            public Vector3[] Points, Prev;  // the simulated points (world)
            public float[] Lengths;
            public float Phase;
        }

        Chain[] _scarf;

        void Awake()
        {
            if (visual == null) return;
            _eyes = new[] { Find("Eye.L"), Find("Eye.R") };
            _scarf = new[] { MakeChain("ScarfL", 0.3f), MakeChain("ScarfR", 1.9f) };
        }

        Transform Find(string name)
        {
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        Chain MakeChain(string prefix, float phase)
        {
            var bones = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                bones[i] = Find(prefix + (i + 1));
                if (bones[i] == null) return null;
            }
            var c = new Chain { Bones = bones, Rest = new Vector3[3], Points = new Vector3[4], Prev = new Vector3[4], Lengths = new float[3], Phase = phase };
            for (int i = 0; i < 3; i++)
            {
                var next = i < 2 ? bones[i + 1].position : bones[i].position + (bones[i].position - bones[i - 1].position);
                var d = next - bones[i].position;
                c.Lengths[i] = Mathf.Max(0.05f, d.magnitude);
                c.Rest[i] = bones[i].InverseTransformDirection(d.normalized);
            }
            for (int i = 0; i < 4; i++)
                c.Points[i] = c.Prev[i] = i < 3 ? bones[i].position : bones[2].position + bones[2].TransformDirection(c.Rest[2]) * c.Lengths[2];
            return c;
        }

        /// <summary>What the character does outside the run: the title's idling, ready at the line, the crash, cheering,
        /// the victory dance.</summary>
        public void Show(Mood mood)
        {
            _mood = mood;
            if (animator == null) return;
            switch (mood)
            {
                case Mood.Title:
                    _titleClock = 0f;
                    Play("Idle", 1f, 0.3f);
                    break;
                case Mood.Ready: Play("Ready", 1f, 0.25f); break;
                case Mood.Crashed: Play("Hit", 1f, 0.04f); break;
                case Mood.Cheering: Play("Cheer", 1f, 0.2f); break;
                case Mood.Dancing: Play("Dance", 1f, 0.25f); break;
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
            if (visual != null) visual.localPosition = Vector3.zero;
        }

        public void Tick(float dt, int lane, bool jump, bool crouch)
        {
            _mood = Mood.Running;
            var pos = transform.position;
            float target = lane * LaneWidth;
            if (!Mathf.Approximately(target, _targetX))
            {
                GameAudio.Sfx("whoosh", 0.55f);
                LaneChanged?.Invoke(transform.position, Mathf.Sign(target - _targetX));
            }
            _targetX = target;
            pos.x = Mathf.MoveTowards(pos.x, _targetX, Mathf.Max(laneChangeSpeed, Mathf.Abs(_targetX - pos.x) * laneSnap) * dt);
            transform.position = pos;

            if (jump && !InAir)
            {
                _jumpTime = 0f;
                _slideUntil = 0f;
                GameAudio.Sfx("jump");
                _stretchVelocity += 4.5f;          // stretch up on take-off
                TookOff?.Invoke(transform.position);
                Play(UnityEngine.Random.value < flipChance ? "Flip" : "Jump", jumpClipLength / jumpDuration, 0.04f, restart: true);
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
                    _stretchVelocity -= 5.5f;      // squash on landing
                    Landed?.Invoke(transform.position);
                }
                else FeetY = 4f * jumpHeight * p * (1f - p);
            }

            if (crouch && !InAir) _slideUntil = Time.time + minSlideTime;
            bool wasSliding = IsSliding;
            IsSliding = !InAir && Time.time < _slideUntil;
            if (IsSliding && !wasSliding)
            {
                GameAudio.Sfx("slide", 0.8f);
                Slid?.Invoke(transform.position);
            }
            if (visual != null) visual.localPosition = new Vector3(0f, FeetY, 0f);
            UpdateAnimation(dt);
        }

        void UpdateAnimation(float dt)
        {
            if (animator == null) return;
            if (InAir)
            {
                if (_state != "Jump" && _state != "Flip") Play("Jump", jumpClipLength / jumpDuration, 0.04f, restart: true);
                animator.speed = jumpClipLength / jumpDuration * (_fastFall ? 2.5f : 1f);
            }
            else if (IsSliding) Play("Slide", 1f, 0.07f);
            else
            {
                Play("Run", Mathf.Max(0.7f, RunSpeed / runClipSpeed), 0.08f);
                // footsteps: the left foot lands at the cycle's start, the right halfway
                var info = animator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName("Run"))
                {
                    float phase = Mathf.Repeat(info.normalizedTime, 1f);
                    if (Crossed(_lastRunPhase, phase, 0.02f) || Crossed(_lastRunPhase, phase, 0.52f))
                        Footstep?.Invoke(transform.position + new Vector3(phase < 0.5f ? 0.25f : -0.25f, 0f, 0.05f));
                    _lastRunPhase = phase;
                }
            }
        }

        static bool Crossed(float before, float now, float mark) =>
            before <= now ? before < mark && now >= mark : before < mark || now >= mark;

        void Play(string state, float speed, float fade, bool restart = false)
        {
            animator.speed = speed;
            if (_state == state && !restart) return;
            _state = state;
            _stateTime = 0f;
            animator.CrossFadeInFixedTime(state, fade, 0, 0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _stateTime += dt;
            if (animator != null)
            {
                switch (_mood)
                {
                    case Mood.Title:
                        // idling at camp: looking around, now and then waving at whoever's in front of the TV
                        _titleClock += dt;
                        animator.speed = 1f;
                        if (_state == "Idle" && _stateTime > 4.5f) Play(_titleClock > 8f && UnityEngine.Random.value < 0.6f ? "Wave" : "LookAround", 1f, 0.35f);
                        else if (_state == "Wave" && _stateTime > 3.2f) Play("Idle", 1f, 0.35f);
                        else if (_state == "LookAround" && _stateTime > 3.0f) Play("Idle", 1f, 0.35f);
                        break;
                    case Mood.Crashed:
                        animator.speed = 1f;
                        if (_state == "Hit" && _stateTime > 1.15f) Play("Dizzy", 1f, 0.2f);
                        break;
                    case Mood.Ready:
                    case Mood.Cheering:
                    case Mood.Dancing:
                        animator.speed = 1f;
                        break;
                }
            }
            if (visual == null) return;
            // squash & stretch: a springy height that keeps the volume (wider when squashed)
            _stretchVelocity += ((1f - _stretch) * 300f - _stretchVelocity * 16f) * dt;
            _stretch = Mathf.Clamp(_stretch + _stretchVelocity * dt, 0.72f, 1.3f);
            float side = 1f / Mathf.Sqrt(_stretch);
            visual.localScale = new Vector3(side, _stretch, side);
            // turned toward the camera on the title, leaning into lane changes while running, forward with speed
            bool facing = _mood == Mood.Title || _mood == Mood.Cheering || _mood == Mood.Dancing;
            float toGo = _mood == Mood.Running ? _targetX - X : 0f;
            float yaw = facing ? 180f : Mathf.Clamp(toGo * laneLean, -25f, 25f);
            float roll = _mood == Mood.Running ? Mathf.Clamp(-toGo * 9f, -14f, 14f) : 0f;
            float pitch = _mood == Mood.Running && !InAir ? Mathf.InverseLerp(9f, 20f, RunSpeed) * 7f : 0f;
            float k = 1f - Mathf.Exp(-(facing ? 5f : 14f) * dt);
            _yaw = Mathf.LerpAngle(_yaw, yaw, k);
            _roll = Mathf.Lerp(_roll, roll, 1f - Mathf.Exp(-10f * dt));
            _pitch = Mathf.Lerp(_pitch, pitch, 1f - Mathf.Exp(-4f * dt));
            visual.localRotation = Quaternion.Euler(_pitch, _yaw, _roll);
        }

        /// <summary>Snaps the facing (a new run: no slow turn from the results' facing).</summary>
        public void FaceAway() => _yaw = 0f;

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);
            // blinks (over whatever the clip shows: only open eyes blink)
            if (_eyes != null && _eyes[0] != null)
            {
                _nextBlink -= dt;
                if (_nextBlink <= 0f)
                {
                    _blinkUntil = Time.time + 0.12f;
                    _nextBlink = UnityEngine.Random.Range(1.8f, 4.6f);
                }
                if (Time.time < _blinkUntil)
                    foreach (var e in _eyes)
                        if (e != null && e.localScale.x > 0.5f) e.localScale = new Vector3(e.localScale.x, e.localScale.y * 0.08f, e.localScale.z);
            }
            if (_scarf != null)
                foreach (var c in _scarf)
                    if (c != null) Simulate(c, dt);
        }

        /// <summary>A scarf tail as a little verlet chain: pinned at the knot, blown back by the run's wind (and lifted by
        /// it), pulled down by gravity, fluttering; the bones then point along it.</summary>
        void Simulate(Chain c, float dt)
        {
            if (dt <= 0f) return;
            c.Points[0] = c.Bones[0].position;
            float wind = _mood == Mood.Running ? RunSpeed : 0f;
            if (visual == null) return;
            float t = Time.time * 9f + c.Phase;
            var flutter = new Vector3(Mathf.Sin(t) * 0.6f + Mathf.Sin(t * 2.3f) * 0.3f, Mathf.Sin(t * 1.7f + 1f) * 0.5f, 0f);
            // a scarf streams nearly level in a runner's wind (gravity loses to it): lighter as the wind grows
            float gravity = Mathf.Lerp(7f, 2.2f, Mathf.Clamp01(wind / 9f));
            var accel = new Vector3(0f, -gravity + wind * 0.35f, -wind * 1.8f) + flutter * Mathf.Min(1f, wind / 8f) * 7f;
            float damping = 0.86f;
            for (int i = 1; i < 4; i++)
            {
                var p = c.Points[i];
                var v = (p - c.Prev[i]) * damping;
                c.Prev[i] = p;
                c.Points[i] = p + v + accel * dt * dt;
            }
            for (int pass = 0; pass < 3; pass++)
                for (int i = 1; i < 4; i++)
                {
                    var d = c.Points[i] - c.Points[i - 1];
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    c.Points[i] = c.Points[i - 1] + d * (c.Lengths[i - 1] / len);
                    // don't sink into the back: stay above a line falling gently behind the knot
                    // (behind = against the way he faces; standing, the tails may hang down his back, running they stream)
                    float back = Vector3.Dot(c.Points[0] - c.Points[i], visual.forward);
                    float floor = c.Points[0].y - 0.08f - Mathf.Lerp(1.4f, 0.25f, Mathf.Clamp01(wind / 9f)) * Mathf.Max(0f, back);
                    if (c.Points[i].y < floor) c.Points[i].y = floor;
                }
            for (int i = 0; i < 3; i++)
            {
                var want = (c.Points[i + 1] - c.Points[i]).normalized;
                var now = c.Bones[i].TransformDirection(c.Rest[i]);
                if (want.sqrMagnitude > 0.5f) c.Bones[i].rotation = Quaternion.FromToRotation(now, want) * c.Bones[i].rotation;
            }
        }

        /// <summary>Tests: the scarf's chains were found on the bones.</summary>
        public bool ScarfReady => _scarf != null && _scarf[0] != null && _scarf[1] != null;

        /// <summary>Tests: where a scarf tail's tip is from its knot (world axes), as the bones show it.</summary>
        public Vector3 ScarfTip(int chain)
        {
            if (!ScarfReady) return Vector3.zero;
            var c = _scarf[chain];
            var tip = c.Bones[2].position + c.Bones[2].TransformDirection(c.Rest[2]) * c.Lengths[2];
            return tip - c.Bones[0].position;
        }

        /// <summary>Puts the scarf back at rest (a new run).</summary>
        public void SettleScarf()
        {
            if (_scarf == null) return;
            foreach (var c in _scarf)
            {
                if (c == null) continue;
                for (int i = 0; i < 4; i++)
                    c.Points[i] = c.Prev[i] = i < 3 ? c.Bones[i].position : c.Bones[2].position + c.Bones[2].TransformDirection(c.Rest[2]) * c.Lengths[2];
            }
        }
    }
}
