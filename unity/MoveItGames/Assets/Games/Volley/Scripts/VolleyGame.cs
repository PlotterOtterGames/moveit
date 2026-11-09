using System;
using System.Collections.Generic;
using MoveIt.Games.Sports;
using MoveIt.Games.Tennis;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Joint = MoveIt.Motion.Joint;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Volley
{
    /// <summary>
    /// "Volejboll!": beach volleyball for one or two players, aligned with Tenis! (2026-11-07, user: "take the tennis game
    /// changes, align the volley game to match: the difficulty modes, effects, vs and together modes with 2 people"; it
    /// replaced Sporte!'s volleyball, whose moves were timed to a ring). The rules are <see cref="VolleyMatch"/> (no
    /// scene); this draws them on Sporte!'s beach on the Albanian coast (<see cref="Venue"/>, a children's net for the
    /// chibis), with four chibis (<see cref="VolleyPlayerView"/>, <see cref="VolleyLooks"/>), the ball with its trail,
    /// glow and shadow, sand and stars (<see cref="VolleyFx"/>), the crowd, calls, the scoreboard, sounds.
    /// <list type="bullet">
    /// <item><b>The moves</b> (<see cref="VolleyMoves"/>, one reader per player, on the camera's own samples): both hands
    /// together low in front receive a ball from the other side (the defend move, made after they hit it: hands held
    /// together all along get "Herët!"), both hands up set it for the teammate,
    /// one hand hits it over as fast as the hand went. The toys run to the ball by themselves; an icon over the toy the
    /// ball comes to shows the move it wants.</item>
    /// <item><b>The menu</b> (<see cref="VolleyMenu"/>, the remote's arrows, all remembered): one player, two against each
    /// other or two together; one match or a tournament (<see cref="VolleyTournament"/>, the bracket between matches, the
    /// cup at the end, counted per difficulty); easy, medium or hard; behind the team or from above.</item>
    /// <item><b>Two players</b> (<see cref="PlayerSlots"/>: player 1 on the left, a side of the room each during a match):
    /// <b>together</b> a toy each (player 1's covers the left half): one receives, the other sets it up, the first hits it
    /// over; <b>versus</b> each plays their whole team, the screen split down the middle, each half from behind its team.
    /// The box's pose AI shares its runs between the two (each seen ~15 times a second), so the rules give two players a
    /// slower ball and longer late windows (<see cref="VolleyMatch.TwoPlayerPace"/>).</item>
    /// <item>Keys or the remote without a camera (one player): ↓ = receive, ↑ = set, space / Enter / OK = hit.</item>
    /// </list>
    /// </summary>
    public sealed class VolleyGame : MonoBehaviour
    {
        public enum Phase { Waiting, Intro, Playing, MatchEnd, Bracket, Results }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] Light sun;
        [SerializeField, Tooltip("Tundu/Toon with vertex colours and gloss: the ball, the cup.")] Material actorMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: sparks, confetti.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the soft dot: sand.")] Material dotMaterial;
        [SerializeField, Tooltip("Tundu/Fx additive: the ball's trail.")] Material trailMaterial;
        [SerializeField, Tooltip("Tundu/Fx, alpha: the sun's shadows (the atlas is drawn in code).")] Material shadowMaterial;
        [SerializeField, Tooltip("Tundu/Fx additive with the soft dot: the ball's glow.")] Material glowMaterial;
        [SerializeField, Tooltip("Tundu/Sky: the beach's daylight colours it.")] Material skyMaterial;
        [SerializeField, Tooltip("Tundu/Water: the sea.")] Material waterMaterial;
        [Header("Venue")]
        [SerializeField] SportsArt art = new SportsArt();
        [Header("Match")]
        [SerializeField, Tooltip("Points to win a match (two clear, at most this + 3).")] int pointsToWin = 7;
        [SerializeField] float introSeconds = 3.2f, matchEndSeconds = 3.5f;
        [SerializeField, Tooltip("The ball is drawn this big (m across): a real one (0.21 m) is lost on a TV, and the chibis' heads are 0.74 m wide.")] float ballSize = 0.45f;

        /// <summary>A camera view: where the camera sits (behind the team's baseline, m), how it looks down (degrees), its
        /// vertical field of view; one picture and a half of the split screen.</summary>
        [Serializable]
        public struct CameraView
        {
            public float Height, Behind, Pitch, Fov, SplitHeight, SplitBehind, SplitPitch, SplitFov;
        }

        /// <summary>Behind the team (the near toys big, the whole court and the ball's arcs in the picture), or from
        /// above.</summary>
        public static readonly CameraView[] CameraViews =
        {
            new CameraView { Height = 4.8f, Behind = 7f, Pitch = 17f, Fov = 46f, SplitHeight = 5.4f, SplitBehind = 6.4f, SplitPitch = 20f, SplitFov = 56f },
            new CameraView { Height = 12.5f, Behind = 8f, Pitch = 42f, Fov = 48f, SplitHeight = 12.5f, SplitBehind = 7f, SplitPitch = 44f, SplitFov = 58f },
        };

        const string CupsKey = "volley.cups";
        /// <summary>The two players' colours (their toys wear them): player 1 red, player 2 sky blue.</summary>
        public static readonly Color[] PlayerColors = { new Color(0.91f, 0.25f, 0.23f), new Color(0.19f, 0.78f, 0.96f) };
        public const float JoinWait = 4f;
        static readonly Color Sun = SportHud.Sun, Coral = SportHud.Coral;

        public Phase Current { get; private set; }
        public VolleyMatch Match => _match;
        public VolleyMode Mode { get; private set; } = VolleyMode.Solo;
        public VolleyFormat Format { get; private set; }
        public VolleyDifficulty Difficulty { get; private set; }
        public VolleyTournament Tournament => Format == VolleyFormat.Tournament ? _tournament : null;
        public VolleyMenu Menu => _menu;
        public VolleyMode NextMode
        {
            get => _menu.Mode;
            set => _menu.Mode = value;
        }
        public PlayerSlots Slots => _slots;
        /// <summary>Practice (tests): no score, the computer returns everything and times the player's balls to
        /// <see cref="PracticeContact"/>.</summary>
        public bool Practice { get; set; }
        public Func<int, double, double> PracticeContact { get; set; }
        /// <summary>Practice: what the computer's n-th attack is (tests: a smash to block, a free ball to receive).</summary>
        public Func<int, Attack> PracticeAttack { get; set; }
        public int FirstServer { get; set; }
        public event Action<VolleyEvent> MatchEvents;
        /// <summary>Every move the camera saw while playing, with the player and its match time (tests).</summary>
        public event Action<int, MoveEvent, double> MovesSeen;
        public int GlitchesIgnored => _humans[0].Moves.Ignored + _humans[1].Moves.Ignored;
        public IReadOnlyList<VolleyPlayerView> Views => _views;
        public Camera SplitCamera => _cam2 != null && _cam2.enabled ? _cam2 : null;

        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        VolleyMatch _match;
        Venue _venue;
        readonly VolleyPlayerView[] _views = new VolleyPlayerView[4];
        readonly string[] _viewLooks = new string[4];
        readonly VolleyMenu _menu = new VolleyMenu();
        VolleyTournament _tournament;
        float _phaseTime;
        bool _paused, _newCup;
        int _longestRally;
        readonly PlayerSlots _slots = new PlayerSlots { Area = TwoArea };
        float _readySince = -1f;
        int _serves;

        /// <summary>One player's input: their moves and their hints.</summary>
        sealed class Human
        {
            public readonly VolleyMoves Moves = new VolleyMoves();
            public readonly List<MoveEvent> Seen = new List<MoveEvent>();
            public double LastSample;
            public Body Body;
            public int HintedReceive, HintedSet, HintedHit, HintedBlock, HintedTip;
            public string Hint;
            public float HintUntil;

            public void Reset()
            {
                Moves.Reset();
                HintedReceive = HintedSet = HintedHit = HintedBlock = HintedTip = 0;
                Hint = null;
            }
        }
        readonly Human[] _humans = { new Human(), new Human() };

        Transform _ball, _trophy;
        TrailRenderer _ballTrail;
        Mesh _cupMesh, _netMesh;
        float _confettiAt;
        VolleyFx _fx;
        Texture2D _shadowAtlas, _dot, _cup;
        readonly Texture2D[] _icons = new Texture2D[4];
        Material _shadows;
        Camera _cam2;
        readonly float[] _cameraX = new float[2];
        float _shake, _glowStrength;
        Color _glowColour = Color.white;
        readonly bool[] _wasInAir = new bool[4];
        readonly Vector2[] _lastRun = new Vector2[4];
        // keys without a camera: the pose a key holds (until when)
        Move _keyPose;
        float _keyPoseUntil;

        string _call, _callSub;
        float _callUntil, _callFrom;
        readonly List<Popup> _popups = new List<Popup>();

        sealed class Popup { public Vector3 World; public string Text; public Color Color; public float Age; }

        /// <summary>The moves need the knees in the picture (the bump is low), room for an arm swung out and above the head
        /// for the hands up.</summary>
        static readonly PlayArea Area = new PlayArea(BodyPart.Knees, side: 0.3f, above: 0.3f);
        static readonly PlayArea TwoArea = new PlayArea(BodyPart.Knees, side: 0.3f, above: 0.3f);

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            PlayerGate.Use(_input, Area);
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null) _mirrorWasVisible = _mirror.Visible;
            _menu.Load();
            SportsArt.Register(art);

            // the beach and its daylight (no real-time shadows: VolleyFx draws the sun's as soft shapes on the sand)
            var kit = new ActivityKit { Cam = cam, Sun = sun, Sky = skyMaterial, Water = waterMaterial };
            Daylight.Beach.Apply(kit);
            if (sun != null) sun.shadows = LightShadows.None;
            var world = new GameObject("Beach").transform;
            _venue = Venue.Load(Venue.Kind.Beach, world, seed: 5, occupancy: 0.9f, water: waterMaterial);
            BuildNet(world);

            _dot = TennisArt.Dot();
            _cup = TennisArt.Cup();
            _shadowAtlas = TennisArt.ShadowAtlas();
            _shadows = new Material(shadowMaterial) { name = "Shadows (atlas)" };
            _shadows.SetTexture("_BaseMap", _shadowAtlas);
            for (int i = 1; i < 4; i++) _icons[i] = VolleyArt.MoveIcon((Move)i);
            _fx = VolleyFx.Create(_shadows, sparkMaterial, dotMaterial, glowMaterial, sun != null ? sun.transform.forward : new Vector3(-0.3f, -0.77f, 0.56f));
            BuildBall();
            _cupMesh = TennisArt.CupMesh();
            _trophy = Shape("Trophy", _cupMesh, actorMaterial);
            _trophy.gameObject.SetActive(false);
            Difficulty = _menu.Difficulty;
            NewMatch(VolleyLevel.For(_menu.Difficulty, 1, 0));
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("volley");
        }

        void OnDestroy()
        {
            foreach (var t in new[] { _dot, _cup, _shadowAtlas, _icons[1], _icons[2], _icons[3] })
                if (t != null) Destroy(t);
            if (_cupMesh != null) Destroy(_cupMesh);
            if (_netMesh != null) Destroy(_netMesh);
            if (_shadows != null) Destroy(_shadows);
            if (waterMaterial != null) waterMaterial.SetFloat("_FlowOffset", 0f);
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
        }

        /// <summary>The net's mesh between the posts, under the venue's tapes (Tundu/Net draws its cords).</summary>
        void BuildNet(Transform parent)
        {
            if (art.net == null) return;
            _netMesh = VolleyArt.NetMesh(4.82f, VolleyMatch.NetHeight - 0.78f, VolleyMatch.NetHeight - 0.04f, 0.1f);
            var go = new GameObject("Net");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = _netMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = art.net;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        void BuildBall()
        {
            _ball = SportsBalls.Create("Ball", SportsBalls.Volleyball, actorMaterial, null, null, Sun);
            _ball.localScale = Vector3.one * (ballSize / 0.21f);
            _ball.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            _ballTrail = _ball.gameObject.AddComponent<TrailRenderer>();
            _ballTrail.sharedMaterial = trailMaterial;
            _ballTrail.time = 0.2f;
            _ballTrail.minVertexDistance = 0.05f;
            _ballTrail.widthCurve = new AnimationCurve(new Keyframe(0f, ballSize * 0.85f), new Keyframe(1f, 0f));
            _ballTrail.shadowCastingMode = ShadowCastingMode.Off;
            _ballTrail.emitting = false;
            SetTrail(Color.white, 0.4f);
        }

        Transform Shape(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
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

        static string CupKey(VolleyMode mode, VolleyDifficulty d) => $"{CupsKey}{(mode == VolleyMode.Together ? "2" : "")}.{(int)d}";

        int[] Cups(VolleyMode mode) => new[]
        {
            PlayerPrefs.GetInt(CupKey(mode, VolleyDifficulty.Easy), 0), PlayerPrefs.GetInt(CupKey(mode, VolleyDifficulty.Medium), 0),
            PlayerPrefs.GetInt(CupKey(mode, VolleyDifficulty.Hard), 0),
        };

        /// <summary>A fresh match against <paramref name="level"/>'s pair (or between the two players): the toys dressed
        /// for it (the players' stay as long as the mode does, the computer's change with the pair).</summary>
        void NewMatch(VolleyLevel level)
        {
            if (_match != null) _match.Events -= OnMatchEvent;
            _match = new VolleyMatch(level, pointsToWin, Random.Range(0, 1 << 30), FirstServer, Mode)
            {
                Practice = Practice && Mode == VolleyMode.Solo, PracticeContact = PracticeContact, PracticeAttack = PracticeAttack,
            };
            _match.Events += OnMatchEvent;
            _match.Begin();
            var pair = VolleyLooks.Pair(level.Opponent);
            ChibiLook[] looks = Mode switch
            {
                VolleyMode.Versus => new[] { VolleyLooks.Player(), VolleyLooks.Partner(), VolleyLooks.Player2(), VolleyLooks.Partner2() },
                VolleyMode.Together => new[] { VolleyLooks.Player(), VolleyLooks.Player2(), pair.a, pair.b },
                _ => new[] { VolleyLooks.Player(), VolleyLooks.Partner(), pair.a, pair.b },
            };
            for (int i = 0; i < 4; i++)
            {
                string key = looks[i].Name + (i >= 2 && Mode != VolleyMode.Versus ? $"#{level.Opponent}" : "");
                if (_views[i] != null && _viewLooks[i] == key) continue;
                if (_views[i] != null) Destroy(_views[i].gameObject);
                _views[i] = VolleyPlayerView.Create(i < 2 ? $"Team 0 toy {i}" : $"Team 1 toy {i - 2}", looks[i]);
                _viewLooks[i] = key;
            }
            ApplyDetail();
            foreach (var h in _humans) h.Moves.Reset();
            Debug.Log($"[Volley] {(Tournament != null ? VolleyStrings.RoundName(_tournament.Round) : "Match")} {Mode} {Difficulty} " +
                      $"(strength {level.Strength:0.0}){(Mode == VolleyMode.Versus ? "" : $" vs {level.TeamName}")}{(Practice ? " (practice)" : "")}");
        }

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            if (phase is Phase.Waiting or Phase.Results or Phase.Bracket) _slots.ClearReady();
            _readySince = -1f;
            ApplyViewports();
            if (phase != Phase.Results) Debug.Log($"[Volley] {phase}");
            else
            {
                var t = Tournament;
                string how = t != null ? (t.Won ? "won the tournament" : $"out in {VolleyStrings.RoundName(t.Round)}")
                    : Mode == VolleyMode.Versus ? $"player {_match.Score.Winner + 1} won" : _match.Score.Winner == 0 ? "won" : "lost";
                Debug.Log($"[Volley] Results {Mode} {Format} {Difficulty}: {how}, longest rally {_longestRally}");
            }
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            var tracker = MotionSystem.Instance != null ? MotionSystem.Instance.Tracker : null;
            _slots.Locked = Current is Phase.Intro or Phase.Playing or Phase.MatchEnd or Phase.Bracket && Mode != VolleyMode.Solo;
            if (tracker != null) _slots.Update(tracker.Bodies);
            AskForPeople();
            HandleKeys();
            ReadBodies();

            switch (Current)
            {
                case Phase.Waiting:
                    if (HandleMenu()) break;
                    if (ReadyToGo(_menu.Mode, true, out bool alone)) StartRun(alone ? VolleyMode.Solo : _menu.Mode);
                    break;
                case Phase.Intro:
                    if (_phaseTime >= introSeconds)
                    {
                        GameAudio.Sfx("go", vary: 0f);
                        SetPhase(Phase.Playing);
                    }
                    break;
                case Phase.Playing:
                    _paused = Mode == VolleyMode.Solo ? !UsingKeys && PlayerGate.ShouldPause(_input) : TwoPlayersPaused();
                    if (!_paused) _match.Step(dt);
                    if (_match.Phase == VolleyPhase.Over) EndMatch();
                    break;
                case Phase.MatchEnd:
                    _match.Step(dt);
                    if (_phaseTime >= matchEndSeconds)
                        SetPhase(_tournament != null && Format == VolleyFormat.Tournament && !_tournament.Over ? Phase.Bracket : Phase.Results);
                    break;
                case Phase.Bracket:
                    if (_phaseTime > 1.5f && ReadyToGo(Mode, false, out _)) StartMatch(_tournament.Level);
                    break;
                case Phase.Results:
                    if (_phaseTime < 1.5f) break;
                    if (RemoteInput.Up || RemoteInput.Down || RemoteInput.Left || RemoteInput.Right)
                    {
                        SetPhase(Phase.Waiting);
                        break;
                    }
                    if (ReadyToGo(Mode, true, out bool again)) StartRun(again ? VolleyMode.Solo : Mode);
                    break;
            }

            if (_mirror != null) _mirror.Visible = _mirrorWasVisible && (Current is Phase.Waiting or Phase.Results or Phase.Bracket || _paused);
            if (waterMaterial != null) waterMaterial.SetFloat("_FlowOffset", Time.time * 0.25f);
            UpdateViews(dt);
            UpdateBall(dt);
            UpdateTrophy();
            UpdateCamera(dt);
        }

        bool UsingKeys => Mode == VolleyMode.Solo && (_input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService);

        bool TwoWaiting => Current switch
        {
            Phase.Waiting => _menu.Mode != VolleyMode.Solo,
            Phase.Results or Phase.Bracket => Mode != VolleyMode.Solo,
            _ => false,
        };

        /// <summary>The box's pose AI: two people while two play or a two-player game is chosen (taking turns on its NPU),
        /// else one.</summary>
        void AskForPeople()
        {
            bool one = (Current == Phase.Waiting ? _menu.Mode : Mode) == VolleyMode.Solo;
            if (one)
            {
                var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
                AndroidMotionService.SetPeople(1, keep: body != null ? body.ServiceTrack : 0);
            }
            else AndroidMotionService.SetPeople(2, BoxGraphics.Current.TwoPlayerRuns);
        }

        bool HandleMenu()
        {
            bool up = RemoteInput.Up, down = RemoteInput.Down, left = RemoteInput.Left, right = RemoteInput.Right;
            if (!(up || down || left || right)) return false;
            bool camera = PlayerGate.State(_input) != GateState.NoService;
            if (_menu.Handle(up, down, left, right, camera, out bool moved))
            {
                GameAudio.Sfx("sport_volley", 0.4f);
                _readySince = -1f;
                _slots.ClearReady();
                Difficulty = _menu.Difficulty;
                ApplyViewports();
            }
            else if (moved) GameAudio.Sfx("ui_move", 0.5f);
            if (_input != null && camera) _input.PreferBody();
            return true;
        }

        bool ReadyToGo(VolleyMode mode, bool allowAlone, out bool alone)
        {
            alone = false;
            if (mode == VolleyMode.Solo)
            {
                _readySince = -1f;
                _slots.ClearReady();
                return StartPressed();
            }
            if ((_slots[0].Ready && _slots[1].Ready) || (RemoteInput.Confirm && _slots.BothCanPlay)) return true;
            int ready = _slots[0].Ready ? 0 : _slots[1].Ready ? 1 : -1;
            if (!allowAlone || ready < 0 || _slots.Present >= 2)
            {
                _readySince = -1f;
                return false;
            }
            if (_readySince < 0f) _readySince = Time.time;
            if (Time.time - _readySince < JoinWait) return false;
            MotionSystem.Instance?.Tracker.SetActive(_slots[ready].Body);
            alone = true;
            return true;
        }

        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        /// <summary>What hands up (or OK) does on the screen showing (tests).</summary>
        public void Continue()
        {
            switch (Current)
            {
                case Phase.Waiting:
                    StartRun(_menu.Mode);
                    break;
                case Phase.Bracket:
                    StartMatch(_tournament.Level);
                    break;
                case Phase.Results:
                    StartRun(Mode);
                    break;
            }
        }

        void StartRun(VolleyMode mode)
        {
            Mode = mode;
            Format = _menu.FormatFor(mode);
            Difficulty = _menu.Difficulty;
            if (_input != null && PlayerGate.State(_input) != GateState.NoService) _input.PreferBody();
            _longestRally = 0;
            _newCup = false;
            _serves = 0;
            _readySince = -1f;
            _slots.ClearReady();
            foreach (var h in _humans) h.Reset();
            _tournament = Format == VolleyFormat.Tournament ? new VolleyTournament(Difficulty, Random.Range(0, 1 << 30), pointsToWin) : null;
            GameAudio.PlayMusic("volley");
            StartMatch(_tournament != null ? _tournament.Level
                : mode == VolleyMode.Versus ? VolleyLevel.Versus() : VolleyLevel.For(Difficulty, 1, Random.Range(0, VolleyLevel.Pairs)));
        }

        void StartMatch(VolleyLevel level)
        {
            NewMatch(level);
            string title = Mode != VolleyMode.Versus && Tournament != null ? VolleyStrings.RoundName(Tournament.Round) : VolleyStrings.Title;
            string sub = Mode switch
            {
                VolleyMode.Versus => VolleyStrings.PlayerVersusPlayer,
                VolleyMode.Together => VolleyStrings.UsVersus(level.TeamName),
                _ => VolleyStrings.Versus(level.TeamName),
            };
            ShowCall(title, sub, introSeconds);
            GameAudio.Sfx("sport_whistle", 0.8f, vary: 0f);
            if (Mode == VolleyMode.Solo)
            {
                if (_input != null && _input.Gestures != null) _input.Gestures.Recenter();
            }
            else _slots.Recenter();
            SetPhase(Phase.Intro);
        }

        void EndMatch()
        {
            _longestRally = Mathf.Max(_longestRally, _match.LongestRally);
            int winner = _match.Score.Winner;
            bool won = winner == 0, two = Mode == VolleyMode.Together;
            var t = Tournament;
            if (t != null && !Practice)
            {
                t.Record(won, _match.Score.Points(0), _match.Score.Points(1));
                if (t.Won)
                {
                    string key = CupKey(Mode, Difficulty);
                    PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
                    PlayerPrefs.Save();
                    _newCup = true;
                }
            }
            GameAudio.Jingle(won || Mode == VolleyMode.Versus ? "jingle_win" : "jingle_gameover");
            if (won || Mode == VolleyMode.Versus)
            {
                _venue?.Fans?.Cheer(1f, 4f);
                _fx.Confetti(new Vector3(0f, 0f, VolleyMatch.Side(Mathf.Max(0, winner)) * 4.5f), t != null && t.Won ? 160 : 80);
            }
            string call = Mode switch
            {
                VolleyMode.Versus => VolleyStrings.Won(winner),
                VolleyMode.Together => won ? VolleyStrings.WeWon : VolleyStrings.TheyWon(_match.Level.TeamName),
                _ => won ? VolleyStrings.YouWon : VolleyStrings.TheyWon(_match.Level.TeamName),
            };
            string sub = t == null ? $"{_match.Score.Points(0)} - {_match.Score.Points(1)}"
                : t.Won ? VolleyStrings.WonTheCup(two) : won ? VolleyStrings.ThroughTo(t.Round, two) : VolleyStrings.OutIn(t.Round, two);
            ShowCall(t != null && t.Won ? VolleyStrings.Champion(two) : call, sub, matchEndSeconds);
            for (int k = 0; k < _match.Humans; k++)
            {
                var s = _match.Stats[k];
                Debug.Log($"[Volley] {Mode}: player {k + 1} blocks {s.Blocks}, tips dug {s.TipsDug}, beaten by tips {s.Beaten}, " +
                          $"smashes received low {s.TooHard}, not blocked {s.Unblocked}");
                Debug.Log($"[Volley] {Mode}: player {k + 1} bumps {s.Bumps}, sets {s.Sets}, spikes {s.Spikes} ({s.Perfect} perfect), pushes {s.Pushes}, " +
                          $"serves {s.Serves} ({s.Aces} aces), missed {s.Missed}, wrong {s.Wrong}, out of reach {s.OutOfReach}");
            }
            Debug.Log($"[Volley] Match {(Mode == VolleyMode.Versus ? $"won by player {winner + 1}" : won ? "won" : "lost")} " +
                      $"{_match.Score.Points(0)}-{_match.Score.Points(1)}, longest rally {_match.LongestRally}" +
                      (t != null ? $"; tournament: {(t.Won ? "won" : t.Out ? "out" : $"next {VolleyStrings.RoundName(t.Round)} vs {t.NameOf(t.Opponent)}")}" : ""));
            SetPhase(Phase.MatchEnd);
        }

        bool TwoPlayersPaused()
        {
            for (int k = 0; k < 2; k++)
                if (!_slots[k].Present || !_slots[k].Calibrated) return true;
            return false;
        }

        /// <summary>Keys and the remote without a camera: ↓ (or B) receive, ↑ (or V) set, space / Enter / OK hit; a pose
        /// key holds the pose a moment (a remote's press is short).</summary>
        void HandleKeys()
        {
            if (Current != Phase.Playing || _paused || !UsingKeys) return;
            var kb = Keyboard.current;
            if (RemoteInput.Down || (kb != null && kb.bKey.wasPressedThisFrame)) Key(Move.Bump);
            if (RemoteInput.Up || (kb != null && kb.vKey.wasPressedThisFrame)) Key(Move.Set);
            bool held = kb != null && (kb.downArrowKey.isPressed && _keyPose == Move.Bump || kb.upArrowKey.isPressed && _keyPose == Move.Set);
            if (_keyPose != Move.None && (Time.time < _keyPoseUntil || held)) _match.See(0, _keyPose, _match.Time);
            if (RemoteInput.Confirm)
            {
                _match.See(0, Move.Hit, _match.Time, 7f, new Vector2(0.2f, -1f).normalized);
                GameAudio.Sfx("tennis_swish", 0.4f);
            }
            _match.SawUntil(0, _match.Time);

            void Key(Move pose)
            {
                _keyPose = pose;
                _keyPoseUntil = Time.time + 0.6f;
            }
        }

        Body BodyOf(int slot) => Mode == VolleyMode.Solo
            ? (slot == 0 && MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null)
            : _slots[slot].Body;

        void ReadBodies()
        {
            bool waiting = Current is Phase.Waiting or Phase.Results;
            int players = waiting ? (_slots.Present == 2 ? 2 : 1) : _match.Humans;
            for (int k = 0; k < players; k++) ReadBody(k, waiting && _slots.Present == 2 ? _slots[k].Body : BodyOf(k));
        }

        /// <summary>
        /// A new camera sample of player <paramref name="slot"/>: the moves reader takes the wrists (raw, relative to the
        /// shoulders seen in the same frame; it filters the pose model's glitches itself), and what it saw goes to the
        /// rules with the match time the camera saw it, then how far the camera has told everything.
        /// </summary>
        void ReadBody(int slot, Body body)
        {
            var h = _humans[slot];
            if (body != h.Body)
            {
                h.Body = body;
                h.Moves.Reset();
                h.LastSample = 0;
            }
            if (body == null || !body.IsTracking || body.SampleTime == h.LastSample) return;
            h.LastSample = body.SampleTime;
            var raw = body.Raw;
            var shoulders = ((Vector2)raw[(int)Joint.LeftShoulder] + (Vector2)raw[(int)Joint.RightShoulder]) * 0.5f;
            h.Seen.Clear();
            int ignored = h.Moves.Ignored;
            h.Moves.Update(body.SampleTime, shoulders, body.FastTorsoLength, raw[(int)Joint.LeftWrist], body.Visibility[(int)Joint.LeftWrist],
                           raw[(int)Joint.RightWrist], body.Visibility[(int)Joint.RightWrist], h.Seen);
            bool playing = Current == Phase.Playing && !_paused && !UsingKeys && slot < _match.Humans;
            if (!playing) return;
            if (h.Moves.Ignored != ignored) Debug.Log($"[Volley] A hand's jump ignored (the pose model's glitch; {h.Moves.Ignored} so far)");
            double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            double age = Math.Max(0.0, Math.Min(_match.HitLag, nowUnix - body.SampleTime));
            double at = _match.Time - age;   // the match time of this sample
            foreach (var e in h.Seen)
            {
                double t = at - (body.SampleTime - e.Time);
                _match.See(slot, e.Move, t, e.Speed, e.Direction, e.RightHand);
                MovesSeen?.Invoke(slot, e, t);
                if (e.Move == Move.Hit)
                    Debug.Log($"[Volley] Hit{(Mode == VolleyMode.Solo ? "" : $" by player {slot + 1}")} {(e.RightHand ? "right" : "left")} " +
                              $"{e.Speed:0.0} torso/s dir ({e.Direction.x:0.0}, {e.Direction.y:0.0}), seen {age * 1000:0} ms ago");
            }
            if (!double.IsNegativeInfinity(h.Moves.Settled)) _match.SawUntil(slot, at - (body.SampleTime - h.Moves.Settled));
        }

        // ---------------------------------------------------------------- match events: sounds, effects, calls

        void OnMatchEvent(VolleyEvent e)
        {
            MatchEvents?.Invoke(e);
            var who = e.Player >= 0 ? _match.Players[e.Player] : null;
            int owner = who != null ? who.Controller : -1;
            switch (e.Kind)
            {
                case VolleyEventKind.Toss:
                    GameAudio.Sfx("tennis_toss", 0.5f);
                    if (owner >= 0 && (_match.TossesMissed > 0 || _serves < 2)) ShowHint(owner, VolleyStrings.ServeHint, 2.4f);
                    break;
                case VolleyEventKind.Touch:
                    OnTouch(e, who, owner);
                    break;
                case VolleyEventKind.Land:
                    float speed = _match.BallVelocity.magnitude;
                    GameAudio.Sfx("sport_thud", Mathf.Clamp(0.4f + speed * 0.04f, 0.4f, 1f));
                    _fx.Land(e.Position, e.In, speed);
                    if (!e.In) AddPopup(e.Position + Vector3.up * 0.7f, VolleyStrings.Out, Coral);
                    break;
                case VolleyEventKind.Net:
                    GameAudio.Sfx("sport_net");
                    AddPopup(e.Position + Vector3.up * 0.9f, VolleyStrings.Net, Coral);
                    break;
                case VolleyEventKind.Missed:
                    if (owner < 0) break;
                    var h = _humans[owner];
                    if (e.Attack == Attack.Smash)
                    {
                        // a smash nobody blocked
                        if (h.HintedBlock++ < 4) ShowHint(owner, VolleyStrings.BlockHint, 3f);
                    }
                    else if (e.Number == 1 && h.HintedReceive++ < 3) ShowHint(owner, VolleyStrings.ReceiveHint, 2.6f);
                    else if (e.Number == 2 && h.HintedSet++ < 3) ShowHint(owner, VolleyStrings.SetHint, 2.6f);
                    else if (e.Number == 3 && h.HintedHit++ < 3) ShowHint(owner, VolleyStrings.HitHint, 2.6f);
                    break;
                case VolleyEventKind.OutOfReach:
                    AddPopup(e.Position + Vector3.up * 0.8f, VolleyStrings.OutOfReach, Coral);
                    break;
                case VolleyEventKind.Defence:
                    OnDefence(e);
                    break;
                case VolleyEventKind.Dive:
                    if (who != null) _fx.Kick(who.World, Vector3.zero, 1.5f);
                    break;
                case VolleyEventKind.Point:
                    OnPoint(e);
                    break;
            }
        }

        void OnTouch(VolleyEvent e, VolleyPlayer who, int owner)
        {
            var view = who != null ? _views[who.Index] : null;
            bool spike = e.Touch == Touch.Spike, human = owner >= 0;
            switch (e.Touch)
            {
                case Touch.Bump:
                    GameAudio.Sfx("sport_bump", 0.85f);
                    break;
                case Touch.Set:
                    GameAudio.Sfx("sport_volley", 0.6f, 1.15f);
                    break;
                case Touch.Spike:
                    GameAudio.Sfx("tennis_power", 0.7f + 0.3f * e.Power);
                    GameAudio.Sfx("sport_volley", 0.9f, 0.9f);
                    break;
                case Touch.Shank:
                    GameAudio.Sfx("sport_miss", 0.8f);
                    break;
                case Touch.Block:
                    GameAudio.Sfx("sword_block", 0.9f);
                    GameAudio.Sfx("sport_volley", 1f, 0.8f);
                    break;
                default:
                    GameAudio.Sfx("sport_volley", 0.75f);
                    break;
            }
            if (e.Touch == Touch.Block)
            {
                _fx.Touch(e.Position, true, Sun);
                _shake = 0.3f;
                _venue?.Fans?.Cheer(0.8f, 1.2f);
                if (view != null) view.Flash(Sun, 0.5f);
                AddPopup(e.Position + Vector3.up * 0.7f, VolleyStrings.Blocked, Sun);
                Debug.Log($"[Volley] Block by {(human ? (Mode == VolleyMode.Solo ? "the player" : $"player {owner + 1}") : "the computer")} " +
                          $"at ({e.Position.x:0.0}, {e.Position.y:0.0}, {e.Position.z:0.0})");
                return;
            }
            if (e.Touch == Touch.Serve && human) _serves++;
            var colour = spike ? (e.Perfect ? new Color(1f, 0.55f, 0.2f) : Sun) : Color.white;
            _fx.Touch(e.Position, spike && e.Power > 0.45f, colour);
            SetTrail(spike ? new Color(1f, 0.55f, 0.2f) : Color.white, spike ? 0.8f : 0.35f);
            _glowColour = spike ? new Color(1f, 0.6f, 0.25f) : new Color(1f, 1f, 0.85f);
            _glowStrength = spike ? 0.6f + 0.4f * e.Power : 0.35f;
            if (spike && e.Perfect) _shake = 0.25f;
            if (view != null && human) view.Flash(e.Perfect ? Sun : Color.white, e.Perfect ? 0.4f : 0.2f);
            if (human)
            {
                bool tooHard = e.Touch == Touch.Shank && e.Attack == Attack.Smash;
                string text = e.Touch switch
                {
                    Touch.Spike => e.Perfect ? VolleyStrings.SuperSpike : e.Power > 0.6f ? VolleyStrings.Spike : null,
                    Touch.Shank => tooHard ? VolleyStrings.TooHard : VolleyStrings.Wrong,
                    Touch.Bump when e.Late => VolleyStrings.Save,
                    _ => null,
                };
                if (text != null) AddPopup(e.Position + Vector3.up * 0.8f, text, e.Touch == Touch.Shank ? Coral : Sun);
                // a smash received low flew off the arms: it had to be blocked
                if (tooHard && _humans[owner].HintedBlock++ < 4) ShowHint(owner, VolleyStrings.BlockHint, 3f);
                else if (e.Touch == Touch.Shank && _humans[owner].HintedReceive++ < 3) ShowHint(owner, VolleyStrings.ReceiveHint, 2.6f);
            }
            Debug.Log($"[Volley] {e.Touch} #{e.Number} by {(human ? (Mode == VolleyMode.Solo ? "the player" : $"player {owner + 1}") : "the computer")}" +
                      $"{(e.Late ? " (late)" : "")} power {e.Power:0.00}{(e.Perfect ? " perfect" : "")} at ({e.Position.x:0.0}, {e.Position.y:0.0}, {e.Position.z:0.0}), " +
                      $"pace {_match.Pace:0.00}");
        }

        /// <summary>The other side hit a jumping attack at a player: a tip says so over the hitter; a tip into a block goes
        /// over it (the hint: a tip is received low); a smash into a block comes as the block's own touch.</summary>
        void OnDefence(VolleyEvent e)
        {
            var blocker = e.Player >= 0 ? _match.Players[e.Player] : null;
            int owner = blocker != null ? blocker.Controller : -1;
            if (e.Attack == Attack.Tip) AddPopup(e.Position + Vector3.up * 0.6f, VolleyStrings.TipCall, Color.white);
            if (e.Attack == Attack.Tip && e.Choice == Defence.Block && owner >= 0)
            {
                AddPopup(new Vector3(blocker.Position.x, VolleyMatch.BlockHeight + 0.5f, blocker.Position.y), VolleyStrings.OverTheBlock, Coral);
                if (_humans[owner].HintedTip++ < 4) ShowHint(owner, VolleyStrings.TipHint, 3f);
            }
            Debug.Log($"[Volley] Defence{(Mode == VolleyMode.Solo ? "" : $" of player {owner + 1}")}: {e.Attack} met with {e.Choice} " +
                      $"({(e.Correct ? "right" : "wrong")}), block rate {_match.BlockRate:0.00}");
        }

        string TeamName(int team) => Mode switch
        {
            VolleyMode.Versus => VolleyStrings.Player(team),
            VolleyMode.Together => team == 0 ? VolleyStrings.Us : _match.Level.TeamName,
            _ => team == 0 ? VolleyStrings.You : _match.Level.TeamName,
        };

        void OnPoint(VolleyEvent e)
        {
            bool ours = e.Team == 0 || Mode == VolleyMode.Versus;
            _longestRally = Mathf.Max(_longestRally, _match.LongestRally);
            _venue?.Fans?.Cheer(ours ? 0.9f : 0.4f, 2.4f);
            GameAudio.Sfx(ours ? "tennis_cheer" : "tennis_aww", 0.75f);
            GameAudio.Sfx("sport_whistle", 0.45f, vary: 0f);
            if (Practice) return;
            var score = _match.Score;
            string sub = score.MatchPoint(0) || score.MatchPoint(1) ? VolleyStrings.MatchPoint : null;
            if (e.Team == _match.Server.Team && _match.IsHuman(e.Team) && _match.Crossings == 1 && e.Reason == PointReason.Landed && _match.LastTouch == Touch.Serve)
                AddPopup(_match.BallPosition + Vector3.up * 0.8f, VolleyStrings.Ace, Sun);
            ShowCall($"{VolleyStrings.Point} {TeamName(e.Team)}", sub ?? $"{score.Points(0)} - {score.Points(1)}", 1.7f, 0.4f);
            Debug.Log($"[Volley] Point to {TeamName(e.Team)} ({e.Reason}): {score.Points(0)}-{score.Points(1)}, rally {_match.Crossings}");
        }

        void ShowCall(string call, string sub, float seconds, float delay = 0f)
        {
            _call = call;
            _callSub = sub;
            _callUntil = Time.time + delay + seconds;
            _callFrom = Time.time + delay;
        }

        void ShowHint(int slot, string text, float seconds)
        {
            var h = _humans[Mathf.Clamp(slot, 0, 1)];
            h.Hint = text;
            h.HintUntil = Time.time + seconds;
        }

        void AddPopup(Vector3 world, string text, Color color) => _popups.Add(new Popup { World = world, Text = text, Color = color });

        // ---------------------------------------------------------------- drawing the match

        void UpdateViews(float dt)
        {
            double now = _match.Time;
            float step = Current == Phase.Playing && _paused ? 0f : dt;
            bool rally = _match.Phase is VolleyPhase.Rally or VolleyPhase.Toss or VolleyPhase.Serve && Current is Phase.Playing or Phase.Intro;
            var ball = _match.BallPosition;
            for (int i = 0; i < 4; i++)
            {
                var v = _views[i];
                if (v == null) continue;
                var p = _match.Players[i];
                v.LookAt = ball;
                v.Animate(p, now, step, rally);
                // sand kicked up when a toy sets off, pulls up or lands from a jump
                if (step > 0f && Current is Phase.Playing)
                {
                    float before = _lastRun[i].magnitude, speed = p.Velocity.magnitude;
                    if ((speed > 4f && before <= 4f) || (before > 5f && speed < before - 1.5f))
                        _fx.Kick(v.FeetWorld, new Vector3(p.Velocity.x, 0f, p.Velocity.y) * (speed > before ? 1f : -1f), 0.6f);
                    bool air = v.JumpHeight > 0.05f;
                    if (!_wasInAir[i] && air)
                    {
                        if (p.Controller < 0 && p.Attack is Attack.Smash or Attack.Tip)
                        {
                            bool tip = p.Attack == Attack.Tip && _match.Time >= p.TellAt;
                            GameAudio.Sfx(tip ? "jump" : "whoosh", tip ? 0.3f : 0.65f);
                        }
                        else if (p.BlockAt >= 0 && Mathf.Abs((float)(_match.Time - p.BlockAt)) < 0.5f) GameAudio.Sfx("jump", 0.35f);
                    }
                    if (_wasInAir[i] && !air)
                    {
                        _fx.Kick(v.FeetWorld, Vector3.zero, 1f);
                        GameAudio.Sfx("land", 0.25f);
                    }
                    _wasInAir[i] = air;
                }
                _lastRun[i] = p.Velocity;
            }
        }

        void UpdateTrophy()
        {
            bool show = Current == Phase.Results && Tournament != null && Tournament.Won;
            if (_trophy.gameObject.activeSelf != show) _trophy.gameObject.SetActive(show);
            if (!show) return;
            var t = cam.transform;
            float pop = Mathf.SmoothStep(0f, 1f, _phaseTime / 0.6f);
            _trophy.SetPositionAndRotation(t.position + t.forward * 5.4f - Vector3.up * (0.8f + 0.06f * Mathf.Sin(Time.time * 2f)),
                                           Quaternion.Euler(0f, Time.time * 55f, 0f));
            _trophy.localScale = Vector3.one * (1.45f * pop);
            if (Time.time >= _confettiAt)
            {
                _confettiAt = Time.time + 0.6f;
                _fx.Confetti(_trophy.position + Vector3.down * 3.2f, 14);
            }
        }

        /// <summary>The ball, its streak (warm and wide after a spike), its glow and its shadows (straight below it on the
        /// sand: where it will come down).</summary>
        void UpdateBall(float dt)
        {
            var p = _match.BallPosition;
            bool show = Current != Phase.Waiting || _match.BallLive;
            _ball.gameObject.SetActive(show);
            _fx.UpdateShadows(_views, show ? p : (Vector3?)null, ballSize);
            if (!show)
            {
                _fx.UpdateGlow(null, cam, ballSize, _glowColour, 0f);
                return;
            }
            if ((_ball.position - p).sqrMagnitude > 4f) _ballTrail.Clear();
            _ball.position = p;
            var v = _match.BallVelocity;
            if (v.sqrMagnitude > 0.5f)
                _ball.rotation = Quaternion.AngleAxis(v.magnitude * 300f * dt, Vector3.Cross(Vector3.up, v).normalized) * _ball.rotation;
            if (!_match.BallLive) _ballTrail.Clear();
            _ballTrail.emitting = _match.BallLive && v.sqrMagnitude > 16f;
            _ballTrail.widthMultiplier = Mathf.Lerp(0.7f, 1.3f, Mathf.InverseLerp(6f, 18f, v.magnitude));
            _glowStrength = Mathf.MoveTowards(_glowStrength, 0.45f, dt * 0.6f);
            _fx.UpdateGlow(_match.BallLive ? p : (Vector3?)null, cam, ballSize, _glowColour, _glowStrength);
        }

        /// <summary>The far team always in low detail (small on the screen), all four in the split screen (each drawn
        /// twice); PerfLab's chibi=full / chibi=low draws all four one way.</summary>
        void ApplyDetail()
        {
            bool split = Split;
            string force = PerfSwitches.Get("chibi");
            for (int i = 0; i < _views.Length; i++)
                if (_views[i] != null) _views[i].Low = force == "full" ? false : force == "low" || split || i >= 2;
        }

        bool Split => Mode == VolleyMode.Versus && Current != Phase.Waiting || PerfSwitches.Get("split") == "1";

        /// <summary>Versus (once a match is on): the screen split, player 1's half on the left, each from behind its team.</summary>
        void ApplyViewports()
        {
            bool split = Split;
            BoxGraphics.SetSplit(split);
            ApplyDetail();
            cam.rect = split ? new Rect(0f, 0f, 0.5f, 1f) : new Rect(0f, 0f, 1f, 1f);
            if (split && _cam2 == null)
            {
                _cam2 = new GameObject("Camera (player 2)").AddComponent<Camera>();
                _cam2.CopyFrom(cam);
                var from = cam.GetUniversalAdditionalCameraData();
                var to = _cam2.GetUniversalAdditionalCameraData();
                to.renderPostProcessing = from.renderPostProcessing;
                to.renderShadows = from.renderShadows;
                to.antialiasing = from.antialiasing;
                var listener = _cam2.GetComponent<AudioListener>();
                if (listener != null) Destroy(listener);
            }
            if (_cam2 != null)
            {
                _cam2.enabled = split;
                _cam2.rect = new Rect(0.5f, 0f, 0.5f, 1f);
            }
        }

        float _view = -1f;

        CameraView ViewNow(float dt)
        {
            float wanted = Mathf.Clamp(_menu.View, 0, CameraViews.Length - 1);
            _view = _view < 0f ? wanted : Mathf.MoveTowards(_view, wanted, dt / 0.5f);
            int a = Mathf.FloorToInt(Mathf.Min(_view, CameraViews.Length - 1.001f));
            float k = Mathf.SmoothStep(0f, 1f, _view - a);
            CameraView v0 = CameraViews[a], v1 = CameraViews[Mathf.Min(a + 1, CameraViews.Length - 1)];
            return new CameraView
            {
                Height = Mathf.Lerp(v0.Height, v1.Height, k), Behind = Mathf.Lerp(v0.Behind, v1.Behind, k),
                Pitch = Mathf.Lerp(v0.Pitch, v1.Pitch, k), Fov = Mathf.Lerp(v0.Fov, v1.Fov, k),
                SplitHeight = Mathf.Lerp(v0.SplitHeight, v1.SplitHeight, k), SplitBehind = Mathf.Lerp(v0.SplitBehind, v1.SplitBehind, k),
                SplitPitch = Mathf.Lerp(v0.SplitPitch, v1.SplitPitch, k), SplitFov = Mathf.Lerp(v0.SplitFov, v1.SplitFov, k),
            };
        }

        /// <summary>The camera high behind the team's baseline looking over the net, drifting a little sideways after the
        /// ball and the team's toys.</summary>
        void UpdateCamera(float dt)
        {
            float ease = 1f - Mathf.Exp(-2f * dt);
            _shake = Mathf.Max(0f, _shake - dt);
            var shake = (Vector3)(Random.insideUnitCircle * (0.12f * _shake));
            bool split = Split;
            if (split != (_cam2 != null && _cam2.enabled)) ApplyViewports();
            var view = ViewNow(dt);
            var ball = _match.BallPosition;
            for (int team = 0; team < (split ? 2 : 1); team++)
            {
                float mid = (_match.Players[team * 2].Position.x + _match.Players[team * 2 + 1].Position.x) * 0.5f;
                float wantX = Mathf.Clamp((ball.x * 0.3f + mid * 0.3f) * VolleyMatch.RightSign(team), -2.4f, 2.4f);
                _cameraX[team] = Mathf.Lerp(_cameraX[team], wantX, ease);
                float height = split ? view.SplitHeight : view.Height, behind = split ? view.SplitBehind : view.Behind;
                var home = new Vector3(0f, height, VolleyMatch.Side(team) * (VolleyMatch.HalfLength + behind));
                var rotation = Quaternion.Euler(split ? view.SplitPitch : view.Pitch, team == 0 ? 0f : 180f, 0f);
                var right = new Vector3(VolleyMatch.RightSign(team), 0f, 0f);
                var c = team == 0 ? cam : _cam2;
                c.fieldOfView = split ? view.SplitFov : view.Fov;
                c.transform.SetPositionAndRotation(home + right * _cameraX[team] + shake, rotation);
            }
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].Age += dt;
                _popups[i].World += Vector3.up * (0.7f * dt);
                if (_popups[i].Age > 1.2f) _popups.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- HUD

        void OnGUI()
        {
            float w = GameHud.Begin();
            foreach (var p in _popups)
                foreach (var c in Cameras())
                {
                    var screen = c.WorldToScreenPoint(p.World);
                    if (screen.z <= 0f || !c.pixelRect.Contains(screen)) continue;
                    var g = GameHud.FromScreen(screen);
                    var color = p.Color;
                    color.a = Mathf.Clamp01(1.2f - p.Age);
                    GameHud.Text(new Rect(g.x - 300, g.y - 35, 600, 70), p.Text, GameHud.Medium, TextAnchor.MiddleCenter, color);
                }

            bool split = Split;
            if (split) GameHud.Box(new Rect(w / 2f - 3f, 0f, 6f, GameHud.Height), new Color(0.05f, 0.08f, 0.06f, 0.9f));
            if (Current is Phase.Playing or Phase.MatchEnd or Phase.Intro && !Practice)
            {
                DrawScoreboard(w);
                if (Mode != VolleyMode.Versus) DrawMatchInfo(w);
            }

            switch (Current)
            {
                case Phase.Waiting:
                    DrawWaiting(w);
                    break;
                case Phase.Intro:
                    Label(new Rect(0, 470, w, 60), VolleyStrings.FirstTo(pointsToWin), 34, Color.white);
                    break;
                case Phase.Playing:
                    if (_paused)
                    {
                        if (Mode == VolleyMode.Solo) PlayerGate.DrawPaused(w, _input);
                        else DrawTwoPaused(w);
                    }
                    else
                    {
                        DrawMoveIcon(w);
                        if (Mode == VolleyMode.Solo && !UsingKeys) PlayerGate.DrawEdgeWarning(w, _input);
                    }
                    break;
                case Phase.Bracket:
                    DrawBracket(w);
                    break;
                case Phase.Results:
                    DrawResults(w);
                    break;
            }

            if (_call != null && Time.time >= _callFrom && Time.time < _callUntil && Current is Phase.Intro or Phase.Playing or Phase.MatchEnd)
            {
                if (split)
                {
                    DrawCall(w / 4f, w / 2f);
                    DrawCall(w * 3f / 4f, w / 2f);
                }
                else DrawCall(w / 2f, w);
            }
            if (Current == Phase.Playing && !_paused)
                for (int k = 0; k < _match.Humans; k++)
                {
                    var h = _humans[k];
                    if (h.Hint == null || Time.time >= h.HintUntil) continue;
                    float x0 = split ? k * w / 2f : 0f, width = split ? w / 2f : w;
                    string text = Mode == VolleyMode.Together ? $"{VolleyStrings.Player(k)}: {h.Hint}" : h.Hint;
                    float y = Mode == VolleyMode.Together ? 180f + 70f * k : 180f;
                    GameHud.Text(new Rect(x0, y, width, 70), text, GameHud.Medium, TextAnchor.MiddleCenter,
                                 Mode == VolleyMode.Solo ? Color.white : Color.Lerp(Color.white, PlayerColors[k], 0.35f));
                }
        }

        IEnumerable<Camera> Cameras()
        {
            yield return cam;
            if (_cam2 != null && _cam2.enabled) yield return _cam2;
        }

        const float MirrorLeft = 440f;

        /// <summary>
        /// The move the coming ball wants, over the head of the player's toy it comes to: a little figure (hands together
        /// low, both up, one swinging) on a dark disc and its name, in the player's colour with two players. It shows from
        /// the moment the ball is on its way until it's played.
        /// </summary>
        void DrawMoveIcon(float w)
        {
            DrawDefence(w);
            var c = _match.Next;
            if (!c.Active || c.Player == null || c.Player.Controller < 0 || !c.Reachable) return;
            // a smash coming over can't be received: it had to be blocked
            if (c.Number == 1 && c.Attack == Attack.Smash) return;
            var want = c.Number switch { 1 => Move.Bump, 2 => Move.Set, _ => Move.Hit };
            var view = _views[c.Player.Index];
            if (view == null) return;
            int slot = c.Player.Controller;
            var camera = Mode == VolleyMode.Versus && slot == 1 && _cam2 != null ? _cam2 : cam;
            var screen = camera.WorldToScreenPoint(view.HeadWorld + Vector3.up * 0.55f);
            if (screen.z <= 0f) return;
            var g = GameHud.FromScreen(screen);
            float until = (float)(c.Time - _match.Time);
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 9f) * Mathf.Clamp01(1f - until);
            float size = 86f * pulse;
            var disc = new Rect(g.x - size / 2f, g.y - size, size, size);
            var tint = Mode == VolleyMode.Solo ? Sun : PlayerColors[slot];
            SportHud.Rounded(disc, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.78f), size / 2f);
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(new Rect(disc.x + 8f, disc.y + 8f, disc.width - 16f, disc.height - 16f), _icons[(int)want]);
            GUI.color = old;
            string name = want switch { Move.Bump => VolleyStrings.Bump, Move.Set => VolleyStrings.Set, _ => VolleyStrings.Hit };
            SportHud.Text(new Rect(g.x - 120f, disc.yMax + 2f, 240f, 40f), name, 28, tint, TextAnchor.MiddleCenter, 2f);
        }

        /// <summary>
        /// An attack being set up at a player's team (from the other side's set to its hit): on Easy a "!" over a hitter that
        /// will smash; at the bottom of the screen (the player's own half of it: two together, their side; versus, their
        /// half of the split screen) the two choices side by side (both hands up: block; hands together low: receive), the
        /// one the camera sees the player make lit up (the camera's news is a little late: what they see lit is what
        /// counts). Over the blocker they hid the "!" of the hitter right behind it.
        /// </summary>
        void DrawDefence(float w)
        {
            var t = _match.Coming;
            if (!t.Active || t.Defender < 0 || t.Blocker == null) return;
            int slot = t.Defender;
            var camera = Mode == VolleyMode.Versus && slot == 1 && _cam2 != null ? _cam2 : cam;
            if (_match.Level.ShowTell && t.Kind == Attack.Smash && t.Hitter != null && _views[t.Hitter.Index] != null)
            {
                var hs = camera.WorldToScreenPoint(_views[t.Hitter.Index].HeadWorld + Vector3.up * 0.45f);
                if (hs.z > 0f)
                {
                    var g = GameHud.FromScreen(hs);
                    float size = 72f * (1f + 0.1f * Mathf.Sin(Time.time * 12f));
                    var r = new Rect(g.x - size / 2f, g.y - size, size, size);
                    SportHud.Rounded(r, Coral, size / 2f);
                    SportHud.Text(r, "!", 58, Color.white, TextAnchor.MiddleCenter, 0f);
                }
            }
            float x = Mode switch
            {
                VolleyMode.Versus => slot == 0 ? w / 4f : w * 3f / 4f,
                VolleyMode.Together => w / 2f + (slot == 0 ? -380f : 380f),
                _ => w / 2f,
            };
            var shows = _match.ShowsNow(slot);
            var tint = Mode == VolleyMode.Solo ? Sun : PlayerColors[slot];
            const float bottom = 1000f;
            DrawChoice(new Vector2(x - 75f, bottom), _icons[(int)Move.Set], VolleyStrings.Block, shows == Defence.Block, tint);
            DrawChoice(new Vector2(x + 75f, bottom), _icons[(int)Move.Bump], VolleyStrings.Bump, shows == Defence.Receive, tint);
        }

        /// <summary>A choice's disc, its bottom at <paramref name="at"/>.y, its name under it.</summary>
        static void DrawChoice(Vector2 at, Texture2D icon, string name, bool lit, Color tint)
        {
            float size = lit ? 112f : 80f;
            var disc = new Rect(at.x - size / 2f, at.y - size, size, size);
            SportHud.Rounded(disc, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, lit ? 0.86f : 0.6f), size / 2f);
            var old = GUI.color;
            GUI.color = lit ? tint : new Color(1f, 1f, 1f, 0.55f);
            GUI.DrawTexture(new Rect(disc.x + 6f, disc.y + 6f, disc.width - 12f, disc.height - 12f), icon);
            GUI.color = old;
            SportHud.Text(new Rect(at.x - 100f, disc.yMax + 2f, 200f, 38f), name, lit ? 30 : 24, lit ? tint : new Color(1f, 1f, 1f, 0.75f),
                          TextAnchor.MiddleCenter, 2f);
        }

        void DrawCall(float centerX, float width)
        {
            float age = Time.time - _callFrom;
            float pop = 1f + 0.25f * Mathf.Exp(-age * 10f);
            var style = _call.Length > 18 || width < 1200f && _call.Length > 12 ? GameHud.Medium : GameHud.Big;
            var old = GUI.matrix;
            var center = new Vector2(centerX, 330f);
            GUIUtility.ScaleAroundPivot(Vector2.one * pop, center * (Screen.height / GameHud.Height));
            GameHud.Text(new Rect(centerX - width / 2f, 270, width, 120), _call, style, TextAnchor.MiddleCenter, Sun);
            GUI.matrix = old;
            if (_callSub != null) GameHud.Text(new Rect(centerX - width / 2f, 390, width, 70), _callSub, GameHud.Medium, TextAnchor.MiddleCenter, Color.white);
        }

        static void DrawTitle(float w, string title, float y = 34f, int size = 104) => Label(new Rect(0f, y, w, size * 1.15f), title, size, Sun);

        static void Label(Rect r, string text, int size, Color colour, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            SportHud.Text(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), text, size, new Color(0f, 0f, 0f, 0.65f * colour.a), anchor, 0f);
            SportHud.Text(r, text, size, colour, anchor, 0f);
        }

        /// <summary>The waiting screen: the title, the menu, then the three moves (a figure each), and what the players
        /// still have to do (one player: the camera's gate; two: a card each).</summary>
        void DrawWaiting(float w)
        {
            bool camera = PlayerGate.State(_input) != GateState.NoService;
            DrawTitle(w, VolleyStrings.Title);
            const float menuY = 168f;
            _menu.Draw(w / 2f, menuY, camera, Cups(_menu.Mode == VolleyMode.Together ? VolleyMode.Together : VolleyMode.Solo), _cup);
            float below = menuY + VolleyMenu.Height;
            Label(new Rect(0f, below + 6f, w, 44f), VolleyStrings.MenuHint, 26, new Color(1f, 1f, 1f, 0.75f));
            DrawMoves(w, below + 58f);
            if (TwoWaiting)
            {
                string name = _menu.Mode == VolleyMode.Together ? VolleyStrings.TogetherModeSub : VolleyStrings.VersusModeSub;
                Label(new Rect(0f, below + 244f, w, 50f), name, 32, Color.white);
                DrawPlayerCards(w, 790f);
                return;
            }
            var old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.Translate(new Vector3(0f, below - 64f, 0f));
            PlayerGate.DrawWaiting(w, _input, "", VolleyStrings.KeyboardHelp);
            GUI.matrix = old;
        }

        /// <summary>The four moves in a row (receive, set, hit, block: a figure and what it does), and under them how to
        /// defend.</summary>
        void DrawMoves(float w, float y)
        {
            Label(new Rect(0f, y, w, 40f), VolleyStrings.HowTo, 28, Color.white);
            string[] texts = { VolleyStrings.HowBump, VolleyStrings.HowSet, VolleyStrings.HowHit, VolleyStrings.HowBlock };
            Move[] icons = { Move.Bump, Move.Set, Move.Hit, Move.Set };
            const float card = 440f, gap = 18f;
            float x0 = w / 2f - (card * 4f + gap * 3f) / 2f;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(x0 + i * (card + gap), y + 48f, card, 96f);
                GameHud.Box(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.8f));
                var old = GUI.color;
                GUI.color = i == 3 ? Coral : Sun;
                GUI.DrawTexture(new Rect(r.x + 8f, r.y + 6f, 84f, 84f), _icons[(int)icons[i]]);
                GUI.color = old;
                GUI.Label(new Rect(r.x + 96f, r.y + 2f, r.width - 106f, r.height - 4f), texts[i], CardStyle);
            }
            Label(new Rect(0f, y + 150f, w, 40f), VolleyStrings.HowDefend, 26, Sun);
        }

        GUIStyle _cardStyle;

        /// <summary>The moves' cards' text: white, wrapping onto a second line.</summary>
        GUIStyle CardStyle => _cardStyle ??= new GUIStyle(SportHud.Style(24))
        {
            wordWrap = true, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white }, clipping = TextClipping.Clip,
        };

        void DrawPlayerCards(float w, float y)
        {
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                var r = new Rect(k == 0 ? 70f : w - 630f, y, 560f, 150f);
                SportHud.Panel(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.84f), 22f, null, 0f, false);
                SportHud.Rounded(new Rect(r.x + 14f, r.y + 18f, 10f, r.height - 36f), PlayerColors[k], 5f);
                SportHud.Text(new Rect(r.x + 44f, r.y + 10f, r.width - 60f, 64f), VolleyStrings.Player(k), 40, PlayerColors[k], TextAnchor.MiddleLeft, 0f);
                string status;
                Color color = Color.white;
                if (!s.Present) status = CommonStrings.StepIn;
                else if (s.Placement.Blocks)
                {
                    status = s.Placement.Issue switch
                    {
                        PlacementIssue.MoveLeft => CommonStrings.MoveLeft,
                        PlacementIssue.MoveRight => CommonStrings.MoveRight,
                        _ => CommonStrings.StepBack,
                    };
                    color = Brand.Current.SecondaryColor;
                }
                else if (!s.Calibrated) status = $"{VolleyStrings.StandStill} {s.CalibrationProgress * 100f:0}%";
                else if (!s.Ready) status = VolleyStrings.RaiseYourHands;
                else
                {
                    status = VolleyStrings.Ready;
                    color = Sun;
                }
                SportHud.Text(new Rect(r.x + 44f, r.y + 76f, r.width - 60f, 60f), status, 32, color, TextAnchor.MiddleLeft, 0f);
            }
            var middle = new Rect(650f, y, w - 1300f, 150f);
            if (_slots.TooClose) GameHud.Banner(new Rect(middle.x, y + 40f, middle.width, 76f), VolleyStrings.StepApart, Brand.Current.SecondaryColor);
            else if (_readySince >= 0f)
            {
                int waiting = _slots[0].Ready ? 1 : 0;
                int left = Mathf.Max(1, Mathf.CeilToInt(JoinWait - (Time.time - _readySince)));
                Label(new Rect(middle.x, y + 10f, middle.width, 64f), VolleyStrings.WaitingFor(waiting), 34, PlayerColors[waiting]);
                Label(new Rect(middle.x, y + 80f, middle.width, 50f), VolleyStrings.PlayingAloneIn(left), 28, new Color(1f, 1f, 1f, 0.8f));
            }
        }

        string YouName => Mode == VolleyMode.Together ? VolleyStrings.Us : VolleyStrings.You;

        void DrawBracket(float w)
        {
            var t = _tournament;
            if (t == null) return;
            bool two = Mode == VolleyMode.Together;
            DrawTitle(w, VolleyStrings.TournamentOf(Difficulty), 28f, 72);
            VolleyBracketView.Draw(t, new Rect(170f, 130f, w - 340f, 560f), YouName, _cup, Time.time);
            if (!t.Over) Label(new Rect(0f, 700f, w - MirrorLeft, 60f), VolleyStrings.NextMatch(t.NameOf(t.Opponent)), 40, Color.white);
            if (_phaseTime < 1.5f) return;
            if (two) DrawReadyLine(w, 770f);
            else Label(new Rect(0f, 770f, w - MirrorLeft, 50f), PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter
                           : PlayerGate.PlacementHint(_input) ?? VolleyStrings.RaiseForNext(false), 30, Sun);
        }

        void DrawReadyLine(float w, float y)
        {
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            string Status(int k)
            {
                var s = _slots[k];
                return !s.Present ? CommonStrings.StepIn : !s.Calibrated ? VolleyStrings.StandStill : s.Ready ? VolleyStrings.Ready : VolleyStrings.RaiseYourHands;
            }
            Label(new Rect(0f, y, w - MirrorLeft, 50f), $"{VolleyStrings.Player(0)}: {Status(0)}    {VolleyStrings.Player(1)}: {Status(1)}", 30, Color.white);
        }

        void DrawResults(float w)
        {
            bool two = Mode != VolleyMode.Solo, together = Mode == VolleyMode.Together;
            var t = Tournament;
            if (t != null)
            {
                DrawTitle(w, t.Won ? VolleyStrings.Champion(together) : VolleyStrings.OutIn(t.Round, together), 26f, t.Won ? 120 : 72);
                var champion = t.Champion;
                string sub = t.Won ? VolleyStrings.WonTheCup(together) + (_newCup ? $"    {VolleyStrings.Cups}: {Cups(Mode)[(int)Difficulty]}" : "")
                    : champion.HasValue ? VolleyStrings.TournamentWinner(t.NameOf(champion.Value)) : null;
                if (sub != null) Label(new Rect(0f, t.Won ? 166f : 150f, w, 56f), sub, 36, Color.white);
                if (!t.Won) VolleyBracketView.Draw(t, new Rect(170f, 220f, w - 340f, 480f), YouName, _cup, Time.time);
            }
            else if (Mode == VolleyMode.Versus)
            {
                int winner = Mathf.Max(0, _match.Score.Winner);
                DrawTitle(w, VolleyStrings.Won(winner), 120f);
                Label(new Rect(0f, 270f, w, 60f), $"{_match.Score.Points(0)} - {_match.Score.Points(1)}    {VolleyStrings.LongestRally}: {_longestRally}", 40, Color.white);
                for (int k = 0; k < 2; k++)
                {
                    var s = _match.Stats[k];
                    float x = k == 0 ? w / 2f - 640f : w / 2f + 40f;
                    Label(new Rect(x, 400f, 600f, 60f), VolleyStrings.Player(k), 44, PlayerColors[k]);
                    Label(new Rect(x, 466f, 600f, 50f), $"{VolleyStrings.Touches}: {s.Touches}    {VolleyStrings.Spikes}: {s.Spikes}    {VolleyStrings.Blocks}: {s.Blocks}", 32, Color.white);
                }
            }
            else
            {
                bool won = _match.Score.Winner == 0;
                string title = together ? (won ? VolleyStrings.WeWon : VolleyStrings.TheyWon(_match.Level.TeamName))
                    : won ? VolleyStrings.YouWon : VolleyStrings.TheyWon(_match.Level.TeamName);
                DrawTitle(w, title, 150f, title.Length > 20 ? 80 : 104);
                Label(new Rect(0f, 300f, w, 60f), $"{VolleyStrings.SingleOf(Difficulty)}    {_match.Score.Points(0)} - {_match.Score.Points(1)}", 40, Color.white);
                int touches = _match.Stats[0].Touches + _match.Stats[1].Touches, spikes = _match.Stats[0].Spikes + _match.Stats[1].Spikes;
                int blocks = _match.Stats[0].Blocks + _match.Stats[1].Blocks;
                Label(new Rect(0f, 370f, w, 50f), $"{VolleyStrings.Touches}: {touches}    {VolleyStrings.Spikes}: {spikes}    {VolleyStrings.Blocks}: {blocks}    {VolleyStrings.LongestRally}: {_longestRally}",
                      32, new Color(1f, 1f, 1f, 0.85f));
            }
            if (_phaseTime < 1.5f) return;
            if (two) DrawReadyLine(w, 720f);
            string again = PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter : PlayerGate.PlacementHint(_input) ?? VolleyStrings.AgainOrMenu(two);
            Label(new Rect(0f, two ? 776f : t != null && t.Won ? 800f : 730f, w - MirrorLeft, 50f), again, 30, Sun);
        }

        void DrawTwoPaused(float w)
        {
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                if (s.Present && s.Calibrated) continue;
                GameHud.Message(w, s.Present ? VolleyStrings.HoldStillPlayer(k) : VolleyStrings.ComeBackPlayer(k), null, PlayerColors[k]);
                return;
            }
        }

        void DrawMatchInfo(float w)
        {
            string info = Tournament != null ? $"{VolleyStrings.RoundName(Tournament.Round)}  ·  {VolleyStrings.Difficulty(Difficulty)}"
                : VolleyStrings.SingleOf(Difficulty);
            float width = SportHud.Width(info, 28) + 48f;
            var r = new Rect(w - 30f - width, 26f, width, 52f);
            SportHud.Panel(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.72f), 26f, null, 0f, false);
            SportHud.Text(r, info, 28, Color.white, TextAnchor.MiddleCenter, 0f);
        }

        /// <summary>Both teams (two chips in their shirts' colours), their points and a dot by the team serving: top left, or
        /// across the middle of a split screen.</summary>
        void DrawScoreboard(float w)
        {
            var score = _match.Score;
            const float y = 24f, rowH = 60f, width = 600f;
            float x = Split ? w / 2f - width / 2f : 30f;
            SportHud.Panel(new Rect(x - 8f, y - 8f, width + 16f, rowH * 2f + 16f), new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.8f),
                           20f, null, 0f, false);
            for (int team = 0; team < 2; team++)
            {
                float ry = y + team * rowH;
                for (int k = 0; k < 2; k++)
                {
                    var look = _views[team * 2 + k] != null ? _views[team * 2 + k].Character?.Look : null;
                    var chip = look != null ? look.Color("top") : Color.white;
                    SportHud.Rounded(new Rect(x + 8f + k * 16f, ry + 10f, 11f, rowH - 20f), chip, 5f);
                }
                if (score.Server == team && !score.MatchOver)
                {
                    GUI.color = Sun;
                    GUI.DrawTexture(new Rect(x + 48f, ry + 20f, 20f, 20f), _dot);
                    GUI.color = Color.white;
                }
                GameHud.Text(new Rect(x + 78f, ry, 400f, rowH), TeamName(team), GameHud.Small, TextAnchor.MiddleLeft);
                SportHud.Rounded(new Rect(x + 500f, ry + 8f, 86f, rowH - 16f), new Color(1f, 1f, 1f, 0.14f), 10f);
                GameHud.Text(new Rect(x + 500f, ry, 86f, rowH), score.Points(team).ToString(), GameHud.Small, TextAnchor.MiddleCenter, Sun);
            }
        }
    }
}
