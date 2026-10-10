using System;
using System.IO;
using MoveIt.Motion;
using UnityEngine;
using static MoveIt.Games.CommonStrings;

namespace MoveIt.Games
{
    /// <summary>
    /// The camera test screen (Settings → Kamera), drawn next to the full-screen skeleton of
    /// <see cref="MotionDebugOverlay"/>: which camera and AI model run where, live speed numbers, a latency graph,
    /// and a recorder. OK / Enter starts and stops recording every skeleton packet to a .jsonl file
    /// (on Android: /sdcard/Android/data/&lt;package&gt;/files/sessions, fetch with adb pull) that
    /// motion-service-py/replay.py can play back on the PC for tuning gestures with real movement.
    /// </summary>
    public sealed class RecognitionLab : MonoBehaviour
    {
        const int GraphSamples = 150;

        readonly float[] _latency = new float[GraphSamples];
        int _latencyHead;
        StreamWriter _recording;
        string _recordingPath = "";
        int _recordedFrames;
        MotionSystem _motion;
        MotionDebugOverlay _overlay;
        bool _overlayWasFullScreen;
        string _snapshotPath = "";

        public bool IsRecording => _recording != null;

        void OnEnable()
        {
            _motion = MotionSystem.Instance;
            if (_motion != null) _motion.PacketReceived += OnPacket;
        }

        void Start()
        {
            // The motion rig (with its overlay) comes from the menu in corner mode: show the skeleton full screen here.
            _overlay = FindAnyObjectByType<MotionDebugOverlay>();
            if (_overlay == null) return;
            _overlayWasFullScreen = _overlay.FullScreen;
            _overlay.FullScreen = true;
            _overlay.Visible = true;
        }

        void OnDisable()
        {
            if (_motion != null) _motion.PacketReceived -= OnPacket;
            StopRecording();
            if (_overlay != null) _overlay.FullScreen = _overlayWasFullScreen;
        }

        void Update()
        {
            if (_motion == null && MotionSystem.Instance != null)
            {
                _motion = MotionSystem.Instance;
                _motion.PacketReceived += OnPacket;
            }
            if (RemoteInput.Confirm)
            {
                if (IsRecording) StopRecording();
                else StartRecording();
            }
            if (RemoteInput.Right && AndroidMotionService.IsSupported) _snapshotPath = AndroidMotionService.RequestSnapshot();
        }

        void OnPacket(MotionPacket packet)
        {
            _latency[_latencyHead] = _motion.LatencyMs;
            _latencyHead = (_latencyHead + 1) % GraphSamples;
            if (_recording == null) return;
            _recording.WriteLine(JsonUtility.ToJson(packet));
            _recordedFrames++;
        }

        void StartRecording()
        {
            string dir = Path.Combine(Application.persistentDataPath, "sessions");
            Directory.CreateDirectory(dir);
            _recordingPath = Path.Combine(dir, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            _recording = new StreamWriter(_recordingPath);
            _recordedFrames = 0;
            Debug.Log($"[RecognitionLab] Recording to {_recordingPath}");
        }

        void StopRecording()
        {
            if (_recording == null) return;
            _recording.Dispose();
            _recording = null;
            Debug.Log($"[RecognitionLab] Saved {_recordedFrames} frames to {_recordingPath}");
        }

        void OnGUI()
        {
            GUI.depth = -1;  // on top of the full-screen skeleton (lower depth = drawn later)
            float w = GameHud.Begin();
            var panel = new Rect(w - 620, 20, 600, 620);
            GameHud.Box(panel, new Color(0f, 0f, 0f, 0.75f));
            float x = panel.x + 24, y = panel.y + 18;

            GameHud.Text(new Rect(x, y, 560, 50), T("Testi i kamerës", "Camera test"), GameHud.Medium, TextAnchor.UpperLeft);
            y += 70;

            var android = AndroidMotionService.Current;
            bool connected = _motion != null && _motion.IsConnected;
            Row(ref y, x, T("Burimi", "Source"), connected ? _motion.Source : T("pa lidhje", "not connected"));
            if (AndroidMotionService.IsSupported)
                Row(ref y, x, T("Kamera", "Camera"), $"{android.camera}  ({android.state})");
            Row(ref y, x, T("Modeli", "Model"), connected ? $"{_motion.Model} {android.@delegate}".Trim() : "-");
            Row(ref y, x, T("Kamera fps", "Camera fps"), connected && _motion.CameraFps > 0 ? $"{_motion.CameraFps:0}" : "-");
            Row(ref y, x, T("AI fps", "AI fps"), connected ? $"{_motion.ServiceFps:0}" : "-");
            Row(ref y, x, T("AI koha", "AI time"), connected ? $"{_motion.InferenceMs:0} ms" : "-");
            Row(ref y, x, T("Vonesa", "Latency"), connected ? $"{_motion.LatencyMs:0} ms" : "-");

            y += 10;
            DrawGraph(new Rect(x, y, 552, 110));
            y += 124;

            string rec = IsRecording
                ? T($"● REGJISTROHET… {_recordedFrames} kuadro   (OK = ndalo)", $"● RECORDING… {_recordedFrames} frames   (OK = stop)")
                : T("OK = regjistro një seancë për testim", "OK = record a session for tuning");
            GameHud.Text(new Rect(x, y, 560, 40), rec, GameHud.Small, TextAnchor.UpperLeft,
                IsRecording ? new Color(1f, 0.35f, 0.3f) : Color.white);
            if (!string.IsNullOrEmpty(_recordingPath) && !IsRecording)
                GameHud.Text(new Rect(x, y + 40, 560, 60), Path.GetFileName(_recordingPath), GameHud.Small, TextAnchor.UpperLeft,
                    new Color(1f, 1f, 1f, 0.6f));
            if (AndroidMotionService.IsSupported)
                GameHud.Text(new Rect(x, y + 80, 560, 40),
                    string.IsNullOrEmpty(_snapshotPath)
                        ? T("→ = ruaj një foto të kamerës", "→ = save a camera snapshot")
                        : T("Foto u ruajt: camera-snapshot.jpg", "Snapshot saved: camera-snapshot.jpg"),
                    GameHud.Small, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.6f));
        }

        static void Row(ref float y, float x, string label, string value)
        {
            GameHud.Text(new Rect(x, y, 230, 36), label, GameHud.Small, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.7f));
            GameHud.Text(new Rect(x + 230, y, 330, 36), value, GameHud.Small, TextAnchor.UpperLeft);
            y += 40;
        }

        /// <summary>Capture-to-game latency over the last ~5 s, scaled 0–200 ms (green under 100 ms).</summary>
        void DrawGraph(Rect r)
        {
            GameHud.Box(r, new Color(1f, 1f, 1f, 0.06f));
            float limit = r.y + r.height - r.height * 100f / 200f;
            GameHud.Box(new Rect(r.x, limit, r.width, 1f), new Color(1f, 1f, 1f, 0.25f));
            float barW = r.width / GraphSamples;
            for (int i = 0; i < GraphSamples; i++)
            {
                float ms = _latency[(_latencyHead + i) % GraphSamples];
                if (ms <= 0f) continue;
                float h = Mathf.Clamp01(ms / 200f) * r.height;
                GameHud.Box(new Rect(r.x + i * barW, r.y + r.height - h, Mathf.Max(1f, barW - 1f), h),
                    ms < 100f ? new Color(0.3f, 0.85f, 0.4f) : new Color(1f, 0.55f, 0.2f));
            }
        }
    }
}
