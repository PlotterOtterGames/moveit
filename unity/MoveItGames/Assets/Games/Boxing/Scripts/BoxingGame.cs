using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Boxing
{
    /// <summary>
    /// "Superboks!" (2026-11-18; the overhaul of 2026-11-19, the user: "a complete graphics and gameplay overhaul ...
    /// production ready and fun like the Wii box game ... don't over-AI it, keep it well designed ... not first person:
    /// from behind, see-through, the camera a bit at the top"). A superhero climbs from the neighbourhood's gym to the
    /// stars (<see cref="Campaign"/>: four places, three opponents each), fighting Wii Sports Boxing's way:
    /// <list type="bullet">
    /// <item><b>The view</b> is Wii Sports Boxing's: behind the hero and a little above, the hero see-through
    /// (Tundu/AthleteGhost) with solid gloves, the opponent big in front (<see cref="BoxerView"/>), the referee at the side
    /// (<see cref="RefereeView"/>), the stage round the ring (<see cref="BoxingWorld"/>). Designed shots for the rest: the
    /// arena and the opponent close up for the intro, a side shot in slow motion for a knockdown, the super punch's dive,
    /// the winner from the front.</item>
    /// <item><b>The fight</b> is <see cref="Bout"/>: punch where the gloves aren't, gloves up block, a lean dodges (Wii's
    /// slow-motion moment: the opponent staggers, open for a counter), punches in a row make it cover up (an uppercut
    /// breaks the cover) and dizzy; knockdowns and the referee's count, three rounds, KO, TKO, a decision; the super punch
    /// with both arms up when the power is full.</item>
    /// <item><b>The map</b> (the waiting screen): the climb, the fight to play, the difficulty, the outfit and the gloves;
    /// both hands up or OK to fight.</item>
    /// </list>
    /// Keys without a camera: J K punch, U uppercut, I hook, B + a punch at the body, ↑ guard, ← → lean, ↓ duck, S the super;
    /// the remote's OK punches, its arrows guard, lean and duck for a moment.
    /// </summary>
    public sealed partial class BoxingGame : MonoBehaviour
    {
        public enum Phase { Waiting, Intro, Fight, Result }
        enum Row { Fight, Difficulty, Outfit, Gloves }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] Light sun;
        [SerializeField] BoxingWorld world;
        [SerializeField] BoxingArt art = new BoxingArt();
        [SerializeField, Tooltip("Tundu/Fx with the star texture: hit stars, confetti.")] Material sparkMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the soft dot: sparks, snow, dust.")] Material dotMaterial;
        [SerializeField, Tooltip("Tundu/Portrait: a round portrait in the HUD.")] Material portraitMaterial;
        [SerializeField] float introSeconds = 3.8f, resultDelay = 2.4f;

        const string ProgressKey = "boxing.progress";
        public const float HeroZ = -0.5f, FoeZ = 0.52f;

        public Phase Current { get; private set; }
        public Bout Bout => _bout;
        public BoxingProgress Progress => _progress;
        public BoxingDifficulty Difficulty => (BoxingDifficulty)_progress.Difficulty;
        public int SelectedFight => _progress.Selected[_progress.Difficulty];
        public BoutMoves Moves => _moves;
        public FightReward LastReward { get; private set; }
        /// <summary>Every event of the fight (tests and logs).</summary>
        public event Action<BoutEvent> BoutEvents;
        /// <summary>Tests: nothing is saved.</summary>
        public static bool Saving = true;

        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        BoxingProgress _progress;
        Bout _bout;
        BoxerView _hero, _foe;
        RefereeView _referee;
        string _foeId;
        BoxingFx _fx;
        Portraits _portraits;
        readonly BoutMoves _moves = new BoutMoves();
        readonly List<BoutPunch> _punches = new List<BoutPunch>();
        double _lastSample;
        float _phaseTime, _shake, _slowMo = 1f, _slowUntil, _kick;
        bool _paused, _ghost;
        Row _row = Row.Fight;
        int _nextRight = 1;
        // keys and the remote: what they hold
        float _keyGuardUntil, _keyLeanUntil, _keyDuckUntil;
        int _keyLean;
        bool _keyBody;
        // calls in the middle, popups at the boxers
        readonly List<(string text, string sub, Color colour, float at, float life, int size)> _calls = new List<(string, string, Color, float, float, int)>();
        readonly List<Popup> _popups = new List<Popup>();
        int _blockedInRow;
        float _tipUntil, _flashRate, _dodgeFlash, _hurtFlash;
        string _tip;
        bool _coverTipShown;
        readonly System.Random _rng = new System.Random(20261119);

        sealed class Popup { public Vector3 World; public string Text; public Color Color; public float Age; }

        static readonly PlayArea Area = new PlayArea(BodyPart.Hips, side: 0.5f, above: 0.4f);
        static readonly Color Sun = new Color(1f, 0.79f, 0.24f), Coral = new Color(1f, 0.35f, 0.31f), Sky = new Color(0.19f, 0.78f, 0.96f),
                              Grape = new Color(0.48f, 0.38f, 1f), Leaf = new Color(0.44f, 0.84f, 0.42f);

        // ------------------------------------------------------------------ lifecycle

        void Start()
        {
            useGUILayout = false;
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            PlayerGate.Use(_input, Area);
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null) _mirrorWasVisible = _mirror.Visible;
            _progress = BoxingProgress.Load(PlayerPrefs.GetString(ProgressKey, ""));
            // measuring on the box (PerfLab): autoplay=1 = the hero fights by itself, stage=<0-3> = that stage's champion
            // again and again; neither saves anything
            Autoplay |= PerfSwitches.Get("autoplay") == "1";
            _perfStage = PerfSwitches.Get("stage");
            if (int.TryParse(_perfStage, out int stage))
            {
                _autoFight = Mathf.Clamp(stage, 0, Campaign.Stages.Length - 1) * Campaign.FightsPerStage + Campaign.FightsPerStage - 1;
                _measuring = true;
                OpenAll(Difficulty);
                _progress.Selected[_progress.Difficulty] = _autoFight;
            }
            _measuring |= Autoplay;
            world.Init(art, sun, cam);
            _fx = BoxingFx.Create(sparkMaterial, dotMaterial, art.glow, art.burst, art.sheetJson, cam);
            _portraits = new Portraits();
            if (cam != null) cam.nearClipPlane = 0.08f;
            _referee = RefereeView.Create(world.transform, BoxingWorld.RingHeight);
            ShowFight(SelectedFight, true);
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("fly");
        }

        void OnDestroy()
        {
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
            Time.timeScale = 1f;
            _hudBatch?.Dispose();
            _portraits?.Dispose();
        }

        void Save()
        {
            if (!Saving || _measuring) return;
            PlayerPrefs.SetString(ProgressKey, _progress.Save());
            PlayerPrefs.Save();
        }

        /// <summary>The hero see-through (from behind, in the fight) or solid (the shots from the front, the map).</summary>
        void Ghost(bool on)
        {
            if (_hero == null || on == _ghost) return;
            _ghost = on;
            _hero.SeeThrough(on ? art.ghostDepth : null, on ? art.ghostColour : null, art.cape);
            if (!on && _hero.Chibi != null) _hero.Chibi.Low = false;
        }

        /// <summary>The hero a little smaller than a stage's opponents: they get stronger, and bigger, up the climb.</summary>
        public const float HeroSize = 0.88f;

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            if (_mirror != null) _mirror.Visible = phase == Phase.Waiting && _mirrorWasVisible;
        }

        bool UsingKeys => _input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService;
        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        // ------------------------------------------------------------------ the boxers on the stage

        /// <summary>The stage of fight <paramref name="fight"/>, the hero and that opponent standing in it.</summary>
        void ShowFight(int fight, bool rebuildHero)
        {
            var o = Campaign.Opponent(fight);
            world.Build(o.Stage);
            if (_hero == null || rebuildHero)
            {
                if (_hero != null) Destroy(_hero.gameObject);
                _hero = BoxerView.Create("Hero", world.transform, BoxingLooks.Hero(_progress.Outfit), HeroSize, true, art.glove, art.athlete,
                                         BoxingLooks.GloveTable(_progress.Gloves), BoxingLooks.Cape(_progress.Outfit), art.cape);
                _ghost = false;
                _hero.transform.localPosition = new Vector3(0f, BoxingWorld.RingHeight, HeroZ);
                _portraits.Forget(0);
            }
            if (_foe == null || _foeId != o.Id)
            {
                if (_foe != null) Destroy(_foe.gameObject);
                var (look, gloves, cape) = BoxingLooks.Opponent(o.Id);
                _foe = BoxerView.Create(o.Name, world.transform, look, o.Scale, false, art.glove, art.athlete, gloves, cape, art.cape);
                _foe.transform.localPosition = FoeStand(o);
                _foe.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                _foeId = o.Id;
                _portraits.Forget(1);
            }
        }

        static Vector3 FoeStand(OpponentProfile o) => new Vector3(0f, BoxingWorld.RingHeight, FoeZ + 0.12f * (o.Scale - 1f));

        // ------------------------------------------------------------------ the frame

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            FollowPerfStage();
            float dt = Time.deltaTime;
            _phaseTime += dt;
            ReadBody();
            switch (Current)
            {
                case Phase.Waiting:
                    Menu();
                    if (_phaseTime > 0.6f && (StartPressed() || Autoplay && _phaseTime > 2f) && _progress.Unlocked(Difficulty, SelectedFight)) BeginIntro();
                    IdleViews(dt);
                    break;
                case Phase.Intro:
                    IdleViews(dt);
                    if (_phaseTime >= introSeconds) BeginFight();
                    break;
                case Phase.Fight:
                    Fight(dt);
                    break;
                case Phase.Result:
                    IdleViews(dt);
                    if (_phaseTime > resultDelay)
                    {
                        if (StartPressed() || Autoplay && _phaseTime > resultDelay + 1.5f)
                        {
                            // won: the next fight (the map moved on); lost: the same again (measuring: the stage's champion)
                            if (_autoFight >= 0) _progress.Selected[_progress.Difficulty] = _autoFight;
                            ShowFight(SelectedFight, LastReward != null && LastReward.NewOutfits.Count > 0);
                            BeginIntro();
                        }
                        else if (RemoteInput.Left || RemoteInput.Right || RemoteInput.Up || RemoteInput.Down)
                        {
                            ShowFight(SelectedFight, false);
                            SetPhase(Phase.Waiting);
                            GameAudio.PlayMusic("fly");
                        }
                    }
                    break;
            }
            _fx.Weather(world.Stage, dt);
            CrowdFlashes(dt);
            UpdatePopups(dt);
        }

        void LateUpdate()
        {
            UpdateCamera(Time.deltaTime);
            DrawGlows();
            if (_hero != null && _foe != null)
            {
                _portraits.Ensure(0, _hero, cam);
                _portraits.Ensure(1, _foe, cam);
            }
        }

        /// <summary>The map's rows (the remote): ↑ ↓ pick one, ← → change it.</summary>
        void Menu()
        {
            if (RemoteInput.Up || RemoteInput.Down)
            {
                _row = (Row)(((int)_row + (RemoteInput.Down ? 1 : 3)) % 4);
                GameAudio.Sfx("ui_move", 0.5f);
                return;
            }
            int step = RemoteInput.Right ? 1 : RemoteInput.Left ? -1 : 0;
            if (step == 0) return;
            switch (_row)
            {
                case Row.Fight:
                {
                    int next = Mathf.Clamp(SelectedFight + step, 0, Campaign.Fights - 1);
                    if (!_progress.Unlocked(Difficulty, next)) { GameAudio.Sfx("ui_back", 0.5f); return; }
                    _progress.Selected[_progress.Difficulty] = next;
                    ShowFight(next, false);
                    break;
                }
                case Row.Difficulty:
                    _progress.Difficulty = Mathf.Clamp(_progress.Difficulty + step, 0, BoxingProgress.Difficulties - 1);
                    ShowFight(SelectedFight, false);
                    break;
                case Row.Outfit:
                {
                    int n = _progress.OutfitsUnlocked;
                    _progress.Outfit = (_progress.Outfit + step + n) % n;
                    ShowFight(SelectedFight, true);
                    break;
                }
                default:
                {
                    int g = _progress.Gloves;
                    for (int k = 0; k < BoxingProgress.GloveCount; k++)
                    {
                        g = (g + step + BoxingProgress.GloveCount) % BoxingProgress.GloveCount;
                        if (_progress.GlovesUnlocked(g)) break;
                    }
                    _progress.Gloves = g;
                    _hero.SetGloves(BoxingLooks.GloveTable(g));
                    _portraits.Forget(0);
                    break;
                }
            }
            GameAudio.Sfx("ui_select", 0.5f);
            Save();
        }

        /// <summary>Test hook: picks a fight and a difficulty (as if chosen on the map).</summary>
        public void Choose(BoxingDifficulty difficulty, int fight)
        {
            _progress.Difficulty = (int)difficulty;
            _progress.Selected[(int)difficulty] = fight;
            ShowFight(fight, false);
        }

        /// <summary>Test hook: every fight open at <paramref name="difficulty"/>.</summary>
        public void OpenAll(BoxingDifficulty difficulty)
        {
            for (int i = 0; i < Campaign.Fights - 1; i++)
                if (!_progress.Won(difficulty, i)) _progress.SetStars(difficulty, i, 1);
        }

        /// <summary>Test hook and the map's start: the intro, then the fight.</summary>
        public void BeginIntro()
        {
            ShowFight(SelectedFight, false);
            _bout = null;
            _calls.Clear();
            _referee.Reset(BoxingWorld.RingHeight);
            SetPhase(Phase.Intro);
            GameAudio.PlayMusic("boxing");
            GameAudio.Sfx("ready", 0.7f);
            world.CheerCrowd(0.6f);
        }

        void BeginFight()
        {
            var level = BoxingLevel.For(Difficulty, SelectedFight);
            _bout = new Bout(level, Environment.TickCount);
            _moves.Recenter();
            _blockedInRow = 0;
            _coverTipShown = false;
            _slowMo = 1f;
            _overAt = -1f;
            SetPhase(Phase.Fight);
            if (_input != null && PlayerGate.State(_input) != GateState.NoService) _input.PreferBody();
            Debug.Log($"[Boxing] Fight {SelectedFight} ({Campaign.Opponent(SelectedFight).Id}) at {Difficulty}, strength {level.Strength:0.00}");
        }

        void Fight(float dt)
        {
            _paused = !Autoplay && !UsingKeys && PlayerGate.ShouldPause(_input);
            if (_paused || _bout == null) return;
            if (Autoplay) ReadAutoplay();
            else if (UsingKeys) ReadKeys();
            float step = dt * (Time.unscaledTime < _slowUntil ? _slowMo : 1f);
            _bout.Step(step);
            foreach (var e in _bout.Events) Handle(e);
            _bout.Events.Clear();
            var pose = CurrentPose();
            float lean = Autoplay || UsingKeys ? Mathf.Clamp(pose.HeadX / HeroPose.KeyMove, -1f, 1f) : _moves.LeanAmount;
            float duck = Autoplay || UsingKeys ? (pose.Duck ? 1f : 0f) : pose.Duck ? 1f : Mathf.Clamp01(_moves.DuckAmount * 0.6f);
            bool guard = pose.Guard || UsingKeys && Keyboard.current != null && Keyboard.current.upArrowKey.isPressed;
            _hero.FollowHero(_bout, guard, lean, duck, step);
            _foe.FollowFoe(_bout, step);
            AimFoe();
            _referee.Follow(_bout, _hero.transform.localPosition, _foe.transform.localPosition, BoxingWorld.RingHeight, step);
            if (_bout.Phase == BoutPhase.Over && _overAt < 0f) _overAt = Time.time;
            if (_overAt > 0f && Time.time - _overAt > 4.2f) EndFight();
        }

        float _overAt = -1f;

        void EndFight()
        {
            _overAt = -1f;
            int fight = SelectedFight;
            LastReward = _progress.Record(Difficulty, fight, _bout.HeroWon, _bout.Stars);
            Save();
            SetPhase(Phase.Result);
            if (_bout.HeroWon)
            {
                GameAudio.Jingle("jingle_win", 0.3f);
                _fx.Confetti(new Vector3(0f, BoxingWorld.RingHeight + 1f, 0.2f), LastReward.Belt ? 240 : 120);
                world.CheerCrowd(1f);
            }
            else GameAudio.Jingle("jingle_gameover", 0.2f);
            Debug.Log($"[Boxing] Fight over: {(_bout.HeroWon ? "won" : "lost")} by {_bout.Ending} in round {_bout.Round}, {_bout.Stars} stars; " +
                      $"punches {_bout.Landed}/{_bout.Thrown}, dodges {_bout.Dodges}, blocks {_bout.Blocks}, hits taken {_bout.HitsTaken}, " +
                      $"knockdowns {_bout.Knockdowns}/{_bout.HeroKnockdowns}, covers broken {_bout.CoversBroken}, supers {_bout.Supers}");
        }

        /// <summary>Between fights: both boxers in their stance (the hero follows the player's guard and lean even here:
        /// they can try the moves before the fight); the referee at his place.</summary>
        void IdleViews(float dt)
        {
            var idle = _idle ??= new Bout(BoxingLevel.For(BoxingDifficulty.Easy, 0), 1);
            bool guard = !UsingKeys && _moves.Pose.Guard;
            float lean = UsingKeys ? 0f : _moves.LeanAmount;
            var showing = Current == Phase.Result && _bout != null ? _bout : idle;
            _hero.FollowHero(showing, guard, lean, UsingKeys ? 0f : (_moves.Pose.Duck ? 1f : 0f), dt);
            _foe.FollowFoe(showing, dt);
            _foe.transform.localPosition = FoeStand(Campaign.Opponent(SelectedFight));
            _foe.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _foe.StrikeAt = null;
            _referee.Follow(Current == Phase.Result ? _bout : null, _hero.transform.localPosition, _foe.transform.localPosition, BoxingWorld.RingHeight, dt);
        }

        Bout _idle;

        /// <summary>The opponent's punch flies at where it aimed: the hero's head plus the aim.</summary>
        void AimFoe()
        {
            if (_foe == null || _bout == null || _hero == null) return;
            var head = _hero.transform.position + Vector3.up * (1.06f * HeroSize);
            _foe.StrikeAt = _bout.State is Foe.Strike or Foe.Impact ? head + (Vector3)_bout.Attack.Aim : (Vector3?)null;
        }

        // ------------------------------------------------------------------ input

        /// <summary>
        /// A new camera sample: the moves reader takes the joints (raw: it filters the pose model's glitches itself);
        /// what it saw goes to the rules with the match time the camera saw it (the sample's age taken off now), then how
        /// far the camera has told everything.
        /// </summary>
        void ReadBody()
        {
            var body = MotionSystem.Instance != null ? MotionSystem.Instance.ActiveBody : null;
            if (body == null || !body.IsTracking || body.SampleTime == _lastSample) return;
            _lastSample = body.SampleTime;
            _punches.Clear();
            _moves.Update(body.SampleTime, body.Raw, body.Visibility, body.HasWorld ? body.RawWorld : null, body.HasWorld, _punches);
            bool fighting = Current == Phase.Fight && !_paused && !UsingKeys && !Autoplay && _bout != null;
            foreach (var p in _punches)
            {
                Debug.Log($"[Boxing] Punch {(p.Right ? "right" : "left")} {p.Blow}{(p.Body ? " body" : "")} {p.Speed:0.0} torso/s " +
                          $"dir ({p.Direction.x:0.00}, {p.Direction.y:0.00}) ext {p.Extension:0.0} rule {p.Rule}");
                if (!fighting && Current != Phase.Fight) _hero.Punch(p.Right, p.Blow, p.Body);   // shadow boxing on the map
            }
            if (!fighting) return;
            double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            double age = Math.Max(0.0, Math.Min(0.35, nowUnix - body.SampleTime));
            double at = _bout.Time - age;
            foreach (var p in _punches)
            {
                double t = at - (body.SampleTime - p.Time);
                bool accepted = _bout.Phase == BoutPhase.Fighting && _bout.Hero == HeroState.Fighting;
                _bout.Punch(t, p.Right, p.Blow, p.Body, p.Speed);
                if (accepted) _hero.Punch(p.Right, p.Blow, p.Body);
                else if (_bout.Phase == BoutPhase.Down && _bout.DownFighter == 0) GameAudio.Sfx("box_swish", 0.4f, 1.3f);
            }
            if (!double.IsNegativeInfinity(_moves.Settled))
            {
                double t = at - (body.SampleTime - _moves.Settled);
                _bout.Pose(t, _moves.Pose);
                _bout.SawUntil(t);
            }
        }

        /// <summary>Keys and the remote (no camera): punches at once, holds for a moment.</summary>
        void ReadKeys()
        {
            var kb = Keyboard.current;
            float now = Time.time;
            if (RemoteInput.Up) _keyGuardUntil = now + 0.7f;
            if (RemoteInput.Left || kb != null && kb.leftArrowKey.isPressed) { _keyLean = -1; _keyLeanUntil = now + (kb != null && kb.leftArrowKey.isPressed ? 0.05f : 0.6f); }
            if (RemoteInput.Right || kb != null && kb.rightArrowKey.isPressed) { _keyLean = 1; _keyLeanUntil = now + (kb != null && kb.rightArrowKey.isPressed ? 0.05f : 0.6f); }
            if (RemoteInput.Down || kb != null && kb.downArrowKey.isPressed) _keyDuckUntil = now + (kb != null && kb.downArrowKey.isPressed ? 0.05f : 0.6f);
            if (kb != null && kb.bKey.wasPressedThisFrame) _keyBody = true;
            if (kb != null && kb.sKey.wasPressedThisFrame) _bout.Super();
            void Key(bool right, Blow blow)
            {
                bool accepted = _bout.Phase == BoutPhase.Fighting && _bout.Hero == HeroState.Fighting;
                _bout.Punch(_bout.Time, right, blow, _keyBody && blow != Blow.Uppercut, 4f);
                if (accepted) _hero.Punch(right, blow, _keyBody && blow != Blow.Uppercut);
                _keyBody = false;
            }
            if (kb != null && kb.jKey.wasPressedThisFrame) Key(false, Blow.Straight);
            if (kb != null && kb.kKey.wasPressedThisFrame) Key(true, Blow.Straight);
            if (kb != null && kb.uKey.wasPressedThisFrame) Key((_nextRight ^= 1) == 1, Blow.Uppercut);
            if (kb != null && kb.iKey.wasPressedThisFrame) Key((_nextRight ^= 1) == 1, Blow.Hook);
            if (RemoteInput.Confirm || kb != null && kb.spaceKey.wasPressedThisFrame) Key((_nextRight ^= 1) == 1, Blow.Straight);
            var pose = HeroPose.Keys(now < _keyGuardUntil || kb != null && kb.upArrowKey.isPressed, now < _keyLeanUntil ? _keyLean : 0,
                                     now < _keyDuckUntil);
            _keyPose = pose;
            _bout.Pose(_bout.Time, pose);
            _bout.SawUntil(_bout.Time);
        }

        HeroPose _keyPose;

        HeroPose CurrentPose() => Autoplay ? _autoPose : UsingKeys ? _keyPose : _moves.Pose;

        // ------------------------------------------------------------------ the hero by itself (measuring)

        /// <summary>The hero fights by itself (PerfLab <c>autoplay=1</c>: a fight nobody plays, to measure the box; nothing
        /// saved): the right defence a moment after each tell, punches where the gloves aren't (an uppercut into the cover),
        /// up again by punching, the super when the power is full; the map and the results go on by themselves.</summary>
        public bool Autoplay { get; set; }
        bool _measuring;
        int _autoFight = -1;
        HeroPose _autoPose, _autoDefence;
        double _autoNext, _autoDefendFrom = double.MaxValue, _autoDefendUntil = double.MinValue, _autoSuperUntil = double.MinValue;
        bool _autoRight, _autoFireball;
        Vector2 _autoHead;
        float _autoBodyX;
        readonly System.Random _autoRng = new System.Random(20261119);
        string _perfStage = "";

        /// <summary>PerfLab's stage= changed while the scene is open (an A/B of the stages, which doesn't reload the scene):
        /// that stage's champion, from its intro.</summary>
        void FollowPerfStage()
        {
            if (!Autoplay && PerfSwitches.Get("autoplay") == "1") Autoplay = _measuring = true;
            string s = PerfSwitches.Get("stage");
            if (s == _perfStage) return;
            _perfStage = s;
            if (!int.TryParse(s, out int stage)) return;
            _autoFight = Mathf.Clamp(stage, 0, Campaign.Stages.Length - 1) * Campaign.FightsPerStage + Campaign.FightsPerStage - 1;
            _measuring = true;
            OpenAll(Difficulty);
            _progress.Selected[_progress.Difficulty] = _autoFight;
            if (Current == Phase.Waiting) ShowFight(SelectedFight, false);
            else BeginIntro();
        }

        void AutoDefend(BoutEvent e)
        {
            if (e.Kind != BoutEventKind.WindUp && e.Kind != BoutEventKind.FireballThrown) return;
            bool fireball = e.Kind == BoutEventKind.FireballThrown;
            var a = fireball ? new PlannedAttack { Attack = Attack.Fireball } : _bout.Attack;
            a.Aim = _autoHead;
            _autoDefence = a.Body || Bout.GuardStops(a.Attack) && _autoRng.NextDouble() < 0.35
                ? new HeroPose { Guard = true } : Bout.Escape(a, 0.24f, _autoRng.Next(6));
            _autoFireball = fireball;
            _autoDefendFrom = _bout.Time + (fireball ? 0.3 : 0.0);
            _autoDefendUntil = _bout.Time + (fireball ? _bout.Level.FireballFlight : _bout.StateLength + _bout.Level.StrikeTime) + 0.3;
        }

        void ReadAutoplay()
        {
            double now = _bout.Time;
            // the guard from the tell on; a dodge once the punch is aimed (a fireball: soon after it's thrown)
            bool defending = now <= _autoDefendUntil && now >= _autoDefendFrom
                             && (_autoDefence.Guard || _autoFireball || _bout.Committed || _bout.State is Foe.Strike or Foe.Impact);
            var want = defending ? _autoDefence : default;
            float step = 1.8f * Time.deltaTime;
            _autoHead = Vector2.MoveTowards(_autoHead, want.Head, step);
            _autoBodyX = Mathf.MoveTowards(_autoBodyX, want.BodyX, step);
            var pose = new HeroPose
            {
                Guard = want.Guard, HeadX = _autoHead.x, HeadDrop = -_autoHead.y, BodyX = _autoBodyX,
                Lean = Mathf.Abs(_autoHead.x) > 0.1f ? (_autoHead.x > 0f ? 1 : -1) : 0, Duck = -_autoHead.y > 0.12f,
            };
            if (_bout.Power >= 1f && !defending && _bout.State is Foe.Ready or Foe.Recover or Foe.Staggered) _autoSuperUntil = now + 0.35;
            if (now < _autoSuperUntil) pose.ArmsUp = true;
            if (now >= _autoNext)
            {
                bool down = _bout.Phase == BoutPhase.Down && _bout.DownFighter == 0;
                bool fighting = _bout.Phase == BoutPhase.Fighting && _bout.Hero == HeroState.Fighting;
                if (down)
                {
                    _bout.Punch(now, _autoRight, Blow.Straight, false, 4f);
                    _autoRight = !_autoRight;
                    _autoNext = now + 0.3;
                }
                else if (fighting && !defending && now >= _autoSuperUntil)
                {
                    bool open = _bout.State is Foe.Recover or Foe.Staggered or Foe.Dizzy or Foe.Taunt or Foe.Hurt or Foe.Throwing or Foe.Covering
                                || _bout.State is Foe.Ready or Foe.Blocking && _autoRng.NextDouble() < 0.5;
                    if (open)
                    {
                        // where the gloves aren't
                        var blow = _bout.State == Foe.Covering || _bout.Guard == Guard.High && _autoRng.NextDouble() < 0.5 ? Blow.Uppercut
                                 : _autoRng.NextDouble() < 0.7 ? Blow.Straight : Blow.Hook;
                        bool body = blow != Blow.Uppercut && _bout.Guard == Guard.High && _bout.State != Foe.Covering;
                        bool accepted = _bout.Phase == BoutPhase.Fighting && _bout.Hero == HeroState.Fighting;
                        _bout.Punch(now, _autoRight, blow, body, 4.5f);
                        if (accepted) _hero.Punch(_autoRight, blow, body);
                        _autoRight = !_autoRight;
                        _autoNext = now + 0.32 + 0.2 * _autoRng.NextDouble();
                    }
                }
            }
            _autoPose = pose;
            _bout.Pose(now, pose);
            _bout.SawUntil(now);
        }

        // ------------------------------------------------------------------ what happened

        void Handle(BoutEvent e)
        {
            BoutEvents?.Invoke(e);
            if (Autoplay) AutoDefend(e);
            var o = _bout.Opponent;
            switch (e.Kind)
            {
                case BoutEventKind.RoundStart:
                    Call(BoxingStrings.Round(e.Round), e.Round == _bout.Level.Rounds ? BoxingStrings.FinalRound : null, Sun, 1.2f);
                    Call(BoxingStrings.Fight, null, Coral, 1.0f, 150, 1.1f);
                    GameAudio.Sfx("box_bell", 0.85f);
                    break;
                case BoutEventKind.Thrown:
                    GameAudio.Sfx("box_swish", 0.35f, 1.15f + Random.Range(-0.1f, 0.1f));
                    break;
                case BoutEventKind.Landed:
                {
                    var at = e.Body ? _foe.ChestPosition : _foe.HeadPosition;
                    switch (e.Outcome)
                    {
                        case Outcome.Hit:
                        case Outcome.Interrupted:
                        {
                            bool big = e.Counter || e.Blow == Blow.Uppercut || e.Combo >= 3;
                            GameAudio.Sfx(big ? "box_punch_big" : "box_punch", big ? 0.9f : 0.75f, 1f + Random.Range(-0.08f, 0.08f));
                            _fx.Hit(at, big, big ? Sun : Color.white, e.CoverBroken ? "boom" : e.Counter ? "wham" : null, big ? 1.15f : 0.8f);
                            _foe.Hurt(e.Body, e.Right ? -1 : 1, big ? 1f : 0.65f);
                            if (e.Counter) PopupAt(at + Vector3.up * 0.55f, BoxingStrings.Counter, Sun);
                            else if (e.CoverBroken) PopupAt(at + Vector3.up * 0.55f, BoxingStrings.CoverBroken, Sun);
                            else if (e.Combo >= 3) PopupAt(at + Vector3.up * 0.45f, $"{e.Combo}x", Color.white);
                            _blockedInRow = 0;
                            _shake = Mathf.Max(_shake, big ? 0.22f : 0.1f);
                            _kick = Mathf.Max(_kick, big ? 1f : 0.5f);
                            if (e.Counter || e.CoverBroken) world.CheerCrowd(0.45f);
                            if (big) _flashRate = 9f;
                            break;
                        }
                        case Outcome.Blocked:
                            GameAudio.Sfx("box_block", 0.6f, 1f + Random.Range(-0.08f, 0.08f));
                            _fx.Block(at);
                            _foe.Blocked();
                            if (++_blockedInRow >= 4 && _bout.Level.Difficulty != BoxingDifficulty.Hard)
                            {
                                _blockedInRow = 0;
                                Tip(_bout.State == Foe.Covering ? BoxingStrings.CoverTip
                                    : _bout.Guard == Guard.Low ? BoxingStrings.LowGuardTip : BoxingStrings.HighGuardTip);
                            }
                            break;
                        default:
                            GameAudio.Sfx("box_swish", 0.5f, 0.9f);
                            break;
                    }
                    break;
                }
                case BoutEventKind.Covers:
                    if (!_coverTipShown && _bout.Level.Difficulty != BoxingDifficulty.Hard)
                    {
                        _coverTipShown = true;
                        Tip(BoxingStrings.CoverTip);
                    }
                    break;
                case BoutEventKind.WindUp:
                    if (Campaign.IsSpecial(e.Attack))
                    {
                        Call(BoxingStrings.Special(e.Attack), null, Coral, 0.9f, 90, 0.3f);
                        GameAudio.Sfx(e.Attack == Attack.Fireball ? "box_fire" : e.Attack == Attack.Lightning ? "box_zap" : "box_power", 0.6f, 0.8f);
                    }
                    break;
                case BoutEventKind.Strike:
                    GameAudio.Sfx("box_swish", 0.5f, 0.85f + Random.Range(-0.06f, 0.06f));
                    break;
                case BoutEventKind.Impact:
                {
                    var head = _hero.HeadPosition + Vector3.up * 0.05f;
                    switch (e.Outcome)
                    {
                        case Outcome.Hit:
                            bool power = Campaign.IsSpecial(e.Attack);
                            GameAudio.Sfx(power ? "box_punch_big" : "box_punch", 0.9f, 0.85f + Random.Range(-0.06f, 0.06f));
                            _fx.Hit(head, power, Coral, power ? "boom" : "", power ? 1f : 0.7f);
                            _hero.Hurt(e.Attack == Attack.Body, e.Side, power ? 1f : 0.7f);
                            _shake = power ? 0.7f : 0.4f;
                            _hurtFlash = power ? 0.7f : 0.45f;
                            if (power) world.CheerCrowd(0.5f);
                            if (power && _lastPose.Guard) PopupAt(head + Vector3.up * 0.4f, BoxingStrings.GuardBroken, Coral);
                            break;
                        case Outcome.Blocked:
                            GameAudio.Sfx("box_block", 0.75f);
                            _fx.Block(head + Vector3.forward * 0.3f);
                            _hero.Blocked();
                            PopupAt(head + Vector3.up * 0.3f + Vector3.forward * 0.3f, BoxingStrings.Blocked, Sky);
                            _shake = Mathf.Max(_shake, 0.12f);
                            break;
                        case Outcome.Dodged:
                            // Wii's moment: time slows, a whoosh, the opponent off balance in front of you: counter!
                            GameAudio.Sfx("whoosh", 0.8f, 1.0f);
                            _fx.Whoosh(head + Vector3.forward * 0.2f);
                            PopupAt(head + Vector3.up * 0.3f + Vector3.forward * 0.4f, BoxingStrings.Dodged, Leaf);
                            SlowMotion(0.4f, 0.55f);
                            _dodgeFlash = 1f;
                            break;
                    }
                    break;
                }
                case BoutEventKind.Dizzy:
                    PopupAt(_foe.HeadPosition + Vector3.up * 0.6f, BoxingStrings.Dizzy, Sun);
                    GameAudio.Sfx("fly_star", 0.6f, 1.3f);
                    world.CheerCrowd(0.7f);
                    break;
                case BoutEventKind.Defends:
                    GameAudio.Sfx(e.Outcome == Outcome.Missed ? "box_swish" : "box_block", 0.35f, 1.3f);
                    break;
                case BoutEventKind.Taunt:
                    GameAudio.Sfx("arcade_boing", 0.35f, 1.2f);
                    break;
                case BoutEventKind.Knockdown:
                    GameAudio.Sfx("box_down", 0.95f);
                    // the hero down: its own prompt ("get up!") takes the lower third
                    if (e.Fighter == 1) Call(BoxingStrings.Knockdown, null, Sun, 1.4f, 140);
                    _downShotAt = Time.unscaledTime;
                    if (e.Fighter == 1)
                    {
                        world.CheerCrowd(1f);
                        SlowMotion(0.35f, 1.0f);
                        _fx.Hit(_foe.HeadPosition, true, Sun, "pow", 1.4f);
                        _flashRate = 14f;
                    }
                    _shake = 0.55f;
                    _dustAt = Time.time + Bout.FallTime * 0.8f;
                    _dustFighter = e.Fighter;
                    break;
                case BoutEventKind.Count:
                    GameAudio.Sfx("box_count", 0.85f, e.Fighter == 0 ? 0.9f : 1.05f);
                    _referee.Count();
                    break;
                case BoutEventKind.GotUp:
                    if (e.Fighter == 1) world.CheerCrowd(0.4f);
                    break;
                case BoutEventKind.KO:
                case BoutEventKind.TKO:
                case BoutEventKind.Decision:
                    GameAudio.Sfx("box_bells", 0.9f);
                    GameAudio.StopMusic(0.6f);
                    _referee.Winner(e.HeroWon ? 0 : 1);
                    // the knockout's own burst over the opponent on the canvas
                    if (e.HeroWon && e.Kind != BoutEventKind.Decision)
                        _fx.Pop(_foe.HeadPosition + Vector3.up * 0.25f, e.Kind == BoutEventKind.KO ? "ko" : "boom", 0.8f, 2.2f);
                    Call(e.Kind == BoutEventKind.KO ? BoxingStrings.KO : e.Kind == BoutEventKind.TKO ? BoxingStrings.TKO
                             : e.HeroWon ? BoxingStrings.WonOnPoints : BoxingStrings.LostOnPoints,
                         e.HeroWon ? BoxingStrings.YouWon : BoxingStrings.YouLost, e.HeroWon ? Sun : Coral, 3.4f, 140, 0.4f);
                    if (e.HeroWon)
                    {
                        world.CheerCrowd(1f);
                        SlowMotion(0.3f, 1.2f);
                        _flashRate = 20f;
                        _hero.Cheer(4f);
                    }
                    break;
                case BoutEventKind.RoundEnd:
                    GameAudio.Sfx("box_bell", 0.75f);
                    break;
                case BoutEventKind.Rest:
                    Call(BoxingStrings.Rest, null, Sky, 2f, 110);
                    break;
                case BoutEventKind.PowerFull:
                    GameAudio.Sfx("box_power", 0.8f);
                    Call(BoxingStrings.PowerFull, null, Sun, 1.8f, 70, 0.5f);
                    break;
                case BoutEventKind.SuperStart:
                    GameAudio.Sfx("box_super", 1f);
                    Call(BoxingStrings.Super, null, Sun, 1.3f, 140);
                    break;
                case BoutEventKind.SuperHit:
                    GameAudio.Sfx("box_punch_big", 1f, 0.8f);
                    _fx.Hit(_foe.HeadPosition, true, Sun, "boom", 1.6f);
                    _fx.Hit(_foe.ChestPosition, true, Grape, "");
                    _foe.Hurt(false, 0, 1f);
                    _shake = 0.8f;
                    _flashRate = 16f;
                    world.CheerCrowd(1f);
                    break;
                case BoutEventKind.FireballThrown:
                    GameAudio.Sfx("box_fire", 0.8f);
                    break;
                case BoutEventKind.FireballAt:
                {
                    var head = _hero.HeadPosition;
                    if (e.Outcome == Outcome.Returned)
                    {
                        GameAudio.Sfx("fly_return", 0.9f);
                        PopupAt(head + Vector3.up * 0.4f + Vector3.forward * 0.5f, BoxingStrings.Returned, Sun);
                    }
                    else if (e.Outcome == Outcome.Hit)
                    {
                        GameAudio.Sfx("box_punch_big", 0.8f, 0.7f);
                        _fx.Hit(head, true, new Color(1f, 0.55f, 0.2f), "boom");
                        _hero.Hurt(false, 0, 1f);
                        _shake = 0.5f;
                        _hurtFlash = 0.6f;
                    }
                    else
                    {
                        GameAudio.Sfx("whoosh", 0.6f, 0.8f);
                        PopupAt(head + Vector3.up * 0.3f + Vector3.forward * 0.4f, BoxingStrings.Dodged, Leaf);
                        SlowMotion(0.45f, 0.4f);
                    }
                    break;
                }
                case BoutEventKind.FireballBack:
                    GameAudio.Sfx("box_punch_big", 1f, 0.75f);
                    _fx.Hit(_foe.HeadPosition, true, new Color(1f, 0.55f, 0.2f), "wham");
                    _foe.Hurt(false, 0, 1f);
                    break;
            }
            _lastPose = _bout.LastPose;
        }

        HeroPose _lastPose;
        float _downShotAt = -10f, _dustAt = -1f;
        int _dustFighter;

        void SlowMotion(float factor, float seconds)
        {
            _slowMo = factor;
            _slowUntil = Time.unscaledTime + seconds;
        }

        void Tip(string text)
        {
            _tip = text;
            _tipUntil = Time.time + 3.5f;
        }

        void Call(string text, string sub, Color colour, float life, int size = 120, float delay = 0f) =>
            _calls.Add((text, sub, colour, Time.time + delay, life, size));

        void PopupAt(Vector3 world, string text, Color colour) => _popups.Add(new Popup { World = world, Text = text, Color = colour });

        void UpdatePopups(float dt)
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].Age += dt;
                _popups[i].World += Vector3.up * dt * 0.35f;
                if (_popups[i].Age > 1.2f) _popups.RemoveAt(i);
            }
            for (int i = _calls.Count - 1; i >= 0; i--)
                if (Time.time - _calls[i].at > _calls[i].life) _calls.RemoveAt(i);
            _hurtFlash = Mathf.MoveTowards(_hurtFlash, 0f, dt * 1.6f);
            _dodgeFlash = Mathf.MoveTowards(_dodgeFlash, 0f, dt * 2.2f);
            if (_dustAt > 0f && Time.time >= _dustAt)
            {
                _dustAt = -1f;
                var who = _dustFighter == 1 ? _foe : _hero;
                if (who != null) _fx.Dust(who.transform.position + who.transform.forward * -0.5f);
            }
        }

        /// <summary>Cameras flashing in the crowd: now and then, a storm after a big moment.</summary>
        void CrowdFlashes(float dt)
        {
            _flashRate = Mathf.MoveTowards(_flashRate, Current == Phase.Fight ? 1.2f : 0.6f, dt * 6f);
            int n = Mathf.FloorToInt(_flashRate * dt + (float)_rng.NextDouble());
            for (int i = 0; i < n; i++)
            {
                var seat = world.RandomSeat(_rng);
                if (seat.HasValue) _fx.Flash(world.transform.TransformPoint(seat.Value), 0.35f + 0.25f * (float)_rng.NextDouble());
            }
        }

        // ------------------------------------------------------------------ the camera and the glows

        /// <summary>The camera's shots. The fight is <see cref="Shot.Fight"/> (behind the see-through hero); the rest are
        /// designed shots for the moments around it. Every side shot looks from the hero's left (-x), inside the ropes (they
        /// run at ±2.2 m: a camera beside them looked straight through one), on the side the referee doesn't stand on.</summary>
        enum Shot { Map, IntroArena, IntroFoe, IntoFight, Fight, FoeFalls, FoeCount, HeroDown, Super, Rest, KoSide, Winner }

        Shot _shot = Shot.Map;
        Vector3 _camPos;
        Quaternion _camRot;
        bool _camStarted;
        Vector2 _follow;

        /// <summary>The fight's camera (Wii Sports Boxing's): above and behind the hero's head, looking down a little at the
        /// opponent's face (m, the hero's feet at <see cref="HeroZ"/>): the opponent's face shows above the see-through
        /// hero's head, its wind-ups whole.</summary>
        public static readonly Vector3 FightCamera = new Vector3(0.3f, 2.62f, -2.95f);
        public static readonly Vector3 FightLook = new Vector3(0f, 1.12f, 0.6f);
        public const float FightFov = 44f;
        /// <summary>A knockdown stays in the fight's view this long (s, real time: the "POW!" lands), then cuts to the side.</summary>
        const float FallCut = 0.4f;

        static float Ease(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        /// <summary>The shots that start with a cut (the rest glide in from the shot before: the knockout too, from the
        /// count's side shot it looks like).</summary>
        static bool Cuts(Shot s) => s is Shot.IntroFoe or Shot.FoeFalls or Shot.HeroDown or Shot.Super;

        void UpdateCamera(float dt)
        {
            if (cam == null) return;
            var ring = Vector3.up * BoxingWorld.RingHeight;
            Shot shot;
            Vector3 pos, look;
            float fov;
            float foeZ = _foe != null ? _foe.transform.localPosition.z : FoeZ;
            float foeSize = _foe != null ? _foe.Size : 1f;
            var foeHead = (_foe != null ? _foe.HeadPosition : ring + new Vector3(0f, 1.1f, FoeZ)) - ring;
            switch (Current)
            {
                case Phase.Waiting:
                {
                    // the face-off between the map's panels: the hero from behind on the left, the opponent turned to us
                    // on the right, both whole, the camera swaying gently
                    shot = Shot.Map;
                    pos = new Vector3(4.35f, 1.75f, -1.55f + 0.2f * Mathf.Sin(Time.time * 0.15f));
                    look = new Vector3(0f, 0.8f, 0.05f);
                    fov = 38f;
                    break;
                }
                case Phase.Intro:
                {
                    float t = _phaseTime;
                    var face = foeHead + new Vector3(0f, -0.28f * foeSize, 0f);
                    var close = foeHead + new Vector3(-1.1f, 0f, -1.4f) * foeSize;
                    if (t < 1.5f)
                    {
                        // the arena: from high behind the opponent (the hero facing us) round to the ring's side
                        float u = Ease(t / 1.5f);
                        shot = Shot.IntroArena;
                        pos = Vector3.Lerp(new Vector3(-3.9f, 3.1f, 3.3f), new Vector3(-4.0f, 1.9f, -0.6f), u);
                        look = Vector3.Lerp(new Vector3(0f, 0.6f, 0f), new Vector3(0f, 0.85f, 0.05f), u);
                        fov = Mathf.Lerp(52f, 46f, u);
                    }
                    else if (t < 2.9f)
                    {
                        // the opponent close up from its front-left, over the see-through hero's shoulder (it stands right
                        // in front: no angle leaves it out), the referee far behind
                        float u = Ease((t - 1.5f) / 1.4f);
                        shot = Shot.IntroFoe;
                        pos = Vector3.Lerp(close + new Vector3(-0.2f, 0.06f, -0.25f), close, u);
                        look = face;
                        fov = 38f;
                    }
                    else
                    {
                        // into the fight's camera, behind the hero
                        float u = Ease((t - 2.9f) / Mathf.Max(0.1f, introSeconds - 2.9f));
                        shot = Shot.IntoFight;
                        pos = Vector3.Lerp(close, FightCamera, u);
                        look = Vector3.Lerp(face, FightLook, u);
                        fov = Mathf.Lerp(38f, FightFov, u);
                    }
                    break;
                }
                case Phase.Result:
                    shot = Shot.Winner;
                    WinnerShot(out pos, out look, out fov);
                    break;
                default:
                {
                    shot = Shot.Fight;
                    var pose = CurrentPose();
                    // the camera follows the hero's lean a little (a dodge shows), a punch kicks it in
                    float k = 1f - Mathf.Exp(-10f * dt);
                    _follow = Vector2.Lerp(_follow, new Vector2(pose.HeadX * 0.55f, pose.HeadDrop * 0.5f), k);
                    _kick = Mathf.MoveTowards(_kick, 0f, dt * 5f);
                    pos = FightCamera + new Vector3(_follow.x, -_follow.y, 0.06f * _kick);
                    look = FightLook + new Vector3(_follow.x * 0.35f, -_follow.y * 0.4f, 0f);
                    fov = FightFov - 1.5f * _kick - (Time.unscaledTime < _slowUntil ? 3f : 0f);
                    if (_bout == null) break;
                    float sinceDown = Time.unscaledTime - _downShotAt;
                    if (_bout.Phase == BoutPhase.Down && _bout.DownFighter == 1 && sinceDown >= FallCut)
                    {
                        if (sinceDown < FallCut + 1.6f)
                        {
                            // it goes down: low from the side as it falls, in slow motion
                            shot = Shot.FoeFalls;
                            pos = new Vector3(-1.95f, 1.15f, -0.1f);
                            look = new Vector3(0.1f, 0.4f, foeZ + 0.55f);
                            fov = 46f;
                        }
                        else
                        {
                            // the count: higher and further back, the hero standing on the right, the referee counting
                            // behind the opponent
                            shot = Shot.FoeCount;
                            pos = new Vector3(-2.1f, 1.8f, -0.35f);
                            look = new Vector3(0.35f, 0.4f, foeZ + 0.03f);
                            fov = 52f;
                        }
                    }
                    else if (_bout.Phase == BoutPhase.Down && _bout.DownFighter == 0)
                    {
                        // knocked down: from the side, the opponent on the left over the hero on the canvas, the referee
                        // counting between them
                        shot = Shot.HeroDown;
                        pos = new Vector3(-2.1f, 1.55f, -0.3f);
                        look = new Vector3(0.2f, 0.45f, -0.35f);
                        fov = 50f;
                    }
                    if (_bout.Hero == HeroState.Super)
                    {
                        // the super punch from the side: the hero rises and dives
                        shot = Shot.Super;
                        pos = new Vector3(-1.95f, 1.35f, -0.2f);
                        look = new Vector3(0f, 1.0f, 0.05f);
                        fov = 50f;
                    }
                    if (_bout.Phase == BoutPhase.Rest)
                    {
                        // between rounds: wide from the side, both boxers getting their breath
                        shot = Shot.Rest;
                        pos = new Vector3(-4.1f, 2.1f, -0.4f);
                        look = new Vector3(0f, 0.7f, 0f);
                        fov = 46f;
                    }
                    if (_bout.Phase == BoutPhase.Over && _overAt > 0f)
                    {
                        if (_bout.HeroWon && Time.time - _overAt < 1.8f)
                        {
                            // the knockout from the side (the opponent on the canvas, the referee, the hero cheering),
                            // then the winner from the front
                            shot = Shot.KoSide;
                            pos = new Vector3(-2.1f, 1.4f, -0.9f);
                            look = new Vector3(0.3f, 0.45f, foeZ - 0.12f);
                            fov = 54f;
                        }
                        else
                        {
                            shot = Shot.Winner;
                            WinnerShot(out pos, out look, out fov);
                        }
                    }
                    break;
                }
            }
            pos += ring;
            look += ring;
            // the hero see-through from behind: the fight, and from the opponent's close-up on (over its shoulder)
            Ghost(shot is Shot.Fight or Shot.IntroFoe or Shot.IntoFight);
            var rot = Quaternion.LookRotation(look - pos);
            bool cut = shot != _shot && Cuts(shot);
            if (shot != _shot)
            {
                _shot = shot;
                // the comic bursts and the popups belong to the shot they popped in
                if (cut)
                {
                    _fx.ClearBursts();
                    _popups.Clear();
                }
            }
            if (!_camStarted || cut)
            {
                _camPos = pos;
                _camRot = rot;
                cam.fieldOfView = fov;
                _camStarted = true;
            }
            float rate = shot switch
            {
                Shot.Fight => 18f,
                Shot.IntroArena or Shot.IntroFoe or Shot.IntoFight => 14f,
                Shot.FoeFalls or Shot.HeroDown or Shot.Super or Shot.KoSide => 10f,
                Shot.Map => 5f,
                _ => 3f,
            };
            float k2 = 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime);
            _camPos = Vector3.Lerp(_camPos, pos, k2);
            _camRot = Quaternion.Slerp(_camRot, rot, k2);
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 2.2f);
            var shake = _shake > 0f ? new Vector3(Mathf.Sin(Time.time * 61f), Mathf.Sin(Time.time * 53f + 1f), 0f) * 0.05f * _shake : Vector3.zero;
            cam.transform.SetPositionAndRotation(_camPos + _camRot * shake, _camRot);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, k2);
        }

        /// <summary>The fight's end and the results: the hero from the front, high on its right (an opponent still
        /// standing is beside it, under the results card; the referee behind the hero's right shoulder:
        /// <see cref="RefereeView.EndSpot"/>), the hero at 0.3-0.5 of the screen's width, left of the card.</summary>
        static void WinnerShot(out Vector3 pos, out Vector3 look, out float fov)
        {
            var hero = new Vector3(0f, 0f, HeroZ);
            pos = hero + new Vector3(1.4f, 2.05f, 2.55f);
            var at = hero + new Vector3(0f, 0.65f, 0f);
            var right = Vector3.Cross(Vector3.up, (at - pos).normalized).normalized;
            look = at + right * 0.4f;
            fov = 40f;
        }

        /// <summary>The effects drawn each frame: a power punch's glowing glove, dizzy stars, the fireball, lightning, the
        /// hero's power round its gloves, the crowd's flashes.</summary>
        void DrawGlows()
        {
            if (_fx == null) return;
            _fx.BeginFrame();
            if (_bout != null && Current == Phase.Fight)
            {
                var a = _bout.Attack;
                if (_bout.State is Foe.WindUp or Foe.Strike && a.Special && _foe != null)
                {
                    int arm = a.Side < 0 ? 1 : a.Side > 0 ? 0 : 1;
                    float k = _bout.State == Foe.WindUp ? Mathf.Clamp01(_bout.StateTime / Mathf.Max(0.05f, _bout.StateLength)) : 1f;
                    var colour = a.Attack switch
                    {
                        Attack.Lightning => new Color(0.6f, 0.9f, 1f, 0.8f),
                        Attack.Fireball => new Color(1f, 0.55f, 0.15f, 0.85f),
                        Attack.Rocket => new Color(1f, 0.6f, 0.2f, 0.8f),
                        _ => new Color(1f, 0.35f, 0.3f, 0.75f),
                    };
                    _fx.Glow(_foe.GlovePosition(arm), 0.35f + 0.45f * k + 0.05f * Mathf.Sin(Time.time * 30f), colour);
                    if (a.Attack is Attack.Sweep or Attack.Fireball) _fx.Glow(_foe.GlovePosition(1 - arm), 0.3f + 0.4f * k, colour);
                    if (a.Attack == Attack.Lightning && Random.value < 0.4f)
                        _fx.Bolt(_foe.GlovePosition(arm), _foe.GlovePosition(arm) + Vector3.up * 1.2f + Random.insideUnitSphere * 0.3f, 0.02f, new Color(0.7f, 0.95f, 1f, 0.9f));
                }
                if (_bout.State is Foe.Dizzy or Foe.Out) _fx.DizzyStars(_foe.HeadPosition, Time.time);
                if (_bout.FireballFlying && _foe != null && _hero != null)
                {
                    float u = Mathf.Clamp01((float)((_bout.Time - _bout.FireballThrown) / Math.Max(0.05, _bout.FireballArrives - _bout.FireballThrown)));
                    var from = _foe.ChestPosition + Vector3.back * 0.4f;
                    var to = _hero.HeadPosition + (Vector3)_bout.FireballAim + Vector3.forward * 0.2f;
                    var p = Vector3.Lerp(from, to, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.25f;
                    _fx.Glow(p, 0.55f + 0.06f * Mathf.Sin(Time.time * 40f), new Color(1f, 0.6f, 0.2f, 0.95f));
                    _fx.Glow(p, 0.3f, new Color(1f, 0.95f, 0.6f, 1f));
                    for (int i = 1; i <= 4; i++)
                        _fx.Glow(Vector3.Lerp(from, to, Mathf.Max(0f, u - i * 0.05f)), 0.4f - i * 0.07f, new Color(1f, 0.4f, 0.1f, 0.5f - i * 0.1f));
                }
                if (_bout.Power >= 1f && _hero != null)
                    for (int s = 0; s < 2; s++) _fx.Glow(_hero.GlovePosition(s), 0.42f + 0.08f * Mathf.Sin(Time.time * 9f + s), new Color(1f, 0.85f, 0.3f, 0.6f));
                if (_bout.Hero == HeroState.Super && _hero != null)
                {
                    _fx.Glow(_hero.GlovePosition(1), 0.9f, new Color(1f, 0.85f, 0.35f, 0.9f));
                    _fx.Glow(_hero.ChestPosition, 1.6f, new Color(1f, 0.7f, 0.3f, 0.35f));
                }
            }
            _fx.EndFrame(Time.unscaledDeltaTime);
        }
    }
}
