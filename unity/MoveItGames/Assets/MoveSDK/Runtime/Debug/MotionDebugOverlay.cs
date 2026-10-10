using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// Draws the tracked skeletons, connection stats, gesture state and an event log with IMGUI.
    /// Full-screen in the MotionDebug scene, or as a small "mirror" in a corner of a game. F1 toggles it.
    /// When calibrated it also draws the gesture thresholds (lane borders, jump and crouch lines).
    /// </summary>
    public sealed class MotionDebugOverlay : MonoBehaviour
    {
        [SerializeField] bool fullScreen = true;
        [SerializeField, Range(0.1f, 0.5f), Tooltip("Corner view height as a fraction of the screen.")]
        float cornerSize = 0.28f;
        [SerializeField] bool showStats = true;
        [SerializeField] bool showEventLog = true;
        [SerializeField] BodyGestures gestures;

        static readonly Color LeftColor = new Color(0.25f, 0.65f, 1f);
        static readonly Color RightColor = new Color(1f, 0.55f, 0.1f);
        static readonly Color MidColor = new Color(0.9f, 0.9f, 0.9f);
        static readonly Color InactiveColor = new Color(1f, 1f, 1f, 0.3f);
        static readonly Color ThresholdColor = new Color(1f, 1f, 0.2f, 0.45f);

        readonly List<string> _log = new List<string>();
        GUIStyle _label, _box;
        bool _visible = true;

        /// <summary>Full-screen skeleton (camera test) or the small mirror in the corner (menus and games).</summary>
        public bool FullScreen
        {
            get => fullScreen;
            set => fullScreen = value;
        }

        /// <summary>Show or hide the overlay (F1 toggles it too).</summary>
        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        void Awake()
        {
            if (gestures == null) gestures = FindAnyObjectByType<BodyGestures>();
        }

        void OnEnable()
        {
            if (gestures != null) gestures.OnGesture += LogGesture;
        }

        void OnDisable()
        {
            if (gestures != null) gestures.OnGesture -= LogGesture;
        }

        void LogGesture(Gesture g)
        {
            _log.Insert(0, $"{Time.time,7:0.00}s  {g}");
            if (_log.Count > 10) _log.RemoveAt(_log.Count - 1);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) _visible = !_visible;
        }

        void OnGUI()
        {
            if (!_visible) return;
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = 1080f;
            EnsureStyles();

            var system = MotionSystem.Instance;
            float aspect = system != null && system.ActiveBody != null ? system.ActiveBody.Aspect : 16f / 9f;
            Rect view = fullScreen
                ? FitAspect(new Rect(0, 0, w, h), aspect)
                : new Rect(w - h * cornerSize * aspect - 20, h - h * cornerSize - 20, h * cornerSize * aspect, h * cornerSize);

            GUI.color = new Color(0, 0, 0, fullScreen ? 0.85f : 0.55f);
            GUI.DrawTexture(view, Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (system != null)
            {
                if (gestures != null && gestures.IsCalibrated && system.ActiveBody != null) DrawThresholds(view, aspect);
                foreach (var body in system.Tracker.Bodies) DrawBody(view, body, body == system.ActiveBody);
                if (!system.IsConnected)
                    GUI.Label(new Rect(view.x + 10, view.y + view.height / 2 - 20, view.width - 20, 40),
                        fullScreen ? "Waiting for motion service on UDP 5005...  (python main.py --preview)" : "no motion service", _label);
            }

            if (fullScreen && showStats) DrawStats(system);
        }

        void DrawStats(MotionSystem system)
        {
            var lines = new List<string>();
            if (system == null) lines.Add("No MotionSystem in scene");
            else
            {
                lines.Add(system.IsConnected
                    ? $"{system.Source}   service {system.ServiceFps:0.0} fps   camera {system.CameraFps:0} fps   inference {system.InferenceMs:0.0} ms   latency {system.LatencyMs:0} ms"
                    : "Motion service: not connected");
                lines.Add($"people {system.Tracker.Bodies.Count}   active id {(system.ActiveBody != null ? system.ActiveBody.Id.ToString() : "-")}");
            }
            if (gestures != null)
            {
                lines.Add(gestures.IsCalibrated
                    ? "calibrated"
                    : gestures.HasPlayer ? $"calibrating... stand still {gestures.CalibrationProgress * 100:0}%" : "no player");
                lines.Add($"lane {gestures.Lane,2}   lateral {gestures.LateralOffset,5:0.00}   vertical {gestures.VerticalOffset,5:0.00}   v-speed {gestures.VerticalSpeed,5:0.0}");
                lines.Add($"{(gestures.IsJumping ? "JUMPING  " : "")}{(gestures.IsCrouching ? "CROUCHING  " : "")}{(gestures.AreHandsUp ? "HANDS UP" : "")}");
            }
            if (showEventLog)
            {
                lines.Add("");
                lines.AddRange(_log);
            }
            float height = lines.Count * 30 + 20;
            GUI.Box(new Rect(10, 10, 760, height), GUIContent.none, _box);
            for (int i = 0; i < lines.Count; i++)
                GUI.Label(new Rect(22, 18 + i * 30, 740, 30), lines[i], _label);
            GUI.Label(new Rect(22, 1080 - 40, 900, 30), "F1 hide/show   |   keyboard: arrows/A-D lanes, Space jump, Down crouch, Enter confirm", _label);
        }

        void DrawBody(Rect view, Body body, bool active)
        {
            foreach (var (a, b) in Skeleton.Bones)
            {
                if (!body.IsVisible(a, 0.3f) || !body.IsVisible(b, 0.3f)) continue;
                var color = !active ? InactiveColor
                    : Skeleton.IsLeft(a) && Skeleton.IsLeft(b) ? LeftColor
                    : Skeleton.IsRight(a) && Skeleton.IsRight(b) ? RightColor
                    : MidColor;
                DrawLine(ToGui(view, body, body[a]), ToGui(view, body, body[b]), color, fullScreen ? 6f : 3f);
            }
            for (int i = 0; i < Skeleton.JointCount; i++)
            {
                var j = (Joint)i;
                if (!body.IsVisible(j, 0.3f)) continue;
                var p = ToGui(view, body, body[j]);
                float s = fullScreen ? 12f : 6f;
                GUI.color = active ? Color.white : InactiveColor;
                GUI.DrawTexture(new Rect(p.x - s / 2, p.y - s / 2, s, s), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }

        void DrawThresholds(Rect view, float aspect)
        {
            float u = gestures.BaselineTorso;
            float x0 = gestures.BaselineX, y0 = gestures.BaselineY;
            foreach (float x in new[] { x0 - gestures.LaneEnter * u, x0 + gestures.LaneEnter * u })
            {
                float gx = view.x + x / aspect * view.width;
                DrawLine(new Vector2(gx, view.y), new Vector2(gx, view.yMax), ThresholdColor, 2f);
            }
            foreach (float y in new[] { y0 + gestures.JumpRise * u, y0, y0 - gestures.CrouchEnter * u })
            {
                float gy = view.y + (1f - y) * view.height;
                DrawLine(new Vector2(view.x, gy), new Vector2(view.xMax, gy), ThresholdColor, y == y0 ? 1f : 2f);
            }
        }

        static Vector2 ToGui(Rect view, Body body, Vector3 p) =>
            new Vector2(view.x + p.x / body.Aspect * view.width, view.y + (1f - p.y) * view.height);

        static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            var d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) return;
            var matrix = GUI.matrix;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2, length, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = Color.white;
        }

        static Rect FitAspect(Rect area, float aspect)
        {
            float width = Mathf.Min(area.width, area.height * aspect);
            float height = width / aspect;
            return new Rect(area.x + (area.width - width) / 2, area.y + (area.height - height) / 2, width, height);
        }

        void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 22, richText = false };
            _label.normal.textColor = Color.white;
            _box = new GUIStyle(GUI.skin.box);
        }
    }
}
