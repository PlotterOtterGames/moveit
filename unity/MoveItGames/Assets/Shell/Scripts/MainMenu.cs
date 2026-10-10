using System;
using MoveIt.Games;
using MoveIt.Motion;
using UnityEngine;
using UnityEngine.SceneManagement;
using static MoveIt.Games.CommonStrings;

namespace MoveIt.Shell
{
    /// <summary>
    /// The box's home screen (the app is registered as Android's HOME, so the box boots here and the remote's Home
    /// button returns here). A row of game tiles: a step to the left or right (or ← →) moves one tile, stepping back to
    /// the middle keeps it; raise both hands or press OK to play. ↓ reaches the Settings button. Settings is remote-only and shows just what a family
    /// needs. Android's own settings are hidden behind 7 presses on "Device" (like Android's developer-mode trick).
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        struct Entry
        {
            public string Scene;
            public Func<string> Title;
            public Func<string> Description;
            public Color Color;
        }

        public enum SettingsItem { Wifi, Volume, Language, Camera, Version, Device, AndroidSettings }

        static readonly Entry[] Games =
        {
            new Entry
            {
                Scene = "Runner", Color = new Color(0.2f, 0.5f, 1f),
                Title = () => T("Vrapo!", "Run!"),
                Description = () => T("Hap anash, kërce dhe ulu për të shmangur pengesat", "Step aside, jump and duck to dodge obstacles"),
            },
            new Entry
            {
                Scene = "Slash", Color = new Color(1f, 0.55f, 0.1f),
                Title = () => T("Fruta!", "Fruit Slash!"),
                Description = () => T("Copëto frutat me duar, shmang bombat", "Slice the fruit with your hands, avoid the bombs"),
            },
            new Entry
            {
                Scene = "Bowling", Color = new Color(0.85f, 0.2f, 0.3f),
                Title = () => "Bowling",
                Description = () => T("Rrëzo kunjat! Deri në 4 lojtarë me radhë", "Knock down the pins! Up to 4 players taking turns"),
            },
            new Entry
            {
                Scene = "Paint", Color = new Color(0.48f, 0.38f, 1f),
                Title = () => T("Pikturo!", "Paint!"),
                Description = () => T("Pikturo me duar në TV, ose mbi veten me kamerën", "Paint on the TV with your hands, or on yourself with the camera"),
            },
            new Entry
            {
                Scene = "Sword", Color = new Color(0.27f, 0.7f, 0.36f),
                Title = () => T("Shpata!", "Swords!"),
                Description = () => T("Godit kalorësit me shpatë dhe mbrohu nga goditjet e tyre", "Hit the knights with your sword and block their blows"),
            },
        };

        const int AdminTapsNeeded = 7;
        const string CameraTestScene = "MotionDebug";

        BodyInput _input;
        MotionDebugOverlay _mirror;
        int _previousLane;
        int _adminTaps;

        public int SelectedGame { get; private set; } = 1;
        public bool SettingsButtonFocused { get; private set; }
        public bool SettingsOpen { get; private set; }
        public SettingsItem SettingsFocus { get; private set; }
        public bool AdminUnlocked { get; private set; }

        int SettingsCount => AdminUnlocked ? 7 : 6;

        void Start()
        {
            _input = BodyInput.Instance != null ? BodyInput.Instance : FindAnyObjectByType<BodyInput>();
            _input?.ResetLane();
            _previousLane = 0;
            _mirror = FindAnyObjectByType<MotionDebugOverlay>();
            GameAudio.PlayMusic("menu");
        }

        void Update()
        {
            GameNavigation.HandleCommonKeys();
            if (SettingsOpen) UpdateSettings();
            else UpdateHome();
        }

        // ---------- home ----------

