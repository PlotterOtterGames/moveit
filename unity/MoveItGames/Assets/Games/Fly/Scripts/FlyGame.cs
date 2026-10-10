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
    /// "Fluturo!": fly over the city as a superhero and punch the robots. The rules are in <see cref="FlyRun"/> (tested
    /// without a scene); this draws them and turns the player into steering and punches:
    /// <list type="bullet">
    /// <item><b>fly</b>: step left or right (the hero follows the body, no lanes), duck to fly low, jump to fly up;</item>
    /// <item><b>punch</b>: either fist, any direction (<see cref="PunchDetector"/>); it smashes the most urgent robot or
    /// fireball within reach (a gold ring shows what a punch would hit).</item>
    /// </list>
    /// Robots fly in, hover in front of the hero (their eye turns red), then charge: punch them, or move away while they
    /// charge. After each flight the giant robot comes and throws fireballs: punch them back. Three bosses beaten = won,
    /// no hearts left = lost. Without a camera: arrows fly, space punches.
    /// </summary>
    public sealed class FlyGame : MonoBehaviour
    {
        public enum Phase { Waiting, Countdown, Playing, Results }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] FlyWorld world;
        [SerializeField, Tooltip("Tundu/Toon, white, flat (no world bend): the hero, robots, stars, hearts, confetti.")]
        Material toonMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: sparkles and sparks.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx, additive, with the soft dot texture: the fireballs' glow.")] Material glowMaterial;
        [SerializeField, Tooltip("Tundu/Fx drawn on top of everything: the reach rings.")] Material markerMaterial;

        [Header("Play")]
        [SerializeField] float countdownSeconds = 3f;
        [SerializeField, Tooltip("A jump lifts the hero to the top of its window for this long (s).")] float jumpLift = 0.7f;

        const string BestKey = "fly.best";
        static readonly Color Sun = new Color(1f, 0.79f, 0.24f), Coral = new Color(1f, 0.35f, 0.31f), Sky = new Color(0.19f, 0.78f, 0.96f);
        static readonly Color Grape = new Color(0.48f, 0.38f, 1f), Leaf = new Color(0.44f, 0.84f, 0.42f);
        static readonly Color[] RobotPaints = { new Color(0.36f, 0.4f, 0.55f), new Color(0.3f, 0.55f, 0.62f), new Color(0.55f, 0.36f, 0.6f) };
        static readonly Color[] Confetti = { Sun, Coral, Sky, Grape, Leaf };

        public Phase Current { get; private set; }
        public FlyRun Run { get; private set; }
        public int Best { get; private set; }
        /// <summary>Nothing spawns by itself (tests, a practice mode later): use <see cref="Run"/>'s Spawn methods.</summary>
        public bool Practice { get; set; }
        /// <summary>Every punch seen (tests, debugging): the hand.</summary>
        public event Action<bool> Punched;
        /// <summary>Every rule event, as it happens (tests, debugging).</summary>
        public event Action<FlyEvent> RunEvent;
        public Hero Hero => _hero;

        BodyInput _input;
        Hero _hero;
        RobotView _boss;
        readonly PunchDetector _punches = new PunchDetector();
        readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        readonly List<Bit> _bits = new List<Bit>();
        readonly List<Popup> _popups = new List<Popup>();
        float _phaseTime, _lift, _shake, _endIn = -1f, _toastUntil, _hintFireballsUntil;
        double _lastSample = -1;
        bool _paused, _newBest, _pendingPunch, _pendingRight, _nextKeyRight = true;
        string _toast, _toastSub;
        Mesh _ring, _star;
        Texture2D _heart;
        Material _ringMaterial;
        MaterialPropertyBlock _tint;
        Vector3 _camVelocity;
        int _starCombo;
        float _starComboUntil;

        sealed class View
        {
            public Thing Thing;
            public Transform T;
            public RobotView Robot;
            public Transform Ring;
            public bool Smashed;
            public float Spin;
        }

        sealed class Bit { public Transform T; public Vector3 Velocity, Spin, Scale; public float Age, Life; public bool Gravity; }
        sealed class Popup { public Vector3 World; public string Text; public Color Color; public float Age; }

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            Best = PlayerPrefs.GetInt(BestKey, 0);
            _tint = new MaterialPropertyBlock();
            _ring = FlyArt.Ring(0.42f);
            _star = FlyArt.Star();
            _heart = FlyArt.Heart();
            _ringMaterial = new Material(markerMaterial);
            _hero = Hero.Create(toonMaterial);
            _hero.transform.position = World(Vector3.zero);
            Shader.SetGlobalFloat("_TunduCurve", 0.0008f);
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("fly");
        }

        void OnDestroy()
        {
            Shader.SetGlobalFloat("_TunduCurve", 0f);   // other scenes stay flat
            if (_ring != null) Destroy(_ring);
            if (_star != null) Destroy(_star);
            if (_heart != null) Destroy(_heart);
            if (_ringMaterial != null) Destroy(_ringMaterial);
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
                    if (_phaseTime > 1.5f && StartPressed()) SetPhase(Phase.Countdown);
                    break;
            }
            _pendingPunch = false;

            // the city drifts by slowly while waiting, flies past while playing, stops while paused
            if (world != null) world.Speed = Current != Phase.Playing ? 6f : _paused || Run == null ? 0f : Run.Speed;
            UpdateViews(dt);
            UpdateBits(dt);
            UpdateCamera(dt);
        }

        bool UsingKeys => _input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService;

        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            if (phase == Phase.Countdown) NewRound();
            Debug.Log(phase == Phase.Results && Run != null
                ? $"[Fly] Results {(Run.Phase == FlyPhase.Won ? "WON" : "lost")} score={Run.Score} level={Run.Level} robots={Run.RobotsSmashed} " +
                  $"stars={Run.StarsCollected} fireballs={Run.FireballsReturned} hitsTaken={Run.HitsTaken}"
                : $"[Fly] {phase}");
        }

        void NewRound()
        {
            ClearViews();
            Run = new FlyRun(Random.Range(0, int.MaxValue)) { Scripted = Practice };
            Run.Start();
            _endIn = -1f;
            _newBest = false;
            _lift = 0f;
            _punches.Reset();
            GameAudio.PlayMusic("fly");
            GameAudio.Sfx("count", vary: 0f);
        }

        void EndRound()
        {
            if (Run.Score > Best)
            {
                Best = Run.Score;
                _newBest = true;
                PlayerPrefs.SetInt(BestKey, Best);
                PlayerPrefs.Save();
            }
            SetPhase(Phase.Results);
        }

        // ---------------------------------------------------------------- input

        /// <summary>Punches come from every new camera sample (the detector needs them all, in order).</summary>
        void ReadPunches()
        {
            var body = _input != null && _input.Gestures != null ? _input.Gestures.Body : null;
            if (body == null || body.SampleTime == _lastSample) return;
            _lastSample = body.SampleTime;
            float torso = body.HasWorld ? body.WorldTorsoLength : body.FastTorsoLength;
            if (_punches.Update(body.SampleTime, ArmSample.From(body, false), ArmSample.From(body, true), torso, body.HasWorld, out var info))
                QueuePunch(info.RightHand);
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

        void QueuePunch(bool right)
        {
            _pendingPunch = true;
            _pendingRight = right;
            if (Current != Phase.Playing) return;
            _hero.Punch(right);
            GameAudio.Sfx("fly_punch", 0.6f, 1f + Random.Range(-0.08f, 0.08f));
            Punched?.Invoke(right);
            Debug.Log($"[Fly] Punch {(right ? "right" : "left")}");
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
            Run.Step(dt, Steering(dt), _pendingPunch, _pendingRight);
            Handle(Run.Events);
            if (Run.Over && _endIn < 0f) _endIn = Run.Phase == FlyPhase.Won ? 3.5f : 2.2f;
        }

        void Handle(IReadOnlyList<FlyEvent> events)
        {
            foreach (var e in events)
            {
                RunEvent?.Invoke(e);
                var at = World(e.Position);
                switch (e.Kind)
                {
                    case FlyEventKind.LevelStarted:
                        Toast(FlyStrings.Level(Run.Level), Run.Level == 1 ? FlyStrings.SaveTheCity : null);
                        if (Run.Level > 1) GameAudio.Sfx("go", vary: 0f);
                        Debug.Log($"[Fly] Level {Run.Level}");
                        break;
                    case FlyEventKind.BossAppears:
                        Toast(FlyStrings.BossComing, FlyStrings.PunchFireballs);
                        _hintFireballsUntil = Time.time + 8f;
                        GameAudio.Sfx("fly_boss");
                        Debug.Log($"[Fly] Boss (hp {Run.Boss.Hp})");
                        break;
                    case FlyEventKind.RobotCharges:
                        GameAudio.Sfx("fly_charge", 0.7f);
                        break;
                    case FlyEventKind.RobotSmashed:
                        GameAudio.Sfx("fly_smash");
                        Sparks(at, 14, Sun);
                        Scrap(at, 10);
                        AddPopup(at + Vector3.up * 0.6f, $"{FlyStrings.Smash} +{e.Points}", Color.white);
                        _shake = Mathf.Max(_shake, 0.12f);
                        Debug.Log($"[Fly] Robot smashed, score {Run.Score}");
                        break;
                    case FlyEventKind.RobotHitHero:
                    case FlyEventKind.FireballHitHero:
                        GameAudio.Sfx("fly_hurt");
                        _hero.Hurt();
                        _shake = 0.4f;
                        Sparks(_hero.transform.position + Vector3.forward * 0.6f, 12, Coral);
                        AddPopup(_hero.transform.position + Vector3.up * 0.8f, FlyStrings.Ouch, Coral);
                        if (e.Kind == FlyEventKind.FireballHitHero) _hintFireballsUntil = Time.time + 4f;
                        Debug.Log($"[Fly] Hurt by a {(e.Kind == FlyEventKind.RobotHitHero ? "robot" : "fireball")}, hearts {Run.Hearts}");
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
                        BurstConfetti(at, 60);
                        AddPopup(at + Vector3.up * 2f, $"+{e.Points}", Sun);
                        _hero.Cheer();
                        if (Run.Phase != FlyPhase.Won)
                        {
                            GameAudio.Jingle("jingle_win", 0.3f);
                            Toast(FlyStrings.LevelClear, FlyStrings.NextLevel);
                        }
                        Debug.Log($"[Fly] Boss defeated, level {Run.Level}, score {Run.Score}");
                        break;
                    case FlyEventKind.Won:
                        GameAudio.StopMusic(0.4f);
                        GameAudio.Jingle("jingle_win");
                        Toast(FlyStrings.YouWin, null);
                        for (int i = 0; i < 4; i++) BurstConfetti(_hero.transform.position + new Vector3(Random.Range(-4f, 4f), 3f, Random.Range(3f, 8f)), 30);
                        break;
                    case FlyEventKind.Lost:
                        GameAudio.StopMusic(0.4f);
                        GameAudio.Jingle("jingle_gameover");
                        break;
                }
            }
        }

        // ---------------------------------------------------------------- drawing the run

        /// <summary>The flight frame (x right, y up around the flying height, z ahead) in the scene.</summary>
        static Vector3 World(Vector3 p) => new Vector3(p.x, FlyWorld.FlyHeight + p.y, p.z);

        void UpdateViews(float dt)
        {
            if (Run == null)
            {
                _hero.transform.position = World(new Vector3(0f, Mathf.Sin(Time.time) * 0.1f, 0f));
                return;
            }
            _hero.transform.position = World(new Vector3(Run.Hero.x, Run.Hero.y, 0f));

            // the boss
            if (Run.Boss != null && Run.Boss.Hp > 0 && Run.Phase is FlyPhase.BossArrives or FlyPhase.Boss)
            {
                if (_boss == null)
                {
                    _boss = RobotView.Create(toonMaterial, new Color(0.3f, 0.28f, 0.45f), boss: true);
                    _boss.transform.localScale = Vector3.one * 4.2f;
                }
                _boss.transform.position = World(Run.Boss.Position);
            }
            else if (_boss != null)
            {
                Destroy(_boss.gameObject);
                _boss = null;
            }

            // things: create views for new ones, move them, remove finished ones
            var alive = new HashSet<int>();
            foreach (var t in Run.Things)
            {
                alive.Add(t.Id);
                if (!_views.TryGetValue(t.Id, out var v)) _views[t.Id] = v = CreateView(t);
                Place(v, dt);
            }
            var gone = new List<int>();
            foreach (var pair in _views)
                if (!alive.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (int id in gone)
            {
                var v = _views[id];
                if (v.Thing.Kind == ThingKind.Fireball && !v.Smashed) Sparks(v.T.position, 8, Coral);   // fizzles out
                DestroyView(v);
                _views.Remove(id);
            }
        }

        View CreateView(Thing t)
        {
            var v = new View { Thing = t, Spin = Random.Range(-1f, 1f) };
            switch (t.Kind)
            {
                case ThingKind.Robot:
                    v.Robot = RobotView.Create(toonMaterial, RobotPaints[t.Id % RobotPaints.Length]);
                    v.T = v.Robot.transform;
                    v.Ring = MakeRing();
                    break;
                case ThingKind.Star:
                    v.T = MeshThing("Star", _star, Sun, 0.75f);
                    break;
                case ThingKind.Heart:
                    v.T = HeartThing();
                    break;
                default:
                    v.T = FireballThing();
                    v.Ring = MakeRing();
                    break;
            }
            v.T.position = World(t.Position);
            return v;
        }

        void Place(View v, float dt)
        {
            var t = v.Thing;
            v.T.position = World(t.Position);
            switch (t.Kind)
            {
                case ThingKind.Robot:
                    float warn = t.State == ThingState.Lunge ? 1f
                        : t.State == ThingState.Hover ? Mathf.Clamp01((t.StateTime - (Run.Settings.HoverSeconds - 0.8f)) / 0.8f) : 0f;
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
                        foreach (var r in v.T.GetComponentsInChildren<Renderer>()) Tint(r, Sun);
                    }
                    float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 18f + v.Spin * 5f);
                    v.T.localScale = Vector3.one * pulse;
                    v.T.rotation = cam.transform.rotation;
                    if (Random.value < dt * 20f) Trail(v.T.position, v.Smashed ? Sun : Coral);
                    break;
            }
            UpdateRing(v);
        }

        /// <summary>A ring around a robot or fireball: gold and pulsing when a punch would hit it, faint white for a
        /// hovering robot that is out of reach (fly closer), hidden otherwise.</summary>
        void UpdateRing(View v)
        {
            if (v.Ring == null) return;
            var t = v.Thing;
            bool inReach = Current == Phase.Playing && Run.CanPunch(t) && t.Position.z <= Run.ReachFar;
            bool target = inReach && Run.InReach(true) == t || inReach && Run.InReach(false) == t;
            bool waiting = t.Kind == ThingKind.Robot && t.State == ThingState.Hover && !inReach;
            v.Ring.gameObject.SetActive(target || waiting);
            if (!v.Ring.gameObject.activeSelf) return;
            float size = (t.Kind == ThingKind.Robot ? 1.5f : 1.1f) * (target ? 1f + 0.08f * Mathf.Sin(Time.time * 14f) : 1.1f);
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

        Transform MeshThing(string name, Mesh mesh, Color color, float size)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = toonMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            Tint(r, color);
            go.transform.localScale = Vector3.one * size;
            var root = new GameObject(name + " root").transform;
            go.transform.SetParent(root, false);
            return root;
        }

        /// <summary>The classic heart from two balls and a tilted block.</summary>
        Transform HeartThing()
        {
            var root = new GameObject("Heart").transform;
            void Ball(PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler)
            {
                var go = GameObject.CreatePrimitive(type);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(euler);
                go.transform.localScale = scale;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = toonMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                Tint(r, Coral);
            }
            Ball(PrimitiveType.Sphere, new Vector3(-0.17f, 0.12f, 0f), new Vector3(0.42f, 0.42f, 0.3f), Vector3.zero);
            Ball(PrimitiveType.Sphere, new Vector3(0.17f, 0.12f, 0f), new Vector3(0.42f, 0.42f, 0.3f), Vector3.zero);
            Ball(PrimitiveType.Cube, new Vector3(0f, -0.08f, 0f), new Vector3(0.42f, 0.42f, 0.28f), new Vector3(0f, 0f, 45f));
            return root;
        }

        /// <summary>A fireball: a hot core and a big soft glow (always facing the camera).</summary>
        Transform FireballThing()
        {
            var root = new GameObject("Fireball").transform;
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(core.GetComponent<Collider>());
            core.transform.SetParent(root, false);
            core.transform.localScale = Vector3.one * 0.55f;
            var cr = core.GetComponent<Renderer>();
            cr.sharedMaterial = toonMaterial;
            cr.shadowCastingMode = ShadowCastingMode.Off;
            Tint(cr, new Color(1f, 0.6f, 0.3f));
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(root, false);
            glow.transform.localScale = Vector3.one * 1.5f;
            var gr = glow.GetComponent<Renderer>();
            gr.sharedMaterial = glowMaterial;
            gr.shadowCastingMode = ShadowCastingMode.Off;
            Tint(gr, Coral);
            return root;
        }

        void Tint(Renderer r, Color c)
        {
            _tint.SetColor("_BaseColor", c);
            r.SetPropertyBlock(_tint);
        }

        void DestroyView(View v)
        {
            if (v.Ring != null) Destroy(v.Ring.gameObject);
            if (v.T != null) Destroy(v.T.gameObject);
        }

        void ClearViews()
        {
            foreach (var v in _views.Values) DestroyView(v);
            _views.Clear();
            if (_boss != null) Destroy(_boss.gameObject);
            _boss = null;
        }

        // ---------------------------------------------------------------- effects

        void Sparks(Vector3 at, int count, Color color)
        {
            for (int i = 0; i < count; i++)
            {
                var quad = Quad(sparkMaterial, color);
                quad.position = at;
                float size = Random.Range(0.12f, 0.26f);
                _bits.Add(new Bit
                {
                    T = quad, Scale = Vector3.one * size, Life = Random.Range(0.3f, 0.55f),
                    Velocity = Random.onUnitSphere * Random.Range(3f, 6f), Spin = new Vector3(0f, 0f, Random.Range(-720f, 720f)),
                });
            }
        }

        void Trail(Vector3 at, Color color)
        {
            var quad = Quad(glowMaterial, color);
            quad.position = at + Random.insideUnitSphere * 0.15f;
            _bits.Add(new Bit { T = quad, Scale = Vector3.one * Random.Range(0.3f, 0.5f), Life = 0.35f, Velocity = Vector3.back * Run.Speed * 0.2f });
        }

        Transform Quad(Material material, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            Tint(r, color);
            return quad.transform;
        }

        /// <summary>Bits of metal flying off a smashed robot.</summary>
        void Scrap(Vector3 at, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                var r = cube.GetComponent<Renderer>();
                r.sharedMaterial = toonMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                Tint(r, i % 3 == 0 ? Sun : new Color(0.62f, 0.66f, 0.78f));
                cube.transform.position = at + Random.insideUnitSphere * 0.2f;
                var scale = Vector3.one * Random.Range(0.08f, 0.16f);
                cube.transform.localScale = scale;
                _bits.Add(new Bit
                {
                    T = cube.transform, Scale = scale, Life = Random.Range(0.6f, 1f), Gravity = true,
                    Velocity = new Vector3(Random.Range(-3f, 3f), Random.Range(1f, 5f), Random.Range(2f, 8f)), Spin = Random.insideUnitSphere * 720f,
                });
            }
        }

        void BurstConfetti(Vector3 at, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                var r = cube.GetComponent<Renderer>();
                r.sharedMaterial = toonMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                Tint(r, Confetti[Random.Range(0, Confetti.Length)]);
                cube.transform.position = at + Random.insideUnitSphere * 0.5f;
                var scale = new Vector3(Random.Range(0.12f, 0.22f), Random.Range(0.12f, 0.22f), 0.02f);
                cube.transform.localScale = scale;
                _bits.Add(new Bit
                {
                    T = cube.transform, Scale = scale, Life = Random.Range(1.2f, 1.8f), Gravity = true,
                    Velocity = new Vector3(Random.Range(-5f, 5f), Random.Range(3f, 8f), Random.Range(-3f, 3f)), Spin = Random.insideUnitSphere * 600f,
                });
            }
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
                if (b.Gravity) b.Velocity += Vector3.down * 9f * dt;
                else b.Velocity *= Mathf.Exp(-4f * dt);
                b.T.position += b.Velocity * dt;
                if (b.Gravity) b.T.Rotate(b.Spin * dt, Space.World);
                else b.T.rotation = camRot * Quaternion.Euler(0f, 0f, b.Spin.z * b.Age);
                b.T.localScale = b.Scale * Mathf.Clamp01((b.Life - b.Age) / (b.Life * 0.4f));
            }
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].Age += dt;
                _popups[i].World += Vector3.up * (0.8f * dt);
                if (_popups[i].Age > 1f) _popups.RemoveAt(i);
            }
        }

        /// <summary>Behind and above the hero, following it softly (and a little less than it moves, so the hero
        /// visibly flies across the screen), leaning into turns, shaking when hit.</summary>
        void UpdateCamera(float dt)
        {
            var hero = _hero.transform.position;
            var want = new Vector3(hero.x * 0.55f, FlyWorld.FlyHeight + (hero.y - FlyWorld.FlyHeight) * 0.45f + 1.6f, -5f);
            var pos = Vector3.SmoothDamp(cam.transform.position, want, ref _camVelocity, 0.18f, Mathf.Infinity, dt);
            _shake = Mathf.Max(0f, _shake - dt);
            var look = new Vector3(hero.x * 0.75f, FlyWorld.FlyHeight + (hero.y - FlyWorld.FlyHeight) * 0.6f + 0.2f, 8f);
            cam.transform.position = pos + (Vector3)(Random.insideUnitCircle * (0.25f * _shake));
            cam.transform.rotation = Quaternion.LookRotation(look - pos) * Quaternion.Euler(0f, 0f, -_camVelocity.x * 1.5f);
        }

        void AddPopup(Vector3 world, string text, Color color) => _popups.Add(new Popup { World = world, Text = text, Color = color });

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
                GameHud.Text(new Rect(g.x - 250, g.y - 35, 500, 70), p.Text, GameHud.Medium, TextAnchor.MiddleCenter, c);
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
                GameHud.Text(new Rect(w - 630, 20, 600, 60), $"{CommonStrings.Best}: {Best}", GameHud.Medium, TextAnchor.UpperRight);
                if (Current == Phase.Playing) DrawProgress(w);
            }

            switch (Current)
            {
                case Phase.Waiting:
                    PlayerGate.DrawWaiting(w, _input, FlyStrings.Title, FlyStrings.KeyboardHelp);
                    GameHud.Text(new Rect(0, 620, w, 60), FlyStrings.HowTo, GameHud.Small);
                    GameHud.Text(new Rect(0, 680, w, 50), FlyStrings.JumpHint, GameHud.Small, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
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
                    }
                    break;
                case Phase.Results:
                    bool won = Run != null && Run.Phase == FlyPhase.Won;
                    GameHud.Message(w, won ? FlyStrings.YouWin : CommonStrings.GameOver,
                        $"{CommonStrings.Score}: {Run?.Score}    {FlyStrings.Robots}: {Run?.RobotsSmashed}    {FlyStrings.Stars}: {Run?.StarsCollected}" +
                        (_newBest ? $"    {CommonStrings.NewBest}" : ""), won ? Sun : (Color?)null);
                    if (_phaseTime > 1.5f)
                        GameHud.Text(new Rect(0, 540, w, 80),
                            PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter : CommonStrings.PlayAgain, GameHud.Small);
                    break;
            }

            if (_toast != null && Time.time < _toastUntil && Current == Phase.Playing)
            {
                GameHud.Text(new Rect(0, 300, w, 120), _toast, _toast.Length > 22 ? GameHud.Medium : GameHud.Big, TextAnchor.MiddleCenter, Sun);
                if (_toastSub != null) GameHud.Text(new Rect(0, 420, w, 70), _toastSub, GameHud.Small);
            }
        }

        /// <summary>What to tell the player right now, if anything.</summary>
        string Hint()
        {
            if (Run == null || Run.Over || Run.Phase == FlyPhase.LevelClear) return null;
            if (Run.InReach(true) != null || Run.InReach(false) != null) return FlyStrings.PunchIt;
            foreach (var t in Run.Things)
            {
                if (t.Kind == ThingKind.Robot && t.State == ThingState.Hover) return FlyStrings.GetCloser;
                if (t.Kind == ThingKind.Robot && t.State == ThingState.Lunge) return FlyStrings.Dodge;
            }
            return Run.Phase == FlyPhase.Boss && Time.time < _hintFireballsUntil ? FlyStrings.PunchFireballs : null;
        }

        /// <summary>The level and how far to the boss, or the boss's health.</summary>
        void DrawProgress(float w)
        {
            const float barW = 560f, barH = 26f;
            float x = (w - barW) / 2f, y = 72f;
            bool boss = Run.Boss != null && Run.Phase is FlyPhase.BossArrives or FlyPhase.Boss;
            GameHud.Text(new Rect(x, 18, barW, 50), boss ? FlyStrings.Boss : FlyStrings.Level(Run.Level), GameHud.Small, TextAnchor.MiddleCenter);
            GameHud.Box(new Rect(x - 4, y - 4, barW + 8, barH + 8), new Color(0f, 0f, 0f, 0.55f));
            if (boss)
            {
                float fill = Run.Boss.MaxHp > 0 ? Mathf.Clamp01((float)Run.Boss.Hp / Run.Boss.MaxHp) : 0f;
                GameHud.Box(new Rect(x, y, barW * fill, barH), Coral);
                for (int i = 1; i < Run.Boss.MaxHp; i++)   // one notch per fireball it takes
                    GameHud.Box(new Rect(x + barW * i / Run.Boss.MaxHp - 1f, y, 2f, barH), new Color(0f, 0f, 0f, 0.35f));
            }
            else
            {
                GameHud.Box(new Rect(x, y, barW * Run.FlightProgress, barH), Sky);
                // a little marker for the boss at the end
                GameHud.Box(new Rect(x + barW - 10f, y - 6f, 10f, barH + 12f), Coral);
            }
        }
    }
}
