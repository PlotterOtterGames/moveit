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
