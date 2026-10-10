using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>
    /// The game's view of the Tundu cloud: Game Pass, gift card codes, the game catalog and app updates. On the box
    /// it talks to the Java client (Assets/Plugins/Android/MoveItCloud.androidlib, al.moveit.cloud.TunduCloud), which
    /// does all the checking (signatures, trusted time). In the editor and on PC it simulates a box, so the screens
    /// can be built without hardware: any well-formed code adds 30 days (kept in PlayerPrefs).
    /// </summary>
    public static class TunduCloud
    {
        [Serializable]
        public sealed class PassState
        {
            public bool active;
            public long until;
            public long now;
            public string device = "";
            public bool blocked;
        }

        [Serializable]
        public sealed class UpdateState
        {
            /// <summary>"none", "downloading", "ready", "installing", "failed".</summary>
            public string state = "none";
            public float progress;
            public string version = "";
            public bool safe_mode;
        }

        [Serializable]
        public sealed class RedeemResult
        {
            /// <summary>"idle", "working", "done".</summary>
            public string state = "idle";
            /// <summary>ok, already_yours, used, not_active, void, invalid, limit, check_code, network, locked, slow_down, blocked, no_identity.</summary>
            public string result = "";
            public int days;
            public long retry_after;
        }

        [Serializable]
        sealed class CatalogEntry
        {
            public string id = "";
            public string tier = "free";
            public int minApp;
            public bool hidden;
            public long from;
        }

        [Serializable]
        sealed class CatalogFile
        {
            public CatalogEntry[] games = Array.Empty<CatalogEntry>();
        }

        public static bool OnBox => Application.platform == RuntimePlatform.Android;

        static PassState _pass = new PassState();
        static List<GameRule> _catalog = new List<GameRule>();
        static UpdateState _update = new UpdateState();
        static float _passReadAt = -10f, _catalogReadAt = -10f, _updateReadAt = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() => EnsureStarted();

        /// <summary>
        /// Starts the Java side once. Also called before any other call, so the home screen's MarkHealthy can never
        /// arrive before the start (that would count as a failed start and could trigger safe mode).
        /// </summary>
        static void EnsureStarted()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_started) return;
            _started = true;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                JavaClass.CallStatic("start", activity);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TunduCloud] start failed: {e.Message}");
            }
#endif
        }

        /// <summary>Game Pass right now (refreshed at most once a second).</summary>
        public static PassState Pass
        {
            get
            {
                if (Time.unscaledTime - _passReadAt >= 1f)
                {
                    _passReadAt = Time.unscaledTime;
                    _pass = Read("passState", _pass, SimulatedPass);
                }
                return _pass;
            }
        }

        /// <summary>The signed catalog's rules (refreshed every few seconds); empty = built-in defaults only.</summary>
        public static IReadOnlyList<GameRule> Catalog
        {
            get
            {
                if (Time.unscaledTime - _catalogReadAt >= 5f)
                {
                    _catalogReadAt = Time.unscaledTime;
                    string json = Call("catalog", "[]");
                    try
                    {
                        var file = JsonUtility.FromJson<CatalogFile>("{\"games\":" + json + "}");
                        var rules = new List<GameRule>();
                        foreach (var e in file.games)
                        {
                            rules.Add(new GameRule
                            {
                                Id = e.id, Tier = e.tier == "pass" ? GameTier.Pass : GameTier.Free, MinApp = e.minApp, Hidden = e.hidden, From = e.from,
                            });
                        }
                        _catalog = rules;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[TunduCloud] catalog: {e.Message}");
                    }
                }
                return _catalog;
            }
        }

        public static UpdateState Update
        {
            get
            {
                if (Time.unscaledTime - _updateReadAt >= 1f)
                {
                    _updateReadAt = Time.unscaledTime;
                    _update = Read("updateState", _update, () => new UpdateState());
                }
                return _update;
            }
        }

        public static Access AccessFor(string scene)
        {
            var pass = Pass;
            return GameAccess.For(GameAccess.IdOf(scene), Catalog, AppVersion, pass.now, pass.active);
        }

        /// <summary>This app's versionCode (0 in the editor).</summary>
        public static int AppVersion => _appVersion ??= OnBox ? CallInt("appVersion") : 0;
        static int? _appVersion;

        public static string DeviceId => OnBox ? Call("deviceId", "") : "d_EDITOR";

        /// <summary>Starts redeeming a code; poll <see cref="PollRedeem"/>.</summary>
        public static void Redeem(string code)
        {
            if (OnBox) Java.CallStatic("redeem", code);
            else SimulateRedeem(code);
            _passReadAt = -10f;
        }

        public static RedeemResult PollRedeem()
        {
            if (!OnBox) return SimulatedRedeem();
            var r = Read("redeemResult", new RedeemResult(), () => new RedeemResult());
            if (r.state == "done") _passReadAt = -10f;
            return r;
        }

        /// <summary>A game is running: updates must not install now.</summary>
        public static void SetBusy(bool busy)
        {
            if (OnBox) Java.CallStatic("setBusy", busy);
        }

        /// <summary>A good moment to install a downloaded update (the app restarts). True if it started.</summary>
        public static bool InstallNow() => OnBox && CallBool("installNow");

        /// <summary>The home screen is up: this version works (stops the crash-loop safe mode, reports boot_ok).</summary>
        public static void MarkHealthy()
        {
            if (OnBox) Java.CallStatic("markHealthy");
        }

        public static void CheckNow()
        {
            if (OnBox) Java.CallStatic("checkNow");
            _updateReadAt = _passReadAt = _catalogReadAt = -10f;
        }

        // ---------- JNI ----------

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool _started;
        static AndroidJavaClass _java;
        static AndroidJavaClass JavaClass => _java ??= new AndroidJavaClass("al.moveit.cloud.TunduCloud");

        static AndroidJavaClass Java
        {
            get
            {
                EnsureStarted();
                return JavaClass;
            }
        }
