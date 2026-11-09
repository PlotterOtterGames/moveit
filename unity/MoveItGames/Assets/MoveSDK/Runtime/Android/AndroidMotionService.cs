using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace MoveIt.Motion
{
    /// <summary>
    /// Runs the on-device motion service (camera + pose AI, the Java library in
    /// Assets/Plugins/Android/MoveItMotion.androidlib) on Android. It sends skeletons to <see cref="MotionSystem"/>
    /// over localhost exactly like the PC service, so nothing else in the game changes.
    /// In the editor and on PC this does nothing: run motion-service-py/main.py instead.
    /// </summary>
    public static class AndroidMotionService
    {
        [Serializable]
        public sealed class Status
        {
            /// <summary>"editor", "stopped", "starting", "running", "paused", "no-permission", "no-camera", "error".</summary>
            public string state = "stopped";
            public string camera = "";
            public string @delegate = "";
            public string model = "";
            public float fps;
            public float inf;
            public float cam;
            /// <summary>Over the last ~5 s: camera frames the AI never saw (busy with the one before), -1 before the first count.</summary>
            public int skipped = -1;
            /// <summary>Over the last ~5 s: times the AI lost the person it followed (it then runs the person detector again).</summary>
            public int lost = -1;
            /// <summary>How old a frame is when its analysis starts (ms), and the clock the camera stamps frames on.</summary>
            public float age;
            public string clock = "";
            public string error = "";
        }

        /// <summary>"auto" (full model, lite if the device is too slow), "full" or "lite".</summary>
        public static string Model = "auto";

        public static Status Current { get; private set; } = new Status { state = Application.isEditor ? "editor" : "stopped" };
        public static bool NeedsPermission => Current.state == "no-permission";
        public static bool IsSupported => Application.platform == RuntimePlatform.Android;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!IsSupported) return;
            var go = new GameObject("Android Motion Service") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        /// <summary>Saves the next camera frame on the device (camera test screen). Returns its path, or "".</summary>
        public static string RequestSnapshot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var service = new AndroidJavaClass("al.moveit.motion.MotionService");
                return service.CallStatic<string>("requestSnapshot");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AndroidMotion] Snapshot failed: {e.Message}");
            }
#endif
            return "";
        }

        /// <summary>Ask Android for the camera again (Settings → Kamera when permission was refused).</summary>
        public static void RequestPermission() => Runner.Instance?.StartWithPermission();

        /// <summary>Switches the pose model while the service runs: "auto", "full" or "lite".</summary>
        public static void SetModel(string model)
        {
            if (model == Model) return;
            Model = model;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var service = new AndroidJavaClass("al.moveit.motion.MotionService");
                service.CallStatic("setModel", model);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AndroidMotion] setModel failed: {e.Message}");
            }
