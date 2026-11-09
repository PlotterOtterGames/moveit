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

namespace MoveIt.Games.Kart
{
    /// <summary>
    /// "Gara!": kart racing for one or two players with an invisible steering wheel (2026-11-08, user: "a car game: see how
    /// Nex Playground, Wii, Switch and Kinect did theirs, choose the best gameplay and implement it, as polished as Tenis!
    /// and Volejboll!"). The rules are <see cref="KartRace"/> (no scene); this draws them: a track built in code
    /// (<see cref="KartWorld"/>, art/kart's scenery), six karts with the chibis at the wheel (<see cref="KartView"/>),
    /// shadows, sparks, flames and splashes (<see cref="KartFx"/>), a chase camera, the HUD, engines and sounds.
    /// <list type="bullet">
    /// <item><b>The moves</b> (<see cref="KartMoves"/>, one reader per player, on the camera's own samples): both hands out
    /// like on a wheel and turned steer (the lean steers with the hands down); a jump (or the wheel lifted quickly) is a
    /// trick at a ramp, a hop elsewhere, a quick start at "go"; both hands up use the gift box's item.</item>
    /// <item><b>The menu</b> (<see cref="KartMenu"/>): one player, two against each other (split screen) or two together in
    /// one kart; one race or a cup of three (points per place, the podium and the cup, cups counted per difficulty); easy,
    /// medium or hard; the track; behind the kart or higher.</item>
    /// <item>Keys without a camera: ← → steer, space jumps, ↑ / Enter use the item.</item>
    /// </list>
    /// </summary>
    public sealed class KartGame : MonoBehaviour
    {
        public enum Phase { Waiting, Intro, Racing, Finish, Standings, Results }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] Light sun;
        [SerializeField, Tooltip("Tundu/Sky: each track's daylight colours it.")] Material skyMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: sparks, stars, confetti.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the soft dot: dust, puffs, splashes.")] Material dotMaterial;
        [SerializeField, Tooltip("Tundu/Fx, alpha: the sun's shadows (the atlas is drawn in code).")] Material shadowMaterial;
        [SerializeField, Tooltip("Tundu/Toon with vertex colours and gloss: the cup, the paint bombs.")] Material actorMaterial;
        [SerializeField] KartWorldArt world = new KartWorldArt();
        [SerializeField] KartModels models = new KartModels();
        [Header("Race")]
        [SerializeField] int laps = 3;
        [SerializeField] float finishSeconds = 6f;

        [Serializable]
        public struct CameraView
        {
            public float Height, Behind, Ahead, Fov, SplitFov;
        }

        /// <summary>Behind the kart (low, the road rushing at you), or higher (more of the road ahead).</summary>
        public static readonly CameraView[] CameraViews =
        {
            new CameraView { Height = 2.6f, Behind = 6.2f, Ahead = 7f, Fov = 60f, SplitFov = 66f },
            new CameraView { Height = 6.5f, Behind = 9.5f, Ahead = 9f, Fov = 58f, SplitFov = 64f },
        };

        const string CupsKey = "kart.cups";
        public static readonly Color[] PlayerColors = { new Color(0.91f, 0.25f, 0.23f), new Color(0.19f, 0.78f, 0.96f) };
        public const float JoinWait = 4f;
        static readonly Color Sun = SportHud.Sun, Coral = SportHud.Coral;

        public Phase Current { get; private set; }
        public KartRace Race => _race;
        public KartCup Cup => Format == KartFormat.Cup ? _cup : null;
        public KartMode Mode { get; private set; } = KartMode.Solo;
        public KartFormat Format { get; private set; }
        public KartDifficulty Difficulty { get; private set; }
        public KartMenu Menu => _menu;
        public KartMode NextMode { get => _menu.Mode; set => _menu.Mode = value; }
        public PlayerSlots Slots => _slots;
        public IReadOnlyList<KartView> Views => _views;
        public int TrackIndex { get; private set; } = -1;
        public KartWorld World => _world;
        public event Action<KartEvent> RaceEvents;
        /// <summary>Every move the camera saw while racing (tests): the player, the move, its race time.</summary>
        public event Action<int, KartMoveEvent, double> MovesSeen;
        public Camera SplitCamera => _cam2 != null && _cam2.enabled ? _cam2 : null;
        /// <summary>Tests: the computer quiet (no items, no pads), the race never paused.</summary>
        public bool Practice { get; set; }
        /// <summary>Tests: laps a race (3).</summary>
        public int Laps { get => laps; set => laps = Mathf.Max(1, value); }
        /// <summary>Player <paramref name="slot"/>'s camera reader (tests: the wheel, the jumps).</summary>
        public KartMoves MovesOf(int slot) => _humans[Mathf.Clamp(slot, 0, 1)].Moves;

        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        KartRace _race;
        KartCup _cup;
        KartWorld _world;
        readonly List<KartView> _views = new List<KartView>();
        readonly List<string> _viewKeys = new List<string>();
        readonly KartMenu _menu = new KartMenu();
        KartFx _fx;
        Camera _cam2;
        float _phaseTime;
        bool _paused, _newCup;
        float _readySince = -1f;
        readonly PlayerSlots _slots = new PlayerSlots { Area = TwoArea };
        int[] _drivers;
        Transform _trophy;
        Mesh _cupMesh;
        float _confettiAt;
        Texture2D _shadowAtlas, _cupIcon, _dot, _wheelTex, _minimap, _padTex;
        Rect _minimapBounds;
        readonly Texture2D[] _itemIcons = new Texture2D[5];
        readonly Texture2D[] _figures = new Texture2D[4];
        Material _shadows, _pads;
        AudioSource[] _engines;
        AudioSource _drift;
        readonly float[] _shake = new float[2];
        readonly float[] _camYaw = new float[2];
        readonly Vector3[] _camPos = new Vector3[2];
        float _fovKick;
        float _keySteer;
        string _call, _callSub;
        float _callUntil, _callFrom;
        readonly List<Popup> _popups = new List<Popup>();

        sealed class Popup { public Vector3 World; public string Text; public Color Color; public float Age; public int Slot; }

        /// <summary>One player's input: their moves, what the HUD shows of them, their hints.</summary>
        sealed class Human
        {
            public readonly KartMoves Moves = new KartMoves();
            public readonly List<KartMoveEvent> Seen = new List<KartMoveEvent>();
            public double LastSample;
            public Body Body;
            public float Wheel;            // the wheel the HUD draws (degrees, eased)
            public float NoWheelFor;       // seconds without both hands on the wheel
            public string Hint;
            public float HintUntil;
            public int HintedWheel, HintedJump, HintedItem;
            public float PlaceFlash;
            public int LastPlace;

            public void Reset()
            {
                Moves.Reset();
                Seen.Clear();
                LastSample = 0;
                Wheel = 0f;
                NoWheelFor = 0f;
                Hint = null;
                HintedWheel = HintedJump = HintedItem = 0;
                PlaceFlash = 0f;
            }
        }
        readonly Human[] _humans = { new Human(), new Human() };

