using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MoveIt.Games.Bowling
{
    /// <summary>
    /// Gray-box ten-pin bowling for 1–4 players taking turns (one person in front of the camera at a time).
    /// Step sideways to place the ball, then swing your arm underhand to roll it. Swing speed = ball speed,
    /// sideways hand motion at release = aim. Real lane size, physics pins, standard scoring.
    /// </summary>
    public sealed class BowlingGame : MonoBehaviour
    {
        enum State { Waiting, SelectPlayers, Aiming, Rolling, Settling, Result, GameOver }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] Material laneMaterial;
        [SerializeField] Material gutterMaterial;
        [SerializeField] Material wallMaterial;
        [SerializeField] Material pinMaterial;
        [SerializeField] Material ballMaterial;
        [SerializeField] Material aimMaterial;

        [Header("Game")]
        [SerializeField, Tooltip("5 = quick family game, 10 = full game.")]
        int frames = 5;
        [SerializeField] int maxPlayers = 4;

        [Header("Throw")]
        [SerializeField] float minBallSpeed = 5.5f;
        [SerializeField] float maxBallSpeed = 10f;
        [SerializeField, Tooltip("Swing speed (torso lengths/s) that gives the slowest ball.")]
        float minSwingSpeed = 1.8f;
        [SerializeField, Tooltip("Swing speed (torso lengths/s) that gives the fastest ball.")]
        float maxSwingSpeed = 6f;
        [SerializeField] float maxAimDegrees = 5f;
        [SerializeField, Tooltip("Degrees of aim per torso length/s of sideways hand speed during the swing.")]
        float aimPerSideSpeed = 2f;
        [SerializeField, Tooltip("Sideways hand speed (torso lengths/s) ignored as jitter, so a normal swing rolls straight.")]
        float aimDeadZone = 0.6f;
        [SerializeField, Tooltip("Metres the ball moves per torso length the player steps sideways.")]
        float positionPerTorso = 0.3f;

        const float LaneHalfWidth = 0.53f;
        const float LaneEnd = 19.2f;
        const float HeadPinZ = 18.29f;
        const float PinSpacing = 0.3048f;
        const float RowSpacing = 0.2639f;
        const float PinHeight = 0.38f;
        const float PinDiameter = 0.12f;
        const float BallRadius = 0.109f;
        const float GutterWidth = 0.24f;
        const float StartZ = 0.4f;
        const float MaxBallX = LaneHalfWidth - BallRadius - 0.02f;

        State _state;
        float _stateTime;
        int _playerCount = 1;
        int _player;
        readonly List<BowlingScore> _scores = new List<BowlingScore>();
        string _resultText;
        float _turnBannerUntil;

        Rigidbody _ball;
        Transform _aimArrow;
        readonly List<Rigidbody> _pins = new List<Rigidbody>();
        readonly List<Vector3> _rack = new List<Vector3>();
        int _standingBefore;
        bool _gutter;
        bool _lastFrameDone;

        float _ballX;
        float _aimAngle;
        bool _keyboardAim;
        bool _charging;
        float _charge;
        int _previousMotionLane;

        readonly UnderhandThrowDetector _throwDetector = new UnderhandThrowDetector();
        BodyInput _input;
        float _savedFixedDeltaTime;

        void OnEnable()
        {
            // Small, fast objects: smaller physics steps keep the ball from tunnelling and pins stable.
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
        }

        void OnDisable() => Time.fixedDeltaTime = _savedFixedDeltaTime;

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            BuildLane();
            BuildPins();
            BuildBall();
            SetState(State.Waiting);
            GameAudio.PlayMusic("bowling");
        }

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _stateTime += dt;

            switch (_state)
            {
                case State.Waiting:
                    if (PlayerGate.StartPressed(_input)) SetState(State.SelectPlayers);
                    break;
                case State.SelectPlayers:
                    UpdateSelectPlayers();
                    break;
                case State.Aiming:
                    UpdateAiming(dt);
                    break;
                case State.Rolling:
                    UpdateRolling();
                    break;
                case State.Settling:
                    if (_stateTime > 2.2f) FinishRoll();
                    break;
                case State.Result:
                    if (_stateTime > 1.8f) NextRoll();
                    break;
                case State.GameOver:
                    if (_stateTime > 2f && PlayerGate.StartPressed(_input)) SetState(State.SelectPlayers);
                    break;
            }
            UpdateCamera(dt);
        }

        bool _pinsSounded;

        void SetState(State s)
        {
            _state = s;
            _stateTime = 0f;
            if (s != State.Result) Debug.Log($"[Bowling] {s}");
        }

        // ---------- flow ----------

        void UpdateSelectPlayers()
        {
            var kb = Keyboard.current;
            int change = 0;
            if (kb != null && kb.leftArrowKey.wasPressedThisFrame) change--;
            if (kb != null && kb.rightArrowKey.wasPressedThisFrame) change++;
            var gestures = _input != null ? _input.Gestures : null;
            int lane = gestures != null ? gestures.Lane : 0;
            if (lane != _previousMotionLane && lane != 0) change += lane;
            _previousMotionLane = lane;
            _playerCount = Mathf.Clamp(_playerCount + change, 1, maxPlayers);

            if (_stateTime > 0.5f && PlayerGate.StartPressed(_input)) StartGame();
        }

        void StartGame()
        {
            _scores.Clear();
            for (int i = 0; i < _playerCount; i++) _scores.Add(new BowlingScore(frames));
            _player = 0;
            ResetRack();
            BeginAiming(showTurn: _playerCount > 1);
        }

        void BeginAiming(bool showTurn)
        {
            if (showTurn) _turnBannerUntil = Time.time + 2f;
            _standingBefore = CountStanding();
            _ball.isKinematic = true;
            _ballX = 0f;
            _aimAngle = 0f;
            _charging = false;
            _keyboardAim = PlayerGate.State(_input) != GateState.Ready;
            _throwDetector.Reset();
            _aimArrow.gameObject.SetActive(true);
            PlaceBall();
            SetState(State.Aiming);
        }

        void UpdateAiming(float dt)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                float move = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                float aim = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
                if (move != 0f || aim != 0f || kb.spaceKey.wasPressedThisFrame) _keyboardAim = true;
                _ballX += move * 0.8f * dt;
                _aimAngle = Mathf.Clamp(_aimAngle + aim * 6f * dt, -maxAimDegrees, maxAimDegrees);

                if (kb.spaceKey.wasPressedThisFrame)
                {
                    _charging = true;
                    _charge = 0f;
                }
                if (_charging)
                {
                    _charge = Mathf.Min(1f, _charge + dt);
                    if (kb.spaceKey.wasReleasedThisFrame)
                    {
                        _charging = false;
                        Throw(Mathf.Lerp(minBallSpeed, maxBallSpeed, _charge), _aimAngle);
                        return;
                    }
                }
            }

            var gestures = _input != null ? _input.Gestures : null;
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            bool motionReady = gestures != null && gestures.IsCalibrated && body != null;
            if (motionReady && !_keyboardAim)
            {
                _ballX = gestures.LateralOffset * positionPerTorso;
                if (_throwDetector.Update(body, gestures.BaselineTorso, out var info) && _stateTime > 0.8f)
                {
                    float t = Mathf.InverseLerp(minSwingSpeed, maxSwingSpeed, info.Speed);
                    float side = Mathf.Sign(info.SideSpeed) * Mathf.Max(0f, Mathf.Abs(info.SideSpeed) - aimDeadZone);
                    float angle = Mathf.Clamp(side * aimPerSideSpeed, -maxAimDegrees, maxAimDegrees);
                    Throw(Mathf.Lerp(minBallSpeed, maxBallSpeed, t), angle);
                    return;
                }
            }
            _ballX = Mathf.Clamp(_ballX, -MaxBallX, MaxBallX);
            PlaceBall();
        }

        void Throw(float speed, float angleDegrees)
        {
            Debug.Log($"[Bowling] Player {_player + 1} throws {speed:0.0} m/s, aim {angleDegrees:0.0}°, from x={_ballX:0.00}");
            var dir = Quaternion.Euler(0f, angleDegrees, 0f) * Vector3.forward;
            _aimArrow.gameObject.SetActive(false);
            _ball.isKinematic = false;
            _ball.linearVelocity = dir * speed;
            _ball.angularVelocity = Vector3.Cross(Vector3.up, dir) * (speed / BallRadius);
            _gutter = false;
            _pinsSounded = false;
            GameAudio.Sfx("roll", 0.9f);
            SetState(State.Rolling);
        }

        void UpdateRolling()
        {
            var p = _ball.position;
            if (Mathf.Abs(p.x) > LaneHalfWidth && p.z < HeadPinZ - 0.3f) _gutter = true;
            if (!_pinsSounded && p.z > HeadPinZ - 0.15f && !_gutter)
            {
                _pinsSounded = true;
                GameAudio.Sfx("pins");
            }
            bool done = p.z > LaneEnd + 0.4f || p.y < -0.4f || _stateTime > 7f
                        || (_stateTime > 1f && _ball.linearVelocity.magnitude < 0.15f);
            if (done) SetState(State.Settling);
        }

        void FinishRoll()
        {
            int standing = CountStanding();
            int knocked = Mathf.Max(0, _standingBefore - standing);
            var score = _scores[_player];
            bool freshRack = _standingBefore == BowlingScore.Pins;
            _lastFrameDone = score.Roll(knocked);

            if (knocked == _standingBefore && knocked > 0) GameAudio.Jingle("jingle_win", 0.35f);   // strike or spare
            if (knocked == _standingBefore && freshRack) _resultText = BowlingStrings.Strike;
            else if (knocked == _standingBefore && knocked > 0) _resultText = BowlingStrings.Spare;
            else if (knocked == 0 && _gutter) _resultText = BowlingStrings.Gutter;
            else _resultText = BowlingStrings.PinsDown(knocked);
            Debug.Log($"[Bowling] Player {_player + 1}: {knocked} pins ({_resultText}), total {score.Total}");
            SetState(State.Result);
        }

        void NextRoll()
        {
            var score = _scores[_player];
            if (_lastFrameDone)
            {
                if (_scores.TrueForAll(s => s.IsComplete))
                {
                    SetState(State.GameOver);
                    return;
                }
                _player = (_player + 1) % _playerCount;
                ResetRack();
                BeginAiming(showTurn: _playerCount > 1);
                return;
            }
            if (score.NeedsFullRack) ResetRack();
            else RemoveFallenPins();
            BeginAiming(showTurn: false);
        }

        // ---------- pins ----------

        bool IsStanding(Rigidbody pin)
        {
            if (!pin.gameObject.activeSelf) return false;
            var p = pin.position;
            return Vector3.Angle(pin.transform.up, Vector3.up) < 20f && p.y > 0.1f && p.z < LaneEnd + 0.3f
                   && Mathf.Abs(p.x) < LaneHalfWidth + 0.1f;
        }

        int CountStanding()
        {
            int n = 0;
            foreach (var pin in _pins)
                if (IsStanding(pin)) n++;
            return n;
        }

        void ResetRack()
        {
            for (int i = 0; i < _pins.Count; i++)
            {
                var pin = _pins[i];
                pin.gameObject.SetActive(true);
                pin.linearVelocity = Vector3.zero;
                pin.angularVelocity = Vector3.zero;
                pin.transform.SetPositionAndRotation(_rack[i], Quaternion.identity);
                pin.position = _rack[i];
                pin.rotation = Quaternion.identity;
                pin.Sleep();
            }
        }

        void RemoveFallenPins()
        {
            foreach (var pin in _pins)
                if (!IsStanding(pin)) pin.gameObject.SetActive(false);
        }

        void PlaceBall()
        {
            var pos = new Vector3(_ballX, BallRadius + 0.005f, StartZ);
            _ball.position = pos;
            _ball.transform.position = pos;
            _aimArrow.position = pos + Quaternion.Euler(0f, _aimAngle, 0f) * new Vector3(0f, -BallRadius + 0.01f, 1.2f);
            _aimArrow.rotation = Quaternion.Euler(0f, _aimAngle, 0f);
        }

        // ---------- camera ----------

        void UpdateCamera(float dt)
        {
            Vector3 pos, look;
            var ball = _ball.position;
            switch (_state)
            {
                case State.Aiming:
                    pos = new Vector3(ball.x * 0.5f, 1.15f, -2f);
                    look = new Vector3(0f, 0.2f, 12f);
                    break;
                case State.Rolling:
                    pos = new Vector3(ball.x * 0.6f, 0.9f, Mathf.Min(ball.z - 2.2f, 13.5f));
                    look = new Vector3(ball.x * 0.3f, 0.2f, Mathf.Min(ball.z + 6f, HeadPinZ));
                    break;
                case State.Settling:
                case State.Result:
                    pos = new Vector3(0f, 1.25f, 15f);
                    look = new Vector3(0f, 0.15f, HeadPinZ + 0.4f);
                    break;
                default:
                    pos = new Vector3(0f, 2.2f, -3f);
                    look = new Vector3(0f, 0f, 10f);
                    break;
            }
            float k = 1f - Mathf.Exp(-4f * dt);
            var t = cam.transform;
            t.position = Vector3.Lerp(t.position, pos, k);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(look - t.position), k);
        }

        // ---------- building the lane ----------

        void BuildLane()
        {
            var lanePhysics = new PhysicsMaterial("Lane") { dynamicFriction = 0.2f, staticFriction = 0.2f, bounciness = 0f };
            // Lane + approach (starts 4 m before the foul line).
            Box("Lane", laneMaterial, lanePhysics, new Vector3(0f, -0.05f, (LaneEnd - 4f) / 2f), new Vector3(LaneHalfWidth * 2f, 0.1f, LaneEnd + 4f));
            float gutterX = LaneHalfWidth + GutterWidth / 2f;
            float wallX = LaneHalfWidth + GutterWidth + 0.03f;
            foreach (float side in new[] { -1f, 1f })
            {
                Box("Gutter", gutterMaterial, lanePhysics, new Vector3(side * gutterX, -0.12f, LaneEnd / 2f), new Vector3(GutterWidth, 0.1f, LaneEnd));
                Box("Wall", wallMaterial, null, new Vector3(side * wallX, 0f, 10.5f), new Vector3(0.06f, 0.3f, 21f));
            }
            Box("Pit Floor", wallMaterial, null, new Vector3(0f, -0.55f, 20.2f), new Vector3(2f, 0.1f, 2f));
            Box("Back Wall", wallMaterial, null, new Vector3(0f, 0.1f, 21f), new Vector3(2f, 1.2f, 0.1f));
        }

        void BuildPins()
        {
            var pinPhysics = new PhysicsMaterial("Pin") { dynamicFriction = 0.4f, staticFriction = 0.4f, bounciness = 0.2f };
            for (int row = 0; row < 4; row++)
            {
                for (int i = 0; i <= row; i++)
                {
                    var pos = new Vector3((i - row / 2f) * PinSpacing, PinHeight / 2f + 0.001f, HeadPinZ + row * RowSpacing);
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.name = $"Pin {_pins.Count + 1}";
                    Destroy(go.GetComponent<Collider>());
                    // Convex mesh keeps the flat base, so pins stand on their own (a capsule would wobble over).
                    var collider = go.AddComponent<MeshCollider>();
                    collider.convex = true;
                    collider.sharedMaterial = pinPhysics;
                    go.GetComponent<Renderer>().sharedMaterial = pinMaterial;
                    go.transform.SetParent(transform, false);
                    go.transform.localScale = new Vector3(PinDiameter, PinHeight / 2f, PinDiameter);
                    go.transform.position = pos;
                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = 1.5f;
                    _pins.Add(rb);
                    _rack.Add(pos);
                }
            }
        }

        void BuildBall()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ball";
            go.GetComponent<Renderer>().sharedMaterial = ballMaterial;
            go.GetComponent<Collider>().sharedMaterial = new PhysicsMaterial("Ball") { dynamicFriction = 0.15f, staticFriction = 0.15f, bounciness = 0.05f };
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * (BallRadius * 2f);
            _ball = go.AddComponent<Rigidbody>();
            _ball.mass = 6f;
            _ball.maxAngularVelocity = 200f;
            _ball.angularDamping = 0.05f;
            _ball.interpolation = RigidbodyInterpolation.Interpolate;
            _ball.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _ball.isKinematic = true;

            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(arrow.GetComponent<Collider>());
            arrow.name = "Aim";
            arrow.GetComponent<Renderer>().sharedMaterial = aimMaterial;
            arrow.transform.SetParent(transform, false);
            arrow.transform.localScale = new Vector3(0.04f, 0.01f, 2f);
            _aimArrow = arrow.transform;
        }

        void Box(string name, Material material, PhysicsMaterial physics, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.GetComponent<Collider>().sharedMaterial = physics;
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            go.transform.localScale = size;
        }

        // ---------- HUD ----------

        void OnGUI()
        {
            float w = GameHud.Begin();
            switch (_state)
            {
                case State.Waiting:
                    PlayerGate.DrawWaiting(w, _input, BowlingStrings.Title, BowlingStrings.KeyboardHelp);
                    break;

                case State.SelectPlayers:
                    GameHud.Text(new Rect(0, 160, w, 120), BowlingStrings.HowManyPlayers, GameHud.Big);
                    GameHud.Text(new Rect(0, 320, w, 200), $"‹  {_playerCount}  ›", GameHud.Big, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.2f));
                    GameHud.Text(new Rect(0, 560, w, 60),
                        PlayerGate.State(_input) == GateState.Ready ? BowlingStrings.PlayersHint : BowlingStrings.PlayersKeyboardHint, GameHud.Small);
                    break;

                case State.Aiming:
                    DrawScoreboard(w);
                    if (Time.time < _turnBannerUntil)
                        GameHud.Message(w, BowlingStrings.Turn(_player + 1));
                    else if (!_keyboardAim && PlayerGate.ShouldPause(_input))
                        PlayerGate.DrawPaused(w, _input);
                    GameHud.Text(new Rect(0, 960, w, 60),
                        _keyboardAim ? BowlingStrings.KeyboardHelp : BowlingStrings.AimHint, GameHud.Small);
                    if (_charging)
                    {
                        GameHud.Box(new Rect(w / 2 - 300, 900, 600, 30), new Color(0f, 0f, 0f, 0.6f));
                        GameHud.Box(new Rect(w / 2 - 300, 900, 600 * _charge, 30), Color.Lerp(Color.green, Color.red, _charge));
                    }
                    break;

                case State.Rolling:
                case State.Settling:
                    DrawScoreboard(w);
                    break;

                case State.Result:
                    DrawScoreboard(w);
                    GameHud.Message(w, _resultText, null,
                        _resultText == BowlingStrings.Strike || _resultText == BowlingStrings.Spare ? new Color(1f, 0.85f, 0.2f) : Color.white);
                    break;

                case State.GameOver:
                    DrawScoreboard(w);
                    GameHud.Message(w, WinnerText(), CommonStrings.GameOver);
                    if (_stateTime > 2f)
                        GameHud.Text(new Rect(0, 540, w, 80),
                            PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter : CommonStrings.PlayAgain, GameHud.Small);
                    break;
            }
        }

        string WinnerText()
        {
            if (_scores.Count == 1) return $"{BowlingStrings.Total}: {_scores[0].Total}";
            int best = -1, winner = 0;
            bool draw = false;
            for (int i = 0; i < _scores.Count; i++)
            {
                int total = _scores[i].Total;
                if (total > best)
                {
                    best = total;
                    winner = i;
                    draw = false;
                }
                else if (total == best) draw = true;
            }
            return draw ? BowlingStrings.Draw : BowlingStrings.Winner(winner + 1);
        }

        void DrawScoreboard(float w)
        {
            const float nameWidth = 200f, cellWidth = 90f, totalWidth = 130f, rowHeight = 70f;
            float tableWidth = nameWidth + cellWidth * frames + totalWidth;
            float x0 = (w - tableWidth) / 2f, y0 = 20f;
            for (int p = 0; p < _scores.Count; p++)
            {
                var score = _scores[p];
                float y = y0 + p * (rowHeight + 6f);
                bool current = p == _player && _state != State.GameOver;
                GameHud.Box(new Rect(x0, y, tableWidth, rowHeight), current ? new Color(0.35f, 0.3f, 0.05f, 0.85f) : new Color(0f, 0f, 0f, 0.6f));
                GameHud.Text(new Rect(x0 + 12, y, nameWidth, rowHeight), BowlingStrings.Player(p + 1), GameHud.Small, TextAnchor.MiddleLeft);
                var cumulative = score.CumulativeScores();
                for (int f = 0; f < frames; f++)
                {
                    float cx = x0 + nameWidth + f * cellWidth;
                    GameHud.Box(new Rect(cx, y, 2, rowHeight), new Color(1f, 1f, 1f, 0.3f));
                    GameHud.Text(new Rect(cx, y + 2, cellWidth, rowHeight / 2), score.Marks(f), GameHud.Small);
                    if (cumulative[f].HasValue)
                        GameHud.Text(new Rect(cx, y + rowHeight / 2 - 4, cellWidth, rowHeight / 2), cumulative[f].Value.ToString(), GameHud.Small);
                }
                GameHud.Text(new Rect(x0 + nameWidth + frames * cellWidth, y, totalWidth, rowHeight), score.Total.ToString(), GameHud.Medium);
            }
        }
    }
}