        void UpdateHome()
        {
            // A step out to the side moves one tile that way; stepping back to the middle doesn't move it back.
            // (← → also change BodyInput.Lane; those are handled as remote keys below, not as steps.)
            int lane = _input != null ? _input.Lane : 0;
            if (lane != _previousLane)
            {
                if (lane != 0 && !_input.UsingKeyboard) Select(SelectedGame + lane);
                _previousLane = lane;
            }
            if (RemoteInput.Left) Select(SelectedGame - 1);
            if (RemoteInput.Right) Select(SelectedGame + 1);
            if (RemoteInput.Down && !SettingsButtonFocused)
            {
                SettingsButtonFocused = true;
                GameAudio.Sfx("ui_move");
            }
            if (RemoteInput.Up && SettingsButtonFocused)
            {
                SettingsButtonFocused = false;
                GameAudio.Sfx("ui_move");
            }

            bool ok = RemoteInput.Confirm;
            if (SettingsButtonFocused && ok)
            {
                OpenSettings();
                return;
            }
            if (ok || (_input != null && _input.ConfirmTriggered)) Launch(Games[SelectedGame].Scene);
        }

        void Select(int index)
        {
            index = Mathf.Clamp(index, 0, Games.Length - 1);
            SettingsButtonFocused = false;
            if (index == SelectedGame) return;
            SelectedGame = index;
            GameAudio.Sfx("ui_move");
        }

        static void Launch(string scene)
        {
            GameAudio.Sfx("ui_select", vary: 0f);
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else Debug.LogWarning($"[MainMenu] Scene '{scene}' is not in the build settings (run MoveIt > Build All Scenes).");
        }

        // ---------- settings ----------

        void OpenSettings()
        {
            GameAudio.Sfx("ui_select", vary: 0f);
            SettingsOpen = true;
            if (_mirror != null) _mirror.Visible = false;  // it would cover the bottom of the panel
            SettingsFocus = SettingsItem.Wifi;
            _adminTaps = 0;
            AdminUnlocked = false;
        }

        void CloseSettings()
        {
            SettingsOpen = false;
            if (_mirror != null) _mirror.Visible = true;
            AdminUnlocked = false;
            _previousLane = _input != null ? _input.Lane : 0;  // ← → in settings also moved the lane; don't jump tiles
        }

        void UpdateSettings()
        {
            if (RemoteInput.Back)
            {
                GameAudio.Sfx("ui_back", vary: 0f);
                CloseSettings();
                return;
            }
            int count = SettingsCount;
            if (RemoteInput.Up) SettingsFocus = (SettingsItem)(((int)SettingsFocus + count - 1) % count);
            if (RemoteInput.Down) SettingsFocus = (SettingsItem)(((int)SettingsFocus + 1) % count);
            int step = (RemoteInput.Right ? 1 : 0) - (RemoteInput.Left ? 1 : 0);
            if (RemoteInput.Up || RemoteInput.Down || step != 0) GameAudio.Sfx("ui_move");
            if (step != 0) Adjust(SettingsFocus, step);
            if (RemoteInput.Confirm)
            {
                GameAudio.Sfx("ui_select", vary: 0f);
                Activate(SettingsFocus);
            }
        }

        void Adjust(SettingsItem item, int step)
        {
            switch (item)
            {
                case SettingsItem.Volume:
                    AndroidDevice.Volume = Mathf.Round((AndroidDevice.Volume + 0.1f * step) * 10f) / 10f;
                    break;
                case SettingsItem.Language:
                    Albanian = !Albanian;
                    break;
            }
        }

        void Activate(SettingsItem item)
        {
            switch (item)
            {
                case SettingsItem.Wifi:
                    AndroidDevice.OpenWifiSettings();
                    break;
                case SettingsItem.Language:
                    Albanian = !Albanian;
                    break;
                case SettingsItem.Camera:
                    if (AndroidMotionService.NeedsPermission) AndroidMotionService.RequestPermission();
                    else Launch(CameraTestScene);
                    break;
                case SettingsItem.Device:
                    if (++_adminTaps >= AdminTapsNeeded) AdminUnlocked = true;
                    break;
                case SettingsItem.AndroidSettings:
                    AndroidDevice.OpenAndroidSettings();
                    break;
            }
        }

        static string Label(SettingsItem item) => item switch
        {
            SettingsItem.Wifi => "Wi-Fi",
            SettingsItem.Volume => T("Volumi", "Volume"),
            SettingsItem.Language => T("Gjuha", "Language"),
            SettingsItem.Camera => T("Kamera", "Camera"),
            SettingsItem.Version => T("Versioni", "Version"),
            SettingsItem.Device => T("Pajisja", "Device"),
            _ => T("Cilësimet e Android-it", "Android settings"),
        };

