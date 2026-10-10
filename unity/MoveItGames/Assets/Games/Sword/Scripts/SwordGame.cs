using System;
using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace MoveIt.Games.Sword
{
    /// <summary>
    /// "Shpata!": a sword-and-shield duel against toy knights, after Wii Sports Resort's Swordplay. The player is a
    /// see-through figure in the middle, seen from behind (<see cref="GhostAvatar"/>): the sword follows one forearm,
    /// a round shield rides on the other hand. Attack and counterattack:
    /// <list type="bullet">
    /// <item>a <b>guarding</b> knight holds its shield in front: swings bounce off (and it may strike right back);</item>
    /// <item>it <b>attacks</b> ("!") from above, the left or the right: a glowing circle shows where the blow lands.
    /// The shield there when it lands (or the sword held across it) = blocked: the knight is knocked off balance and
    /// open for a counterattack. Not blocked = a heart lost, and the knight is open for a moment while it recovers;</item>
    /// <item>sometimes it <b>lowers its shield</b>: a chance to strike first.</item>
    /// </list>
    /// Whoever runs out of health first loses. Beat a knight and a stronger one steps up (and two hearts come back).
    /// Rules live in <see cref="SwordMoves"/> (on the screen, what the player sees). Without a camera: arrows swing,
    /// space raises the shield.
    /// </summary>
    public sealed class SwordGame : MonoBehaviour
    {
        public enum Phase { Waiting, Countdown, Playing, Results }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] GhostAvatar avatar;
        [SerializeField, Tooltip("Tundu/Toon, white, flat (no world bend): knights and confetti, coloured per part.")]
        Material toonMaterial;
        [SerializeField, Tooltip("Tundu/Fx with the star texture: dizzy stars and sparks.")] Material starMaterial;
        [SerializeField, Tooltip("Tundu/Fx drawn on top of everything: the shield guide and the alert.")]
        Material markerMaterial;

        [Header("Duel")]
        [SerializeField] int startHearts = 5;
        [SerializeField, Tooltip("Hearts given back after beating a knight.")] int heartsBack = 2;
        [SerializeField, Tooltip("Metres in front of the player where knights stop.")] float fightDistance = 1.8f;
        [SerializeField, Tooltip("Knights are this much bigger than the figures they're built at (1.6 m): eye to eye with the player.")]
        float knightSize = 1.15f;
        [SerializeField] float countdownSeconds = 3f;
        [SerializeField, Tooltip("A block counts when the shield (or a parry) was in place this recently before the blow lands (s).")]
        float blockGrace = 0.25f;
        [SerializeField, Tooltip("Open after its blow was blocked: the counterattack (s).")] float staggerSeconds = 2f;
        [SerializeField, Tooltip("Open after its blow landed (s).")] float recoverSeconds = 0.9f;
        [SerializeField, Tooltip("How long a lowered shield stays down (s).")] float openingSeconds = 1.1f;
        [SerializeField, Tooltip("Size of the spot a blow lands on (radius, metres): the shield must cover it.")]
        float spotRadius = 0.3f;

        const string BestKey = "sword.best", HandKey = "sword.rightHanded";
        static readonly Color Sun = new Color(1f, 0.79f, 0.24f), Coral = new Color(1f, 0.35f, 0.31f), Leaf = new Color(0.44f, 0.84f, 0.42f);
        static readonly Color[] Armours =
        {
            new Color(0.48f, 0.38f, 1f), new Color(1f, 0.35f, 0.31f), new Color(0.19f, 0.78f, 0.96f), new Color(0.44f, 0.84f, 0.42f),
            new Color(1f, 0.62f, 0.11f),
        };

        public Phase Current { get; private set; }
        public int Hearts { get; private set; }
        public int Score { get; private set; }
        public int Best { get; private set; }
        public int KnightsBeaten { get; private set; }
        /// <summary>Which duel this is (1 = the first knight). Each one is a little stronger.</summary>
        public int Duel { get; private set; }
        public Knight CurrentKnight => _knight;
        public SwordMoves Moves { get; } = new SwordMoves();
        /// <summary>Knights only attack when told (<see cref="ForceWindUp"/>): tests, a practice mode later.</summary>
        public bool Practice { get; set; }
        public int Hits { get; private set; }
        public int Clangs { get; private set; }
        public int Blocks { get; private set; }
        public int HitsTaken { get; private set; }
        /// <summary>Every finished swing and its direction (tests, debugging).</summary>
        public event Action<Cut> SwingEnded;
        /// <summary>A knight's blow was blocked: from where, and with the shield (false = a sword parry).</summary>
        public event Action<AttackFrom, bool> AttackBlocked;
        /// <summary>The figure's chest and shoulder height on the screen (screen heights), for parries.</summary>
        public Vector2 ChestOnScreen { get; private set; }
        public float ShouldersOnScreen { get; private set; }
        /// <summary>Where the current blow will land, on the screen (the shield's target), and its radius.</summary>
        public Vector2 SpotOnScreen { get; private set; }
        public float SpotRadiusOnScreen { get; private set; }

        BodyInput _input;
        MotionDebugOverlay _mirror;
        bool _mirrorWasVisible;
        Knight _knight;
        float _phaseTime, _spawnIn, _decideIn, _lastBlockSeen = -99f, _blockHeld, _endIn = -1f, _hintUntil;
        bool _strikeResolved, _strikeBlocked, _blockWithShield, _paused, _newBest, _wasSwinging, _covered;
        int _lastHitSwing = -1, _attacksThisDuel;
        float _shake;
        Vector3 _cameraHome;
        Transform _guideDisc, _guideRing, _alert;
        MaterialPropertyBlock _markerColor;
        Material _alertMaterial;
        Texture2D _heart;
        string _toast;
        float _toastUntil;
        readonly List<Popup> _popups = new List<Popup>();
        readonly List<Bit> _bits = new List<Bit>();
        float _keyBlockUntil;
        Cut _keyCut;
        float _keyCutTime = -1f;

        sealed class Popup { public Vector3 World; public string Text; public Color Color; public float Age; }
        sealed class Bit { public Transform T; public Vector3 Velocity, Spin, Scale; public float Age, Life; public bool Gravity; }

        // ---------------------------------------------------------------- lifecycle

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            // the see-through figure is the mirror here: hide the small skeleton one, give it back to the next scene
            _mirror = MotionSystem.Instance != null ? MotionSystem.Instance.GetComponent<MotionDebugOverlay>() : null;
            if (_mirror != null)
            {
                _mirrorWasVisible = _mirror.Visible;
                _mirror.Visible = false;
            }
            Best = PlayerPrefs.GetInt(BestKey, 0);
            avatar.RightHanded = PlayerPrefs.GetInt(HandKey, 1) == 1;
            _cameraHome = cam.transform.position;
            _markerColor = new MaterialPropertyBlock();
            _heart = SwordArt.Heart();
            _guideDisc = Marker("Shield spot", SwordArt.Disc());
            _guideRing = Marker("Shield spot ring", SwordArt.Ring(0.42f));
            _alert = Marker("Alert", SwordArt.Alert());
            _alertMaterial = new Material(markerMaterial) { renderQueue = markerMaterial.renderQueue + 1 };   // over the spot
            _alert.GetComponent<Renderer>().sharedMaterial = _alertMaterial;
            SetPhase(Phase.Waiting);
            GameAudio.PlayMusic("sword");
        }

        void OnDestroy()
        {
            if (_heart != null) Destroy(_heart);
            if (_alertMaterial != null) Destroy(_alertMaterial);
            if (_mirror != null) _mirror.Visible = _mirrorWasVisible;
        }

        Transform Marker(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = markerMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _phaseTime += dt;
            HandleKeys();
            UpdateSwordAndShield(dt);

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
                        SetPhase(Phase.Playing);
                    }
                    break;
                case Phase.Playing:
                    _paused = !UsingKeys && PlayerGate.ShouldPause(_input);
                    if (!_paused) Fight(dt);
                    break;
                case Phase.Results:
                    if (_phaseTime > 1.5f && StartPressed()) SetPhase(Phase.Countdown);
                    break;
            }

            if (_knight != null) _knight.enabled = !_paused;
            UpdateMarkers();
            UpdateBits(dt);
            UpdateCamera(dt);
        }

        bool UsingKeys => _input == null || _input.UsingKeyboard || PlayerGate.State(_input) == GateState.NoService;

        bool StartPressed() => (_input != null && PlayerGate.StartPressed(_input)) || RemoteInput.Confirm;

        void SetPhase(Phase phase)
        {
            Current = phase;
            _phaseTime = 0f;
            if (phase == Phase.Countdown)
            {
                ResetRound();
                GameAudio.PlayMusic("sword");
                GameAudio.Sfx("count", vary: 0f);
            }
            Debug.Log(phase == Phase.Results
                ? $"[Sword] Results score={Score} knights={KnightsBeaten} hits={Hits} blocks={Blocks} hitsTaken={HitsTaken}"
                : $"[Sword] {phase}");
        }

        void ResetRound()
        {
            if (_knight != null) Destroy(_knight.gameObject);
            _knight = null;
            Hearts = startHearts;
            Score = KnightsBeaten = Duel = Hits = Clangs = Blocks = HitsTaken = 0;
            _spawnIn = 0.4f;
            _endIn = -1f;
            _newBest = false;
        }

        void EndRound()
        {
            if (_knight != null) Destroy(_knight.gameObject);
            _knight = null;
            if (Score > Best)
            {
                Best = Score;
                _newBest = true;
                PlayerPrefs.SetInt(BestKey, Best);
                PlayerPrefs.Save();
            }
            GameAudio.StopMusic(0.4f);
            GameAudio.Jingle(_newBest ? "jingle_win" : "jingle_gameover");
            SetPhase(Phase.Results);
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            // H, or the remote's up key (on the keyboard the arrows swing)
            if ((kb != null && kb.hKey.wasPressedThisFrame) || (!UsingKeys && RemoteInput.Up))
            {
                avatar.RightHanded = !avatar.RightHanded;
                PlayerPrefs.SetInt(HandKey, avatar.RightHanded ? 1 : 0);
                PlayerPrefs.Save();
                ShowToast(SwordStrings.HandSwapped(avatar.RightHanded));
            }
            if (kb == null || !UsingKeys) return;
            // Keyboard play (no camera): arrows swing, space raises the shield where the blow will land.
            if (kb.rightArrowKey.wasPressedThisFrame) KeySwing(Cut.Right);
            if (kb.leftArrowKey.wasPressedThisFrame) KeySwing(Cut.Left);
            if (kb.upArrowKey.wasPressedThisFrame) KeySwing(Cut.Up);
            if (kb.downArrowKey.wasPressedThisFrame) KeySwing(Cut.Down);
            if (kb.spaceKey.isPressed) _keyBlockUntil = Time.time + 0.1f;
        }

        void KeySwing(Cut cut)
        {
            _keyCut = cut;
            _keyCutTime = Time.time;
            GameAudio.Sfx("sword_swish", 0.7f);
            if (Current == Phase.Playing && _knight != null &&
                _knight.State is not (KnightState.Approach or KnightState.Strike or KnightState.Defeated or KnightState.Cheer))
                SwingReached(_knight, cut);
        }

        // ---------------------------------------------------------------- sword and shield

        Vector2 OnScreen(Vector3 world)
        {
            var v = cam.WorldToViewportPoint(world);
            return new Vector2(v.x * cam.aspect, v.y);
        }

        float RadiusOnScreen(Vector3 world, float radius) =>
            Vector2.Distance(OnScreen(world), OnScreen(world + cam.transform.right * radius));

        void UpdateSwordAndShield(float dt)
        {
            // keyboard: a quick canned swing, or the shield raised to the spot (the rules are applied directly)
            avatar.Puppet = UsingKeys ? KeySwingPuppet() : null;
            avatar.ShieldPuppet = UsingKeys && Time.time < _keyBlockUntil ? KeyShieldPuppet() : null;
            ChestOnScreen = OnScreen(avatar.ChestWorld);
            ShouldersOnScreen = OnScreen(avatar.ShoulderWorld).y;
            Moves.Update(OnScreen(avatar.HiltWorld), OnScreen(avatar.TipWorld), dt);
            Moves.UpdateShield(OnScreen(avatar.ShieldCenterWorld), RadiusOnScreen(avatar.ShieldCenterWorld, avatar.ShieldRadius));
            avatar.SetSwinging(Moves.Swinging);
            if (Moves.Swinging && !_wasSwinging) GameAudio.Sfx("sword_swish", 0.6f, 1f + Random.Range(-0.08f, 0.08f));
            if (!Moves.Swinging && _wasSwinging) SwingEnded?.Invoke(Moves.SwingCut);
            _wasSwinging = Moves.Swinging;
        }

        (Vector3, Vector3)? KeySwingPuppet()
        {
            float u = (Time.time - _keyCutTime) / 0.22f;
            if (u < 0f || u > 1f) return null;
            float side = avatar.RightHanded ? 1f : -1f;
            var dir = (Vector3)SwordMoves.Direction(_keyCut);
            var hand = new Vector3(0.1f * side, 1.35f, 0.35f) + dir * Mathf.Lerp(-0.45f, 0.45f, u);
            return (hand, hand - new Vector3(dir.y, -dir.x, 0f) * 0.15f - dir * 0.2f - new Vector3(0f, 0.1f, 0.15f));
        }

        (Vector3, Vector3)? KeyShieldPuppet()
        {
            var from = _knight != null && _knight.State is KnightState.WindUp or KnightState.Strike ? _knight.Attack : AttackFrom.Overhead;
            var hand = SpotWorld(from) - Vector3.forward * 0.07f;
            float side = avatar.RightHanded ? -1f : 1f;
            return (hand, hand + new Vector3(0.12f * side, -0.3f, -0.15f));
        }

        /// <summary>Where a blow from <paramref name="from"/> lands: over the head, or beside it on that side.</summary>
        Vector3 SpotWorld(AttackFrom from)
        {
            var head = avatar.HeadWorld;
            return from switch
            {
                AttackFrom.Overhead => head + new Vector3(0f, 0.3f, 0.35f),
                AttackFrom.Left => head + new Vector3(-0.45f, -0.12f, 0.35f),
                _ => head + new Vector3(0.45f, -0.12f, 0.35f),
            };
        }

        // ---------------------------------------------------------------- the duel

        void Fight(float dt)
        {
            if (_endIn >= 0f)
            {
                _endIn -= dt;
                if (_endIn < 0f) EndRound();
                return;
            }
            if (_knight == null)
            {
                _spawnIn -= dt;
                if (_spawnIn <= 0f) Spawn();
                return;
            }

            var k = _knight;
            switch (k.State)
            {
                case KnightState.Guard:
                    TrySwingHit(k);
                    if (!Practice && k.State == KnightState.Guard)
                    {
                        _decideIn -= dt;
                        if (_decideIn <= 0f) Decide(k);
                    }
                    break;
                case KnightState.Opening:
                    TrySwingHit(k);
                    if (k.State == KnightState.Opening && k.StateTime >= openingSeconds) BackToGuard(k);
                    break;
                case KnightState.WindUp:
                    TrySwingHit(k);
                    TrackBlock(k, dt);
                    if (k.StateTime >= k.WindUpSeconds)
                    {
                        _strikeBlocked = Time.time - _lastBlockSeen <= blockGrace;
                        _strikeResolved = false;
                        k.Enter(KnightState.Strike);
                    }
                    break;
                case KnightState.Strike:
                    if (!_strikeResolved && k.StateTime >= 0.15f)
                    {
                        _strikeResolved = true;
                        if (_strikeBlocked) BlockedBlow(k);
                        else TakeHit(k);
                    }
                    if (k.State == KnightState.Strike && k.StateTime >= 0.45f && Hearts > 0) k.Enter(KnightState.Recover);
                    break;
                case KnightState.Recover:
                    TrySwingHit(k);
                    if (k.State == KnightState.Recover && k.StateTime >= recoverSeconds) BackToGuard(k);
                    break;
                case KnightState.Staggered:
                    TrySwingHit(k);
                    if (k.State == KnightState.Staggered && k.StateTime >= staggerSeconds) BackToGuard(k);
                    break;
                case KnightState.Defeated:
                    if (k.StateTime >= 1f)
                    {
                        Destroy(k.gameObject);
                        _knight = null;
                        _spawnIn = 1.2f;
                        int before = Hearts;
                        Hearts = Mathf.Min(startHearts, Hearts + heartsBack);
                        ShowToast(Hearts > before ? $"{SwordStrings.NextKnight}  {SwordStrings.HeartsBack(Hearts - before)}" : SwordStrings.NextKnight);
                    }
                    break;
            }
        }

        // How each duel plays: the first knight is slow and only swings from above at first; later ones wind up quicker,
        // attack sooner, lower their guard less and strike back more often.
        float WindUpSeconds => Mathf.Max(0.7f, 1.6f - 0.15f * (Duel - 1));
        float GuardSeconds => Mathf.Max(0.9f, 2.2f - 0.2f * (Duel - 1)) * Random.Range(0.8f, 1.25f);
        float OpeningChance => Mathf.Max(0.15f, 0.4f - 0.05f * (Duel - 1));
        float RiposteChance => Mathf.Min(0.6f, 0.2f + 0.08f * (Duel - 1));

        void Spawn()
        {
            Duel++;
            int hp = 4 + Duel;
            var home = new Vector3(0f, 0f, fightDistance);
            _knight = Knight.Create(toonMaterial, starMaterial, Armours[(Duel - 1) % Armours.Length], home + new Vector3(0f, 0f, 9f), home, 2f, knightSize);
            _knight.Hp = _knight.MaxHp = hp;
            _decideIn = 2f;   // a moment to get ready
            _attacksThisDuel = 0;
            ShowToast(SwordStrings.Knight(Duel));
            Debug.Log($"[Sword] Knight {Duel} (hp {hp})");
        }

        void Decide(Knight k)
        {
            if (_attacksThisDuel > 0 && Random.value < OpeningChance)
            {
                k.Enter(KnightState.Opening);
                Debug.Log("[Sword] Knight lowers its shield");
                return;
            }
            WindUp(k, NextAttack(), WindUpSeconds);
        }

        AttackFrom NextAttack() => Duel == 1 && _attacksThisDuel < 2 ? AttackFrom.Overhead : (AttackFrom)Random.Range(0, 3);

        void WindUp(Knight k, AttackFrom from, float seconds)
        {
            _attacksThisDuel++;
            k.BeginWindUp(from, seconds);
            _blockHeld = 0f;
            _lastBlockSeen = -99f;
            GameAudio.Sfx("sword_alert");
            Debug.Log($"[Sword] Knight winds up: {from} ({seconds:0.0} s)");
        }

        /// <summary>Tests: the knight winds up now (if it is guarding). Returns whether it did.</summary>
        public bool ForceWindUp(AttackFrom from, float seconds)
        {
            if (_knight == null || _knight.State != KnightState.Guard) return false;
            WindUp(_knight, from, seconds);
            return true;
        }

        /// <summary>Tests: is the shield over the spot a blow from <paramref name="from"/> would land on, right now?</summary>
        public bool ShieldCoversSpot(AttackFrom from)
        {
            var spot = SpotWorld(from);
            return Moves.ShieldCovers(OnScreen(spot), RadiusOnScreen(spot, spotRadius));
        }

        void BackToGuard(Knight k)
        {
            k.Enter(KnightState.Guard);
            _decideIn = GuardSeconds;
        }

        /// <summary>The shield over the spot (or the sword held across the blow), held for a moment, counts.</summary>
        void TrackBlock(Knight k, float dt)
        {
            var spot = SpotWorld(k.Attack);
            SpotOnScreen = OnScreen(spot);
            SpotRadiusOnScreen = RadiusOnScreen(spot, spotRadius);
            bool keys = Time.time < _keyBlockUntil;
            bool shield = keys || Moves.ShieldCovers(SpotOnScreen, SpotRadiusOnScreen);
            bool parry = !shield && Moves.Parries(k.Attack, ChestOnScreen, ShouldersOnScreen);
            _covered = shield || parry;
            _blockHeld = _covered ? _blockHeld + dt : 0f;
            if (_blockHeld >= 0.06f)
            {
                _lastBlockSeen = Time.time;
                _blockWithShield = shield;
            }
        }

        void TrySwingHit(Knight k)
        {
            if (!Moves.Swinging || Moves.SwingId == _lastHitSwing) return;
            var center = OnScreen(k.HitCenter);
            if (!Moves.SweptThrough(center, RadiusOnScreen(k.HitCenter, k.HitRadius))) return;
            _lastHitSwing = Moves.SwingId;
            SwingReached(k, Moves.SwingCut);
        }

        /// <summary>A swing got to the knight: it lands if the knight is open, else it bounces off its shield.</summary>
        void SwingReached(Knight k, Cut cut)
        {
            if (k.Open) Hit(k, cut);
            else Clang(k);
        }

        void Hit(Knight k, Cut cut)
        {
            Hits++;
            k.Hp--;
            bool counter = k.State == KnightState.Staggered;
            int points = counter ? 15 : 10;
            Score += points;
            k.Recoil();
            GameAudio.Sfx("sword_hit");
            Sparks(k.HitCenter, 10, Sun);
            AddPopup(k.HitCenter + Vector3.up * 0.5f, $"{SwordStrings.Hit} +{points}", Color.white);
            Debug.Log($"[Sword] Hit {cut} ({k.State}), knight hp {k.Hp}");
            if (k.Hp <= 0) Defeat(k);
        }

        void Clang(Knight k)
        {
            Clangs++;
            k.ShieldJolt();
            GameAudio.Sfx("sword_clang", 0.7f);
            Sparks(k.ShieldWorld - Vector3.forward * 0.1f, 8, Color.white);
            if (Duel <= 2) _hintUntil = Time.time + 2.5f;   // "its shield stops you: wait for its attack"
            Debug.Log($"[Sword] Clang off the shield ({k.State})");
            // sometimes it answers at once (not while already attacking)
            if (!Practice && k.State == KnightState.Guard && _attacksThisDuel > 0 && Random.value < RiposteChance)
                WindUp(k, (AttackFrom)Random.Range(0, 3), WindUpSeconds * 0.75f);
        }

        void BlockedBlow(Knight k)
        {
            Blocks++;
            Score += 15;
            k.Flash(Sun);
            k.Enter(KnightState.Staggered);
            avatar.Flash(Sun, 0.35f);
            GameAudio.Sfx("sword_block");
            Sparks(_blockWithShield ? avatar.ShieldCenterWorld + Vector3.forward * 0.1f : Vector3.Lerp(avatar.TipWorld, avatar.HiltWorld, 0.4f), 16, Sun);
            AddPopup(k.HeadTop, $"{SwordStrings.Blocked} +15", Sun);
            Debug.Log($"[Sword] Blocked {k.Attack} with the {(_blockWithShield ? "shield" : "sword")}");
            AttackBlocked?.Invoke(k.Attack, _blockWithShield);
        }

        void TakeHit(Knight k)
        {
            HitsTaken++;
            Hearts--;
            avatar.Flash(Coral, 0.5f);
            _shake = 0.35f;
            GameAudio.Sfx("sword_hurt");
            AddPopup(avatar.HeadWorld + Vector3.up * 0.3f + Vector3.forward * 0.6f, SwordStrings.Ouch, Coral);
            Debug.Log($"[Sword] Hurt by {k.Attack}, hearts {Hearts}");
            if (Hearts > 0) return;
            k.Enter(KnightState.Cheer);
            _endIn = 2f;
        }

        void Defeat(Knight k)
        {
            KnightsBeaten++;
            int points = 100 * Duel;
            Score += points;
            k.Enter(KnightState.Defeated);
            GameAudio.Sfx("sword_poof");
            GameAudio.Jingle("jingle_win", 0.3f);
            Confetti(k.HitCenter, 40);
            AddPopup(k.HeadTop, $"{SwordStrings.Victory} +{points}", Sun);
            Debug.Log($"[Sword] Knight {Duel} defeated, score {Score}");
        }

        // ---------------------------------------------------------------- markers (shield spot, alert)

        void UpdateMarkers()
        {
            var k = Current == Phase.Playing && !_paused ? _knight : null;
            var camRot = cam.transform.rotation;
            bool winding = k != null && k.State == KnightState.WindUp;
            _alert.gameObject.SetActive(winding);
            _guideDisc.gameObject.SetActive(winding);
            _guideRing.gameObject.SetActive(winding);
            if (!winding) return;

            _alert.SetPositionAndRotation(k.HeadTop + Vector3.up * (0.35f + 0.06f * Mathf.Abs(Mathf.Sin(Time.time * 10f))), camRot);
            _alert.localScale = Vector3.one * 0.45f;
            Tint(_alert, Coral);
            // the spot the blow lands on: a ring that tightens as the blow comes, green while the shield is on it
            var spot = SpotWorld(k.Attack);
            float closing = Mathf.Clamp01(k.StateTime / Mathf.Max(0.1f, k.WindUpSeconds));
            float size = spotRadius * 2f * Mathf.Lerp(1.6f, 1f, closing);
            _guideRing.SetPositionAndRotation(spot, camRot);
            _guideRing.localScale = Vector3.one * size;
            _guideDisc.SetPositionAndRotation(spot, camRot);
            _guideDisc.localScale = Vector3.one * spotRadius * 2f;
            var c = _covered ? Leaf : Color.white;
            Tint(_guideRing, new Color(c.r, c.g, c.b, 0.95f));
            Tint(_guideDisc, new Color(c.r, c.g, c.b, _covered ? 0.3f : 0.1f + 0.06f * Mathf.Sin(Time.time * 12f)));
        }

        void Tint(Transform marker, Color c)
        {
            _markerColor.SetColor("_BaseColor", c);
            marker.GetComponent<Renderer>().SetPropertyBlock(_markerColor);
        }

        // ---------------------------------------------------------------- effects

        void Sparks(Vector3 at, int count, Color color)
        {
            for (int i = 0; i < count; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                var r = quad.GetComponent<Renderer>();
                r.sharedMaterial = starMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", color);
                r.SetPropertyBlock(block);
                quad.transform.position = at;
                float size = Random.Range(0.08f, 0.16f);
                _bits.Add(new Bit
                {
                    T = quad.transform, Scale = Vector3.one * size, Life = Random.Range(0.25f, 0.45f),
                    Velocity = Random.onUnitSphere * Random.Range(2f, 4f), Spin = new Vector3(0f, 0f, Random.Range(-720f, 720f)),
                });
            }
        }

        void Confetti(Vector3 at, int count)
        {
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < count; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                var r = cube.GetComponent<Renderer>();
                r.sharedMaterial = toonMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                block.SetColor("_BaseColor", Armours[Random.Range(0, Armours.Length)]);
                r.SetPropertyBlock(block);
                cube.transform.position = at + Random.insideUnitSphere * 0.3f;
                var scale = new Vector3(Random.Range(0.06f, 0.12f), Random.Range(0.06f, 0.12f), 0.015f);
                cube.transform.localScale = scale;
                _bits.Add(new Bit
                {
                    T = cube.transform, Scale = scale, Life = Random.Range(0.9f, 1.4f), Gravity = true,
                    Velocity = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(2f, 5f), Random.Range(-1f, 1.5f)),
                    Spin = Random.insideUnitSphere * 600f,
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
                if (b.Gravity) b.Velocity += Vector3.down * 7f * dt;
                else b.Velocity *= Mathf.Exp(-4f * dt);
                b.T.position += b.Velocity * dt;
                if (b.Gravity) b.T.Rotate(b.Spin * dt, Space.World);
                else b.T.rotation = camRot * Quaternion.Euler(0f, 0f, b.Spin.z * b.Age);
                b.T.localScale = b.Scale * Mathf.Clamp01((b.Life - b.Age) / (b.Life * 0.4f));
            }
        }

        void UpdateCamera(float dt)
        {
            _shake = Mathf.Max(0f, _shake - dt);
            cam.transform.position = _cameraHome + (Vector3)(Random.insideUnitCircle * (0.12f * _shake));
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].Age += dt;
                _popups[i].World += Vector3.up * (0.6f * dt);
                if (_popups[i].Age > 1f) _popups.RemoveAt(i);
            }
        }

        void AddPopup(Vector3 world, string text, Color color) =>
            _popups.Add(new Popup { World = world, Text = text, Color = color });

        void ShowToast(string text)
        {
            _toast = text;
            _toastUntil = Time.time + 2f;
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

            if (Current is Phase.Playing or Phase.Results)
            {
                // the player's hearts and score (left), the knight's health (middle), the best (right)
                for (int i = 0; i < startHearts; i++)
                {
                    GUI.color = i < Hearts ? Coral : new Color(1f, 1f, 1f, 0.25f);
                    GUI.DrawTexture(new Rect(30 + i * 70, 24, 62, 62), _heart);
                }
                GUI.color = Color.white;
                GameHud.Text(new Rect(30, 96, 600, 60), $"{CommonStrings.Score}: {Score}", GameHud.Medium, TextAnchor.UpperLeft);
                GameHud.Text(new Rect(w - 630, 20, 600, 60), $"{CommonStrings.Best}: {Best}", GameHud.Medium, TextAnchor.UpperRight);
                if (_knight != null && Current == Phase.Playing) DrawKnightHealth(w, _knight);
            }

            switch (Current)
            {
                case Phase.Waiting:
                    PlayerGate.DrawWaiting(w, _input, SwordStrings.Title, SwordStrings.KeyboardHelp);
                    GameHud.Text(new Rect(0, 620, w, 60), SwordStrings.HowTo, GameHud.Small);
                    GameHud.Text(new Rect(0, 680, w, 50), SwordStrings.SwapHint, GameHud.Small, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
                    break;
                case Phase.Countdown:
                    int n = Mathf.CeilToInt(countdownSeconds - _phaseTime);
                    GameHud.Message(w, n > 0 ? n.ToString() : CommonStrings.Go, SwordStrings.HowTo);
                    break;
                case Phase.Playing:
                    if (_paused) PlayerGate.DrawPaused(w, _input);
                    else if (_knight != null && _knight.Arrived)
                    {
                        string hint = _knight.State switch
                        {
                            KnightState.WindUp => SwordStrings.ShieldHere,
                            KnightState.Staggered or KnightState.Recover => SwordStrings.HitNow,
                            KnightState.Opening => SwordStrings.OpenGuard,
                            KnightState.Guard when Time.time < _hintUntil => SwordStrings.WaitForAttack,
                            _ => null,
                        };
                        if (hint != null) GameHud.Text(new Rect(0, 170, w, 70), hint, GameHud.Medium, TextAnchor.MiddleCenter, Sun);
                    }
                    break;
                case Phase.Results:
                    GameHud.Message(w, CommonStrings.GameOver,
                        $"{CommonStrings.Score}: {Score}    {SwordStrings.KnightsBeaten}: {KnightsBeaten}" + (_newBest ? $"    {CommonStrings.NewBest}" : ""));
                    if (_phaseTime > 1.5f)
                        GameHud.Text(new Rect(0, 540, w, 80),
                            PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter : CommonStrings.PlayAgain, GameHud.Small);
                    break;
            }

            if (_toast != null && Time.time < _toastUntil)
                GameHud.Text(new Rect(0, 250, w, 70), _toast, GameHud.Medium, TextAnchor.MiddleCenter, Color.white);
        }

        void DrawKnightHealth(float w, Knight k)
        {
            const float barW = 560f, barH = 30f;
            float x = (w - barW) / 2f, y = 72f;
            GameHud.Text(new Rect(x, 18, barW, 50), SwordStrings.Knight(Duel), GameHud.Small, TextAnchor.MiddleCenter);
            GameHud.Box(new Rect(x - 4, y - 4, barW + 8, barH + 8), new Color(0f, 0f, 0f, 0.55f));
            float fill = k.MaxHp > 0 ? Mathf.Clamp01((float)k.Hp / k.MaxHp) : 0f;
            GameHud.Box(new Rect(x, y, barW * fill, barH), Armours[(Duel - 1) % Armours.Length]);
            for (int i = 1; i < k.MaxHp; i++)   // one notch per hit
                GameHud.Box(new Rect(x + barW * i / k.MaxHp - 1f, y, 2f, barH), new Color(0f, 0f, 0f, 0.35f));
        }
    }
}
