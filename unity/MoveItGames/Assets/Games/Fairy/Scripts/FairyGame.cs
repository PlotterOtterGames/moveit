using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using Joint = MoveIt.Motion.Joint;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// "Zana!" as a garden defence (2026-11-16; the user, not satisfied with the flying version, gave the plan from
    /// another conversation: "Fairy Garden Defense: you're a fairy defending your enchanted garden; dark creatures creep
    /// in from the edges in waves; you cast spells with hand gestures before they reach your fairy tree"). The flight is
    /// kept in D:\Backups\zana-flight-2026-11-16. The rules are <see cref="GardenRun"/>; this draws them and reads the
    /// players:
    /// <list type="bullet">
    /// <item><b>The garden</b> (<see cref="GardenWorld"/>) seen from the front like a stage, the fairy tree in the middle,
    /// the fairy (or two) hovering in front of it like a mirror of the player (<see cref="FairyView"/>), the creatures
    /// (<see cref="GloomViews"/>) coming along the paths from both sides and flying in from the sky.</item>
    /// <item><b>The wand</b> (2026-11-16, the user: "a magic wand as some type of controller like a mouse, and cast spells
    /// by following the shape on the screen"; then "dont make the wand tied up to the character ... freeform; dont allow
    /// the wand to kill enemies by itself, only with spells"): a raised hand points it (<see cref="Wand"/>; the mouse
    /// without a camera), drawn as a magic wand where it points, like a mouse pointer. It does nothing by itself.
    /// <b>Spells are drawn in the air</b> (2026-11-17, the user: "swing the hand in the form of the magic, and then the game
    /// guesses which spell it was, and some sort of countdown between spells"): the wand leaves a glowing trail, a short
    /// stop (or lowering the hand) ends the shape, <see cref="SpellCaster"/> reads it (a lightning bolt, a rainbow's arch,
    /// the shield's circle, the seeds' wave, the big magic's star, the freeze's spiral, the rain's heart:
    /// <see cref="SpellShapes"/>, shown in the spell book along the bottom) and the fairy casts it, the lightning and the
    /// rainbow on the side it was drawn on; then the wand charges again (a ring of stars round it, the seconds left).</item>
    /// <item><b>The fairy</b> (<see cref="FairyView"/>) flies about in front of the tree by herself (<see cref="Wander"/>),
    /// gathers light between her hands while a shape is drawn and throws it when it's cast.</item>
    /// <item><b>Waves</b> with a breather between (stardust, the garden growing a flower, an orb to catch by touching it
    /// with the fairy's hand), the transformation after every fifth (both arms up), until the tree falls: the score is the
    /// waves saved, the stardust opens wings, dresses and the two other spells.</item>
    /// </list>
    /// Two fairies defend together (the box's NPU runs shared, 15 poses a second each).
    /// </summary>
    public sealed partial class FairyGame : MonoBehaviour
    {
        public enum Phase { Waiting, Intro, Playing, Transform, Over }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] Light sun;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: sparkles, bursts, confetti.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the soft dot: puffs, motes.")] Material dotMaterial;
        [SerializeField, Tooltip("Tundu/Wing: the fairies' wings.")] Material wingMaterial;
        [SerializeField] FairyWorldArt art = new FairyWorldArt();
        [SerializeField] GloomArt gloom = new GloomArt();
        [SerializeField] Texture2D ui;
        [SerializeField] TextAsset uiJson;

        const string ProgressKey = "fairy.garden";
        public static readonly Color[] PlayerColors = { new Color(1f, 0.45f, 0.72f), new Color(0.3f, 0.8f, 1f) };
        public const float JoinWait = 4f;
        /// <summary>The stage camera: where it stands and what it looks at (garden metres).</summary>
        /// <summary>The camera while playing: low and close, nearly level (2026-11-16: from 4.6 m up and 14 m from the tree
        /// the fairy was 16 % of the screen's height and a bug ~60 px; now she is ~38 %, the tree's door behind her, the
        /// paths running out to the screen's edges at the tree).</summary>
        static readonly Vector3 CameraAt = new Vector3(0f, 2.6f, -6.5f), CameraLook = new Vector3(0f, 2.7f, 4f);

        public Phase Current { get; private set; }
        public GardenRun Run => _run;
        public GardenProgress Progress => _progress;
        public GardenWorld World => _world;
        public FairyMenu Menu => _menu;
        public PlayerSlots Slots => _slots;
        public IReadOnlyList<FairyView> Fairies => _fairies;
        /// <summary>Every event of the rules as it happens (tests).</summary>
        public event Action<GardenEvent> Happened;
        /// <summary>Every spell cast by drawing its shape (tests): the player, the spell.</summary>
        public event Action<int, Spell> SpellCast;
        /// <summary>Every stroke read (tests, the log): the player, what it read as.</summary>
        public event Action<int, SpellRead> StrokeRead;
        /// <summary>Tests and box measurements: the game plays itself (nothing saved).</summary>
        public bool Autoplay { get; set; }
        /// <summary>Tests: false keeps the family's progress as it was.</summary>
        public bool Saving { get; set; } = true;
        public bool Two => _two;
        public GardenMoves MovesOf(int slot) => _humans[Mathf.Clamp(slot, 0, 1)].Moves;
        /// <summary>Player <paramref name="slot"/>'s wand, and the strokes it draws (tests).</summary>
        public Wand WandOf(int slot) => _humans[Mathf.Clamp(slot, 0, 1)].Wand;
        public SpellCaster CasterOf(int slot) => _humans[Mathf.Clamp(slot, 0, 1)].Caster;
        /// <summary>Spells player <paramref name="slot"/> has cast this run by drawing them (tests).</summary>
        public int CastsBy(int slot) => _castsBy[Mathf.Clamp(slot, 0, 1)];
        /// <summary>Where player <paramref name="slot"/>'s wand points now (HUD pixels), and whether it's up (tests).</summary>
        public bool PointsAt(int slot, out Vector2 px) => CursorOf(slot, out px);
        /// <summary>Spells cast so far this run, by spell (tests).</summary>
        public int CastsOf(Spell s) => _casts[(int)s];
        /// <summary>The last run's end: what it opened (the results screen).</summary>
        public GardenProgress.Opened LastOpened => _opened;

        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        float _mirrorCorner = 0.28f;
        GardenProgress _progress;
        GardenRun _run;
        GardenWorld _world;
        GloomViews _views;
        FairyFx _fx;
        FairyGlows _glows;
        readonly SpellTrails _spells = new SpellTrails();
        readonly List<FairyView> _fairies = new List<FairyView>();
        Mesh[] _wingMeshes;
        readonly FairyMenu _menu = new FairyMenu();
        readonly PlayerSlots _slots = new PlayerSlots { Area = TwoArea };
        Dictionary<string, Rect> _sheet;
        float _phaseTime, _readySince = -1f, _runSeconds;
        bool _paused, _two;
        AudioSource _music;
        string _song;
        float _shake;
        Vector3 _camPos, _camLook;
        bool _camSet;
        readonly int[] _casts = new int[7], _castsBy = new int[2];
        GardenProgress.Opened _opened;
        int _dustBefore;

        // rings and flashes drawn for a moment
        float _seedsAge = 9f, _burstAge = 9f, _treeFlash, _healAge = 9f, _purifySound;
        Vector3 _burstFrom;
        // each player's last stroke read, as the HUD shows it: cast (the stroke turning into its clean shape), read as
        // nothing, drawn while the wand still charged, or a spell not ready yet; and when the wand was last charged
        enum ReadKind { Cast, Fizzle, Wait, NotReady }
        readonly float[] _readAge = { 9f, 9f }, _chargedAge = { 9f, 9f };
        readonly ReadKind[] _readKind = new ReadKind[2];
        readonly SpellOffer[] _readOffer = new SpellOffer[2];
        readonly Vector2[][] _readPath = new Vector2[2][], _readGuide = new Vector2[2][];
        double _spellClock;

        // the fairies: where each is and where she flies to by herself; the game's own wands when it plays itself, the
        // mouse in the editor; each fairy's shape on the screen
        readonly Vector3[] _fly = new Vector3[2], _flyVel = new Vector3[2], _wanderTo = new Vector3[2];
        readonly float[] _wanderWait = new float[2];
        readonly Vector2[] _auto = new Vector2[2];
        readonly List<Vector2>[] _trail = { new List<Vector2>(), new List<Vector2>() };
        Vector2 _mouse;
        float _mouseUntil;
        // the game's own wand drawing a shape: its path, how far along, how long it has rested
        readonly Vector2[][] _autoPath = new Vector2[2][];
        readonly float[] _autoAlong = new float[2], _autoRest = new float[2];
        /// <summary>The soft glow round the wand's star (HUD pixels).</summary>
        public const float WandGlow = 60f;

        sealed class Human
        {
            public readonly GardenMoves Moves = new GardenMoves();
            public readonly Wand Wand = new Wand();
            public readonly SpellCaster Caster = new SpellCaster();
            public double LastSample;
            public float Unit;
            public Body Body;
            public bool ArmsHeld;

            public void Reset()
            {
                Moves.Reset();
                Wand.Reset();
                Caster.Reset();
                LastSample = 0;
                Unit = 0f;
                ArmsHeld = false;
            }
        }
        readonly Human[] _humans = { new Human(), new Human() };

        /// <summary>A hand points the wand all round the body and both go up over the head: the hips in the picture, a
        /// little room to the sides and above the head.</summary>
        static readonly PlayArea Area = new PlayArea(BodyPart.Hips, side: 0.35f, above: 0.45f);
        static readonly PlayArea TwoArea = new PlayArea(BodyPart.Hips, side: 0.3f, above: 0.45f);

        int Players => _two ? 2 : 1;
        Element Element => _progress != null ? _progress.Element : Element.Light;

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            useGUILayout = false;
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            PlayerGate.Use(_input, Area);
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null)
            {
                _mirrorWasVisible = _mirror.Visible;
                _mirrorCorner = _mirror.CornerSize;
                _mirror.CornerSize = 0.15f;   // the paths come in at the screen's bottom corners
            }
            _menu.Load();
            switch (PerfSwitches.Get("mode"))
            {
                case "together": _menu.Two = true; break;
                case "solo": _menu.Two = false; break;
            }
            switch (PerfSwitches.Get("diff"))
            {
                case "easy": _menu.Difficulty = FairyDifficulty.Easy; break;
                case "medium": _menu.Difficulty = FairyDifficulty.Medium; break;
                case "hard": _menu.Difficulty = FairyDifficulty.Hard; break;
            }
            Autoplay |= PerfSwitches.Get("autoplay") == "1";
            var saved = GardenProgress.Load(PlayerPrefs.GetString(ProgressKey, ""));
            _progress = Autoplay ? saved.Clone() : saved;
            if (sun != null) sun.shadows = LightShadows.None;
            if (cam != null)
            {
                cam.farClipPlane = 400f;   // the clouds and the mountains' ends are ~300 m off
                cam.nearClipPlane = 0.2f;
            }
            _sheet = FairyArt.Sheet(uiJson);
            _world = GardenWorld.Create(art);
            _world.SetDecorations(_progress.Decorations);
            _views = GloomViews.Create(gloom, _sheet);
            _fx = FairyFx.Create(sparkMaterial, dotMaterial);
            _glows = new FairyGlows(art.glow);
            _wingMeshes = new Mesh[FairyLooks.AllWings.Length * 2];
            for (int i = 0; i < FairyLooks.AllWings.Length; i++)
            {
                _wingMeshes[i * 2] = FairyArt.WingPanel(FairyLooks.AllWings[i].Shape, true);
                _wingMeshes[i * 2 + 1] = FairyArt.WingPanel(FairyLooks.AllWings[i].Shape, false);
            }
            if (ui != null) _hud = new HudBatch(ui, ui);
            _music = gameObject.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.spatialBlend = 0f;
            _two = _menu.Two;
            NewRun();
            Dress();
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("fairy");
            Debug.Log($"[Fairy] The garden: {_progress.Stardust} stardust, best waves {_progress.Best[0]}/{_progress.Best[1]}/{_progress.Best[2]}, {_progress.Decorations} flowers, {_progress.Seconds / 60f:0} min played");
        }

        void OnDestroy()
        {
            _hud?.Dispose();
            _glows?.Destroy();
            if (_wingMeshes != null)
                foreach (var m in _wingMeshes)
                    if (m != null) Destroy(m);
            if (_mirror != null)
            {
                _mirror.Visible = _mirrorWasVisible;
                _mirror.CornerSize = _mirrorCorner;
            }
        }

        void Save()
        {
            if (Autoplay || !Saving || _progress == null) return;
            PlayerPrefs.SetString(ProgressKey, _progress.Save());
            PlayerPrefs.Save();
        }

        /// <summary>The fairies for the players (player 1 in the dress and wings chosen).</summary>
        void Dress()
        {
            int count = Players;
            while (_fairies.Count < count) _fairies.Add(null);
            for (int i = 0; i < _fairies.Count; i++)
            {
                if (i >= count)
                {
                    if (_fairies[i] != null) _fairies[i].gameObject.SetActive(false);
                    continue;
                }
                var look = i == 0 ? FairyLooks.Player(_progress.Dress) : FairyLooks.Player2();
                int wings = i == 0 ? _progress.Wings : 1;
                if (_fairies[i] == null) _fairies[i] = FairyView.Create(i == 0 ? "Fairy" : "Fairy 2", look, wings, wingMaterial, _wingMeshes, null);
                else
                {
                    _fairies[i].Recolour(look);
                    _fairies[i].SetWings(wings, _wingMeshes);
                }
                _fairies[i].gameObject.SetActive(true);
            }
        }

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            if (phase is Phase.Waiting or Phase.Over) _slots.ClearReady();
            _readySince = -1f;
            Debug.Log($"[Fairy] {phase}");
        }

        /// <summary>A fresh run (its creatures' meshes made one a frame from now).</summary>
        void NewRun()
        {
            int seed = Autoplay ? 20261116 : Environment.TickCount;
            _run = new GardenRun(_menu.Difficulty, Players, seed) { Spells = _progress.SpellMask };
            // PerfLab: start at a later wave (box measurements of a busy garden)
            if (int.TryParse(PerfSwitches.Get("wave"), out int wave) && wave > 1) _run.SkipTo(wave);
            _views.NewRun();
            _spells.Clear();
            Array.Clear(_casts, 0, _casts.Length);
            Array.Clear(_castsBy, 0, _castsBy.Length);
            _runSeconds = 0f;
            foreach (var h in _humans) h.Reset();
            for (int i = 0; i < 2; i++)
            {
                _fly[i] = _wanderTo[i] = GardenRun.FairyAt(i, Players);
                _flyVel[i] = Vector3.zero;
                _wanderWait[i] = 1f + i;
                _auto[i] = new Vector2(HudWidth * (i == 0 ? 0.42f : 0.58f), 560f);
                _trail[i].Clear();
                _readPath[i] = _readGuide[i] = _autoPath[i] = null;
                _readAge[i] = _chargedAge[i] = 9f;
                _autoRest[i] = 0f;
                _morphFrom[i] = _morphTo[i] = null;
                _lastDrawn[i] = 0f;
            }
        }

        void StartRun()
        {
            _two = _menu.Two;
            NewRun();
            Dress();
            if (!_two)
            {
                if (_input != null && _input.Gestures != null) _input.Gestures.Recenter();
            }
            else _slots.Recenter();
            _dustBefore = _progress.Stardust;
            GameAudio.StopMusic(0.5f);
            PlaySong("fairy_fly");
            SetPhase(Phase.Intro);
        }

        /// <summary>Tests: one fairy or two (not saved), a fresh run.</summary>
        public void Choose(bool two)
        {
            _menu.Two = two;
            _two = two;
            NewRun();
            Dress();
        }

        /// <summary>Tests: the game stops defending (the game playing itself, or the scripted player, lets the creatures
        /// through).</summary>
        public bool Idle { get; set; }

        /// <summary>What hands up (or OK) does on the screen showing (tests).</summary>
        public void Continue()
        {
            switch (Current)
            {
                case Phase.Waiting:
                    StartRun();
                    break;
                case Phase.Intro:
                    SetPhase(Phase.Playing);
                    break;
                case Phase.Transform:
                    DoTransform();
                    break;
                case Phase.Over:
                    BackToMenu();
                    break;
            }
        }

        void BackToMenu()
        {
            NewRun();
            _world.SetDecorations(_progress.Decorations);
            if (_music.isPlaying) _music.Stop();
            _song = null;
            GameAudio.PlayMusic("fairy");
            SetPhase(Phase.Waiting);
        }

        // ---------------------------------------------------------------- every frame

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _phaseTime += dt;
            // who is player 1 and 2: the one on the left is 1 while waiting, locked in a run
            var tracker = MotionSystem.Instance != null ? MotionSystem.Instance.Tracker : null;
            _slots.Locked = Current != Phase.Waiting && _two;
            if (tracker != null) _slots.Update(tracker.Bodies);
            AskForPeople();
            ReadBodies();
            double now = Time.unscaledTimeAsDouble;
            for (int k = 0; k < 2; k++) _humans[k].Wand.Update(now, Time.unscaledDeltaTime);
            _views.Warm(_run);
            switch (Current)
            {
                case Phase.Waiting:
                    if (HandleMenu()) break;
                    if (Autoplay && _phaseTime > 0.5f) StartRun();
                    else if (ReadyToGo(out bool alone))
                    {
                        if (alone) _menu.Two = false;
                        StartRun();
                    }
                    break;
                case Phase.Intro:
                    if (_phaseTime > 2.8f || RemoteInput.Confirm || Autoplay && _phaseTime > 0.8f) SetPhase(Phase.Playing);
                    break;
                case Phase.Playing:
                    Play(dt);
                    break;
                case Phase.Transform:
                    WaitForTransform();
                    break;
                case Phase.Over:
                    if (_phaseTime > 2.5f && (StartPressed() || Autoplay || _phaseTime > 30f)) BackToMenu();
                    break;
            }
            DrawWorld(dt);
        }

        bool UsingKeys => !_two && (_input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService);

        void Play(float dt)
        {
            bool wantPause = !Autoplay && !UsingKeys && (_two ? TwoPlayersPaused() : PlayerGate.ShouldPause(_input));
            if (wantPause != _paused)
            {
                _paused = wantPause;
                if (_paused) _music.Pause();
                else _music.UnPause();
            }
            if (_paused) return;
            ReadControls(dt);
            UpdateSpells(dt);
            for (int k = 0; k < 2; k++)
            {
                _readAge[k] += dt;
                _chargedAge[k] += dt;
            }
            _run.Step(dt);
            _runSeconds += dt;
            foreach (var e in _run.Events) OnEvent(e);
            _run.Events.Clear();
            // the orb between waves: a wand that touches it catches it
            if (_run.OrbActive && ScreenOf(_run.OrbAt, out var orb))
                for (int k = 0; k < Players; k++)
                    if (CursorOf(k, out var px) && Vector2.Distance(px, orb) <= 95f && _run.TryTakeOrb(_run.OrbAt, k)) break;
            foreach (var e in _run.Events) OnEvent(e);
            _run.Events.Clear();
            // the tree's hurt shows over the garden
            _world.SetGloom((1f - (float)_run.TreeHealth / _run.TreeMax) * 0.85f, dt);
            _world.SetTreeLife(_run.TreeMax > 0 ? (float)_run.TreeHealth / _run.TreeMax : 1f, dt);
            if (_run.Current == GardenRun.Stage.Transform) SetPhase(Phase.Transform);
            else if (_run.Over) EndRun();
            // the music: the boss's own while it's there
            bool boss = false;
            foreach (var c in _run.Creatures)
                if (c.IsBoss && c.Alive) boss = true;
            string song = boss ? "fairy_boss" : "fairy_fly";
            if (_song != song) PlaySong(song);
        }

        void WaitForTransform()
        {
            bool arms = false;
            for (int k = 0; k < Players; k++) arms |= _humans[k].Moves.ArmsUp;
            bool keys = UsingKeys && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            if (arms || keys || RemoteInput.Confirm || Autoplay && _phaseTime > 1f || _phaseTime > 9f) DoTransform();
        }

        void DoTransform()
        {
            if (_run.Current != GardenRun.Stage.Transform) return;
            _run.Transform();
            foreach (var e in _run.Events) OnEvent(e);
            _run.Events.Clear();
            foreach (var h in _humans) h.ArmsHeld = true;   // the arms raised for it aren't the big magic too
            SetPhase(Phase.Playing);
        }

        void EndRun()
        {
            var keep = Autoplay || !Saving ? _progress.Clone() : _progress;
            _opened = keep.Add(_run, _runSeconds);
            if (keep == _progress) Save();
            _shownDust = keep.Stardust;
            SetPhase(Phase.Over);
            if (_music.isPlaying) _music.Stop();
            GameAudio.Jingle(_opened.Best ? "jingle_win" : "jingle_gameover");
            foreach (var f in _fairies)
                if (f != null && f.gameObject.activeSelf) f.Face(Chibi.Face.Sad, 3f);
            Debug.Log($"[Fairy] The tree fell at wave {_run.Wave}: {_run.WavesCleared} waves saved, {_run.Purified} purified, {_run.Stardust} stardust, " +
                      $"level {_run.Level}, {_runSeconds:0} s; spells drawn {_castsBy[0]} + {_castsBy[1]}: lightning {_casts[(int)Spell.Lightning]}, " +
                      $"rainbow {_casts[(int)Spell.Rainbow]}, shield {_casts[(int)Spell.Shield]}, seeds {_casts[(int)Spell.Seeds]}, " +
                      $"big magic {_casts[(int)Spell.Burst]}, freeze {_casts[(int)Spell.Freeze]}, rain {_casts[(int)Spell.Heal]}");
        }

        int _shownDust;

        // ---------------------------------------------------------------- the wand and the spells

        /// <summary>The HUD's width (it is 1080 high).</summary>
        static float HudWidth => GameHud.Height * Screen.width / Mathf.Max(1, Screen.height);

        void ReadControls(float dt)
        {
            if (Autoplay)
            {
                AutoPlay(dt);
                return;
            }
            if (!UsingKeys) return;
            // without a camera the mouse is the wand
            var mouse = Mouse.current;
            if (mouse == null) return;
            var m = GameHud.FromScreen(mouse.position.ReadValue());
            if ((m - _mouse).sqrMagnitude > 0.5f || mouse.leftButton.isPressed) _mouseUntil = Time.unscaledTime + 4f;
            _mouse = m;
        }

        /// <summary>Where player <paramref name="k"/>'s wand points (HUD pixels), and whether it's up.</summary>
        bool CursorOf(int k, out Vector2 px)
        {
            px = default;
            if (k >= Players || Idle) return false;
            if (Autoplay)
            {
                px = _auto[k];
                return true;
            }
            if (UsingKeys)
            {
                px = _mouse;
                return k == 0 && Time.unscaledTime < _mouseUntil;
            }
            var h = _humans[k];
            if (h.Body == null || !h.Wand.Active) return false;
            px = new Vector2(h.Wand.Position.x * HudWidth, (1f - h.Wand.Position.y) * GameHud.Height);
            return true;
        }

        /// <summary>A point of the garden on the HUD (false: behind the camera).</summary>
        bool ScreenOf(Vector3 world, out Vector2 px)
        {
            px = default;
            if (cam == null) return false;
            var p = cam.WorldToScreenPoint(world);
            if (p.z <= 0.1f) return false;
            px = GameHud.FromScreen(p);
            return true;
        }

        /// <summary>The point of the plane z = <paramref name="z"/> that HUD point <paramref name="px"/> shows.</summary>
        bool OnPlane(Vector2 px, float z, out Vector3 p)
        {
            p = default;
            if (cam == null) return false;
            float scale = Screen.height / GameHud.Height;
            var ray = cam.ScreenPointToRay(new Vector3(px.x * scale, Screen.height - px.y * scale, 0f));
            if (Mathf.Abs(ray.direction.z) < 1e-4f) return false;
            float t = (z - ray.origin.z) / ray.direction.z;
            if (t <= 0f) return false;
            p = ray.origin + ray.direction * t;
            return true;
        }

        static Color WandColour(int k, Element element) => k == 0 ? FairyLooks.ElementColour(element) : new Color(0.55f, 0.9f, 1f);

        /// <summary>The spells this run may be read as (the freeze and the rain once bought).</summary>
        int AllowedSpells()
        {
            int mask = 0;
            foreach (Spell s in Enum.GetValues(typeof(Spell)))
                if (_run.Has(s)) mask |= SpellReader.Bit(s);
            return mask;
        }

        /// <summary>Each player's wand drawing spells (<see cref="SpellCaster"/>): a stroke read as a spell is cast (the
        /// lightning and the rainbow on the side it was drawn on), unless the wand still charges or the spell isn't ready;
        /// a stroke read as nothing fizzles. The wand touches nothing else (the user: "only with spells").</summary>
        void UpdateSpells(float dt)
        {
            _spellClock += dt;
            float w = HudWidth;
            int allowed = AllowedSpells();
            for (int k = 0; k < Players; k++)
            {
                var c = _humans[k].Caster;
                c.Allowed = allowed;
                bool up = CursorOf(k, out var px);
                bool was = c.Drawing;
                var outcome = c.Update(_spellClock, up, px);
                if (c.Drawing)
                {
                    _lastDrawn[k] = _runSeconds;
                    if (!was) GameAudio.Sfx("fairy_charge", 0.22f, _run.Charged(k) ? 1.1f : 0.8f);
                }
                if (outcome == SpellCaster.Outcome.Read) OnRead(k, c.LastRead, c.Last, w);
                else if (outcome == SpellCaster.Outcome.Fizzled)
                {
                    Feedback(k, ReadKind.Fizzle, c.Last, default);
                    Debug.Log($"[Fairy] Stroke {k + 1} read as nothing: {c.LastRead}");
                    StrokeRead?.Invoke(k, c.LastRead);
                }
            }
        }

        void OnRead(int k, SpellRead read, Vector2[] points, float width)
        {
            Debug.Log($"[Fairy] Stroke {k + 1} read: {read}");
            StrokeRead?.Invoke(k, read);
            var spell = read.Spell;
            int side = _run.SideFor(spell, read.Centre.x < width * 0.5f ? -1 : 1);
            var offer = new SpellOffer(spell, side);
            if (!_run.Charged(k)) Feedback(k, ReadKind.Wait, points, offer);
            else if (!_run.Ready(spell)) Feedback(k, ReadKind.NotReady, points, offer);
            else if (!CastSpell(k, offer, points)) Feedback(k, ReadKind.Fizzle, points, offer);
        }

        /// <summary>The HUD shows what became of player <paramref name="k"/>'s stroke.</summary>
        void Feedback(int k, ReadKind kind, Vector2[] points, SpellOffer offer)
        {
            _readKind[k] = kind;
            _readAge[k] = 0f;
            _readPath[k] = points;
            _readOffer[k] = offer;
            _readGuide[k] = kind == ReadKind.Cast && points != null ? FitGuide(offer, points) : null;
            PrepareMorph(k);
            _lastDrawn[k] = _runSeconds;
            switch (kind)
            {
                case ReadKind.Fizzle:
                    GameAudio.Sfx("fairy_fizzle", 0.6f);
                    break;
                case ReadKind.Wait:
                case ReadKind.NotReady:
                    GameAudio.Sfx("fairy_fizzle", 0.45f, 1.3f);
                    break;
            }
        }

        /// <summary>The spell's clean shape (<see cref="SpellShapes"/>) laid over the stroke's box: what the trail turns
        /// into as it's cast.</summary>
        Vector2[] FitGuide(SpellOffer offer, Vector2[] stroke)
        {
            var guide = SpellShapes.PathOf(offer, HudWidth);
            Vector2 gMin = guide[0], gMax = guide[0], sMin = stroke[0], sMax = stroke[0];
            foreach (var q in guide)
            {
                gMin = Vector2.Min(gMin, q);
                gMax = Vector2.Max(gMax, q);
            }
            foreach (var q in stroke)
            {
                sMin = Vector2.Min(sMin, q);
                sMax = Vector2.Max(sMax, q);
            }
            var gSize = Vector2.Max(gMax - gMin, Vector2.one);
            var sSize = sMax - sMin;
            // the same proportions as the guide, as big as the stroke, on its middle
            float scale = Mathf.Max(sSize.x / gSize.x, sSize.y / gSize.y);
            scale = Mathf.Clamp(scale, 0.4f, 2.5f);
            var centre = (sMin + sMax) * 0.5f;
            var gCentre = (gMin + gMax) * 0.5f;
            var output = new Vector2[guide.Length];
            for (int i = 0; i < guide.Length; i++) output[i] = centre + (guide[i] - gCentre) * scale;
            return output;
        }

        /// <summary>The fairy's move for <paramref name="spell"/>.</summary>
        static CastMove MoveOf(Spell spell) => spell switch
        {
            Spell.Rainbow => CastMove.Out,
            Spell.Shield => CastMove.Wide,
            Spell.Seeds => CastMove.Down,
            Spell.Burst => CastMove.Twirl,
            _ => CastMove.Up,
        };

        /// <summary>Player <paramref name="player"/>'s fairy casts <paramref name="offer"/> (<paramref name="stroke"/>: the
        /// shape drawn, which the HUD turns into the spell's clean shape): she throws it, its light goes where its magic
        /// does. False when it can't be cast now.</summary>
        bool CastSpell(int player, SpellOffer offer, Vector2[] stroke = null)
        {
            var spell = offer.Spell;
            int from = _run.Events.Count;
            int hit = _run.Cast(spell, player, offer.Side);
            if (hit < 0) return false;
            _casts[(int)spell]++;
            _castsBy[player]++;
            SpellCast?.Invoke(player, spell);
            if (stroke != null) Feedback(player, ReadKind.Cast, stroke, offer);
            var view = player < _fairies.Count ? _fairies[player] : null;
            var hands = view != null ? view.Palms : _fly[Mathf.Clamp(player, 0, 1)] + Vector3.up * 0.4f;
            var colour = FairyLooks.SpellColour(spell);
            // the creatures it touched (their events are the cast's): a bolt down onto each for the lightning, a dart of
            // light from her hands to each for the rainbow
            for (int i = from, n = 0; i < _run.Events.Count; i++)
            {
                var e = _run.Events[i];
                if (e.Kind is not (GardenEventKind.Hit or GardenEventKind.Purified) || e.Creature < 0) continue;
                var c = _run.Find(e.Creature);
                float size = c != null ? c.Size * GloomViews.DrawScale : 1f;
                if (spell == Spell.Lightning) _spells.Strike(e.At + Vector3.up * size * 0.45f, colour, 0.03f * n);
                else if (spell == Spell.Rainbow) _spells.Dart(hands, e.Creature, Color.HSVToRGB(Mathf.Repeat(0.9f + n * 0.17f, 1f), 0.55f, 1f), 0.04f * n);
                n++;
            }
            if (view != null)
            {
                view.Cast(MoveOf(spell), offer.Side);
                view.Flash(colour, 0.45f);
            }
            _fx.Catch(hands, colour, 14);
            switch (spell)
            {
                case Spell.Lightning:
                    _spells.Beam(hands, hands + new Vector3(offer.Side * 0.6f, 6f, 1f), colour, 0.3f, 0.45f);
                    GameAudio.Sfx("fairy_cast_bolt", 0.85f);
                    _shake = Mathf.Max(_shake, 0.12f);
                    break;
                case Spell.Rainbow:
                    _fx.Rainbow(hands + Vector3.up * 0.4f, new Vector3(offer.Side, 0f, 0f), Vector3.up);
                    GameAudio.Sfx("fairy_cast_swipe", 0.75f);
                    GameAudio.Sfx("fairy_sparkle", 0.5f, 1.2f);
                    break;
                case Spell.Shield:
                    GameAudio.Sfx("fairy_cast_circle", 0.8f);
                    break;
                case Spell.Seeds:
                    _seedsAge = 0f;
                    GameAudio.Sfx("fairy_sparkle", 0.9f);
                    GameAudio.Sfx("fairy_pop", 0.6f, 0.8f);
                    break;
                case Spell.Burst:
                    _burstAge = 0f;
                    _burstFrom = _fly[Mathf.Clamp(player, 0, 1)];
                    _fx.Rainbow(_burstFrom + Vector3.up * 1.2f, Vector3.right, Vector3.up);
                    _fx.Confetti(_burstFrom + Vector3.up * 2f, 50);
                    GameAudio.Sfx("fairy_magic", 1f);
                    Call(FairyStrings.BigMagic, null, 1.6f);
                    break;
                case Spell.Freeze:
                    GameAudio.Sfx("fairy_cast_vee", 0.8f);
                    Call(FairyStrings.Frozen, null, 1.2f);
                    break;
                case Spell.Heal:
                    _healAge = 0f;
                    GameAudio.Sfx("fairy_cast_heart", 0.8f);
                    GameAudio.Sfx("fairy_heart", 0.6f);
                    Call(FairyStrings.Healed, null, 1.4f);
                    break;
            }
            // after a side's spell she flies a little that way, once she has thrown it (no mechanic: it looks alive)
            if (offer.Side != 0)
            {
                var home = GardenRun.FairyAt(player, Players);
                Span(player, out float lo, out float hi);
                _wanderTo[player] = new Vector3(Mathf.Clamp(_fly[player].x + offer.Side * Random.Range(0.9f, 1.6f), lo, hi), home.y + Random.Range(-0.1f, 0.3f), home.z);
                _wanderWait[player] = Random.Range(1f, 2f);
            }
            return true;
        }

        /// <summary>The game playing itself (tests, box measurements): once each fairy's wand is (nearly) charged, her own
        /// wand draws what would help (<see cref="GardenRun.Suggest"/>): to the start of its shape on the screen, a moment's
        /// rest, along it at <see cref="AutoTrace"/> (a good player's pace), a rest at its end: read by the same
        /// <see cref="SpellCaster"/> as a player's strokes. It catches the orb.</summary>
        void AutoPlay(float dt)
        {
            for (int k = 0; k < Players; k++)
            {
                var goal = _auto[k];
                float speed = AutoSpeed;
                var path = _autoPath[k];
                if (path == null && _run.Current == GardenRun.Stage.Wave && _run.RechargeOf(k) < 0.6f && !_humans[k].Caster.Drawing)
                {
                    var s = _run.Suggest(k);
                    if (s.HasValue)
                    {
                        _autoPath[k] = path = SpellShapes.PathOf(s.Value, HudWidth);
                        _autoAlong[k] = -1f;
                        _autoRest[k] = 0f;
                    }
                }
                if (path != null)
                {
                    if (_autoAlong[k] < 0f)
                    {
                        // to its start, then a moment's rest
                        goal = path[0];
                        if (Vector2.Distance(_auto[k], goal) < 2f)
                        {
                            _autoRest[k] += dt;
                            if (_autoRest[k] > 0.5f)
                            {
                                _autoAlong[k] = 0f;
                                _autoRest[k] = 0f;
                            }
                        }
                    }
                    else if (_autoAlong[k] < SpellShapes.Length(path))
                    {
                        _autoAlong[k] += AutoTrace * dt;
                        goal = AlongPath(path, _autoAlong[k]);
                        speed = 5000f;
                    }
                    else
                    {
                        // a rest at its end: the stroke ends and is read
                        goal = path[path.Length - 1];
                        _autoRest[k] += dt;
                        if (_autoRest[k] > 0.6f) _autoPath[k] = null;
                    }
                }
                else if (k == 0 && _run.OrbActive && _run.StageTime > 1.4f && ScreenOf(_run.OrbAt, out var orb)) goal = orb;
                _auto[k] = Vector2.MoveTowards(_auto[k], goal, speed * dt);
            }
        }

        static Vector2 AlongPath(Vector2[] path, float along)
        {
            for (int i = 1; i < path.Length; i++)
            {
                float seg = Vector2.Distance(path[i - 1], path[i]);
                if (along <= seg) return Vector2.Lerp(path[i - 1], path[i], seg > 1e-4f ? along / seg : 0f);
                along -= seg;
            }
            return path[path.Length - 1];
        }

        /// <summary>The game's own wand: HUD pixels a second to a shape, along it.</summary>
        const float AutoSpeed = 1100f, AutoTrace = 650f;

        void OnEvent(GardenEvent e)
        {
            Happened?.Invoke(e);
            var c = e.Creature >= 0 ? _run.Find(e.Creature) : null;
            switch (e.Kind)
            {
                case GardenEventKind.WaveStart:
                    Call(FairyStrings.Wave(e.Count), _run.Plan.Boss ? FairyStrings.BossComes : FairyStrings.DefendTree, 2.2f);
                    _waveCall = true;   // the HUD's own wave label waits while this says it big
                    GameAudio.Sfx("fairy_wave", 0.7f);
                    break;
                case GardenEventKind.BossCame:
                    GameAudio.Sfx("fairy_chief", 0.9f);
                    break;
                case GardenEventKind.Charged:
                    if (e.Player >= 0 && e.Player < 2) _chargedAge[e.Player] = 0f;
                    GameAudio.Sfx("fairy_ready", 0.35f, 1.4f);
                    break;
                case GardenEventKind.Hit:
                    if (c != null) _fx.Pop(c.Position + Vector3.up * c.Size, FairyLooks.ElementSecond(Element), 6);
                    if (Time.time - _purifySound > 0.12f)
                    {
                        _purifySound = Time.time;
                        GameAudio.Sfx("fairy_pop", 0.5f, 1f + Random.Range(-0.1f, 0.15f));
                    }
                    break;
                case GardenEventKind.Purified:
                {
                    var at = c != null ? c.Position + Vector3.up * c.Size * 0.6f : e.At;
                    float h = c?.Species?.Hue ?? 0.85f, a = c?.Species?.Accent ?? 0.1f;
                    _fx.Purify(at, Color.HSVToRGB(Mathf.Repeat(h, 1f), 0.5f, 1f), Color.HSVToRGB(Mathf.Repeat(a, 1f), 0.6f, 1f), c?.Size ?? 1f);
                    // a spell purifies a whole run at once: one chime for them (a boss's own)
                    if (e.What == CreatureKind.Boss) GameAudio.Sfx("fairy_chief_saved", 0.7f);
                    else if (Time.time - _purifySound > 0.12f)
                    {
                        _purifySound = Time.time;
                        GameAudio.Sfx("fairy_purify", 0.6f, 1f + Random.Range(-0.08f, 0.12f));
                    }
                    break;
                }
                case GardenEventKind.Split:
                    GameAudio.Sfx("fairy_pop", 0.6f, 0.8f);
                    break;
                case GardenEventKind.TreeHit:
                    _shake = 0.35f;
                    _treeFlash = 1f;
                    _fx.Hit(GardenRun.Tree + Vector3.up * 1.4f + Vector3.right * Mathf.Sign(e.At.x) * 1.2f);
                    GameAudio.Sfx("fairy_hit", 0.8f);
                    foreach (var f in _fairies)
                        if (f != null && f.gameObject.activeSelf) f.Face(Chibi.Face.Sad, 1f);
                    break;
                case GardenEventKind.Blocked:
                    if (c != null) _fx.Pop(c.Position + Vector3.up * c.Size, new Color(0.6f, 0.85f, 1f), 5);
                    GameAudio.Sfx("fairy_ring", 0.4f);
                    break;
                case GardenEventKind.WaveCleared:
                {
                    Call(FairyStrings.WaveSaved, FairyStrings.Dust(e.Count), 2.4f);
                    GameAudio.Jingle("jingle_win");
                    _world.Grow(Mathf.Min(GardenProgress.MaxDecorations, _progress.Decorations + _run.WavesCleared));
                    foreach (var f in _fairies)
                        if (f != null && f.gameObject.activeSelf) f.Cheer(1.6f);
                    break;
                }
                case GardenEventKind.OrbShown:
                    Call(FairyStrings.CatchOrb, null, 2f);
                    GameAudio.Sfx("fairy_sparkle", 0.5f);
                    break;
                case GardenEventKind.OrbTaken:
                    Call(FairyStrings.Orb(e.Orb), null, 1.8f);
                    _fx.Catch(e.At, OrbColour(e.Orb), 16);
                    GameAudio.Sfx(e.Orb == OrbKind.Heart ? "fairy_heart" : "fairy_ring", 0.8f);
                    break;
                case GardenEventKind.TransformReady:
                    GameAudio.Sfx("fairy_ready", 0.9f);
                    break;
                case GardenEventKind.Transformed:
                    Call(FairyStrings.Form(e.Count), FairyStrings.FormPower(e.Count), 3f);
                    GameAudio.Sfx("fairy_magic", 1f);
                    GameAudio.Jingle("jingle_win");
                    for (int i = 0; i < Players && i < _fairies.Count; i++)
                    {
                        var f = _fairies[i];
                        if (f == null) continue;
                        f.Spin();
                        f.Flash(FairyLooks.ElementColour(Element), 0.9f);
                        _fx.Confetti(FairyAt(i) + Vector3.up * 1.5f, 40);
                        _fx.Rainbow(FairyAt(i) + Vector3.up * 1f, Vector3.right, Vector3.up);
                    }
                    break;
            }
        }

        static Color OrbColour(OrbKind k) => k switch
        {
            OrbKind.Heart => new Color(1f, 0.4f, 0.65f),
            OrbKind.Star => new Color(1f, 0.85f, 0.3f),
            _ => new Color(0.5f, 1f, 0.45f),
        };

        // ---------------------------------------------------------------- drawing the world

        /// <summary>Where fairy <paramref name="i"/> hovers (her hips), bobbing.</summary>
        Vector3 FairyAt(int i) => GardenRun.FairyAt(i, Players) + Vector3.up * (0.08f * Mathf.Sin(Time.time * 1.6f + i));

        /// <summary>How far fairy <paramref name="i"/> flies across (metres): the front of the tree, her own half with two
        /// (kept off the paths' far ends, where the lightning's bolts are drawn).</summary>
        void Span(int i, out float lo, out float hi)
        {
            if (Players > 1)
            {
                lo = i == 0 ? -2.9f : 0.6f;
                hi = i == 0 ? -0.6f : 2.9f;
            }
            else
            {
                lo = -2.3f;
                hi = 2.3f;
            }
        }

        /// <summary>The fairies fly about in front of the tree by themselves (2026-11-16, the user: "move left or right, no
        /// mechanic, just for more immersion"): to a spot a metre or more from where she is, a pause there, the next; she
        /// holds still while her player follows a shape and while she casts (<paramref name="free"/> false), and stays put
        /// on the waiting screen (its camera is on her).</summary>
        void Wander(int i, float dt, bool free)
        {
            var home = GardenRun.FairyAt(i, Players);
            Vector3 target;
            if (Current == Phase.Waiting) target = home;
            else if (!free) target = _fly[i];
            else
            {
                _wanderWait[i] -= dt;
                if (_wanderWait[i] <= 0f && Vector3.Distance(_fly[i], _wanderTo[i]) < 0.2f)
                {
                    Span(i, out float lo, out float hi);
                    float x = _fly[i].x;
                    for (int tries = 0; tries < 8 && Mathf.Abs(x - _fly[i].x) < 1.1f; tries++) x = Random.Range(lo, hi);
                    _wanderTo[i] = new Vector3(x, home.y + Random.Range(-0.2f, 0.4f), home.z);
                    _wanderWait[i] = Random.Range(1.4f, 3.4f);
                }
                target = _wanderTo[i];
            }
            _fly[i] = Vector3.SmoothDamp(_fly[i], target, ref _flyVel[i], 0.6f, 3.2f, dt);
        }

        /// <summary>0: fairy <paramref name="k"/> isn't gathering a spell; else how much of a shape her player has drawn
        /// (at least a little; a whole bolt's worth is all of it) while the wand is charged.</summary>
        float Charge(int k)
        {
            var c = _humans[k].Caster;
            if (Current != Phase.Playing || _paused || !c.Drawing || !_run.Charged(k)) return 0f;
            float length = 0f;
            var s = c.Stroke;
            for (int i = 1; i < s.Count; i++) length += Vector2.Distance(s[i - 1], s[i]);
            return Mathf.Clamp(length / 900f, 0.12f, 1f);
        }

        /// <summary>Where fairy <paramref name="i"/> looks (degrees: yaw toward her right, pitch down): at her player's wand,
        /// else at the camera.</summary>
        Vector2 LookAt(int i)
        {
            if (Current != Phase.Playing || !CursorOf(i, out var px) || !OnPlane(px, _fly[i].z - 2.5f, out var p)) return Vector2.zero;
            var head = _fly[i] + Vector3.up * 0.75f;
            var d = p - head;
            // she faces the camera (-z): her right is the world's -x
            float yaw = Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            return new Vector2(yaw, pitch);
        }

        void DrawWorld(float dt)
        {
            if (sun != null)
            {
                sun.color = _world.SunColour;
                sun.intensity = _world.SunIntensity;
                sun.transform.rotation = Quaternion.LookRotation(GardenWorld.SunTravel, Vector3.up);
            }
            // the fairies fly about by themselves, hold still to gather a spell and to throw it
            for (int i = 0; i < Players && i < _fairies.Count; i++)
            {
                var v = _fairies[i];
                if (v == null) continue;
                float charge = Charge(i);
                Wander(i, dt, !_paused && charge <= 0f && !v.Casting && Current is Phase.Playing or Phase.Intro);
            }
            // two fairies don't fly into each other
            if (Players > 1)
            {
                float gap = _fly[1].x - _fly[0].x, need = 1.3f;
                if (Mathf.Abs(gap) < need && Mathf.Abs(_fly[1].y - _fly[0].y) < 1.6f)
                {
                    float push = (need - Mathf.Abs(gap)) * 0.5f * (gap >= 0f ? 1f : -1f);
                    _fly[0].x -= push;
                    _fly[1].x += push;
                }
            }
            for (int i = 0; i < Players && i < _fairies.Count; i++)
            {
                var v = _fairies[i];
                if (v == null) continue;
                float charge = Charge(i);
                v.Hover(_fly[i] + Vector3.up * (0.06f * Mathf.Sin(Time.time * 1.6f + i)), _flyVel[i], charge, LookAt(i), dt);
                var w = FairyLooks.AllWings[i == 0 ? Mathf.Clamp(_progress.Wings, 0, FairyLooks.AllWings.Length - 1) : 1];
                _fx.Glitter(_fly[i] + Vector3.up * 0.55f + Vector3.forward * 0.3f, Vector3.forward, w.Edge, dt, 3f + _run.Level);
                // sparkles drifting from the light she gathers
                if (charge > 0f) _fx.Glitter(v.Palms, Vector3.down, WandColour(i, Element), dt, 8f + 24f * charge);
            }
            // soft shadows under the fairies (fainter and wider the higher they fly) and the creatures
            for (int i = 0; i < Players && i < _fairies.Count; i++)
            {
                float up = Mathf.Max(0f, _fly[i].y - GardenWorld.Height(_fly[i].x, _fly[i].z));
                _world.Shadow(_fly[i], 0.75f + up * 0.12f, Mathf.Lerp(0.42f, 0.18f, Mathf.InverseLerp(0.5f, 4f, up)));
            }
            foreach (var c in _run.Creatures)
            {
                if (c.State == CreatureState.Gone) continue;
                float size = c.Size * GloomViews.DrawScale;
                float fade = c.State == CreatureState.Purified ? Mathf.Clamp01(1.2f - c.StateTime) : 1f;
                if (c.Flying) _world.Shadow(c.Position, size * 0.45f, 0.22f * fade);
                else _world.Shadow(c.Position, size * 0.55f, 0.5f * fade);
            }
            _world.Draw(cam, dt);
            if (PerfSwitches.Get("pollen") != "0") _fx.Pollen(dt, 1f);
            foreach (var p in _world.Grown) _fx.Catch(p, FairyFx.Gold, 12);
            _world.Grown.Clear();
            _views.Draw(_run, cam, dt);
            UpdateCamera(dt);
            DrawGlows(dt);
        }

        void DrawGlows(float dt)
        {
            _glows.Begin(cam);
            var tint = FairyLooks.ElementColour(Element);
            // soft shafts of the low sun slanting down through the air (the box has no bloom: this is its glow)
            if (PerfSwitches.Get("rays") != "0")
            {
                float g = 1f - Mathf.Clamp01(_run.TreeMax > 0 ? 1f - (float)_run.TreeHealth / _run.TreeMax : 0f) * 0.6f;
                var along = new Vector3(GardenWorld.SunTravel.x, GardenWorld.SunTravel.y, 0f).normalized;
                for (int k = 0; k < 5; k++)
                {
                    float x = -17f + k * 6f + Mathf.Sin(k * 2.1f) * 1.4f, z = 6.5f + (k % 2) * 2.5f;
                    var top = new Vector3(x, 12f, z);
                    float a = (0.09f + 0.035f * Mathf.Sin(Time.time * (0.35f + k * 0.07f) + k * 1.9f)) * g;
                    _glows.Streak(top, top + along * 16f, 2.4f + (k % 3) * 1.1f, new Color(1f, 0.86f, 0.6f), a);
                }
            }
            // the light each fairy gathers between her hands while her player draws a shape
            for (int k = 0; k < Players && k < _fairies.Count; k++)
            {
                var f = _fairies[k];
                float charge = Charge(k);
                if (f == null || charge <= 0f) continue;
                var c = WandColour(k, Element);
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 12f + k);
                _glows.Glow(f.Palms, (0.3f + 0.65f * charge) * pulse, c, 0.9f);
                _glows.Glow(f.Palms, 0.12f + 0.2f * charge, Color.white, 1f);
            }
            // the spells' light: the lightning's bolts, the rainbow's darts, the beams she throws
            _spells.Draw(_glows, dt, id =>
            {
                var c = _run.Find(id);
                return c != null ? c.Position + Vector3.up * c.Size * GloomViews.DrawScale * 0.45f : (Vector3?)null;
            });
            foreach (var (id, colour) in _spells.Arrived)
            {
                var c = _run.Find(id);
                if (c != null) _fx.Pop(c.Position + Vector3.up * c.Size * GloomViews.DrawScale * 0.45f, colour, 10);
            }
            foreach (var (at, colour) in _spells.Landed) _fx.Pop(at, colour, 12);
            // the seeds: a ring of glowing seeds bursting out from the tree's foot
            _seedsAge += dt;
            if (_seedsAge < 0.8f)
            {
                float r = Mathf.Lerp(1.2f, GardenRun.SeedRadius, _seedsAge / 0.8f), a = 1f - _seedsAge / 0.8f;
                for (int k = 0; k < 28; k++)
                {
                    float ang = k / 28f * Mathf.PI * 2f;
                    var p = GardenRun.Tree + new Vector3(Mathf.Cos(ang) * r, 0.5f + Mathf.Sin(_seedsAge * 9f + k) * 0.25f, Mathf.Sin(ang) * r * 0.55f);
                    _glows.Glow(p, 0.8f, k % 2 == 0 ? new Color(0.55f, 1f, 0.4f) : tint, 0.9f * a);
                }
            }
            // the big magic: a rainbow ring sweeping out over the garden
            _burstAge += dt;
            if (_burstAge < 1.1f)
            {
                float r = Mathf.Lerp(0.5f, 16f, _burstAge / 1.1f), a = 1f - _burstAge / 1.1f;
                for (int k = 0; k < 40; k++)
                {
                    float ang = k / 40f * Mathf.PI * 2f;
                    var p = _burstFrom + Vector3.up * 1.2f + new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * 0.55f, 2f);
                    _glows.Glow(p, 1.6f, Color.HSVToRGB(k / 40f, 0.6f, 1f), 0.85f * a);
                }
            }
            // the healing rain over the crown
            _healAge += dt;
            if (_healAge < 1.6f)
                for (int k = 0; k < 14; k++)
                {
                    float x = (Mathf.Repeat(k * 0.37f, 1f) - 0.5f) * 5f;
                    float y = GardenRun.CanopyY + 2.5f - Mathf.Repeat(_healAge * 3f + k * 0.21f, 1f) * 4f;
                    _glows.Glow(new Vector3(x, y, GardenRun.TreeZ - 1.8f), 0.6f, new Color(1f, 0.55f, 0.8f), 0.8f * (1f - _healAge / 1.6f));
                }
            // the tree: its windows and lanterns glowing warm, a red flash when it's hit
            _treeFlash = Mathf.Max(0f, _treeFlash - dt * 2.5f);
            float health = _run.TreeMax > 0 ? (float)_run.TreeHealth / _run.TreeMax : 1f;
            for (int i = 0; i < _world.Lights.Count; i++)
            {
                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 2f + i * 1.7f);
                float size = _world.LightSizes[i];
                _glows.Glow(_world.Lights[i], size * 1.4f * pulse, Color.Lerp(new Color(1f, 0.35f, 0.45f), new Color(1f, 0.78f, 0.92f), health),
                            Mathf.Lerp(0.35f, 0.55f, size) * pulse * Mathf.Lerp(0.35f, 1f, health));
            }
            // the spirit tree's magic: little lights circling its trunk and drifting up into the canopy (fewer as it's hurt;
            // those behind the trunk hidden by it), and a soft radiance behind the canopy
            if (PerfSwitches.Get("spirits") != "0")
            {
                int count = Mathf.RoundToInt(Mathf.Lerp(4f, 14f, health));
                for (int k = 0; k < count; k++)
                {
                    float speed = (0.22f + 0.09f * (k % 4)) * (k % 2 == 0 ? 1f : -1f);
                    float ang = Time.time * speed + k * 2.399f;
                    float rad = 1.8f + 1.4f * Mathf.Repeat(k * 0.618f, 1f);
                    float up = Mathf.Repeat(k * 0.381f + Time.time * 0.035f, 1f);
                    var p = GardenRun.Tree + new Vector3(Mathf.Cos(ang) * rad, 0.8f + 5.4f * up + 0.25f * Mathf.Sin(Time.time * 1.3f + k), Mathf.Sin(ang) * rad * 0.8f);
                    var c = k % 3 == 0 ? new Color(0.55f, 1f, 1f) : k % 3 == 1 ? new Color(1f, 0.6f, 0.95f) : new Color(1f, 0.93f, 0.72f);
                    float twinkle = (0.6f + 0.4f * Mathf.Sin(Time.time * 4f + k * 1.7f)) * Mathf.Min(1f, up * 6f) * Mathf.Min(1f, (1f - up) * 6f);
                    _glows.Glow(p, 0.34f, c, 0.75f * twinkle);
                    _glows.Glow(p, 0.1f, Color.white, 0.9f * twinkle);
                }
            }
            if (PerfSwitches.Get("aura") != "0")
            {
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 0.8f);
                _glows.Glow(GardenRun.Tree + new Vector3(0f, 6.3f, 2.4f), 10.5f * pulse, new Color(0.78f, 0.62f, 1f), 0.15f * Mathf.Lerp(0.3f, 1f, health));
            }
            if (_treeFlash > 0f) _glows.Glow(GardenRun.Tree + Vector3.up * 2.5f + Vector3.back * 1.5f, 6f, new Color(1f, 0.25f, 0.3f), 0.35f * _treeFlash);
            // the orb, looping round the fairies
            if (_run.OrbActive && Current == Phase.Playing)
            {
                var c = OrbColour(_run.Orb);
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 7f);
                _glows.Glow(_run.OrbAt, 1.5f * pulse, c, 0.9f);
                _glows.Glow(_run.OrbAt, 0.5f, Color.white, 1f);
            }
            _glows.Draw();
        }

        // ---------------------------------------------------------------- the camera

        void UpdateCamera(float dt)
        {
            if (cam == null) return;
            _shake = Mathf.Max(0f, _shake - dt);
            Vector3 pos, look;
            float fov;
            if (Current == Phase.Waiting)
            {
                // close in front of the fairy, a little to the side: her face, her wings, the tree behind her
                var mid = FairyAt(0);
                if (Players > 1) mid = (mid + FairyAt(1)) * 0.5f;
                float sway = 0.6f * Mathf.Sin(Time.time * 0.25f);
                pos = mid + new Vector3(1.4f + sway, 0.5f, Players > 1 ? -6.4f : -4.6f);
                look = mid + new Vector3(Players > 1 ? 0.3f : -0.6f, 0.35f, 0f);
                fov = 46f;
            }
            else
            {
                pos = CameraAt + new Vector3(0.3f * Mathf.Sin(Time.time * 0.21f), 0.15f * Mathf.Sin(Time.time * 0.17f), 0f);
                look = CameraLook;
                fov = 50f;
            }
            if (!_camSet)
            {
                _camPos = pos;
                _camLook = look;
                cam.fieldOfView = fov;
                _camSet = true;
            }
            float k = 1f - Mathf.Exp(-(Current == Phase.Waiting || Current == Phase.Intro ? 2.2f : 4f) * dt);
            _camPos = Vector3.Lerp(_camPos, pos, k);
            _camLook = Vector3.Lerp(_camLook, look, k);
            var shake = (Vector3)(Random.insideUnitCircle * (0.18f * _shake));
            cam.transform.SetPositionAndRotation(_camPos + shake, Quaternion.LookRotation(_camLook - _camPos, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, k);
        }

        // ---------------------------------------------------------------- players, bodies, the menu

        void AskForPeople()
        {
            bool one = Current == Phase.Waiting ? !_menu.Two : !_two;
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
            int dress = _progress.Dress, wings = _progress.Wings;
            var element = _progress.Element;
            var diff = _menu.Difficulty;
            if (_menu.Handle(up, down, left, right, camera, _progress, out bool moved))
            {
                GameAudio.Sfx("ui_select", 0.4f);
                _readySince = -1f;
                _slots.ClearReady();
                if (dress != _progress.Dress || wings != _progress.Wings || element != _progress.Element) Save();
                _two = _menu.Two;
                Dress();
                if (diff != _menu.Difficulty || _run.Players != Players) NewRun();
            }
            else if (moved) GameAudio.Sfx("ui_move", 0.5f);
            if (_input != null && camera) _input.PreferBody();
            return true;
        }

        bool ReadyToGo(out bool alone)
        {
            alone = false;
            if (!_menu.Two)
            {
                _readySince = -1f;
                _slots.ClearReady();
                return StartPressed();
            }
            if ((_slots[0].Ready && _slots[1].Ready) || (RemoteInput.Confirm && _slots.BothCanPlay)) return true;
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

        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        bool TwoPlayersPaused()
        {
            for (int k = 0; k < 2; k++)
                if (!_slots[k].SeenWithin(0.6f) || !_slots[k].Calibrated) return true;
            return false;
        }

        Body BodyOf(int slot) => !_two
            ? (slot == 0 && MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null)
            : _slots[slot].Body;

        void ReadBodies()
        {
            bool waiting = Current == Phase.Waiting;
            int players = waiting ? (_slots.Present == 2 && _menu.Two ? 2 : 1) : Players;
            for (int k = 0; k < players; k++) ReadBody(k, waiting && players == 2 ? _slots[k].Body : BodyOf(k));
        }

        /// <summary>A new camera sample of player <paramref name="slot"/>: both arms up (the transformation) and the wand
        /// (each hand's wrist round the smooth shoulders' middle, in smooth torso lengths, as the hand cursors do).</summary>
        void ReadBody(int slot, Body body)
        {
            var h = _humans[slot];
            if (body != h.Body)
            {
                h.Body = body;
                h.Reset();
            }
            if (body == null || !body.IsTracking) return;
            if (body.SampleTime == h.LastSample) return;
            h.LastSample = body.SampleTime;
            var raw = body.Raw;
            var vis = body.Visibility;
            var sample = new FairyBodySample
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
            h.Unit = h.Unit <= 0f ? body.TorsoLength : Mathf.Lerp(h.Unit, body.TorsoLength, 0.1f);
            float unit = Mathf.Max(1e-4f, h.Unit);
            var shoulders = (Vector2)body.ShoulderCenter;
            // the wrist itself, not pushed along the forearm like the other hand cursors: that added a third of the
            // elbow's error, the joint the model gets most wrong while a hand draws in front of the body (Wand)
            var left = ((Vector2)raw[(int)Joint.LeftWrist] - shoulders) / unit;
            var right = ((Vector2)raw[(int)Joint.RightWrist] - shoulders) / unit;
            h.Wand.Sample(body.SampleTime, Time.unscaledTimeAsDouble, left, vis[(int)Joint.LeftWrist], right, vis[(int)Joint.RightWrist]);
        }

        // ---------------------------------------------------------------- music

        void PlaySong(string name)
        {
            _song = name;
            var clip = Resources.Load<AudioClip>($"Music/{name}");
            if (clip == null) return;
            _music.clip = clip;
            _music.volume = GameAudio.MusicVolume * 0.85f;
            _music.Play();
        }

        // ---------------------------------------------------------------- calls

        string _call, _callSub;
        float _callFrom, _callUntil;

        bool _waveCall;

        void Call(string call, string sub, float seconds)
        {
            _waveCall = false;
            _call = call;
            _callSub = sub;
            _callFrom = Time.time;
            _callUntil = Time.time + seconds;
        }
    }
}