        /// <summary>The wheel needs the hips in the picture (standing or sitting on the couch) and room above the head for
        /// both hands up.</summary>
        static readonly PlayArea Area = new PlayArea(BodyPart.Hips, side: 0.3f, above: 0.35f);
        static readonly PlayArea TwoArea = new PlayArea(BodyPart.Hips, side: 0.3f, above: 0.35f);

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            PlayerGate.Use(_input, Area);
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null) _mirrorWasVisible = _mirror.Visible;
            _menu.Load();
            if (sun != null) sun.shadows = LightShadows.None;
            _dot = TennisArt.Dot();
            _cupIcon = TennisArt.Cup();
            _shadowAtlas = TennisArt.ShadowAtlas();
            _shadows = new Material(shadowMaterial) { name = "Shadows (atlas)" };
            _shadows.SetTexture("_BaseMap", _shadowAtlas);
            _wheelTex = KartArt.Wheel();
            for (int i = 1; i < 5; i++) _itemIcons[i] = KartArt.ItemIcon((KartItem)i);
            for (int i = 0; i < 4; i++) _figures[i] = KartArt.MoveFigure((KartArt.Figure)i);
            if (world.pads != null)
            {
                _padTex = KartArt.PadArrows();
                _pads = new Material(world.pads) { name = "Pads (arrows)" };
                _pads.SetTexture("_BaseMap", _padTex);
                _pads.SetColor("_BaseColor", new Color(1f, 0.75f, 0.25f, 0.9f));
                world.pads = _pads;
            }
            _fx = KartFx.Create(_shadows, sparkMaterial, dotMaterial, actorMaterial, sun != null ? sun.transform.forward : new Vector3(-0.3f, -0.77f, 0.56f));
            _cupMesh = TennisArt.CupMesh();
            _trophy = new GameObject("Trophy").transform;
            _trophy.gameObject.AddComponent<MeshFilter>().sharedMesh = _cupMesh;
            var tr = _trophy.gameObject.AddComponent<MeshRenderer>();
            tr.sharedMaterial = actorMaterial;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            _trophy.gameObject.SetActive(false);
            BuildAudio();
            Difficulty = _menu.Difficulty;
            _drivers = KartCup.DrawDrivers(5, Random.Range(0, 1 << 30));
            int track = _menu.Track == KartMenu.RandomTrack ? Random.Range(0, KartTracks.Count) : _menu.Track;
            ShowGrid(track);
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("kart");
        }

        void OnDestroy()
        {
            foreach (var t in new[] { _dot, _cupIcon, _shadowAtlas, _wheelTex, _minimap, _padTex })
                if (t != null) Destroy(t);
            foreach (var t in _itemIcons) if (t != null) Destroy(t);
            foreach (var t in _figures) if (t != null) Destroy(t);
            if (_cupMesh != null) Destroy(_cupMesh);
            if (_shadows != null) Destroy(_shadows);
            if (_pads != null) Destroy(_pads);
            if (world.water != null) world.water.SetFloat("_FlowOffset", 0f);
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
        }

        void BuildAudio()
        {
            var clip = Resources.Load<AudioClip>("Sfx/kart_engine");
            _engines = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.clip = clip;
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.volume = 0f;
                if (clip != null) s.Play();
                _engines[i] = s;
            }
            _drift = gameObject.AddComponent<AudioSource>();
            _drift.clip = Resources.Load<AudioClip>("Sfx/kart_drift");
            _drift.loop = true;
            _drift.playOnAwake = false;
            _drift.spatialBlend = 0f;
            _drift.volume = 0f;
            if (_drift.clip != null) _drift.Play();
        }

        // ---------------------------------------------------------------- tracks and races

        /// <summary>Builds <paramref name="track"/>'s world (the old one goes) and its daylight.</summary>
        void SetTrack(int track)
        {
            track = ((track % KartTracks.Count) + KartTracks.Count) % KartTracks.Count;
            if (TrackIndex == track && _world != null) return;
            if (_world != null) Destroy(_world.gameObject);
            TrackIndex = track;
            var t = KartTracks.Get(track);
            _world = KartWorld.Build(t, world, null, track + 1);
            var kit = new ActivityKit { Cam = cam, Sun = sun, Sky = skyMaterial, Water = world.water };
            DaylightOf(t.Theme).Apply(kit);
            if (sun != null) sun.shadows = LightShadows.None;
            if (cam != null) cam.farClipPlane = 900f;
            if (_minimap != null) Destroy(_minimap);
            _minimap = KartArt.Minimap(t, 256, out _minimapBounds);
            Debug.Log($"[Kart] Track {t.Id}: {t.Length:0} m, tightest corner {t.TightestRadius:0} m, {t.Features.Count} features");
        }

        static Daylight DaylightOf(KartTheme theme) => theme switch
        {
            KartTheme.Coast => new Daylight
            {
                SkyTop = Hex(0x1F7FE6), SkyHorizon = Hex(0xCBEFFA), Ground = Hex(0x7FC9E0), Fog = Hex(0xD7F1FA),
                AmbientSky = Hex(0x9ED2F2), AmbientEquator = Hex(0xE0D3B8), AmbientGround = Hex(0xC8B58C),
                Sun = Hex(0xFFEBC2), SunAngles = new Vector3(42f, 205f, 0f), SunIntensity = 1.35f, FogStart = 180f, FogEnd = 850f, Clouds = 0.42f,
            },
            KartTheme.Village => new Daylight
            {
                SkyTop = Hex(0x3A86E3), SkyHorizon = Hex(0xD6ECF8), Ground = Hex(0xA3C0CC), Fog = Hex(0xD3EAF6),
                AmbientSky = Hex(0xA3C6EE), AmbientEquator = Hex(0xC8C8CC), AmbientGround = Hex(0x7E8A64),
                Sun = Hex(0xFFE8C8), SunAngles = new Vector3(38f, -40f, 0f), SunIntensity = 1.35f, FogStart = 160f, FogEnd = 750f, Clouds = 0.5f,
            },
            _ => new Daylight
            {
                SkyTop = Hex(0x3C8AE6), SkyHorizon = Hex(0xFBE6CC), Ground = Hex(0xC9B49A), Fog = Hex(0xF3E0C8),
                AmbientSky = Hex(0xA9C8EE), AmbientEquator = Hex(0xE3D0B4), AmbientGround = Hex(0x7E8A58),
                Sun = Hex(0xFFE0B8), SunAngles = new Vector3(36f, 140f, 0f), SunIntensity = 1.4f, FogStart = 150f, FogEnd = 650f, Clouds = 0.38f,
            },
        };

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        /// <summary>The waiting screen: the track with the karts on the grid.</summary>
        void ShowGrid(int track)
        {
            Mode = _menu.Mode;
            SetTrack(track);
            NewRace(KartLevel.For(_menu.Difficulty, 0), track);
        }

        int SoloSeed => Random.Range(0, 1 << 30);

        /// <summary>A fresh race on <paramref name="track"/>: the karts dressed for it (kept while the racers stay).</summary>
        void NewRace(KartLevel level, int track)
        {
            if (_race != null) _race.Events -= OnRaceEvent;
            var drivers = new List<int>(_drivers ?? KartCup.DrawDrivers(5, 1));
            int ai = Mode == KartMode.Versus ? 4 : 5;
            while (drivers.Count > ai) drivers.RemoveAt(drivers.Count - 1);
            _race = new KartRace(KartTracks.Get(track), level, Mode, laps, drivers, SoloSeed) { QuietComputer = Practice };
            _race.Events += OnRaceEvent;
            int count = _race.Karts.Length;
            while (_views.Count < count)
            {
                _views.Add(null);
                _viewKeys.Add(null);
            }
            for (int i = 0; i < _views.Count; i++)
            {
                if (i >= count)
                {
                    if (_views[i] != null) _views[i].gameObject.SetActive(false);
                    continue;
                }
                var k = _race.Karts[i];
                ChibiLook driver, passenger = null;
                KartPaint paint;
                if (k.IsHuman)
                {
                    driver = k.Controller == 1 ? KartLooks.Player2() : KartLooks.Player();
                    paint = k.Controller == 1 ? KartLooks.Player2Paint : KartLooks.PlayerPaint;
                    if (Mode == KartMode.Together) passenger = KartLooks.Player2();
                }
                else
                {
                    driver = KartLooks.Driver(k.Driver);
                    paint = KartLooks.DriverPaint(k.Driver);
                }
                string key = $"{driver.Name}#{(k.IsHuman ? "p" + k.Controller : "d" + k.Driver)}{(passenger != null ? "+2" : "")}";
                if (_views[i] == null || _viewKeys[i] != key)
                {
                    if (_views[i] != null) Destroy(_views[i].gameObject);
                    _views[i] = KartView.Create(k.IsHuman ? $"Player kart {k.Controller}" : $"Kart {k.Name}", models, paint, driver, passenger, null);
                    _viewKeys[i] = key;
                }
                _views[i].gameObject.SetActive(true);
            }
            ApplyDetail();
            foreach (var h in _humans) h.Reset();
            Debug.Log($"[Kart] {(Cup != null ? KartStrings.RaceOfCup(_cup.Race) : "Race")} {Mode} {Difficulty} on {KartTracks.Get(track).Id} " +
                      $"(strength {level.Strength:0.0}, assist {level.Assist:0.00}){(Practice ? " (practice)" : "")}");
        }

        /// <summary>The players' karts in full detail, the computer's in low (small on the screen; in the split screen
        /// everything is drawn twice).</summary>
        void ApplyDetail()
        {
            string force = PerfSwitches.Get("chibi");
            for (int i = 0; i < _views.Count; i++)
            {
                if (_views[i] == null) continue;
                bool human = _race != null && i < _race.Karts.Length && _race.Karts[i].IsHuman;
                _views[i].Low = force == "full" ? false : force == "low" || !human || Split;
            }
        }

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            if (phase is Phase.Waiting or Phase.Results or Phase.Standings) _slots.ClearReady();
            _readySince = -1f;
            ApplyViewports();
            Debug.Log($"[Kart] {phase}");
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            var tracker = MotionSystem.Instance != null ? MotionSystem.Instance.Tracker : null;
            _slots.Locked = Current is Phase.Intro or Phase.Racing or Phase.Finish or Phase.Standings && Mode != KartMode.Solo;
            if (tracker != null) _slots.Update(tracker.Bodies);
            AskForPeople();
            ReadBodies();

            switch (Current)
            {
                case Phase.Waiting:
                    if (HandleMenu()) break;
                    if (ReadyToGo(_menu.Mode, true, out bool alone)) StartRun(alone ? KartMode.Solo : _menu.Mode);
                    break;
                case Phase.Intro:
                    Drive(dt);
                    if (_race.Phase != KartPhase.Countdown) SetPhase(Phase.Racing);
                    break;
                case Phase.Racing:
                    _paused = Mode == KartMode.Solo ? !UsingKeys && !Practice && PlayerGate.ShouldPause(_input) : TwoPlayersPaused();
                    if (!_paused) Drive(dt);
                    if (HumansFinished()) SetPhase(Phase.Finish);
                    break;
                case Phase.Finish:
                    _race.Step(dt);
                    if (_race.Phase == KartPhase.Over || _phaseTime > finishSeconds + 8f) EndRace();
                    break;
                case Phase.Standings:
                    if (_phaseTime > 2f && ReadyToGo(Mode, false, out _)) StartRace(_cup.Level, _cup.Track);
                    break;
                case Phase.Results:
                    if (_phaseTime < 1.5f) break;
                    if (RemoteInput.Up || RemoteInput.Down || RemoteInput.Left || RemoteInput.Right)
                    {
                        ShowGrid(TrackIndex);
                        SetPhase(Phase.Waiting);
                        break;
                    }
                    if (ReadyToGo(Mode, true, out bool again)) StartRun(again ? KartMode.Solo : Mode);
                    break;
            }

            if (_mirror != null) _mirror.Visible = _mirrorWasVisible && (Current is Phase.Waiting or Phase.Results or Phase.Standings || _paused);
            if (world.water != null) world.water.SetFloat("_FlowOffset", Time.time * 0.25f);
            _world?.Animate(_race, Time.time);
            UpdateGantry();
            UpdateViews(dt);
            _fx.UpdateFrame(_race, _views);
            UpdateTrophy();
            UpdateCamera(dt);
            UpdateAudio(dt);
        }

        bool UsingKeys => Mode == KartMode.Solo && (_input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService);

        bool Split => Mode == KartMode.Versus && Current != Phase.Waiting || PerfSwitches.Get("split") == "1";

        bool HumansFinished()
        {
            foreach (var k in _race.Karts)
                if (k.IsHuman && !k.Finished) return false;
            return true;
        }

        /// <summary>The race's step with the players' wheels: each one's reader (or the keys).</summary>
        void Drive(float dt)
        {
            if (UsingKeys) Keys(dt);
            else
                for (int k = 0; k < _race.Humans; k++)
                {
                    var h = _humans[k];
                    _race.Steer(k, h.Moves.Steer, h.Moves.Holding);
                }
            _race.Step(dt);
        }

        /// <summary>Keys and the remote without a camera: ← → steer (held), space jumps, ↑ / Enter use the item.</summary>
        void Keys(float dt)
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool left = (kb != null && (kb.leftArrowKey.isPressed || kb.aKey.isPressed)) || (pad != null && pad.dpad.left.isPressed);
            bool right = (kb != null && (kb.rightArrowKey.isPressed || kb.dKey.isPressed)) || (pad != null && pad.dpad.right.isPressed);
            float want = (right ? 1f : 0f) - (left ? 1f : 0f);
            _keySteer = Mathf.MoveTowards(_keySteer, want, dt * (want == 0f ? 5f : 3f));
            _race.Steer(0, _keySteer, true);
            _humans[0].Wheel = _keySteer * 70f;
            if (kb != null && kb.spaceKey.wasPressedThisFrame) _race.Hop(0, _race.Time);
            if (RemoteInput.Confirm || RemoteInput.Up) _race.UseItem(0, _race.Time);
        }

        /// <summary>The box's pose AI: two people while two play or a two-player game is chosen, else one.</summary>
        void AskForPeople()
        {
            bool one = (Current == Phase.Waiting ? _menu.Mode : Mode) == KartMode.Solo;
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
            var before = (_menu.Mode, _menu.Track, _menu.Difficulty);
            if (_menu.Handle(up, down, left, right, camera, out bool moved))
            {
                GameAudio.Sfx("ui_select", 0.4f);
                _readySince = -1f;
                _slots.ClearReady();
                Difficulty = _menu.Difficulty;
                // the grid shows the chosen track and players
                if (before.Item1 != _menu.Mode || before.Item2 != _menu.Track && _menu.Track != KartMenu.RandomTrack)
                    ShowGrid(_menu.Track == KartMenu.RandomTrack ? TrackIndex : _menu.Track);
                ApplyViewports();
            }
            else if (moved) GameAudio.Sfx("ui_move", 0.5f);
            if (_input != null && camera) _input.PreferBody();
            return true;
        }

        bool ReadyToGo(KartMode mode, bool allowAlone, out bool alone)
        {
            alone = false;
            if (mode == KartMode.Solo)
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
                case Phase.Standings:
                    StartRace(_cup.Level, _cup.Track);
                    break;
                case Phase.Results:
                    StartRun(Mode);
                    break;
            }
        }

        void StartRun(KartMode mode)
        {
            Mode = mode;
            Format = _menu.Format;
            Difficulty = _menu.Difficulty;
            if (_input != null && PlayerGate.State(_input) != GateState.NoService) _input.PreferBody();
            _newCup = false;
            _readySince = -1f;
            _slots.ClearReady();
            foreach (var h in _humans) h.Reset();
            _cup = Format == KartFormat.Cup ? new KartCup(Difficulty, Mode, Random.Range(0, 1 << 30)) : null;
            _drivers = _cup != null ? _cup.Drivers : KartCup.DrawDrivers(Mode == KartMode.Versus ? 4 : 5, Random.Range(0, 1 << 30));
            int track = _cup != null ? _cup.Track : _menu.Track == KartMenu.RandomTrack ? Random.Range(0, KartTracks.Count) : _menu.Track;
            GameAudio.PlayMusic("kart");
            StartRace(_cup != null ? _cup.Level : KartLevel.For(Difficulty, 0), track);
        }

        void StartRace(KartLevel level, int track)
        {
            SetTrack(track);
            NewRace(level, track);
            ShowCall(KartStrings.Track(track), Cup != null ? KartStrings.RaceOfCup(_cup.Race) : KartStrings.Laps(laps), 2.2f);
            if (Mode == KartMode.Solo)
            {
                if (_input != null && _input.Gestures != null) _input.Gestures.Recenter();
            }
            else _slots.Recenter();
            for (int k = 0; k < 2; k++)
            {
                _camYaw[k] = float.NaN;
                _humans[k].LastPlace = 0;
            }
            SetPhase(Phase.Intro);
        }

        void EndRace()
        {
            var places = new int[_race.Karts.Length];
            for (int i = 0; i < places.Length; i++) places[i] = _race.Karts[i].Place;
            var cup = Cup;
            if (cup != null)
            {
                cup.Record(places);
                if (cup.Over && cup.Won)
                {
                    string key = CupKey(Mode, Difficulty);
                    PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
                    PlayerPrefs.Save();
                    _newCup = true;
                }
            }
            for (int i = 0; i < _race.Karts.Length; i++)
            {
                var k = _race.Karts[i];
                if (!k.IsHuman) continue;
                Debug.Log($"[Kart] {(Mode == KartMode.Solo ? "Player" : $"Player kart {k.Controller + 1}")}: place {k.Place}, time {k.FinishTime:0.0} s, " +
                          $"tricks {k.TricksDone}, hops {k.Hops}, stars {k.Stars}, items used {k.ItemsUsed}, walls {k.WallHits}, bumps {k.Bumps}, painted {k.PaintHits}");
            }
            Debug.Log($"[Kart] Race over on {_race.Track.Id}: " + string.Join(", ", Array.ConvertAll(_race.Order(), k => $"{k.Place}. {Name(k)} {k.FinishTime:0.0}")) +
                      (cup != null ? $"; cup after race {cup.Race}: " + string.Join(", ", Array.ConvertAll(cup.Standings(), r => $"{NameOfRacer(r)} {cup.Points[r]}")) : ""));
            SetPhase(cup != null && !cup.Over ? Phase.Standings : Phase.Results);
            if (Current == Phase.Results)
            {
                bool good = cup != null ? cup.Won : BestHumanPlace() <= 3;
                GameAudio.Jingle(good ? "jingle_win" : "jingle_gameover");
                if (good) _world?.Fans?.Cheer(1f, 4f);
            }
        }

        int BestHumanPlace()
        {
            int best = 99;
            foreach (var k in _race.Karts)
                if (k.IsHuman) best = Mathf.Min(best, k.Place);
            return best;
        }

        static string CupKey(KartMode mode, KartDifficulty d) => $"{CupsKey}{(mode == KartMode.Together ? "2" : "")}.{(int)d}";

        int[] Cups(KartMode mode) => new[]
        {
            PlayerPrefs.GetInt(CupKey(mode, KartDifficulty.Easy), 0), PlayerPrefs.GetInt(CupKey(mode, KartDifficulty.Medium), 0),
            PlayerPrefs.GetInt(CupKey(mode, KartDifficulty.Hard), 0),
        };

        bool TwoPlayersPaused()
        {
            for (int k = 0; k < 2; k++)
                if (!_slots[k].Present || !_slots[k].Calibrated) return true;
            return false;
        }

        Body BodyOf(int slot) => Mode == KartMode.Solo
            ? (slot == 0 && MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null)
            : _slots[slot].Body;

        void ReadBodies()
        {
            bool waiting = Current is Phase.Waiting or Phase.Results or Phase.Standings;
            int players = waiting ? (_slots.Present == 2 ? 2 : 1) : _race.Humans;
            for (int k = 0; k < players; k++) ReadBody(k, waiting && _slots.Present == 2 ? _slots[k].Body : BodyOf(k));
        }

        /// <summary>
        /// A new camera sample of player <paramref name="slot"/>: the reader takes the joints (raw: it filters the pose
        /// model's glitches itself), its jumps and raised hands go to the race with the race time the camera saw them.
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
            var vis = body.Visibility;
            var sample = new KartBodySample
            {
                LeftShoulder = raw[(int)Joint.LeftShoulder], RightShoulder = raw[(int)Joint.RightShoulder],
                LeftWrist = raw[(int)Joint.LeftWrist], RightWrist = raw[(int)Joint.RightWrist],
                LeftHip = raw[(int)Joint.LeftHip], RightHip = raw[(int)Joint.RightHip], Nose = raw[(int)Joint.Nose],
                LeftWristVisibility = vis[(int)Joint.LeftWrist], RightWristVisibility = vis[(int)Joint.RightWrist],
                HipVisibility = Mathf.Min(vis[(int)Joint.LeftHip], vis[(int)Joint.RightHip]), NoseVisibility = vis[(int)Joint.Nose],
                Torso = body.FastTorsoLength,
            };
            h.Seen.Clear();
            h.Moves.Update(body.SampleTime, sample, h.Seen);
            bool racing = Current is Phase.Intro or Phase.Racing && !_paused && !UsingKeys && slot < _race.Humans;
            if (!racing) return;
            double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            double age = Math.Max(0.0, Math.Min(0.35, nowUnix - body.SampleTime));
            double at = _race.Time - age;
            foreach (var e in h.Seen)
            {
                double t = at - (body.SampleTime - e.Time);
                MovesSeen?.Invoke(slot, e, t);
                if (e.Move == KartMove.Hop)
                {
                    _race.Hop(slot, t);
                    Debug.Log($"[Kart] Hop{(Mode == KartMode.Solo ? "" : $" by player {slot + 1}")} ({(e.Jumped ? "a jump" : "the wheel lifted")}), " +
                              $"race time {t:0.00}, seen {age * 1000:0} ms ago");
                }
                else if (e.Move == KartMove.HandsUp)
                {
                    var k = _race.KartOf(slot);
                    if (k.Item != KartItem.None && k.ItemRoll <= 0f) _race.UseItem(slot, t);
                }
            }
        }

        // ---------------------------------------------------------------- race events: sounds, effects, calls

        void OnRaceEvent(KartEvent e)
        {
            RaceEvents?.Invoke(e);
            var k = e.Kart >= 0 && e.Kart < _race.Karts.Length ? _race.Karts[e.Kart] : null;
            var view = k != null && k.Index < _views.Count ? _views[k.Index] : null;
            bool human = k != null && k.IsHuman;
            int slot = human ? k.Controller : -1;
            switch (e.Kind)
            {
                case KartEventKind.Countdown:
                    if (e.Value > 0)
                    {
                        GameAudio.Sfx("count", 0.8f, vary: 0f);
                        ShowCall(e.Value.ToString(), null, 0.9f);
                    }
                    else
                    {
                        GameAudio.Sfx("go", 0.9f, vary: 0f);
                        ShowCall(KartStrings.Go, null, 1.1f);
                        _world?.Fans?.Cheer(0.9f, 2f);
                        for (int p = 0; p < _race.Humans; p++) _humans[p].Moves.Calibrate();
                    }
                    break;
                case KartEventKind.QuickStart:
                    if (human)
                    {
                        AddPopup(e.Position, KartStrings.QuickStart, Sun, slot);
                        GameAudio.Sfx("kart_boost", 0.8f);
                    }
                    _fx.Boost(e.Position + Vector3.up * 0.5f, Sun, 10);
                    break;
                case KartEventKind.Hop:
                    if (human) GameAudio.Sfx("jump", 0.35f, 1.2f);
                    break;
                case KartEventKind.TakeOff:
                    if (human) GameAudio.Sfx("whoosh", 0.45f, 1.1f);
                    break;
                case KartEventKind.Trick:
                    if (human)
                    {
                        string text = e.Value == 2 ? KartStrings.DoubleTrick : e.Perfect ? KartStrings.PerfectTrick : KartStrings.Trick;
                        AddPopup(e.Position + Vector3.up * 1.6f, text, Sun, slot);
                        GameAudio.Sfx("kart_trick", 0.8f);
                        view?.Flash(Sun, 0.35f);
                    }
                    _fx.Sparkle(e.Position + Vector3.up, Sun, human ? 14 : 6);
                    break;
                case KartEventKind.Land:
                    _fx.Dust(e.Position, Vector3.zero, DustColour(k), 6);
                    if (human)
                    {
                        GameAudio.Sfx("land", 0.5f);
                        if (e.Value > 0)
                        {
                            GameAudio.Sfx("kart_boost", 0.9f);
                            _fx.Boost(view != null ? view.Exhaust(0) : e.Position, new Color(1f, 0.6f, 0.2f), 18);
                            _fovKick = 1f;
                        }
                        _shake[Mathf.Clamp(slot, 0, 1)] = Mathf.Max(_shake[Mathf.Clamp(slot, 0, 1)], 0.15f + 0.02f * e.Amount);
                    }
                    break;
                case KartEventKind.DriftStart:
                    if (human) GameAudio.Sfx("kart_drift_start", 0.5f);
                    break;
                case KartEventKind.DriftTier:
                    if (human) GameAudio.Sfx("ui_move", 0.45f, 1f + 0.15f * e.Value);
                    break;
                case KartEventKind.DriftEnd:
                    if (e.Value > 0)
                    {
                        if (human)
                        {
                            GameAudio.Sfx("kart_boost", 0.6f + 0.1f * e.Value);
                            AddPopup(e.Position + Vector3.up * 1.4f, e.Value >= 3 ? KartStrings.SuperTurbo : KartStrings.Turbo, KartFx.SparkColours[e.Value - 1], slot);
                            _fovKick = Mathf.Max(_fovKick, 0.6f + 0.15f * e.Value);
                        }
                        _fx.Boost(view != null ? view.Exhaust(1) : e.Position, KartFx.SparkColours[e.Value - 1], 6 + 3 * e.Value);
                    }
                    break;
                case KartEventKind.Pad:
                    if (human)
                    {
                        GameAudio.Sfx("kart_pad", 0.7f);
                        _fovKick = Mathf.Max(_fovKick, 0.8f);
                    }
                    break;
                case KartEventKind.Wall:
                    if (human)
                    {
                        GameAudio.Sfx("kart_bump", 0.4f + 0.5f * e.Amount);
                        _shake[Mathf.Clamp(slot, 0, 1)] = Mathf.Max(_shake[Mathf.Clamp(slot, 0, 1)], 0.2f * e.Amount + 0.05f);
                    }
                    _fx.Sparkle(e.Position + Vector3.up * 0.4f, Color.white, 5);
                    break;
                case KartEventKind.Bump:
                {
                    var other = e.Other >= 0 ? _race.Karts[e.Other] : null;
                    if (human || (other != null && other.IsHuman))
                    {
                        GameAudio.Sfx("kart_bump", 0.5f);
                        int s = human ? slot : other.Controller;
                        _shake[Mathf.Clamp(s, 0, 1)] = Mathf.Max(_shake[Mathf.Clamp(s, 0, 1)], 0.12f);
                    }
                    break;
                }
                case KartEventKind.Star:
                    if (human)
                    {
                        GameAudio.Sfx("kart_star", 0.55f, 1f + 0.03f * Mathf.Min(10, e.Value));
                    }
                    _fx.Sparkle(e.Position, Sun, human ? 8 : 3);
                    break;
                case KartEventKind.ItemBox:
                    _fx.Sparkle(e.Position, new Color(0.49f, 0.86f, 1f), 12);
                    if (human)
                    {
                        GameAudio.Sfx("kart_box", 0.7f);
                        if (e.Value == 1) GameAudio.Sfx("kart_roll", 0.4f);
                    }
                    break;
                case KartEventKind.ItemReady:
                    if (human)
                    {
                        GameAudio.Sfx("kart_item", 0.6f);
                        var hh = _humans[Mathf.Clamp(slot, 0, 1)];
                        if (!UsingKeys && hh.HintedItem++ < 3) ShowHint(slot, KartStrings.ItemHint, 3f);
                    }
                    break;
                case KartEventKind.ItemUsed:
                    if (human)
                    {
                        // "raise your hands to use it" is done with once it's used (it stayed up to 3 s over an empty slot)
                        var hu = _humans[Mathf.Clamp(slot, 0, 1)];
                        if (hu.Hint == KartStrings.ItemHint) hu.HintUntil = 0f;
                    }
                    switch ((KartItem)e.Value)
                    {
                        case KartItem.Rocket:
                            GameAudio.Sfx("kart_rocket", human ? 0.9f : 0.35f);
                            _fx.Boost(view != null ? view.Exhaust(0) : e.Position, new Color(1f, 0.45f, 0.2f), 20);
                            if (human) _fovKick = 1.3f;
                            break;
                        case KartItem.Paint:
                            GameAudio.Sfx("fly_throw", human ? 0.7f : 0.3f);
                            break;
                        case KartItem.Shield:
                            GameAudio.Sfx("kart_shield", human ? 0.7f : 0.3f);
                            break;
                        case KartItem.Honey:
                            GameAudio.Sfx("kart_honey", human ? 0.6f : 0.3f);
                            break;
                    }
                    break;
                case KartEventKind.PaintHit:
                {
                    _fx.Splash(e.Position + Vector3.up * 0.6f, new Color(0.55f, 0.4f, 1f), 22);
                    var owner = e.Other >= 0 ? _race.Karts[e.Other] : null;
                    if (human || (owner != null && owner.IsHuman)) GameAudio.Sfx("kart_paint", 0.8f);
                    if (human)
                    {
                        AddPopup(e.Position + Vector3.up * 1.5f, KartStrings.Painted, new Color(0.7f, 0.55f, 1f), slot);
                        view?.Flash(new Color(0.55f, 0.4f, 1f), 0.9f);
                        _shake[Mathf.Clamp(slot, 0, 1)] = 0.3f;
                    }
                    break;
                }
                case KartEventKind.ShieldBlock:
                    GameAudio.Sfx("kart_shield", human ? 0.8f : 0.3f, 1.2f);
                    _fx.Sparkle(e.Position + Vector3.up * 0.8f, new Color(0.5f, 0.9f, 1f), 14);
                    if (human) AddPopup(e.Position + Vector3.up * 1.5f, KartStrings.Blocked, new Color(0.5f, 0.9f, 1f), slot);
                    break;
                case KartEventKind.PaintSplat:
                    _fx.Splash(e.Position, new Color(0.55f, 0.4f, 1f), 10);
                    break;
                case KartEventKind.Honey:
                    _fx.Splash(e.Position + Vector3.up * 0.3f, new Color(1f, 0.7f, 0.15f), 12);
                    if (human)
                    {
                        GameAudio.Sfx("kart_honey", 0.7f, 0.8f);
                        AddPopup(e.Position + Vector3.up * 1.4f, KartStrings.HoneyHit, new Color(1f, 0.75f, 0.2f), slot);
                    }
                    break;
                case KartEventKind.Lap:
                    if (human && e.Value < _race.Laps)
                    {
                        GameAudio.Sfx("kart_lap", 0.7f);
                        ShowCall(KartStrings.Lap(e.Value, _race.Laps), null, 1.6f);
                    }
                    break;
                case KartEventKind.FinalLap:
                    if (human)
                    {
                        GameAudio.Sfx("kart_final", 0.8f);
                        ShowCall(KartStrings.FinalLap, KartStrings.Lap(e.Value, _race.Laps), 2f);
                    }
                    break;
                case KartEventKind.Finish:
                    if (human)
                    {
                        GameAudio.Sfx("sport_finish", 0.8f);
                        _fx.Confetti(e.Position, e.Value <= 3 ? 160 : 60);
                        _world?.Fans?.Cheer(e.Value <= 3 ? 1f : 0.6f, 4f);
                        bool two = Mode == KartMode.Together;
                        string title = Mode == KartMode.Versus ? $"{KartStrings.Player(slot)}: {KartStrings.PlaceName(e.Value)}" : KartStrings.Finished(e.Value, two);
                        ShowCall(KartStrings.Finish, title, 3.5f);
                    }
                    break;
                case KartEventKind.Place:
                    if (human)
                    {
                        var hp = _humans[Mathf.Clamp(slot, 0, 1)];
                        hp.PlaceFlash = 1f;
                        if (e.Value < (int)e.Amount && Current == Phase.Racing) GameAudio.Sfx("ui_move", 0.35f, 1.3f);
                    }
                    break;
            }
        }

        Color DustColour(Kart k) => _race.Track.Theme == KartTheme.Coast && k != null && k.OffRoad ? new Color(0.95f, 0.85f, 0.62f, 0.8f) : new Color(0.85f, 0.82f, 0.74f, 0.7f);

        void ShowCall(string call, string sub, float seconds, float delay = 0f)
        {
            _call = call;
            _callSub = sub;
            _callFrom = Time.time + delay;
            _callUntil = _callFrom + seconds;
        }

        void ShowHint(int slot, string text, float seconds)
        {
            var h = _humans[Mathf.Clamp(slot, 0, 1)];
            h.Hint = text;
            h.HintUntil = Time.time + seconds;
        }

        void AddPopup(Vector3 world, string text, Color color, int slot) => _popups.Add(new Popup { World = world, Text = text, Color = color, Slot = slot });

        string Name(Kart k)
        {
            if (!k.IsHuman) return k.Name;
            return Mode switch
            {
                KartMode.Versus => KartStrings.Player(k.Controller),
                KartMode.Together => KartStrings.Us,
                _ => KartStrings.You,
            };
        }

        string NameOfRacer(int racer)
        {
            if (_cup == null) return "";
            if (racer < _cup.HumanKarts) return Mode == KartMode.Versus ? KartStrings.Player(racer) : Mode == KartMode.Together ? KartStrings.Us : KartStrings.You;
            return _cup.NameOf(racer);
        }

        // ---------------------------------------------------------------- drawing the race

        void UpdateViews(float dt)
        {
            if (_race == null) return;
            double t = _race.Time;
            float step = Current == Phase.Racing && _paused ? 0f : dt;
            for (int i = 0; i < _race.Karts.Length && i < _views.Count; i++)
            {
                var k = _race.Karts[i];
                var v = _views[i];
                if (v == null) continue;
                float wheel = k.IsHuman && !k.Finished ? _humans[Mathf.Clamp(k.Controller, 0, 1)].Wheel : k.Steer * 60f;
                v.Show(k, _race.Track, wheel, step, t);
                if (step <= 0f) continue;
                // sparks while drifting, dust off the road, puffs from the exhausts
                if (k.Drifting && !k.Airborne && Time.frameCount % 2 == 0)
                    _fx.Sparks(v.RearWheel(0), v.RearWheel(1), k.DriftTier, -v.Forward);
                if (k.OffRoad && k.Speed > 6f && Time.frameCount % 3 == 0)
                    _fx.Dust(v.RearWheel(Time.frameCount % 2), -v.Forward * k.Speed, DustColour(k), 1);
                if (Time.frameCount % (k.BoostTime > 0f ? 2 : 9) == i % 3 && k.Speed > 3f)
                    _fx.Puff(v.Exhaust(Time.frameCount % 2), -v.Forward, k.BoostTime > 0f);
            }
            // the players' wheels, eased for the HUD and the steering wheels
            for (int p = 0; p < 2; p++)
            {
                var h = _humans[p];
                if (UsingKeys && p == 0) continue;
                float target = h.Moves.Holding ? Mathf.Clamp(h.Moves.WheelAngle, -110f, 110f) : h.Moves.Steer * 60f;
                h.Wheel = Mathf.Lerp(h.Wheel, target, 1f - Mathf.Exp(-18f * dt));
                bool racing = Current == Phase.Racing && !_paused && p < _race.Humans && !Practice;
                h.NoWheelFor = racing && !h.Moves.Holding ? h.NoWheelFor + dt : 0f;
                if (h.NoWheelFor > 2.5f && h.HintedWheel < 6 && (h.Hint == null || Time.time > h.HintUntil))
                {
                    h.HintedWheel++;
                    ShowHint(p, KartStrings.HoldTheWheel, 3f);
                }
                h.PlaceFlash = Mathf.MoveTowards(h.PlaceFlash, 0f, dt * 1.5f);
            }
            // a jump hint before the first ramps (until they've tricked)
            if (Current == Phase.Racing && !_paused && !Practice)
                for (int p = 0; p < _race.Humans; p++)
                {
                    var k = _race.KartOf(p);
                    var h = _humans[p];
                    if (k.TricksDone > 0 || h.HintedJump >= 3) continue;
                    if (_race.TimeToLip(k, out _) < 1.2f && (h.Hint == null || Time.time > h.HintUntil))
                    {
                        h.HintedJump++;
                        ShowHint(p, KartStrings.JumpHint, 1.6f);
                    }
                }
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].Age += dt;
                _popups[i].World += Vector3.up * (1.2f * dt);
                if (_popups[i].Age > 1.3f) _popups.RemoveAt(i);
            }
        }

        /// <summary>The gantry's lamps: dark, then red one by one with the countdown, green at "go".</summary>
        void UpdateGantry()
        {
            var lamps = _world != null ? _world.GantryLights : null;
            if (lamps == null || _race == null) return;
            var m = lamps.sharedMaterial;
            Color c;
            if (_race.Phase == KartPhase.Countdown && Current == Phase.Intro)
                c = _race.Time > -3f ? new Color(1f, 0.15f, 0.1f) : new Color(0.25f, 0.22f, 0.25f);
            else if (_race.Time < 2.0 && Current is Phase.Racing or Phase.Intro) c = new Color(0.2f, 1f, 0.35f);
            else c = new Color(0.95f, 0.95f, 0.9f);
            m.SetColor("_BaseColor", c);
            m.SetColor("_EmissionColor", c * 0.9f);
        }

        void UpdateTrophy()
        {
            bool show = Current == Phase.Results && Cup != null && Cup.Over && Cup.StandingOf(0) <= 3;
            if (_trophy.gameObject.activeSelf != show) _trophy.gameObject.SetActive(show);
            if (!show) return;
            var t = cam.transform;
            float pop = Mathf.SmoothStep(0f, 1f, _phaseTime / 0.6f);
            _trophy.SetPositionAndRotation(t.position + t.forward * 5.4f - t.up * (0.6f + 0.06f * Mathf.Sin(Time.time * 2f)), Quaternion.Euler(0f, Time.time * 55f, 0f));
            int standing = Cup.StandingOf(0);
            _trophy.localScale = Vector3.one * (1.45f * pop * (standing == 1 ? 1f : standing == 2 ? 0.8f : 0.65f));
            if (standing == 1 && Time.time >= _confettiAt)
            {
                _confettiAt = Time.time + 0.6f;
                _fx.Confetti(_trophy.position + Vector3.down * 3.2f, 14);
            }
        }

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
                Height = Mathf.Lerp(v0.Height, v1.Height, k), Behind = Mathf.Lerp(v0.Behind, v1.Behind, k), Ahead = Mathf.Lerp(v0.Ahead, v1.Ahead, k),
                Fov = Mathf.Lerp(v0.Fov, v1.Fov, k), SplitFov = Mathf.Lerp(v0.SplitFov, v1.SplitFov, k),
            };
        }

        /// <summary>
        /// The chase camera behind each player's kart: following the way it goes (smoothly: a drift's slide and a trick's
        /// spin don't swing it), a little wider at speed and on a boost, shaking on a bump. Waiting: slowly round the
        /// player's kart on the grid. The results: from the front of the player's kart.
        /// </summary>
        void UpdateCamera(float dt)
        {
            if (_race == null) return;
            var view = ViewNow(dt);
            bool split = Split;
            if (split != (_cam2 != null && _cam2.enabled)) ApplyViewports();
            _fovKick = Mathf.MoveTowards(_fovKick, 0f, dt * 0.8f);
            for (int p = 0; p < (split ? 2 : 1); p++)
            {
                var c = p == 0 ? cam : _cam2;
                var k = _race.KartOf(p);
                var track = _race.Track;
                var target = track.World(k.S, k.D, k.Ground + k.Height * 0.6f);
                float heading = track.Heading(k.S) + k.Psi * 0.7f;
                float yaw = 90f - heading * Mathf.Rad2Deg;
                if (float.IsNaN(_camYaw[p])) _camYaw[p] = yaw;
                _camYaw[p] = Mathf.LerpAngle(_camYaw[p], yaw, 1f - Mathf.Exp(-4f * dt));
                _shake[p] = Mathf.Max(0f, _shake[p] - dt);
                var shake = (Vector3)(Random.insideUnitCircle * (0.25f * _shake[p]));
                Vector3 pos, look;
                if (Current is Phase.Waiting or Phase.Standings || Current == Phase.Results && Cup == null)
                {
                    float orbit = Time.time * 9f + (Current == Phase.Waiting ? 150f : 200f);
                    var rot = Quaternion.Euler(0f, orbit, 0f);
                    pos = target + rot * new Vector3(0f, 2.2f, -6.5f);
                    look = target + Vector3.up * 0.9f;
                }
                else if (Current == Phase.Results)
                {
                    var rot = Quaternion.Euler(0f, _camYaw[p] + 160f, 0f);
                    pos = target + rot * new Vector3(0f, 3.2f, -7.5f);
                    look = target + Vector3.up * 1.4f;
                }
                else
                {
                    var rot = Quaternion.Euler(0f, _camYaw[p], 0f);
                    pos = target + rot * new Vector3(0f, view.Height, -view.Behind);
                    // keep above the road under the camera (the hills)
                    float under = track.Height(track.Wrap(k.S - view.Behind));
                    pos.y = Mathf.Max(pos.y, under + view.Height * 0.8f);
                    look = target + rot * new Vector3(0f, 1.0f, view.Ahead);
                }
                if (float.IsNaN(_camPos[p].x) || (_camPos[p] - pos).sqrMagnitude > 900f || _camPos[p] == Vector3.zero) _camPos[p] = pos;
                _camPos[p] = Vector3.Lerp(_camPos[p], pos, 1f - Mathf.Exp(-10f * dt));
                c.transform.position = _camPos[p] + shake;
                c.transform.rotation = Quaternion.LookRotation(look - c.transform.position, Vector3.up);
                float speedFov = Mathf.InverseLerp(_race.Level.BaseSpeed, _race.Level.BaseSpeed * 1.5f, k.Speed) * 8f;
                c.fieldOfView = (split ? view.SplitFov : view.Fov) + speedFov + 4f * _fovKick;
            }
        }

        void UpdateAudio(float dt)
        {
            if (_engines == null || _race == null) return;
            float master = GameAudio.SfxVolume;
            bool on = Current is Phase.Intro or Phase.Racing or Phase.Finish && !_paused;
            int players = Mode == KartMode.Versus ? 2 : 1;
            bool drifting = false;
            for (int p = 0; p < 2; p++)
            {
                var s = _engines[p];
                if (s.clip == null) continue;
                bool active = on && p < players;
                var k = _race.KartOf(p);
                float speed = active ? k.Speed / Mathf.Max(1f, _race.Level.BaseSpeed) : 0f;
                float revving = _race.Phase == KartPhase.Countdown ? 0.15f + 0.1f * Mathf.Sin(Time.time * 7f) : 0f;
                float pitch = 0.75f + 0.65f * Mathf.Clamp(speed, 0f, 1.6f) + revving + (k.BoostTime > 0f ? 0.12f : 0f);
                s.pitch = Mathf.Lerp(s.pitch, pitch, 1f - Mathf.Exp(-8f * dt));
                s.volume = Mathf.MoveTowards(s.volume, active ? 0.32f * master / players : 0f, dt * 2f);
                drifting |= active && k.Drifting && !k.Airborne;
            }
            if (_drift.clip != null) _drift.volume = Mathf.MoveTowards(_drift.volume, drifting ? 0.25f * master : 0f, dt * 4f);
        }

        // ---------------------------------------------------------------- HUD

        IEnumerable<Camera> Cameras()
        {
            yield return cam;
            if (_cam2 != null && _cam2.enabled) yield return _cam2;
        }

        const float MirrorLeft = 440f;

        void OnGUI()
        {
            float w = GameHud.Begin();
            bool split = Split;
            foreach (var p in _popups)
                foreach (var c in Cameras())
                {
                    var screen = c.WorldToScreenPoint(p.World);
                    if (screen.z <= 0f || !c.pixelRect.Contains(screen)) continue;
                    var g = GameHud.FromScreen(screen);
                    var color = p.Color;
                    color.a = Mathf.Clamp01(1.3f - p.Age);
                    float pop = 1f + 0.3f * Mathf.Exp(-p.Age * 8f);
                    SportHud.Text(new Rect(g.x - 300f, g.y - 40f, 600f, 80f), p.Text, (int)(46 * pop), color, TextAnchor.MiddleCenter, 3f);
                }
            if (split) GameHud.Box(new Rect(w / 2f - 3f, 0f, 6f, GameHud.Height), new Color(0.05f, 0.08f, 0.1f, 0.9f));

            switch (Current)
            {
                case Phase.Waiting:
                    DrawWaiting(w);
                    break;
                case Phase.Intro:
                case Phase.Racing:
                case Phase.Finish:
                    if (_paused)
                    {
                        if (Mode == KartMode.Solo) PlayerGate.DrawPaused(w, _input);
                        else DrawTwoPaused(w);
                        break;
                    }
                    for (int p = 0; p < (split ? 2 : 1); p++) DrawRaceHud(w, p, split);
                    DrawMinimap(w, split);
                    if (Mode == KartMode.Solo && !UsingKeys && Current == Phase.Racing) PlayerGate.DrawEdgeWarning(w, _input);
                    break;
                case Phase.Standings:
                    DrawStandings(w);
                    break;
                case Phase.Results:
                    DrawResults(w);
                    break;
            }
            if (_call != null && Time.time >= _callFrom && Time.time < _callUntil && Current is Phase.Intro or Phase.Racing or Phase.Finish)
            {
                if (split)
                {
                    DrawCall(w / 4f, w / 2f);
                    DrawCall(w * 3f / 4f, w / 2f);
                }
                else DrawCall(w / 2f, w);
            }
            if (Current == Phase.Racing && !_paused)
                for (int k = 0; k < _race.Humans; k++)
                {
                    var h = _humans[k];
                    if (h.Hint == null || Time.time >= h.HintUntil) continue;
                    float x0 = split ? k * w / 2f : 0f, width = split ? w / 2f : w;
                    string text = Mode == KartMode.Together ? $"{KartStrings.Player(k)}: {h.Hint}" : h.Hint;
                    float y = Mode == KartMode.Together ? 200f + 64f * k : 200f;
                    SportHud.Text(new Rect(x0, y, width, 64f), text, 38, Mode == KartMode.Solo ? Color.white : Color.Lerp(Color.white, PlayerColors[k], 0.35f), TextAnchor.MiddleCenter, 3f);
                }
        }

        /// <summary>One player's race HUD: the place (big) and the lap top left, the item top middle, the wheel bottom
        /// middle (it turns with their hands; dim with the hands down), in their half of a split screen.</summary>
        void DrawRaceHud(float w, int slot, bool split)
        {
            var k = _race.KartOf(slot);
            var h = _humans[slot];
            float x0 = split ? slot * w / 2f : 0f, half = split ? w / 2f : w;
            var night = new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.8f);
            // the place
            var placeRect = new Rect(x0 + 28f, 24f, 210f, 150f);
            GameHud.Box(placeRect, night);
            float flash = 1f + 0.25f * h.PlaceFlash;
            var placeColour = k.Place == 1 ? Sun : k.Place <= 3 ? Color.white : new Color(1f, 1f, 1f, 0.85f);
            SportHud.Text(new Rect(placeRect.x + 10f, placeRect.y - 4f, 120f, 130f), k.Place.ToString(), (int)(118 * flash), placeColour, TextAnchor.MiddleCenter, 0f);
            SportHud.Text(new Rect(placeRect.x + 116f, placeRect.y + 52f, 90f, 60f), $"/{_race.Karts.Length}", 40, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft, 0f);
            SportHud.Text(new Rect(placeRect.x, placeRect.y + 112f, placeRect.width, 34f), KartStrings.PlaceShort, 22, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter, 0f);
            // the lap
            int lap = Mathf.Clamp(k.Lap + 1, 1, _race.Laps);
            var lapRect = new Rect(x0 + 252f, 24f, 190f, 70f);
            GameHud.Box(lapRect, night);
            SportHud.Text(new Rect(lapRect.x, lapRect.y + 2f, lapRect.width, 30f), KartStrings.LapShort, 22, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter, 0f);
            SportHud.Text(new Rect(lapRect.x, lapRect.y + 26f, lapRect.width, 44f), $"{lap}/{_race.Laps}", 38, lap == _race.Laps ? Sun : Color.white, TextAnchor.MiddleCenter, 0f);
            // stars
            var starRect = new Rect(x0 + 252f, 104f, 190f, 70f);
            GameHud.Box(starRect, night);
            SportHud.Text(new Rect(starRect.x, starRect.y, starRect.width, starRect.height), $"★ {k.Stars}", 38, Sun, TextAnchor.MiddleCenter, 0f);
            // the item
            float ix = x0 + half / 2f;
            var slotRect = new Rect(ix - 70f, 22f, 140f, 140f);
            bool hasItem = k.ItemRoll > 0f || k.Item != KartItem.None;
            SportHud.Rounded(slotRect, hasItem ? night : new Color(night.r, night.g, night.b, 0.32f), 70f);
            Texture2D icon = null;
            if (k.ItemRoll > 0f) icon = _itemIcons[1 + (int)(Time.time * 14f) % 4];
            else if (k.Item != KartItem.None) icon = _itemIcons[(int)k.Item];
            if (icon != null)
            {
                float bob = k.ItemRoll > 0f ? 0f : 4f * Mathf.Sin(Time.time * 5f);
                GUI.DrawTexture(new Rect(slotRect.x + 18f, slotRect.y + 16f + bob, 104f, 104f), icon);
                if (k.ItemRoll <= 0f && !UsingKeys)
                {
                    var old = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.6f + 0.4f * Mathf.Sin(Time.time * 6f));
                    GUI.DrawTexture(new Rect(slotRect.xMax - 10f, slotRect.y + 70f, 70f, 70f), _figures[(int)KartArt.Figure.HandsUp]);
                    GUI.color = old;
                }
            }
            // the wheel: turned with their hands, a ring of the drift's sparks round it
            float size = split ? 150f : 190f;
            var wheelRect = new Rect(x0 + half / 2f - size / 2f, GameHud.Height - size - 34f, size, size);
            bool holding = UsingKeys || Practice || h.Moves.Holding;
            var tint = Mode == KartMode.Solo ? Color.white : Color.Lerp(Color.white, PlayerColors[slot], 0.5f);
            if (k.Drifting && k.DriftTier > 0) SportHud.Rounded(new Rect(wheelRect.x - 10f, wheelRect.y - 10f, size + 20f, size + 20f), KartFx.SparkColours[k.DriftTier - 1] * new Color(1f, 1f, 1f, 0.5f), (size + 20f) / 2f);
            var matrix = GUI.matrix;
            var centre = wheelRect.center;
            GUI.matrix = matrix * Matrix4x4.TRS(centre, Quaternion.Euler(0f, 0f, h.Wheel), Vector3.one) * Matrix4x4.TRS(-centre, Quaternion.identity, Vector3.one);
            var oldC = GUI.color;
            GUI.color = holding ? tint : new Color(tint.r, tint.g, tint.b, 0.35f);
            GUI.DrawTexture(wheelRect, _wheelTex);
            GUI.color = oldC;
            GUI.matrix = matrix;
            // the speed under it
            float kmh = k.Speed * 3.6f;
            SportHud.Text(new Rect(wheelRect.x - 40f, wheelRect.yMax - 6f, size + 80f, 34f), $"{kmh:0} km/h", 24, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter, 2f);
        }

        /// <summary>The track from above with a dot per kart (the players' bigger, in their colours).</summary>
        void DrawMinimap(float w, bool split)
        {
            if (_minimap == null) return;
            const float size = 230f;
            var r = split ? new Rect(w / 2f - size / 2f, GameHud.Height - size - 24f, size, size) : new Rect(w - size - 26f, 24f, size, size);
            GameHud.Box(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.55f));
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.DrawTexture(r, _minimap);
            for (int pass = 0; pass < 2; pass++)
                foreach (var k in _race.Karts)
                {
                    if (k.IsHuman != (pass == 1)) continue;
                    var p = _race.Track.Point(k.S) + _race.Track.Left(k.S) * k.D;
                    float u = (p.x - _minimapBounds.x) / _minimapBounds.width, v = (p.y - _minimapBounds.y) / _minimapBounds.height;
                    var at = new Vector2(r.x + u * r.width, r.yMax - v * r.height);
                    float d = k.IsHuman ? 22f : 14f;
                    GUI.color = k.IsHuman ? (Mode == KartMode.Solo ? Coral : PlayerColors[k.Controller]) : KartLooks.DriverPaint(k.Driver).Body;
                    GUI.DrawTexture(new Rect(at.x - d / 2f, at.y - d / 2f, d, d), _dot);
                }
            GUI.color = old;
        }

        void DrawCall(float centerX, float width)
        {
            float age = Time.time - _callFrom;
            float pop = 1f + 0.3f * Mathf.Exp(-age * 10f);
            int size = _call.Length > 16 ? 76 : _call.Length > 3 ? 104 : 170;
            if (width < 1200f) size = (int)(size * 0.75f);
            var old = GUI.matrix;
            var center = new Vector2(centerX, 330f);
            GUIUtility.ScaleAroundPivot(Vector2.one * pop, center * (Screen.height / GameHud.Height));
            SportHud.Text(new Rect(centerX - width / 2f, 250f, width, 160f), _call, size, Sun, TextAnchor.MiddleCenter, 4f);
            GUI.matrix = old;
            if (_callSub != null) SportHud.Text(new Rect(centerX - width / 2f, 400f, width, 70f), _callSub, 40, Color.white, TextAnchor.MiddleCenter, 3f);
        }

        static void Label(Rect r, string text, int size, Color colour, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            SportHud.Text(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), text, size, new Color(0f, 0f, 0f, 0.65f * colour.a), anchor, 0f);
            SportHud.Text(r, text, size, colour, anchor, 0f);
        }

        static void DrawTitle(float w, string title, float y = 30f, int size = 104) => Label(new Rect(0f, y, w, size * 1.15f), title, size, Sun);

        void DrawWaiting(float w)
        {
            bool camera = PlayerGate.State(_input) != GateState.NoService;
            DrawTitle(w, KartStrings.Title);
            const float menuY = 160f;
            _menu.Draw(w / 2f, menuY, camera, Cups(_menu.Mode == KartMode.Together ? KartMode.Together : KartMode.Solo), _cupIcon);
            float below = menuY + KartMenu.Height;
            Label(new Rect(0f, below + 4f, w, 40f), KartStrings.MenuHint, 26, new Color(1f, 1f, 1f, 0.75f));
            DrawMoves(w, below + 50f);
            if (_menu.Mode != KartMode.Solo)
            {
                string name = _menu.Mode == KartMode.Together ? KartStrings.TogetherModeSub : KartStrings.VersusModeSub;
                Label(new Rect(0f, below + 230f, w, 50f), name, 32, Color.white);
                DrawPlayerCards(w, 800f);
                return;
            }
            var old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.Translate(new Vector3(0f, below - 50f, 0f));
            PlayerGate.DrawWaiting(w, _input, "", KartStrings.KeyboardHelp);
            GUI.matrix = old;
        }

        /// <summary>The moves in a row: the wheel, the jump, the drift, both hands up (a figure and what it does).</summary>
        void DrawMoves(float w, float y)
        {
            Label(new Rect(0f, y, w, 40f), KartStrings.HowTo, 28, Color.white);
            string[] texts = { KartStrings.HowSteer, KartStrings.HowJump, KartStrings.HowDrift, KartStrings.HowItem };
            var icons = new[] { KartArt.Figure.Wheel, KartArt.Figure.Jump, KartArt.Figure.Wheel, KartArt.Figure.HandsUp };
            const float card = 440f, gap = 18f;
            float x0 = w / 2f - (card * 4f + gap * 3f) / 2f;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(x0 + i * (card + gap), y + 46f, card, 104f);
                GameHud.Box(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.8f));
                var old = GUI.color;
                GUI.color = i == 2 ? KartFx.SparkColours[1] : Sun;
                GUI.DrawTexture(new Rect(r.x + 8f, r.y + 8f, 88f, 88f), _figures[(int)icons[i]]);
                GUI.color = old;
                GUI.Label(new Rect(r.x + 100f, r.y + 2f, r.width - 110f, r.height - 4f), texts[i], CardStyle);
            }
        }

        GUIStyle _cardStyle;
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
                SportHud.Text(new Rect(r.x + 44f, r.y + 10f, r.width - 60f, 64f), KartStrings.Player(k), 40, PlayerColors[k], TextAnchor.MiddleLeft, 0f);
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
                else if (!s.Calibrated) status = $"{KartStrings.StandStill} {s.CalibrationProgress * 100f:0}%";
                else if (!s.Ready) status = KartStrings.RaiseYourHands;
                else
                {
                    status = KartStrings.Ready;
                    color = Sun;
                }
                SportHud.Text(new Rect(r.x + 44f, r.y + 76f, r.width - 60f, 60f), status, 32, color, TextAnchor.MiddleLeft, 0f);
            }
            var middle = new Rect(650f, y, w - 1300f, 150f);
            if (_slots.TooClose) GameHud.Banner(new Rect(middle.x, y + 40f, middle.width, 76f), KartStrings.StepApart, Brand.Current.SecondaryColor);
            else if (_readySince >= 0f)
            {
                int waiting = _slots[0].Ready ? 1 : 0;
                int left = Mathf.Max(1, Mathf.CeilToInt(JoinWait - (Time.time - _readySince)));
                Label(new Rect(middle.x, y + 10f, middle.width, 64f), KartStrings.WaitingFor(waiting), 34, PlayerColors[waiting]);
                Label(new Rect(middle.x, y + 80f, middle.width, 50f), KartStrings.PlayingAloneIn(left), 28, new Color(1f, 1f, 1f, 0.8f));
            }
        }

        void DrawReadyLine(float w, float y)
        {
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            string Status(int k)
            {
                var s = _slots[k];
                return !s.Present ? CommonStrings.StepIn : !s.Calibrated ? KartStrings.StandStill : s.Ready ? KartStrings.Ready : KartStrings.RaiseYourHands;
            }
            Label(new Rect(0f, y, w - MirrorLeft, 50f), $"{KartStrings.Player(0)}: {Status(0)}    {KartStrings.Player(1)}: {Status(1)}", 30, Color.white);
        }

        /// <summary>Between a cup's races: the race's result and the standings (points), the next track.</summary>
        void DrawStandings(float w)
        {
            var cup = _cup;
            if (cup == null || cup.Places.Count == 0) return;
            DrawTitle(w, KartStrings.CupOf(Difficulty), 26f, 72);
            Label(new Rect(0f, 120f, w, 50f), $"{KartStrings.Standings} · {KartStrings.RaceOfCup(cup.Race - 1)}", 34, Color.white);
            DrawTable(w, 180f, cup.Standings(), r => cup.Points[r].ToString(), r => cup.Places[cup.Places.Count - 1][r]);
            Label(new Rect(0f, 760f, w - MirrorLeft, 54f), KartStrings.NextRace(cup.Track), 40, Color.white);
            if (_phaseTime < 2f) return;
            bool two = Mode != KartMode.Solo;
            if (two) DrawReadyLine(w, 826f);
            else Label(new Rect(0f, 826f, w - MirrorLeft, 50f), PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter
                           : PlayerGate.PlacementHint(_input) ?? KartStrings.RaiseForNext(false), 30, Sun);
        }

        /// <summary>A table of racers (in <paramref name="order"/>): place, a colour chip, the name, a value; the players'
        /// rows lit.</summary>
        void DrawTable(float w, float y, int[] order, Func<int, string> value, Func<int, int> lastPlace)
        {
            const float rowH = 82f, width = 940f;
            float x = w / 2f - width / 2f;
            for (int i = 0; i < order.Length; i++)
            {
                int racer = order[i];
                var k = racer < _race.Karts.Length ? _race.Karts[racer] : null;
                bool human = k != null && k.IsHuman;
                var r = new Rect(x, y + i * rowH, width, rowH - 10f);
                GameHud.Box(r, human ? new Color(SportHud.Sun.r * 0.35f, SportHud.Sun.g * 0.3f, 0.1f, 0.88f) : new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.84f));
                SportHud.Text(new Rect(r.x + 10f, r.y, 70f, r.height), (i + 1).ToString(), 44, i == 0 ? Sun : Color.white, TextAnchor.MiddleCenter, 0f);
                var chip = k == null ? Color.white : human ? PlayerColors[Mathf.Clamp(k.Controller, 0, 1)] : KartLooks.DriverPaint(k.Driver).Body;
                SportHud.Rounded(new Rect(r.x + 92f, r.y + 14f, 14f, r.height - 28f), chip, 7f);
                string name = k != null ? Name(k) : NameOfRacer(racer);
                SportHud.Text(new Rect(r.x + 124f, r.y, 480f, r.height), name, 38, human ? Sun : Color.white, TextAnchor.MiddleLeft, 0f);
                if (lastPlace != null)
                    SportHud.Text(new Rect(r.x + 560f, r.y, 180f, r.height), $"({KartStrings.PlaceShort.ToLowerInvariant()} {lastPlace(racer)})", 26, new Color(1f, 1f, 1f, 0.65f), TextAnchor.MiddleCenter, 0f);
                SportHud.Text(new Rect(r.xMax - 200f, r.y, 180f, r.height), value(racer), 40, Color.white, TextAnchor.MiddleRight, 0f);
            }
        }

        void DrawResults(float w)
        {
            bool two = Mode == KartMode.Together;
            var cup = Cup;
            if (cup != null && cup.Over)
            {
                int standing = cup.StandingOf(0);
                if (Mode == KartMode.Versus)
                {
                    int best = cup.StandingOf(0) <= cup.StandingOf(1) ? 0 : 1;
                    DrawTitle(w, KartStrings.Won(best), 30f, 96);
                }
                else DrawTitle(w, KartStrings.CupPlace(standing, two), 30f, standing == 1 ? 104 : 80);
                string sub = standing == 1 && _newCup ? $"{KartStrings.Cups}: {Cups(Mode)[(int)Difficulty]}" : standing > 1 ? KartStrings.CupWinner(NameOfRacer(cup.Standings()[0])) : null;
                if (sub != null) Label(new Rect(0f, 150f, w, 54f), sub, 36, Color.white);
                if (standing > 3 || Mode == KartMode.Versus) DrawTable(w, 220f, cup.Standings(), r => cup.Points[r].ToString(), null);
            }
            else
            {
                var order = _race.Order();
                var ranks = Array.ConvertAll(order, k => k.Index);
                if (Mode == KartMode.Versus)
                {
                    int best = _race.Karts[0].Place < _race.Karts[1].Place ? 0 : 1;
                    DrawTitle(w, KartStrings.Won(best), 30f, 96);
                }
                else DrawTitle(w, KartStrings.Finished(_race.Karts[0].Place, two), 30f, _race.Karts[0].Place == 1 ? 104 : 88);
                DrawTable(w, 170f, ranks, r => Clock(_race.Karts[r].FinishTime), null);
            }
            if (_phaseTime < 1.5f) return;
            bool twoPlayers = Mode != KartMode.Solo;
            if (twoPlayers) DrawReadyLine(w, 780f);
            string again = PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter : PlayerGate.PlacementHint(_input) ?? KartStrings.AgainOrMenu(twoPlayers);
            Label(new Rect(0f, twoPlayers ? 836f : 800f, w - MirrorLeft, 50f), again, 30, Sun);
        }

        static string Clock(double seconds)
        {
            if (seconds <= 0 || double.IsInfinity(seconds)) return "-";
            int m = (int)(seconds / 60.0);
            double s = seconds - m * 60;
            return $"{m}:{s:00.00}";
        }

        void DrawTwoPaused(float w)
        {
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                if (s.Present && s.Calibrated) continue;
                GameHud.Message(w, s.Present ? KartStrings.HoldStillPlayer(k) : KartStrings.ComeBackPlayer(k), null, PlayerColors[k]);
                return;
            }
        }
    }
}
