using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// Lunapark! (2026-11-09, the user's choice from the arcade options: "Arcade hall"): a fun fair at night with six
    /// booths, each a minute of one move the camera reads well. Godit! (moles around you in the mirror), Fruta! (slice the
    /// fruit), Muri! (make the shape of the hole in the wall), Kap! (step under the sweets with a basket), Zogu! (flap your
    /// arms to fly), Topi! (keep the ball up). After Nex Playground's Party Fowl (its #2 of 2025), WarioWare, EyeToy Play and
    /// Carnival Games (docs: the conversation of 2026-11-09).
    /// <list type="bullet">
    /// <item>The hall: ← → (or a step to a side) chooses a booth, ↑ ↓ reach the rows (one player or two; one booth or a
    /// party of five), both hands up (or OK) plays. Tickets won add up to plush toys on the shelf.</item>
    /// <item>A round: how to play, a countdown (each player's stage is fixed where they stand), a minute (40 s in a party),
    /// the result: points, stars, tickets, a new best. Hands up plays again, an arrow goes back to the hall.</item>
    /// <item>Two players: side by side in the same picture, each their own moles, fruit, basket, ball, hole or bird; the
    /// party names a winner after five rounds.</item>
    /// </list>
    /// The booths' rules are engine-free (<see cref="MachineRules"/>, tested with simulated players); this draws them
    /// (<see cref="MachineView"/>) over the camera picture with sprites from tools/arcade/art.py in a few draw calls.
    /// </summary>
    public sealed class ArcadeGame : MonoBehaviour
    {
        public enum Phase { Hall, HowTo, Countdown, Playing, RoundEnd, PartyEnd }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField, Tooltip("Tundu/Fx, alpha blended: the sprite sheet.")] Material spriteMaterial;
        [SerializeField, Tooltip("Tundu/Fx, additive: glows, sparks, trails.")] Material glowMaterial;
        [SerializeField, Tooltip("Tundu/Fx: the camera picture (its vignette).")] Material pictureMaterial;
        [SerializeField, Tooltip("Tundu/Wall: Muri!'s wall.")] Material wallMaterial;

        [Header("Art (tools/arcade/art.py)")]
        [SerializeField] TextAsset sheetJson;
        [SerializeField] Texture2D sheet;
        [SerializeField] Texture2D wallBoard;

        [Header("Flow")]
        [SerializeField] float roundSeconds = 60f;
        [SerializeField] float howToSeconds = 4.5f;
        [SerializeField] float countdownSeconds = 3f;

        public Phase Current { get; private set; } = Phase.Hall;
        /// <summary>The booth chosen in the hall.</summary>
        public Machine Selected { get; private set; }
        /// <summary>The hall's rows: two players, a party.</summary>
        public bool TwoPlayers { get; set; }
        public bool PartyMode { get; set; }
        /// <summary>The booth being played (or the last one).</summary>
        public Machine Playing { get; private set; }
        public int RoundPlayers { get; private set; } = 1;
        public MachineRules Rules => _view?.Rules;
        public MachineView View => _view;
        public ArcadeParty Party { get; private set; }
        public ArcadeProgress Progress { get; private set; } = new ArcadeProgress();
        public RoundReward LastReward { get; private set; }
        public int[] LastScores { get; private set; } = new int[2];
        public int LastRoundWinner { get; private set; } = -1;
        public ArcadeStage Stage => _stage;
        public ArcadeArt Art => _art;
        public PlayerSlots Slots => _slots;
        /// <summary>Tests: skip the how-to card.</summary>
        public bool SkipHowTo { get; set; }
        /// <summary>Every booth event as it happens (tests, logs).</summary>
        public event Action<ArcadeEvent> Events;

        const string ProgressKey = "arcade.progress", SelectedKey = "arcade.selected", PlayersKey = "arcade.players", PartyKey = "arcade.party";
        const float JoinWait = 4f;
        static readonly PlayArea HallArea = new PlayArea(BodyPart.Hips, side: 0.7f);
        static readonly PlayArea TwoArea = new PlayArea(BodyPart.Hips, side: 0.5f);

        ArcadeArt _art;
        SpriteBatch _back, _glow, _front;
        ArcadeFx _fx;
        readonly ArcadeStage _stage = new ArcadeStage();
        MachineView _view;
        HallView _hall;
        CameraFeed _feed;
        Material _picture, _spriteMat, _glowMat, _frontMat;
        Transform _pictureQuad;
        Texture2D _backdrop;
        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        float _mirrorCorner;
        readonly PlayerSlots _slots = new PlayerSlots { Area = TwoArea };
        float _phaseTime, _readySince = -1f, _guiWidth = 1920f;
        int _menuRow, _previousLane;
        bool _paused;
        string _toast;
        float _toastUntil;
        int _prizeShown = -1;
        bool _ticketSound;
        readonly Body[] _bodies = new Body[2];
        readonly KeyBody _keys = new KeyBody();

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null)
            {
                _mirrorWasVisible = _mirror.Visible;
                _mirrorCorner = _mirror.CornerSize;
                _mirror.CornerSize = 0.2f;
            }
            _art = ArcadeArt.Load(sheetJson, sheet, wallBoard);
            _spriteMat = new Material(spriteMaterial) { renderQueue = 3000 };
            _spriteMat.SetTexture("_BaseMap", sheet);
            _glowMat = new Material(glowMaterial) { renderQueue = 3010 };
            _glowMat.SetTexture("_BaseMap", sheet);
            _frontMat = new Material(spriteMaterial) { renderQueue = 3020 };
            _frontMat.SetTexture("_BaseMap", sheet);
            _back = new SpriteBatch("Sprites", _spriteMat, transform, 0f);
            _glow = new SpriteBatch("Glow", _glowMat, transform, -0.1f);
            _front = new SpriteBatch("HUD sprites", _frontMat, transform, -0.2f);
            _fx = new ArcadeFx(_art);
            _hall = new HallView(_art);
            _picture = new Material(pictureMaterial) { renderQueue = 2990 };
            _picture.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _picture.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            _picture.EnableKeyword("_VIGNETTE");
            _picture.SetColor("_VignetteColor", new Color(16f / 255f, 18f / 255f, 48f / 255f, 1f));
            _backdrop = MakeBackdrop();
            _pictureQuad = MakePictureQuad();
            _feed = CameraFeed.Request();
            Progress = ArcadeProgress.Load(PlayerPrefs.GetString(ProgressKey, ""));
            Selected = (Machine)Mathf.Clamp(PlayerPrefs.GetInt(SelectedKey, 0), 0, 5);
            TwoPlayers = PlayerPrefs.GetInt(PlayersKey, 0) == 1;
            PartyMode = PlayerPrefs.GetInt(PartyKey, 0) == 1;
            Debug.Log($"[Arcade] Ready: {_art.Count} sprites, tickets {Progress.Tickets}, prizes {Progress.PrizesWon}");
            EnterHall();
        }

        void OnDestroy()
        {
            _view?.Dispose();
            if (_feed != null) CameraFeed.Release();
            _feed = null;
            if (_mirror != null)
            {
                _mirror.Visible = _mirrorWasVisible;
                _mirror.CornerSize = _mirrorCorner;
            }
            foreach (var m in new[] { _picture, _spriteMat, _glowMat, _frontMat })
                if (m != null) Destroy(m);
            if (_backdrop != null) Destroy(_backdrop);
        }

        Transform MakePictureQuad()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Camera picture";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _picture;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // the vignette reads the quad's own 0..1 coordinates from uv1
            var mesh = Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            mesh.SetUVs(1, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            return go.transform;
        }

        static Texture2D MakeBackdrop()
        {
            // no camera picture yet: a dim room-like gradient
            var t = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Arcade backdrop" };
            for (int y = 0; y < 64; y++)
            {
                float k = y / 63f;
                var c = Color.Lerp(new Color(0.12f, 0.1f, 0.22f), new Color(0.3f, 0.24f, 0.42f), k);
                for (int x = 0; x < 4; x++) t.SetPixel(x, y, c);
            }
            t.Apply();
            return t;
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (Current != Phase.Hall && RemoteInput.Back)
            {
                GameAudio.Sfx("ui_back", vary: 0f);
                EnterHall();
                return;
            }
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            _guiWidth = Screen.width / (Screen.height / ArcadeStage.HudHeight);
            _stage.GuiWidth = _guiWidth;
            FitCamera();
            var tracker = MotionSystem.Instance != null ? MotionSystem.Instance.Tracker : null;
            bool inRound = Current is Phase.HowTo or Phase.Countdown or Phase.Playing or Phase.RoundEnd;
            _slots.Locked = inRound && RoundPlayers == 2;
            if (tracker != null) _slots.Update(tracker.Bodies);
            AskForPeople();
            GatherBodies();

            switch (Current)
            {
                case Phase.Hall:
                    UpdateHall();
                    break;
                case Phase.HowTo:
                    FollowPicture(dt);
                    if (_phaseTime >= howToSeconds || SkipHowTo || _phaseTime > 1f && StartPressed()) EnterCountdown();
                    break;
                case Phase.Countdown:
                    TrackBodies(dt);
                    if (Mathf.FloorToInt(_phaseTime) != Mathf.FloorToInt(_phaseTime - dt) && _phaseTime < countdownSeconds) GameAudio.Sfx("count", vary: 0f);
                    if (_phaseTime >= countdownSeconds)
                    {
                        GameAudio.Sfx("go", vary: 0f);
                        SetPhase(Phase.Playing);
                    }
                    break;
                case Phase.Playing:
                    TrackBodies(dt);
                    _paused = Paused();
                    if (!_paused) StepRules(dt);
                    if (Rules.Finished) EndRound();
                    break;
                case Phase.RoundEnd:
                    UpdateRoundEnd();
                    break;
                case Phase.PartyEnd:
                    if (_phaseTime > 2f && StartPressed()) StartParty();
                    else if (_phaseTime > 1f && (RemoteInput.Left || RemoteInput.Right || RemoteInput.Up || RemoteInput.Down)) EnterHall();
                    break;
            }
            if (_mirror != null)
                _mirror.Visible = _mirrorWasVisible && (Current is Phase.Hall or Phase.RoundEnd or Phase.PartyEnd || _paused ||
                                                        Current == Phase.Playing && _view != null && !_view.UsesPicture);
            Render(dt);
        }

        void FitCamera()
        {
            cam.orthographic = true;
            cam.orthographicSize = ArcadeStage.HudHeight / 2f;
            cam.transform.position = new Vector3(_guiWidth * 0.5f, ArcadeStage.HudHeight / 2f, -10f);
            _pictureQuad.position = new Vector3(_guiWidth * 0.5f, ArcadeStage.HudHeight / 2f, 5f);
            _pictureQuad.localScale = new Vector3(_guiWidth, ArcadeStage.HudHeight, 1f);
        }

        bool UsingKeys => RoundPlayers == 1 && (_input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService);

        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        /// <summary>The box's pose AI: two people while two play or two are chosen, else one.</summary>
        void AskForPeople()
        {
            bool two = Current == Phase.Hall ? TwoPlayers : RoundPlayers == 2;
            if (!two)
            {
                var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
                AndroidMotionService.SetPeople(1, keep: body != null ? body.ServiceTrack : 0);
            }
            else AndroidMotionService.SetPeople(2, BoxGraphics.Current.TwoPlayerRuns);
        }

        void GatherBodies()
        {
            if (RoundPlayers == 2 || Current == Phase.Hall && TwoPlayers)
            {
                _bodies[0] = _slots[0].Body;
                _bodies[1] = _slots[1].Body;
            }
            else
            {
                _bodies[0] = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
                _bodies[1] = null;
            }
        }

        bool Paused()
        {
            if (UsingKeys) return false;
            if (RoundPlayers == 1) return PlayerGate.ShouldPause(_input);
            for (int p = 0; p < 2; p++)
                if (!_slots[p].Present) return true;
            return false;
        }

        // ---------------------------------------------------------------- the hall

        void EnterHall()
        {
            _view?.Dispose();
            _view = null;
            Party = null;
            RoundPlayers = 1;
            _fx.Clear();
            PlayerGate.Use(_input, HallArea);
            _previousLane = _input != null ? _input.Lane : 0;
            _slots.ClearReady();
            _readySince = -1f;
            GameAudio.PlayMusic("arcade");
            SetPhase(Phase.Hall);
        }

        void UpdateHall()
        {
            int lane = _input != null ? _input.Lane : 0;
            if (lane != _previousLane)
            {
                if (lane != 0 && !TwoPlayers && _input != null && !_input.UsingKeyboard) Choose((int)Selected + lane);
                _previousLane = lane;
            }
            if (RemoteInput.Up) MoveRow(-1);
            if (RemoteInput.Down) MoveRow(1);
            int step = RemoteInput.Right ? 1 : RemoteInput.Left ? -1 : 0;
            if (step != 0)
            {
                if (_menuRow == 0) Choose((int)Selected + step);
                else if (_menuRow == 1)
                {
                    bool camera = PlayerGate.State(_input) != GateState.NoService;
                    if (camera || TwoPlayers)
                    {
                        TwoPlayers = !TwoPlayers;
                        PlayerPrefs.SetInt(PlayersKey, TwoPlayers ? 1 : 0);
                        GameAudio.Sfx("ui_select", 0.5f);
                        _slots.ClearReady();
                    }
                }
                else
                {
                    PartyMode = !PartyMode;
                    PlayerPrefs.SetInt(PartyKey, PartyMode ? 1 : 0);
                    GameAudio.Sfx("ui_select", 0.5f);
                }
            }
            if (ReadyToStart(out bool alone))
            {
                if (alone) TwoPlayers = false;
                if (PartyMode) StartParty();
                else StartRound(Selected, TwoPlayers ? 2 : 1);
            }
        }

        void MoveRow(int d)
        {
            int next = Mathf.Clamp(_menuRow + d, 0, 2);
            if (next != _menuRow) GameAudio.Sfx("ui_move", 0.5f);
            _menuRow = next;
        }

        void Choose(int index)
        {
            int next = (index % 6 + 6) % 6;
            if (next == (int)Selected) return;
            Selected = (Machine)next;
            PlayerPrefs.SetInt(SelectedKey, next);
            GameAudio.Sfx("ui_move");
        }

        bool ReadyToStart(out bool alone)
        {
            alone = false;
            if (!TwoPlayers)
            {
                _readySince = -1f;
                _slots.ClearReady();
                return StartPressed();
            }
            if (_slots[0].Ready && _slots[1].Ready || RemoteInput.Confirm && _slots.BothCanPlay) return true;
            int ready = _slots[0].Ready ? 0 : _slots[1].Ready ? 1 : -1;
            if (ready < 0 || _slots.Present >= 2)
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

        // ---------------------------------------------------------------- rounds

        void StartParty()
        {
            Party = new ArcadeParty(TwoPlayers ? 2 : 1, UnityEngine.Random.Range(0, 1 << 30));
            Debug.Log($"[Arcade] Party for {Party.Players}: {string.Join(", ", Party.Order)}");
            StartRound(Party.Current, Party.Players);
        }

        /// <summary>Plays booth <paramref name="m"/> (tests start one directly).</summary>
        public void StartRound(Machine m, int players = 1)
        {
            _view?.Dispose();
            Playing = m;
            RoundPlayers = Mathf.Clamp(players, 1, 2);
            if (_input != null && PlayerGate.State(_input) != GateState.NoService) _input.PreferBody();
            float seconds = Party != null ? ArcadeParty.RoundSeconds : roundSeconds;
            int seed = UnityEngine.Random.Range(1, 1 << 30);
            bool two = RoundPlayers == 2;
            MachineRules rules = m switch
            {
                Machine.Whack => new WhackRules(RoundPlayers, seconds, seed, two ? 0.8f : 1f),
                Machine.Slice => new SliceRules(RoundPlayers, seconds, seed, two ? 0.65f : 1f),
                Machine.Wall => new WallRules(RoundPlayers, seconds, seed, steps: !two),
                Machine.Catch => new CatchRules(RoundPlayers, seconds, seed, two ? 1.35f : 2.25f),
                Machine.Flap => new FlapRules(RoundPlayers, seconds, seed),
                _ => new KeepUpRules(RoundPlayers, seconds, seed, two ? 1.6f : 2.7f),
            };
            _view = rules switch
            {
                WhackRules r => new WhackView(r, _art, _stage, _fx),
                SliceRules r => new SliceView(r, _art, _stage, _fx),
                WallRules r => new WallView(r, _art, _stage, _fx, wallMaterial, transform),
                CatchRules r => new CatchView(r, _art, _stage, _fx),
                FlapRules r => new FlapView(r, _art, _stage, _fx),
                KeepUpRules r => new KeepUpView(r, _art, _stage, _fx),
                _ => null,
            };
            _fx.Clear();
            _keys.Reset();
            if (RoundPlayers == 1) PlayerGate.Use(_input, AreaFor(m));
            _slots.Area = TwoArea;
            _slots.ClearReady();
            _prizeShown = -1;
            GameAudio.PlayMusic("arcade_play");
            GameAudio.Sfx("ui_select", vary: 0f);
            Debug.Log($"[Arcade] {m} for {RoundPlayers} player(s), {seconds:0} s{(Party != null ? $", party round {Party.Round + 1}" : "")}");
            SetPhase(SkipHowTo ? Phase.Countdown : Phase.HowTo);
            if (SkipHowTo) LockStage();
        }

        /// <summary>Where a booth needs the player (one player; two only need room beside each other).</summary>
        static PlayArea AreaFor(Machine m) => m switch
        {
            Machine.Whack => new PlayArea(BodyPart.Hips, side: 1.4f, above: 1.0f),
            Machine.Slice => new PlayArea(BodyPart.Hips, side: 1.4f, above: 0.8f),
            Machine.Wall => new PlayArea(BodyPart.Knees, side: 1.2f, above: 0.8f),
            Machine.Catch => new PlayArea(BodyPart.Hips, side: 1.6f, above: 0.4f),
            Machine.Flap => new PlayArea(BodyPart.Hips, side: 1.0f, above: 0.6f),
            _ => new PlayArea(BodyPart.Knees, side: 1.5f, above: 0.8f),
        };

        /// <summary>The framing a booth needs around each stage: torso lengths above and below the hips, to each side.</summary>
        static (float above, float below, float side) FrameFor(Machine m) => m switch
        {
            Machine.Whack => (2.85f, 0.7f, 2.0f),
            Machine.Slice => (2.75f, 1.0f, 2.4f),
            Machine.Wall => (3.0f, 2.15f, 2.4f),
            Machine.Catch => (3.0f, 1.0f, 2.6f),
            Machine.Flap => (2.6f, 1.0f, 2.0f),
            _ => (3.45f, 1.95f, 2.8f),
        };

        void EnterCountdown()
        {
            LockStage();
            SetPhase(Phase.Countdown);
        }

        void LockStage()
        {
            float imageAspect = _bodies[0] != null ? _bodies[0].Aspect : _feed != null && _feed.HasFrame ? _feed.Aspect : 16f / 9f;
            var (above, below, side) = FrameFor(Playing);
            _stage.Lock(_bodies, RoundPlayers, imageAspect, _guiWidth / ArcadeStage.HudHeight, above, below, side);
            if (RoundPlayers == 1) _input?.Gestures?.Recenter();
            else _slots.Recenter();
            Debug.Log($"[Arcade] Stage: crop {_stage.Crop}, torso {_stage.Torso[0]:0.000}{(RoundPlayers == 2 ? $" / {_stage.Torso[1]:0.000}" : "")} " +
                      $"({_stage.UnitPx(0):0} px)");
        }

        void FollowPicture(float dt)
        {
            float imageAspect = _bodies[0] != null ? _bodies[0].Aspect : _feed != null && _feed.HasFrame ? _feed.Aspect : 16f / 9f;
            _stage.Follow(_bodies, RoundPlayers, imageAspect, _guiWidth / ArcadeStage.HudHeight, dt);
        }

        void TrackBodies(float dt)
        {
            double now = Time.unscaledTimeAsDouble;
            if (UsingKeys)
            {
                _keys.Update(this, dt);
                _stage.Bodies[0].CopyFrom(_keys.Body);
                return;
            }
            for (int p = 0; p < RoundPlayers; p++) _stage.Track(p, _bodies[p], now, dt);
        }

        void StepRules(float dt)
        {
            var rules = Rules;
            rules.Events.Clear();
            if (UsingKeys && rules is FlapRules flap && _keys.Flapped) flap.Flap(0);
            rules.Step(dt, _stage.Bodies);
            foreach (var e in rules.Events)
            {
                _view.OnEvent(e);
                Events?.Invoke(e);
            }
            rules.Events.Clear();
        }

        void EndRound()
        {
            var rules = Rules;
            for (int p = 0; p < 2; p++) LastScores[p] = p < rules.Players ? rules.Score[p] : 0;
            int best = RoundPlayers == 2 ? Mathf.Max(LastScores[0], LastScores[1]) : LastScores[0];
            LastReward = Progress.Record(Playing, best, RoundPlayers == 1 && Party == null);
            LastRoundWinner = Party != null ? Party.Record(LastScores) : RoundPlayers == 2 ? (LastScores[0] == LastScores[1] ? -1 : LastScores[0] > LastScores[1] ? 0 : 1) : -1;
            PlayerPrefs.SetString(ProgressKey, Progress.Save());
            PlayerPrefs.Save();
            bool good = LastReward.Stars >= 2 || LastReward.NewBest;
            GameAudio.Jingle(good ? "jingle_win" : "jingle_gameover");
            _ticketSound = LastReward.Tickets > 0;
            if (good) _fx.ConfettiRain(_guiWidth, 70);
            if (LastReward.Prizes.Count > 0)
            {
                _prizeShown = LastReward.Prizes[LastReward.Prizes.Count - 1];
                _toast = ArcadeStrings.WonPrize(_prizeShown);
                _toastUntil = Time.time + 5f;
                GameAudio.Sfx("dress_surprise", 0.9f);
            }
            string scores = RoundPlayers == 2 ? LastScores[0] + " / " + LastScores[1] : LastScores[0].ToString();
            Debug.Log($"[Arcade] {Playing} over: {scores} points, {LastReward.Stars} stars, " +
                      $"+{LastReward.Tickets} tickets ({Progress.Tickets}){(LastReward.NewBest ? ", a new best" : "")}" +
                      (LastReward.Prizes.Count > 0 ? $", won plush {string.Join(",", LastReward.Prizes)}" : "") +
                      (Party != null ? $"; party round {Party.Round}/{ArcadeParty.Rounds}, wins {string.Join("-", Party.Wins)}" : ""));
            SetPhase(Phase.RoundEnd);
        }

        void UpdateRoundEnd()
        {
            // the ticket machine, once the jingle has had its moment
            if (_ticketSound && _phaseTime > 0.9f)
            {
                _ticketSound = false;
                GameAudio.Sfx("arcade_ticket", 0.8f, vary: 0f);
            }
            if (Party != null)
            {
                if (_phaseTime < 2f) return;
                if (_phaseTime > 6f || StartPressed())
                {
                    if (Party.Over) EnterPartyEnd();
                    else StartRound(Party.Current, Party.Players);
                }
                return;
            }
            if (_phaseTime < 1.5f) return;
            if (RemoteInput.Left || RemoteInput.Right || RemoteInput.Up || RemoteInput.Down)
            {
                EnterHall();
                return;
            }
            if (StartPressed()) StartRound(Playing, RoundPlayers);
        }

        void EnterPartyEnd()
        {
            if (Party.Players == 1 && Party.Total[0] > 0 && Progress.RecordParty(Party.Total[0]))
            {
                PlayerPrefs.SetString(ProgressKey, Progress.Save());
                PlayerPrefs.Save();
            }
            _fx.ConfettiRain(_guiWidth, 120);
            GameAudio.Jingle("jingle_win");
            Debug.Log($"[Arcade] Party over: totals {string.Join(" / ", Party.Total)}, wins {string.Join("-", Party.Wins)}, winner {Party.Winner}");
            SetPhase(Phase.PartyEnd);
        }

        /// <summary>What hands up (or OK) does on the screen showing (tests).</summary>
        public void Continue()
        {
            switch (Current)
            {
                case Phase.Hall:
                    if (PartyMode) StartParty();
                    else StartRound(Selected, TwoPlayers ? 2 : 1);
                    break;
                case Phase.HowTo:
                    EnterCountdown();
                    break;
                case Phase.RoundEnd:
                    if (Party != null)
                    {
                        if (Party.Over) EnterPartyEnd();
                        else StartRound(Party.Current, Party.Players);
                    }
                    else StartRound(Playing, RoundPlayers);
                    break;
                case Phase.PartyEnd:
                    StartParty();
                    break;
            }
        }

        /// <summary>Tests: ends the booth now (its minute is up).</summary>
        public void FinishRound()
        {
            if (Current != Phase.Playing) return;
            Rules.Step(Rules.Duration, _stage.Bodies);
            Rules.Events.Clear();
            EndRound();
        }

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            _paused = false;
            if (phase is Phase.Hall or Phase.RoundEnd or Phase.PartyEnd) _slots.ClearReady();
            _readySince = -1f;
            Debug.Log($"[Arcade] {phase}");
        }

        // ---------------------------------------------------------------- drawing

        void Render(float dt)
        {
            _back.Clear();
            _glow.Clear();
            _front.Clear();
            bool picture = Current is Phase.HowTo or Phase.Countdown or Phase.Playing or Phase.RoundEnd && _view != null && _view.UsesPicture;
            _pictureQuad.gameObject.SetActive(picture);
            if (picture) UpdatePicture();
            float time = Time.time;
            if (Current == Phase.Hall || Current == Phase.PartyEnd || _view == null)
                _hall.Draw(_back, _glow, _guiWidth, time, dt, (int)Selected, _menuRow == 0, Progress);
            else
            {
                if (Current != Phase.HowTo) _view.Draw(_back, _glow, time, dt);
                DrawBulbFrame(time);
            }
            _fx.Step(dt);
            _fx.Draw(_back, _glow);
            DrawHudSprites(time);
            _back.Apply();
            _glow.Apply();
            _front.Apply();
        }

        void UpdatePicture()
        {
            bool live = _feed != null && _feed.IsLive;
            var crop = _stage.Crop;
            if (live)
            {
                float aspect = _bodies[0] != null ? _bodies[0].Aspect : _feed.Aspect;
                _picture.SetTexture("_BaseMap", _feed.Texture);
                _picture.SetTextureScale("_BaseMap", new Vector2(-crop.width / aspect, crop.height));
                _picture.SetTextureOffset("_BaseMap", new Vector2(1f - crop.xMin / aspect, crop.yMin));
            }
            else
            {
                _picture.SetTexture("_BaseMap", _backdrop);
                _picture.SetTextureScale("_BaseMap", Vector2.one);
                _picture.SetTextureOffset("_BaseMap", Vector2.zero);
            }
        }

        /// <summary>Light bulbs all round the screen, chasing: the booth's frame.</summary>
        void DrawBulbFrame(float time)
        {
            float w = _guiWidth, h = ArcadeStage.HudHeight;
            int across = 26, down = 14, n = 0;
            for (int k = 0; k <= across; k++)
            {
                Bulb(new Vector2(w * k / across, 14f), time, n++);
                Bulb(new Vector2(w * k / across, h - 14f), time, n++);
            }
            for (int k = 1; k < down; k++)
            {
                Bulb(new Vector2(14f, h * k / down), time, n++);
                Bulb(new Vector2(w - 14f, h * k / down), time, n++);
            }
        }

        void Bulb(Vector2 at, float time, int k)
        {
            bool on = ((int)(time * 6f) + k / 2) % 3 != 0;
            if (on) _glow.Draw(_art["glow"], at, 18f, 0f, new Color(1f, 0.82f, 0.4f, 0.55f));
            _back.Draw(_art[on ? "bulb_on" : "bulb_off"], at, 13f);
        }

        /// <summary>The HUD's panels and icons (the words are IMGUI on top, in OnGUI).</summary>
        void DrawHudSprites(float time)
        {
            var panel = _art["panel"];
            var night = new Color(0.08f, 0.1f, 0.22f, 0.88f);
            float w = _guiWidth;
            switch (Current)
            {
                case Phase.Hall:
                {
                    var menu = MenuRect();
                    _front.Nine(panel, Flip(menu), 36f, night);
                    float rowH = menu.height / 2f;
                    if (_menuRow > 0) _front.Nine(panel, Flip(new Rect(menu.x + 10f, menu.y + 8f + (_menuRow - 1) * rowH, menu.width - 20f, rowH - 16f)), 26f, new Color(1f, 1f, 1f, 0.12f));
                    for (int row = 0; row < 2; row++)
                        for (int k = 0; k < 2; k++)
                        {
                            bool on = row == 0 ? TwoPlayers == (k == 1) : PartyMode == (k == 1);
                            if (on) _front.Nine(panel, Flip(OptionRect(menu, row, k)), 26f, MachineView.Sun);
                        }
                    // the tickets above the shelf
                    var shelf = _hall.Shelf;
                    _front.Draw(_art["ticket"], new Vector2(shelf.xMin + 40f, shelf.yMax + 46f), 22f);
                    break;
                }
                case Phase.HowTo:
                {
                    var card = HowToRect();
                    var colour = HallView.BoothColours[(int)Playing];
                    _front.Nine(panel, Flip(card), 50f, Color.white);
                    _front.Nine(panel, Flip(new Rect(card.x + 14f, card.y + 14f, card.width - 28f, 130f)), 40f, colour);
                    _front.Draw(_art[HallView.Icons[(int)Playing]], new Vector2(card.x + 220f, 1080f - (card.y + 295f)), 95f);
                    // how long until the countdown (raised hands skip it)
                    var track = new Rect(card.x + 90f, card.yMax - 58f, card.width - 180f, 18f);
                    _front.Nine(panel, Flip(track), 9f, new Color(0.08f, 0.1f, 0.22f, 0.1f));
                    float fill = Mathf.Clamp01(_phaseTime / howToSeconds);
                    _front.Nine(panel, Flip(new Rect(track.x, track.y, Mathf.Max(track.height, track.width * fill), track.height)), 9f, colour);
                    break;
                }
                case Phase.Playing:
                case Phase.Countdown:
                {
                    _front.Nine(panel, Flip(new Rect(w / 2f - 95f, 26f, 190f, 92f)), 36f, night);
                    for (int p = 0; p < RoundPlayers; p++)
                    {
                        var r = ScoreRect(p);
                        _front.Nine(panel, Flip(r), 36f, night);
                        _front.Nine(panel, Flip(new Rect(p == 0 ? r.x + 12f : r.xMax - 22f, r.y + 14f, 10f, r.height - 28f)), 5f,
                                    RoundPlayers == 2 ? MachineView.PlayerColour(p) : HallView.BoothColours[(int)Playing]);
                    }
                    if (RoundPlayers == 1 && Progress.Best[(int)Playing] >= 0) _front.Nine(panel, Flip(BestRect()), 30f, night);
                    break;
                }
                case Phase.RoundEnd:
                {
                    var card = ResultsRect();
                    _front.Nine(panel, Flip(card), 50f, Color.white);
                    _front.Nine(panel, Flip(new Rect(card.x + 14f, card.y + 14f, card.width - 28f, 110f)), 40f, HallView.BoothColours[(int)Playing]);
                    float pop = Mathf.Clamp01((_phaseTime - 0.3f) / 0.25f);
                    for (int s = 0; s < 3; s++)
                    {
                        bool earned = s < LastReward.Stars;
                        float delay = 0.4f + s * 0.25f;
                        float k = earned ? Mathf.Clamp01((_phaseTime - delay) / 0.2f) : pop;
                        float bounce = earned && _phaseTime > delay ? 1f + 0.3f * Mathf.Exp(-(_phaseTime - delay) * 6f) * Mathf.Cos((_phaseTime - delay) * 14f) : 1f;
                        var at = new Vector2(w / 2f + (s - 1) * 150f, 1080f - (card.y + 330f) + (s == 1 ? 24f : 0f));
                        _front.Draw(_art[earned ? "star" : "star_white"], at, 62f * Mathf.Max(0.05f, k) * bounce, 0f,
                                    earned ? Color.white : new Color(0.1f, 0.1f, 0.25f, 0.16f));
                    }
                    _front.Draw(_art["ticket"], new Vector2(TicketLineX(w) + TicketIcon / 2f, 1080f - (card.y + 470f)), 30f);
                    if (_prizeShown >= 0)
                    {
                        float k = Mathf.Clamp01((_phaseTime - 1.2f) / 0.3f);
                        var at = new Vector2(card.xMax + 150f, 1080f - (card.y + 280f));
                        _glow.Draw(_art["glow"], at, 140f * k, 0f, new Color(1f, 0.9f, 0.5f, 0.6f));
                        _front.Draw(_art["plush" + _prizeShown], at, 95f * Mathf.Max(0.05f, k), Mathf.Sin(time * 3f) * 8f);
                    }
                    break;
                }
                case Phase.PartyEnd:
                {
                    var card = PartyEndRect();
                    _front.Nine(panel, Flip(card), 50f, Color.white);
                    _front.Nine(panel, Flip(new Rect(card.x + 14f, card.y + 14f, card.width - 28f, 110f)), 40f, MachineView.Grape);
                    // the five rounds: each booth's icon (its points under it are the words')
                    for (int i = 0; i < Party.Scores.Count && i < Party.Order.Length; i++)
                    {
                        var at = PartyRoundAt(card, i);
                        _front.Draw(_art[HallView.Icons[(int)Party.Order[i]]], new Vector2(at.x, 1080f - at.y), 38f);
                    }
                    break;
                }
            }
        }

        /// <summary>An IMGUI rectangle (y down) as a world rectangle (y up).</summary>
        static Rect Flip(Rect gui) => new Rect(gui.x, ArcadeStage.HudHeight - gui.yMax, gui.width, gui.height);

        Rect MenuRect() => new Rect(_guiWidth / 2f - 430f, 168f, 860f, 128f);

        static Rect OptionRect(Rect menu, int row, int k)
        {
            float rowH = menu.height / 2f, x0 = menu.x + 250f, span = menu.width - 280f;
            float w = (span - 16f) / 2f;
            return new Rect(x0 + k * (w + 16f), menu.y + row * rowH + 10f, w, rowH - 20f);
        }

        Rect ScoreRect(int p) => p == 0 ? new Rect(36f, 26f, 380f, 92f) : new Rect(_guiWidth - 416f, 26f, 380f, 92f);
        Rect BestRect() => new Rect(_guiWidth - 356f, 26f, 320f, 72f);
        Rect ResultsRect() => new Rect(_guiWidth / 2f - 620f, 160f, 1240f, 640f);
        Rect HowToRect() => new Rect(_guiWidth / 2f - 640f, 230f, 1280f, 560f);
        Rect PartyEndRect() => new Rect(_guiWidth / 2f - 640f, 200f, 1280f, 600f);

        /// <summary>The centre (IMGUI, y down) of round <paramref name="i"/>'s icon on the party's results.</summary>
        static Vector2 PartyRoundAt(Rect card, int i) => new Vector2(card.x + 190f + i * (card.width - 380f) / (ArcadeParty.Rounds - 1), card.y + 455f);

        /// <summary>The results' tickets line, the icon and its words centred together (the words measured by IMGUI).</summary>
        const float TicketIcon = 96f, TicketGap = 16f;
        float _ticketTextWidth = 330f;
        float TicketLineX(float w) => w / 2f - (TicketIcon + TicketGap + _ticketTextWidth) / 2f;

        // ---------------------------------------------------------------- IMGUI (the words)

        void OnGUI()
        {
            float w = GameHud.Begin();
            switch (Current)
            {
                case Phase.Hall:
                    DrawHallText(w);
                    break;
                case Phase.HowTo:
                    DrawHowTo(w);
                    break;
                case Phase.Countdown:
                    DrawPlayingText(w);
                    int n = Mathf.CeilToInt(countdownSeconds - _phaseTime);
                    SportHud.Banner(w, 470f, n > 0 ? n.ToString() : CommonStrings.Go, n > 0 ? SportHud.Sky : SportHud.Leaf, _phaseTime - Mathf.Floor(_phaseTime), 1f, 180);
                    break;
                case Phase.Playing:
                    DrawPlayingText(w);
                    if (_paused)
                    {
                        if (RoundPlayers == 1) PlayerGate.DrawPaused(w, _input);
                        else
                            for (int p = 0; p < 2; p++)
                                if (!_slots[p].Present) SportHud.Text(new Rect(0f, 500f + p * 70f, w, 60f), ArcadeStrings.ComeBack(p), 44, Color.white);
                    }
                    else if (RoundPlayers == 1 && !UsingKeys) PlayerGate.DrawEdgeWarning(w, _input);
                    break;
                case Phase.RoundEnd:
                    DrawResults(w);
                    break;
                case Phase.PartyEnd:
                    DrawPartyEnd(w);
                    break;
            }
            if (!string.IsNullOrEmpty(_toast) && Time.time < _toastUntil && Current != Phase.RoundEnd)
                SportHud.Pill(w / 2f, 960f, _toast, SportHud.Sun, 34);
        }

        void DrawHallText(float w)
        {
            SportHud.Text(new Rect(0f, 30f, w, 130f), ArcadeStrings.Title, 112, SportHud.Sun, TextAnchor.MiddleCenter, 5f);
            var menu = MenuRect();
            float rowH = menu.height / 2f;
            bool camera = PlayerGate.State(_input) != GateState.NoService;
            for (int row = 0; row < 2; row++)
            {
                var label = new Rect(menu.x + 34f, menu.y + row * rowH, 220f, rowH);
                Label(label, row == 0 ? ArcadeStrings.PlayersRow : ArcadeStrings.GameRow, 30, _menuRow == row + 1 ? SportHud.Sun : new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
                for (int k = 0; k < 2; k++)
                {
                    bool on = row == 0 ? TwoPlayers == (k == 1) : PartyMode == (k == 1);
                    string text = row == 0 ? (k == 0 ? ArcadeStrings.OnePlayer : ArcadeStrings.TwoPlayers) : (k == 0 ? ArcadeStrings.OneGame : ArcadeStrings.Party);
                    bool usable = row == 1 || k == 0 || camera;
                    Label(OptionRect(menu, row, k), text, 26, on ? SportHud.Night : new Color(1f, 1f, 1f, usable ? 0.8f : 0.35f), TextAnchor.MiddleCenter);
                }
            }
            // the booths' names and bests
            for (int i = 0; i < 6; i++)
            {
                var b = _hall.Booths[i];
                var r = new Rect(b.x, ArcadeStage.HudHeight - b.y - 125f, b.width, 50f);
                Label(r, ArcadeStrings.Name((Machine)i), b.width > 240f ? 38 : 32, i == (int)Selected ? SportHud.Night : new Color(0.08f, 0.1f, 0.22f, 0.85f), TextAnchor.MiddleCenter);
                int best = Progress.Best[i];
                if (best >= 0) Label(new Rect(b.x, ArcadeStage.HudHeight - b.y - 79f, b.width, 30f), ArcadeStrings.Best(best), 20, new Color(0.08f, 0.1f, 0.22f, 0.6f), TextAnchor.MiddleCenter, false);
            }
            // tickets and the next plush, above the shelf
            var shelf = _hall.Shelf;
            float ty = ArcadeStage.HudHeight - shelf.yMax - 71f;
            SportHud.Text(new Rect(shelf.xMin + 76f, ty, 120f, 50f), Progress.Tickets.ToString(), 40, SportHud.Sun, TextAnchor.MiddleLeft, 3f);
            Label(new Rect(shelf.xMin + 168f, ty, shelf.width - 168f, 50f), Progress.ToNextPrize > 0 ? ArcadeStrings.ToNextPrize(Progress.ToNextPrize) : ArcadeStrings.AllPrizes,
                  22, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft, false);
            string hint = TwoPlayers
                ? !camera ? ArcadeStrings.ChooseKeys : _slots.Present < 2 ? ArcadeStrings.BothStepIn : _slots.TooClose ? ArcadeStrings.StandApart : ArcadeStrings.BothRaise
                : PlayerGate.State(_input) switch
                {
                    GateState.NoService => ArcadeStrings.ChooseKeys,
                    GateState.NoPlayer => CommonStrings.StepIn,
                    GateState.Placing => PlayerGate.PlacementHint(_input),
                    GateState.Calibrating => CommonStrings.HoldStill,
                    _ => PlayerGate.PlacementHint(_input) ?? ArcadeStrings.ChooseHint,
                };
            SportHud.Text(new Rect(0f, 1010f, w, 56f), hint, 32, Color.white, TextAnchor.MiddleCenter, 3f);
            if (TwoPlayers && camera)
                for (int p = 0; p < 2; p++)
                {
                    var s = _slots[p];
                    string state = !s.Present ? "—" : s.Ready ? "✔" : s.Calibrated ? "↑↑" : "…";
                    SportHud.Text(new Rect(p == 0 ? 40f : w - 340f, 330f, 300f, 50f), $"{ArcadeStrings.Player(p)} {state}", 30, MachineView.PlayerColour(p),
                                  p == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, 3f);
                }
        }

        void DrawHowTo(float w)
        {
            var card = HowToRect();
            SportHud.Text(new Rect(card.x, card.y + 14f, card.width, 130f), ArcadeStrings.Name(Playing).ToUpperInvariant(), 84, Color.white, TextAnchor.MiddleCenter, 4f);
            if (Party != null)
                Label(new Rect(card.xMax - 380f, card.y + 14f, 330f, 130f), ArcadeStrings.GameOf(Party.Round + 1), 30, new Color(1f, 1f, 1f, 0.9f), TextAnchor.MiddleRight);
            var s = SportHud.Style(42, false);
            s.wordWrap = true;
            s.alignment = TextAnchor.MiddleLeft;
            s.normal.textColor = SportHud.Night;
            GUI.Label(new Rect(card.x + 400f, card.y + 165f, card.width - 460f, 260f), ArcadeStrings.HowTo(Playing), s);
            // over the bar that runs out: what starts it sooner (or the keys)
            string under = UsingKeys ? ArcadeStrings.KeyHelp(Playing) : RoundPlayers == 2 ? ArcadeStrings.BothRaise : CommonStrings.RaiseHands;
            Label(new Rect(card.x, card.yMax - 118f, card.width, 50f), under, 30, new Color(0.08f, 0.1f, 0.22f, 0.65f), TextAnchor.MiddleCenter, false);
        }

        void DrawPlayingText(float w)
        {
            var rules = Rules;
            if (rules == null) return;
            int left = Mathf.CeilToInt(rules.Left);
            Label(new Rect(w / 2f - 95f, 22f, 190f, 64f), left.ToString(), 54, left <= 5 && Current == Phase.Playing ? SportHud.Coral : Color.white, TextAnchor.MiddleCenter);
            Label(new Rect(w / 2f - 95f, 76f, 190f, 34f), ArcadeStrings.Time, 20, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter, false);
            for (int p = 0; p < RoundPlayers; p++)
            {
                var r = ScoreRect(p);
                string label = RoundPlayers == 2 ? ArcadeStrings.Player(p).ToUpperInvariant() : ArcadeStrings.Points;
                var anchor = p == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                float inset = 40f;
                Label(new Rect(r.x + inset, r.y + 4f, r.width - inset * 2f, 36f), label, 22, new Color(1f, 1f, 1f, 0.65f), anchor, false);
                Label(new Rect(r.x + inset, r.y + 30f, r.width - inset * 2f, 60f), rules.Score[p].ToString(), 50, Color.white, anchor);
                int streak = Streak(rules, p);
                if (streak >= 3)
                    SportHud.Text(new Rect(r.x + inset, r.yMax + 2f, r.width - inset * 2f, 44f), ArcadeStrings.Streak(streak), 30, SportHud.Sun, anchor, 2f);
            }
            if (RoundPlayers == 1 && Progress.Best[(int)Playing] >= 0)
            {
                var b = BestRect();
                Label(b, ArcadeStrings.Best(Progress.Best[(int)Playing]), 28, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter);
            }
            if (Current == Phase.Playing && _phaseTime < 5f || Current == Phase.Countdown)
                SportHud.Pill(w / 2f, 960f, UsingKeys ? ArcadeStrings.KeyHelp(Playing) : ArcadeStrings.Hint(Playing), Color.white, 34);
        }

        static int Streak(MachineRules rules, int p) => rules switch
        {
            WhackRules r => r.Boards[p].Combo,
            SliceRules r => r.Boards[p].Streak,
            WallRules r => r.Boards[p].Streak,
            CatchRules r => r.Boards[p].Combo,
            KeepUpRules r => r.Balls[p].Streak,
            _ => 0,
        };

        void DrawResults(float w)
        {
            var card = ResultsRect();
            SportHud.Text(new Rect(card.x, card.y + 14f, card.width, 110f), ArcadeStrings.Name(Playing).ToUpperInvariant(), 64, Color.white, TextAnchor.MiddleCenter, 3f);
            if (RoundPlayers == 1)
                Label(new Rect(card.x, card.y + 140f, card.width, 110f), LastScores[0].ToString(), 96, SportHud.Night, TextAnchor.MiddleCenter);
            else
                for (int p = 0; p < 2; p++)
                {
                    var r = new Rect(card.x + (p == 0 ? 60f : card.width / 2f), card.y + 140f, card.width / 2f - 60f, 110f);
                    Label(r, $"{ArcadeStrings.Player(p)}: {LastScores[p]}", 52, LastRoundWinner == p ? MachineView.PlayerColour(p) : SportHud.Night, TextAnchor.MiddleCenter);
                }
            Label(new Rect(card.x, card.y + 245f, card.width, 40f), ArcadeStrings.Points, 24, new Color(0.08f, 0.1f, 0.22f, 0.55f), TextAnchor.MiddleCenter, false);
            string tickets = $"{ArcadeStrings.TicketsWon(LastReward.Tickets)}  ·  {ArcadeStrings.TicketsInAll(Progress.Tickets)}";
            _ticketTextWidth = SportHud.Style(30, true).CalcSize(new GUIContent(tickets)).x;
            Label(new Rect(TicketLineX(w) + TicketIcon + TicketGap, card.y + 445f, _ticketTextWidth + 20f, 50f), tickets, 30, SportHud.Night, TextAnchor.MiddleLeft);
            if (LastReward.NewBest) SportHud.Pill(w / 2f, card.y + 520f, CommonStrings.NewBest, SportHud.Sun, 32);
            else if (RoundPlayers == 2 || Party != null)
                Label(new Rect(card.x, card.y + 515f, card.width, 50f),
                      LastRoundWinner >= 0 ? ArcadeStrings.RoundWinner(LastRoundWinner) : RoundPlayers == 2 ? ArcadeStrings.Draw : ArcadeStrings.GameOf(Party.Round),
                      36, LastRoundWinner >= 0 ? MachineView.PlayerColour(LastRoundWinner) : SportHud.Night, TextAnchor.MiddleCenter);
            if (_prizeShown >= 0 && _phaseTime > 1.3f)
                SportHud.Text(new Rect(card.xMax - 40f, card.y + 400f, 380f, 80f), ArcadeStrings.WonPrize(_prizeShown), 28, SportHud.Sun, TextAnchor.MiddleCenter, 3f);
            string next = Party != null
                ? Party.Over ? ArcadeStrings.GameOf(ArcadeParty.Rounds) : ArcadeStrings.NextGame(Party.Current)
                : UsingKeys ? ArcadeStrings.AgainKeys : PlayerGate.PlacementHint(_input) ?? ArcadeStrings.AgainOrBack;
            if (_phaseTime > 1.5f) SportHud.Text(new Rect(0f, card.yMax + 26f, w, 56f), next, 34, Color.white, TextAnchor.MiddleCenter, 3f);
        }

        void DrawPartyEnd(float w)
        {
            var card = PartyEndRect();
            SportHud.Text(new Rect(card.x, card.y + 14f, card.width, 110f), ArcadeStrings.Party, 60, Color.white, TextAnchor.MiddleCenter, 3f);
            if (Party.Players == 1)
            {
                Label(new Rect(card.x, card.y + 135f, card.width, 120f), Party.Total[0].ToString(), 110, SportHud.Night, TextAnchor.MiddleCenter);
                Label(new Rect(card.x, card.y + 250f, card.width, 50f), ArcadeStrings.PartyTotal(Party.Total[0]), 32, new Color(0.08f, 0.1f, 0.22f, 0.7f), TextAnchor.MiddleCenter, false);
                if (Party.Total[0] > 0 && Progress.PartyBest == Party.Total[0]) SportHud.Pill(w / 2f, card.y + 315f, CommonStrings.NewBest, SportHud.Sun, 34);
            }
            else
            {
                int winner = Party.Winner;
                Label(new Rect(card.x, card.y + 135f, card.width, 100f), winner >= 0 ? ArcadeStrings.PartyWinner(winner) : ArcadeStrings.Draw, 58,
                      winner >= 0 ? MachineView.PlayerColour(winner) : SportHud.Night, TextAnchor.MiddleCenter);
                for (int p = 0; p < 2; p++)
                    Label(new Rect(card.x + (p == 0 ? 80f : card.width / 2f), card.y + 240f, card.width / 2f - 80f, 120f),
                          $"{ArcadeStrings.Player(p)}\n{ArcadeStrings.Wins(Party.Wins[p])} · {Party.Total[p]}", 36, MachineView.PlayerColour(p), TextAnchor.MiddleCenter);
            }
            // each round's points under its booth's icon (two players: in the round winner's colour)
            for (int i = 0; i < Party.Scores.Count && i < Party.Order.Length; i++)
            {
                var at = PartyRoundAt(card, i);
                var sc = Party.Scores[i];
                bool two = Party.Players == 2;
                string points = two ? $"{sc[0]} : {sc[1]}" : sc[0].ToString();
                var colour = !two || sc[0] == sc[1] ? SportHud.Night : MachineView.PlayerColour(sc[0] > sc[1] ? 0 : 1);
                Label(new Rect(at.x - 130f, at.y + 46f, 260f, 46f), points, 32, colour, TextAnchor.MiddleCenter);
            }
            if (_phaseTime > 2f) SportHud.Text(new Rect(0f, card.yMax + 26f, w, 56f), UsingKeys ? ArcadeStrings.AgainKeys : ArcadeStrings.AgainOrBack, 34, Color.white, TextAnchor.MiddleCenter, 3f);
        }

        /// <summary>A label without an outline (one draw: the box pays for every copy of a text).</summary>
        static void Label(Rect r, string text, int size, Color colour, TextAnchor anchor, bool bold = true)
        {
            var s = SportHud.Style(size, bold);
            s.alignment = anchor;
            s.normal.textColor = colour;
            GUI.Label(r, text, s);
        }

        // ---------------------------------------------------------------- the keys (no camera)

        /// <summary>A body moved by the keys, for playing without a camera (development, the remote).</summary>
        sealed class KeyBody
        {
            public readonly StageBody Body = StageBody.Standing();
            public bool Flapped;
            float _x, _handFor;
            int _hole = -1;
            float _swipe = -1f;
            Vector2 _swipeFrom, _swipeTo;

            public void Reset()
            {
                _x = 0f;
                _hole = -1;
                _swipe = -1f;
                Body.SetStanding(Vector2.zero);
            }

            public void Update(ArcadeGame game, float dt)
            {
                var kb = Keyboard.current;
                Flapped = false;
                float move = 0f;
                if (kb != null) move = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                _x = Mathf.Clamp(_x + move * 3f * dt, -2.2f, 2.2f);
                Body.SetStanding(new Vector2(_x, 0f));
                bool space = kb != null && kb.spaceKey.wasPressedThisFrame || RemoteInput.Confirm;
                switch (game.Rules)
                {
                    case WhackRules whack:
                        if (kb != null)
                        {
                            Key[] keys = { Key.Q, Key.W, Key.E, Key.A, Key.D, Key.Z, Key.C };
                            for (int i = 0; i < keys.Length; i++)
                                if (kb[keys[i]].wasPressedThisFrame)
                                {
                                    _hole = i;
                                    _handFor = 0.3f;
                                }
                        }
                        _handFor -= dt;
                        if (_hole >= 0 && _handFor > 0f)
                        {
                            var at = whack.HoleAt(_hole);
                            Body[at.x < 0f || at.x == 0f ? Part.LeftHand : Part.RightHand] = at;
                        }
                        break;
                    case SliceRules slice:
                        if (space)
                        {
                            SliceRules.Fruit nearest = null;
                            foreach (var f in slice.Boards[0].Fruit)
                                if (!f.Sliced && f.Kind != SliceRules.FruitKind.Bomb && (nearest == null || f.Pos.y > nearest.Pos.y)) nearest = f;
                            if (nearest != null)
                            {
                                _swipe = 0f;
                                _swipeFrom = nearest.Pos + new Vector2(-0.8f, 0.1f);
                                _swipeTo = nearest.Pos + new Vector2(0.8f, -0.1f) + nearest.Vel * 0.15f;
                            }
                        }
                        if (_swipe >= 0f)
                        {
                            _swipe += dt;
                            Body[Part.RightHand] = Vector2.Lerp(_swipeFrom, _swipeTo, Mathf.Clamp01(_swipe / 0.15f));
                            if (_swipe > 0.2f) _swipe = -1f;
                        }
                        break;
                    case WallRules wall:
                        var coming = wall.Coming;
                        if (coming != null && kb != null && kb.spaceKey.isPressed)
                        {
                            var pose = coming.Pose;
                            var hips = new Vector2(pose.Step, pose.Squat ? -0.45f : 0f);
                            Body.SetStanding(hips);
                            Arm(Part.LeftShoulder, Part.LeftElbow, Part.LeftHand, true, pose.LeftUpper, pose.LeftFore);
                            Arm(Part.RightShoulder, Part.RightElbow, Part.RightHand, false, pose.RightUpper, pose.RightFore);
                            if (pose.Legs)
                            {
                                Body[Part.LeftKnee] = Body[Part.LeftHip] + Geo.Limb(true, pose.LeftThigh) * 0.92f;
                                Body[Part.RightKnee] = Body[Part.RightHip] + Geo.Limb(false, pose.RightThigh) * 0.92f;
                            }
                        }
                        break;
                    case FlapRules _:
                        Flapped = space;
                        break;
                }
            }

            void Arm(Part shoulder, Part elbow, Part hand, bool left, float upper, float fore)
            {
                Body[elbow] = Body[shoulder] + Geo.Limb(left, upper) * 0.585f;
                Body[hand] = Body[elbow] + Geo.Limb(left, fore) * 0.66f;
            }
        }
    }
}