        static string Value(SettingsItem item)
        {
            var motion = MotionSystem.Instance;
            return item switch
            {
                SettingsItem.Wifi => AndroidDevice.IsOnline ? T("I lidhur  ·  OK = ndrysho", "Connected  ·  OK = change") : T("Pa lidhje  ·  OK = lidhu", "Not connected  ·  OK = connect"),
                SettingsItem.Volume => $"‹  {Mathf.RoundToInt(AndroidDevice.Volume * 100f)}%  ›",
                SettingsItem.Language => Albanian ? "‹  Shqip  ›" : "‹  English  ›",
                SettingsItem.Camera => CameraValue(motion),
                SettingsItem.Version => $"{Application.version}  ·  " + T("përditësimet automatike së shpejti", "automatic updates coming soon"),
                SettingsItem.Device => $"{AndroidDevice.Model}  ·  {AndroidDevice.OperatingSystem}",
                _ => T("Vetëm për mbështetje teknike", "Technical support only"),
            };
        }

        static string CameraValue(MotionSystem motion)
        {
            var android = AndroidMotionService.Current;
            if (AndroidMotionService.NeedsPermission) return T("Pa leje  ·  OK = lejo kamerën", "No permission  ·  OK = allow camera");
            if (android.state == "no-camera") return T("Nuk ka kamerë  ·  lidh kamerën USB", "No camera  ·  plug in the USB camera");
            if (motion == null || !motion.IsConnected)
                return android.state == "error" ? T($"Gabim: {android.error}", $"Error: {android.error}") : T("Nuk u gjet  ·  OK = provo", "Not found  ·  OK = test");
            string where = string.IsNullOrEmpty(android.camera) ? "" : $"{android.camera}, ";
            string engine = string.IsNullOrEmpty(android.@delegate) ? motion.Model : $"{android.@delegate} {motion.Model}";
            return $"{where}{engine}  {motion.ServiceFps:0} fps, {motion.InferenceMs:0} ms  ·  " + T("OK = provo", "OK = test");
        }

        // ---------- drawing ----------

        GUIStyle _tileTitle;
        GUIStyle TileTitle => _tileTitle ??= new GUIStyle(GameHud.Big) { fontSize = 76, wordWrap = false };

        void OnGUI()
        {
            GUI.depth = 10;  // draw behind the skeleton mirror (lower depth = on top)
            float w = GameHud.Begin();
            var brand = Brand.Current;
            GameHud.Box(new Rect(0, 0, w, GameHud.Height), brand.BackgroundColor);
            DrawTopBar(w, brand);
            DrawHome(w, brand);
            if (SettingsOpen) DrawSettings(w, brand);
        }

        void DrawTopBar(float w, Brand brand)
        {
            GameHud.Text(new Rect(40, 20, 800, 70), brand.name, GameHud.Medium, TextAnchor.MiddleLeft, brand.PrimaryColor);
            GameHud.Text(new Rect(40, 82, 800, 40), brand.Tagline, GameHud.Small, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f));

