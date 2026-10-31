using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>
    /// A game made of short activities played with the whole body (Aventura!, Sporte!): the flow they share.
    /// <list type="bullet">
    /// <item>The activity screen: a card each with its best and medal; a step to the side (or ← →) moves one card, both
    /// hands up (or OK) plays; behind them a world of the game's (<see cref="BuildChooseWorld"/>), filmed its way
    /// (<see cref="PlaceChooseCamera"/>).</item>
    /// <item>An activity: its how-to card, a countdown, then about one to two minutes; points earn a medal (bronze,
    /// silver, gold, platinum). Bests and medals are saved per game. Then back to the activity screen, the next one
    /// selected. Back (Esc) from an activity returns to the activity screen.</item>
    /// <item>The player's body in metres (<see cref="BodyPuppet"/>) for activities that need it (a keeper's touches);
    /// without a camera every activity has its keys.</item>
    /// </list>
    /// A game says its <see cref="Title"/>, its activities (<see cref="AddActivities"/>), its music and where it saves.
    /// </summary>
    public abstract class ActivityGame : MonoBehaviour
    {
        public enum Phase { Choose, HowTo, Countdown, Playing, Results }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] Light sun;
        [SerializeField, Tooltip("Tundu/Toon, vertex colours with gloss: characters and props that move.")] Material actorMaterial;
        [SerializeField, Tooltip("Tundu/Toon, vertex colours, matte: the worlds.")] Material worldMaterial;
        [SerializeField, Tooltip("Tundu/Toon, plain white: confetti and debris (coloured per piece).")] Material toonMaterial;
        [SerializeField, Tooltip("Tundu/Ghost colour material (queue 3000).")] Material ghostMaterial;
        [SerializeField, Tooltip("Tundu/Ghost depth material (queue 2999).")] Material ghostDepthMaterial;
        [SerializeField, Tooltip("Tundu/Fx alpha: glass, light shafts.")] Material fxMaterial;
        [SerializeField, Tooltip("Tundu/Fx alpha drawn on top: markers.")] Material markerMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: sparks.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the soft dot: splashes, puffs.")] Material dotMaterial;
        [SerializeField, Tooltip("Tundu/Water: rivers, the sea.")] Material waterMaterial;
        [SerializeField, Tooltip("Tundu/Sky: copied, its colours set per activity.")] Material skyMaterial;

        [Header("Flow")]
        [SerializeField] float howToSeconds = 4.5f;
        [SerializeField] float countdownSeconds = 3f;

        static readonly Color Sun = new Color(1f, 0.79f, 0.24f), Coral = new Color(1f, 0.35f, 0.31f);
        /// <summary>A step to a side moves a tile and hands up plays: down to the waist is enough here.</summary>
        static readonly PlayArea ChooseArea = new PlayArea(BodyPart.Hips, side: 0.7f);

        public Phase Current { get; private set; }
        public int Selected { get; private set; }
        public IReadOnlyList<Activity> Activities => _activities;
        /// <summary>The activity being played (null on the activity screen).</summary>
        public Activity Playing { get; private set; }
        public Medal LastMedal { get; private set; }
        public bool LastNewBest { get; private set; }
        public BodyPuppet Puppet => _kit.Puppet;
        /// <summary>Tests: skip the how-to card.</summary>
        public bool SkipHowTo { get; set; }

        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        ActivityKit _kit;
        readonly List<Activity> _activities = new List<Activity>();
        float _phaseTime;
        int _previousLane;
        bool _paused;
        Transform _chooseWorld;
        Material _sky;
        Texture2D _medal, _dot;
        float _orbit;

        protected virtual void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null) _mirrorWasVisible = _mirror.Visible;
            _sky = skyMaterial != null ? new Material(skyMaterial) : null;
            if (_sky != null) RenderSettings.skybox = _sky;
            _medal = ArtShapes.Medal();
            _dot = ArtShapes.Dot();
            _kit = new ActivityKit
            {
                Cam = cam, Sun = sun, Actor = actorMaterial, World = worldMaterial, Toon = toonMaterial, Ghost = ghostMaterial,
                GhostDepth = ghostDepthMaterial, Fx = fxMaterial, Marker = markerMaterial, Water = waterMaterial, Sky = _sky,
                Fx3 = GameFx.Create(cam, sparkMaterial, dotMaterial, toonMaterial), Puppet = new BodyPuppet(), Input = _input,
            };
            AddActivities(_activities);
            Selected = Mathf.Clamp(PlayerPrefs.GetInt($"{Prefix}.selected", 0), 0, _activities.Count - 1);
            EnterChoose();
        }

        protected virtual void OnDestroy()
        {
            if (Playing != null) EndActivity();
            if (_sky != null) Destroy(_sky);
            if (_medal != null) Destroy(_medal);
            if (_dot != null) Destroy(_dot);
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
        }

        /// <summary>The game's name, big on the activity screen.</summary>
        protected abstract string Title { get; }
        /// <summary>"Choose an adventure" / "Choose a sport".</summary>
        protected abstract string ChooseText { get; }
        /// <summary>Where the game saves (PlayerPrefs keys start with it) and its log tag.</summary>
        protected abstract string Prefix { get; }
        /// <summary>The activity screen's music.</summary>
        protected abstract string ChooseMusic { get; }
        protected abstract void AddActivities(List<Activity> list);

        public int BestOf(string id) => PlayerPrefs.GetInt($"{Prefix}.best.{id}", -1);
        public Medal MedalOf(string id) => (Medal)PlayerPrefs.GetInt($"{Prefix}.medal.{id}", 0);

        // ---------------------------------------------------------------- flow

        protected virtual void Update()
        {
            // Back (Esc) from an activity goes to the activity screen; from there, to the box's home screen.
            if (Current != Phase.Choose && RemoteInput.Back)
            {
                GameAudio.Sfx("ui_back", vary: 0f);
                EnterChoose();
                return;
            }
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            _kit.Puppet.Update(body, _input != null ? _input.Gestures : null, Time.unscaledTimeAsDouble, dt);

            switch (Current)
            {
                case Phase.Choose:
                    UpdateChoose(dt);
                    break;
                case Phase.HowTo:
                    Playing.Step(dt, false);
                    if (_phaseTime >= howToSeconds || StartPressed()) SetPhase(Phase.Countdown);
                    break;
                case Phase.Countdown:
                    Playing.Step(dt, false);
                    if (_phaseTime < countdownSeconds && Mathf.FloorToInt(_phaseTime) != Mathf.FloorToInt(_phaseTime - dt))
                        GameAudio.Sfx("count", vary: 0f);
                    if (_phaseTime >= countdownSeconds)
                    {
                        GameAudio.Sfx("go", vary: 0f);
                        _input?.Gestures?.Recenter();
                        Playing.Begin();
                        SetPhase(Phase.Playing);
                    }
                    break;
                case Phase.Playing:
                    _paused = !_kit.UsingKeys && PlayerGate.ShouldPause(_input);
                    Playing.Step(_paused ? 0f : dt, !_paused);
                    if (Playing.Finished) Finish();
                    break;
                case Phase.Results:
                    Playing.Step(dt, false);
                    if (_phaseTime > 2f && StartPressed())
                    {
                        Selected = (Selected + 1) % _activities.Count;
                        EnterChoose();
                    }
                    break;
            }
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible && (Current is Phase.Choose or Phase.Results || _paused);
        }

        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            Debug.Log(Playing != null ? $"[{Prefix}] {Playing.Id}: {phase}" : $"[{Prefix}] {phase}");
        }

        /// <summary>Plays activity <paramref name="index"/> (tests start one directly).</summary>
        public void StartActivity(int index)
        {
            Selected = Mathf.Clamp(index, 0, _activities.Count - 1);
            PlayerPrefs.SetInt($"{Prefix}.selected", Selected);
            EndActivity();
            ClearChooseWorld();
            var a = _activities[Selected];
            Playing = a;
            _kit.Fx3.Clear();
            _kit.Root = new GameObject(a.Id).transform;
            a.Build(_kit);
            PlayerGate.Use(_input, a.Area);
            GameAudio.PlayMusic(a.Music);
            GameAudio.Sfx("ui_select", vary: 0f);
            SetPhase(SkipHowTo ? Phase.Countdown : Phase.HowTo);
        }

        void Finish()
        {
            var a = Playing;
            LastMedal = Medals.For(a.Points, a.MedalNeeds);
            int best = BestOf(a.Id);
            LastNewBest = a.Points > best;
            if (LastNewBest) PlayerPrefs.SetInt($"{Prefix}.best.{a.Id}", a.Points);
            if (LastMedal > MedalOf(a.Id)) PlayerPrefs.SetInt($"{Prefix}.medal.{a.Id}", (int)LastMedal);
            PlayerPrefs.Save();
            GameAudio.StopMusic(0.5f);
            GameAudio.Jingle(LastMedal >= Medal.Silver || LastNewBest ? "jingle_win" : "jingle_gameover");
            if (LastMedal != Medal.None)
                _kit.Fx3.Confetti(cam.transform.position + cam.transform.forward * 4f + Vector3.up * 0.5f, 60,
                                  new[] { Sun, Coral, new Color(0.19f, 0.78f, 0.96f), new Color(0.48f, 0.38f, 1f), new Color(0.44f, 0.84f, 0.42f) });
            Debug.Log($"[{Prefix}] {a.Id} finished: {a.Points} points, {LastMedal}{(LastNewBest ? " (new best)" : "")}, {a.Time:0.0} s. {a.ResultLine}");
            SetPhase(Phase.Results);
        }

        void EndActivity()
        {
            if (Playing == null) return;
            Playing.Dispose();
            if (_kit.Root != null) Destroy(_kit.Root.gameObject);
            _kit.Root = null;
            Playing = null;
        }

        // ---------------------------------------------------------------- the activity screen

        void EnterChoose()
        {
            EndActivity();
            _kit.Fx3.Clear();
            PlayerGate.Use(_input, ChooseArea);
            _previousLane = _input != null ? _input.Lane : 0;
            BuildChooseWorld();
            GameAudio.PlayMusic(ChooseMusic);
            SetPhase(Phase.Choose);
        }

        void UpdateChoose(float dt)
        {
            int lane = _input != null ? _input.Lane : 0;
            if (lane != _previousLane)
            {
                if (lane != 0 && !_input.UsingKeyboard) Select(Selected + lane);
                _previousLane = lane;
            }
            if (RemoteInput.Left) Select(Selected - 1);
            if (RemoteInput.Right) Select(Selected + 1);
            if (StartPressed())
            {
                StartActivity(Selected);
                return;
            }
            // the island turns slowly, the figure copies the player (or waves)
            _orbit += dt * 6f;
            var p = _kit.Puppet;
            p.KeyRoot = Vector2.zero;
            p.KeyRig = Mathf.Repeat(Time.time, 6f) < 1.6f ? ToyPoses.Cheer(Time.time) : (ToyRig?)null;
            UpdateChooseWorld(Selected, dt);
            if (PlaceChooseCamera(cam, dt)) return;
            var target = ChooseFigureAt + new Vector3(0f, 1.3f, 0f);
            var offset = Quaternion.Euler(0f, Mathf.Sin(_orbit * Mathf.Deg2Rad) * 18f, 0f) * new Vector3(0f, 1.2f, -5.2f);
            cam.transform.position = target + offset;
            cam.transform.LookAt(target + Vector3.up * 0.4f);
            cam.fieldOfView = 50f;
        }

        void Select(int index)
        {
            int next = Mathf.Clamp(index, 0, _activities.Count - 1);
            if (next == Selected) return;
            Selected = next;
            GameAudio.Sfx("ui_move");
        }

        void BuildChooseWorld()
        {
            ClearChooseWorld();
            _chooseWorld = new GameObject("Choose").transform;
            BuildChooseWorld(_chooseWorld, _kit);
        }

        /// <summary>Every frame on the activity screen: the game's own world may react to the choice.</summary>
        protected virtual void UpdateChooseWorld(int selected, float dt) { }

        /// <summary>A game may film its activity screen itself (return true); by default the camera circles the
        /// figure.</summary>
        protected virtual bool PlaceChooseCamera(Camera camera, float dt) => false;

        /// <summary>The activity screen's cards and texts (a game may draw its own).</summary>
        protected virtual void DrawChooseScreen(float w) => DrawChoose(w);

        protected IReadOnlyList<Activity> ActivityList => _activities;
        protected Texture2D MedalTexture => _medal;
        protected BodyInput Input => _input;
        protected float PhaseTime => _phaseTime;
        /// <summary>Seconds in the current phase (tests).</summary>
        public float PhaseClock => _phaseTime;

        /// <summary>Where the player's figure stands in the activity screen's world (the camera looks at it).</summary>
        protected virtual Vector3 ChooseFigureAt => Vector3.zero;

        /// <summary>The world behind the activity screen, built under <paramref name="root"/> (the player's figure is added
        /// at the middle): by default a little grassy island floating in the sky, with flowers, trees and a flag.</summary>
        protected virtual void BuildChooseWorld(Transform root, ActivityKit kit)
        {
            kit.Weather(new Color(0.36f, 0.72f, 0.95f), new Color(0.82f, 0.93f, 0.97f), new Color(0.74f, 0.89f, 0.86f),
                        new Color(0.82f, 0.93f, 0.97f), 40f, 140f, new Color(0.62f, 0.66f, 0.74f),
                        new Vector3(50f, -30f, 0f), new Color(1f, 0.96f, 0.88f), 1.2f);
            var m = new PartMesh { Srgb = true };
            var grass = new Color(0.53f, 0.81f, 0.43f);
            m.Add(PrimitiveType.Cylinder, new Vector3(0f, -0.15f, 0f), new Vector3(7f, 0.15f, 7f), grass, 0.1f)
             .Add(PrimitiveType.Sphere, new Vector3(0f, -1.6f, 0f), new Vector3(6.6f, 3f, 6.6f), new Color(0.73f, 0.55f, 0.36f), 0.05f)
             .Add(PrimitiveType.Sphere, new Vector3(0f, -2.8f, 0f), new Vector3(3.6f, 3f, 3.6f), new Color(0.6f, 0.44f, 0.3f), 0.05f);
            var rng = new System.Random(5);
            Color[] flowers = { Sun, Coral, new Color(1f, 0.6f, 0.8f), Color.white, new Color(0.48f, 0.38f, 1f) };
            for (int i = 0; i < 40; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 1.2f + (float)rng.NextDouble() * 2f;
                var at = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                m.Add(PrimitiveType.Cylinder, at + Vector3.up * 0.1f, new Vector3(0.02f, 0.1f, 0.02f), new Color(0.3f, 0.6f, 0.3f), 0f)
                 .Add(PrimitiveType.Sphere, at + Vector3.up * 0.22f, Vector3.one * 0.1f, flowers[i % flowers.Length], 0.3f);
            }
            // round trees at the back, a flag on the right
            foreach (var (x, z, h) in new[] { (-2.6f, 1.6f, 2.2f), (2.3f, 2.2f, 2.8f), (-0.6f, 2.9f, 2.5f) })
            {
                m.Add(PrimitiveType.Cylinder, new Vector3(x, h * 0.3f, z), new Vector3(0.22f, h * 0.3f, 0.22f), new Color(0.54f, 0.35f, 0.24f), 0.1f)
                 .Add(PrimitiveType.Sphere, new Vector3(x, h * 0.8f, z), new Vector3(1.4f, 1.3f, 1.4f) * (h / 2.4f), new Color(0.31f, 0.69f, 0.34f), 0.1f);
            }
            m.Add(PrimitiveType.Cylinder, new Vector3(1.8f, 1.2f, 0.4f), new Vector3(0.06f, 1.2f, 0.06f), Color.white, 0.4f)
             .Add(PrimitiveType.Cube, new Vector3(2.2f, 2.1f, 0.4f), new Vector3(0.8f, 0.5f, 0.04f), Coral, 0.3f);
            // clouds below and around
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f, r = 14f + (float)rng.NextDouble() * 10f;
                var c = new Vector3(Mathf.Cos(a) * r, -4f + (float)rng.NextDouble() * 8f, Mathf.Sin(a) * r + 6f);
                for (int k = 0; k < 3; k++)
                    m.Add(PrimitiveType.Sphere, c + new Vector3((k - 1) * 1.6f, (k == 1 ? 0.5f : 0f), 0f), Vector3.one * (k == 1 ? 3f : 2.2f), Color.white, 0f);
            }
            PartMesh.Show("Island", root, m.Build("Choose island"), kit.World);
        }

        void ClearChooseWorld()
        {
            if (_chooseWorld != null) Destroy(_chooseWorld.gameObject);
            _chooseWorld = null;
        }

        // ---------------------------------------------------------------- HUD

        protected virtual void OnGUI()
        {
            float w = GameHud.Begin();
            _kit?.Fx3.DrawPopups();
            switch (Current)
            {
                case Phase.Choose:
                    DrawChooseScreen(w);
                    break;
                case Phase.HowTo:
                    DrawHowTo(w);
                    break;
                case Phase.Countdown:
                    Playing.DrawHud(w);
                    DrawTopBar(w);
                    DrawCountdown(w);
                    break;
                case Phase.Playing:
                    Playing.DrawHud(w);
                    DrawTopBar(w);
                    if (_paused) PlayerGate.DrawPaused(w, _input);
                    else if (!_kit.UsingKeys) PlayerGate.DrawEdgeWarning(w, _input);
                    break;
                case Phase.Results:
                    DrawResults(w);
                    break;
            }
        }

        /// <summary>The points and the medals won so far: a compact panel at the top right (the activity's own HUD has
        /// the top left).</summary>
        void DrawTopBar(float w)
        {
            var needs = Playing.MedalNeeds;
            var have = Medals.For(Playing.Points, needs);
            var r = new Rect(w - 412f, 28f, 376f, 72f);
            SportHud.Panel(r, SportHud.Night, 18f, new Color(1f, 1f, 1f, 0.15f));
            SportHud.Text(new Rect(r.x + 22f, r.y, 120f, r.height), ActivityStrings.Points.ToUpperInvariant(), 26, new Color(1f, 1f, 1f, 0.7f),
                          TextAnchor.MiddleLeft, 0f);
            SportHud.Text(new Rect(r.x + 22f + SportHud.Width(ActivityStrings.Points.ToUpperInvariant(), 26) + 12f, r.y, 140f, r.height),
                          Playing.Points.ToString(), 44, Color.white, TextAnchor.MiddleLeft, 0f);
            for (int i = 0; i < 4; i++)
            {
                var m = (Medal)(i + 1);
                GUI.color = have >= m ? MedalLook.Colour(m) : new Color(1f, 1f, 1f, 0.18f);
                GUI.DrawTexture(new Rect(r.xMax - 196f + i * 46f, r.y + 14f, 44f, 44f), _medal);
            }
            GUI.color = Color.white;
            if (have < Medal.Platinum)
                SportHud.Text(new Rect(w - 520f, r.yMax + 4f, 484f, 40f), ActivityStrings.NextMedal(have + 1, needs[(int)have]), 26,
                              new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleRight, 2f);
        }

        /// <summary>3, 2, 1, go: each number slams in.</summary>
        void DrawCountdown(float w)
        {
            int n = Mathf.CeilToInt(countdownSeconds - _phaseTime);
            float into = _phaseTime - Mathf.Floor(_phaseTime);
            string text = n > 0 ? n.ToString() : CommonStrings.Go;
            SportHud.Banner(w, 420f, text, n > 0 ? SportHud.Sky : SportHud.Leaf, into, 1f, 200);
            SportHud.Pill(w / 2f, 900f, Playing.HowTo.Length > 90 ? ActivityStrings.GetReady : Playing.HowTo, Color.white, 32);
        }

        /// <summary>The default activity screen: the game's name, a card a sport (its medal and best), the chosen one's
        /// how-to, a hint.</summary>
        void DrawChoose(float w)
        {
            SportHud.Text(new Rect(80f, 40f, w, 140f), Title, 120, SportHud.Sun, TextAnchor.MiddleLeft, 6f);
            SportHud.Text(new Rect(86f, 170f, w, 60f), ChooseText, 44, Color.white, TextAnchor.MiddleLeft, 3f);
            DrawCards(w, 640f);
            DrawChooseHint(w);
        }

        /// <summary>A card a sport along the bottom, the chosen one raised and rimmed in white.</summary>
        protected void DrawCards(float w, float y)
        {
            int n = _activities.Count;
            const float gap = 36f;
            float cardW = Mathf.Min(440f, (w - 160f - gap * (n - 1)) / n), cardH = 250f;
            float x0 = (w - (cardW * n + gap * (n - 1))) / 2f;
            int medals = 0;
            for (int i = 0; i < n; i++)
            {
                var a = _activities[i];
                bool selected = i == Selected;
                float lift = selected ? 26f : 0f;
                var r = new Rect(x0 + i * (cardW + gap), y - lift, cardW, cardH);
                // only the chosen card, raised, casts a shadow (each layer is a draw of the whole card on the TV box's GPU)
                SportHud.Panel(r, selected ? Color.white : new Color(0.93f, 0.95f, 1f), 26f, selected ? a.Colour : (Color?)null, 6f, selected,
                               selected ? 1f : 0.9f);
                // the sport's colour band with its name
                var band = new Rect(r.x + 10f, r.y + 10f, r.width - 20f, 92f);
                SportHud.Rounded(band, a.Colour, 18f);
                SportHud.Text(band, a.Title.ToUpperInvariant(), cardW > 380f ? 52 : 42, Color.white, TextAnchor.MiddleCenter, 3f);
                var medal = MedalOf(a.Id);
                if (medal != Medal.None) medals++;
                GUI.color = medal == Medal.None ? new Color(0.08f, 0.1f, 0.22f, 0.15f) : MedalLook.Colour(medal);
                GUI.DrawTexture(new Rect(r.x + 26f, r.y + 120f, 96f, 96f), _medal);
                GUI.color = Color.white;
                int best = BestOf(a.Id);
                var s = SportHud.Style(30);
                s.alignment = TextAnchor.MiddleLeft;
                s.normal.textColor = SportHud.Night;
                GUI.Label(new Rect(r.x + 140f, r.y + 118f, r.width - 150f, 50f), medal == Medal.None ? ActivityStrings.NoBest : ActivityStrings.MedalName(medal), s);
                var s2 = SportHud.Style(26, false);
                s2.alignment = TextAnchor.MiddleLeft;
                s2.normal.textColor = new Color(0.08f, 0.1f, 0.22f, 0.7f);
                GUI.Label(new Rect(r.x + 140f, r.y + 164f, r.width - 150f, 44f), best >= 0 ? ActivityStrings.Best(best) : "", s2);
            }
            SportHud.Pill(w / 2f, y + cardH + 26f, ActivityStrings.MedalsWon(medals, n), SportHud.Sun, 28);
        }

        protected void DrawChooseHint(float w)
        {
            string hint = PlayerGate.State(_input) switch
            {
                GateState.NoService => ActivityStrings.ChooseKeys,
                GateState.NoPlayer => CommonStrings.StepIn,
                GateState.Placing => PlayerGate.PlacementHint(_input),
                GateState.Calibrating => CommonStrings.HoldStill,
                _ => PlayerGate.PlacementHint(_input) ?? ActivityStrings.ChooseHint,
            };
            SportHud.Text(new Rect(0f, 990f, w, 60f), hint, 36, Color.white, TextAnchor.MiddleCenter, 3f);
        }

        /// <summary>The how-to card: the sport's colour, what to do with the body, the medals' points.</summary>
        void DrawHowTo(float w)
        {
            var a = Playing;
            float t = Mathf.Clamp01(_phaseTime / 0.35f);
            float slide = (1f - (1f - t) * (1f - t)) ;
            var r = new Rect(w / 2f - 700f, 230f + (1f - slide) * 60f, 1400f, 560f);
            SportHud.Panel(r, Color.white, 34f, null, 0f, true, slide);
            var band = new Rect(r.x + 14f, r.y + 14f, r.width - 28f, 130f);
            var bc = a.Colour;
            bc.a = slide;
            SportHud.Rounded(band, bc, 26f);
            SportHud.Text(band, a.Title.ToUpperInvariant(), 84, new Color(1f, 1f, 1f, slide), TextAnchor.MiddleCenter, 4f);
            var body = SportHud.Style(40, false);
            body.wordWrap = true;
            body.alignment = TextAnchor.UpperCenter;
            body.normal.textColor = new Color(0.08f, 0.1f, 0.22f, slide);
            GUI.Label(new Rect(r.x + 80f, r.y + 175f, r.width - 160f, 200f), a.HowTo, body);
            var needs = a.MedalNeeds;
            for (int i = 0; i < 4; i++)
            {
                var m = (Medal)(i + 1);
                float x = r.x + 190f + i * 270f;
                GUI.color = MedalLook.Colour(m) * new Color(1f, 1f, 1f, slide);
                GUI.DrawTexture(new Rect(x, r.y + 390f, 72f, 72f), _medal);
                GUI.color = Color.white;
                var s = SportHud.Style(36);
                s.alignment = TextAnchor.MiddleLeft;
                s.normal.textColor = new Color(0.08f, 0.1f, 0.22f, slide);
                GUI.Label(new Rect(x + 84f, r.y + 390f, 180f, 72f), needs[i].ToString(), s);
            }
            string keys = PlayerGate.State(_input) == GateState.NoService ? a.KeyboardHelp : ActivityStrings.GetReady;
            SportHud.Pill(w / 2f, r.yMax - 14f, keys, SportHud.Sun, 30, null, slide);
        }

        /// <summary>The results: the medal popping in, its name, the points (a new best), the activity's line.</summary>
        void DrawResults(float w)
        {
            var a = Playing;
            float t = Mathf.Clamp01(_phaseTime / 0.35f);
            var r = new Rect(w / 2f - 640f, 150f, 1280f, 700f);
            SportHud.Panel(r, Color.white, 34f, null, 0f, true, t);
            var band = new Rect(r.x + 14f, r.y + 14f, r.width - 28f, 110f);
            var bc = a.Colour;
            bc.a = t;
            SportHud.Rounded(band, bc, 26f);
            SportHud.Text(band, a.Title.ToUpperInvariant(), 64, new Color(1f, 1f, 1f, t), TextAnchor.MiddleCenter, 3f);
            // the medal pops in with a little bounce
            float pop = _phaseTime < 0.25f ? 0.05f : Mathf.Max(0.05f, 1f + 0.35f * Mathf.Exp(-(_phaseTime - 0.25f) * 6f) * Mathf.Cos((_phaseTime - 0.25f) * 14f));
            var old = GUI.matrix;
            var centre = new Vector2(w / 2f, r.y + 270f);
            GUI.matrix = old * Matrix4x4.TRS(centre, Quaternion.identity, new Vector3(pop, pop, 1f)) * Matrix4x4.Translate(-centre);
            GUI.color = LastMedal == Medal.None ? new Color(0.08f, 0.1f, 0.22f, 0.15f) : MedalLook.Colour(LastMedal);
            GUI.DrawTexture(new Rect(centre.x - 100f, centre.y - 100f, 200f, 200f), _medal);
            GUI.color = Color.white;
            GUI.matrix = old;
            var title = SportHud.Style(52);
            title.alignment = TextAnchor.MiddleCenter;
            title.normal.textColor = SportHud.Night;
            GUI.Label(new Rect(r.x, r.y + 390f, r.width, 70f), ActivityStrings.YouWon(LastMedal), title);
            var pts = SportHud.Style(44);
            pts.alignment = TextAnchor.MiddleCenter;
            pts.normal.textColor = new Color(0.08f, 0.1f, 0.22f, 0.85f);
            GUI.Label(new Rect(r.x, r.y + 465f, r.width, 60f), $"{ActivityStrings.Points}: {a.Points}", pts);
            if (LastNewBest) SportHud.Pill(w / 2f, r.y + 530f, CommonStrings.NewBest, SportHud.Sun, 32);
            var line = SportHud.Style(32, false);
            line.alignment = TextAnchor.MiddleCenter;
            line.normal.textColor = new Color(0.08f, 0.1f, 0.22f, 0.75f);
            GUI.Label(new Rect(r.x, r.y + 595f, r.width, 50f), a.ResultLine, line);
            if (_phaseTime > 2f)
            {
                string again = PlayerGate.State(_input) == GateState.NoService ? ActivityStrings.BackKeys
                             : PlayerGate.PlacementHint(_input) ?? ActivityStrings.BackToChoose;
                SportHud.Text(new Rect(0f, r.yMax + 30f, w, 60f), again, 34, Color.white, TextAnchor.MiddleCenter, 3f);
            }
        }
    }
}
