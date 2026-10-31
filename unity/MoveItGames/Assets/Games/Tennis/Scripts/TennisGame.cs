using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Joint = MoveIt.Motion.Joint;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// "Tenis!": singles against a computer player, after Wii Sports Tennis. The rules are <see cref="TennisMatch"/> (no
    /// scene); this draws them: the stadium (<see cref="TennisStadium"/>), the two toy players
    /// (<see cref="TennisPlayerView"/>, the player in sky blue, seen from behind), the ball with its shadow, a trail and a
    /// ring that closes on it at the moment to swing, a marker on the court where the player will hit from, sparks, the
    /// umpire's calls, the scoreboard, sounds.
    /// <list type="bullet">
    /// <item>The body moves the player (<see cref="TennisMatch.BodyToCourt"/>): sideways from where they stood when the
    /// match started (<see cref="BodyGestures.LateralOffset"/>), forward and back from how big the torso looks compared
    /// with then (smoothed). The rules' assist glides the last metre or so to the ball.</item>
    /// <item>Swings come from the camera (<see cref="SwingDetector"/> on the low-lag joints): any quick move of a hand. Each
    /// is passed on with the moment the camera saw it, so the camera's lag doesn't make every swing late.</item>
    /// <item>A run is a ladder: win the match (first to 2 games) and a stronger player steps up; lose and it's over. The
    /// best run (matches won) is saved.</item>
    /// <item>Keyboard or remote without a camera: the arrows move, space / Enter / OK swings (hold W for a lob, S for a
    /// slice). H (or the remote's ↑ with the camera) puts the racket in the other hand.</item>
    /// </list>
    /// </summary>
    public sealed class TennisGame : MonoBehaviour
    {
        public enum Phase { Waiting, Intro, Playing, MatchEnd, Results }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField, Tooltip("Tundu/Toon with vertex colours and gloss: the players.")] Material actorMaterial;
        [SerializeField, Tooltip("Tundu/Toon with vertex colours, matte: the stadium.")] Material stadiumMaterial;
        [SerializeField, Tooltip("Tundu/Fx, alpha: the net (its cell texture is drawn in code).")] Material netMaterial;
        [SerializeField, Tooltip("Tundu/Toon, the ball's yellow, a little emission.")] Material ballMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: sparks.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the soft dot: dust puffs and the ball's shadow.")] Material dotMaterial;
        [SerializeField, Tooltip("Tundu/Fx additive: the ball's and the rackets' trails.")] Material trailMaterial;
        [SerializeField, Tooltip("Tundu/Fx drawn on top: the timing ring.")] Material markerMaterial;

        [Header("Match")]
        [SerializeField, Tooltip("Games to win a match (Wii: 1, 3 or 5 game matches; 2 = best of 3).")] int gamesToWin = 2;
        [SerializeField] float introSeconds = 3.2f, matchEndSeconds = 3.5f;
        [SerializeField, Tooltip("The ball is drawn this big (m across): a real one (6.7 cm) is hard to see on a TV.")] float ballSize = 0.3f;
        [SerializeField, Tooltip("The ring that closes on the ball shows from this long (s) before the moment to swing.")] float ringLead = 0.9f;

        const string BestKey = "tennis.best", HandKey = "tennis.rightHanded";
        static readonly Color Sun = TennisStadium.Sun, Coral = TennisStadium.Coral, Sky = TennisStadium.Sky, Leaf = TennisStadium.Leaf;
        static readonly Color[] TheirShirts = { TennisStadium.Coral, TennisStadium.Grape, TennisStadium.Leaf, new Color(1f, 0.55f, 0.15f) };

        public Phase Current { get; private set; }
        public TennisMatch Match => _match;
        public int Level { get; private set; } = 1;
        public int MatchesWon { get; private set; }
        public int Best { get; private set; }
        public bool RightHanded { get; private set; } = true;
        /// <summary>Practice (set before the player starts): no score, the computer returns everything and times its
        /// feeds with <see cref="PracticeContact"/> (tests).</summary>
        public bool Practice { get; set; }
        public Func<double, double> PracticeContact { get; set; }
        /// <summary>Who serves first in the next match (0 = the player; tests set 1 to watch the player move while
        /// receiving).</summary>
        public int FirstServer { get; set; }
        public Func<double, double> PracticeToss { get; set; }
        /// <summary>Every swing the camera saw while playing: the match time it was mapped to, and what it did.</summary>
        public event Action<SwingEvent, double, SwingResult> SwingHandled;
        public event Action<MatchEvent> MatchEvents;
        public int SwingsSeen { get; private set; }

        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        TennisMatch _match;
        TennisStadium _stadium;
        readonly TennisPlayerView[] _views = new TennisPlayerView[2];
        readonly SwingDetector _detector = new SwingDetector();
        double _lastSample;
        int _viewsLevel = -1;
        float _phaseTime;
        bool _paused, _newBest;
        int _longestRally, _hintedMisses, _humanHitsThisRun;

        Transform _ball, _shadow, _ring, _spot;
        float _nearer = 1f, _lateral, _depthBase;
        Vector2 _keyWanted = new Vector2(0f, -TennisMatch.HomeDepth);
        TrailRenderer _ballTrail;
        MaterialPropertyBlock _tint;
        Texture2D _dot, _netCell;
        Material _net;
        Vector3 _cameraHome, _lookHome;
        float _shake, _cameraX, _cameraZ;

        string _call, _callSub;
        float _callUntil;
        string _hint;
        float _hintUntil;
        readonly List<Popup> _popups = new List<Popup>();
        readonly List<Bit> _bits = new List<Bit>();

        sealed class Popup { public Vector3 World; public string Text; public Color Color; public float Age; }
        sealed class Bit { public Transform T; public Vector3 Velocity, Scale; public float Age, Life, Spin, Grow; public bool Gravity, Flat; }

        // ---------------------------------------------------------------- lifecycle

        /// <summary>
        /// 0.9 torso lengths sideways reach the sideline (<see cref="TennisMatch.BodyToCourt"/>), a step toward the TV makes
        /// the body look up to 20 % bigger at the net: the whole body must still fit then.
        /// </summary>
        static readonly PlayArea Area = new PlayArea(BodyPart.Knees, side: 1f, above: 0.1f, grow: 0.2f);

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            PlayerGate.Use(_input, Area);
            // the small skeleton mirror sits in the bottom-right corner, where the player goes when stepping right: shown
            // only while waiting, on the results and while paused (given back to the next scene as it was)
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null) _mirrorWasVisible = _mirror.Visible;
            Best = PlayerPrefs.GetInt(BestKey, 0);
            RightHanded = PlayerPrefs.GetInt(HandKey, 1) == 1;
            _tint = new MaterialPropertyBlock();
            _dot = TennisArt.Dot();
            _netCell = TennisArt.NetCell();
            _net = new Material(netMaterial) { name = "Net (cells)" };   // a copy: the texture is made in code
            _net.SetTexture("_BaseMap", _netCell);
            _stadium = TennisStadium.Build(stadiumMaterial, _net);
            BuildBall();
            _cameraHome = cam.transform.position;
            _lookHome = _cameraHome + cam.transform.forward * 20f;
            NewMatch(1);
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("tennis");
        }

        void OnDestroy()
        {
            if (_dot != null) Destroy(_dot);
            if (_netCell != null) Destroy(_netCell);
            if (_net != null) Destroy(_net);
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
        }

        void BuildBall()
        {
            _ball = Shape("Ball", PartMesh.Primitive(PrimitiveType.Sphere), ballMaterial, true);
            _ball.localScale = Vector3.one * ballSize;
            _ballTrail = _ball.gameObject.AddComponent<TrailRenderer>();
            _ballTrail.sharedMaterial = trailMaterial;
            _ballTrail.time = 0.18f;
            _ballTrail.minVertexDistance = 0.05f;
            _ballTrail.widthCurve = new AnimationCurve(new Keyframe(0f, ballSize * 0.9f), new Keyframe(1f, 0f));
            _ballTrail.shadowCastingMode = ShadowCastingMode.Off;
            SetTrail(Color.white, 0.45f);
            _shadow = Shape("Ball shadow", PartMesh.Primitive(PrimitiveType.Quad), dotMaterial, false);
            _shadow.rotation = Quaternion.Euler(90f, 0f, 0f);
            _ring = Shape("Timing ring", TennisArt.Ring(0.4f), markerMaterial, false);
            _ring.gameObject.SetActive(false);
            _spot = Shape("Hit spot", TennisArt.Ring(0.36f), dotMaterial, false);
            _spot.gameObject.SetActive(false);
        }

        Transform Shape(string name, Mesh mesh, Material material, bool shadows)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void SetTrail(Color c, float alpha)
        {
            _ballTrail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(0f, 1f) },
            };
        }

        /// <summary>A fresh match against the ladder's <paramref name="level"/>-th player (their look changes with it).</summary>
        void NewMatch(int level)
        {
            Level = level;
            if (_match != null) _match.Events -= OnMatchEvent;
            _match = new TennisMatch(TennisLevel.For(level), gamesToWin, Random.Range(0, 1 << 30), FirstServer)
            {
                Practice = Practice, PracticeContact = PracticeContact, PracticeToss = PracticeToss, HumanRightHanded = RightHanded,
            };
            _match.Events += OnMatchEvent;
            _match.Begin();   // the players take their places (the clock runs once play starts)
            var their = _match.Level;
            var looks = new[]
            {
                new TennisPlayerView.Look { Shirt = Sky, Accent = Sun, Shorts = Color.white, Skin = new Color(0.97f, 0.8f, 0.66f), Hair = new Color(0.25f, 0.16f, 0.1f), Shoes = Coral, HairStyle = 0 },
                new TennisPlayerView.Look { Shirt = TheirShirts[their.Colour], Accent = Color.white, Shorts = new Color(0.2f, 0.22f, 0.4f),
                                            Skin = level % 3 == 1 ? new Color(0.98f, 0.84f, 0.72f) : level % 3 == 2 ? new Color(0.78f, 0.55f, 0.4f) : new Color(0.62f, 0.42f, 0.3f),
                                            Hair = level % 3 == 0 ? new Color(0.88f, 0.7f, 0.33f) : level % 2 == 0 ? new Color(0.7f, 0.29f, 0.18f) : new Color(0.25f, 0.16f, 0.1f),
                                            Shoes = Color.white, HairStyle = level % 5 },
            };
            for (int i = 0; i < 2; i++)
            {
                // the player stays the same; the opponent is new for every match
                if (_views[i] != null && (i == 0 || _viewsLevel == level)) continue;
                if (_views[i] != null) Destroy(_views[i].gameObject);
                _views[i] = TennisPlayerView.Create(i == 0 ? "Player" : $"Opponent (match {level})", actorMaterial, trailMaterial, looks[i]);
            }
            _viewsLevel = level;
            _detector.Reset();
            _match.HumanWanted = _keyWanted = new Vector2(0f, -TennisMatch.HomeDepth);
            Debug.Log($"[Tennis] Match {level} vs {their.Name}{(Practice ? " (practice)" : "")}");
        }

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            Debug.Log(phase == Phase.Results ? $"[Tennis] Results matches={MatchesWon} best={Best} longestRally={_longestRally}" : $"[Tennis] {phase}");
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            HandleKeys();
            ReadBody();
            Steer(dt);

            switch (Current)
            {
                case Phase.Waiting:
                    if (StartPressed()) StartRun();
                    break;
                case Phase.Intro:
                    if (_phaseTime >= introSeconds)
                    {
                        GameAudio.Sfx("go", vary: 0f);
                        SetPhase(Phase.Playing);
                    }
                    break;
                case Phase.Playing:
                    _paused = !UsingKeys && PlayerGate.ShouldPause(_input);
                    if (!_paused) _match.Step(dt);
                    if (_match.Phase == MatchPhase.Over) EndMatch();
                    break;
                case Phase.MatchEnd:
                    _match.Step(dt);
                    if (_phaseTime >= matchEndSeconds)
                    {
                        if (_match.Score.Winner == 0 || Practice) StartMatch(Level + 1);
                        else SetPhase(Phase.Results);
                    }
                    break;
                case Phase.Results:
                    if (_phaseTime > 1.5f && StartPressed()) StartRun();
                    break;
            }

            if (_mirror != null) _mirror.Visible = _mirrorWasVisible && (Current is Phase.Waiting or Phase.Results || _paused);
            UpdateViews(dt);
            UpdateBall();
            UpdateBits(dt);
            UpdateCamera(dt);
        }

        bool UsingKeys => _input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService;

        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        void StartRun()
        {
            MatchesWon = 0;
            _longestRally = 0;
            _newBest = false;
            _hintedMisses = 0;
            _humanHitsThisRun = 0;
            GameAudio.PlayMusic("tennis");
            StartMatch(1);
        }

        void StartMatch(int level)
        {
            NewMatch(level);
            ShowCall(TennisStrings.Match(level), TennisStrings.Versus(_match.Level.Name), introSeconds);
            GameAudio.Sfx("tennis_call", vary: 0f);
            // where the player stands now is the middle of the baseline
            if (_input != null && _input.Gestures != null) _input.Gestures.Recenter();
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            _depthBase = body != null && body.IsTracking ? body.TorsoLength : 0f;
            _nearer = 1f;
            _lateral = 0f;
            SetPhase(Phase.Intro);
        }

        void EndMatch()
        {
            _longestRally = Mathf.Max(_longestRally, _match.LongestRally);
            bool won = _match.Score.Winner == 0;
            if (won && !Practice)
            {
                MatchesWon++;
                if (MatchesWon > Best)
                {
                    Best = MatchesWon;
                    _newBest = true;
                    PlayerPrefs.SetInt(BestKey, Best);
                    PlayerPrefs.Save();
                }
            }
            GameAudio.Jingle(won ? "jingle_win" : "jingle_gameover");
            if (won) _stadium.Cheer(1f);
            ShowCall(won ? TennisStrings.YouWon : TennisStrings.TheyWon(_match.Level.Name), won ? TennisStrings.NextPlayer : null, matchEndSeconds);
            Debug.Log($"[Tennis] Match {Level} {(won ? "won" : "lost")} {_match.Score.Games(0)}-{_match.Score.Games(1)}, points {_match.PointsWon}-{_match.PointsLost}, " +
                      $"hits {_match.HumanHits} ({_match.HumanPerfect} perfect), air swings {_match.HumanWhiffs}, longest rally {_match.LongestRally}");
            SetPhase(Phase.MatchEnd);
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if ((kb != null && kb.hKey.wasPressedThisFrame) || (!UsingKeys && RemoteInput.Up))
            {
                RightHanded = !RightHanded;
                _match.HumanRightHanded = RightHanded;
                PlayerPrefs.SetInt(HandKey, RightHanded ? 1 : 0);
                PlayerPrefs.Save();
                ShowHint(TennisStrings.HandSwapped(RightHanded), 2f);
            }
            if (Current != Phase.Playing || _paused) return;
            // space, Enter or the remote's OK: a swing right now (W held = up = a lob, S held = a slice)
            if (!RemoteInput.Confirm) return;
            bool lob = kb != null && kb.wKey.isPressed, slice = kb != null && kb.sKey.isPressed;
            var dir = new Vector2(-1f, lob ? 0.85f : slice ? -0.8f : 0.1f).normalized;
            var result = _match.Swing(_match.Time, lob ? 4f : 7f, dir);
            if (result is SwingResult.Early or SwingResult.Late or SwingResult.NoBall) GameAudio.Sfx("tennis_swish", 0.6f);
        }

        /// <summary>
        /// Where the player's body (or the arrows) puts their player on the court. The body: sideways from where they
        /// stood when the match started, toward or away from the TV from how big the torso looks compared with then
        /// (smoothed over a quarter second: a front camera's depth is rough, and the rules' assist covers the rest).
        /// </summary>
        void Steer(float dt)
        {
            if (Current is not (Phase.Playing or Phase.Intro or Phase.MatchEnd)) return;
            if (UsingKeys)
            {
                var kb = Keyboard.current;
                if (kb != null && Current == Phase.Playing && !_paused)
                {
                    var move = new Vector2((kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f),
                                           (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f));
                    _keyWanted += move * (7f * dt);
                }
                _match.HumanWanted = _keyWanted;
                _keyWanted = _match.HumanWanted;   // kept on the player's half
                return;
            }
            var g = _input != null ? _input.Gestures : null;
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            if (g == null || body == null || !body.IsTracking || !g.IsCalibrated) return;
            if (_depthBase <= 0f) _depthBase = body.TorsoLength;
            _lateral = Mathf.Lerp(_lateral, g.LateralOffset, 1f - Mathf.Exp(-dt / 0.06f));
            _nearer = Mathf.Lerp(_nearer, body.TorsoLength / _depthBase, 1f - Mathf.Exp(-dt / 0.25f));
            _match.HumanWanted = TennisMatch.BodyToCourt(_lateral, _nearer);
        }

        /// <summary>Every new camera sample goes to the swing detector (also while waiting, so it's settled when play
        /// starts); a swing is passed to the match with the match time the camera saw it.</summary>
        void ReadBody()
        {
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            if (body == null || !body.IsTracking || body.SampleTime == _lastSample) return;
            _lastSample = body.SampleTime;
            if (!_detector.Update(body.SampleTime, body.FastShoulderCenter, body.FastTorsoLength,
                                  body.Fast[(int)Joint.LeftWrist], body.Visibility[(int)Joint.LeftWrist],
                                  body.Fast[(int)Joint.RightWrist], body.Visibility[(int)Joint.RightWrist], out var swing))
                return;
            SwingsSeen++;
            if (Current != Phase.Playing || _paused) return;
            double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            double age = Math.Max(0.0, Math.Min(0.35, nowUnix - swing.Time));
            double at = _match.Time - age;
            var result = _match.Swing(at, swing.Speed, swing.Direction);
            if (result is SwingResult.Early or SwingResult.Late or SwingResult.NoBall) GameAudio.Sfx("tennis_swish", 0.5f, 1f + Random.Range(-0.08f, 0.08f));
            Debug.Log($"[Tennis] Swing {(swing.RightHand ? "right" : "left")} {swing.Speed:0.0} torso/s dir ({swing.Direction.x:0.0}, {swing.Direction.y:0.0}), " +
                      $"seen {age * 1000:0} ms ago: {result}");
            SwingHandled?.Invoke(swing, at, result);
        }

        // ---------------------------------------------------------------- match events: sounds, effects, calls

        void OnMatchEvent(MatchEvent e)
        {
            MatchEvents?.Invoke(e);
            switch (e.Kind)
            {
                case MatchEventKind.Toss:
                    GameAudio.Sfx("tennis_toss", 0.5f);
                    if (e.Team == 0 && (_match.TossesMissed > 0 || _humanHitsThisRun == 0)) ShowHint(TennisStrings.YourServe, 2.2f);
                    break;
                case MatchEventKind.Hit:
                    OnHit(e);
                    break;
                case MatchEventKind.Bounce:
                    float speed = _match.Ball.Velocity.magnitude;
                    GameAudio.Sfx("tennis_bounce", Mathf.Clamp(0.3f + speed * 0.04f, 0.3f, 0.9f));
                    Puff(e.Position, e.In ? new Color(1f, 1f, 1f, 0.6f) : new Color(1f, 0.4f, 0.35f, 0.8f));
                    if (!e.In) AddPopup(e.Position + Vector3.up * 0.6f, TennisStrings.Out, Coral);
                    break;
                case MatchEventKind.Net:
                    GameAudio.Sfx("tennis_net");
                    AddPopup(e.Position + Vector3.up * 0.8f, TennisStrings.Net, Coral);
                    break;
                case MatchEventKind.Missed:
                    if (!_match.Next.Reachable) ShowHint(TennisStrings.MoveToTheBall, 2.5f);
                    else if (_hintedMisses++ < 3) ShowHint(TennisStrings.SwingNow, 2.5f);
                    break;
                case MatchEventKind.TooFar:
                    GameAudio.Sfx("tennis_swish", 0.5f);
                    ShowHint(TennisStrings.GetCloser, 2.5f);
                    break;
                case MatchEventKind.Point:
                    OnPoint(e);
                    break;
            }
        }

        void OnHit(MatchEvent e)
        {
            var s = e.Shot;
            bool power = s.Kind is ShotKind.PowerServe or ShotKind.Smash || s.Speed >= 20f;
            GameAudio.Sfx(power ? "tennis_power" : "tennis_hit", 0.9f, (e.Team == 0 ? 1.05f : 0.95f) + Random.Range(-0.05f, 0.05f));
            var view = _views[e.Player];
            Sparks(e.Position, power ? 14 : 7, power ? new Color(1f, 0.6f, 0.2f) : Color.white);
            SetTrail(power ? new Color(1f, 0.5f, 0.15f) : s.Perfect ? Sun : Color.white, power ? 0.85f : 0.45f);
            if (e.Team != 0) return;
            _humanHitsThisRun++;
            view.Flash(s.Perfect ? Sun : Color.white, s.Perfect ? 0.8f : 0.3f);
            // how it went: the timing (it aimed the ball), or the kind of shot when it's a special one
            string text = s.Kind switch
            {
                ShotKind.PowerServe => TennisStrings.PowerServe,
                ShotKind.Smash => TennisStrings.Smash,
                ShotKind.Lob => TennisStrings.Lob,
                ShotKind.Serve => null,
                _ => s.Perfect ? TennisStrings.Perfect : s.Timing < -0.45f ? TennisStrings.Early : s.Timing > 0.45f ? TennisStrings.Late : null,
            };
            if (text != null) AddPopup(e.Position + Vector3.up * 0.9f, text, s.Perfect || power ? Sun : Color.white);
            Debug.Log($"[Tennis] Hit {s.Kind} {s.Stroke} {s.Speed:0.0} m/s timing {s.Timing:0.00}{(s.Perfect ? " perfect" : "")} to ({s.Target.x:0.0}, {s.Target.z:0.0})");
        }

        void OnPoint(MatchEvent e)
        {
            bool ours = e.Team == 0;
            _longestRally = Mathf.Max(_longestRally, _match.RallyHits);
            if (ours)
            {
                _stadium.Cheer(0.8f);
                GameAudio.Sfx("tennis_cheer", 0.8f);
            }
            else GameAudio.Sfx("tennis_aww", 0.7f);
            if (Practice) return;
            var score = _match.Score;
            string us = TennisStrings.You, them = _match.Level.Name;
            string Name(int team) => team == 0 ? us : them;
            string call = e.Call switch
            {
                ScoreCall.Deuce => TennisStrings.Deuce,
                ScoreCall.Advantage => TennisStrings.Advantage(Name(score.Advantage)),
                ScoreCall.Game => TennisStrings.Game(Name(e.Team)),
                ScoreCall.Match => TennisStrings.Game(Name(e.Team)),
                _ => $"{Spoken(score.Points(score.Server))} - {Spoken(score.Points(1 - score.Server))}",
            };
            string sub = null;
            if (e.Call is ScoreCall.Points or ScoreCall.Advantage)
            {
                if (score.MatchPoint(0) || score.MatchPoint(1)) sub = TennisStrings.MatchPoint;
                else if (score.GamePoint(0) || score.GamePoint(1)) sub = TennisStrings.GamePoint;
            }
            if (ours && e.Reason == PointReason.Winner)
                AddPopup(_match.BallPosition + Vector3.up * 0.8f, _match.RallyHits == 1 && _match.LastWasServe ? TennisStrings.Ace : TennisStrings.Winner, Sun);
            ShowCall(call, sub, 1.8f, 0.5f);
            GameAudio.Sfx("tennis_call", 0.6f, vary: 0f);
            Debug.Log($"[Tennis] Point to {(ours ? "you" : "them")} ({e.Reason}): {score.Games(0)}-{score.Games(1)}, " +
                      $"{score.PointsText(0)}-{score.PointsText(1)} ({e.Call})");
        }

        static string Spoken(int points) => points switch { 0 => TennisStrings.Love, 1 => "15", 2 => "30", _ => "40" };

        void ShowCall(string call, string sub, float seconds, float delay = 0f)
        {
            _call = call;
            _callSub = sub;
            _callUntil = Time.time + delay + seconds;
            _callFrom = Time.time + delay;
        }

        float _callFrom;

        void ShowHint(string text, float seconds)
        {
            _hint = text;
            _hintUntil = Time.time + seconds;
        }

        // ---------------------------------------------------------------- drawing the match

        void UpdateViews(float dt)
        {
            double now = _match.Time;
            for (int i = 0; i < 2; i++)
                if (_views[i] != null) _views[i].Animate(_match.Players[i], now, Current == Phase.Playing && _paused ? 0f : dt, _match.Players[i].RightHanded);
        }

        void UpdateBall()
        {
            var p = _match.BallPosition;
            bool show = Mathf.Abs(p.z) < 32f && Mathf.Abs(p.x) < 25f && Current != Phase.Waiting || !_match.BallLive;
            _ball.gameObject.SetActive(show);
            if (!show) return;
            _ball.position = p;
            var v = _match.Ball.Velocity;
            if (v.sqrMagnitude > 1f)
                _ball.rotation = Quaternion.AngleAxis(v.magnitude * 400f * Time.deltaTime, Vector3.Cross(Vector3.up, v).normalized) * _ball.rotation;
            if (!_match.BallLive) _ballTrail.Clear();   // back in the server's hand: no streak across the court
            _ballTrail.emitting = _match.BallLive && v.sqrMagnitude > 9f;

            // the shadow: on the ground under the ball, bigger and fainter the higher it is
            float h = Mathf.Max(0f, p.y);
            _shadow.position = new Vector3(p.x, 0.012f, p.z);
            _shadow.localScale = Vector3.one * (ballSize * 1.4f + h * 0.12f);
            _tint.SetColor("_BaseColor", new Color(0.05f, 0.08f, 0.25f, Mathf.Lerp(0.55f, 0.15f, h / 4f)));
            _shadow.GetComponent<Renderer>().SetPropertyBlock(_tint);

            UpdateRing(p);
            UpdateSpot();
        }

        /// <summary>A ring on the court where the player will hit the incoming ball from: green while the glide gets there
        /// from where the body stands, coral when the body has to move closer.</summary>
        void UpdateSpot()
        {
            var m = _match;
            bool show = Current == Phase.Playing && !_paused && m.Phase == MatchPhase.Rally && m.Next.Active && m.Next.Team == 0 && !m.Next.Taken;
            _spot.gameObject.SetActive(show);
            if (!show) return;
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 9f);
            _spot.SetPositionAndRotation(new Vector3(m.Next.Spot.x, 0.015f, m.Next.Spot.y), Quaternion.Euler(90f, 0f, 0f));
            _spot.localScale = Vector3.one * 1.5f * pulse;
            var c = m.Next.Reachable ? Leaf : Coral;
            c.a = 0.85f;
            _tint.SetColor("_BaseColor", c);
            _spot.GetComponent<Renderer>().SetPropertyBlock(_tint);
        }

        /// <summary>The timing ring: around the ball as it comes to one of the player's players (or at the top of their
        /// toss), closing to the ball's size at the moment to swing, turning green then.</summary>
        void UpdateRing(Vector3 ball)
        {
            double at = -1;
            var m = _match;
            if (Current == Phase.Playing && !_paused)
            {
                if (m.Phase == MatchPhase.Rally && m.Next.Active && m.Next.Team == 0 && m.Next.Reachable && !m.Next.Taken) at = m.Next.Time;
                else if (m.Phase == MatchPhase.Toss && m.ServerTeam == 0) at = m.TossTop;
            }
            float left = (float)(at - m.Time);
            bool show = at > 0 && left <= ringLead && left > -m.LateWindow;
            _ring.gameObject.SetActive(show);
            if (!show) return;
            float k = Mathf.Clamp01(left / ringLead);
            _ring.SetPositionAndRotation(ball, cam.transform.rotation);
            _ring.localScale = Vector3.one * ballSize * (1.6f + 5f * k);
            var c = left <= 0.08f ? Leaf : Color.Lerp(Sun, Color.white, k);
            c.a = Mathf.Lerp(1f, 0.35f, k);
            _tint.SetColor("_BaseColor", c);
            _ring.GetComponent<Renderer>().SetPropertyBlock(_tint);
        }

        void UpdateCamera(float dt)
        {
            // behind and above the player, following it a little from side to side and toward the net
            var you = _match.Human;
            float ease = 1f - Mathf.Exp(-2f * dt);
            _cameraX = Mathf.Lerp(_cameraX, Mathf.Clamp(you.Position.x * 0.5f, -2.8f, 2.8f), ease);
            _cameraZ = Mathf.Lerp(_cameraZ, Mathf.Clamp((you.Position.y + TennisMatch.HomeDepth) * 0.35f, -1f, 3.5f), ease);
            _shake = Mathf.Max(0f, _shake - dt);
            var shake = (Vector3)(Random.insideUnitCircle * (0.1f * _shake));
            cam.transform.position = _cameraHome + new Vector3(_cameraX, 0f, _cameraZ) + shake;
            cam.transform.LookAt(_lookHome + new Vector3(_cameraX * 0.4f, 0f, _cameraZ * 0.3f));
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].Age += dt;
                _popups[i].World += Vector3.up * (0.7f * dt);
                if (_popups[i].Age > 1.2f) _popups.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- effects

        void Sparks(Vector3 at, int count, Color color)
        {
            for (int i = 0; i < count; i++)
            {
                var t = Shape("Spark", PartMesh.Primitive(PrimitiveType.Quad), sparkMaterial, false);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", color);
                t.GetComponent<Renderer>().SetPropertyBlock(block);
                t.position = at;
                float size = Random.Range(0.12f, 0.24f);
                _bits.Add(new Bit { T = t, Scale = Vector3.one * size, Life = Random.Range(0.25f, 0.45f), Velocity = Random.onUnitSphere * Random.Range(2.5f, 5f),
                                    Spin = Random.Range(-720f, 720f) });
            }
        }

        /// <summary>A puff of court dust where the ball bounced, flat on the ground and spreading.</summary>
        void Puff(Vector3 at, Color color)
        {
            var t = Shape("Puff", PartMesh.Primitive(PrimitiveType.Quad), dotMaterial, false);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            t.GetComponent<Renderer>().SetPropertyBlock(block);
            t.SetPositionAndRotation(new Vector3(at.x, 0.02f, at.z), Quaternion.Euler(90f, 0f, 0f));
            _bits.Add(new Bit { T = t, Scale = Vector3.one * 0.35f, Life = 0.45f, Grow = 2.2f, Flat = true });
        }

        void UpdateBits(float dt)
        {
            var camRot = cam.transform.rotation;
            for (int i = _bits.Count - 1; i >= 0; i--)
            {
                var b = _bits[i];
                b.Age += dt;
                if (b.Age >= b.Life)
                {
                    Destroy(b.T.gameObject);
                    _bits.RemoveAt(i);
                    continue;
                }
                float fade = Mathf.Clamp01((b.Life - b.Age) / (b.Life * 0.5f));
                if (b.Flat)
                {
                    b.T.localScale = b.Scale * (1f + b.Grow * b.Age / b.Life) * Mathf.Lerp(0.6f, 1f, fade);
                    continue;
                }
                if (b.Gravity) b.Velocity += Vector3.down * 7f * dt;
                else b.Velocity *= Mathf.Exp(-4f * dt);
                b.T.position += b.Velocity * dt;
                b.T.rotation = camRot * Quaternion.Euler(0f, 0f, b.Spin * b.Age);
                b.T.localScale = b.Scale * fade;
            }
        }

        void AddPopup(Vector3 world, string text, Color color) => _popups.Add(new Popup { World = world, Text = text, Color = color });

        // ---------------------------------------------------------------- HUD

        void OnGUI()
        {
            float w = GameHud.Begin();
            foreach (var p in _popups)
            {
                var screen = cam.WorldToScreenPoint(p.World);
                if (screen.z <= 0f) continue;
                var g = GameHud.FromScreen(screen);
                var c = p.Color;
                c.a = Mathf.Clamp01(1.2f - p.Age);
                GameHud.Text(new Rect(g.x - 300, g.y - 35, 600, 70), p.Text, GameHud.Medium, TextAnchor.MiddleCenter, c);
            }

            if (Current is Phase.Playing or Phase.MatchEnd or Phase.Intro && !Practice) DrawScoreboard();
            if (Current != Phase.Waiting)
                GameHud.Text(new Rect(w - 830, 20, 800, 60), $"{TennisStrings.Match(Level)}    {CommonStrings.Best}: {Best}", GameHud.Small, TextAnchor.UpperRight);

            switch (Current)
            {
                case Phase.Waiting:
                    PlayerGate.DrawWaiting(w, _input, TennisStrings.Title, TennisStrings.KeyboardHelp);
                    GameHud.Text(new Rect(0, 600, w, 60), TennisStrings.HowTo, GameHud.Small);
                    GameHud.Text(new Rect(0, 650, w, 60), TennisStrings.AimHint, GameHud.Small, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));
                    GameHud.Text(new Rect(0, 700, w, 50), TennisStrings.SwapHint, GameHud.Small, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
                    break;
                case Phase.Intro:
                    GameHud.Text(new Rect(0, 470, w, 60), TennisStrings.BestOf3, GameHud.Small);
                    break;
                case Phase.Playing:
                    if (_paused) PlayerGate.DrawPaused(w, _input);
                    else if (!UsingKeys) PlayerGate.DrawEdgeWarning(w, _input);
                    break;
                case Phase.Results:
                    GameHud.Message(w, CommonStrings.GameOver,
                        $"{TennisStrings.MatchesWon}: {MatchesWon}    {TennisStrings.LongestRally}: {_longestRally}" + (_newBest ? $"    {CommonStrings.NewBest}" : ""));
                    if (_phaseTime > 1.5f)
                        GameHud.Text(new Rect(0, 540, w, 80),
                            PlayerGate.PlayAgainText(_input), GameHud.Small);
                    break;
            }

            if (_call != null && Time.time >= _callFrom && Time.time < _callUntil && Current != Phase.Waiting && Current != Phase.Results)
            {
                float age = Time.time - _callFrom;
                float pop = 1f + 0.25f * Mathf.Exp(-age * 10f);
                var style = _call.Length > 18 ? GameHud.Medium : GameHud.Big;
                var old = GUI.matrix;
                var center = new Vector2(w / 2f, 330f);
                GUIUtility.ScaleAroundPivot(Vector2.one * pop, center * (Screen.height / GameHud.Height));
                GameHud.Text(new Rect(0, 270, w, 120), _call, style, TextAnchor.MiddleCenter, Sun);
                GUI.matrix = old;
                if (_callSub != null) GameHud.Text(new Rect(0, 390, w, 70), _callSub, GameHud.Medium, TextAnchor.MiddleCenter, Color.white);
            }
            if (_hint != null && Time.time < _hintUntil && Current == Phase.Playing && !_paused)
                GameHud.Text(new Rect(0, 180, w, 70), _hint, GameHud.Medium, TextAnchor.MiddleCenter, Color.white);
        }

        /// <summary>Top left: both pairs with their games and points, a dot by the team serving.</summary>
        void DrawScoreboard()
        {
            var score = _match.Score;
            const float x = 30f, y = 24f, rowH = 62f, width = 620f;
            GameHud.Box(new Rect(x - 6, y - 6, width + 12, rowH * 2 + 12), new Color(0.05f, 0.07f, 0.18f, 0.72f));
            for (int team = 0; team < 2; team++)
            {
                float ry = y + team * rowH;
                var chip = team == 0 ? Sky : TheirShirts[_match.Level.Colour];
                GameHud.Box(new Rect(x, ry + 8, 12, rowH - 16), chip);
                if (score.Server == team && !score.MatchOver)
                {
                    GUI.color = new Color(0.85f, 0.95f, 0.3f);
                    GUI.DrawTexture(new Rect(x + 22, ry + 20, 22, 22), _dot);
                    GUI.color = Color.white;
                }
                string name = team == 0 ? TennisStrings.You : _match.Level.Name;
                GameHud.Text(new Rect(x + 54, ry, 380, rowH), name, GameHud.Small, TextAnchor.MiddleLeft);
                GameHud.Box(new Rect(x + 440, ry + 6, 70, rowH - 12), new Color(1f, 1f, 1f, 0.12f));
                GameHud.Text(new Rect(x + 440, ry, 70, rowH), score.Games(team).ToString(), GameHud.Small, TextAnchor.MiddleCenter, Sun);
                GameHud.Text(new Rect(x + 520, ry, 100, rowH), score.PointsText(team), GameHud.Small, TextAnchor.MiddleCenter);
            }
        }
    }
}
