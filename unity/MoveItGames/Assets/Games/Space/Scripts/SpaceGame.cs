using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using Joint = MoveIt.Motion.Joint;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Space
{
    /// <summary>
    /// "Yjet!": explore a galaxy and find all its planets (reworked 2026-11-14, user: "a No Man's Sky game: just explore
    /// and find planets; unique planets, many, beautiful without much manual work; very flowing from planet to planet;
    /// finished when all are found; some hours; a sense of a map"). The rules are <see cref="Galaxy"/> (64 star systems,
    /// ~280 planets, the same on every box) and <see cref="Explorer"/> (the flight, the scan, the autopilot); this draws
    /// them and reads the body.
    /// <list type="bullet">
    /// <item><b>Flying</b>: the astronaut always flies on; leaning or stepping aside turns (<see cref="SpaceMoves"/>), both
    /// arms up boost, ducking slows. Flying into a planet's zone scans it: the astronaut swings round it close by and after
    /// four seconds it is found (a card, its name, the count). The radar (top right) and an arrow show the nearest planet
    /// not yet found; while nobody steers, the autopilot turns that way (a child who only boosts still finds everything).</item>
    /// <item><b>The map</b>: when a system's planets are all found the galaxy map opens (also the remote's OK, M): the
    /// spiral of stars, the routes between them, the systems visited with their planets' dots, the ones next to them
    /// waiting, the rest in the fog. Lean (or the arrows) to choose a star, both arms up (or OK) to fly there: a warp.</item>
    /// <item><b>The end</b>: every planet found = the finale (the galaxy lit up, the time it took); flying on after it.</item>
    /// <item>What a family has found is kept on the box (<see cref="GalaxyProgress"/>, PlayerPrefs). Two players fly one
    /// trip together: both lean to turn (the average), either one's arms up boost.</item>
    /// </list>
    /// </summary>
    public sealed class SpaceGame : MonoBehaviour
    {
        public enum Phase { Waiting, Flying, Map, Warp, Finale }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] Light sun;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: sparkles, bursts, confetti.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the soft dot: dust, streaks.")] Material dotMaterial;
        [SerializeField] SpaceWorldArt art = new SpaceWorldArt();

        const string ProgressKey = "space.galaxy";
        public static readonly Color[] PlayerColors = { new Color(1f, 0.35f, 0.31f), new Color(0.19f, 0.78f, 0.96f) };
        public const float JoinWait = 4f;
        static readonly Color Sun = SportHud.Sun;
        static readonly Color Scan = new Color(0.45f, 0.9f, 1f);
        /// <summary>The songs flown to, one after another (the system's star picks the first).</summary>
        static readonly string[] Playlist = { "space_moon", "space_candy", "space_ice", "space_jungle", "space_star" };

        public Phase Current { get; private set; }
        public Galaxy Galaxy { get; private set; }
        public GalaxyProgress Progress => _progress;
        public Explorer Explorer => _explorer;
        public SpaceMode Mode { get; private set; } = SpaceMode.Solo;
        public SpaceMenu Menu => _menu;
        public PlayerSlots Slots => _slots;
        public SpaceWorld World => _world;
        public IReadOnlyList<AstronautView> Astronauts => _astronauts;
        public event Action<ExploreEvent> Explored;
        /// <summary>Tests and box measurements: the autopilot flies everything (boosting between planets, choosing the
        /// map's suggestion), on a copy of the family's progress (nothing saved).</summary>
        public bool Autoplay { get; set; }
        /// <summary>Tests: false keeps the family's progress as it was (nothing found is saved).</summary>
        public bool Saving { get; set; } = true;
        /// <summary>The system the map's cursor is on.</summary>
        public int MapCursor => _cursor;
        /// <summary>Player <paramref name="slot"/>'s camera reader (tests).</summary>
        public SpaceMoves MovesOf(int slot) => _humans[Mathf.Clamp(slot, 0, 1)].Moves;
        public float Steer { get; private set; }
        public bool Boosting { get; private set; }

        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        GalaxyProgress _progress;
        Explorer _explorer;
        SpaceWorld _world;
        SpaceFx _fx;
        readonly List<AstronautView> _astronauts = new List<AstronautView>();
        readonly SpaceMenu _menu = new SpaceMenu();
        readonly PlayerSlots _slots = new PlayerSlots { Area = TwoArea };
        float _phaseTime, _readySince = -1f, _saveAt;
        bool _paused;
        AudioSource _music;
        int _song = -1;
        float _fovKick, _shake, _bank, _lastHeading, _warpFlash;
        Vector3 _camPos;
        Quaternion _camRot = Quaternion.identity;
        bool _camSet;
        string _call, _callSub;
        float _callFrom, _callUntil;
        // the discovery card
        Planet _card;
        float _cardAt = -10f;
        int _cardCount;
        bool _cardSuit;
        // the map
        int _cursor = -1, _jumpTo = -1;
        float _leanSince = -1f, _leanRepeat, _armsUpSince = -1f, _mapOpenAt = -1f;
        readonly List<int> _reachable = new List<int>();
        float _turnBackShown = -10f, _hintAt;
        Planet _scanning;
        float _scanSound = -10f;

        /// <summary>One player's input: their moves, the arms as drawn.</summary>
        sealed class Human
        {
            public readonly SpaceMoves Moves = new SpaceMoves();
            public double LastSample;
            public Body Body;
            public Vector2? Left, Right;

            public void Reset()
            {
                Moves.Reset();
                LastSample = 0;
                Left = Right = null;
            }
        }
        readonly Human[] _humans = { new Human(), new Human() };

        /// <summary>The whole body leans and steps, the arms go up: the hips in the picture, room to step to each side and
        /// to raise both arms over the head.</summary>
        static readonly PlayArea Area = new PlayArea(BodyPart.Hips, side: 0.6f, above: 0.4f);
        static readonly PlayArea TwoArea = new PlayArea(BodyPart.Hips, side: 0.45f, above: 0.4f);

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            useGUILayout = false;
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            PlayerGate.Use(_input, Area);
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null) _mirrorWasVisible = _mirror.Visible;
            _menu.Load();
            switch (PerfSwitches.Get("mode"))
            {
                case "together": _menu.Mode = SpaceMode.Together; break;
                case "solo": _menu.Mode = SpaceMode.Solo; break;
            }
            Autoplay |= PerfSwitches.Get("autoplay") == "1";
            Galaxy = new Galaxy();
            var saved = GalaxyProgress.Load(Galaxy, PlayerPrefs.GetString(ProgressKey, ""));
            // the game playing itself (tests, measuring the box) keeps the family's progress as it was
            _progress = Autoplay ? GalaxyProgress.Load(Galaxy, saved.Save(Galaxy)) : saved;
            if (int.TryParse(PerfSwitches.Get("system"), out int system) && system >= 0 && system < Galaxy.All.Count)
            {
                _progress.Visited.Add(system);
                _progress.Current = system;
            }
            if (sun != null) sun.shadows = LightShadows.None;
            if (cam != null)
            {
                cam.farClipPlane = 9000f;
                cam.nearClipPlane = 0.3f;
            }
            _world = SpaceWorld.Create(art, Galaxy);
            _explorer = new Explorer(Galaxy, _progress);
            _world.Show(_explorer.System);
            StartNearAPlanet();
            _fx = SpaceFx.Create(sparkMaterial, dotMaterial);
            if (art.ui != null) _hud = new HudBatch(art.ui, art.ui);
            _music = gameObject.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = false;
            _music.spatialBlend = 0f;
            Mode = _menu.Mode;
            Dress();
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("space");
            Debug.Log($"[Space] Galaxy {Galaxy.Seed}: {Galaxy.Planets.Count} planets in {Galaxy.All.Count} systems; found {_progress.Found.Count}, " +
                      $"in {_explorer.System.Name} ({_progress.FoundIn(_explorer.System)} of {_explorer.System.Planets.Count}), {_progress.Seconds / 60f:0} min played");
        }

        /// <summary>The astronaut hovering beside a planet of the system (the home planet at the very start, else one
        /// already found, else the one nearest the way in), just outside its zone, going round it: the waiting screen
        /// shows it behind the astronaut, and flying starts from there.</summary>
        void StartNearAPlanet()
        {
            Planet best = null;
            float bestD = float.MaxValue;
            foreach (var p in _explorer.System.Planets)
            {
                float d = _explorer.Distance(p) - (p.Type == PlanetType.Home ? 1e6f : _explorer.Found(p) ? 1e5f : 0f);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            if (best == null) return;
            _home = best;
            var out_ = best.Position.sqrMagnitude > 1f ? best.Position.normalized : Vector2.up;
            _explorer.Position = best.Position + out_ * (Explorer.FindReach(best) + 60f);
            var tangent = new Vector2(-out_.y, out_.x);
            _explorer.Heading = Mathf.Atan2(tangent.x, tangent.y);
            _lastHeading = _explorer.Heading;
            _camSet = false;
        }

        Planet _home;

        /// <summary>Tests: straight into system <paramref name="id"/> (no map, no warp), beside one of its planets.</summary>
        public void Visit(int id)
        {
            if (id < 0 || id >= Galaxy.All.Count) return;
            _explorer.Enter(id);
            _world.Show(_explorer.System);
            StartNearAPlanet();
        }

        void OnDestroy()
        {
            Save();
            _hud?.Dispose();
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
        }

        void Save()
        {
            if (Autoplay || !Saving || Galaxy == null || _progress == null) return;
            PlayerPrefs.SetString(ProgressKey, _progress.Save(Galaxy));
            PlayerPrefs.Save();
        }

        int Players => Mode == SpaceMode.Together ? 2 : 1;

        /// <summary>The astronauts for the players (player 1 in the suit chosen).</summary>
        void Dress()
        {
            int count = Players;
            while (_astronauts.Count < count) _astronauts.Add(null);
            for (int i = 0; i < _astronauts.Count; i++)
            {
                if (i >= count)
                {
                    if (_astronauts[i] != null) _astronauts[i].gameObject.SetActive(false);
                    continue;
                }
                var look = i == 0 ? SpaceLooks.Player(_progress.Suit) : SpaceLooks.Player2();
                if (_astronauts[i] == null)
                    _astronauts[i] = AstronautView.Create(i == 0 ? "Astronaut" : "Astronaut 2", look, art.glass, art.flame, _world.Dome, _world.FlameMesh, null);
                else _astronauts[i].Recolour(look);
                _astronauts[i].gameObject.SetActive(true);
                _astronauts[i].Low = false;
            }
        }

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            if (phase is Phase.Waiting or Phase.Finale) _slots.ClearReady();
            _readySince = -1f;
            Debug.Log($"[Space] {phase}");
        }

        void StartFlying(SpaceMode mode)
        {
            if (_input != null && PlayerGate.State(_input) != GateState.NoService) _input.PreferBody();
            _readySince = -1f;
            _slots.ClearReady();
            Mode = mode;
            Dress();
            if (Mode == SpaceMode.Solo)
            {
                if (_input != null && _input.Gestures != null) _input.Gestures.Recenter();
            }
            else _slots.Recenter();
            foreach (var h in _humans) h.Moves.Calibrate();
            GameAudio.StopMusic(0.5f);
            if (_song < 0) _song = (int)_explorer.System.Star % Playlist.Length;
            PlayNext(false);
            ShowCall(SpaceStrings.System(_explorer.System.Name), SpaceStrings.StarName(_explorer.System.Star), 2.6f);
            _camSet = false;
            SetPhase(Phase.Flying);
        }

        /// <summary>What hands up (or OK) does on the screen showing (tests): start flying, close the map, fly on.</summary>
        public void Continue()
        {
            switch (Current)
            {
                case Phase.Waiting:
                case Phase.Finale:
                    StartFlying(_menu.Mode);
                    break;
                case Phase.Map:
                    CloseMap();
                    break;
            }
        }

        /// <summary>Tests: the menu's player choice as the remote would set it.</summary>
        public void Choose(SpaceMode mode)
        {
            _menu.Mode = mode;
            Mode = mode;
            if (Current == Phase.Waiting) Dress();
        }

        /// <summary>Tests: the explorer just outside <paramref name="p"/>'s zone, heading at it.</summary>
        public void PlaceNear(Planet p, float extra = 40f)
        {
            if (p.System != _explorer.System.Id) return;
            var from = p.Position.normalized * (p.Orbit + Explorer.FindReach(p) + extra);
            _explorer.Position = from;
            var to = p.Position - from;
            _explorer.Heading = Mathf.Atan2(to.x, to.y);
            _camSet = false;
        }

        /// <summary>Opens the galaxy map (the cursor on the map's suggestion).</summary>
        public void OpenMap()
        {
            if (Current != Phase.Flying) return;
            _reachable.Clear();
            for (int i = 0; i < Galaxy.All.Count; i++)
                if (_progress.Reachable(Galaxy, i)) _reachable.Add(i);
            int suggest = _progress.Suggest(Galaxy);
            _cursor = suggest >= 0 ? suggest : _explorer.System.Id;
            _leanSince = _armsUpSince = -1f;
            _mapOpenAt = -1f;
            GameAudio.Sfx("ui_select", 0.5f);
            Save();
            SetPhase(Phase.Map);
        }

        void CloseMap()
        {
            _mapOpenAt = -1f;
            SetPhase(Phase.Flying);
        }

        /// <summary>Warps to system <paramref name="id"/> (from the map; tests).</summary>
        public void JumpTo(int id)
        {
            if (id < 0 || id >= Galaxy.All.Count || !_progress.Reachable(Galaxy, id)) return;
            if (id == _explorer.System.Id)
            {
                CloseMap();
                return;
            }
            _jumpTo = id;
            GameAudio.Sfx("space_launch", 0.9f);
            SetPhase(Phase.Warp);
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            // the remote's back closes the map (it leaves the game everywhere else)
            if (Current == Phase.Map && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                GameAudio.Sfx("ui_back", 0.5f);
                CloseMap();
            }
            else if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            var tracker = MotionSystem.Instance != null ? MotionSystem.Instance.Tracker : null;
            _slots.Locked = Current != Phase.Waiting && Mode != SpaceMode.Solo;
            if (tracker != null) _slots.Update(tracker.Bodies);
            AskForPeople();
            ReadBodies();

            switch (Current)
            {
                case Phase.Waiting:
                    if (HandleMenu()) break;
                    if (Autoplay) StartFlying(SpaceMode.Solo);
                    else if (ReadyToGo(_menu.Mode, true, out bool alone)) StartFlying(alone ? SpaceMode.Solo : _menu.Mode);
                    break;
                case Phase.Flying:
                    Fly(dt);
                    break;
                case Phase.Map:
                    Map(dt);
                    break;
                case Phase.Warp:
                    Warp(dt);
                    break;
                case Phase.Finale:
                    _explorer.Step(dt, 0f, false, false);
                    if (_phaseTime > 4f && (Autoplay || StartPressed())) StartFlying(Mode);
                    if (Random.value < dt * 3f && _astronauts.Count > 0 && _astronauts[0] != null)
                        _fx.Firework(_astronauts[0].transform.position + Quaternion.Euler(0f, _explorer.Heading * Mathf.Rad2Deg, 0f) * new Vector3(Random.Range(-14f, 14f), Random.Range(4f, 12f), Random.Range(14f, 30f)));
                    break;
            }

            if (_mirror != null) _mirror.Visible = _mirrorWasVisible && (Current == Phase.Waiting || _paused);
            UpdateAstronauts(dt);
            var velocity = SpaceWorld.World(_explorer.Forward) * (_explorer.Speed * (Current == Phase.Warp ? WarpStretch() : 1f));
            bool moving = Current is Phase.Flying or Phase.Warp or Phase.Finale;
            _world.Draw(dt, cam, moving ? velocity : Vector3.zero);
            _world.BeginGlows(cam);
            UpdateCamera(dt);
            _world.DrawGlows();
            KeepPlaying();
            if (Time.time > _saveAt)
            {
                _saveAt = Time.time + 30f;
                if (Current != Phase.Waiting) Save();
            }
        }

        // ---------------------------------------------------------------- flying

        void Fly(float dt)
        {
            bool solo = Mode == SpaceMode.Solo;
            bool wantPause = !Autoplay && !UsingKeys && (solo ? PlayerGate.ShouldPause(_input) : TwoPlayersPaused());
            if (wantPause != _paused)
            {
                _paused = wantPause;
                if (_paused) _music.Pause();
                else _music.UnPause();
            }
            if (_paused) return;
            if (RemoteInput.Confirm || Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                OpenMap();
                return;
            }
            ReadControls(out float steer, out bool boost, out bool slow);
            Steer = steer;
            Boosting = boost;
            _explorer.Step(dt, steer, boost, slow);
            var events = _explorer.Events;
            for (int i = 0; i < events.Count; i++) OnEvent(events[i]);
            events.Clear();
            // the scan: a soft hum when it starts
            if (_explorer.Scanning != _scanning)
            {
                _scanning = _explorer.Scanning;
                if (_scanning != null && Time.time - _scanSound > 1.5f)
                {
                    _scanSound = Time.time;
                    GameAudio.Sfx("space_ring", 0.45f);
                }
            }
            // the map opens by itself a moment after a system is done
            if (_mapOpenAt > 0f && Time.time >= _mapOpenAt && Current == Phase.Flying)
            {
                if (_progress.AllFound(Galaxy)) _mapOpenAt = -1f;
                else OpenMap();
            }
            // hints now and then
            if (Time.time > _hintAt && _phaseTime > 20f && !Autoplay)
            {
                _hintAt = Time.time + 45f;
                if (_explorer.Target != null && _explorer.Distance(_explorer.Target) > 600f && !boost) ShowCall(SpaceStrings.BoostHint, null, 2.4f);
                else if (_explorer.Target != null) ShowCall(SpaceStrings.FollowArrow, null, 2.4f);
            }
        }

        bool UsingKeys => Mode == SpaceMode.Solo && (_input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService);

        /// <summary>The flight's controls: the body (two players: the average lean, either one's arms up or duck), else the
        /// keys (← → turn, ↑ boost, ↓ slow), or the autopilot (tests: boosting between planets).</summary>
        void ReadControls(out float steer, out bool boost, out bool slow)
        {
            steer = 0f;
            boost = slow = false;
            if (Autoplay)
            {
                var t = _explorer.Target;
                boost = t != null && _explorer.Scanning == null && _explorer.Distance(t) > Explorer.FindReach(t) * 2.2f;
                return;
            }
            if (UsingKeys)
            {
                var kb = Keyboard.current;
                if (kb == null) return;
                steer = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                boost = kb.upArrowKey.isPressed;
                slow = kb.downArrowKey.isPressed;
                return;
            }
            int n = 0;
            for (int k = 0; k < Players; k++)
            {
                var m = _humans[k].Moves;
                if (_humans[k].Body == null) continue;
                steer += m.Steer;
                boost |= m.Boosting;
                slow |= m.Ducking;
                n++;
            }
            if (n > 0) steer /= n;
        }

        void OnEvent(ExploreEvent e)
        {
            Explored?.Invoke(e);
            switch (e.Kind)
            {
                case ExploreEventKind.Found:
                {
                    int suitsBefore = Suits(_progress.Found.Count - 1);
                    _card = e.Planet;
                    _cardAt = Time.time;
                    _cardCount = _progress.Found.Count;
                    _cardSuit = _progress.Suits > suitsBefore;
                    _fovKick = 1f;
                    var centre = SpaceWorld.CentreOf(e.Planet);
                    var colour = Colour(e.Planet.Look.Main);
                    _fx.Burst(centre, Vector3.zero, Quaternion.LookRotation(centre - (cam != null ? cam.transform.position : Vector3.zero)), colour, e.Planet.Radius * 1.25f, 40);
                    _fx.Pop(centre, Sun, 18);
                    GameAudio.Sfx("space_bigstar", 0.8f);
                    if (e.Planet.Rare) GameAudio.Sfx("space_gate", 0.6f);
                    foreach (var a in _astronauts)
                        if (a != null && a.gameObject.activeSelf) a.Face(Chibi.Face.Grin, 2f);
                    Debug.Log($"[Space] Found {e.Planet.Name} ({e.Planet.Type}{(e.Planet.Rare ? ", rare" : "")}) in {_explorer.System.Name}: {_progress.Found.Count} of {Galaxy.Planets.Count}, {_progress.Seconds / 60f:0.0} min");
                    Save();
                    break;
                }
                case ExploreEventKind.SystemComplete:
                    ShowCall(SpaceStrings.SystemDone(_explorer.System.Name), SpaceStrings.AllFoundHere, 3.2f, 2.2f);
                    GameAudio.Jingle("jingle_win");
                    _mapOpenAt = Time.time + 6f;
                    foreach (var a in _astronauts)
                        if (a != null && a.gameObject.activeSelf) a.Face(Chibi.Face.Happy, 4f);
                    break;
                case ExploreEventKind.GalaxyComplete:
                    _mapOpenAt = -1f;
                    Save();
                    SetPhase(Phase.Finale);
                    GameAudio.Jingle("jingle_win");
                    Debug.Log($"[Space] The whole galaxy found in {_progress.Seconds / 3600f:0.00} h");
                    break;
                case ExploreEventKind.Bump:
                    if (Time.time - _scanSound > 0.6f && _explorer.Speed > Explorer.Cruise)
                    {
                        _shake = 0.25f;
                        GameAudio.Sfx("space_bump", 0.5f);
                        _scanSound = Time.time;
                    }
                    break;
                case ExploreEventKind.TurnBack:
                    if (Time.time - _turnBackShown > 8f)
                    {
                        _turnBackShown = Time.time;
                        ShowCall(SpaceStrings.TurnBack, null, 2.2f);
                    }
                    break;
            }
        }

        static int Suits(int found)
        {
            int n = 1;
            while (n < GalaxyProgress.SuitAt.Length && found >= GalaxyProgress.SuitAt[n]) n++;
            return n;
        }

        static Color Colour(Rgb c) => new Color(c.R, c.G, c.B);

        // ---------------------------------------------------------------- the map

        void Map(float dt)
        {
            if (_reachable.Count == 0) return;
            int move = 0;
            Vector2 dir = Vector2.zero;
            if (RemoteInput.Left) dir = Vector2.left;
            else if (RemoteInput.Right) dir = Vector2.right;
            else if (RemoteInput.Up) dir = Vector2.down;     // the screen's up (y grows down)
            else if (RemoteInput.Down) dir = Vector2.up;
            if (dir == Vector2.zero && !UsingKeys && !Autoplay)
            {
                // the body: a lean held moves the cursor (again every 0.8 s while it's held)
                float steer = 0f;
                int n = 0;
                for (int k = 0; k < Players; k++)
                    if (_humans[k].Body != null)
                    {
                        steer += _humans[k].Moves.Steer;
                        n++;
                    }
                if (n > 0) steer /= n;
                if (Mathf.Abs(steer) > 0.5f)
                {
                    if (_leanSince < 0f)
                    {
                        _leanSince = Time.time;
                        _leanRepeat = Time.time + 0.3f;
                    }
                    if (Time.time >= _leanRepeat)
                    {
                        dir = steer > 0f ? Vector2.right : Vector2.left;
                        _leanRepeat = Time.time + 0.85f;
                    }
                }
                else _leanSince = -1f;
            }
            if (dir != Vector2.zero)
            {
                int next = Nearest(_cursor, dir);
                if (next >= 0 && next != _cursor)
                {
                    _cursor = next;
                    GameAudio.Sfx("ui_move", 0.5f);
                }
            }
            // flying there: OK, or both arms up held (the map's first second ignores them: they may still be up)
            bool armsUp = false;
            for (int k = 0; k < Players; k++) armsUp |= _humans[k].Body != null && _humans[k].Moves.Boosting;
            if (armsUp && _phaseTime > 1f)
            {
                if (_armsUpSince < 0f) _armsUpSince = Time.time;
            }
            else _armsUpSince = -1f;
            bool go = RemoteInput.Confirm || (_armsUpSince > 0f && Time.time - _armsUpSince > 0.7f) || (Autoplay && _phaseTime > 2f);
            if (go && _phaseTime > 0.3f) JumpTo(_cursor);
        }

        /// <summary>The reachable system nearest <paramref name="from"/> on the map in screen direction
        /// <paramref name="dir"/> (y down), -1 for none.</summary>
        int Nearest(int from, Vector2 dir)
        {
            var at = MapPoint(Galaxy.All[from].Map, 1f);
            int best = -1;
            float bestScore = float.MaxValue;
            foreach (int id in _reachable)
            {
                if (id == from) continue;
                var d = MapPoint(Galaxy.All[id].Map, 1f) - at;
                float along = Vector2.Dot(d, dir);
                if (along <= 0.01f) continue;
                float across = Mathf.Abs(d.x * dir.y - d.y * dir.x);
                float score = along + across * 2.2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = id;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- the warp

        const float WarpTime = 2.6f, WarpSwitch = 1.25f;

        float WarpStretch() => 1f + 30f * Mathf.Sin(Mathf.Clamp01(_phaseTime / WarpTime) * Mathf.PI);

        void Warp(float dt)
        {
            float before = _phaseTime - dt;
            if (before < WarpSwitch && _phaseTime >= WarpSwitch && _jumpTo >= 0)
            {
                _explorer.Enter(_jumpTo);
                _world.Show(_explorer.System);
                _jumpTo = -1;
                _warpFlash = 1f;
                _camSet = false;
                Save();
                Debug.Log($"[Space] Into {_explorer.System.Name} ({_explorer.System.Star}, {_explorer.System.Planets.Count} planets, {_progress.FoundIn(_explorer.System)} found)");
            }
            _explorer.Step(dt * 0.35f, 0f, false, false);
            _explorer.Events.Clear();
            if (_phaseTime >= WarpTime)
            {
                ShowCall(SpaceStrings.System(_explorer.System.Name), SpaceStrings.StarName(_explorer.System.Star), 2.6f);
                GameAudio.Sfx("whoosh", 0.7f);
                SetPhase(Phase.Flying);
            }
        }

        // ---------------------------------------------------------------- players and the menu

        /// <summary>The box's pose AI: two people while two fly or two are chosen, else one.</summary>
        void AskForPeople()
        {
            bool one = (Current == Phase.Waiting ? _menu.Mode : Mode) == SpaceMode.Solo;
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
            int suit = _progress.Suit;
            if (_menu.Handle(up, down, left, right, camera, _progress, out bool moved))
            {
                GameAudio.Sfx("ui_select", 0.4f);
                _readySince = -1f;
                _slots.ClearReady();
                if (suit != _progress.Suit) Save();
                Mode = _menu.Mode;
                Dress();
            }
            else if (moved) GameAudio.Sfx("ui_move", 0.5f);
            if (_input != null && camera) _input.PreferBody();
            return true;
        }

        bool ReadyToGo(SpaceMode mode, bool allowAlone, out bool alone)
        {
            alone = false;
            if (mode == SpaceMode.Solo)
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

        bool TwoPlayersPaused()
        {
            for (int k = 0; k < 2; k++)
                if (!_slots[k].SeenWithin(0.6f) || !_slots[k].Calibrated) return true;
            return false;
        }

        Body BodyOf(int slot) => Mode == SpaceMode.Solo
            ? (slot == 0 && MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null)
            : _slots[slot].Body;

        void ReadBodies()
        {
            bool waiting = Current == Phase.Waiting;
            int players = waiting ? (_slots.Present == 2 && _menu.Mode == SpaceMode.Together ? 2 : 1) : Players;
            for (int k = 0; k < players; k++) ReadBody(k, waiting && players == 2 ? _slots[k].Body : BodyOf(k));
        }

        /// <summary>A new camera sample of player <paramref name="slot"/> for their reader (raw joints); the arms drawn come
        /// from the smooth joints shown on the camera's clock.</summary>
        void ReadBody(int slot, Body body)
        {
            var h = _humans[slot];
            if (body != h.Body)
            {
                h.Body = body;
                h.Moves.Reset();
                h.LastSample = 0;
            }
            if (body == null || !body.IsTracking)
            {
                h.Left = h.Right = null;
                return;
            }
            Palms(body, h);
            if (body.SampleTime == h.LastSample) return;
            h.LastSample = body.SampleTime;
            var raw = body.Raw;
            var vis = body.Visibility;
            var sample = new SpaceBodySample
            {
                LeftShoulder = raw[(int)Joint.LeftShoulder], RightShoulder = raw[(int)Joint.RightShoulder],
                LeftWrist = raw[(int)Joint.LeftWrist], RightWrist = raw[(int)Joint.RightWrist],
                LeftHip = raw[(int)Joint.LeftHip], RightHip = raw[(int)Joint.RightHip], Nose = raw[(int)Joint.Nose],
                LeftWristVisibility = vis[(int)Joint.LeftWrist], RightWristVisibility = vis[(int)Joint.RightWrist],
                HipVisibility = Mathf.Min(vis[(int)Joint.LeftHip], vis[(int)Joint.RightHip]),
                NoseVisibility = vis[(int)Joint.Nose],
                Torso = body.FastTorsoLength,
            };
            h.Moves.Update(body.SampleTime, sample);
        }

        /// <summary>The palms round the shoulders' middle as drawn (the smooth joints on the camera's clock).</summary>
        static void Palms(Body body, Human h)
        {
            var j = body.Shown;
            var ls = (Vector2)j[(int)Joint.LeftShoulder];
            var rs = (Vector2)j[(int)Joint.RightShoulder];
            var sh = (ls + rs) * 0.5f;
            var hips = ((Vector2)j[(int)Joint.LeftHip] + (Vector2)j[(int)Joint.RightHip]) * 0.5f;
            float torso = Mathf.Max(1e-4f, Vector2.Distance(sh, hips));
            Vector2? Palm(Joint wrist, Joint elbow)
            {
                if (!body.IsVisible(wrist, 0.3f)) return null;
                var w = (Vector2)j[(int)wrist];
                var e = (Vector2)j[(int)elbow];
                return (w + (w - e) * 0.3f - sh) / torso;
            }
            h.Left = Palm(Joint.LeftWrist, Joint.LeftElbow);
            h.Right = Palm(Joint.RightWrist, Joint.RightElbow);
        }

        // ---------------------------------------------------------------- music

        /// <summary>The songs one after another while flying (the menu has its own loop).</summary>
        void KeepPlaying()
        {
            if (Current == Phase.Waiting || _paused || _music.isPlaying) return;
            PlayNext(true);
        }

        void PlayNext(bool advance)
        {
            if (advance) _song = (_song + 1) % Playlist.Length;
            var clip = Resources.Load<AudioClip>($"Music/{Playlist[Mathf.Clamp(_song, 0, Playlist.Length - 1)]}");
            if (clip == null) return;
            _music.clip = clip;
            _music.volume = GameAudio.MusicVolume * 0.85f;
            _music.Play();
        }

        // ---------------------------------------------------------------- the astronauts

        void UpdateAstronauts(float dt)
        {
            bool flying = Current is Phase.Flying or Phase.Warp or Phase.Finale or Phase.Map;
            float turn = dt > 0f ? Mathf.DeltaAngle(_lastHeading * Mathf.Rad2Deg, _explorer.Heading * Mathf.Rad2Deg) / dt : 0f;
            _lastHeading = _explorer.Heading;
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(turn * 0.55f, -32f, 32f), 1f - Mathf.Exp(-4f * dt));
            float boost = _explorer.Boost;
            float yaw = _explorer.Heading * Mathf.Rad2Deg;
            var frame = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, -_bank);
            float bob = Mathf.Sin(Time.time * 1.4f) * 0.22f;
            for (int i = 0; i < Players && i < _astronauts.Count; i++)
            {
                var a = _astronauts[i];
                if (a == null) continue;
                var h = _humans[i];
                float side = Players > 1 ? (i == 0 ? -1.5f : 1.5f) : 0f;
                var pos = SpaceWorld.World(_explorer.Position, bob + (i == 1 ? 0.3f * Mathf.Sin(Time.time * 1.1f + 1f) : 0f))
                          + Quaternion.Euler(0f, yaw, 0f) * new Vector3(side, 0f, 0f);
                Vector2? left = h.Left, right = h.Right;
                if (Autoplay || UsingKeys || h.Body == null)
                {
                    // without a body: arms up while boosting, held out a little otherwise
                    left = boost > 0.5f ? new Vector2(-0.25f, 1.1f) : new Vector2(-0.75f, -0.55f);
                    right = boost > 0.5f ? new Vector2(0.25f, 1.1f) : new Vector2(0.75f, -0.55f);
                }
                a.FlyTilt = flying ? 24f + 34f * boost : 0f;
                float slow = flying && _explorer.Speed < Explorer.Look - 2f ? 0.4f : 0f;
                a.Pose(pos, frame, flying, left, right, Current == Phase.Waiting ? 0f : slow, -_bank * 0.12f, flying ? 0.55f + 0.95f * boost : 0.3f, dt);
            }
            // the sun lights them from the star
            if (sun != null && _astronauts.Count > 0 && _astronauts[0] != null)
            {
                var to = _astronauts[0].transform.position;
                if (to.sqrMagnitude > 1f) sun.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                sun.color = _world.StarLight;
            }
        }

        // ---------------------------------------------------------------- the camera

        void UpdateCamera(float dt)
        {
            if (cam == null || _astronauts.Count == 0 || _astronauts[0] == null) return;
            _fovKick = Mathf.MoveTowards(_fovKick, 0f, dt * 1.1f);
            _shake = Mathf.Max(0f, _shake - dt);
            _warpFlash = Mathf.MoveTowards(_warpFlash, 0f, dt * 1.6f);
            bool two = Players > 1;
            var astro = _astronauts[0].transform.position;
            if (two && _astronauts.Count > 1 && _astronauts[1] != null) astro = (astro + _astronauts[1].transform.position) * 0.5f;
            var yaw = Quaternion.Euler(0f, _explorer.Heading * Mathf.Rad2Deg, 0f);
            var fwd = yaw * Vector3.forward;
            Vector3 pos, look;
            float fov, roll = 0f;
            if (Current == Phase.Waiting)
            {
                // in front of the astronaut, the planet it hovers by behind it (swaying a little); the astronaut right of
                // the middle (the words are on the left)
                var centre = _home != null ? SpaceWorld.CentreOf(_home) : Vector3.zero;
                var away = astro - centre;
                away.y = 0f;
                away = away.sqrMagnitude > 1f ? away.normalized : Vector3.back;
                float sway = 14f * Mathf.Sin(Time.time * 0.25f);
                var dir = Quaternion.Euler(0f, 10f + 0.6f * sway, 0f) * away;
                pos = astro + dir * (two ? 7.2f : 4.6f) + Vector3.up * 1.1f;
                var right = Vector3.Cross(Vector3.up, (astro - pos).normalized);
                look = astro + Vector3.up * 0.55f - right * (two ? 2.6f : 1.75f);
                fov = 50f;
            }
            else
            {
                // chasing close behind and above, a little more from above while boosting; while scanning, turned toward
                // the planet (it fills one side of the screen as the astronaut swings round it)
                float boost = _explorer.Boost;
                float back = (two ? 5.6f : 3.7f) + 0.5f * boost, up = (two ? 1.9f : 1.25f) + 0.15f * boost;
                pos = astro - fwd * back + Vector3.up * up;
                look = astro + fwd * 20f + Vector3.up * (two ? 2.8f : 2.3f);
                var p = _explorer.Scanning ?? (_explorer.Target != null && _explorer.Distance(_explorer.Target) < Explorer.FindReach(_explorer.Target) * 1.6f ? _explorer.Target : null);
                // a planet just found stays in view while its card shows
                if (p == null && _card != null && Time.time - _cardAt < 2.5f && _card.System == _explorer.System.Id) p = _card;
                if (p != null && Current == Phase.Flying)
                {
                    // turned toward the planet, and moved out to its far side a little: the planet and the astronaut both
                    // in the picture
                    var centre = SpaceWorld.CentreOf(p);
                    float near = Mathf.Clamp01(1f - (_explorer.Distance(p) - Explorer.OrbitRadius(p)) / (Explorer.FindReach(p) * 1.2f));
                    var away = astro - centre;
                    away.y = 0f;
                    if (away.sqrMagnitude > 1f) pos += away.normalized * (2.2f * near);
                    look = Vector3.Lerp(look, centre, 0.2f * near);
                }
                fov = (two ? 62f : 58f) + 8f * boost + 6f * _fovKick;
                if (Current == Phase.Warp)
                {
                    float w = Mathf.Sin(Mathf.Clamp01(_phaseTime / WarpTime) * Mathf.PI);
                    fov += 45f * w;
                    pos -= fwd * (3f * w);
                }
                if (Current == Phase.Finale)
                {
                    float a = _phaseTime * 9f;
                    var round = Quaternion.Euler(0f, _explorer.Heading * Mathf.Rad2Deg + 180f + a, 0f);
                    pos = astro + round * new Vector3(0f, 1.6f, -9f);
                    look = astro + Vector3.up * 1.2f;
                    fov = 56f;
                }
                roll = -_bank * 0.45f;
            }
            // what eases is the camera's place and aim relative to the astronaut: a fast flight never leaves it behind
            var offset = pos - astro;
            var aim = look - astro;
            if (!_camSet)
            {
                _camOffset = offset;
                _camAim = aim;
                _camRot = Quaternion.LookRotation(aim - offset, Vector3.up);
                cam.fieldOfView = fov;
                _camSet = true;
            }
            float k = Current == Phase.Waiting ? 1f - Mathf.Exp(-3f * dt) : 1f - Mathf.Exp(-5f * dt);
            _camOffset = Vector3.Lerp(_camOffset, offset, k);
            _camAim = Vector3.Lerp(_camAim, aim, k);
            _camPos = astro + _camOffset;
            var want = Quaternion.LookRotation(_camAim - _camOffset, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
            _camRot = Quaternion.Slerp(_camRot, want, 1f - Mathf.Exp(-8f * dt));
            var shake = (Vector3)(Random.insideUnitCircle * (0.25f * _shake));
            cam.transform.SetPositionAndRotation(_camPos + shake, _camRot);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, Mathf.Min(fov, 120f), 1f - Mathf.Exp(-5f * dt));
        }

        Vector3 _camOffset, _camAim;

        // ---------------------------------------------------------------- HUD

        /// <summary>Which parts a pass over the HUD's code draws: while flying and on the map its pictures go into one mesh
        /// first (<see cref="HudBatch"/>), then its words; the other screens draw everything with IMGUI.</summary>
        enum HudPass { All, Pictures, Words }

        HudPass _pass;
        HudBatch _hud;
        bool _batchWords;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            float w = GameHud.Begin();
            bool screen = Current is Phase.Flying or Phase.Map or Phase.Warp;
            bool batch = _hud != null && screen && !_paused && PerfSwitches.Get("hudbatch") != "0";
            _batchWords = batch && PerfSwitches.Get("hudtext") != "0";
            if (batch)
            {
                _hud.Clear();
                _pass = HudPass.Pictures;
                DrawHud(w);
                _hud.Draw(GUI.matrix);
                _pass = HudPass.Words;
            }
            DrawHud(w);
            _pass = HudPass.All;
        }

        void DrawHud(float w)
        {
            switch (Current)
            {
                case Phase.Waiting:
                    if (_pass == HudPass.All) DrawWaiting(w);
                    break;
                case Phase.Flying:
                    if (_paused)
                    {
                        if (_pass != HudPass.All) break;
                        if (Mode == SpaceMode.Solo) PlayerGate.DrawPaused(w, _input);
                        else DrawTwoPaused(w);
                        break;
                    }
                    DrawFlight(w);
                    if (Mode == SpaceMode.Solo && !UsingKeys && !Autoplay && _pass == HudPass.Words) PlayerGate.DrawEdgeWarning(w, _input);
                    break;
                case Phase.Map:
                    DrawMap(w);
                    break;
                case Phase.Warp:
                    DrawWarp(w);
                    break;
                case Phase.Finale:
                    if (_pass == HudPass.All) DrawFinale(w);
                    break;
            }
            if (_pass == HudPass.Pictures) return;
            if (_call != null && Time.time >= _callFrom && Time.time < _callUntil && Current == Phase.Flying && !_paused) DrawCall(w);
        }

        // ------------------------------------------------ flying

        string _countText, _hereText;
        int _countShown = -1, _hereShown = -1;

        void DrawFlight(float w)
        {
            var night = new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.74f);
            // top left: the planets found in the galaxy, the system and its count
            int found = _progress.Found.Count, here = _progress.FoundIn(_explorer.System);
            if (found != _countShown || _countText == null)
            {
                _countShown = found;
                _countText = SpaceStrings.FoundOf(found, Galaxy.Planets.Count);
            }
            if (here != _hereShown || _hereText == null)
            {
                _hereShown = here;
                _hereText = $"{_explorer.System.Name} · {SpaceStrings.Here(here, _explorer.System.Planets.Count)}";
            }
            var box = new Rect(36f, 28f, 420f, 84f);
            Picture(box, "capsule", night);
            Picture(new Rect(box.x + 14f, box.y + 12f, 60f, 60f), "planet_home", Color.white);
            Words(new Rect(box.x + 86f, box.y - 2f, box.width - 100f, 52f), SpaceStrings.Planets, 26, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft, 0f);
            Words(new Rect(box.x + 86f, box.y + 30f, box.width - 100f, 56f), _countText, 44, Color.white, TextAnchor.MiddleLeft, 0f);
            Words(new Rect(44f, box.yMax + 4f, 700f, 44f), _hereText, 28, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft, 2f);
            DrawRadar(w);
            DrawTarget(w);
            DrawCard(w);
        }

        /// <summary>The system's radar, north up: the sun in the middle, the planets (found: their colour; not found: a
        /// white dot, the target ringed), the astronaut an arrow.</summary>
        void DrawRadar(float w)
        {
            const float size = 300f;
            var r = new Rect(w - size - 36f, 28f, size, size);
            var c = r.center;
            Picture(r, "disc", new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.72f));
            var s = _explorer.System;
            float scale = (size * 0.46f) / s.Edge;
            Vector2 P(Vector2 plane) => new Vector2(c.x + plane.x * scale, c.y - plane.y * scale);
            // orbits as faint rings of dots
            foreach (var p in s.Planets)
            {
                float rr = p.Orbit * scale;
                int dots = Mathf.Clamp(Mathf.RoundToInt(rr * 0.35f), 12, 48);
                for (int i = 0; i < dots; i++)
                {
                    float a = i / (float)dots * Mathf.PI * 2f;
                    Picture(new Rect(c.x + Mathf.Cos(a) * rr - 1.5f, c.y + Mathf.Sin(a) * rr - 1.5f, 3f, 3f), "disc", new Color(1f, 1f, 1f, 0.14f));
                }
            }
            var (edge, _, _) = SpaceWorld.StarColours(s.Star);
            Picture(new Rect(c.x - 13f, c.y - 13f, 26f, 26f), "disc", new Color(Mathf.Min(1f, edge.r), Mathf.Min(1f, edge.g), Mathf.Min(1f, edge.b)));
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            foreach (var p in s.Planets)
            {
                var at = P(p.Position);
                float d = Mathf.Clamp(p.Radius * scale * 2.4f, 12f, 30f);
                if (_explorer.Found(p)) Picture(new Rect(at.x - d / 2f, at.y - d / 2f, d, d), "disc", Colour(p.Look.Main));
                else
                {
                    Picture(new Rect(at.x - d / 2f, at.y - d / 2f, d, d), "disc", new Color(1f, 1f, 1f, 0.55f + 0.35f * pulse));
                    if (p == _explorer.Target)
                    {
                        float ring = d + 16f + 6f * pulse;
                        Picture(new Rect(at.x - ring / 2f, at.y - ring / 2f, ring, ring), "ring", Sun);
                    }
                }
            }
            // the astronaut: an arrow along the heading
            var me = P(_explorer.Position);
            me = new Vector2(Mathf.Clamp(me.x, r.x + 10f, r.xMax - 10f), Mathf.Clamp(me.y, r.y + 10f, r.yMax - 10f));
            float deg = _explorer.Heading * Mathf.Rad2Deg;
            ImageTurned(new Rect(me.x - 17f, me.y - 17f, 34f, 34f), "arrow", PlayerColors[0], deg - 90f);
        }

        /// <summary>The target planet: a ring round it with its distance while it's on the screen, else an arrow at the
        /// screen's edge toward it; while scanning, the ring fills.</summary>
        void DrawTarget(float w)
        {
            var p = _explorer.Scanning ?? _explorer.Target;
            if (p == null || cam == null) return;
            var centre = SpaceWorld.CentreOf(p);
            var sp = cam.WorldToScreenPoint(centre);
            bool scanning = _explorer.Scanning == p;
            float h = GameHud.Height;
            var g = GameHud.FromScreen(sp);
            bool onScreen = sp.z > 0f && g.x > 40f && g.x < w - 40f && g.y > 40f && g.y < h - 40f;
            if (onScreen)
            {
                // the planet's radius on the screen
                var edge = GameHud.FromScreen(cam.WorldToScreenPoint(centre + cam.transform.up * p.Radius));
                float rad = Mathf.Clamp(Vector2.Distance(g, edge) * 1.18f + 14f, 34f, h * 0.48f);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
                int dots = 40;
                float fill = scanning ? _explorer.ScanOf(p) : 0f;
                for (int i = 0; i < dots; i++)
                {
                    float a = -Mathf.PI / 2f + i / (float)dots * Mathf.PI * 2f;
                    bool lit = scanning && i < fill * dots;
                    float ds = lit ? 13f : 9f;
                    var colour = lit ? Scan : new Color(1f, 1f, 1f, scanning ? 0.35f : 0.28f + 0.3f * pulse);
                    Picture(new Rect(g.x + Mathf.Cos(a) * rad - ds / 2f, g.y + Mathf.Sin(a) * rad - ds / 2f, ds, ds), "disc", colour);
                }
                string label = scanning ? $"{SpaceStrings.Scanning} {Mathf.RoundToInt(fill * 100f)}%" : SpaceStrings.Metres(Mathf.Max(0f, _explorer.Distance(p) - Explorer.FindReach(p)));
                float ly = Mathf.Min(g.y + rad + 8f, h - 70f);
                Words(new Rect(g.x - 300f, ly, 600f, 50f), label, scanning ? 34 : 30, scanning ? Scan : Color.white, TextAnchor.MiddleCenter, 3f);
                if (!scanning) Words(new Rect(g.x - 60f, g.y - 40f, 120f, 80f), "?", 64, new Color(1f, 1f, 1f, 0.55f + 0.3f * pulse), TextAnchor.MiddleCenter, 3f);
                return;
            }
            // off the screen: an arrow at its edge, toward the planet (behind the camera: mirrored)
            var mid = new Vector2(w / 2f, h / 2f);
            var dir = g - mid;
            if (sp.z < 0f) dir = -dir;
            if (dir.sqrMagnitude < 1f) dir = Vector2.up;
            dir.Normalize();
            float t = Mathf.Min((w / 2f - 90f) / Mathf.Max(1e-3f, Mathf.Abs(dir.x)), (h / 2f - 90f) / Mathf.Max(1e-3f, Mathf.Abs(dir.y)));
            var at = mid + dir * t;
            float deg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float bounce = 8f * Mathf.Sin(Time.time * 6f);
            at += dir * bounce;
            ImageTurned(new Rect(at.x - 50f, at.y - 45f, 100f, 90f), "arrow", Sun, deg);
            Words(new Rect(at.x - 150f, at.y + (dir.y > 0.5f ? -100f : 44f), 300f, 44f), SpaceStrings.Metres(_explorer.Distance(p)), 28, Sun, TextAnchor.MiddleCenter, 3f);
        }

        /// <summary>The card of a planet just found: its name, its kind, the count; a new suit when one opened.</summary>
        void DrawCard(float w)
        {
            if (_card == null) return;
            float age = Time.time - _cardAt;
            if (age > 4.2f) return;
            float appear = Mathf.Clamp01(age / 0.25f), leave = Mathf.Clamp01((4.2f - age) / 0.4f);
            float a = Mathf.Min(appear, leave);
            float pop = 1f + 0.25f * Mathf.Exp(-age * 9f);
            // left of the middle, under the count (the planet just found is in the middle of the picture)
            var r = new Rect(36f, 180f, 720f, 196f);
            Picture(r, "capsule", new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.82f * a));
            var colour = Colour(_card.Look.Main);
            Picture(new Rect(r.x + 26f, r.y + 34f, 128f, 128f), "glow", new Color(colour.r, colour.g, colour.b, 0.6f * a));
            Picture(new Rect(r.x + 50f, r.y + 58f, 80f, 80f), "disc", new Color(colour.r, colour.g, colour.b, a));
            Picture(new Rect(r.x + 58f, r.y + 62f, 52f, 52f), "glow", new Color(1f, 1f, 1f, 0.45f * a));
            Picture(new Rect(r.x + 82f, r.y + 92f, 46f, 44f), "disc", new Color(0f, 0f, 0.1f, 0.25f * a));
            Words(new Rect(r.x + 170f, r.y + 8f, r.width - 190f, 48f), _card.Rare ? $"{SpaceStrings.NewPlanet}  ★ {SpaceStrings.Rare}" : SpaceStrings.NewPlanet, 30, new Color(Sun.r, Sun.g, Sun.b, a), TextAnchor.MiddleLeft, 0f);
            Words(new Rect(r.x + 170f, r.y + 50f, r.width - 190f, 80f), _card.Name, 64, new Color(1f, 1f, 1f, a), TextAnchor.MiddleLeft, 0f, pop);
            Words(new Rect(r.x + 170f, r.y + 128f, r.width - 190f, 52f), $"{SpaceStrings.TypeName(_card.Type)} · {SpaceStrings.FoundOf(_cardCount, Galaxy.Planets.Count)}", 30, new Color(1f, 1f, 1f, 0.8f * a), TextAnchor.MiddleLeft, 0f);
            if (_cardSuit) Words(new Rect(r.x, r.yMax + 6f, r.width, 50f), SpaceStrings.NewSuit, 34, new Color(Sun.r, Sun.g, Sun.b, a), TextAnchor.MiddleCenter, 3f);
        }

        void DrawCall(float w)
        {
            float age = Time.time - _callFrom;
            float pop = 1f + 0.25f * Mathf.Exp(-age * 10f);
            int size = _call.Length > 26 ? 58 : 76;
            const float y = 560f;
            Words(new Rect(0f, y - 70f, w, 140f), _call, size, Sun, TextAnchor.MiddleCenter, 4f, pop);
            if (_callSub != null) Words(new Rect(0f, y + 56f, w, 60f), _callSub, 34, Color.white, TextAnchor.MiddleCenter, 3f);
        }

        void ShowCall(string call, string sub, float seconds, float delay = 0f)
        {
            _call = call;
            _callSub = sub;
            _callFrom = Time.time + delay;
            _callUntil = _callFrom + seconds;
        }

        // ------------------------------------------------ the map

        /// <summary>The galaxy's disc on the screen: its middle and radius (the disc of radius 1 of <see cref="StarSystem.Map"/>).</summary>
        static Vector2 MapPoint(Vector2 map, float w) => new Vector2(MapCentre.x + map.x * MapRadius, MapCentre.y - map.y * MapRadius);
        static readonly Vector2 MapCentre = new Vector2(1180f, 560f);
        const float MapRadius = 470f;

        void DrawMap(float w)
        {
            float h = GameHud.Height;
            Picture(new Rect(0f, 0f, w, h), "white", new Color(0.03f, 0.03f, 0.1f, 0.94f));
            // the galaxy's light: a glow round its heart and round every star (the arms show by themselves)
            Picture(new Rect(MapCentre.x - 330f, MapCentre.y - 330f, 660f, 660f), "glow", new Color(1f, 0.85f, 0.65f, 0.35f));
            foreach (var s in Galaxy.All)
            {
                var at = MapPoint(s.Map, w);
                var (edge, _, _) = SpaceWorld.StarColours(s.Star);
                Picture(new Rect(at.x - 130f, at.y - 130f, 260f, 260f), "glow", new Color(Mathf.Min(1f, edge.r * 0.7f), Mathf.Min(1f, edge.g * 0.7f), Mathf.Min(1f, edge.b * 0.9f), 0.13f));
            }
            // the routes between known systems
            foreach (var s in Galaxy.All)
            {
                bool known = _progress.Reachable(Galaxy, s.Id);
                if (!known) continue;
                foreach (int n in s.Links)
                {
                    if (n < s.Id || !_progress.Reachable(Galaxy, n)) continue;
                    bool both = _progress.Visited.Contains(s.Id) && _progress.Visited.Contains(n);
                    Line(MapPoint(s.Map, w), MapPoint(Galaxy.All[n].Map, w), both ? 4f : 2.5f, new Color(0.7f, 0.85f, 1f, both ? 0.55f : 0.22f));
                }
            }
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            foreach (var s in Galaxy.All)
            {
                var at = MapPoint(s.Map, w);
                var (edge, _, _) = SpaceWorld.StarColours(s.Star);
                var star = new Color(Mathf.Min(1f, edge.r), Mathf.Min(1f, edge.g), Mathf.Min(1f, edge.b));
                bool visited = _progress.Visited.Contains(s.Id), reachable = _progress.Reachable(Galaxy, s.Id);
                if (!reachable)
                {
                    Picture(new Rect(at.x - 7f, at.y - 7f, 14f, 14f), "disc", new Color(star.r, star.g, star.b, 0.4f));
                    continue;
                }
                if (visited && _progress.Complete(s))
                {
                    float ring = 46f;
                    Picture(new Rect(at.x - ring / 2f, at.y - ring / 2f, ring, ring), "ring", new Color(Sun.r, Sun.g, Sun.b, 0.85f));
                }
                float size = visited ? 24f : 18f;
                Picture(new Rect(at.x - size, at.y - size, size * 2f, size * 2f), "glow", new Color(star.r, star.g, star.b, visited ? 0.7f : 0.45f));
                Picture(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), "disc", visited ? star : new Color(star.r, star.g, star.b, 0.75f));
                if (!visited) continue;
                // its planets: a dot each round it, coloured when found
                int count = s.Planets.Count;
                for (int i = 0; i < count; i++)
                {
                    var p = s.Planets[i];
                    float a = -Mathf.PI / 2f + i / (float)count * Mathf.PI * 2f;
                    var pa = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 30f;
                    bool f = _progress.Found.Contains(p.Id);
                    Picture(new Rect(pa.x - 5.5f, pa.y - 5.5f, 11f, 11f), "disc", f ? Colour(p.Look.Main) : new Color(1f, 1f, 1f, 0.28f));
                }
            }
            // where they are, the cursor
            var here = MapPoint(_explorer.System.Map, w);
            float hr = 58f + 6f * pulse;
            Picture(new Rect(here.x - hr / 2f, here.y - hr / 2f, hr, hr), "ring", new Color(PlayerColors[0].r, PlayerColors[0].g, PlayerColors[0].b, 0.9f));
            if (_cursor >= 0)
            {
                var at = MapPoint(Galaxy.All[_cursor].Map, w);
                float cr = 82f + 10f * pulse;
                Picture(new Rect(at.x - cr / 2f, at.y - cr / 2f, cr, cr), "ring", Sun);
                float hold = _armsUpSince > 0f ? Mathf.Clamp01((Time.time - _armsUpSince) / 0.7f) : 0f;
                if (hold > 0f)
                {
                    int dots = 24;
                    for (int i = 0; i < dots * hold; i++)
                    {
                        float a = -Mathf.PI / 2f + i / (float)dots * Mathf.PI * 2f;
                        Picture(new Rect(at.x + Mathf.Cos(a) * 56f - 6f, at.y + Mathf.Sin(a) * 56f - 6f, 12f, 12f), "disc", Sun);
                    }
                }
            }
            // the words: the title and the count, the system chosen, how to choose
            Words(new Rect(60f, 40f, 760f, 90f), SpaceStrings.Map, 66, Sun, TextAnchor.MiddleLeft, 3f);
            Words(new Rect(62f, 130f, 760f, 50f), $"{SpaceStrings.Planets}: {SpaceStrings.FoundOf(_progress.Found.Count, Galaxy.Planets.Count)} · {SpaceStrings.TimePlayed(_progress.Seconds)}", 32, Color.white, TextAnchor.MiddleLeft, 2f);
            if (_cursor >= 0)
            {
                var s = Galaxy.All[_cursor];
                var panel = new Rect(60f, 330f, 560f, 250f);
                Picture(panel, "white", new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.88f));
                Picture(new Rect(panel.x, panel.y, panel.width, 6f), "white", Sun);
                Words(new Rect(panel.x + 30f, panel.y + 18f, panel.width - 60f, 70f), s.Name, 54, Color.white, TextAnchor.MiddleLeft, 0f);
                Words(new Rect(panel.x + 30f, panel.y + 88f, panel.width - 60f, 46f), SpaceStrings.StarName(s.Star), 30, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft, 0f);
                string status = !_progress.Visited.Contains(s.Id) ? SpaceStrings.NotVisited
                    : _progress.Complete(s) ? $"{SpaceStrings.Explored} · {SpaceStrings.PlanetsIn(s.Planets.Count, s.Planets.Count)}"
                    : SpaceStrings.PlanetsIn(_progress.FoundIn(s), s.Planets.Count);
                Words(new Rect(panel.x + 30f, panel.y + 136f, panel.width - 60f, 46f), status, 30, Sun, TextAnchor.MiddleLeft, 0f);
                if (s.Id == _explorer.System.Id) Words(new Rect(panel.x + 30f, panel.y + 184f, panel.width - 60f, 46f), SpaceStrings.YouAreHere, 28, PlayerColors[0], TextAnchor.MiddleLeft, 0f);
                else Words(new Rect(panel.x + 30f, panel.y + 184f, panel.width - 60f, 46f), SpaceStrings.FlyTo(s.Name), 28, Color.white, TextAnchor.MiddleLeft, 0f);
            }
            Words(new Rect(60f, h - 150f, 900f, 50f), UsingKeys ? SpaceStrings.MapHintRemote : SpaceStrings.MapHintBody, 30, Color.white, TextAnchor.MiddleLeft, 2f);
            if (!UsingKeys) Words(new Rect(60f, h - 100f, 900f, 46f), SpaceStrings.MapHintRemote, 26, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft, 2f);
        }

        void Line(Vector2 a, Vector2 b, float width, Color colour)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;
            var mid = (a + b) * 0.5f;
            ImageTurned(new Rect(mid.x - len / 2f, mid.y - width / 2f, len, width), "white", colour, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        void DrawWarp(float w)
        {
            float h = GameHud.Height;
            // streaks of light rushing out from the middle
            float power = Mathf.Sin(Mathf.Clamp01(_phaseTime / WarpTime) * Mathf.PI);
            var mid = new Vector2(w / 2f, h * 0.45f);
            for (int i = 0; i < 72; i++)
            {
                float a = (i * 137.5f + i * i * 0.37f) * Mathf.Deg2Rad;
                float phase = (_phaseTime * (1.6f + (i % 7) * 0.25f) + i * 0.173f) % 1f;
                float r0 = Mathf.Lerp(80f, w * 0.75f, phase * phase), len = (60f + 520f * power) * (0.3f + phase);
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Line(mid + d * r0, mid + d * (r0 + len), 2f + 4f * phase, new Color(0.75f, 0.9f, 1f, 0.55f * power * (1f - phase * 0.5f)));
            }
            float flash = Mathf.Clamp01(1f - Mathf.Abs(_phaseTime - WarpSwitch) / 0.35f);
            if (flash > 0f) Picture(new Rect(0f, 0f, w, h), "white", new Color(0.85f, 0.95f, 1f, flash));
            if (_jumpTo >= 0) Words(new Rect(0f, 470f, w, 120f), SpaceStrings.FlyTo(Galaxy.All[_jumpTo].Name), 72, Sun, TextAnchor.MiddleCenter, 4f);
        }

        // ------------------------------------------------ the waiting screen and the finale

        void DrawWaiting(float w)
        {
            bool camera = PlayerGate.State(_input) != GateState.NoService;
            bool two = _menu.Mode != SpaceMode.Solo;
            // top left: the game, the goal, how far the trip is
            Label(new Rect(50f, 24f, 800f, 120f), SpaceStrings.Title, 104, Sun, TextAnchor.MiddleLeft);
            Label(new Rect(54f, 140f, 900f, 56f), SpaceStrings.Goal, 40, Color.white, TextAnchor.MiddleLeft);
            float share = _progress.Found.Count / (float)Galaxy.Planets.Count;
            var bar = new Rect(56f, 210f, 560f, 30f);
            SportHud.Rounded(bar, new Color(1f, 1f, 1f, 0.15f), 15f);
            if (share > 0.002f) SportHud.Rounded(new Rect(bar.x, bar.y, Mathf.Max(30f, bar.width * share), bar.height), Sun, 15f);
            Label(new Rect(bar.xMax + 20f, bar.y - 12f, 520f, 54f), $"{SpaceStrings.Planets}: {SpaceStrings.FoundOf(_progress.Found.Count, Galaxy.Planets.Count)}", 32, Color.white, TextAnchor.MiddleLeft);
            Label(new Rect(56f, 250f, 900f, 46f), $"{SpaceStrings.System(_explorer.System.Name)} · {SpaceStrings.TimePlayed(_progress.Seconds)}", 28, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
            // top right: how to fly, a card a move
            const float cw = 500f, ch = 86f;
            float cx = w - cw - 40f, cy = 30f;
            IconText(new Rect(cx, cy, cw, ch), "pose_tilt_right", Scan, SpaceStrings.HowTurn);
            IconText(new Rect(cx, cy + (ch + 10f), cw, ch), "pose_rocket", Sun, SpaceStrings.HowFast);
            IconText(new Rect(cx, cy + 2f * (ch + 10f), cw, ch), "pose_duck", new Color(1f, 0.6f, 0.4f), SpaceStrings.HowSlow);
            IconText(new Rect(cx, cy + 3f * (ch + 10f), cw, ch), "planet_jungle", Color.white, SpaceStrings.HowFind);
            // bottom left: the menu, the prompt above it
            float menuY = GameHud.Height - SpaceMenu.Height - 40f;
            _menu.Draw(60f, menuY, camera, _progress);
            Label(new Rect(60f, menuY - 44f, SpaceMenu.Width, 40f), SpaceStrings.MenuHint, 26, new Color(1f, 1f, 1f, 0.75f));
            if (two)
            {
                if (!DrawPlayerCards(w, menuY - 210f, menuY - 100f)) Label(new Rect(60f, menuY - 96f, SpaceMenu.Width, 46f), SpaceStrings.TogetherModeSub, 30, Color.white);
                return;
            }
            // the prompt above the menu (PlayerGate draws its message at y 300..530 of its own space)
            var old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.Translate(new Vector3(60f, menuY - 50f - 530f, 0f));
            PlayerGate.DrawWaiting(SpaceMenu.Width, _input, "", SpaceStrings.KeyboardHelp);
            GUI.matrix = old;
        }

        /// <summary>A card each (who is who, what each still has to do), and a line when they stand too close or one waits
        /// for the other. True when that line was drawn.</summary>
        bool DrawPlayerCards(float w, float y, float messageY)
        {
            if (_input != null && _input.Gestures != null) _input.Gestures.PlacementShownFrame = Time.frameCount;
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                var r = new Rect(60f + k * (SpaceMenu.Width / 2f + 10f), y, SpaceMenu.Width / 2f - 10f, 100f);
                SportHud.Panel(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.84f), 22f, null, 0f, false);
                SportHud.Rounded(new Rect(r.x + 14f, r.y + 14f, 10f, r.height - 28f), PlayerColors[k], 5f);
                SportHud.Text(new Rect(r.x + 40f, r.y + 4f, r.width - 50f, 48f), SpaceStrings.Player(k), 34, PlayerColors[k], TextAnchor.MiddleLeft, 0f);
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
                else if (!s.Calibrated) status = $"{SpaceStrings.StandStill} {s.CalibrationProgress * 100f:0}%";
                else if (!s.Ready) status = SpaceStrings.RaiseYourHands;
                else
                {
                    status = SpaceStrings.Ready;
                    color = Sun;
                }
                SportHud.Text(new Rect(r.x + 40f, r.y + 50f, r.width - 50f, 44f), status, 26, color, TextAnchor.MiddleLeft, 0f);
            }
            if (_slots.TooClose)
            {
                GameHud.Banner(new Rect(60f, messageY - 8f, SpaceMenu.Width, 60f), SpaceStrings.StepApart, Brand.Current.SecondaryColor);
                return true;
            }
            if (_readySince < 0f) return false;
            int waiting = _slots[0].Ready ? 1 : 0;
            int left = Mathf.Max(1, Mathf.CeilToInt(JoinWait - (Time.time - _readySince)));
            Label(new Rect(60f, messageY, SpaceMenu.Width, 44f), $"{SpaceStrings.WaitingFor(waiting)} {SpaceStrings.PlayingAloneIn(left)}", 28, PlayerColors[waiting]);
            return true;
        }

        void DrawFinale(float w)
        {
            float t = _phaseTime;
            float a = Mathf.Clamp01(t / 0.6f);
            Label(new Rect(0f, 120f, w, 130f), SpaceStrings.Finale, 84, new Color(Sun.r, Sun.g, Sun.b, a));
            Label(new Rect(0f, 250f, w, 70f), SpaceStrings.FinaleSub(Galaxy.Planets.Count, Galaxy.All.Count), 44, new Color(1f, 1f, 1f, a));
            Label(new Rect(0f, 320f, w, 60f), SpaceStrings.TimePlayed(_progress.Seconds), 38, new Color(1f, 1f, 1f, 0.8f * a));
            if (t > 4f) Label(new Rect(0f, GameHud.Height - 160f, w, 60f), SpaceStrings.FlyOn, 36, Sun);
        }

        void DrawTwoPaused(float w)
        {
            for (int k = 0; k < 2; k++)
            {
                var s = _slots[k];
                if (s.SeenWithin(0.6f) && s.Calibrated) continue;
                GameHud.Message(w, SpaceStrings.ComeBackPlayer(k), null, PlayerColors[k]);
                return;
            }
        }

        // ------------------------------------------------ drawing helpers

        /// <summary>A picture of the HUD sheet: into the batch on the pictures pass, IMGUI when not batching, nothing on the
        /// words pass.</summary>
        void Picture(Rect r, string name, Color tint)
        {
            var sheet = _world.Sheet;
            if (sheet == null || !sheet.TryGetValue(name, out var uv)) return;
            if (_pass == HudPass.Pictures)
            {
                _hud.Image(r, uv, tint);
                return;
            }
            if (_pass == HudPass.Words || art.ui == null) return;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(r, art.ui, uv);
            GUI.color = old;
        }

        /// <summary>A picture turned <paramref name="degrees"/> clockwise about its middle.</summary>
        void ImageTurned(Rect r, string name, Color tint, float degrees)
        {
            var sheet = _world.Sheet;
            if (sheet == null || !sheet.TryGetValue(name, out var uv)) return;
            if (_pass == HudPass.Pictures)
            {
                _hud.Image(r, uv, tint, degrees);
                return;
            }
            if (_pass == HudPass.Words || art.ui == null) return;
            var old = GUI.matrix;
            var oldColour = GUI.color;
            var c = r.center;
            GUI.matrix = old * Matrix4x4.TRS(c, Quaternion.Euler(0f, 0f, degrees), Vector3.one) * Matrix4x4.Translate(-c);
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(r, art.ui, uv);
            GUI.color = oldColour;
            GUI.matrix = old;
        }

        void Words(Rect r, string text, int size, Color colour, TextAnchor anchor, float outline, float grow = 1f)
        {
            if (_pass == HudPass.Pictures)
            {
                if (_batchWords) SportHud.Text(_hud, r, text, size, colour, anchor, outline, grow: grow);
            }
            else if (_pass == HudPass.All || !_batchWords) SportHud.Text(r, text, (int)(size * grow), colour, anchor, outline);
        }

        static void Label(Rect r, string text, int size, Color colour, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            SportHud.Text(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), text, size, new Color(0f, 0f, 0f, 0.65f * colour.a), anchor, 0f);
            SportHud.Text(r, text, size, colour, anchor, 0f);
        }

        void IconText(Rect r, string picture, Color tint, string text)
        {
            var sheet = _world.Sheet;
            GameHud.Box(r, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.8f));
            if (sheet != null && sheet.TryGetValue(picture, out var uv) && art.ui != null)
            {
                var old = GUI.color;
                GUI.color = tint;
                GUI.DrawTextureWithTexCoords(new Rect(r.x + 8f, r.y + (r.height - 76f) / 2f, 76f, 76f), art.ui, uv);
                GUI.color = old;
            }
            GUI.Label(new Rect(r.x + 92f, r.y + 2f, r.width - 98f, r.height - 4f), text, CardStyle);
        }

        GUIStyle _cardStyle;
        GUIStyle CardStyle => _cardStyle ??= new GUIStyle(SportHud.Style(24))
        {
            wordWrap = true, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white }, clipping = TextClipping.Clip,
        };
    }
}
