using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Joint = MoveIt.Motion.Joint;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// "Tenis!": doubles after Wii Sports Tennis, for one or two players. The rules are <see cref="TennisMatch"/> (no
    /// scene); this draws them: the stadium (<see cref="TennisStadium"/>), the four toy players
    /// (<see cref="TennisPlayerView"/>: chibis from the shared character kit, <see cref="TennisLooks"/>; seen from behind,
    /// high like Wii's camera), the ball with its shadow and a trail, sparks, the umpire's calls, the scoreboard, sounds.
    /// <list type="bullet">
    /// <item><b>The toys run to the ball by themselves</b> (2026-11-07, user: "the characters move on their own and go
    /// near where the ball will end up; depending on the difficulty not all balls need to be reached"; before, the body's
    /// sideways steps moved them): the player only swings. How quick they are is the difficulty's
    /// (<see cref="TennisLevel.HumanRunSpeed"/>).</item>
    /// <item><b>The menu</b> on the waiting and results screens (<see cref="TennisMenu"/>, the remote's ↑ ↓ ← →, all
    /// remembered): one player, two against each other or two together (2026-11-06: chosen with the remote, never because
    /// two people are in view); one match or a tournament; easy, medium or hard; the classic camera (a little steeper than
    /// Wii's) or from above. <b>A tournament</b> (<see cref="TennisTournament"/>): eight teams, a quarter-final, a
    /// semi-final and the final, each a little stronger, the bracket between them, the cup at the end (tournaments won are
    /// counted per difficulty).</item>
    /// <item><b>Two players</b> (2026-11-05): two people stand side by side (player 1 on the left:
    /// <see cref="PlayerSlots"/>; in a match a player is a side of the room) and both raise their hands. <b>Versus</b>:
    /// one team each, the screen split down the middle, each half seen from behind its team (player 1's on the left,
    /// where they stand). <b>Together</b>: one toy each against the computer's pair, changing places between the back and
    /// the net every game. One player raising their hands with nobody else in view plays alone after
    /// <see cref="JoinWait"/> s. The box's pose AI then follows both (<see cref="AndroidMotionService.SetPeople"/>),
    /// sharing its NPU (<see cref="BoxGraphics.Settings.TwoPlayerRuns"/>).</item>
    /// <item><b>A sideways swing hits when the ball is near enough</b>: every quick move of a hand
    /// (<see cref="SwingDetector"/>, one per player) goes to the rules with the moment the camera saw it
    /// (<see cref="TennisMatch.Swing(int, double, float, Vector2, bool)"/>); it hits for whichever of that player's toys
    /// meets the ball, as hard as the hand went sideways; up or down it misses (a hint says to swing sideways).</item>
    /// <item><b>The toys swing when the player swings</b> (2026-11-06; before, their rackets followed the camera's hand
    /// every frame, which shook with the pose model's noise): they hold the racket ready, take it back as a ball comes,
    /// and every swing of the player is drawn as a stroke (<see cref="TennisPlayerView"/>): the hit timed to the ball,
    /// any other swing at once in the hand's direction, as quick as the hand.</item>
    /// <item>Keyboard or remote without a camera (one player): space / Enter / OK swings (hold W for a lob, S for a
    /// slice) or serves. H (or the remote's ↑ while playing with the camera) puts the racket in the other hand.</item>
    /// </list>
    /// </summary>
    public sealed class TennisGame : MonoBehaviour
    {
        /// <summary><see cref="Bracket"/>: a tournament between its matches (the bracket, hands up for the next one).</summary>
        public enum Phase { Waiting, Intro, Playing, MatchEnd, Bracket, Results }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField, Tooltip("Tundu/Toon with vertex colours and gloss: the players.")] Material actorMaterial;
        [SerializeField, Tooltip("Tundu/Toon with vertex colours, matte: the stadium.")] Material stadiumMaterial;
        [SerializeField, Tooltip("Tundu/Fx, alpha: the net (its cell texture is drawn in code).")] Material netMaterial;
        [SerializeField, Tooltip("Tundu/Toon, the ball's yellow, a little emission.")] Material ballMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: sparks.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the soft dot: dust puffs and the ball's shadow.")] Material dotMaterial;
        [SerializeField, Tooltip("Tundu/Fx additive: the ball's and the rackets' trails.")] Material trailMaterial;
        [SerializeField, Tooltip("Tundu/Fx, alpha: the sun's shadows (its texture, the shadow atlas, is drawn in code).")] Material shadowMaterial;
        [SerializeField, Tooltip("Tundu/Fx additive with the soft dot: the ball's glow.")] Material glowMaterial;

        [Header("Match")]
        [SerializeField, Tooltip("Games to win a match (Wii: 1, 3 or 5 game matches; 2 = best of 3).")] int gamesToWin = 2;
        [SerializeField] float introSeconds = 3.2f, matchEndSeconds = 3.5f;
        [SerializeField, Tooltip("The ball is drawn this big (m across): a real one (6.7 cm) is hard to see on a TV.")] float ballSize = 0.3f;

        /// <summary>A camera view (<see cref="TennisMenu.View"/>): where the camera sits (behind the near baseline, m) and how
        /// it looks down (degrees), its vertical field of view; one picture and a half of the split screen (8:9).</summary>
        [System.Serializable]
        public struct CameraView
        {
            public float Height, Behind, Pitch, Fov, SplitHeight, SplitBehind, SplitPitch, SplitFov;
        }

        /// <summary>
        /// The two camera views (2026-11-07, user: "the perspective a bit more tilted, more top down view available").
        /// Classic: a little higher and steeper than Wii's camera (measured from the user's clip on 2026-11-05: 6.47 m up,
        /// 12.3 m behind the baseline, 17 degrees down, 32 degrees of view), so the far court opens up while the near
        /// player stays big. From above: high over the baseline, looking down on the whole court.
        /// </summary>
        public static readonly CameraView[] CameraViews =
        {
            new CameraView { Height = 8.2f, Behind = 12.6f, Pitch = 22f, Fov = 34f, SplitHeight = 8.4f, SplitBehind = 10.6f, SplitPitch = 26f, SplitFov = 46f },
            new CameraView { Height = 13f, Behind = 14.1f, Pitch = 33f, Fov = 38f, SplitHeight = 12.5f, SplitBehind = 11.5f, SplitPitch = 36f, SplitFov = 48f },
        };

        const string HandKey = "tennis.rightHanded", CupsKey = "tennis.cups";
        static readonly Color Sun = TennisStadium.Sun, Coral = TennisStadium.Coral, Sky = TennisStadium.Sky;
        static readonly Color[] TheirShirts = { TennisStadium.Coral, TennisStadium.Grape, TennisStadium.Leaf, new Color(1f, 0.55f, 0.15f) };
        /// <summary>The two players' colours (their toys wear them): player 1 red, player 2 sky blue.</summary>
        public static readonly Color[] PlayerColors = { new Color(0.91f, 0.25f, 0.23f), TennisStadium.Sky };
        /// <summary>Two players: a player who raised their hands waits this long (s) for the other, then plays alone.</summary>
        public const float JoinWait = 4f;

        public Phase Current { get; private set; }
        public TennisMatch Match => _match;
        public bool RightHanded => _humans[0].RightHanded;
        /// <summary>Who plays the match on (or the last one).</summary>
        public TennisMode Mode { get; private set; } = TennisMode.Solo;
        /// <summary>One match or a tournament, and how hard: the match on (or the last one).</summary>
        public TennisFormat Format { get; private set; }
        public TennisDifficulty Difficulty { get; private set; }
        /// <summary>The tournament on (or the last one), or null for a single match.</summary>
        public TennisTournament Tournament => Format == TennisFormat.Tournament ? _tournament : null;
        /// <summary>The waiting screen's choices (remembered).</summary>
        public TennisMenu Menu => _menu;
        /// <summary>What the next game is: one player, or two against each other or together (the menu's first row).</summary>
        public TennisMode NextMode
        {
            get => _menu.Mode;
            set => _menu.Mode = value;
        }
        /// <summary>The next game for two players (tests): versus or together.</summary>
        public TennisMode TwoPlayerMode
        {
            get => _menu.Mode == TennisMode.Together ? TennisMode.Together : TennisMode.Versus;
            set => _menu.Mode = value == TennisMode.Together ? TennisMode.Together : TennisMode.Versus;
        }
        /// <summary>The two players in front of the camera (player 1 on the left).</summary>
        public PlayerSlots Slots => _slots;
        /// <summary>Practice (set before the player starts): no score, the computer returns everything and times its
        /// feeds with <see cref="PracticeContact"/> (tests).</summary>
        public bool Practice { get; set; }
        public Func<double, double> PracticeContact { get; set; }
        /// <summary>Who serves first in the next match (0 = the player, or player 1's team; tests set 1 to watch the player
        /// move while receiving).</summary>
        public int FirstServer { get; set; }
        public Func<double, double> PracticeToss { get; set; }
        /// <summary>Every quick hand move the camera saw while playing: the match time it was mapped to, and what it did.</summary>
        public event Action<SwingEvent, double, SwingResult> SwingHandled;
        /// <summary>The same with the player who swung (0 = player 1).</summary>
        public event Action<int, SwingEvent, double, SwingResult> PlayerSwingHandled;
        public event Action<MatchEvent> MatchEvents;
        public int SwingsSeen { get; private set; }
        /// <summary>Moves of the hands the swing detectors took for the pose model's glitches and ignored (tests, logs).</summary>
        public int GlitchesIgnored => _humans[0].Detector.Ignored + _humans[1].Detector.Ignored;
        /// <summary>The players as drawn: team 0's two, then team 1's (tests).</summary>
        public IReadOnlyList<TennisPlayerView> Views => _views;
        /// <summary>The second half's camera while the screen is split (versus), else null.</summary>
        public Camera SplitCamera => _cam2 != null && _cam2.enabled ? _cam2 : null;

        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        TennisMatch _match;
        TennisStadium _stadium;
        readonly TennisPlayerView[] _views = new TennisPlayerView[4];
        readonly ChibiLook[] _looks = new ChibiLook[4];
        int _viewsOpponent = -1;
        TennisMode _viewsMode = (TennisMode)(-1);
        readonly TennisMenu _menu = new TennisMenu();
        TennisTournament _tournament;
        float _phaseTime;
        bool _paused, _newCup;
        int _longestRally, _humanHitsThisRun;
        readonly PlayerSlots _slots = new PlayerSlots { Area = TwoArea };
        float _readySince = -1f;
        int _lastBack = -1;   // together: the player at the back in the last game (a call when they change places)

        /// <summary>One player's input: their swings and their hints.</summary>
        sealed class Human
        {
            public readonly SwingDetector Detector = new SwingDetector();
            public double LastSample;
            public Body Body;
            public bool RightHanded = true;
            public int HintedMisses, HintedEarly, HintedLate, HintedVertical;
            public string Hint;
            public float HintUntil;

            public void Reset()
            {
                Detector.Reset();
                HintedMisses = HintedEarly = HintedLate = HintedVertical = 0;
                Hint = null;
            }
        }
        readonly Human[] _humans = { new Human(), new Human() };

        Transform _ball, _trophy;
        Mesh _cupMesh;
        float _confettiAt;
        TennisFx _fx;
        Texture2D _shadowAtlas;
        Material _shadows;
        /// <summary>How fast each toy ran last frame (grass kicked up when one sets off or pulls up).</summary>
        readonly Vector2[] _lastRun = new Vector2[4];
        /// <summary>The last shot's glow (a perfect or power shot glows warmer and bigger).</summary>
        Color _glowColour = Color.white;
        float _glowStrength;
        TrailRenderer _ballTrail;
        Texture2D _dot, _netCell, _cup;
        Material _net;
        Camera _cam2;
        readonly float[] _cameraX = new float[2], _cameraZ = new float[2];
        float _shake;

        string _call, _callSub;
        float _callUntil, _callFrom;
        readonly List<Popup> _popups = new List<Popup>();

        sealed class Popup { public Vector3 World; public string Text; public Color Color; public float Age; }

        // ---------------------------------------------------------------- lifecycle

        /// <summary>
        /// The toys run by themselves, so the player only swings (no steps to the side): the knees in the picture, room
        /// for an arm swung out to either side, and above the head for the serve.
        /// </summary>
        static readonly PlayArea Area = new PlayArea(BodyPart.Knees, side: 0.3f, above: 0.1f);
        /// <summary>Two players share the picture: each needs the knees in it and room for a swing to their outer side.</summary>
        static readonly PlayArea TwoArea = new PlayArea(BodyPart.Knees, side: 0.3f, above: 0.1f);

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            PlayerGate.Use(_input, Area);
            // the small skeleton mirror sits in the bottom-right corner: shown only while waiting, on the results and while
            // paused (given back to the next scene as it was)
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null) _mirrorWasVisible = _mirror.Visible;
            _menu.Load();
            _humans[0].RightHanded = PlayerPrefs.GetInt(HandKey, 1) == 1;
            _humans[1].RightHanded = PlayerPrefs.GetInt(HandKey + "2", 1) == 1;
            foreach (var h in _humans) h.Detector.RightHanded = h.RightHanded;
            _dot = TennisArt.Dot();
            _cup = TennisArt.Cup();
            _netCell = TennisArt.NetCell();
            _net = new Material(netMaterial) { name = "Net (cells)" };   // a copy: the texture is made in code
            _net.SetTexture("_BaseMap", _netCell);
            // the sun's shadows: the stadium's fixed ones and the moving ones (TennisFx) from one atlas drawn in code
            _shadowAtlas = TennisArt.ShadowAtlas();
            _shadows = new Material(shadowMaterial) { name = "Shadows (atlas)" };
            _shadows.SetTexture("_BaseMap", _shadowAtlas);
            var sun = FindAnyObjectByType<Light>();
            _stadium = TennisStadium.Build(stadiumMaterial, _net, _shadows, sun != null ? sun.transform.forward : (Vector3?)null);
            _fx = TennisFx.Create(_shadows, sparkMaterial, dotMaterial, glowMaterial, sun);
            BuildBall();
            // the tournament's cup: shown spinning in front of the camera on the champion screen
            _cupMesh = TennisArt.CupMesh();
            _trophy = Shape("Trophy", _cupMesh, actorMaterial, false);
            _trophy.gameObject.SetActive(false);
            Difficulty = _menu.Difficulty;
            NewMatch(TennisLevel.For(_menu.Difficulty, 1, 0, Mode != TennisMode.Together));
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("tennis");
        }

        void OnDestroy()
        {
            if (_dot != null) Destroy(_dot);
            if (_cup != null) Destroy(_cup);
            if (_netCell != null) Destroy(_netCell);
            if (_net != null) Destroy(_net);
            if (_shadowAtlas != null) Destroy(_shadowAtlas);
            if (_cupMesh != null) Destroy(_cupMesh);
            if (_shadows != null) Destroy(_shadows);
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
        }

        /// <summary>Tournaments won at a difficulty (one player, or two together: counted apart).</summary>
        static string CupKey(TennisMode mode, TennisDifficulty d) => $"{CupsKey}{(mode == TennisMode.Together ? "2" : "")}.{(int)d}";

        int[] Cups(TennisMode mode) => new[]
        {
            PlayerPrefs.GetInt(CupKey(mode, TennisDifficulty.Easy), 0), PlayerPrefs.GetInt(CupKey(mode, TennisDifficulty.Medium), 0),
            PlayerPrefs.GetInt(CupKey(mode, TennisDifficulty.Hard), 0),
        };

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

        /// <summary>A fresh match against <paramref name="level"/>'s player (singles) or pair (two players together; their
        /// looks change with them), or between the two players. Singles has one toy a side: the others aren't made.</summary>
        void NewMatch(TennisLevel level)
        {
            if (_match != null) _match.Events -= OnMatchEvent;
            _match = new TennisMatch(level, gamesToWin, Random.Range(0, 1 << 30), FirstServer, Mode)
            {
                Practice = Practice && Mode == TennisMode.Solo, PracticeContact = PracticeContact, PracticeToss = PracticeToss,
            };
            for (int k = 0; k < 2; k++) _match.SetRightHanded(k, _humans[k].RightHanded);
            _match.Events += OnMatchEvent;
            _match.Begin();   // the players take their places (the clock runs once play starts)
            var their = _match.Level;
            ChibiLook[] looks = Mode switch
            {
                TennisMode.Versus => new[] { TennisLooks.Player(), null, TennisLooks.Player2(), null },
                TennisMode.Together => new[] { TennisLooks.Player(), TennisLooks.Player2(), null, null },
                _ => new[] { TennisLooks.Player(), null, TennisLooks.Opponent(their.NameIndex), null },
            };
            if (Mode == TennisMode.Together) (looks[2], looks[3]) = TennisLooks.Opponents(their);
            string[] names = Mode switch
            {
                TennisMode.Versus => new[] { "Player 1", null, "Player 2", null },
                TennisMode.Together => new[] { "Player", "Player 2", $"Opponent {their.Name}", $"Opponent {their.Partner}" },
                _ => new[] { "Player", null, $"Opponent {their.Name}", null },
            };
            for (int i = 0; i < 4; i++)
            {
                // the players' toys stay as long as the mode does; the computer's change with the opponent
                bool keep = _views[i] != null && _viewsMode == Mode && (i < 2 || Mode == TennisMode.Versus || _viewsOpponent == their.Opponent);
                _looks[i] = looks[i];
                if (keep) continue;
                if (_views[i] != null) Destroy(_views[i].gameObject);
                _views[i] = null;
                // (no blob shadow: TennisFx draws every toy's sun shadow); singles: one toy a side
                if (looks[i] != null) _views[i] = TennisPlayerView.Create(names[i], actorMaterial, trailMaterial, looks[i]);
            }
            _viewsOpponent = their.Opponent;
            _viewsMode = Mode;
            ApplyDetail();
            _lastBack = -1;
            foreach (var h in _humans) h.Detector.Reset();
            Debug.Log($"[Tennis] {(Format == TennisFormat.Tournament && _tournament != null ? TennisStrings.RoundName(_tournament.Round) : "Match")} " +
                      $"{Mode} {Difficulty} (strength {their.Strength:0.0}){(Mode == TennisMode.Versus ? "" : $" vs {their.TeamName}")}{(Practice ? " (practice)" : "")}");
        }

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            // hands raised in joy at the end aren't a start
            if (phase is Phase.Waiting or Phase.Results or Phase.Bracket) _slots.ClearReady();
            _readySince = -1f;
            ApplyViewports();
            if (phase != Phase.Results) Debug.Log($"[Tennis] {phase}");
            else
            {
                var t = Tournament;
                string how = t != null ? (t.Won ? "won the tournament" : $"out in {TennisStrings.RoundName(t.Round)}")
                    : Mode == TennisMode.Versus ? $"player {_match.Score.Winner + 1} won" : _match.Score.Winner == 0 ? "won" : "lost";
                Debug.Log($"[Tennis] Results {Mode} {Format} {Difficulty}: {how}, longest rally {_longestRally}");
            }
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            var tracker = MotionSystem.Instance != null ? MotionSystem.Instance.Tracker : null;
            _slots.Locked = Current is Phase.Intro or Phase.Playing or Phase.MatchEnd or Phase.Bracket && Mode != TennisMode.Solo;
            if (tracker != null) _slots.Update(tracker.Bodies);
            AskForPeople();
            HandleKeys();
            ReadBodies();

            switch (Current)
            {
                case Phase.Waiting:
                    if (HandleMenu()) break;
                    if (ReadyToGo(_menu.Mode, true, out bool alone)) StartRun(alone ? TennisMode.Solo : _menu.Mode);
                    break;
                case Phase.Intro:
                    if (_phaseTime >= introSeconds)
                    {
                        GameAudio.Sfx("go", vary: 0f);
                        SetPhase(Phase.Playing);
                    }
                    break;
                case Phase.Playing:
                    _paused = Mode == TennisMode.Solo ? !UsingKeys && PlayerGate.ShouldPause(_input) : TwoPlayersPaused();
                    if (!_paused)
                    {
                        _match.Step(dt);
                        if (UsingKeys) _match.SawUntil(_match.Time);   // no camera to catch up with: points end at once
                    }
                    if (_match.Phase == MatchPhase.Over) EndMatch();
                    break;
                case Phase.MatchEnd:
                    _match.Step(dt);
                    if (_phaseTime >= matchEndSeconds) SetPhase(_tournament != null && Format == TennisFormat.Tournament && !_tournament.Over ? Phase.Bracket : Phase.Results);
                    break;
                case Phase.Bracket:
                    // the next round: hands up (both, two together), or OK
                    if (_phaseTime > 1.5f && ReadyToGo(Mode, false, out _)) StartMatch(_tournament.Level);
                    break;
                case Phase.Results:
                    if (_phaseTime < 1.5f) break;
                    // the arrows go back to the menu; hands up (or OK) plays the same again
                    if (RemoteInput.Up || RemoteInput.Down || RemoteInput.Left || RemoteInput.Right)
                    {
                        SetPhase(Phase.Waiting);
                        break;
                    }
                    if (ReadyToGo(Mode, true, out bool again)) StartRun(again ? TennisMode.Solo : Mode);
                    break;
            }

            if (_mirror != null) _mirror.Visible = _mirrorWasVisible && (Current is Phase.Waiting or Phase.Results or Phase.Bracket || _paused);
            UpdateViews(dt);
            UpdateBall();
            UpdateTrophy();

            UpdateCamera(dt);
        }

        bool UsingKeys => Mode == TennisMode.Solo && (_input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService);

        /// <summary>A screen where two players get ready (waiting with a two-player game chosen, the results or the bracket
        /// after one): each one's card says what they still have to do.</summary>
        bool TwoWaiting => Current switch
        {
            Phase.Waiting => _menu.Mode != TennisMode.Solo,
            Phase.Results or Phase.Bracket => Mode != TennisMode.Solo,
            _ => false,
        };

        /// <summary>
        /// The box's pose AI: two people while two play or a two-player game is chosen, else one (the player keeps the
        /// AI's full rate even with someone else in view: that person's track ends; no detector looking for a second).
        /// </summary>
        void AskForPeople()
        {
            bool one = (Current == Phase.Waiting ? _menu.Mode : Mode) == TennisMode.Solo;
            if (one)
            {
                var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
                AndroidMotionService.SetPeople(1, keep: body != null ? body.ServiceTrack : 0);
            }
            else AndroidMotionService.SetPeople(2, BoxGraphics.Current.TwoPlayerRuns);
        }

        /// <summary>
        /// The waiting screen's menu (<see cref="TennisMenu"/>): the remote's ↑ ↓ pick a row, ← → change it (remembered;
        /// a new camera view shows at once behind the menu). True when an arrow was used this frame.
        /// </summary>
        bool HandleMenu()
        {
            bool up = RemoteInput.Up, down = RemoteInput.Down, left = RemoteInput.Left, right = RemoteInput.Right;
            if (!(up || down || left || right)) return false;
            bool camera = PlayerGate.State(_input) != GateState.NoService;
            if (_menu.Handle(up, down, left, right, camera, out bool moved))
            {
                GameAudio.Sfx("tennis_hit", 0.4f);
                _readySince = -1f;
                _slots.ClearReady();
                Difficulty = _menu.Difficulty;
                ApplyViewports();
            }
            else if (moved) GameAudio.Sfx("tennis_bounce", 0.35f);
            // the remote's arrows choose here; they aren't keyboard play
            if (_input != null && camera) _input.PreferBody();
            return true;
        }

        /// <summary>
        /// Whether the players are ready for a match of <paramref name="mode"/>: one player raises their hands (or Enter /
        /// OK); two both raise theirs (or OK with both in place). With <paramref name="allowAlone"/>, one of two who raised
        /// their hands with nobody else in view plays alone after <see cref="JoinWait"/> s (<paramref name="alone"/>).
        /// </summary>
        bool ReadyToGo(TennisMode mode, bool allowAlone, out bool alone)
        {
            alone = false;
            if (mode == TennisMode.Solo)
            {
                _readySince = -1f;
                _slots.ClearReady();
                return StartPressed();
            }
            if ((_slots[0].Ready && _slots[1].Ready) || (RemoteInput.Confirm && _slots.BothCanPlay)) return true;
            int ready = _slots[0].Ready ? 0 : _slots[1].Ready ? 1 : -1;
            if (!allowAlone || ready < 0 || _slots.Present >= 2)
            {
                _readySince = -1f;   // both in view: wait for the other one's hands
                return false;
            }
            if (_readySince < 0f) _readySince = Time.time;
            if (Time.time - _readySince < JoinWait) return false;
            // nobody else came: the one who raised their hands plays alone
            MotionSystem.Instance?.Tracker.SetActive(_slots[ready].Body);
            alone = true;
            return true;
        }

        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        /// <summary>What hands up (or OK) does on the screen showing: the menu's game starts, a tournament's next match,
        /// the same game again after the results (tests).</summary>
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

        /// <summary>A new game with the menu's choices: one match, or a tournament's first round (a new draw).</summary>
        void StartRun(TennisMode mode)
        {
            Mode = mode;
            Format = _menu.FormatFor(mode);
            Difficulty = _menu.Difficulty;
            // the remote (its arrows choosing the game, or the menu's) mustn't leave the camera player on the keys
            if (_input != null && PlayerGate.State(_input) != GateState.NoService) _input.PreferBody();
            _longestRally = 0;
            _newCup = false;
            _humanHitsThisRun = 0;
            _readySince = -1f;
            _slots.ClearReady();
            foreach (var h in _humans) h.Reset();
            // one player plays singles against one of the fourteen, two together doubles against one of the seven pairs
            bool singles = mode != TennisMode.Together;
            _tournament = Format == TennisFormat.Tournament ? new TennisTournament(Difficulty, Random.Range(0, 1 << 30), gamesToWin, singles) : null;
            GameAudio.PlayMusic("tennis");
            StartMatch(_tournament != null ? _tournament.Level
                : TennisLevel.For(Difficulty, 1, Random.Range(0, TennisLevel.OpponentCount(singles)), singles));
        }

        void StartMatch(TennisLevel level)
        {
            NewMatch(level);
            string title = Mode != TennisMode.Versus && Tournament != null ? TennisStrings.RoundName(Tournament.Round) : TennisStrings.Title;
            string sub = Mode switch
            {
                TennisMode.Versus => TennisStrings.PlayerVersusPlayer,
                TennisMode.Together => TennisStrings.UsVersus(_match.Level.TeamName),
                _ => TennisStrings.Versus(_match.Level.TeamName),
            };
            ShowCall(title, sub, introSeconds);
            GameAudio.Sfx("tennis_call", vary: 0f);
            // where the players stand now is the middle (the placement check's sides, two players' sides of the room)
            if (Mode == TennisMode.Solo)
            {
                if (_input != null && _input.Gestures != null) _input.Gestures.Recenter();
            }
            else _slots.Recenter();
            SetPhase(Phase.Intro);
        }

        /// <summary>A match is over: the call, a tournament's round recorded (and a cup counted when it's won), then the
        /// celebration before the bracket or the results.</summary>
        void EndMatch()
        {
            _longestRally = Mathf.Max(_longestRally, _match.LongestRally);
            int winner = _match.Score.Winner;
            bool won = winner == 0, two = Mode == TennisMode.Together;
            var t = Tournament;
            if (t != null && !Practice)
            {
                t.Record(won, _match.Score.Games(0), _match.Score.Games(1));
                if (t.Won)
                {
                    string key = CupKey(Mode, Difficulty);
                    PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
                    PlayerPrefs.Save();
                    _newCup = true;
                }
            }
            GameAudio.Jingle(won || Mode == TennisMode.Versus ? "jingle_win" : "jingle_gameover");
            if (won || Mode == TennisMode.Versus)
            {
                _stadium.Cheer(1f);
                // confetti over the winners' half (a shower of it for the cup)
                _fx.Confetti(new Vector3(0f, 0f, Court.Side(Mathf.Max(0, winner)) * 9f), t != null && t.Won ? 160 : 70);
            }
            string call = Mode switch
            {
                TennisMode.Versus => TennisStrings.Won(winner),
                TennisMode.Together => won ? TennisStrings.WeWon : TennisStrings.TheyWon(_match.Level.TeamName, _match.Level.Singles),
                _ => won ? TennisStrings.YouWon : TennisStrings.TheyWon(_match.Level.TeamName, _match.Level.Singles),
            };
            string sub = t == null ? null : t.Won ? TennisStrings.WonTheCup(two) : won ? TennisStrings.ThroughTo(t.Round, two) : TennisStrings.OutIn(t.Round, two);
            ShowCall(t != null && t.Won ? TennisStrings.Champion(two) : call, sub, matchEndSeconds);
            for (int k = 0; k < _match.Humans; k++)
            {
                var s = _match.Stats[k];
                Debug.Log($"[Tennis] {Mode}: player {k + 1} hits {s.Hits} ({s.Volleys} volleys, {s.Perfect} perfect), " +
                          $"early/late {s.Whiffs}, too far {s.TooFar}, missed {s.Missed}");
            }
            Debug.Log($"[Tennis] Match {(Mode == TennisMode.Versus ? $"won by player {winner + 1}" : won ? "won" : "lost")} " +
                      $"{_match.Score.Games(0)}-{_match.Score.Games(1)}, points {_match.PointsWon}-{_match.PointsLost}, longest rally {_match.LongestRally}" +
                      (t != null ? $"; tournament: {(t.Won ? "won" : t.Out ? "out" : $"next {TennisStrings.RoundName(t.Round)} vs {t.NameOf(t.Opponent)}")}" : ""));
            SetPhase(Phase.MatchEnd);
        }

        /// <summary>Two players: the match waits while one of them is out of view or hasn't stood still since coming back
        /// (their steps are measured from there).</summary>
        bool TwoPlayersPaused()
        {
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                if (!s.Present || !s.Calibrated) return true;
            }
            return false;
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            // the remote's ↑ swaps hands only in a match (on the menu it picks a row)
            bool inMatch = Current is Phase.Intro or Phase.Playing or Phase.MatchEnd;
            bool swap = (kb != null && kb.hKey.wasPressedThisFrame) || (inMatch && !UsingKeys && Mode == TennisMode.Solo && RemoteInput.Up);
            if (swap) SwapHands(0);
            if (kb != null && kb.jKey.wasPressedThisFrame) SwapHands(1);
            if (Current != Phase.Playing || _paused || !RemoteInput.Confirm || Mode != TennisMode.Solo) return;
            // space, Enter or the remote's OK: serves, or swings across, away from the coming ball's side (the stroke: W
            // held = a gentle scoop straight up = a lob, S held = down = a slice)
            if (_match.Human.Serving)
            {
                _match.Swing(_match.Time, 7f, Vector2.down);
                return;
            }
            var n = _match.Next;
            float ballSide = _match.Phase == MatchPhase.Rally && n.Active && n.Team == 0 && !n.Taken ? n.Side : RightHanded ? 1f : -1f;
            bool lob = kb != null && kb.wKey.isPressed;
            float rise = lob ? 0.97f : kb != null && kb.sKey.isPressed ? -0.6f : 0.15f;
            var direction = new Vector2(-ballSide * Mathf.Sqrt(1f - rise * rise), rise);
            var result = _match.Swing(_match.Time, lob ? 2.5f : 7f, direction, gentle: lob);
            OnSwingResult(0, result);
            GameAudio.Sfx("tennis_swish", 0.5f);
        }

        /// <summary>Player <paramref name="slot"/>'s racket goes to the other hand (remembered).</summary>
        void SwapHands(int slot)
        {
            var h = _humans[slot];
            h.RightHanded = !h.RightHanded;
            h.Detector.RightHanded = h.RightHanded;
            _match.SetRightHanded(slot, h.RightHanded);
            PlayerPrefs.SetInt(slot == 0 ? HandKey : HandKey + "2", h.RightHanded ? 1 : 0);
            PlayerPrefs.Save();
            ShowHint(slot, TennisStrings.HandSwapped(h.RightHanded), 2f);
        }

        /// <summary>The body a player plays with: one player, the tracker's active body; two, their slot's.</summary>
        Body BodyOf(int slot) => Mode == TennisMode.Solo
            ? (slot == 0 && MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null)
            : _slots[slot].Body;

        /// <summary>Every player's new camera samples (<see cref="ReadBody"/>).</summary>
        void ReadBodies()
        {
            int players = Current is Phase.Waiting or Phase.Results ? (_slots.Present == 2 ? 2 : 1) : _match.Humans;
            for (int k = 0; k < players; k++) ReadBody(k, Current is Phase.Waiting or Phase.Results && _slots.Present == 2 ? _slots[k].Body : BodyOf(k));
        }

        /// <summary>
        /// A new camera sample of player <paramref name="slot"/>: the rules learn how far the camera has seen them (a point
        /// they could still have played waits for it), both wrists go to their swing detector (also while waiting, so it's
        /// settled when play starts), and a swing to the rules with the match time the camera saw it and whether the
        /// racket hand made it.
        /// </summary>
        void ReadBody(int slot, Body body)
        {
            var h = _humans[slot];
            if (body != h.Body)
            {
                // another body plays on this side now (found again, or the two sides were put right): its first samples
                // aren't a swing
                h.Body = body;
                h.Detector.Reset();
                h.LastSample = 0;
            }
            if (body == null || !body.IsTracking || body.SampleTime == h.LastSample) return;
            h.LastSample = body.SampleTime;
            double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            double age = Math.Max(0.0, Math.Min(_match.HitLag, nowUnix - body.SampleTime));
            double at = _match.Time - age;
            bool playing = Current == Phase.Playing && !_paused && !UsingKeys;
            if (playing) _match.SawUntil(slot, at);

            // the camera's own samples (the detector takes the pose model's glitches out itself: a low-lag filter would
            // smear a one-frame jump into a move), relative to the shoulders as seen in the same frame
            var raw = body.Raw;
            var shoulders = ((Vector2)raw[(int)Joint.LeftShoulder] + (Vector2)raw[(int)Joint.RightShoulder]) * 0.5f;
            int ignored = h.Detector.Ignored;
            bool swung = h.Detector.Update(body.SampleTime, shoulders, body.FastTorsoLength,
                                           raw[(int)Joint.LeftWrist], body.Visibility[(int)Joint.LeftWrist],
                                           raw[(int)Joint.RightWrist], body.Visibility[(int)Joint.RightWrist], out var swing);
            if (h.Detector.Ignored != ignored && Current == Phase.Playing)
                Debug.Log($"[Tennis] A hand's jump ignored (the pose model's glitch; {h.Detector.Ignored} so far)");
            if (!swung) return;
            SwingsSeen++;
            if (Current != Phase.Playing || _paused || slot >= _match.Humans) return;
            double swingAt = at - (body.SampleTime - swing.Time);
            var result = _match.Swing(slot, swingAt, swing.Speed, swing.Direction, swing.RightHand == h.RightHanded, swing.Gentle);
            // a gentle move that hit nothing was just the hand moving: no sound, no hint
            if (result is not (SwingResult.Serve or SwingResult.Toss or SwingResult.Backswing or SwingResult.Held) && !(swing.Gentle && result == SwingResult.NoBall))
                GameAudio.Sfx("tennis_swish", 0.45f, 1f + Random.Range(-0.08f, 0.08f));
            OnSwingResult(slot, result);
            Debug.Log($"[Tennis] Swing{(Mode == TennisMode.Solo ? "" : $" by player {slot + 1}")} {(swing.RightHand ? "right" : "left")} " +
                      $"{(swing.Gentle ? "gentle " : "")}{swing.Speed:0.0} torso/s (sideways {swing.SidewaysSpeed:0.0}) " +
                      $"dir ({swing.Direction.x:0.0}, {swing.Direction.y:0.0}), seen {age * 1000:0} ms ago, pace {_match.Pace:0.00}: {result}");
            SwingHandled?.Invoke(swing, swingAt, result);
            PlayerSwingHandled?.Invoke(slot, swing, swingAt, result);
        }

        /// <summary>A hint after a swing that didn't hit: too early, too late, or too far from the ball.</summary>
        void OnSwingResult(int slot, SwingResult result)
        {
            var h = _humans[slot];
            switch (result)
            {
                case SwingResult.Early:
                    if (h.HintedEarly++ < 3) ShowHint(slot, TennisStrings.TooEarly, 2f);
                    break;
                case SwingResult.Late:
                    if (h.HintedLate++ < 3) ShowHint(slot, TennisStrings.TooLate, 2f);
                    break;
                case SwingResult.Vertical:
                    if (h.HintedVertical++ < 5) ShowHint(slot, TennisStrings.SwingSideways, 2.5f);
                    break;
            }
        }

        // ---------------------------------------------------------------- match events: sounds, effects, calls

        void OnMatchEvent(MatchEvent e)
        {
            MatchEvents?.Invoke(e);
            var owner = e.Player >= 0 ? _match.Players[e.Player].Controller : -1;
            switch (e.Kind)
            {
                case MatchEventKind.Toss:
                    GameAudio.Sfx("tennis_toss", 0.5f);
                    if (_match.IsHuman(e.Team) && (_match.TossesMissed > 0 || _humanHitsThisRun == 0) && owner >= 0)
                        ShowHint(owner, TennisStrings.YourServe, 2.2f);
                    if (Mode == TennisMode.Together && _match.Back(0).Controller != _lastBack)
                    {
                        // they changed places for this game: say who plays at the back
                        if (_lastBack >= 0) ShowCall(TennisStrings.SwapPlaces, TennisStrings.AtTheBack(_match.Back(0).Controller), 1.6f);
                        _lastBack = _match.Back(0).Controller;
                    }
                    break;
                case MatchEventKind.Hit:
                    OnHit(e);
                    break;
                case MatchEventKind.Bounce:
                    float speed = _match.Ball.Velocity.magnitude;
                    GameAudio.Sfx("tennis_bounce", Mathf.Clamp(0.3f + speed * 0.04f, 0.3f, 0.9f));
                    _fx.Bounce(e.Position, e.In, speed);
                    if (!e.In) AddPopup(e.Position + Vector3.up * 0.6f, TennisStrings.Out, Coral);
                    break;
                case MatchEventKind.Net:
                    GameAudio.Sfx("tennis_net");
                    AddPopup(e.Position + Vector3.up * 0.8f, TennisStrings.Net, Coral);
                    break;
                case MatchEventKind.Missed:
                    if (owner < 0) break;
                    // a ball the toy couldn't get to isn't the player's fault: no hint, just what happened
                    if (!_match.Next.Reachable) AddPopup(_match.Next.Point + Vector3.up * 0.7f, TennisStrings.OutOfReach, Coral);
                    else if (_humans[owner].HintedMisses++ < 3) ShowHint(owner, TennisStrings.SwingWhenClose, 2.5f);
                    break;
                case MatchEventKind.TooFar:
                    AddPopup(e.Position + Vector3.up * 0.7f, TennisStrings.OutOfReach, Coral);
                    if (_views[e.Player] != null) _views[e.Player].Flash(Coral, 0.25f);
                    break;
                case MatchEventKind.Vertical:
                    if (_views[e.Player] != null) _views[e.Player].Flash(Coral, 0.25f);
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
            var colour = power ? new Color(1f, 0.6f, 0.2f) : s.Perfect ? Sun : Color.white;
            _fx.Hit(e.Position, power, colour);
            SetTrail(power ? new Color(1f, 0.5f, 0.15f) : s.Perfect ? Sun : Color.white, power ? 0.85f : 0.45f);
            // the ball glows warmer and bigger after a perfect or a power shot, then settles
            _glowColour = power ? new Color(1f, 0.55f, 0.2f) : s.Perfect ? Sun : new Color(1f, 1f, 0.85f);
            _glowStrength = power ? 1f : s.Perfect ? 0.8f : 0.45f;
            if (!_match.IsHuman(e.Team)) return;
            _humanHitsThisRun++;
            if (view != null) view.Flash(s.Perfect ? Sun : Color.white, s.Perfect ? 0.35f : 0.2f);   // a glint, not a coat of paint
            string text = s.Kind switch
            {
                ShotKind.PowerServe => TennisStrings.PowerServe,
                ShotKind.Smash => TennisStrings.Smash,
                ShotKind.Lob => TennisStrings.Lob,
                ShotKind.Serve => null,
                _ => s.Perfect ? TennisStrings.Perfect : null,
            };
            if (text != null) AddPopup(e.Position + Vector3.up * 0.9f, text, s.Perfect || power ? Sun : Color.white);
            Debug.Log($"[Tennis] Hit {s.Kind} {s.Stroke} by {(_match.Players[e.Player].AtNet ? "the net player" : "the back player")}" +
                      $"{(Mode == TennisMode.Solo ? "" : $" (player {_match.Players[e.Player].Controller + 1})")} {s.Speed:0.0} m/s (pace {s.Pace:0.00}), " +
                      $"timing {s.Timing:0.00}{(s.Perfect ? " perfect" : "")} at ({e.Position.x:0.0}, {e.Position.y:0.0}, {e.Position.z:0.0}) " +
                      $"to ({s.Target.x:0.0}, {s.Target.z:0.0})");
        }

        /// <summary>A team's name on the scoreboard and in the umpire's calls.</summary>
        string TeamName(int team) => Mode switch
        {
            TennisMode.Versus => TennisStrings.Player(team),
            TennisMode.Together => team == 0 ? TennisStrings.Us : _match.Level.TeamName,
            _ => team == 0 ? TennisStrings.You : _match.Level.TeamName,
        };

        void OnPoint(MatchEvent e)
        {
            bool ours = e.Team == 0 || Mode == TennisMode.Versus;
            _longestRally = Mathf.Max(_longestRally, _match.RallyHits);
            if (ours)
            {
                _stadium.Cheer(0.8f);
                GameAudio.Sfx("tennis_cheer", 0.8f);
            }
            else GameAudio.Sfx("tennis_aww", 0.7f);
            if (Practice) return;
            var score = _match.Score;
            string call = e.Call switch
            {
                ScoreCall.Deuce => TennisStrings.Deuce,
                ScoreCall.Advantage => TennisStrings.Advantage(TeamName(score.Advantage)),
                ScoreCall.Game => TennisStrings.Game(TeamName(e.Team)),
                ScoreCall.Match => TennisStrings.Game(TeamName(e.Team)),
                _ => $"{Spoken(score.Points(score.Server))} - {Spoken(score.Points(1 - score.Server))}",
            };
            string sub = null;
            if (e.Call is ScoreCall.Points or ScoreCall.Advantage)
            {
                if (score.MatchPoint(0) || score.MatchPoint(1)) sub = TennisStrings.MatchPoint;
                else if (score.GamePoint(0) || score.GamePoint(1)) sub = TennisStrings.GamePoint;
            }
            if (_match.IsHuman(e.Team) && e.Reason == PointReason.Winner)
                AddPopup(_match.BallPosition + Vector3.up * 0.8f, _match.RallyHits == 1 && _match.LastWasServe ? TennisStrings.Ace : TennisStrings.Winner, Sun);
            ShowCall(call, sub, 1.8f, 0.5f);
            GameAudio.Sfx("tennis_call", 0.6f, vary: 0f);
            Debug.Log($"[Tennis] Point to {TeamName(e.Team)} ({e.Reason}): {score.Games(0)}-{score.Games(1)}, " +
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

        void ShowHint(int slot, string text, float seconds)
        {
            var h = _humans[Mathf.Clamp(slot, 0, 1)];
            h.Hint = text;
            h.HintUntil = Time.time + seconds;
        }

        // ---------------------------------------------------------------- drawing the match

        /// <summary>Every toy as the rules have it this frame: its place, its racket (ready, back for a coming ball, or a
        /// swing: the computer's and the players' alike), its head after the ball.</summary>
        void UpdateViews(float dt)
        {
            double now = _match.Time;
            float step = Current == Phase.Playing && _paused ? 0f : dt;
            Vector3? ball = _match.BallLive ? _match.BallPosition : (Vector3?)null;
            for (int i = 0; i < 4; i++)
            {
                var v = _views[i];
                if (v == null) continue;
                var p = _match.Players[i];
                v.LookAt = ball;
                v.Animate(p, now, step, p.RightHanded);
                // a toy setting off or pulling up kicks a little grass up behind its feet
                if (step > 0f && Current is Phase.Playing)
                {
                    float before = _lastRun[i].magnitude, speed = p.Velocity.magnitude;
                    if ((speed > 4f && before <= 4f) || (before > 5f && speed < before - 1.2f))
                        _fx.Kick(v.FeetWorld, new Vector3(p.Velocity.x, 0f, p.Velocity.y) * (speed > before ? 1f : -1f));
                }
                _lastRun[i] = p.Velocity;
            }
        }

        /// <summary>
        /// The champion screen (a tournament won): the cup spinning big in front of the camera, catching the sun, and
        /// confetti falling round it.
        /// </summary>
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

        /// <summary>
        /// The ball, its streak (longer and warmer for a quick, a perfect or a power shot), its glow and its shadow
        /// (<see cref="TennisFx"/>, with the toys' shadows: bigger and fainter the higher the ball is, so it tells how high
        /// the ball is).
        /// </summary>
        void UpdateBall()
        {
            var p = _match.BallPosition;
            bool show = Mathf.Abs(p.z) < 32f && Mathf.Abs(p.x) < 25f && Current != Phase.Waiting || !_match.BallLive;
            _ball.gameObject.SetActive(show);
            _fx.UpdateShadows(_views, show ? p : (Vector3?)null, ballSize);
            if (!show)
            {
                _fx.UpdateGlow(null, cam, ballSize, _glowColour, 0f);
                return;
            }
            if ((_ball.position - p).sqrMagnitude > 9f) _ballTrail.Clear();   // a ball that jumped (a hit found late, a practice feed): no streak
            _ball.position = p;
            var v = _match.Ball.Velocity;
            if (v.sqrMagnitude > 1f)
                _ball.rotation = Quaternion.AngleAxis(v.magnitude * 400f * Time.deltaTime, Vector3.Cross(Vector3.up, v).normalized) * _ball.rotation;
            if (!_match.BallLive) _ballTrail.Clear();   // back in the server's hand: no streak across the court
            _ballTrail.emitting = _match.BallLive && v.sqrMagnitude > 9f;
            // the streak widens with the ball's speed (a rally speeding up shows)
            _ballTrail.widthMultiplier = Mathf.Lerp(0.75f, 1.35f, Mathf.InverseLerp(10f, 28f, v.magnitude));
            _glowStrength = Mathf.MoveTowards(_glowStrength, 0.35f, Time.deltaTime * 0.6f);
            _fx.UpdateGlow(_match.BallLive ? p : (Vector3?)null, cam, ballSize, _glowColour, _glowStrength);
        }
        /// <summary>
        /// Which toys are drawn as low-detail chibis (<see cref="Chibi.Low"/>, about a third of the triangles): the far
        /// team always (~0.07-0.08 of the screen's height from Wii's camera: the detail doesn't show), and all four in the
        /// split screen (each toy is drawn twice; two players' graphics a little lighter on the TV box, 2026-11-06).
        /// PerfLab's chibi=full / chibi=low draws all four one way (measuring the difference on the box).
        /// </summary>
        void ApplyDetail()
        {
            bool split = Split;
            string force = PerfSwitches.Get("chibi");
            for (int i = 0; i < _views.Length; i++)
                if (_views[i] != null) _views[i].Low = force == "full" ? false : force == "low" || split || i >= 2;
        }

        /// <summary>The screen is split: a versus match is on (or PerfLab's split=1, measuring its cost on the box).</summary>
        bool Split => Mode == TennisMode.Versus && Current != Phase.Waiting || PerfSwitches.Get("split") == "1";

        /// <summary>
        /// Versus (once a match is on): the screen split down the middle, player 1's half on the left (where they stand),
        /// each seen from behind its own team (Wii splits the screen when players are on both teams). The second half's
        /// camera is made here, a copy of the scene's (the box's graphics settings included).
        /// </summary>
        void ApplyViewports()
        {
            bool split = Split;
            _stadium.ShowNearEnd(split);
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
            if (split)
                for (int team = 0; team < 2; team++)
                {
                    _cameraX[team] = Mathf.Clamp(_match.Back(team).Position.x * Court.RightSign(team) * 0.75f, -3.2f, 3.2f);
                    _cameraZ[team] = 0f;
                }
        }

        /// <summary>The camera view the menu chose, eased into over half a second when it changes (0 = classic, 1 = from
        /// above).</summary>
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

        /// <summary>
        /// The camera high behind the team's back player, looking down the court (<see cref="CameraViews"/>: the classic
        /// view a little steeper than Wii's, or from above; a taller, wider view for a half of the split screen), following
        /// that player sideways so they stay near the middle of the picture, and a little toward the net when they run in.
        /// </summary>
        void UpdateCamera(float dt)
        {
            float ease = 1f - Mathf.Exp(-2.5f * dt);
            _shake = Mathf.Max(0f, _shake - dt);
            var shake = (Vector3)(Random.insideUnitCircle * (0.1f * _shake));
            bool split = Split;
            if (split != (_cam2 != null && _cam2.enabled)) ApplyViewports();   // PerfLab's switch
            // PerfLab's detail=0 (players only): the stadium without its grass detail, as before 2026-11-07 (what the
            // world-space detail costs the box's GPU)
            if (PerfSwitches.Get("detail") == "0" && stadiumMaterial.IsKeywordEnabled("_WORLD_DETAIL"))
            {
                stadiumMaterial.DisableKeyword("_WORLD_DETAIL");
                stadiumMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            }
            var view = ViewNow(dt);
            for (int team = 0; team < (split ? 2 : 1); team++)
            {
                var you = _match.Back(team);
                float depth = you.Position.y * Court.Side(team);   // metres behind the net
                _cameraX[team] = Mathf.Lerp(_cameraX[team], Mathf.Clamp(you.Position.x * Court.RightSign(team) * 0.75f, -3.2f, 3.2f), ease);
                _cameraZ[team] = Mathf.Lerp(_cameraZ[team], Mathf.Clamp((TennisMatch.HomeDepth - depth) * 0.3f, -0.6f, 2.5f), ease);
                float height = split ? view.SplitHeight : view.Height, behind = split ? view.SplitBehind : view.Behind;
                var home = new Vector3(0f, height, Court.Side(team) * (Court.HalfLength + behind));
                var rotation = Quaternion.Euler(split ? view.SplitPitch : view.Pitch, team == 0 ? 0f : 180f, 0f);
                // sideways and forward in the team's own view (the far team looks the other way)
                var right = new Vector3(Court.RightSign(team), 0f, 0f);
                var forward = new Vector3(0f, 0f, -Court.Side(team));
                var c = team == 0 ? cam : _cam2;
                c.fieldOfView = split ? view.SplitFov : view.Fov;
                c.transform.SetPositionAndRotation(home + right * _cameraX[team] + forward * _cameraZ[team] + shake, rotation);
            }
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].Age += dt;
                _popups[i].World += Vector3.up * (0.7f * dt);
                if (_popups[i].Age > 1.2f) _popups.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- effects (TennisFx: particles, shadows, the glow)

        void AddPopup(Vector3 world, string text, Color color) => _popups.Add(new Popup { World = world, Text = text, Color = color });

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
                if (Mode != TennisMode.Versus) DrawMatchInfo(w);
            }

            switch (Current)
            {
                case Phase.Waiting:
                    DrawWaiting(w);
                    break;
                case Phase.Intro:
                    Label(new Rect(0, 470, w, 60), TennisStrings.BestOf3, 34, Color.white);
                    break;
                case Phase.Playing:
                    if (_paused)
                    {
                        if (Mode == TennisMode.Solo) PlayerGate.DrawPaused(w, _input);
                        else DrawTwoPaused(w);
                    }
                    else if (Mode == TennisMode.Solo && !UsingKeys) PlayerGate.DrawEdgeWarning(w, _input);
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
                    string text = Mode == TennisMode.Together ? $"{TennisStrings.Player(k)}: {h.Hint}" : h.Hint;
                    float y = Mode == TennisMode.Together ? 180f + 70f * k : 180f;
                    GameHud.Text(new Rect(x0, y, width, 70), text, GameHud.Medium, TextAnchor.MiddleCenter,
                                 Mode == TennisMode.Solo ? Color.white : Color.Lerp(Color.white, PlayerColors[k], 0.35f));
                }
        }

        IEnumerable<Camera> Cameras()
        {
            yield return cam;
            if (_cam2 != null && _cam2.enabled) yield return _cam2;
        }

        /// <summary>The skeleton mirror's corner (bottom right, while it shows): text below y 740 stays left of it.</summary>
        const float MirrorLeft = 440f, LowerY = 740f;

        /// <summary>The umpire's call, popping in, centred on <paramref name="centerX"/>.</summary>
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

        /// <summary>The game's name at the top of the waiting, bracket and results screens (a drop shadow: two draws of the
        /// big text; an outline was ten).</summary>
        static void DrawTitle(float w, string title, float y = 34f, int size = 104) =>
            Label(new Rect(0f, y, w, size * 1.15f), title, size, Sun);

        /// <summary>Text over the 3D scene with a drop shadow: two draws (SportHud's outline is five, and the box's GPU pays
        /// for every one).</summary>
        static void Label(Rect r, string text, int size, Color colour, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            SportHud.Text(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), text, size, new Color(0f, 0f, 0f, 0.65f * colour.a), anchor, 0f);
            SportHud.Text(r, text, size, colour, anchor, 0f);
        }

        /// <summary>
        /// The waiting screen: the title, the menu (<see cref="TennisMenu"/>: who, what, how hard, which view), then what
        /// the players still have to do: one player the camera's gate (step in, stand still, raise your hands) and how to
        /// play; two players a card each on their side with their status.
        /// </summary>
        void DrawWaiting(float w)
        {
            bool camera = PlayerGate.State(_input) != GateState.NoService;
            DrawTitle(w, TennisStrings.Title);
            const float menuY = 168f;
            _menu.Draw(w / 2f, menuY, camera, Cups(_menu.Mode == TennisMode.Together ? TennisMode.Together : TennisMode.Solo), _cup);
            float below = menuY + TennisMenu.Height;
            Label(new Rect(0f, below + 6f, w, 44f), TennisStrings.MenuHint, 26, new Color(1f, 1f, 1f, 0.75f));
            if (TwoWaiting)
            {
                string name = _menu.Mode == TennisMode.Together ? TennisStrings.TogetherModeSub : TennisStrings.VersusModeSub;
                Label(new Rect(0f, below + 58f, w, 56f), name, 36, Color.white);
                DrawPlayerCards(w, 590f);
                return;
            }
            // the camera's gate, moved down below the menu (its title left out: the menu's is at the top)
            var old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.Translate(new Vector3(0f, below - 230f, 0f));
            PlayerGate.DrawWaiting(w, _input, "", TennisStrings.KeyboardHelp);
            GUI.matrix = old;
            float lw = w - MirrorLeft;   // left of the skeleton mirror
            GameHud.Text(new Rect(40f, 820f, lw - 40f, 50f), TennisStrings.HowTo, GameHud.Small);
            GameHud.Text(new Rect(40f, 868f, lw - 40f, 50f), TennisStrings.AimHint, GameHud.Small, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));
            GameHud.Text(new Rect(40f, 914f, lw - 40f, 50f), TennisStrings.HandHint, GameHud.Small, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.65f));
        }

        /// <summary>
        /// Two players before a match: a card each on their side (player 1 on the left) with what they still have to do
        /// (step in, stand still, raise their hands, ready), "move apart" when they stand too close, and the countdown to
        /// playing alone when one has raised their hands and nobody else is in view.
        /// </summary>
        void DrawPlayerCards(float w, float y)
        {
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                var r = new Rect(k == 0 ? 70f : w - 630f, y, 560f, 150f);
                SportHud.Panel(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.84f), 22f, null, 0f, false);
                SportHud.Rounded(new Rect(r.x + 14f, r.y + 18f, 10f, r.height - 36f), PlayerColors[k], 5f);
                SportHud.Text(new Rect(r.x + 44f, r.y + 10f, r.width - 60f, 64f), TennisStrings.Player(k), 40, PlayerColors[k], TextAnchor.MiddleLeft, 0f);
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
                else if (!s.Calibrated) status = $"{TennisStrings.StandStill} {s.CalibrationProgress * 100f:0}%";
                else if (!s.Ready) status = TennisStrings.RaiseYourHands;
                else
                {
                    status = TennisStrings.Ready;
                    color = Sun;
                }
                SportHud.Text(new Rect(r.x + 44f, r.y + 76f, r.width - 60f, 60f), status, 32, color, TextAnchor.MiddleLeft, 0f);
            }
            var middle = new Rect(650f, y, w - 1300f, 150f);
            if (_slots.TooClose) GameHud.Banner(new Rect(middle.x, y + 40f, middle.width, 76f), TennisStrings.StepApart, Brand.Current.SecondaryColor);
            else if (_readySince >= 0f)
            {
                int waiting = _slots[0].Ready ? 1 : 0;
                int left = Mathf.Max(1, Mathf.CeilToInt(JoinWait - (Time.time - _readySince)));
                Label(new Rect(middle.x, y + 10f, middle.width, 64f), TennisStrings.WaitingFor(waiting), 34, PlayerColors[waiting]);
                Label(new Rect(middle.x, y + 80f, middle.width, 50f), TennisStrings.PlayingAloneIn(left), 28, new Color(1f, 1f, 1f, 0.8f));
            }
        }

        /// <summary>The player's team's name on the bracket.</summary>
        string YouName => Mode == TennisMode.Together ? TennisStrings.Us : TennisStrings.You;

        /// <summary>A tournament between two of its matches: the bracket so far, who's next, and hands up to go on.</summary>
        void DrawBracket(float w)
        {
            var t = _tournament;
            if (t == null) return;
            bool two = Mode == TennisMode.Together;
            DrawTitle(w, TennisStrings.TournamentOf(Difficulty), 28f, 72);
            TennisBracketView.Draw(t, new Rect(170f, 130f, w - 340f, 560f), YouName, _cup, Time.time);
            if (!t.Over)
                Label(new Rect(0f, 700f, w - MirrorLeft, 60f), TennisStrings.NextMatch(t.NameOf(t.Opponent)), 40, Color.white);
            if (_phaseTime < 1.5f) return;
            if (two) DrawReadyLine(w, 770f);
            else Label(new Rect(0f, 770f, w - MirrorLeft, 50f), PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter
                           : PlayerGate.PlacementHint(_input) ?? TennisStrings.RaiseForNext(false), 30, Sun);
        }

        /// <summary>Two players on the bracket and the results: one line saying who still has to raise their hands.</summary>
        void DrawReadyLine(float w, float y)
        {
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            string Status(int k)
            {
                var s = _slots[k];
                return !s.Present ? CommonStrings.StepIn : !s.Calibrated ? TennisStrings.StandStill : s.Ready ? TennisStrings.Ready : TennisStrings.RaiseYourHands;
            }
            Label(new Rect(0f, y, w - MirrorLeft, 50f), $"{TennisStrings.Player(0)}: {Status(0)}    {TennisStrings.Player(1)}: {Status(1)}", 30, Color.white);
        }

        /// <summary>
        /// The results: a single match's winner and score, a tournament's end (the cup, or where the player went out, with
        /// the bracket), versus each player's shots; then hands up to play the same again, or the arrows for the menu.
        /// </summary>
        void DrawResults(float w)
        {
            bool two = Mode != TennisMode.Solo, together = Mode == TennisMode.Together;
            var t = Tournament;
            if (t != null)
            {
                DrawTitle(w, t.Won ? TennisStrings.Champion(together) : TennisStrings.OutIn(t.Round, together), 26f, t.Won ? 120 : 72);
                var champion = t.Champion;
                string sub = t.Won ? TennisStrings.WonTheCup(together) + (_newCup ? $"    {TennisStrings.Cups}: {Cups(Mode)[(int)Difficulty]}" : "")
                    : champion.HasValue ? TennisStrings.TournamentWinner(t.NameOf(champion.Value)) : null;
                if (sub != null) Label(new Rect(0f, t.Won ? 166f : 150f, w, 56f), sub, 36, Color.white);
                // won: the cup itself in the middle (3D, UpdateTrophy); out: the bracket, with who went on to win it
                if (!t.Won) TennisBracketView.Draw(t, new Rect(170f, 220f, w - 340f, 480f), YouName, _cup, Time.time);
            }
            else if (Mode == TennisMode.Versus)
            {
                int winner = Mathf.Max(0, _match.Score.Winner);
                DrawTitle(w, TennisStrings.Won(winner), 120f);
                Label(new Rect(0f, 270f, w, 60f), $"{_match.Score.Games(0)} - {_match.Score.Games(1)}    {TennisStrings.LongestRally}: {_longestRally}", 40, Color.white);
                for (int k = 0; k < 2; k++)
                {
                    var s = _match.Stats[k];
                    float x = k == 0 ? w / 2f - 640f : w / 2f + 40f;
                    Label(new Rect(x, 400f, 600f, 60f), TennisStrings.Player(k), 44, PlayerColors[k]);
                    Label(new Rect(x, 466f, 600f, 50f), $"{TennisStrings.Hits}: {s.Hits}    {TennisStrings.PerfectShots}: {s.Perfect}", 32, Color.white);
                }
            }
            else
            {
                bool won = _match.Score.Winner == 0;
                string title = together ? (won ? TennisStrings.WeWon : TennisStrings.TheyWon(_match.Level.TeamName, _match.Level.Singles))
                    : won ? TennisStrings.YouWon : TennisStrings.TheyWon(_match.Level.TeamName, _match.Level.Singles);
                DrawTitle(w, title, 150f, title.Length > 20 ? 80 : 104);
                Label(new Rect(0f, 300f, w, 60f), $"{TennisStrings.SingleOf(Difficulty)}    {_match.Score.Games(0)} - {_match.Score.Games(1)}", 40, Color.white);
                int hits = _match.HumanHits, perfect = _match.HumanPerfect;
                Label(new Rect(0f, 370f, w, 50f), $"{TennisStrings.Hits}: {hits}    {TennisStrings.PerfectShots}: {perfect}    {TennisStrings.LongestRally}: {_longestRally}",
                      32, new Color(1f, 1f, 1f, 0.85f));
            }
            if (_phaseTime < 1.5f) return;
            if (two) DrawReadyLine(w, 720f);
            string again = PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter : PlayerGate.PlacementHint(_input) ?? TennisStrings.AgainOrMenu(two);
            Label(new Rect(0f, two ? 776f : t != null && t.Won ? 800f : 730f, w - MirrorLeft, 50f), again, 30, Sun);
        }

        /// <summary>A two-player match waiting for a player: come back, or stand still (their steps count from there).</summary>
        void DrawTwoPaused(float w)
        {
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                if (s.Present && s.Calibrated) continue;
                GameHud.Message(w, s.Present ? TennisStrings.HoldStillPlayer(k) : TennisStrings.ComeBackPlayer(k), null, PlayerColors[k]);
                return;
            }
        }

        /// <summary>What's being played, top right: a tournament's round, or one match, and the difficulty.</summary>
        void DrawMatchInfo(float w)
        {
            string info = Tournament != null ? $"{TennisStrings.RoundName(Tournament.Round)}  ·  {TennisStrings.Difficulty(Difficulty)}"
                : TennisStrings.SingleOf(Difficulty);
            float width = SportHud.Width(info, 28) + 48f;
            var r = new Rect(w - 30f - width, 26f, width, 52f);
            SportHud.Panel(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.72f), 26f, null, 0f, false);
            SportHud.Text(r, info, 28, Color.white, TextAnchor.MiddleCenter, 0f);
        }

        /// <summary>Both sides (a chip in each player's shirt colour: one in singles, two in doubles) with their games and
        /// points, a dot by the side serving: top left, or across the middle of a split screen.</summary>
        void DrawScoreboard(float w)
        {
            var score = _match.Score;
            const float y = 24f, rowH = 60f, width = 680f;
            float x = Split ? w / 2f - width / 2f : 30f;
            // one rounded panel (a layer less than a shadowed one: the box's GPU pays for every layer during play)
            SportHud.Panel(new Rect(x - 8f, y - 8f, width + 16f, rowH * 2f + 16f), new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.8f),
                           20f, null, 0f, false);
            for (int team = 0; team < 2; team++)
            {
                float ry = y + team * rowH;
                for (int k = 0; k < (_match.Singles ? 1 : 2); k++)
                {
                    var look = _looks[team * 2 + k];
                    var chip = look != null ? look.Color("top") : team == 0 ? Sky : TheirShirts[_match.Level.Colour];
                    SportHud.Rounded(new Rect(x + 8f + k * 16f, ry + 10f, 11f, rowH - 20f), chip, 5f);
                }
                if (score.Server == team && !score.MatchOver)
                {
                    GUI.color = new Color(0.85f, 0.95f, 0.3f);
                    GUI.DrawTexture(new Rect(x + 48f, ry + 20f, 20f, 20f), _dot);
                    GUI.color = Color.white;
                }
                GameHud.Text(new Rect(x + 78f, ry, 410f, rowH), TeamName(team), GameHud.Small, TextAnchor.MiddleLeft);
                SportHud.Rounded(new Rect(x + 500f, ry + 8f, 70f, rowH - 16f), new Color(1f, 1f, 1f, 0.14f), 10f);
                GameHud.Text(new Rect(x + 500f, ry, 70f, rowH), score.Games(team).ToString(), GameHud.Small, TextAnchor.MiddleCenter, Sun);
                GameHud.Text(new Rect(x + 580f, ry, 100f, rowH), score.PointsText(team), GameHud.Small, TextAnchor.MiddleCenter);
            }
        }
    }
}
