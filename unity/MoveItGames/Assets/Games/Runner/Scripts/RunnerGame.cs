using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games.Runner
{
    /// <summary>
    /// Vrapo!: Dino runs through a prehistoric valley. Step left or right to change lanes, jump over logs and stones, duck
    /// under bridges and a sleeping brontosaurus's neck, step round boulders and sleeping triceratops, collect eggs (a
    /// golden one now and then). The runner stays at z = 0 and the world comes toward the camera.
    ///
    /// The console polish of 2026-11-18 (the user: "a complete polish ... graphics, animation, music, effects, models ...
    /// a game ready for production for a console ... see other games critically acclaimed"), after Subway Surfers (clear
    /// lanes, families of obstacles you read by their shape, coins showing the way), Crash Bandicoot 4 (a lush valley
    /// packed with detail, a hero with lots of animation), Alto's Adventure (the time of day changing as you go) and
    /// Kinect Adventures' Reflex Ridge (its reviews: "squat, jump, sidestep, repeat" got tedious, so the course is
    /// designed in patterns and varied): a title at Dino's camp, a countdown swinging the camera round behind him, the run
    /// in the valley (RunnerWorld) on a designed course (RunnerTrack), juice for everything (RunnerFx), a crash with a
    /// hit-stop and his dizzy face, the results and a new record's dance. The game's rules are the old ones: the same
    /// lanes, speeds, jump, slide and hitboxes.
    /// </summary>
    public sealed partial class RunnerGame : MonoBehaviour
    {
        enum State { Waiting, Countdown, Running, Crashed, GameOver }

        sealed class Item
        {
            public Spawn Spawn;
            public Piece Piece;
            public Transform Root;
            public Transform Model;
            public float Depth;
            public bool Done;
        }

        [Header("Scene")]
        [SerializeField] RunnerPlayer player;
        [SerializeField] Camera cam;
        [SerializeField] Light sun;
        [SerializeField] RunnerFx fx;
        [SerializeField] RunnerArt art = new RunnerArt();
        [SerializeField] Texture2D hudSheet;
        [SerializeField] TextAsset hudJson;

        [Header("Speed (m/s)")]
        [SerializeField] float startSpeed = 9f;
        [SerializeField] float maxSpeed = 20f;
        [SerializeField, Tooltip("Speed gained per second of running.")]
        float acceleration = 0.12f;

        [Header("Help")]
        [SerializeField, Tooltip("Show the moves over the obstacles during the first seconds of a run.")]
        float hintSeconds = 40f;

        const float LowBarrierHeight = 0.7f;
        const float HighBarrierBottom = 1.15f;
        const float CountdownSeconds = 3f;
        const float CrashSeconds = 1.5f;
        const float LaneWidth = RunnerWorld.LaneWidth;
        const float TitleTime = 4f;       // into a time of day: where a session's first title starts in it
        const string BestKey = "runner.best";
        const int MilestoneEvery = 500;

        State _state;
        float _stateTime;
        float _speed;
        float _distance;
        float _runTime;
        int _eggs, _golden;
        int _eggStreak;
        float _lastEggTime = -10f;
        int _best;
        bool _newBest, _passedBest;
        bool _paused;
        int _milestone;
        float _hitStop;
        float _dayStart;                  // where in the day the run starts: the day goes on from run to run (Alto's)
        float _bounceLeft;

        RunnerTrack _track;
        RunnerWorld _world;
        RunnerCamera _director;
        BodyInput _input;
        readonly List<Spawn> _spawns = new List<Spawn>();
        readonly List<Item> _items = new List<Item>();
        readonly Dictionary<Piece, Stack<Item>> _pool = new Dictionary<Piece, Stack<Item>>();

        int Score => Mathf.FloorToInt(_distance) + _eggs * 10 + _golden * 50;

        /// <summary>Tests: the time of day to hold (seconds from the morning; negative = the game's own).</summary>
        public static float TestTime = -1f;
        /// <summary>Tests: nothing stops the run (screenshots of a long run).</summary>
        public static bool Invincible;
        bool _madeInvincible;             // PerfLab's autoplay made the runner invincible: undone when the scene goes

        /// <summary>Tests: start the countdown as if the player raised their hands.</summary>
        public void TestStart()
        {
            if (_state == State.Waiting || _state == State.GameOver)
            {
                ResetRun();
                SetState(State.Countdown);
            }
        }

        public string Phase => _state.ToString();
        public RunnerPlayer Player => player;
        public float Distance => _distance;
        public int Eggs => _eggs + _golden;
        public bool NewBest => _newBest;

        /// <summary>Lanes switch at 0.55 torso lengths and players step further; jumps need room above the head, ducks the knees seen.</summary>
        static readonly PlayArea Area = new PlayArea(BodyPart.Knees, side: 0.8f, above: 0.3f);

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            PlayerGate.Use(_input, Area);
            _best = PlayerPrefs.GetInt(BestKey, 0);
            player.LaneWidth = LaneWidth;
            if (cam == null) cam = Camera.main;
            _world = RunnerWorld.Create(art, sun);
            _director = new RunnerCamera(cam);
            if (fx != null)
            {
                fx.Setup(player.transform, cam, _world.Crater);
                player.Footstep += at => { if (_state == State.Running) { fx.Footstep(at, _speed); GameAudio.Sfx("step", 0.22f, 1f, 0.12f); } };
                player.Landed += at => { fx.Land(at, _speed); _director.Shake(0.28f); };
                player.TookOff += at => fx.TakeOff(at, _speed);
                player.LaneChanged += (at, dir) => fx.LanePuff(at, dir, _speed);
            }
            SetupHud();
            // the first title in the morning, at golden hour or at sunset (each session its own light); then the day goes on
            _dayStart = TitleTime + new[] { 0, 2, 3 }[Random.Range(0, 3)] * (RunnerWorld.TimeHold + RunnerWorld.TimeBlend);
            ResetRun();
            SetState(State.Waiting);
        }

        void OnDestroy()
        {
            if (_madeInvincible) Invincible = false;   // (a static: the next visit is a real game again)
            Time.timeScale = 1f;
            Shader.SetGlobalFloat("_TunduCurve", 0f);   // other scenes stay flat
            DisposeHud();
        }

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            // a crash's hit-stop: the world freezes for a beat (real time), then goes on
            if (_hitStop > 0f)
            {
                _hitStop -= Time.unscaledDeltaTime;
                Time.timeScale = _hitStop > 0f ? 0.04f : 1f;
            }
            float dt = Time.deltaTime;
            _stateTime += dt;

            switch (_state)
            {
                case State.Waiting:
                    _world.SetTime(TestTime >= 0f ? TestTime : _dayStart);
                    if (PlayerGate.StartPressed(_input)) SetState(State.Countdown);
                    else if (PerfSwitches.Get("autoplay") == "1")
                    {
                        // PerfLab (the box's measurements): an endless run nobody plays, nothing stops it
                        if (!Invincible) _madeInvincible = true;
                        Invincible = true;
                        TestStart();
                    }
                    break;

                case State.Countdown:
                    if (_stateTime < CountdownSeconds && Mathf.FloorToInt(_stateTime) != Mathf.FloorToInt(_stateTime - dt))
                        GameAudio.Sfx("count", vary: 0f);
                    if (_stateTime > 0.9f && player.Current != RunnerPlayer.Mood.Ready) player.Show(RunnerPlayer.Mood.Ready);
                    if (_stateTime >= CountdownSeconds)
                    {
                        _input.Gestures?.Recenter();
                        _input.ResetLane();
                        SetState(State.Running);
                    }
                    break;

                case State.Running:
                    if (_madeInvincible && PerfSwitches.Get("autoplay") != "1")
                    {
                        // the measurement is over: the next obstacle ends the run
                        Invincible = false;
                        _madeInvincible = false;
                    }
                    _paused = PlayerGate.ShouldPause(_input);
                    if (!_paused) Run(dt);
                    break;

                case State.Crashed:
                    // bounced back off what he ran into (the world slides back a little), then sat down dizzy
                    if (_bounceLeft > 0f)
                    {
                        float b = Mathf.Min(_bounceLeft, Mathf.Max(0.01f, _bounceLeft * 7f * dt));
                        _bounceLeft -= b;
                        _distance -= b;
                        _world.Move(-b);
                    }
                    if (_stateTime >= CrashSeconds) SetState(State.GameOver);
                    break;

                case State.GameOver:
                    if (_stateTime > 0.25f && _stateTime - dt <= 0.25f) GameAudio.Jingle(_newBest ? "jingle_win" : "jingle_gameover");
                    if (_newBest && _stateTime > 0.9f && _stateTime - dt <= 0.9f)
                    {
                        player.Show(RunnerPlayer.Mood.Dancing);
                        if (fx != null)
                        {
                            fx.Dizzy(false, Vector3.zero);
                            fx.Confetti(player.transform.position + new Vector3(0f, 2.2f, 0f), 120);
                        }
                    }
                    if (_stateTime > 1.4f && PlayerGate.StartPressed(_input))
                    {
                        ResetRun();
                        SetState(State.Countdown);
                    }
                    break;
            }

            float speedFraction = _state == State.Running ? Mathf.InverseLerp(startSpeed, maxSpeed, _speed) : 0f;
            _director.Tick(dt, new Vector3(player.X, player.FeetY, 0f), speedFraction, _state == State.Running ? LaneLean() : 0f);
            float worldSpeed = _state == State.Running && !_paused ? _speed : 0f;
            if (fx != null && PerfSwitches.Get("fx") != "0")
            {
                fx.Tick(dt, worldSpeed, speedFraction, _world.Night, _world.SunColour);
                if (player.IsSliding && worldSpeed > 0f) fx.Slide(player.transform.position, worldSpeed, dt);
            }
            PlaceItems(dt);
            if (player != null && PerfSwitches.Get("hero") == "0" && player.gameObject.activeSelf) player.gameObject.SetActive(false);
            // Dino's soft shadow, smaller and lighter as he jumps
            float lift = Mathf.Clamp01(player.FeetY / 1.6f);
            _world.Shadow(player.transform.position + new Vector3(0f, 0f, 0.1f), 0.62f * (1f - 0.35f * lift), 0.5f * (1f - 0.45f * lift), 1.35f);
            _world.Draw(cam, dt);
        }

        float LaneLean() => (_input != null ? _input.Lane * LaneWidth : 0f) - player.X;

        void Run(float dt)
        {
            _runTime += dt;
            _speed = Mathf.Min(maxSpeed, startSpeed + acceleration * _runTime);
            float move = _speed * dt;
            _distance += move;
            player.RunSpeed = _speed;
            player.Tick(dt, _input.Lane, _input.JumpTriggered, _input.Crouching);
            _world.Move(move);
            string tod = PerfSwitches.Get("tod");
            _world.SetTime(TestTime >= 0f ? TestTime : tod.Length > 0 && float.TryParse(tod, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float at) ? at : _dayStart + _runTime);

            // the course ahead
            _spawns.Clear();
            _track.Fill(_distance + RunnerWorld.Ahead - 10f, _runTime, _speed, _spawns);
            foreach (var s in _spawns) Spawn(s);

            CheckCollisions();
            Despawn();
            // milestones every 500 m, and the moment the old best is passed
            int m = Mathf.FloorToInt(_distance / MilestoneEvery);
            if (m > _milestone)
            {
                _milestone = m;
                ShowBanner(RunnerStrings.Milestone(m * MilestoneEvery), false);
                GameAudio.Sfx("milestone", 0.9f, 1f, 0f);
            }
            if (!_passedBest && _best > 0 && Score > _best)
            {
                _passedBest = true;
                ShowBanner(RunnerStrings.Beat, true);
                GameAudio.Sfx("record", 1f, 1f, 0f);
                if (fx != null) fx.Confetti(player.transform.position + new Vector3(0f, 2.4f, 3f), 50);
            }
        }

        // ---------- state ----------

        void SetState(State s)
        {
            _state = s;
            _stateTime = 0f;
            switch (s)
            {
                case State.Waiting:
                    player.Show(RunnerPlayer.Mood.Title);
                    _director.Cut(RunnerCamera.Shot.Title, 0f);
                    GameAudio.PlayMusic("runner_title");
                    break;
                case State.Countdown:
                    if (fx != null) fx.Dizzy(false, Vector3.zero);
                    player.Show(RunnerPlayer.Mood.Ready);
                    if (player.Current != RunnerPlayer.Mood.Ready) player.Show(RunnerPlayer.Mood.Ready);
                    _director.Cut(RunnerCamera.Shot.Intro, 2.1f);
                    GameAudio.PlayMusic("runner", 0.5f);
                    GameAudio.Sfx("count", vary: 0f);
                    break;
                case State.Running:
                    _director.Cut(RunnerCamera.Shot.Run, 0.6f);
                    GameAudio.Sfx("go", vary: 0f);
                    ShowBanner(CommonStrings.Go, false, 0.9f);
                    break;
                case State.Crashed:
                    player.Show(RunnerPlayer.Mood.Crashed);
                    _hitStop = 0.13f;
                    _bounceLeft = 1.7f;
                    _director.Shake(1f);
                    _director.Cut(RunnerCamera.Shot.Crash, 1.25f);
                    if (fx != null)
                    {
                        fx.Crash(player.transform.position + new Vector3(0f, 1f, 0.6f));
                        fx.Dizzy(true, player.transform.position + new Vector3(0f, 1.32f, 0.2f));
                    }
                    GameAudio.Sfx("crash", vary: 0f);
                    GameAudio.StopMusic(0.4f);
                    break;
                case State.GameOver:
                    if (Score > _best)
                    {
                        _best = Score;
                        _newBest = true;
                        PlayerPrefs.SetInt(BestKey, _best);
                        PlayerPrefs.Save();
                    }
                    break;
            }
            Debug.Log(s == State.GameOver ? $"[Runner] GameOver score={Score} eggs={_eggs} golden={_golden} distance={_distance:0}" : $"[Runner] {s}");
        }

        void ResetRun()
        {
            // the next run starts in the light the last one ended in (the results screen showed it)
            _dayStart += _runTime;
            foreach (var item in _items) Release(item);
            _items.Clear();
            _speed = startSpeed;
            _distance = 0f;
            _runTime = 0f;
            _eggs = 0;
            _golden = 0;
            _milestone = 0;
            _newBest = false;
            _passedBest = false;
            _paused = false;
            _bounceLeft = 0f;
            _best = PlayerPrefs.GetInt(BestKey, 0);
            _track = new RunnerTrack(Random.Range(1, int.MaxValue));
            player.ResetPlayer();
            player.FaceAway();
            player.SettleScarf();
            _input?.ResetLane();
            ClearHud();
        }

        void Crash() => SetState(State.Crashed);

        // ---------- the course's things ----------

        void Spawn(Spawn s)
        {
            if (!_pool.TryGetValue(s.Piece, out var stack)) _pool[s.Piece] = stack = new Stack<Item>();
            var item = stack.Count > 0 ? stack.Pop() : CreateItem(s.Piece);
            item.Spawn = s;
            item.Done = false;
            item.Root.gameObject.SetActive(true);
            _items.Add(item);
        }

        Item CreateItem(Piece piece)
        {
            string name = piece.ToString();
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);
            var model = new GameObject("Model").transform;
            model.SetParent(root, false);
            var filter = model.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _world.MeshOf(name);
            var renderer = model.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _world.MaterialOf(name);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return new Item { Piece = piece, Root = root, Model = model, Depth = RunnerTrack.DepthOf(piece) };
        }

        /// <summary>Puts every thing where the course has it (its distance less the distance run), with its little life:
        /// eggs bob and spin, the sleeping triceratops breathes; and its shadow.</summary>
        void PlaceItems(float dt)
        {
            float t = Time.time;
            foreach (var item in _items)
            {
                var s = item.Spawn;
                float z = s.At - _distance;
                float x = s.Wide ? 0f : s.Lane * LaneWidth;
                var rule = s.Rule;
                item.Root.position = new Vector3(x, 0f, z);
                switch (rule)
                {
                    case Rule.Egg:
                    case Rule.GoldEgg:
                        item.Model.localPosition = new Vector3(0f, s.Height + 0.1f * Mathf.Sin(t * 4f + s.At * 0.3f), 0f);
                        item.Model.localRotation = Quaternion.Euler(0f, t * 140f + s.At * 7f, 8f * Mathf.Sin(t * 3f + s.At));
                        if (z < 70f) _world.Shadow(item.Root.position, rule == Rule.GoldEgg ? 0.3f : 0.24f, 0.28f);
                        break;
                    case Rule.Dodge:
                        if (item.Piece == Piece.Trike)
                            item.Model.localScale = new Vector3(1f, 1f + 0.025f * Mathf.Sin(t * 1.6f + s.At), 1f);
                        if (z < 80f) _world.Shadow(item.Root.position, 1.05f, 0.42f, 2.9f);
                        break;
                    case Rule.Jump:
                        if (z < 80f) _world.Shadow(item.Root.position, s.Wide ? 4.6f : 1.05f, 0.34f, s.Wide ? 0.12f : 0.42f);
                        break;
                    case Rule.Duck:
                        if (z < 80f)
                        {
                            if (item.Piece == Piece.BrontoNeck)
                            {
                                _world.Shadow(item.Root.position + new Vector3(-8.2f, 0f, -0.6f), 2.6f, 0.4f, 1.6f);
                                _world.Shadow(item.Root.position + new Vector3(0f, 0f, 1.5f), 4.2f, 0.18f, 0.12f);
                            }
                            else
                            {
                                _world.Shadow(item.Root.position + new Vector3(-0.86f, 0f, 0f), 0.4f, 0.4f);
                                _world.Shadow(item.Root.position + new Vector3(0.86f, 0f, 0f), 0.4f, 0.4f);
                            }
                        }
                        break;
                }
            }
        }

        void Despawn()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                float z = item.Spawn.At - _distance;
                bool egg = item.Spawn.Rule is Rule.Egg or Rule.GoldEgg;
                if (z + item.Depth / 2f < -RunnerWorld.Behind + 5f || (item.Done && egg) ||
                    item.Spawn.Rule == Rule.Sight && z < -40f)
                {
                    Release(item);
                    _items.RemoveAt(i);
                }
            }
        }

        void Release(Item item)
        {
            item.Root.gameObject.SetActive(false);
            if (!_pool.TryGetValue(item.Piece, out var stack)) _pool[item.Piece] = stack = new Stack<Item>();
            stack.Push(item);
        }

        // ---------- collisions ----------

        void CheckCollisions()
        {
            float feet = player.FeetY;
            float top = feet + player.Height;
            foreach (var item in _items)
            {
                if (item.Done) continue;
                var rule = item.Spawn.Rule;
                if (rule == Rule.Sight) continue;
                float z = item.Spawn.At - _distance;
                if (Mathf.Abs(z) > item.Depth / 2f + 0.3f) continue;
                // slightly forgiving width so brushing a corner mid lane-change doesn't kill you
                if (!item.Spawn.Wide && Mathf.Abs(player.X - item.Spawn.Lane * LaneWidth) > LaneWidth * 0.45f + RunnerPlayer.HalfWidth - 0.2f) continue;

                bool hit;
                switch (rule)
                {
                    case Rule.Egg:
                    case Rule.GoldEgg:
                        float h = item.Spawn.Height;
                        if (feet - 0.3f < h && h < top + 0.3f) Collect(item, rule == Rule.GoldEgg);
                        continue;
                    case Rule.Jump: hit = feet < LowBarrierHeight - 0.05f; break;
                    case Rule.Duck: hit = top > HighBarrierBottom; break;
                    default: hit = true; break;
                }
                if (hit && !Invincible)
                {
                    Crash();
                    return;
                }
            }
        }

        void Collect(Item item, bool golden)
        {
            item.Done = true;
            if (golden) _golden++;
            else _eggs++;
            // eggs in a row climb a scale: do-re-mi... (resets after a short gap)
            _eggStreak = Time.time - _lastEggTime < 0.8f ? _eggStreak + 1 : 0;
            _lastEggTime = Time.time;
            GameAudio.Sfx(golden ? "egg_gold" : $"egg_{Mathf.Min(_eggStreak, 7)}", vary: 0f);
            var at = item.Model.position;
            if (fx != null) fx.Egg(at, golden);
            FlyEgg(at, golden);
        }
    }
}
