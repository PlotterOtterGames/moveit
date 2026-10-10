using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace MoveIt.Games.Slash
{
    /// <summary>
    /// Gray-box "Fruta": fruit is thrown up from below the screen. Swipe your hands fast through it to slice it.
    /// Slicing 3+ in one swipe is a combo. Bombs cost points (no game over, kid-friendly). 60-second rounds.
    /// Hands come from <see cref="BodyHands"/>, or the mouse when there's no camera.
    /// </summary>
    public sealed class SlashGame : MonoBehaviour
    {
        enum State { Waiting, Countdown, Playing, Results }

        sealed class Flyer
        {
            public Transform T;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float Radius;
            public bool Bomb;
            public Material Material;
        }

        sealed class Debris
        {
            public Transform T;
            public Vector3 Velocity;
            public Vector3 Spin;
            public Vector3 Scale;
            public float Age;
            public float Life;
        }

        sealed class Popup
        {
            public Vector3 World;
            public string Text;
            public Color Color;
            public float Age;
        }

        sealed class Blade
        {
            public LineRenderer Line;
            public Transform Dot;
            public readonly Queue<Vector3> Trail = new Queue<Vector3>();
            public Vector3 Previous;
            public bool HasPrevious;
            public int Combo;
            /// <summary>0 = slow hand (thin, faint blade) .. 1 = slashing (wide, bright), eased.</summary>
            public float Glow;
        }

        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField, Tooltip("Orange, apple, watermelon, fig, pomegranate, lemon (same order as the radii).")]
        Material[] fruitMaterials;
        [SerializeField] Material bombMaterial;
        [SerializeField] Material fuseMaterial;
        [SerializeField] Material bladeMaterial;
        [SerializeField] Material handMaterial;

        [Header("Round")]
        [SerializeField] float roundSeconds = 60f;
        [SerializeField, Tooltip("World units per second squared. Lower = fruit floats longer (easier).")]
        float gravity = 7f;
        [SerializeField, Tooltip("Hand speed (screen sizes per second) that counts as a slash.")]
        float slashSpeed = 1.8f;
        [SerializeField, Tooltip("Extra hit radius around each fruit, world units.")]
        float hitMargin = 0.15f;

        [Header("Spawning")]
        [SerializeField] float spawnIntervalStart = 1.3f;
        [SerializeField] float spawnIntervalEnd = 0.7f;
        [SerializeField] float bombChance = 0.15f;
        [SerializeField] float bombsAfterSeconds = 10f;

        static readonly float[] FruitRadii = { 0.5f, 0.45f, 0.85f, 0.4f, 0.5f, 0.42f };
        const float CountdownSeconds = 3f;
        const float BombRadius = 0.5f;
        const int BombPenalty = 10;
        const int TrailLength = 10;
        const string BestKey = "slash.best";

        State _state;
        float _stateTime;
        float _timeLeft;
        float _spawnTimer;
        int _score;
        int _best;
        int _sliced;
        int _bestCombo;
        bool _newBest;
        bool _paused;
        float _flash;
        float _shake;
        Vector3 _cameraHome;

        readonly List<Flyer> _flyers = new List<Flyer>();
        readonly List<Debris> _debris = new List<Debris>();
        readonly List<Popup> _popups = new List<Popup>();
        Blade _left, _right;
        BodyInput _input;
        BodyHands _hands;

        float WorldHeight => cam.orthographicSize * 2f;
        float WorldWidth => WorldHeight * cam.aspect;

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            _hands = BodyHands.Instance != null ? BodyHands.Instance : FindAnyObjectByType<BodyHands>();
            _best = PlayerPrefs.GetInt(BestKey, 0);
            _cameraHome = cam.transform.position;
            _left = CreateBlade("Left Blade");
            _right = CreateBlade("Right Blade");
            SetState(State.Waiting);
            GameAudio.PlayMusic("slash");
        }

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _stateTime += dt;

            switch (_state)
            {
                case State.Waiting:
                    if (StartPressed()) SetState(State.Countdown);
                    break;

                case State.Countdown:
                    if (_stateTime < CountdownSeconds && Mathf.FloorToInt(_stateTime) != Mathf.FloorToInt(_stateTime - dt))
                        GameAudio.Sfx("count", vary: 0f);
                    if (_stateTime >= CountdownSeconds)
                    {
                        GameAudio.Sfx("go", vary: 0f);
                        ResetRound();
                        SetState(State.Playing);
                    }
                    break;

                case State.Playing:
                    _paused = (_hands == null || !_hands.UsingMouse) && PlayerGate.ShouldPause(_input);
                    if (_paused) break;
                    _timeLeft -= dt;
                    SpawnWaves(dt);
                    MoveFlyers(dt);
                    if (_timeLeft <= 0f) EndRound();
                    break;

                case State.Results:
                    if (_stateTime > 1.5f && StartPressed()) SetState(State.Countdown);
                    break;
            }

            bool canSlice = _state == State.Playing && !_paused;
            UpdateBlade(_left, _hands != null ? _hands.Left : default, canSlice);
            UpdateBlade(_right, _hands != null ? _hands.Right : default, canSlice);
            UpdateDebris(dt);
            UpdateEffects(dt);
        }

        bool StartPressed()
        {
            var mouse = Mouse.current;
            return PlayerGate.StartPressed(_input) || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        }

        void SetState(State s)
        {
            _state = s;
            _stateTime = 0f;
            if (s == State.Countdown)
            {
                GameAudio.PlayMusic("slash");
                GameAudio.Sfx("count", vary: 0f);
            }
            Debug.Log(s == State.Results ? $"[Slash] Results score={_score} sliced={_sliced} bestCombo={_bestCombo}" : $"[Slash] {s}");
        }

        void ResetRound()
        {
            foreach (var f in _flyers) Destroy(f.T.gameObject);
            _flyers.Clear();
            _timeLeft = roundSeconds;
            _spawnTimer = 0.5f;
            _score = _sliced = _bestCombo = 0;
            _newBest = false;
            _left.Combo = _right.Combo = 0;
        }

        void EndRound()
        {
            foreach (var f in _flyers)
            {
                SpawnBurst(f.T.position, f.Bomb ? bombMaterial : f.Material, 4, 0.15f);
                Destroy(f.T.gameObject);
            }
            _flyers.Clear();
            if (_score > _best)
            {
                _best = _score;
                _newBest = true;
                PlayerPrefs.SetInt(BestKey, _best);
                PlayerPrefs.Save();
            }
            GameAudio.StopMusic(0.4f);
            GameAudio.Jingle(_newBest ? "jingle_win" : "jingle_gameover");
            SetState(State.Results);
        }

        // ---------- spawning & movement ----------

        void SpawnWaves(float dt)
        {
            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;
            float progress = 1f - _timeLeft / roundSeconds;
            _spawnTimer = Mathf.Lerp(spawnIntervalStart, spawnIntervalEnd, progress);

            int count = Random.Range(1, 2 + Mathf.Min(3, (int)(progress * 4f)));
            bool bombsAllowed = roundSeconds - _timeLeft > bombsAfterSeconds;
            bool bombPlaced = false;
            for (int i = 0; i < count; i++)
            {
                bool bomb = bombsAllowed && !bombPlaced && Random.value < bombChance;
                bombPlaced |= bomb;
                Launch(bomb);
            }
        }

        void Launch(bool bomb)
        {
            float w = WorldWidth, h = WorldHeight;
            float y0 = -h / 2f - 1f;
            float x0 = Random.Range(-0.4f, 0.4f) * w;
            float apex = Random.Range(0.05f, 0.38f) * h;
            float vy = Mathf.Sqrt(2f * gravity * (apex - y0));
            float timeToApex = vy / gravity;
            float apexX = Random.Range(-0.3f, 0.3f) * w;

            var flyer = new Flyer
            {
                Bomb = bomb,
                Velocity = new Vector3((apexX - x0) / timeToApex, vy, 0f),
                Spin = Random.insideUnitSphere * 200f,
            };
            if (bomb)
            {
                flyer.Radius = BombRadius;
                flyer.T = CreateSphere(bombMaterial, BombRadius);
                var fuse = CreateSphere(fuseMaterial, 0.12f);
                fuse.SetParent(flyer.T, false);
                fuse.localPosition = new Vector3(0f, 0.55f, 0f);
                fuse.localScale = Vector3.one * 0.25f;
            }
            else
            {
                int type = Random.Range(0, Mathf.Min(fruitMaterials.Length, FruitRadii.Length));
                flyer.Radius = FruitRadii[type];
                flyer.Material = fruitMaterials[type];
                flyer.T = CreateSphere(flyer.Material, flyer.Radius);
            }
            flyer.T.position = new Vector3(x0, y0, 0f);
            _flyers.Add(flyer);
        }

        void MoveFlyers(float dt)
        {
            float bottom = -WorldHeight / 2f - 2f;
            for (int i = _flyers.Count - 1; i >= 0; i--)
            {
                var f = _flyers[i];
                f.Velocity.y -= gravity * dt;
                f.T.position += f.Velocity * dt;
                f.T.Rotate(f.Spin * dt, Space.World);
                if (f.T.position.y < bottom && f.Velocity.y < 0f)
                {
                    Destroy(f.T.gameObject);
                    _flyers.RemoveAt(i);
                }
            }
        }

        // ---------- blades & slicing ----------

        Vector3 ZoneToWorld(Vector2 zone) =>
            new Vector3(_cameraHome.x + (zone.x - 0.5f) * WorldWidth, _cameraHome.y + (zone.y - 0.5f) * WorldHeight, -1f);

        void UpdateBlade(Blade blade, BodyHands.Hand hand, bool canSlice)
        {
            if (!hand.Tracked)
            {
                EndCombo(blade);
                blade.HasPrevious = false;
                blade.Glow = 0f;
                blade.Trail.Clear();
                blade.Line.positionCount = 0;
                blade.Dot.gameObject.SetActive(false);
                return;
            }

            var p = ZoneToWorld(hand.Position);
            blade.Dot.gameObject.SetActive(true);
            blade.Dot.position = p;

            bool slashing = hand.Speed >= slashSpeed;
            if (slashing && canSlice && blade.HasPrevious) SliceAlong(blade, blade.Previous, p);
            if (!slashing) EndCombo(blade);
            blade.Previous = p;
            blade.HasPrevious = true;

            blade.Trail.Enqueue(p);
            while (blade.Trail.Count > TrailLength) blade.Trail.Dequeue();
            blade.Line.positionCount = blade.Trail.Count;
            int i = 0;
            foreach (var q in blade.Trail) blade.Line.SetPosition(i++, q);
            // The speed changes in camera steps (30 a second): switching the blade's width on them made it flicker.
            float strength = Mathf.Clamp01(hand.Speed / slashSpeed);
            blade.Glow = Mathf.Lerp(blade.Glow, strength, 1f - Mathf.Exp(-(strength > blade.Glow ? 30f : 8f) * Time.deltaTime));
            blade.Line.startColor = new Color(1f, 1f, 1f, 0f);
            blade.Line.endColor = new Color(1f, 1f, 1f, 0.15f + 0.85f * blade.Glow);
            blade.Line.widthMultiplier = Mathf.Lerp(0.1f, 0.3f, blade.Glow * blade.Glow);
        }

        void SliceAlong(Blade blade, Vector3 from, Vector3 to)
        {
            var dir = to - from;
            dir.z = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            for (int i = _flyers.Count - 1; i >= 0; i--)
            {
                var f = _flyers[i];
                if (DistanceToSegment2D(f.T.position, from, to) > f.Radius + hitMargin) continue;
                _flyers.RemoveAt(i);
                Slice(f, dir, blade);
            }
        }

        void Slice(Flyer f, Vector3 dir, Blade blade)
        {
            var pos = f.T.position;
            Destroy(f.T.gameObject);
            if (f.Bomb)
            {
                GameAudio.Sfx("bomb", vary: 0f);
                _score = Mathf.Max(0, _score - BombPenalty);
                _flash = 0.45f;
                _shake = 0.35f;
                AddPopup(pos, $"-{BombPenalty}", new Color(1f, 0.3f, 0.3f));
                SpawnBurst(pos, bombMaterial, 14, 0.25f);
                return;
            }

            _score++;
            _sliced++;
            blade.Combo++;
            GameAudio.Sfx("slice");
            GameAudio.Sfx("splat", 0.6f, vary: 0.1f);
            AddPopup(pos, "+1", Color.white);
            var perp = new Vector3(-dir.y, dir.x, 0f);
            SpawnHalf(f, pos + perp * (f.Radius * 0.3f), perp);
            SpawnHalf(f, pos - perp * (f.Radius * 0.3f), -perp);
            SpawnBurst(pos, f.Material, 8, 0.15f);
        }

        void EndCombo(Blade blade)
        {
            if (blade.Combo >= 3)
            {
                GameAudio.Sfx($"egg_{Mathf.Min(blade.Combo, 7)}", vary: 0f);   // combo chime, higher for bigger combos
                _score += blade.Combo;
                _bestCombo = Mathf.Max(_bestCombo, blade.Combo);
                AddPopup(blade.Previous, $"{SlashStrings.Combo(blade.Combo)} +{blade.Combo}", new Color(1f, 0.9f, 0.2f));
            }
            blade.Combo = 0;
        }

        static float DistanceToSegment2D(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 ab = b - a, ap = p - a;
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, (Vector2)a + ab * t);
        }

        // ---------- effects ----------

        void SpawnHalf(Flyer f, Vector3 position, Vector3 side)
        {
            var t = CreateSphere(f.Material, f.Radius);
            t.position = position;
            var scale = new Vector3(f.Radius * 1.9f, f.Radius * 1.9f, f.Radius);
            t.localScale = scale;
            t.rotation = Quaternion.LookRotation(side);
            _debris.Add(new Debris
            {
                T = t, Scale = scale, Life = 1.2f,
                Velocity = f.Velocity * 0.4f + side * 3f + Vector3.up,
                Spin = Random.insideUnitSphere * 300f,
            });
        }

        void SpawnBurst(Vector3 position, Material material, int count, float size)
        {
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                go.transform.position = position;
                var scale = Vector3.one * size;
                go.transform.localScale = scale;
                _debris.Add(new Debris
                {
                    T = go.transform, Scale = scale, Life = 0.6f,
                    Velocity = (Vector3)(Random.insideUnitCircle * 5f) + Vector3.up * 2f,
                    Spin = Random.insideUnitSphere * 600f,
                });
            }
        }

        void UpdateDebris(float dt)
        {
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                var d = _debris[i];
                d.Age += dt;
                if (d.Age >= d.Life)
                {
                    Destroy(d.T.gameObject);
                    _debris.RemoveAt(i);
                    continue;
                }
                d.Velocity.y -= gravity * dt;
                d.T.position += d.Velocity * dt;
                d.T.Rotate(d.Spin * dt, Space.World);
                float fade = Mathf.Clamp01((d.Life - d.Age) / (d.Life * 0.4f));
                d.T.localScale = d.Scale * fade;
            }
        }

        void AddPopup(Vector3 world, string text, Color color) =>
            _popups.Add(new Popup { World = world, Text = text, Color = color });

        void UpdateEffects(float dt)
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].Age += dt;
                _popups[i].World += Vector3.up * dt;
                if (_popups[i].Age > 0.9f) _popups.RemoveAt(i);
            }
            _flash = Mathf.Max(0f, _flash - dt);
            _shake = Mathf.Max(0f, _shake - dt);
            cam.transform.position = _cameraHome + (Vector3)(Random.insideUnitCircle * (0.4f * _shake));
        }

        // ---------- objects ----------

        Blade CreateBlade(string name)
        {
            var go = new GameObject(name);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = bladeMaterial;
            line.useWorldSpace = true;
            line.numCapVertices = 4;
            line.widthCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            var dot = CreateSphere(handMaterial, 0.2f);
            dot.name = name + " Hand";
            dot.gameObject.SetActive(false);
            return new Blade { Line = line, Dot = dot };
        }

        static Transform CreateSphere(Material material, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.transform.localScale = Vector3.one * (radius * 2f);
            return go.transform;
        }

        // ---------- HUD ----------

        void OnGUI()
        {
            float w = GameHud.Begin();
            if (_flash > 0f) GameHud.Box(new Rect(0, 0, w, GameHud.Height), new Color(1f, 0f, 0f, _flash * 0.6f));

            foreach (var p in _popups)
            {
                var screen = cam.WorldToScreenPoint(p.World);
                var g = GameHud.FromScreen(screen);
                var c = p.Color;
                c.a = 1f - p.Age / 0.9f;
                GameHud.Text(new Rect(g.x - 200, g.y - 30, 400, 60), p.Text, GameHud.Medium, TextAnchor.MiddleCenter, c);
            }

            if (_state is State.Playing or State.Results)
            {
                GameHud.Text(new Rect(30, 20, 600, 60), $"{CommonStrings.Score}: {_score}", GameHud.Medium, TextAnchor.UpperLeft);
                GameHud.Text(new Rect(w - 630, 20, 600, 60), $"{CommonStrings.Best}: {_best}", GameHud.Medium, TextAnchor.UpperRight);
            }

            switch (_state)
            {
                case State.Waiting:
                    PlayerGate.DrawWaiting(w, _input, SlashStrings.Title, SlashStrings.KeyboardHelp);
                    GameHud.Text(new Rect(0, 620, w, 60), SlashStrings.HowTo, GameHud.Small);
                    break;

                case State.Countdown:
                    int n = Mathf.CeilToInt(CountdownSeconds - _stateTime);
                    GameHud.Message(w, n > 0 ? n.ToString() : CommonStrings.Go, SlashStrings.HowTo);
                    break;

                case State.Playing:
                    GameHud.Text(new Rect(0, 10, w, 110), Mathf.CeilToInt(Mathf.Max(0f, _timeLeft)).ToString(), GameHud.Big,
                        TextAnchor.UpperCenter, _timeLeft < 10f ? new Color(1f, 0.4f, 0.3f) : Color.white);
                    if (_paused) PlayerGate.DrawPaused(w, _input);
                    break;

                case State.Results:
                    GameHud.Message(w, SlashStrings.TimeUp,
                        $"{CommonStrings.Score}: {_score}    {SlashStrings.Sliced}: {_sliced}    {SlashStrings.BestCombo}: {_bestCombo}" +
                        (_newBest ? $"    {CommonStrings.NewBest}" : ""));
                    if (_stateTime > 1.5f)
                        GameHud.Text(new Rect(0, 540, w, 80),
                            PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter : CommonStrings.PlayAgain, GameHud.Small);
                    break;
            }
        }
    }
}