            var motion = MotionSystem.Instance;
            bool camera = motion != null && motion.IsConnected;
            string status = $"Wi-Fi: {(AndroidDevice.IsOnline ? "OK" : "—")}     {T("Kamera", "Camera")}: {(camera ? "OK" : "—")}     {DateTime.Now:HH:mm}";
            GameHud.Text(new Rect(w - 840, 20, 800, 70), status, GameHud.Small, TextAnchor.MiddleRight);
        }

        void DrawHome(float w, Brand brand)
        {
            GameHud.Text(new Rect(0, 150, w, 70), T("Zgjidh lojën", "Choose a game"), GameHud.Medium);

            const float tileH = 400f, gap = 40f;
            float tileW = Mathf.Min(520f, (w - 120f - gap * (Games.Length - 1)) / Games.Length);
            float x0 = (w - (tileW * Games.Length + gap * (Games.Length - 1))) / 2f;
            bool tilesFocused = !SettingsButtonFocused && !SettingsOpen;
            for (int i = 0; i < Games.Length; i++)
            {
                var g = Games[i];
                bool selected = i == SelectedGame;
                float grow = selected && tilesFocused ? 20f : 0f;
                var r = new Rect(x0 + i * (tileW + gap) - grow, 250 - grow, tileW + grow * 2, tileH + grow * 2);
                if (selected) GameHud.Box(new Rect(r.x - 8, r.y - 8, r.width + 16, r.height + 16), tilesFocused ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                var c = g.Color;
                c.a = selected ? 1f : 0.55f;
                GameHud.Box(r, c);
                TileTitle.fontSize = tileW >= 440f ? 76 : 60;   // five tiles on a 16:9 TV are narrower
                GameHud.Text(new Rect(r.x, r.y + 40, r.width, 120), g.Title(), TileTitle);
                GameHud.Text(new Rect(r.x + 30, r.y + 200, r.width - 60, 160), g.Description(), GameHud.Small);
            }

            var button = new Rect(w / 2f - 200f, 720f, 400f, 70f);
            bool buttonFocused = SettingsButtonFocused && !SettingsOpen;
            GameHud.Box(button, buttonFocused ? brand.PrimaryColor : new Color(1f, 1f, 1f, 0.12f));
            GameHud.Text(button, T("Cilësimet", "Settings"), GameHud.Small, TextAnchor.MiddleCenter,
                buttonFocused ? brand.BackgroundColor : Color.white, shadow: !buttonFocused);

            string hint = AndroidMotionService.NeedsPermission
                ? T("Lejo kamerën: ↓ Cilësimet → Kamera", "Allow the camera: ↓ Settings → Camera")
                : PlayerGate.State(_input) switch
            {
                GateState.NoService => T("← → zgjidh,  OK luaj,  ↓ cilësimet   (kamera nuk është aktive)", "← → choose,  OK play,  ↓ settings   (camera service not running)"),
                GateState.NoPlayer => StepIn,
                GateState.Calibrating => HoldStill,
                _ => T("Hap majtas ose djathtas për të zgjedhur, ngri duart lart për të luajtur", "Step left or right to choose, raise both hands to play"),
            };
            // Keep clear of the skeleton mirror in the bottom-right corner.
            GameHud.Text(new Rect(40, 830, w - 680, 140), hint, GameHud.Medium);
        }

        void DrawSettings(float w, Brand brand)
        {
            GameHud.Box(new Rect(0, 0, w, GameHud.Height), new Color(0f, 0f, 0f, 0.75f));
            int count = SettingsCount;
            const float rowH = 86f, panelW = 1300f;
            float panelH = 150f + count * rowH + 90f;
            float x = (w - panelW) / 2f, y = (GameHud.Height - panelH) / 2f;
            GameHud.Box(new Rect(x, y, panelW, panelH), brand.BackgroundColor);
            GameHud.Box(new Rect(x, y, panelW, 6f), brand.PrimaryColor);
            GameHud.Text(new Rect(x + 50, y + 30, panelW - 100, 90), T("Cilësimet", "Settings"), GameHud.Medium, TextAnchor.MiddleLeft);

            for (int i = 0; i < count; i++)
            {
                var item = (SettingsItem)i;
                var row = new Rect(x + 30, y + 140 + i * rowH, panelW - 60, rowH - 10);
                bool focused = item == SettingsFocus;
                GameHud.Box(row, focused ? brand.PrimaryColor : new Color(1f, 1f, 1f, 0.06f));
                var textColor = focused ? brand.BackgroundColor : Color.white;
                GameHud.Text(new Rect(row.x + 24, row.y, 420, row.height), Label(item), GameHud.Small, TextAnchor.MiddleLeft, textColor, shadow: !focused);
                GameHud.Text(new Rect(row.x + 440, row.y, row.width - 464, row.height), Value(item), GameHud.Small, TextAnchor.MiddleRight, textColor, shadow: !focused);
            }

            GameHud.Text(new Rect(x, y + panelH - 80, panelW, 60),
                T("↑ ↓ lëviz    ← → ndrysho    OK zgjidh    MBRAPA mbyll", "↑ ↓ move    ← → change    OK select    BACK close"),
                GameHud.Small, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
        }
    }
}
