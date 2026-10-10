using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games.Runner
{
    /// <summary>
    /// Gray-box endless runner. Step left/right to change lanes, jump over low barriers, crouch under high
    /// bars, avoid blocks, collect coins. The player stays at z = 0 and the world moves toward the camera.
    /// </summary>
    public sealed class RunnerGame : MonoBehaviour
    {
        enum State { Waiting, Countdown, Running, GameOver }
        enum Kind { LowBarrier, HighBarrier, Block, Coin }

        sealed class Item
        {
            public Kind Kind;
            public int Lane;
            public Transform Root;
            public float Depth;
            public bool Done;
        }

        [Header("Scene")]
        [SerializeField] RunnerPlayer player;
        [SerializeField] Transform cameraRig;
        [SerializeField] Material lowBarrierMaterial;
        [SerializeField] Material highBarrierMaterial;
        [SerializeField] Material blockMaterial;
        [SerializeField] Material coinMaterial;
        [SerializeField] Material groundMaterialA;
        [SerializeField] Material groundMaterialB;

        [Header("Dino world art (optional: gray boxes without it)")]
        [SerializeField, Tooltip("The one palette material every art model uses.")]
        Material worldMaterial;
        [SerializeField] GameObject lowBarrierPrefab;
        [SerializeField] GameObject highBarrierPrefab;
        [SerializeField] GameObject blockPrefab;
        [SerializeField] GameObject coinPrefab;
        [SerializeField, Tooltip("Ferns, trees, rocks: scattered beside the track, reshuffled as the ground loops.")]
        GameObject[] sceneryPrefabs;
        [SerializeField, Tooltip("Tall trees: kept farther from the track so they never hide an obstacle.")]
        GameObject[] treePrefabs;
        [SerializeField] Material sideGroundMaterial;
        [SerializeField, Tooltip("Lane lines, built per ground tile so they bend with the curved world.")]
        Material laneMaterial;

        [Header("Look & feel")]
        [SerializeField] RunnerFx fx;
        [SerializeField, Tooltip("Curved world: metres the ground drops per metre² ahead (0 = flat). Far things rise over the horizon.")]
        float worldCurve = 0.0009f;
        [SerializeField, Tooltip("Extra field of view at top speed, degrees: speed you can feel.")]
        float speedFov = 9f;
        [SerializeField, Range(0, 8)] int sceneryPerTileSide = 3;
        [SerializeField, Range(0, 4)] int treesPerTileSide = 1;

        [Header("Track")]
        [SerializeField] float laneWidth = 2.2f;
        [SerializeField] float spawnDistance = 90f;
        [SerializeField] float groundTileLength = 10f;
        [SerializeField] float firstRowDistance = 45f;

        [Header("Speed (m/s)")]
        [SerializeField] float startSpeed = 9f;
        [SerializeField] float maxSpeed = 20f;
        [SerializeField, Tooltip("Speed gained per second of running.")]
        float acceleration = 0.12f;

        [Header("Difficulty")]
        [SerializeField, Tooltip("Seconds of running between obstacle rows.")]
        float rowInterval = 1.7f;
        [SerializeField] float minRowGap = 14f;
        [SerializeField, Tooltip("Only easy patterns during the first seconds of a run.")]
        float easySeconds = 20f;
        [SerializeField, Tooltip("Show JUMP / DUCK / MOVE ASIDE hints during the first seconds of a run.")]
        float hintSeconds = 40f;

        const float LowBarrierHeight = 0.7f;
        const float HighBarrierBottom = 1.15f;
        const float BlockHeight = 2.8f;
        const float CountdownSeconds = 3f;
        const string BestKey = "runner.best";

        State _state;
        float _stateTime;
        float _speed;
        float _distance;
        float _runTime;
        float _nextRowZ;
        int _coins;
        int _eggStreak;
        float _lastEggTime = -10f;
        int _best;
        bool _newBest;
        bool _paused;

        readonly List<Item> _items = new List<Item>();
        readonly Dictionary<Kind, Stack<Item>> _pool = new Dictionary<Kind, Stack<Item>>();
        readonly List<Transform> _groundTiles = new List<Transform>();

        BodyInput _input;
        Camera _cam;
        float _baseFov = 60f;
        Vector3 _camHome;
        float _shake;

        struct Popup
        {
            public Vector3 Position;
            public string Text;
            public float Born;
        }

        readonly List<Popup> _popups = new List<Popup>();

        int Score => Mathf.FloorToInt(_distance) + _coins * 10;

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            _best = PlayerPrefs.GetInt(BestKey, 0);
            player.LaneWidth = laneWidth;
            Shader.SetGlobalFloat("_TunduCurve", worldCurve);
            _cam = cameraRig != null ? cameraRig.GetComponentInChildren<Camera>() : Camera.main;
            if (_cam != null)
            {
                _baseFov = _cam.fieldOfView;
                _camHome = _cam.transform.localPosition;
            }
            if (fx != null) fx.Setup(player.transform, _cam);
            BuildGround();
            ResetRun();
            SetState(State.Waiting);
            GameAudio.PlayMusic("runner");
        }

        void OnDestroy() => Shader.SetGlobalFloat("_TunduCurve", 0f);   // other scenes stay flat

        void Update()
        {
            if (GameNavigation.HandleCommonKeys()) return;
            float dt = Time.deltaTime;
            _stateTime += dt;

            switch (_state)
            {
                case State.Waiting:
                    if (PlayerGate.StartPressed(_input)) SetState(State.Countdown);
                    break;

                case State.Countdown:
                    if (_stateTime < CountdownSeconds && Mathf.FloorToInt(_stateTime) != Mathf.FloorToInt(_stateTime - dt))
                        GameAudio.Sfx("count", vary: 0f);
                    if (_stateTime >= CountdownSeconds)
                    {
                        _input.Gestures?.Recenter();
                        _input.ResetLane();
                        SetState(State.Running);
                    }
                    break;

                case State.Running:
                    _paused = PlayerGate.ShouldPause(_input);
                    if (!_paused) Run(dt);
                    break;

                case State.GameOver:
                    if (_stateTime > 0.6f && _stateTime - dt <= 0.6f) GameAudio.Jingle(_newBest ? "jingle_win" : "jingle_gameover");
                    if (_newBest && _stateTime > 1.2f && _stateTime - dt <= 1.2f) player.Show(RunnerPlayer.Mood.Cheering, faceCamera: true);
                    if (_stateTime > 1.2f && PlayerGate.StartPressed(_input))
                    {
                        ResetRun();
                        SetState(State.Countdown);
                    }
                    break;
            }

            float speedFraction = _state == State.Running ? Mathf.InverseLerp(startSpeed, maxSpeed, _speed) : 0f;
            if (cameraRig != null)
            {
                var p = cameraRig.position;
                p.x = Mathf.Lerp(p.x, player.X * 0.5f, 1f - Mathf.Exp(-6f * dt));
                p.y = Mathf.Lerp(p.y, player.FeetY * 0.3f, 1f - Mathf.Exp(-5f * dt));     // rise a little with jumps
                cameraRig.position = p;
            }
            if (_cam != null)
            {
                _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, _baseFov + speedFov * speedFraction, 1f - Mathf.Exp(-2f * dt));
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 1.5f);
                _cam.transform.localPosition = _camHome + Random.insideUnitSphere * (_shake * _shake * 0.35f);
            }
            if (fx != null)
            {
                bool running = _state == State.Running && !_paused && !player.InAir;
                fx.Tick(dt, running, _state == State.Running && !_paused ? _speed : 0f, speedFraction, player.FeetY);
            }
        }

        void Run(float dt)
        {
            _runTime += dt;
            _speed = Mathf.Min(maxSpeed, startSpeed + acceleration * _runTime);
            float move = _speed * dt;
            _distance += move;
            player.RunSpeed = _speed;

            player.Tick(dt, _input.Lane, _input.JumpTriggered, _input.Crouching);

            foreach (var item in _items)
            {
                item.Root.position += Vector3.back * move;
                if (item.Kind == Kind.Coin && item.Root.childCount > 0)
                {
                    item.Root.Rotate(0f, 180f * dt, 0f, Space.World);
                    var egg = item.Root.GetChild(0);
                    egg.localPosition = new Vector3(0f, 1f + 0.12f * Mathf.Sin(Time.time * 4f + item.Root.position.z * 0.3f), 0f);
                }
            }
            MoveGround(move);

            _nextRowZ -= move;
            while (_nextRowZ < spawnDistance)
            {
                SpawnRow(_nextRowZ);
                _nextRowZ += Mathf.Max(minRowGap, _speed * rowInterval);
            }

            CheckCollisions();
            Despawn();
        }

        // ---------- state ----------

        void SetState(State s)
        {
            _state = s;
            _stateTime = 0f;
            switch (s)
            {
                case State.Waiting: player.Show(RunnerPlayer.Mood.Idle, faceCamera: true); break;
                case State.Countdown:
                    player.Show(RunnerPlayer.Mood.Idle);
                    if (fx != null) fx.Dizzy(false, Vector3.zero);
                    GameAudio.PlayMusic("runner");
                    GameAudio.Sfx("count", vary: 0f);
                    break;
                case State.Running: GameAudio.Sfx("go", vary: 0f); break;
                case State.GameOver:
                    player.Show(RunnerPlayer.Mood.Crashed);
                    _shake = 1f;
                    if (fx != null)
                    {
                        fx.Crash(player.transform.position + new Vector3(0f, 1f, 0.6f));
                        fx.Dizzy(true, player.transform.position + new Vector3(0f, 2.05f, 0.1f));
                    }
                    GameAudio.Sfx("crash", vary: 0f);
                    GameAudio.StopMusic(0.4f);
                    break;
            }
            Debug.Log(s == State.GameOver ? $"[Runner] GameOver score={Score} coins={_coins}" : $"[Runner] {s}");
        }

        void ResetRun()
        {
            foreach (var item in _items) Release(item);
            _items.Clear();
            _speed = startSpeed;
            _distance = 0f;
            _runTime = 0f;
            _coins = 0;
            _newBest = false;
            _paused = false;
            _nextRowZ = firstRowDistance;
            player.ResetPlayer();
            _input?.ResetLane();
        }

        void Crash()
        {
            if (Score > _best)
            {
                _best = Score;
                _newBest = true;
                PlayerPrefs.SetInt(BestKey, _best);
                PlayerPrefs.Save();
            }
            SetState(State.GameOver);
        }

        // ---------- spawning ----------

        void SpawnRow(float z)
        {
            int free = Random.Range(-1, 2);
            int pattern = _runTime < easySeconds ? Random.Range(0, 4) : Random.Range(0, 7);
            switch (pattern)
            {
                case 0: // one block, coins in a free lane
                    Spawn(Kind.Block, OtherLane(free), z);
                    SpawnCoins(free, z);
                    break;
                case 1: // low barrier across the track: jump
                    for (int lane = -1; lane <= 1; lane++) Spawn(Kind.LowBarrier, lane, z);
                    break;
                case 2: // high bar across the track: duck
                    for (int lane = -1; lane <= 1; lane++) Spawn(Kind.HighBarrier, lane, z);
                    break;
                case 3: // breather
                    SpawnCoins(free, z);
                    break;
                case 4: // two blocks: find the free lane
                    for (int lane = -1; lane <= 1; lane++)
                        if (lane != free) Spawn(Kind.Block, lane, z);
                    SpawnCoins(free, z);
                    break;
                default: // block + barrier + free lane
                {
                    int a = OtherLane(free);
                    int b = -free - a;  // the remaining lane (lanes are -1, 0, 1)
                    Spawn(Kind.Block, a, z);
                    Spawn(pattern == 5 ? Kind.LowBarrier : Kind.HighBarrier, b, z);
                    SpawnCoins(free, z);
                    break;
                }
            }
        }

        static int OtherLane(int lane)
        {
            int other = Random.Range(-1, 1);  // -1 or 0
            return other >= lane ? other + 1 : other;
        }

        void SpawnCoins(int lane, float z)
        {
            for (int i = 0; i < 5; i++) Spawn(Kind.Coin, lane, z + i * 2.5f);
        }

        void Spawn(Kind kind, int lane, float z)
        {
            if (!_pool.TryGetValue(kind, out var stack)) _pool[kind] = stack = new Stack<Item>();
            var item = stack.Count > 0 ? stack.Pop() : CreateItem(kind);
            item.Lane = lane;
            item.Done = false;
            item.Root.SetPositionAndRotation(new Vector3(lane * laneWidth, 0f, z + item.Depth / 2f), Quaternion.identity);
            item.Root.gameObject.SetActive(true);
            _items.Add(item);
        }

        Item CreateItem(Kind kind)
        {
            var root = new GameObject(kind.ToString()).transform;
            root.SetParent(transform, false);
            float w = laneWidth * 0.9f;
            var item = new Item { Kind = kind, Root = root };
            var prefab = kind switch
            {
                Kind.LowBarrier => lowBarrierPrefab,
                Kind.HighBarrier => highBarrierPrefab,
                Kind.Block => blockPrefab,
                _ => coinPrefab,
            };
            if (prefab != null)
            {
                // Art sized to the same hitboxes as the gray boxes below (art/runner/world.py)
                item.Depth = kind == Kind.Block ? 6f : kind == Kind.Coin ? 0.6f : 0.4f;
                AddArt(prefab, root, kind == Kind.Coin ? new Vector3(0f, 1f, 0f) : Vector3.zero);
                return item;
            }
            switch (kind)
            {
                case Kind.LowBarrier:
                    item.Depth = 0.4f;
                    AddBox(root, lowBarrierMaterial, new Vector3(0f, LowBarrierHeight / 2f, 0f), new Vector3(w, LowBarrierHeight, 0.4f));
                    break;
                case Kind.HighBarrier:
                    item.Depth = 0.4f;
                    AddBox(root, highBarrierMaterial, new Vector3(0f, HighBarrierBottom + 0.25f, 0f), new Vector3(w, 0.5f, 0.4f));
                    AddBox(root, highBarrierMaterial, new Vector3(-w / 2f + 0.08f, HighBarrierBottom / 2f, 0f), new Vector3(0.16f, HighBarrierBottom, 0.3f));
                    AddBox(root, highBarrierMaterial, new Vector3(w / 2f - 0.08f, HighBarrierBottom / 2f, 0f), new Vector3(0.16f, HighBarrierBottom, 0.3f));
                    break;
                case Kind.Block:
                    item.Depth = 6f;
                    AddBox(root, blockMaterial, new Vector3(0f, BlockHeight / 2f, 0f), new Vector3(w, BlockHeight, 6f));
                    break;
                case Kind.Coin:
                    item.Depth = 0.6f;
                    var coin = AddPrimitive(PrimitiveType.Cylinder, root, coinMaterial, new Vector3(0f, 1f, 0f), new Vector3(0.7f, 0.05f, 0.7f));
                    coin.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    break;
            }
            return item;
        }

        Transform AddArt(GameObject prefab, Transform parent, Vector3 position)
        {
            var t = Instantiate(prefab, parent, false).transform;
            t.localPosition = position;
            if (worldMaterial != null)
                foreach (var r in t.GetComponentsInChildren<Renderer>()) r.sharedMaterial = worldMaterial;
            return t;
        }

        static Transform AddBox(Transform parent, Material material, Vector3 position, Vector3 size) =>
            AddPrimitive(PrimitiveType.Cube, parent, material, position, size);

        static Transform AddPrimitive(PrimitiveType type, Transform parent, Material material, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());  // collisions are checked in code
            go.GetComponent<Renderer>().sharedMaterial = material;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localScale = scale;
            return t;
        }

        void Despawn()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (item.Root.position.z + item.Depth / 2f < -8f || (item.Done && item.Kind == Kind.Coin))
                {
                    Release(item);
                    _items.RemoveAt(i);
                }
            }
        }

        void Release(Item item)
        {
            item.Root.gameObject.SetActive(false);
            _pool[item.Kind].Push(item);
        }

        // ---------- ground ----------

        void BuildGround()
        {
            int count = Mathf.CeilToInt((spawnDistance + 30f) / groundTileLength);
            float pathWidth = laneWidth * 3f + 1f;
            for (int i = 0; i < count; i++)
            {
                // An unscaled root per tile: the path, the grass beside it and the scenery all move together.
                var tile = new GameObject("Ground").transform;
                tile.SetParent(transform, false);
                tile.position = new Vector3(0f, 0f, -15f + groundTileLength * (i + 0.5f));
                AddBox(tile, i % 2 == 0 ? groundMaterialA : groundMaterialB, new Vector3(0f, -0.1f, 0f),
                    new Vector3(pathWidth, 0.2f, groundTileLength));
                if (laneMaterial != null)
                    foreach (float x in new[] { -laneWidth / 2f, laneWidth / 2f })
                        AddBox(tile, laneMaterial, new Vector3(x, 0.006f, 0f), new Vector3(0.09f, 0.02f, groundTileLength));
                if (sideGroundMaterial != null)
                    foreach (float side in new[] { -1f, 1f })
                        AddBox(tile, sideGroundMaterial, new Vector3(side * (pathWidth / 2f + 30f), -0.12f, 0f),
                            new Vector3(60f, 0.2f, groundTileLength));
                if (sceneryPrefabs != null && sceneryPrefabs.Length > 0)
                    for (int k = 0; k < sceneryPerTileSide * 2; k++)
                        AddArt(sceneryPrefabs[Random.Range(0, sceneryPrefabs.Length)], tile, Vector3.zero).name = "Scenery";
                if (treePrefabs != null && treePrefabs.Length > 0)
                    for (int k = 0; k < treesPerTileSide * 2; k++)
                        AddArt(treePrefabs[Random.Range(0, treePrefabs.Length)], tile, Vector3.zero).name = "Tree";
                ScatterScenery(tile);
                _groundTiles.Add(tile);
            }
        }

        /// <summary>Places a tile's scenery at new random spots beside the track (called again each time it loops).</summary>
        void ScatterScenery(Transform tile)
        {
            float edge = laneWidth * 1.5f + 1.2f;
            int k = 0;
            foreach (Transform child in tile)
            {
                bool tree = child.name == "Tree";
                if (!tree && child.name != "Scenery") continue;
                float side = k++ % 2 == 0 ? -1f : 1f;
                float x = side * (edge + (tree ? 3.5f : 0f) + Random.Range(0f, 1f) * Random.Range(0f, 14f));
                child.localPosition = new Vector3(x, 0f, Random.Range(-groundTileLength / 2f, groundTileLength / 2f));
                child.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                child.localScale = Vector3.one * Random.Range(0.8f, 1.25f);
            }
        }

        void MoveGround(float move)
        {
            float span = _groundTiles.Count * groundTileLength;
            foreach (var tile in _groundTiles)
            {
                var p = tile.position;
                p.z -= move;
                if (p.z + groundTileLength / 2f < -15f)
                {
                    p.z += span;
                    ScatterScenery(tile);
                }
                tile.position = p;
            }
        }

        // ---------- collisions ----------

        void CheckCollisions()
        {
            float feet = player.FeetY;
            float top = feet + player.Height;
            foreach (var item in _items)
            {
                if (item.Done) continue;
                if (Mathf.Abs(item.Root.position.z) > item.Depth / 2f + 0.3f) continue;
                // Slightly forgiving width so brushing a corner mid lane-change doesn't kill you.
                if (Mathf.Abs(player.X - item.Lane * laneWidth) > laneWidth * 0.45f + RunnerPlayer.HalfWidth - 0.2f) continue;

                bool hit;
                switch (item.Kind)
                {
                    case Kind.Coin:
                        if (feet < 1.3f && top > 0.7f)
                        {
                            item.Done = true;
                            _coins++;
                            // eggs in a row climb a scale: do-re-mi... (resets after a short gap)
                            _eggStreak = Time.time - _lastEggTime < 0.8f ? _eggStreak + 1 : 0;
                            _lastEggTime = Time.time;
                            GameAudio.Sfx($"egg_{Mathf.Min(_eggStreak, 7)}", vary: 0f);
                            var at = item.Root.position + Vector3.up;
                            if (fx != null) fx.Egg(at);
                            _popups.Add(new Popup { Position = at, Text = "+10", Born = Time.time });
                        }
                        continue;
                    case Kind.LowBarrier: hit = feet < LowBarrierHeight - 0.05f; break;
                    case Kind.HighBarrier: hit = top > HighBarrierBottom; break;
                    default: hit = true; break;
                }
                if (hit)
                {
                    Crash();
                    return;
                }
            }
        }

        // ---------- HUD ----------

        string CurrentHint()
        {
            if (_runTime > hintSeconds) return null;
            int lane = Mathf.RoundToInt(player.X / laneWidth);
            Item nearest = null;
            foreach (var item in _items)
            {
                if (item.Kind == Kind.Coin || item.Lane != lane) continue;
                float z = item.Root.position.z - item.Depth / 2f;
                if (z < 0f || z / _speed > 1.4f) continue;
                if (nearest == null || z < nearest.Root.position.z - nearest.Depth / 2f) nearest = item;
            }
            if (nearest == null) return null;
            return nearest.Kind == Kind.LowBarrier ? RunnerStrings.Jump
                : nearest.Kind == Kind.HighBarrier ? RunnerStrings.Duck
                : RunnerStrings.Switch;
        }

        /// <summary>"+10" floating up from each collected egg.</summary>
        void DrawPopups()
        {
            if (_cam == null) return;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                float age = Time.time - _popups[i].Born;
                if (age > 0.8f)
                {
                    _popups.RemoveAt(i);
                    continue;
                }
                var screen = _cam.WorldToScreenPoint(_popups[i].Position + Vector3.up * (age * 1.6f));
                if (screen.z <= 0f) continue;
                var gui = GameHud.FromScreen(screen);
                var color = new Color(1f, 0.85f, 0.25f, 1f - age / 0.8f);
                GameHud.Text(new Rect(gui.x - 100, gui.y - 30, 200, 60), _popups[i].Text, GameHud.Medium, TextAnchor.MiddleCenter, color);
            }
        }

        void OnGUI()
        {
            float w = GameHud.Begin();
            GameHud.Text(new Rect(30, 20, 600, 60), $"{CommonStrings.Score}: {Score}", GameHud.Medium, TextAnchor.UpperLeft);
            GameHud.Text(new Rect(30, 80, 600, 60), $"{RunnerStrings.Coins}: {_coins}", GameHud.Medium, TextAnchor.UpperLeft);
            GameHud.Text(new Rect(w - 630, 20, 600, 60), $"{CommonStrings.Best}: {_best}", GameHud.Medium, TextAnchor.UpperRight);

            switch (_state)
            {
                case State.Waiting:
                    PlayerGate.DrawWaiting(w, _input, RunnerStrings.Title, RunnerStrings.KeyboardHelp);
                    break;

                case State.Countdown:
                    int n = Mathf.CeilToInt(CountdownSeconds - _stateTime);
                    GameHud.Message(w, n > 0 ? n.ToString() : CommonStrings.Go);
                    break;

                case State.Running:
                    if (_paused)
                    {
                        PlayerGate.DrawPaused(w, _input);
                        break;
                    }
                    if (_runTime < 0.8f) GameHud.Message(w, CommonStrings.Go);
                    DrawPopups();
                    var hint = CurrentHint();
                    if (hint != null) GameHud.Message(w, hint, null, new Color(1f, 0.9f, 0.2f));
                    break;

                case State.GameOver:
                    DrawPopups();
                    GameHud.Message(w, CommonStrings.GameOver,
                        $"{CommonStrings.Score}: {Score}" + (_newBest ? $"   {CommonStrings.NewBest}" : ""));
                    if (_stateTime > 1.2f)
                        GameHud.Text(new Rect(0, 540, w, 80),
                            PlayerGate.State(_input) == GateState.NoService ? CommonStrings.PressEnter : CommonStrings.PlayAgain, GameHud.Small);
                    break;
            }
        }
    }
}