#else
        sealed class NoJava
        {
            public void CallStatic(string method, params object[] args) { }
        }

        static readonly NoJava Java = new NoJava();
#endif

        static string Call(string method, string fallback)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                return Java.CallStatic<string>(method) ?? fallback;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TunduCloud] {method}: {e.Message}");
            }
#endif
            return fallback;
        }

        static int CallInt(string method)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                return Java.CallStatic<int>(method);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TunduCloud] {method}: {e.Message}");
            }
#endif
            return 0;
        }

        static bool CallBool(string method)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                return Java.CallStatic<bool>(method);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TunduCloud] {method}: {e.Message}");
            }
#endif
            return false;
        }

        static T Read<T>(string method, T previous, Func<T> simulated)
        {
            if (!OnBox) return simulated();
            string json = Call(method, null);
            if (string.IsNullOrEmpty(json)) return previous;
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TunduCloud] {method}: {e.Message}");
                return previous;
            }
        }

        // ---------- editor / PC simulation ----------

        const string SimKey = "dev.cloud.pass_until";
        static RedeemResult _simRedeem = new RedeemResult();
        static float _simDoneAt;

        static long UnixNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        static PassState SimulatedPass()
        {
            long until = long.TryParse(PlayerPrefs.GetString(SimKey, "0"), out long u) ? u : 0;
            return new PassState { active = until > UnixNow, until = until, now = UnixNow, device = "d_EDITOR" };
        }

        static void SimulateRedeem(string code)
        {
            string normalized = GiftCode.Normalize(code);
            _simRedeem = new RedeemResult { state = "working" };
            _simDoneAt = Time.unscaledTime + 0.8f; // pretend network
            if (normalized == null)
            {
                _simRedeem = new RedeemResult { state = "done", result = "check_code" };
                return;
            }
            long until = Math.Max(SimulatedPass().until, UnixNow) + 30L * 86400;
            PlayerPrefs.SetString(SimKey, until.ToString());
            PlayerPrefs.Save();
        }

        static RedeemResult SimulatedRedeem()
        {
            if (_simRedeem.state == "working" && Time.unscaledTime >= _simDoneAt) _simRedeem = new RedeemResult { state = "done", result = "ok", days = 30 };
            var r = _simRedeem;
            if (r.state == "done") _simRedeem = new RedeemResult();
            return r;
        }
    }
}
