using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Fly
{
    /// <summary>
    /// "Fluturo!": an endless flight over the city as a superhero, punching robots, district after district. The rules
    /// are in <see cref="FlyRun"/> and the progress between runs in <see cref="FlyProgress"/> (both tested without a scene);
    /// this draws them and turns the player into steering and punches:
    /// <list type="bullet">
    /// <item><b>fly</b>: step left or right (the hero follows the body, no lanes), duck to fly low, jump to fly up;</item>
    /// <item><b>punch</b>: any quick move of either fist (<see cref="PunchDetector"/>, forgiving on purpose); it smashes
    /// the most urgent robot, drone or fireball within reach (a gold ring shows what a punch would hit), both fists at once
    /// two of them. A hit freezes the action for a blink and the hero lunges at what it hit.</item>
    /// </list>
    /// A district: fly (drone swarms to punch through, stars), a rush of robots from every side, then fist to fist with
    /// the champion: a tug-of-war bar, the faster puncher wins. Every third district the giant robot. The time of day moves
    /// on with every district. Missions and outfits (unlocked by stars) carry over between runs. Without a camera: arrows
    /// fly, space punches; ← → choose the outfit while waiting.
    /// </summary>
    public sealed class FlyGame : MonoBehaviour
    {
        public enum Phase { Waiting, Countdown, Playing, Results }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] FlyWorld world;
        [SerializeField, Tooltip("Tundu/Toon, white, flat (no world bend): stars, fireballs.")]
        Material toonMaterial;
        [SerializeField, Tooltip("Tundu/Toon with vertex colours and gloss, flat: the hero, robots, hearts, scrap, confetti.")]
        Material actorMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: sparkles and sparks.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx, additive, with the soft dot texture: the fireballs' glow.")] Material glowMaterial;
        [SerializeField, Tooltip("Tundu/Fx drawn on top of everything: the reach rings.")] Material markerMaterial;
        [SerializeField, Tooltip("Tundu/Fx, a dark soft dot: shadows on the street.")] Material shadowMaterial;
        [SerializeField, Tooltip("Tundu/Fx, additive, a streak: speed lines.")] Material streakMaterial;

        [Header("Play")]
        [SerializeField] float countdownSeconds = 3f;
        [SerializeField, Tooltip("A jump lifts the hero to the top of its window for this long (s).")] float jumpLift = 0.7f;

        /// <summary>The camera's place behind the hero (metres above, behind) and where it looks (above the flying height,
        /// ahead). High enough that a robot hovering right in front of the hero shows above its head, not behind it.</summary>
        public const float CameraHeight = 2.1f, CameraBack = 5.3f, LookHeight = -0.2f, LookAhead = 9f;
        /// <summary>The curved world's bend (Tundu/Toon, Tundu/City): metres of drop per metre² ahead of the camera.</summary>
        const float Curve = 0.0008f;
        /// <summary>The turn arrows show from this far before a crossroads (m).</summary>
        const float ArrowsFrom = 46f;
        const string ProgressKey = "fly.progress", OldBestKey = "fly.best";
        static readonly Color Sun = new Color(1f, 0.79f, 0.24f), Coral = new Color(1f, 0.35f, 0.31f), Sky = new Color(0.19f, 0.78f, 0.96f);
        static readonly Color Grape = new Color(0.48f, 0.38f, 1f), Leaf = new Color(0.44f, 0.84f, 0.42f);
        static readonly Color[] RobotPaints = { new Color(0.3f, 0.36f, 0.62f), new Color(0.18f, 0.58f, 0.66f), new Color(0.58f, 0.3f, 0.66f) };
        static readonly Color ThrowerPaint = new Color(0.92f, 0.46f, 0.16f);
        static readonly Color DartPaint = new Color(0.95f, 0.8f, 0.2f);
        static readonly Color DronePaint = new Color(0.4f, 0.74f, 0.88f);
        static readonly Color ChampionPaint = new Color(0.42f, 0.22f, 0.5f);
        static readonly Color BossPaint = new Color(0.3f, 0.28f, 0.45f);
        static readonly Color Spark = new Color(0.35f, 0.85f, 1f);
        static readonly Color[] Confetti = { Sun, Coral, Sky, Grape, Leaf };

        public Phase Current { get; private set; }
        public FlyRun Run { get; private set; }
        /// <summary>Stars, missions, outfits and bests, kept on the box.</summary>
        public FlyProgress Progress => _progress;
        public int Best => _progress.BestScore;
        /// <summary>The last run's results (stars, missions done, new outfits).</summary>
        public RunResult LastResult { get; private set; }
        /// <summary>Nothing spawns by itself (tests, a practice mode later): use <see cref="Run"/>'s Spawn methods.</summary>
        public bool Practice { get; set; }
        /// <summary>Every punch seen (tests, debugging): the hand.</summary>
        public event Action<bool> Punched;
        /// <summary>Every rule event, as it happens (tests, debugging).</summary>
        public event Action<FlyEvent> RunEvent;
        public Hero Hero => _hero;
        /// <summary>The way through the city's streets (turns at crossroads).</summary>
        public FlyRoute Route => _route;
        /// <summary>Every turn taken (tests, debugging).</summary>
        public event Action<TurnChoice> Turned;

        BodyInput _input;
        Hero _hero;
        RobotView _boss, _champion;
        FlyRoute _route;
        FlyProgress _progress;
        readonly PunchDetector _punches = new PunchDetector();
        readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        readonly HashSet<int> _alive = new HashSet<int>();
        readonly List<int> _gone = new List<int>();
        readonly List<Popup> _popups = new List<Popup>();
        readonly List<Card> _cards = new List<Card>();
        readonly Queue<float> _heroPunchTimes = new Queue<float>();
        float _phaseTime, _lift, _shake, _endIn = -1f, _toastUntil, _hintFireballsUntil, _turnLean, _speedLines, _hitStop, _slowMo;
        float _clashZoom, _koAge = -1f;
        double _lastSample = -1;
        bool _paused, _pendingPunch, _pendingRight, _pendingBoth, _nextKeyRight = true, _jabRight;
        /// <summary>The hero's model lunging at what it just hit (flight frame, metres), easing back.</summary>
        Vector3 _lunge;
        string _toast, _toastSub;
        Mesh _ring, _star, _heartMesh;
        Texture2D _heart, _arrow;
        Material _ringMaterial, _sky;
        Light _sun;
        FlyTheme _themeFrom, _themeTo;
        float _themeBlend = 1f;
        MaterialPropertyBlock _tint;
        Vector3 _camVelocity, _koVelocity;
        Transform _ko;
        int _starCombo;
        float _starComboUntil;
        ParticleSystem _sparks, _glow, _bits, _speed;
        Transform _heroShadow, _bossShadow, _championShadow;
        float _comboAt;
        // performance log
        int _frames, _collections;
        float _frameTime, _worstFrame;

        sealed class View
        {
            public Thing Thing;
            public Transform T;
            public RobotView Robot;
            public Transform Ring, Shadow;
            public bool Smashed;
            public float Spin, Size = 1f, Age;
        }

        sealed class Popup { public Vector3 World; public string Text; public Color Color; public float Age; }

        /// <summary>A card at the bottom of the screen: a new enemy, a mission done.</summary>
        sealed class Card { public string Title, Text; public Color Color; public float Age; }

        // ---------------------------------------------------------------- lifecycle

        /// <summary>A step of 0.85 torso lengths reaches the window's edge (<see cref="FlyRun.Steer"/>), jumps lift the hero, ducks lower it.</summary>
        static readonly PlayArea Area = new PlayArea(BodyPart.Knees, side: 0.95f, above: 0.3f);

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            PlayerGate.Use(_input, Area);
            _progress = FlyProgress.Load(PlayerPrefs.GetString(ProgressKey, ""));
            _progress.BestScore = Mathf.Max(_progress.BestScore, PlayerPrefs.GetInt(OldBestKey, 0));   // from before the endless game
            _tint = new MaterialPropertyBlock();
            _ring = FlyArt.Ring(0.42f);
            _star = FlyArt.Star();
            _heart = FlyArt.Heart();
            _arrow = FlyArt.Arrow();
            _heartMesh = HeartMesh();
            _ringMaterial = new Material(markerMaterial);
            _route = world != null ? world.Route : new FlyRoute();
            WearOutfit(_progress.Outfit);
            _heroShadow = Shadow(1.7f);
            BuildEffects();
            // every kind of robot's meshes now, so a rush doesn't start with a stutter on the box
            foreach (var paint in RobotPaints)
            {
                RobotView.Prewarm(paint);
                RobotView.Prewarm(paint, armoured: true);
            }
            RobotView.Prewarm(ThrowerPaint);
            RobotView.Prewarm(DartPaint);
            RobotView.Prewarm(DronePaint);
            RobotView.Prewarm(ChampionPaint, champion: true);
            RobotView.Prewarm(BossPaint, boss: true);
            // the time of day: a copy of the sky to tint (the scene's material stays as built)
            if (RenderSettings.skybox != null)
            {
                _sky = new Material(RenderSettings.skybox);
                RenderSettings.skybox = _sky;
            }
            _sun = FindAnyObjectByType<Light>();
            SetTheme(1, instantly: true);
            Shader.SetGlobalFloat("_TunduCurve", Curve);
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("fly");
        }

        void OnDestroy()
        {
            Shader.SetGlobalFloat("_TunduCurve", 0f);   // other scenes stay flat
            if (_ring != null) Destroy(_ring);
            if (_star != null) Destroy(_star);
            if (_heartMesh != null) Destroy(_heartMesh);
            if (_heart != null) Destroy(_heart);
            if (_arrow != null) Destroy(_arrow);
            if (_ringMaterial != null) Destroy(_ringMaterial);
            if (_sky != null) Destroy(_sky);
        }

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            ReadPunches();
            ReadKeys();

            switch (Current)
            {
                case Phase.Waiting:
                    ChooseOutfit();
                    if (StartPressed()) SetPhase(Phase.Countdown);
                    break;
                case Phase.Countdown:
                    if (_phaseTime < countdownSeconds && Mathf.FloorToInt(_phaseTime) != Mathf.FloorToInt(_phaseTime - dt))
                        GameAudio.Sfx("count", vary: 0f);
                    if (_phaseTime >= countdownSeconds)
                    {
                        GameAudio.Sfx("go", vary: 0f);
                        if (_input != null && _input.Gestures != null) _input.Gestures.Recenter();   // where you stand = the middle
                        SetPhase(Phase.Playing);
                    }
                    break;
                case Phase.Playing:
                    _paused = !UsingKeys && PlayerGate.ShouldPause(_input);
                    if (!_paused) Play(dt);
                    break;
                case Phase.Results:
                    ChooseOutfit();
                    if (_phaseTime > 2f && StartPressed()) SetPhase(Phase.Countdown);
                    break;
            }
            // a punch waits through a hit-stop (Play takes it), not past the round
            if (Current != Phase.Playing || _paused) _pendingPunch = false;

            Fly(dt);
            UpdateTheme(dt);
            UpdateViews(dt);
            UpdatePopups(dt);
            UpdateEffects(dt);
            UpdateCamera(dt);
            LogPerformance();
        }

        /// <summary>The hero flies on through the streets: slowly while waiting, at the game's speed while playing, not at
        /// all while paused. Reaching a crossroads, the hero's place in its window decides the way (<see cref="FlyRoute"/>).</summary>
        void Fly(float dt)
        {
            bool playing = Current == Phase.Playing && Run != null;
            // the rules brake for rushes, clashes and the boss (the hero hovers in the street) and speed up again after
            float speed = !playing ? 6f : _paused ? 0f : Run.CurrentSpeed;
            float steer = playing && Run.Phase == FlyPhase.Flight ? Run.Hero.x / Run.HalfWidth : 0f;
            var turn = _route.Advance(speed * dt, steer);
            if (turn != TurnChoice.Straight)
            {
                if (playing)
                {
                    Run.Turned();
                    GameAudio.Sfx("fly_whoosh", 0.8f, turn == TurnChoice.Right ? 1.08f : 0.95f);
                }
                Turned?.Invoke(turn);
                Debug.Log($"[Fly] Turn {(turn == TurnChoice.Right ? "right" : "left")}");
            }
            _turnLean = Mathf.Lerp(_turnLean, _route.TurnDirection, 1f - Mathf.Exp(-7f * dt));
            _hero.Turn = _turnLean;
        }

        bool UsingKeys => _input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService;

        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            if (phase == Phase.Countdown) NewRound();
            Debug.Log(phase == Phase.Results && Run != null
                ? $"[Fly] Results district={Run.District} score={Run.Score} robots={Run.RobotsSmashed} drones={Run.DronesSmashed} " +
                  $"clashes={Run.ClashesWon} stars={Run.StarsCollected} hitsTaken={Run.HitsTaken} minutes={Run.Seconds / 60f:0.0}"
                : $"[Fly] {phase}");
        }

        void NewRound()
        {
            ClearViews();
            Run = new FlyRun(Random.Range(0, int.MaxValue)) { Scripted = Practice, Record = _progress.BestScore };
            Run.Start();
            _progress.BeginRun();
            LastResult = null;
            _endIn = -1f;
            _lift = _hitStop = _slowMo = _clashZoom = 0f;
            _lunge = Vector3.zero;
            _cards.Clear();
            _heroPunchTimes.Clear();
            _punches.Reset();
            SetTheme(1, instantly: true);
            GameAudio.PlayMusic("fly");
            GameAudio.Sfx("count", vary: 0f);
        }

        void EndRound()
        {
            LastResult = _progress.EndRun(Run);
            PlayerPrefs.SetString(ProgressKey, _progress.Save());
            PlayerPrefs.Save();
            if (_progress.Outfit != _hero.Outfit) WearOutfit(_progress.Outfit);   // a new outfit: put it on at once
            if (LastResult.NewOutfits.Count > 0) GameAudio.Jingle("jingle_win");
            Debug.Log($"[Fly] Stars +{LastResult.StarsFromRun} +{LastResult.StarsFromMissions} (missions {LastResult.Completed.Count}), " +
                      $"total {_progress.Stars}, outfits {_progress.Unlocked}/{FlyProgress.OutfitStars.Length}");
            SetPhase(Phase.Results);
        }

        /// <summary>The hero in outfit <paramref name="outfit"/> (built again: its colours are in its meshes).</summary>
        void WearOutfit(int outfit)
        {
            var at = _hero != null ? _hero.transform.position : World(Vector3.zero);
            if (_hero != null) Destroy(_hero.gameObject);
            _hero = Hero.Create(actorMaterial, outfit);
            _hero.transform.position = at;
        }

        /// <summary>Waiting or on the results: ← → put on another unlocked outfit.</summary>
        void ChooseOutfit()
        {
            int step = RemoteInput.Right ? 1 : RemoteInput.Left ? -1 : 0;
            if (step == 0 || _progress.Unlocked < 2) return;
            _progress.CycleOutfit(step);
            WearOutfit(_progress.Outfit);
            _hero.Cheer();
            GameAudio.Sfx("fly_star", 0.7f, step > 0 ? 1.2f : 1f);
            PlayerPrefs.SetString(ProgressKey, _progress.Save());
        }

        // ---------------------------------------------------------------- input

        /// <summary>Punches come from every new camera sample (the detector needs them all, in order).</summary>
        void ReadPunches()
        {
            var body = _input != null && _input.Gestures != null ? _input.Gestures.Body : null;
            if (body == null || body.SampleTime == _lastSample) return;
            _lastSample = body.SampleTime;
            if (_punches.Update(body.SampleTime, ArmSample.From(body, false), ArmSample.From(body, true), body.FastTorsoLength,
                                body.HasWorld ? body.WorldTorsoLength : 0f, out var info))
            {
                // for tuning with a real camera: how fast the fist was and how straight the arm
                if (Current == Phase.Playing) Debug.Log($"[Fly] Punch seen: {info.Speed:0.0} torso/s, arm {_punches.Reach(info.RightHand):0.00} straight");
                QueuePunch(info.RightHand, info.BothHands);
            }
        }

        void ReadKeys()
        {
            if (!UsingKeys || Current != Phase.Playing) return;
            var kb = Keyboard.current;
            bool punch = (kb != null && kb.spaceKey.wasPressedThisFrame) || RemoteInput.Confirm;
            if (!punch) return;
            QueuePunch(_nextKeyRight);
            _nextKeyRight = !_nextKeyRight;
        }

        void QueuePunch(bool right, bool both = false)
        {
            _pendingPunch = true;
            _pendingRight = right;
            _pendingBoth = both;
            if (Current != Phase.Playing) return;
            _hero.Punch(right);
            if (both) _hero.Punch(!right);
            GameAudio.Sfx("fly_punch", 0.6f, 1f + Random.Range(-0.08f, 0.08f));
            Punched?.Invoke(right);
            if (both) Punched?.Invoke(!right);
            _heroPunchTimes.Enqueue(Time.time);
            if (both) _heroPunchTimes.Enqueue(Time.time);
            Debug.Log($"[Fly] Punch {(both ? "both" : right ? "right" : "left")}");
        }

        /// <summary>The hero's punches a second over the last two seconds (the clash shows it beside the champion's).</summary>
        float HeroPunchRate
        {
            get
            {
                while (_heroPunchTimes.Count > 0 && Time.time - _heroPunchTimes.Peek() > 2f) _heroPunchTimes.Dequeue();
                return _heroPunchTimes.Count / 2f;
            }
        }

        /// <summary>Where the hero should be (-1..1 each way): the body, or the arrow keys held.</summary>
        Vector2 Steering(float dt)
        {
            if (UsingKeys)
            {
                var kb = Keyboard.current;
                if (kb == null) return Vector2.zero;
                float x = (kb.rightArrowKey.isPressed || kb.dKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed || kb.aKey.isPressed ? 1f : 0f);
                float y = (kb.upArrowKey.isPressed || kb.wKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed || kb.sKey.isPressed ? 1f : 0f);
                return new Vector2(x, y);
            }
            var g = _input.Gestures;
            // a jump lifts the hero for a moment (then it glides back down)
            if (g.IsJumping) _lift = 1f;
            else _lift = Mathf.Max(0f, _lift - dt / jumpLift);
            return FlyRun.Steer(g.LateralOffset, g.VerticalOffset, Mathf.Clamp01(_lift * 1.5f));
        }

        // ---------------------------------------------------------------- play

        void Play(float dt)
        {
            if (_endIn >= 0f)
            {
                Run.Step(dt, Vector2.zero, false);
                Handle(Run.Events);
                _endIn -= dt;
                if (_endIn < 0f) EndRound();
                return;
            }
            // a hit freezes the action for a blink (the punch lands harder); a punch meanwhile waits for it
            if (_hitStop > 0f)
            {
                _hitStop -= dt;
                return;
            }
            // a knockout plays in slow motion for a moment
            float step = dt;
            if (_slowMo > 0f)
            {
                _slowMo -= dt;
                step *= 0.35f;
            }
            Run.Step(step, Steering(dt), _pendingPunch, _pendingRight, _pendingBoth);
            _pendingPunch = false;
            Handle(Run.Events);
            foreach (var m in _progress.Update(Run))
            {
                AddCard(FlyStrings.MissionDone, $"{FlyStrings.MissionText(m)}  +{m.Reward} ★", Sun);
                GameAudio.Jingle("jingle_win", 0.25f);
                Debug.Log($"[Fly] Mission done: {m.Kind} {m.Target} (+{m.Reward} stars)");
            }
            if (Run.Over && _endIn < 0f) _endIn = 2.4f;
        }

        void Handle(IReadOnlyList<FlyEvent> events)
        {
            foreach (var e in events)
            {
                RunEvent?.Invoke(e);
                var at = World(e.Position);
                if (e.Kind is FlyEventKind.RobotHit or FlyEventKind.RobotSmashed or FlyEventKind.FireballReturned or FlyEventKind.DroneSmashed)
                {
                    _comboAt = Time.time;
                    LungeAt(e.Position);
                }
                switch (e.Kind)
                {
                    case FlyEventKind.DistrictStarted:
                        int district = (int)e.Value;
                        Toast(FlyStrings.District(district), district == 1 ? FlyStrings.SaveTheCity : FlyStrings.Faster);
                        if (district > 1)
                        {
                            GameAudio.Sfx("go", vary: 0f);
                            SetTheme(district, instantly: false);
                        }
                        Debug.Log($"[Fly] District {district}");
                        break;
                    case FlyEventKind.NewEnemy:
                        var kind = (EnemyKind)(int)e.Value;
                        AddCard(FlyStrings.EnemyName(kind), FlyStrings.EnemyHow(kind), kind == EnemyKind.Boss ? Coral : Sky);
                        break;
                    case FlyEventKind.SwarmComes:
                        GameAudio.Sfx("fly_whoosh", 0.6f, 1.35f);
                        AddPopup(at + Vector3.up * 1.4f, FlyStrings.Swarm, Sky);
                        break;
                    case FlyEventKind.DroneSmashed:
                        GameAudio.Sfx("fly_smash", 0.55f, 1.35f);
                        Sparks(at, 8, Sky);
                        Sparks(at, 4, Sun);
                        AddPopup(at + Vector3.up * 0.4f, $"+{e.Points}", Sun);
                        _shake = Mathf.Max(_shake, 0.06f);
                        break;
                    case FlyEventKind.DroneBumped:
                        GameAudio.Sfx("fly_bosshit", 0.4f, 1.7f);
                        _shake = Mathf.Max(_shake, 0.15f);
                        Sparks(_hero.transform.position + Vector3.forward * 0.6f, 6, Sky);
                        break;
                    case FlyEventKind.SwarmCleared:
                        GameAudio.Sfx("fly_star", 0.8f, 1.5f, 0f);
                        AddPopup(at + Vector3.up * 1f, $"{FlyStrings.FullSwarm} +{e.Points}", Sun);
                        break;
                    case FlyEventKind.RushStarts:
                        Toast(FlyStrings.Stop, FlyStrings.StopHow);
                        GameAudio.Sfx("fly_boss", 0.6f, 1.3f);
                        Debug.Log($"[Fly] Rush, district {Run.District}: {Run.RushSize} robots in {Run.Settings.Groups} groups");
                        break;
                    case FlyEventKind.GroupComes:
                        GameAudio.Sfx("fly_whoosh", 0.6f, 1.1f);
                        break;
                    case FlyEventKind.RushCleared:
                        AddPopup(_hero.transform.position + Vector3.up * 1.2f + Vector3.forward * 2f, $"{FlyStrings.RushWon} +{e.Points}", Sun);
                        GameAudio.Jingle("jingle_win", 0.2f);
                        Debug.Log($"[Fly] Rush beaten, hearts lost {e.Value}, score {Run.Score}");
                        break;
                    case FlyEventKind.RobotAttacks:
                        GameAudio.Sfx("fly_whoosh", 0.3f, 1.2f);
                        break;
                    case FlyEventKind.RobotCharges:
                        GameAudio.Sfx("fly_charge", 0.6f);
                        break;
                    case FlyEventKind.RobotHit:
                        // an armoured robot dented: a heavy clang, sparks, it reels back
                        GameAudio.Sfx("fly_bosshit", 0.8f, 1.4f);
                        Sparks(at, 12, Color.white);
                        AddPopup(at + Vector3.up * 0.6f, $"{FlyStrings.Smash} +{e.Points}", Color.white);
                        _shake = Mathf.Max(_shake, 0.1f);
                        _hitStop = Mathf.Max(_hitStop, 0.07f);
                        if (_views.TryGetValue(e.ThingId, out var dented) && dented.Robot != null) dented.Robot.Flash(Color.white);
                        break;
                    case FlyEventKind.RobotSmashed:
                        GameAudio.Sfx("fly_smash");
                        Sparks(at, 14, Sun);
                        Scrap(at, 10);
                        AddPopup(at + Vector3.up * 0.6f, $"{FlyStrings.Smash} +{e.Points}", Color.white);
                        _shake = Mathf.Max(_shake, 0.12f);
                        // only a punch freezes the action (not a spark knocking out its thrower far away)
                        if (e.Position.z < Run.ReachFar + 1f) _hitStop = Mathf.Max(_hitStop, 0.05f);
                        break;
                    case FlyEventKind.ClashStarts:
                        bool withBoss = e.Value > 0.5f;
                        Toast(withBoss ? FlyStrings.FinalClash : FlyStrings.Clash, FlyStrings.ClashHow);
                        GameAudio.Sfx("fly_boss", 0.9f, withBoss ? 0.8f : 1.1f);
                        if (!withBoss) ShowChampion();
                        Debug.Log($"[Fly] Clash{(withBoss ? " with the boss" : "")}, the champion punches {Run.Clash.Rate:0.0}/s");
                        break;
                    case FlyEventKind.ClashGo:
                        Toast(FlyStrings.ClashGo, FlyStrings.ClashHow);
                        GameAudio.Sfx("go", vary: 0f);
                        break;
                    case FlyEventKind.ClashPunch:
                        // the hero's fist meets the champion: a spark between them, the champion rocks back
                        Sparks(at, 7, Sun);
                        GameAudio.Sfx("fly_bosshit", 0.35f, 1.3f + Random.Range(-0.1f, 0.1f));
                        _shake = Mathf.Max(_shake, 0.05f);
                        var target = ClashView();
                        if (target != null) target.Flash(Color.white);
                        _comboAt = Time.time;
                        _lunge = new Vector3(0f, 0f, 0.35f);
                        break;
                    case FlyEventKind.ClashRobotPunch:
                        var puncher = ClashView();
                        if (puncher != null) puncher.Jab(_jabRight = !_jabRight);
                        Sparks(at, 5, Coral);
                        GameAudio.Sfx("fly_bosshit", 0.3f, 0.85f + Random.Range(-0.08f, 0.08f));
                        _lunge = new Vector3(0f, 0f, -0.3f);   // the hero rocks back
                        break;
                    case FlyEventKind.ClashFlurry:
                        GameAudio.Sfx("fly_charge", 0.7f, 1.2f);   // (the HUD says "faster!" under the bar)
                        break;
                    case FlyEventKind.ClashWon:
                        Knockout(at, e.Points);
                        Toast(FlyStrings.DistrictSaved(Run.District), FlyStrings.NextDistrict);
                        Debug.Log($"[Fly] Clash won in {e.Value:0.0} s, score {Run.Score}");
                        break;
                    case FlyEventKind.SparkThrown:
                        GameAudio.Sfx("fly_throw", 0.5f, 1.4f);
                        break;
                    case FlyEventKind.RobotHitHero:
                    case FlyEventKind.FireballHitHero:
                    case FlyEventKind.ClashLost:
                        GameAudio.Sfx("fly_hurt");
                        _hero.Hurt();
                        _shake = 0.4f;
                        Sparks(_hero.transform.position + Vector3.forward * 0.6f, 12, Coral);
                        AddPopup(_hero.transform.position + Vector3.up * 0.8f, e.Kind == FlyEventKind.ClashLost ? FlyStrings.ClashLost : FlyStrings.Ouch, Coral);
                        if (e.Kind == FlyEventKind.FireballHitHero) _hintFireballsUntil = Time.time + 4f;
                        if (e.Kind == FlyEventKind.ClashLost) _lunge = new Vector3(0f, 0.2f, -1.2f);   // knocked back
                        Debug.Log($"[Fly] Hurt ({e.Kind}), hearts {Run.Hearts}");
                        break;
                    case FlyEventKind.RobotMissed:
                        GameAudio.Sfx("fly_whoosh", 0.7f);
                        break;
                    case FlyEventKind.StarCollected:
                        _starCombo = Time.time < _starComboUntil ? _starCombo + 1 : 0;
                        _starComboUntil = Time.time + 0.6f;
                        GameAudio.Sfx("fly_star", 0.7f, Mathf.Pow(1.0595f, Mathf.Min(_starCombo, 12) * 2f), 0f);   // rising notes
                        Sparks(at, 6, Sun);
                        break;
                    case FlyEventKind.HeartCollected:
                        GameAudio.Sfx("fly_heart");
                        Sparks(at, 10, Coral);
                        AddPopup(at + Vector3.up * 0.5f, FlyStrings.HeartBack, Coral);
                        break;
                    case FlyEventKind.BossAppears:
                        Toast(FlyStrings.BossComing, FlyStrings.PunchFireballs);
                        _hintFireballsUntil = Time.time + 8f;
                        GameAudio.Sfx("fly_boss");
                        Debug.Log($"[Fly] Boss (hp {Run.Boss.Hp})");
                        break;
                    case FlyEventKind.BossThrows:
                        if (_boss != null) _boss.Throw();
                        GameAudio.Sfx("fly_throw", 0.8f);
                        break;
                    case FlyEventKind.FireballReturned:
                        GameAudio.Sfx("fly_return");
                        Sparks(at, 12, Sun);
                        AddPopup(at + Vector3.up * 0.5f, $"{FlyStrings.Back} +{e.Points}", Sun);
                        break;
                    case FlyEventKind.BossHit:
                        GameAudio.Sfx("fly_bosshit");
                        if (_boss != null) _boss.Flash(Color.white);
                        Sparks(at, 20, Sun);
                        _shake = Mathf.Max(_shake, 0.2f);
                        Debug.Log($"[Fly] Boss hit, hp {Run.Boss.Hp}");
                        break;
                    case FlyEventKind.BossDefeated:
                        GameAudio.Sfx("fly_bossdown");
                        for (int i = 0; i < 3; i++) BurstConfetti(at + Random.insideUnitSphere * 2f, 30);
                        Debug.Log($"[Fly] Boss defeated, district {Run.District}, score {Run.Score}");
                        break;
                    case FlyEventKind.NewRecord:
                        AddPopup(_hero.transform.position + Vector3.up * 1.5f + Vector3.forward * 2f, FlyStrings.NewRecord, Sun);
                        GameAudio.Jingle("jingle_win", 0.3f);
                        break;
                    case FlyEventKind.Lost:
                        GameAudio.StopMusic(0.4f);
                        GameAudio.Jingle("jingle_gameover");
                        break;
                }
            }
        }

        /// <summary>The champion (or the boss) knocked out: it spins away in slow motion, confetti, a big "+points".</summary>
        void Knockout(Vector3 at, int points)
        {
            var view = ClashView();
            if (view != null)
            {
                view.Smash(new Vector3(Random.Range(400f, 800f), Random.Range(-500f, 500f), Random.Range(-700f, 700f)));
                _ko = view.transform;
                _koVelocity = new Vector3(Random.Range(-3f, 3f), 7f, 20f);
                _koAge = 0f;
                if (view == _champion) _champion = null;
                else _boss = null;
            }
            _slowMo = 0.7f;
            _hitStop = 0.12f;
            _shake = 0.35f;
            GameAudio.Sfx("fly_bossdown");
            BurstConfetti(at, 50);
            Sparks(at, 30, Sun);
            AddPopup(at + Vector3.up * 1.5f, $"{FlyStrings.KnockOut} +{points}", Sun);
            _hero.Cheer();
            GameAudio.Jingle("jingle_win", 0.35f);
        }

        /// <summary>The one face to face with the hero in the clash.</summary>
        RobotView ClashView() => Run?.Clash != null && Run.Clash.WithBoss ? _boss : _champion;

        void ShowChampion()
        {
            if (_champion != null) Destroy(_champion.gameObject);
            if (_championShadow != null) Destroy(_championShadow.gameObject);
            _champion = RobotView.Create(actorMaterial, ChampionPaint, champion: true);
            _champion.transform.localScale = Vector3.one * 1.7f;
            _champion.transform.position = World(Run.Clash.Position);
            _championShadow = Shadow(2.4f);
        }

        // ---------------------------------------------------------------- the time of day

        void SetTheme(int district, bool instantly)
        {
            var next = FlyTheme.For(district);
            _themeFrom = instantly || _themeBlend >= 1f ? (instantly ? next : _themeTo) : FlyTheme.Lerp(_themeFrom, _themeTo, _themeBlend);
            _themeTo = next;
            _themeBlend = instantly ? 1f : 0f;
            if (instantly) _themeTo.Apply(_sky, _sun, world != null ? world.CityMaterial : null);
        }

        void UpdateTheme(float dt)
        {
            if (_themeBlend >= 1f) return;
            _themeBlend = Mathf.Min(1f, _themeBlend + dt / 3f);
            FlyTheme.Lerp(_themeFrom, _themeTo, Mathf.SmoothStep(0f, 1f, _themeBlend)).Apply(_sky, _sun, world != null ? world.CityMaterial : null);
        }

        // ---------------------------------------------------------------- drawing the run

        /// <summary>The flight frame (x right, y up around the flying height, z ahead) in the scene.</summary>
        static Vector3 World(Vector3 p) => new Vector3(p.x, FlyWorld.FlyHeight + p.y, p.z);

        /// <summary>The hero's model lunges at what a punch hit (at most half a metre aside, a little forward).</summary>
        void LungeAt(Vector3 target)
        {
            if (Run == null || target.z > Run.ReachFar + 1f) return;   // a spark hitting its thrower far away: no
            var aside = Vector2.ClampMagnitude(new Vector2(target.x - Run.Hero.x, target.y - Run.Hero.y) * 0.4f, 0.5f);
            _lunge = new Vector3(aside.x, aside.y, 0.45f);
        }

        void UpdateViews(float dt)
        {
            if (Run == null)
            {
                _hero.transform.position = World(new Vector3(0f, Mathf.Sin(Time.time) * 0.1f, 0f));
                return;
            }
            _lunge = Vector3.Lerp(_lunge, Vector3.zero, 1f - Mathf.Exp(-9f * dt));
            _hero.transform.position = World(new Vector3(Run.Hero.x, Run.Hero.y, 0f) + _lunge);

            // the boss: flying in, throwing, and face to face in the final clash
            bool bossShown = Run.Boss != null && (Run.Phase is FlyPhase.BossArrives or FlyPhase.Boss ||
                                                  Run.Clash != null && Run.Clash.WithBoss && !Run.Clash.Won);
            if (bossShown)
            {
                if (_boss == null)
                {
                    _boss = RobotView.Create(actorMaterial, BossPaint, boss: true);
                    _boss.transform.localScale = Vector3.one * 4.2f;
                    if (_bossShadow == null) _bossShadow = Shadow(5.5f);
                }
                _boss.transform.position = World(Run.Boss.Position);
                PlaceShadow(_bossShadow, _boss.transform.position, 5.5f);
                _boss.Warn(Run.Clash != null && Run.Clash.WithBoss ? 0.4f + 0.6f * Mathf.Clamp01(-Run.Clash.Bar) : 0f);
            }
            else if (_boss != null && _ko != _boss.transform)
            {
                Destroy(_boss.gameObject);
                _boss = null;
            }
            if (!bossShown && _bossShadow != null)
            {
                Destroy(_bossShadow.gameObject);
                _bossShadow = null;
            }

            // the champion, face to face; its eye reddens as it gets the upper hand
            if (_champion != null && Run.Clash != null && !Run.Clash.WithBoss)
            {
                _champion.transform.position = World(Run.Clash.Position);
                PlaceShadow(_championShadow, _champion.transform.position, 2.4f);
                _champion.Warn(Run.Clash.Flurry > 0f ? 1f : 0.2f + 0.6f * Mathf.Clamp01(-Run.Clash.Bar));
            }
            else if (_champion != null)
            {
                Destroy(_champion.gameObject);
                _champion = null;
            }
            if (_champion == null && _championShadow != null)
            {
                Destroy(_championShadow.gameObject);
                _championShadow = null;
            }
            UpdateKnockout(dt);

            // things: create views for new ones, move them, remove finished ones (reused sets: no garbage per frame)
            _alive.Clear();
            foreach (var t in Run.Things)
            {
                _alive.Add(t.Id);
                if (!_views.TryGetValue(t.Id, out var v)) _views[t.Id] = v = CreateView(t);
                Place(v, dt);
            }
            _gone.Clear();
            foreach (var pair in _views)
                if (!_alive.Contains(pair.Key)) _gone.Add(pair.Key);
            foreach (int id in _gone)
            {
                var v = _views[id];
                if (v.Thing.Kind == ThingKind.Fireball && !v.Smashed) Sparks(v.T.position, 8, Coral);   // fizzles out
                DestroyView(v);
                _views.Remove(id);
            }
        }

        /// <summary>A knocked-out champion or boss tumbling away up the street.</summary>
        void UpdateKnockout(float dt)
        {
            if (_ko == null) return;
            _koAge += dt;
            _koVelocity += Vector3.down * (6f * dt);
            _ko.position += _koVelocity * dt;
            if (Random.value < dt * 30f) Sparks(_ko.position, 2, Sun);
            if (_koAge < 1.8f) return;
            Destroy(_ko.gameObject);
            _ko = null;
        }

        View CreateView(Thing t)
        {
            var v = new View { Thing = t, Spin = Random.Range(-1f, 1f) };
            switch (t.Kind)
            {
                case ThingKind.Robot:
                    // throwers are orange, darts small and yellow; armoured robots are bigger and wear a helmet
                    var paint = t.Thrower ? ThrowerPaint : t.Dart ? DartPaint : RobotPaints[t.Id % RobotPaints.Length];
                    v.Robot = RobotView.Create(actorMaterial, paint, armoured: t.Big);
                    v.T = v.Robot.transform;
                    v.Size = t.Big ? 1.3f : t.Dart ? 0.8f : 1f;
                    v.T.localScale = Vector3.one * v.Size;
                    v.Ring = MakeRing();
                    v.Shadow = Shadow(1.3f * v.Size);
                    break;
                case ThingKind.Drone:
                    v.Robot = RobotView.Create(actorMaterial, DronePaint);
                    v.T = v.Robot.transform;
                    v.Size = 0.55f;
                    v.T.localScale = Vector3.one * v.Size;
                    v.Ring = MakeRing();
                    break;
                case ThingKind.Star:
                    v.T = MeshThing("Star", _star, toonMaterial, Sun, 0.75f, Sun * 0.45f);
                    break;
                case ThingKind.Heart:
                    v.T = MeshThing("Heart", _heartMesh, actorMaterial, Color.white, 1f, Coral * 0.3f);
                    break;
                default:
                    // the boss's fireballs; a thrower's sparks are small electric-blue bolts
                    v.Size = t.Small ? 0.6f : 1f;
                    v.T = FireballThing(t.Small);
                    v.Ring = MakeRing();
                    v.Shadow = Shadow(v.Size);
                    break;
            }
            v.T.position = World(t.Position);
            return v;
        }

        void Place(View v, float dt)
        {
            var t = v.Thing;
            v.T.position = World(t.Position);
            if (v.Shadow != null) PlaceShadow(v.Shadow, v.T.position, t.Kind == ThingKind.Robot ? 1.3f * v.Size : v.Size);
            switch (t.Kind)
            {
                case ThingKind.Robot:
                case ThingKind.Drone:
                    // a robot flying in pops into view (no robot appearing from nowhere in the distance)
                    v.Age += dt;
                    if (!v.Smashed && v.Age < 0.4f)
                    {
                        float k = v.Age / 0.4f, back = 1f + 2.2f * (k - 1f) * (k - 1f) * (k - 1f) + 1.2f * (k - 1f) * (k - 1f);
                        v.T.localScale = Vector3.one * (v.Size * Mathf.Max(0.01f, back));
                    }
                    else if (!v.Smashed) v.T.localScale = Vector3.one * v.Size;
                    // its eye glows hotter the closer it is to striking
                    float warn = t.State switch
                    {
                        ThingState.Lunge => 1f,
                        ThingState.Windup => Mathf.Clamp01(0.4f + 0.6f * t.StateTime / Run.Settings.WindupSeconds),
                        ThingState.Advance => 0.3f,
                        _ => 0f,
                    };
                    v.Robot.Warn(warn);
                    if (t.State == ThingState.Smashed && t.Punched && !v.Smashed)
                    {
                        v.Smashed = true;
                        v.Robot.Smash(new Vector3(Random.Range(300f, 700f), Random.Range(-400f, 400f), Random.Range(-600f, 600f)));
                    }
                    break;
                case ThingKind.Star:
                case ThingKind.Heart:
                    v.T.rotation = Quaternion.Euler(0f, Time.time * 180f * (0.7f + 0.3f * v.Spin), 0f);
                    break;
                default:
                    if (t.State == ThingState.Smashed && !v.Smashed)
                    {
                        v.Smashed = true;   // punched back: it takes on the hero's gold
                        foreach (var r in v.T.GetComponentsInChildren<Renderer>()) Tint(r, Sun, Sun * 1.4f);
                    }
                    float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 18f + v.Spin * 5f);
                    v.T.localScale = Vector3.one * (pulse * v.Size);
                    v.T.rotation = cam.transform.rotation;
                    if (Random.value < dt * 20f) Trail(v.T.position, v.Smashed ? Sun : t.Small ? Spark : Coral);
                    break;
            }
            UpdateRing(v);
        }

        /// <summary>A ring around a robot, drone or fireball: gold and pulsing when a punch would hit it, faint white for a
        /// robot on its way in to attack (get ready), hidden otherwise.</summary>
        void UpdateRing(View v)
        {
            if (v.Ring == null) return;
            var t = v.Thing;
            bool inReach = Current == Phase.Playing && !Run.Over && Run.CanPunch(t);
            bool target = inReach && Run.InReach(true) == t || inReach && Run.InReach(false) == t;
            bool coming = t.Kind == ThingKind.Robot && t.State == ThingState.Advance && !inReach;
            v.Ring.gameObject.SetActive(target || coming);
            if (!v.Ring.gameObject.activeSelf) return;
            float size = (t.Kind == ThingKind.Fireball ? 1.1f : 1.5f) * v.Size * (target ? 1f + 0.08f * Mathf.Sin(Time.time * 14f) : 1.1f);
            v.Ring.SetPositionAndRotation(v.T.position, cam.transform.rotation);
            v.Ring.localScale = Vector3.one * size;
            Tint(v.Ring.GetComponent<Renderer>(), target ? new Color(Sun.r, Sun.g, Sun.b, 0.95f) : new Color(1f, 1f, 1f, 0.35f));
        }

        Transform MakeRing()
        {
            var go = new GameObject("Reach ring");
            go.AddComponent<MeshFilter>().sharedMesh = _ring;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _ringMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        Transform MeshThing(string name, Mesh mesh, Material material, Color color, float size, Color glow = default)
        {
            var root = new GameObject(name + " root").transform;
            var r = PartMesh.Show(name, root, mesh, material);
            Tint(r, color, glow);
            r.transform.localScale = Vector3.one * size;
            return root;
        }

        /// <summary>The classic heart from two balls and a tilted block, as one glossy mesh.</summary>
        static Mesh HeartMesh() => new PartMesh()
            .Add(PrimitiveType.Sphere, new Vector3(-0.17f, 0.12f, 0f), new Vector3(0.42f, 0.42f, 0.3f), Coral, 1.2f)
            .Add(PrimitiveType.Sphere, new Vector3(0.17f, 0.12f, 0f), new Vector3(0.42f, 0.42f, 0.3f), Coral, 1.2f)
            .Add(PrimitiveType.Cube, new Vector3(0f, -0.08f, 0f), new Vector3(0.42f, 0.42f, 0.28f), Coral, 1.2f, new Vector3(0f, 0f, 45f))
            .Build("Heart");

        /// <summary>A fireball: a white-hot core (bright enough to bloom) and a big soft glow facing the camera. A thrower's
        /// spark is the same in electric blue.</summary>
        Transform FireballThing(bool spark)
        {
            var root = new GameObject(spark ? "Spark" : "Fireball").transform;
            var core = PartMesh.Show("Core", root, PartMesh.Primitive(PrimitiveType.Sphere), toonMaterial);
            core.transform.localScale = Vector3.one * 0.55f;
            if (spark) Tint(core, new Color(0.6f, 0.9f, 1f), new Color(0.5f, 1.3f, 1.8f));
            else Tint(core, new Color(1f, 0.6f, 0.3f), new Color(1.6f, 0.7f, 0.25f));
            var glow = PartMesh.Show("Glow", root, PartMesh.Primitive(PrimitiveType.Quad), glowMaterial);
            glow.transform.localScale = Vector3.one * 1.6f;
            Tint(glow, spark ? Spark : Coral);
            return root;
        }

        void Tint(Renderer r, Color c, Color glow = default)
        {
            _tint.SetColor("_BaseColor", c);
            _tint.SetColor("_EmissionColor", glow);
            r.SetPropertyBlock(_tint);
        }

        void DestroyView(View v)
        {
            if (v.Ring != null) Destroy(v.Ring.gameObject);
            if (v.Shadow != null) Destroy(v.Shadow.gameObject);
            if (v.T != null) Destroy(v.T.gameObject);
        }

        /// <summary>A soft round shadow lying on the street (Tundu/Fx).</summary>
        Transform Shadow(float size)
        {
            var r = PartMesh.Show("Shadow", null, PartMesh.Primitive(PrimitiveType.Quad), shadowMaterial);
            r.transform.localScale = Vector3.one * size;
            return r.transform;
        }

        /// <summary>The shadow on the street below <paramref name="over"/>, bent down with the curved world like the street
        /// (Tundu/Fx doesn't bend), smaller and fainter for things high up.</summary>
        void PlaceShadow(Transform shadow, Vector3 over, float size)
        {
            if (shadow == null) return;
            float d = Mathf.Max(0f, over.z - cam.transform.position.z);
            shadow.SetPositionAndRotation(new Vector3(over.x, 0.06f - Curve * d * d, over.z), Quaternion.Euler(90f, 0f, 0f));
            shadow.localScale = Vector3.one * (size * Mathf.Lerp(1.1f, 0.8f, Mathf.Clamp01((over.y - FlyWorld.FlyHeight) / 3f)));
        }

        void ClearViews()
        {
            foreach (var v in _views.Values) DestroyView(v);
            _views.Clear();
            if (_boss != null) Destroy(_boss.gameObject);
            if (_bossShadow != null) Destroy(_bossShadow.gameObject);
            if (_champion != null) Destroy(_champion.gameObject);
            if (_championShadow != null) Destroy(_championShadow.gameObject);
            if (_ko != null) Destroy(_ko.gameObject);
            _boss = _champion = null;
            _ko = null;
        }

        // ---------------------------------------------------------------- effects

        // Effects are particles (one draw call per kind, nothing created or destroyed per spark): star sparks, soft
        // glows (fireball trails), lit bits (scrap metal and confetti, small cubes with the actor material) and speed
        // lines near the camera.

        void BuildEffects()
        {
            _sparks = Particles("Sparks", null, sparkMaterial, 400, drag: 4f);
            _glow = Particles("Glow", null, glowMaterial, 300, drag: 2f);
            _bits = Particles("Bits", null, actorMaterial, 300, gravity: 1f);
            var bits = _bits.GetComponent<ParticleSystemRenderer>();
            bits.renderMode = ParticleSystemRenderMode.Mesh;
            bits.mesh = PartMesh.Primitive(PrimitiveType.Cube);
            bits.alignment = ParticleSystemRenderSpace.World;
            var main = _bits.main;
            main.startRotation3D = true;
            main.startSize3D = true;
            if (streakMaterial != null)
            {
                _speed = Particles("Speed lines", cam.transform, streakMaterial, 160, worldSpace: false);
                var r = _speed.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.03f;
                r.lengthScale = 1.5f;
            }
        }

        static ParticleSystem Particles(string name, Transform parent, Material material, int max, float gravity = 0f, float drag = 0f,
                                        bool worldSpace = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.maxParticles = max;
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.drag = drag;
                limit.limit = 1000f;
            }
            // shrink away at the end of their life (works for opaque bits too, unlike fading)
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 1f), new Keyframe(1f, 0f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, float life, Color color, float spin = 0f)
        {
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize = size, startLifetime = life, startColor = color,
                rotation = Random.Range(0f, 360f), angularVelocity = spin,
            }, 1);
        }

        static void EmitBit(ParticleSystem ps, Vector3 at, Vector3 velocity, Vector3 size, float life, Color color, float gloss)
        {
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize3D = size, startLifetime = life,
                startColor = new Color(color.r, color.g, color.b, gloss),
                rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                angularVelocity3D = Random.insideUnitSphere * 700f,
            }, 1);
        }

        void Sparks(Vector3 at, int count, Color color)
        {
            for (int i = 0; i < count; i++)
                Emit(_sparks, at, Random.onUnitSphere * Random.Range(3f, 7f), Random.Range(0.14f, 0.3f), Random.Range(0.3f, 0.6f), color,
                     Random.Range(-720f, 720f));
        }

        void Trail(Vector3 at, Color color) =>
            Emit(_glow, at + Random.insideUnitSphere * 0.15f, Vector3.back * (Run.CurrentSpeed * 0.2f), Random.Range(0.35f, 0.55f), 0.35f, color);

        /// <summary>Bits of metal flying off a smashed robot.</summary>
        void Scrap(Vector3 at, int count)
        {
            for (int i = 0; i < count; i++)
                EmitBit(_bits, at + Random.insideUnitSphere * 0.2f, new Vector3(Random.Range(-3f, 3f), Random.Range(1f, 5f), Random.Range(2f, 8f)),
                        Vector3.one * Random.Range(0.08f, 0.16f), Random.Range(0.6f, 1f), i % 3 == 0 ? Sun : new Color(0.66f, 0.7f, 0.82f), 1.2f);
        }

        void BurstConfetti(Vector3 at, int count)
        {
            for (int i = 0; i < count; i++)
                EmitBit(_bits, at + Random.insideUnitSphere * 0.5f, new Vector3(Random.Range(-5f, 5f), Random.Range(3f, 8f), Random.Range(-3f, 3f)),
                        new Vector3(Random.Range(0.12f, 0.22f), Random.Range(0.12f, 0.22f), 0.02f), Random.Range(1.2f, 1.8f),
                        Confetti[Random.Range(0, Confetti.Length)], 0.5f);
        }

        void UpdatePopups(float dt)
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].Age += dt;
                _popups[i].World += Vector3.up * (0.8f * dt);
                if (_popups[i].Age > 1f) _popups.RemoveAt(i);
            }
            // one card at a time, each for a few seconds
            if (_cards.Count > 0)
            {
                _cards[0].Age += dt;
                if (_cards[0].Age > 3.2f) _cards.RemoveAt(0);
            }
        }

        /// <summary>The hero's shadow on the street, and speed lines streaming past while flying.</summary>
        void UpdateEffects(float dt)
        {
            PlaceShadow(_heroShadow, _hero.transform.position, 1.7f);
            if (_speed == null) return;
            bool flying = Current == Phase.Playing && !_paused && (Run == null || Run.CurrentSpeed > 4f);
            _speedLines += dt * (flying ? 26f : 0f);
            while (_speedLines >= 1f)
            {
                _speedLines -= 1f;
                float a = Random.Range(0f, Mathf.PI * 2f);
                var local = new Vector3(Mathf.Cos(a) * Random.Range(3f, 4.6f), Mathf.Sin(a) * Random.Range(1.8f, 2.8f), 16f);
                Emit(_speed, local, new Vector3(0f, 0f, -60f), Random.Range(0.04f, 0.08f), 0.32f, new Color(1f, 1f, 1f, 0.4f));
            }
        }

        /// <summary>A line in the log every 10 s while playing: frame rate, the worst frame, and the city's size.</summary>
        void LogPerformance()
        {
            if (Current != Phase.Playing) return;
            float frame = Time.unscaledDeltaTime;
            if (_frames == 0) _collections = GC.CollectionCount(0);   // each window counts its own
            _frames++;
            _frameTime += frame;
            _worstFrame = Mathf.Max(_worstFrame, frame);
            if (_frameTime < 10f) return;
            string draws = "";
#if UNITY_EDITOR
            draws = $", {UnityEditor.UnityStats.batches} batches / {UnityEditor.UnityStats.setPassCalls} set-pass / " +
                    $"{UnityEditor.UnityStats.triangles / 1000}k triangles drawn";
#endif
            int collections = GC.CollectionCount(0);
            Debug.Log($"[Fly] perf: {_frames / _frameTime:0.0} fps, worst frame {_worstFrame * 1000f:0} ms, " +
                      $"{collections - _collections} garbage collections, " +
                      $"city {world.Blocks} blocks / {world.Vertices / 1000}k vertices (slowest block build {world.WorstBuildMs:0.0} ms), " +
                      $"turns {_route.TurnsTaken}{draws}");
            _frames = 0;
            _frameTime = _worstFrame = 0f;
            world.ResetStats();
        }

        /// <summary>Behind and above the hero, following it softly (and a little less than it moves, so the hero
        /// visibly flies across the screen), leaning into turns, shaking when hit; closer and lower for the clash, face to
        /// face with the champion.</summary>
        void UpdateCamera(float dt)
        {
            bool clash = Run != null && Run.Phase is FlyPhase.ClashIntro or FlyPhase.Clash;
            _clashZoom = Mathf.MoveTowards(_clashZoom, clash ? 1f : 0f, dt * 1.5f);
            float zoom = Mathf.SmoothStep(0f, 1f, _clashZoom) * (Run?.Clash != null && Run.Clash.WithBoss ? 0.5f : 1f);
            var hero = _hero.transform.position;
            // the clash: over the hero's shoulder (a little to its left), looking between the two of them
            var want = new Vector3(hero.x * 0.55f - 2.1f * zoom, FlyWorld.FlyHeight + (hero.y - FlyWorld.FlyHeight) * 0.45f + Mathf.Lerp(CameraHeight, 1.5f, zoom),
                                   -Mathf.Lerp(CameraBack, 3.4f, zoom));
            var pos = Vector3.SmoothDamp(cam.transform.position, want, ref _camVelocity, 0.18f, Mathf.Infinity, dt);
            _shake = Mathf.Max(0f, _shake - dt);
            var look = new Vector3(hero.x * 0.75f, FlyWorld.FlyHeight + (hero.y - FlyWorld.FlyHeight) * 0.6f + LookHeight, LookAhead);
            if (zoom > 0f && Run?.Clash != null)
            {
                var between = (hero + World(Run.Clash.Position)) * 0.5f + Vector3.up * 0.3f;
                look = Vector3.Lerp(look, between, zoom);
            }
            cam.transform.position = pos + (Vector3)(Random.insideUnitCircle * (0.25f * _shake));
            cam.transform.rotation = Quaternion.LookRotation(look - pos) * Quaternion.Euler(0f, 0f, -_camVelocity.x * 1.5f - _turnLean * 7f);
        }

        void AddPopup(Vector3 world, string text, Color color) => _popups.Add(new Popup { World = world, Text = text, Color = color });

        void AddCard(string title, string text, Color color)
        {
            if (_cards.Count < 4) _cards.Add(new Card { Title = title, Text = text, Color = color });
        }

        void Toast(string title, string subtitle)
        {
            _toast = title;
            _toastSub = subtitle;
            _toastUntil = Time.time + 2.5f;
        }

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
                c.a = 1f - p.Age;
                GameHud.Text(new Rect(g.x - 300, g.y - 35, 600, 70), p.Text, GameHud.Medium, TextAnchor.MiddleCenter, c);
            }

            if (Run != null && Current is Phase.Playing or Phase.Results)
            {
                for (int i = 0; i < Run.StartHearts; i++)
                {
                    GUI.color = i < Run.Hearts ? Coral : new Color(1f, 1f, 1f, 0.25f);
                    GUI.DrawTexture(new Rect(30 + i * 70, 24, 62, 62), _heart);
                }
                GUI.color = Color.white;
                GameHud.Text(new Rect(30, 96, 600, 60), $"{CommonStrings.Score}: {Run.Score}", GameHud.Medium, TextAnchor.UpperLeft);
                GameHud.Text(new Rect(w - 630, 20, 600, 60), $"{CommonStrings.Best}: {Mathf.Max(Best, Run.Score)}", GameHud.Medium, TextAnchor.UpperRight);
                GameHud.Text(new Rect(w - 630, 80, 600, 50), $"{FlyStrings.District(Run.District)}   ★ {Run.StarsCollected}", GameHud.Small,
                             TextAnchor.UpperRight, Sun);
                if (Current == Phase.Playing && Run.Combo >= 2)
                {
                    // the combo grows bigger with every hit in a row
                    float pop = 1f + 0.15f * Mathf.Clamp01(1f - (Time.time - _comboAt) * 4f);
                    var style = GameHud.Medium;
                    int size = style.fontSize;
                    style.fontSize = Mathf.RoundToInt(size * pop * Mathf.Min(1.4f, 1f + Run.Combo * 0.04f));
                    GameHud.Text(new Rect(30, 150, 700, 80), FlyStrings.Combo(Run.Combo), style, TextAnchor.UpperLeft, Sun);
                    style.fontSize = size;
                }
                if (Current == Phase.Playing) DrawProgress(w);
            }

            switch (Current)
            {
                case Phase.Waiting:
                    PlayerGate.DrawWaiting(w, _input, FlyStrings.Title, FlyStrings.KeyboardHelp);
                    GameHud.Text(new Rect(0, 560, w, 60), FlyStrings.HowTo, GameHud.Small);
                    GameHud.Text(new Rect(0, 615, w, 50), $"{FlyStrings.JumpHint}   ·   {FlyStrings.TurnHowTo}", GameHud.Small,
                                 TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
                    DrawMissions(w, 690f);
                    break;
                case Phase.Countdown:
                    int n = Mathf.CeilToInt(countdownSeconds - _phaseTime);
                    GameHud.Message(w, n > 0 ? n.ToString() : CommonStrings.Go, FlyStrings.HowTo);
                    break;
                case Phase.Playing:
                    if (_paused) PlayerGate.DrawPaused(w, _input);
                    else
                    {
                        string hint = Hint();
                        if (hint != null) GameHud.Text(new Rect(0, 170, w, 70), hint, GameHud.Medium, TextAnchor.MiddleCenter, Sun);
                        if (Run != null && Run.Phase == FlyPhase.Flight) DrawTurnArrows(w);
                        if (!UsingKeys) PlayerGate.DrawEdgeWarning(w, _input);
                    }
                    DrawCard(w);
                    break;
                case Phase.Results:
                    DrawResults(w);
                    break;
            }

            if (_toast != null && Time.time < _toastUntil && Current == Phase.Playing)
            {
                GameHud.Text(new Rect(0, 300, w, 120), _toast, _toast.Length > 22 ? GameHud.Medium : GameHud.Big, TextAnchor.MiddleCenter, Sun);
                if (_toastSub != null) GameHud.Text(new Rect(0, 420, w, 70), _toastSub, GameHud.Small);
            }
        }

        /// <summary>The run's end: how far, the score, the stars it brought (and the missions'), missions done, new outfits;
        /// then the missions for the next run.</summary>
        void DrawResults(float w)
        {
            GameHud.Text(new Rect(0, 150, w, 120), CommonStrings.GameOver, GameHud.Big, TextAnchor.MiddleCenter, Sun);
            if (Run != null)
                GameHud.Text(new Rect(0, 265, w, 70), $"{FlyStrings.Reached(Run.District)}    {CommonStrings.Score}: {Run.Score}    " +
                                                        $"{FlyStrings.BestCombo}: x{Run.BestCombo}", GameHud.Medium);
            float y = 345f;
            var r = LastResult;
            if (r != null)
            {
                string best = r.NewBestScore ? CommonStrings.NewBest : FlyStrings.BestDistrict(_progress.BestDistrict);
                GameHud.Text(new Rect(0, y, w, 50), $"{FlyStrings.StarsEarned(r.StarsFromRun, r.StarsFromMissions)}    {best}", GameHud.Small,
                             TextAnchor.MiddleCenter, Sun);
                y += 55f;
                foreach (var m in r.Completed)
                {
                    GameHud.Text(new Rect(0, y, w, 46), $"✓ {FlyStrings.MissionText(m)}  +{m.Reward} ★", GameHud.Small, TextAnchor.MiddleCenter, Leaf);
                    y += 46f;
                }
                foreach (int outfit in r.NewOutfits)
                {
                    float pop = 1f + 0.06f * Mathf.Sin(Time.time * 6f);
                    var style = GameHud.Medium;
                    int size = style.fontSize;
                    style.fontSize = Mathf.RoundToInt(size * pop);
                    GameHud.Text(new Rect(0, y, w, 64), FlyStrings.NewOutfit(outfit), style, TextAnchor.MiddleCenter, Sky);
                    style.fontSize = size;
                    y += 64f;
                }
            }
            if (_phaseTime > 2f)
                GameHud.Text(new Rect(0, y + 10f, w, 60),
                    PlayerGate.PlayAgainText(_input), GameHud.Small);
            DrawMissions(w, Mathf.Max(y + 90f, 690f));
        }

        /// <summary>The three missions (done ones ticked), the stars to the next outfit, the outfit worn.</summary>
        void DrawMissions(float w, float y)
        {
            const float boxW = 1100f;
            float x = (w - boxW) / 2f;
            GameHud.Box(new Rect(x, y, boxW, 300f), new Color(0.05f, 0.06f, 0.16f, 0.6f));
            GameHud.Text(new Rect(x + 30, y + 10, boxW - 60, 46), $"{FlyStrings.Missions} {FlyStrings.InOneRun}", GameHud.Small, TextAnchor.MiddleLeft, Sun);
            float line = y + 58f;
            foreach (var m in _progress.Missions)
            {
                GameHud.Text(new Rect(x + 30, line, boxW - 220, 44), $"{(m.Done ? "✓" : "☆")} {FlyStrings.MissionText(m)}", GameHud.Small,
                             TextAnchor.MiddleLeft, m.Done ? Leaf : Color.white);
                GameHud.Text(new Rect(x + boxW - 200, line, 170, 44), $"+{m.Reward} ★", GameHud.Small, TextAnchor.MiddleRight, Sun);
                line += 44f;
            }
            int next = _progress.NextOutfitStars;
            string stars = next > 0 ? FlyStrings.NextOutfit(_progress.Stars, next) : FlyStrings.AllOutfits;
            GameHud.Text(new Rect(x + 30, line + 6, boxW - 60, 44), stars, GameHud.Small, TextAnchor.MiddleLeft, Sun);
            if (next > 0)
            {
                int from = FlyProgress.OutfitStars[_progress.Unlocked - 1];
                float fill = Mathf.Clamp01((_progress.Stars - from) / (float)(next - from));
                GameHud.Box(new Rect(x + boxW - 430, line + 20, 400, 16), new Color(1f, 1f, 1f, 0.2f));
                GameHud.Box(new Rect(x + boxW - 430, line + 20, 400 * fill, 16), Sun);
            }
            if (_progress.Unlocked > 1)
                GameHud.Text(new Rect(x + 30, line + 50, boxW - 60, 44), FlyStrings.Outfit(_progress.Outfit, _progress.Unlocked), GameHud.Small,
                             TextAnchor.MiddleLeft, Sky);
        }

        /// <summary>The first card waiting (a new enemy, a mission done), sliding up from the bottom.</summary>
        void DrawCard(float w)
        {
            if (_cards.Count == 0) return;
            var c = _cards[0];
            float slide = Mathf.Clamp01(c.Age * 5f) * Mathf.Clamp01((3.2f - c.Age) * 5f);
            const float cardW = 900f, cardH = 130f;
            var rect = new Rect((w - cardW) / 2f, 1080f - (cardH + 30f) * slide, cardW, cardH);
            GameHud.Box(rect, new Color(0.05f, 0.06f, 0.16f, 0.82f));
            GameHud.Box(new Rect(rect.x, rect.y, 12f, cardH), c.Color);
            GameHud.Text(new Rect(rect.x + 40, rect.y + 8, cardW - 60, 64), c.Title, GameHud.Medium, TextAnchor.MiddleLeft, c.Color);
            GameHud.Text(new Rect(rect.x + 40, rect.y + 70, cardW - 60, 50), c.Text, GameHud.Small, TextAnchor.MiddleLeft);
        }

        /// <summary>
        /// Coming to a crossroads: an arrow beside the hero for each street that turns off, growing as it nears; the one the
        /// hero would take (it is on that side) gold and pulsing. Where the street ends, "turn left or right!".
        /// </summary>
        void DrawTurnArrows(float w)
        {
            if (Run == null || Run.Over || _route.Turning) return;
            var next = _route.Next;
            if (next.Distance > ArrowsFrom || next.Distance < _route.TurnRadius) return;
            var choice = _route.ChoiceAt(Run.Hero.x / Run.HalfWidth);
            float near = 1f - Mathf.Clamp01((next.Distance - _route.TurnRadius) / (ArrowsFrom - _route.TurnRadius));
            float size = Mathf.Lerp(110f, 180f, near);
            void Arrow(bool right, bool chosen)
            {
                float s = size * (chosen ? 1f + 0.08f * Mathf.Sin(Time.time * 12f) : 1f);
                var rect = new Rect(right ? w * 0.5f + 300f : w * 0.5f - 300f - s, 600f - s * 0.5f, s, s);
                GUI.color = chosen ? Sun : new Color(1f, 1f, 1f, 0.6f);
                if (right) GUI.DrawTexture(rect, _arrow);
                else GUI.DrawTextureWithTexCoords(rect, _arrow, new Rect(1f, 0f, -1f, 1f));   // mirrored: pointing left
            }
            if (next.Left) Arrow(false, choice == TurnChoice.Left);
            if (next.Right) Arrow(true, choice == TurnChoice.Right);
            GUI.color = Color.white;
            if (next.MustTurn) GameHud.Text(new Rect(0, 240, w, 70), FlyStrings.TurnHint, GameHud.Medium, TextAnchor.MiddleCenter, Sun);
        }

        /// <summary>What to tell the player right now, if anything.</summary>
        string Hint()
        {
            if (Run == null || Run.Over || Run.Phase is FlyPhase.Cleared or FlyPhase.ClashIntro or FlyPhase.Clash) return null;
            if (Run.InReach(true) != null || Run.InReach(false) != null) return Run.Phase == FlyPhase.Flight ? null : FlyStrings.PunchIt;
            foreach (var t in Run.Things)
            {
                if (t.Kind == ThingKind.Robot && t.State == ThingState.Lunge) return FlyStrings.Dodge;
                if (t.Kind == ThingKind.Fireball && t.Small && t.State == ThingState.Approach) return FlyStrings.PunchSparks;
            }
            return Run.Phase == FlyPhase.Boss && Time.time < _hintFireballsUntil ? FlyStrings.PunchFireballs : null;
        }

        /// <summary>
        /// Flying: the way to the rush. The rush: a block per robot, lit while still to beat. The clash: the tug of war,
        /// the hero's gold against the champion's coral, both punch rates under it. The boss: the fireballs still to return.
        /// </summary>
        void DrawProgress(float w)
        {
            const float barW = 560f, barH = 26f;
            float x = (w - barW) / 2f, y = 72f;
            var dim = new Color(0f, 0f, 0f, 0.55f);
            switch (Run.Phase)
            {
                case FlyPhase.BossArrives:
                case FlyPhase.Boss:
                {
                    GameHud.Text(new Rect(x, 18, barW, 50), FlyStrings.Boss, GameHud.Small, TextAnchor.MiddleCenter);
                    GameHud.Box(new Rect(x - 4, y - 4, barW + 8, barH + 8), dim);
                    float fill = Run.Boss.MaxHp > 0 ? Mathf.Clamp01((float)Run.Boss.Hp / Run.Boss.MaxHp) : 0f;
                    GameHud.Box(new Rect(x, y, barW * fill, barH), Coral);
                    for (int i = 1; i < Run.Boss.MaxHp; i++)   // one notch per fireball it takes
                        GameHud.Box(new Rect(x + barW * i / Run.Boss.MaxHp - 1f, y, 2f, barH), new Color(0f, 0f, 0f, 0.35f));
                    break;
                }
                case FlyPhase.ClashIntro:
                case FlyPhase.Clash:
                case FlyPhase.Cleared when Run.Clash != null:
                    DrawClash(w);
                    break;
                case FlyPhase.Stop:
                case FlyPhase.Rush:
                {
                    GameHud.Text(new Rect(x, 18, barW, 50), FlyStrings.Robots, GameHud.Small, TextAnchor.MiddleCenter);
                    int total = Mathf.Max(1, Run.RushSize), left = Mathf.Min(Run.RobotsLeft, total);
                    float gap = total > 24 ? 3f : 6f;
                    float block = (barW - gap * (total - 1)) / total;
                    for (int i = 0; i < total; i++)
                        GameHud.Box(new Rect(x + i * (block + gap), y, block, barH), i < left ? Coral : new Color(1f, 1f, 1f, 0.18f));
                    break;
                }
                default:
                {
                    GameHud.Text(new Rect(x, 18, barW, 50), FlyStrings.District(Run.District), GameHud.Small, TextAnchor.MiddleCenter);
                    GameHud.Box(new Rect(x - 4, y - 4, barW + 8, barH + 8), dim);
                    GameHud.Box(new Rect(x, y, barW * Run.FlightProgress, barH), Sky);
                    GameHud.Box(new Rect(x + barW - 10f, y - 6f, 10f, barH + 12f), Run.Settings.Boss ? Coral : Sun);   // what's at the end
                    break;
                }
            }
        }

        /// <summary>The clash's tug of war: a long bar, the hero's side gold, the champion's coral, the meeting point a
        /// bright marker that the faster puncher pushes to the other end.</summary>
        void DrawClash(float w)
        {
            var c = Run.Clash;
            const float barW = 980f, barH = 54f;
            float x = (w - barW) / 2f, y = 64f;
            float meet = x + barW * (0.5f + 0.5f * Mathf.Clamp(c.Bar, -1f, 1f));
            GameHud.Box(new Rect(x - 6, y - 6, barW + 12, barH + 12), new Color(0f, 0f, 0f, 0.6f));
            GameHud.Box(new Rect(x, y, meet - x, barH), Sun);
            GameHud.Box(new Rect(meet, y, x + barW - meet, barH), Coral);
            float pulse = 1f + 0.25f * Mathf.Sin(Time.time * 20f);
            GameHud.Box(new Rect(meet - 7f * pulse, y - 14f, 14f * pulse, barH + 28f), Color.white);
            GameHud.Text(new Rect(x, y + barH + 8, 400, 46), $"{FlyStrings.You}  {HeroPunchRate:0.0}/s", GameHud.Small,
                         TextAnchor.MiddleLeft, Sun);
            GameHud.Text(new Rect(x + barW - 400, y + barH + 8, 400, 46),
                         $"{(c.WithBoss ? FlyStrings.Boss : FlyStrings.Champion)}  {c.CurrentRate:0.0}/s", GameHud.Small, TextAnchor.MiddleRight, Coral);
            if (Run.Phase == FlyPhase.Clash && c.Flurry > 0f)
                GameHud.Text(new Rect(0, y + barH + 50, w, 60), FlyStrings.Flurry, GameHud.Medium, TextAnchor.MiddleCenter, Coral);
        }
    }
}
