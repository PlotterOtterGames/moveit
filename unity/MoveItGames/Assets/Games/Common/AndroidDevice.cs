using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>
    /// The few Android system features the box's own settings screen needs, called through JNI so no Java plugin
    /// is required. In the editor everything is simulated, so the settings screen can be developed on the PC.
    /// </summary>
    public static class AndroidDevice
    {
        const int StreamMusic = 3;              // AudioManager.STREAM_MUSIC
        const int FlagActivityNewTask = 0x10000000;

        public static bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable;
        public static string Model => SystemInfo.deviceModel;
        public static string OperatingSystem => SystemInfo.operatingSystem;

        /// <summary>Opens Android's Wi-Fi screen. Back on the remote returns to the app (it is the home screen).</summary>
        public static bool OpenWifiSettings() => OpenSystemScreen("android.settings.WIFI_SETTINGS");

        /// <summary>Full Android settings, for technical support only.</summary>
        public static bool OpenAndroidSettings() => OpenSystemScreen("android.settings.SETTINGS");

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject Activity
        {
            get
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                return player.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }

        static bool OpenSystemScreen(string action)
        {
            try
            {
                using var intent = new AndroidJavaObject("android.content.Intent", action);
                intent.Call<AndroidJavaObject>("addFlags", FlagActivityNewTask);
                using var activity = Activity;
                activity.Call("startActivity", intent);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AndroidDevice] Could not open {action}: {e.Message}");
                return false;
            }
        }

        /// <summary>System media volume 0..1. Set without showing Android's volume bar.</summary>
        public static float Volume
        {
            get
            {
                try
                {
                    using var activity = Activity;
                    using var audio = activity.Call<AndroidJavaObject>("getSystemService", "audio");
                    int max = audio.Call<int>("getStreamMaxVolume", StreamMusic);
                    return max > 0 ? (float)audio.Call<int>("getStreamVolume", StreamMusic) / max : 0f;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[AndroidDevice] Could not read volume: {e.Message}");
                    return 0f;
                }
            }
            set
            {
                try
                {
                    using var activity = Activity;
                    using var audio = activity.Call<AndroidJavaObject>("getSystemService", "audio");
                    int max = audio.Call<int>("getStreamMaxVolume", StreamMusic);
                    audio.Call("setStreamVolume", StreamMusic, Mathf.RoundToInt(Mathf.Clamp01(value) * max), 0);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[AndroidDevice] Could not set volume: {e.Message}");
                }
            }
        }
#else
        static float _editorVolume = 0.7f;

        public static float Volume
        {
            get => _editorVolume;
            set => _editorVolume = Mathf.Clamp01(value);
        }

        static bool OpenSystemScreen(string action)
        {
            Debug.Log($"[AndroidDevice] On the box this opens {action}.");
            return false;
        }
#endif
    }
}