#endif
        }

        /// <summary>Up to how many people the pose AI follows now (1 or 2).</summary>
        public static int People { get; private set; } = 1;
        static float _peopleRate;
        static bool _peopleTwin, _peopleBench;
        static (int max, float rate, bool twin) _wanted = (1, 0f, false);

        /// <summary>PerfLab's people=, npurate=, twin=, npubench=: used instead of what the games ask for (null = the
        /// games'). bench: the NPU runs that many fixed crops as if they were people (their cost, nobody needed).</summary>
        public static (int max, float rate, bool twin, bool bench)? PeopleOverride
        {
            get => _override;
            set
            {
                _override = value;
                SetPeople(_wanted.max, _wanted.rate, _wanted.twin);
            }
        }
        static (int max, float rate, bool twin, bool bench)? _override;

        /// <summary>
        /// Up to how many people the pose AI follows (1 or 2: a two-player game asks for 2 while it wants a second player)
        /// and, on the box's NPU, the landmark model's runs per second shared by them while two are followed (0 = each
        /// every frame; 30 = they take turns, ~15 poses a second each, the same NPU load as one player). Going back to one:
        /// <paramref name="keep"/> (that body's <see cref="Body.ServiceTrack"/>) stays followed. <paramref name="twin"/>:
        /// one person's landmarks run twice, a second player's cost without one (PerfLab). Every scene starts with one
        /// (<c>BoxGraphics</c>). The PC service takes <c>--num-poses 2</c> instead.
        /// </summary>
        public static void SetPeople(int max, float runsPerSecond = 0f, bool twin = false, int keep = 0)
        {
            _wanted = (max, runsPerSecond, twin);
            bool bench = false;
            if (PeopleOverride.HasValue) (max, runsPerSecond, twin, bench) = PeopleOverride.Value;
            max = Mathf.Clamp(max, 1, 2);
            if (max == People && runsPerSecond == _peopleRate && twin == _peopleTwin && bench == _peopleBench) return;
            People = max;
            _peopleRate = runsPerSecond;
            _peopleTwin = twin;
            _peopleBench = bench;
            Debug.Log($"[AndroidMotion] People: up to {max}" + (max > 1 ? $", {runsPerSecond:0} landmark runs/s" : "") +
                      (twin ? ", twin" : "") + (bench ? ", bench" : "") + (keep > 0 ? $", keeping track {keep}" : ""));
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var service = new AndroidJavaClass("al.moveit.motion.MotionService");
                service.CallStatic("setPeople", max, runsPerSecond, twin, keep, bench);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AndroidMotion] setPeople failed: {e.Message}");
            }
#endif
        }

        static string _cameraSize = "";

        /// <summary>The camera frame size to ask for ("1280x1024"; "" = the default, 1024 x 768 on the X98K Pro):
        /// PerfLab's camera=, for comparing sizes on the box.</summary>
        public static void SetCameraSize(string size)
        {
            if (size == _cameraSize) return;
            _cameraSize = size;
            int x = size.IndexOf('x');
            int width = 0, height = 0;
            if (x > 0 && (!int.TryParse(size.Substring(0, x), out width) || !int.TryParse(size.Substring(x + 1), out height)))
                width = height = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var service = new AndroidJavaClass("al.moveit.motion.MotionService");
                service.CallStatic("setCameraSize", width, height);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AndroidMotion] setCameraSize failed: {e.Message}");
            }
#endif
        }

        sealed class Runner : MonoBehaviour
        {
            public static Runner Instance;
            float _nextStatus;

            void Awake() => Instance = this;

            void Start() => StartWithPermission();

            public void StartWithPermission()
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    StartService();
                    return;
                }
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => StartService();
                callbacks.PermissionDenied += _ => Current = new Status { state = "no-permission" };
                callbacks.PermissionDeniedAndDontAskAgain += _ => Current = new Status { state = "no-permission" };
                Permission.RequestUserPermission(Permission.Camera, callbacks);
#endif
            }

            void StartService()
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    int port = MotionSystem.Instance != null ? MotionSystem.Instance.Port : 5005;
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    using var service = new AndroidJavaClass("al.moveit.motion.MotionService");
                    service.CallStatic("start", activity, port, Model);
                    Debug.Log($"[AndroidMotion] Started (port {port}, model {Model})");
                }
                catch (Exception e)
                {
                    Current = new Status { state = "error", error = e.Message };
                    Debug.LogError($"[AndroidMotion] Could not start: {e}");
                }
#endif
            }

            void OnApplicationPause(bool paused)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (paused)
                {
                    using var service = new AndroidJavaClass("al.moveit.motion.MotionService");
                    service.CallStatic("pause");
                }
                else StartWithPermission();
#endif
            }

            void Update()
            {
                if (Time.unscaledTime < _nextStatus) return;
                _nextStatus = Time.unscaledTime + 1f;
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    using var service = new AndroidJavaClass("al.moveit.motion.MotionService");
                    var status = JsonUtility.FromJson<Status>(service.CallStatic<string>("getStatus"));
                    if (status != null && !(NeedsPermission && status.state == "stopped")) Current = status;
                }
                catch (Exception e)
                {
                    Current = new Status { state = "error", error = e.Message };
                }
#endif
            }
        }
    }
}
