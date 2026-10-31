using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MoveIt.Games
{
    /// <summary>
    /// Development tool for finding what costs the GPU on a box: render settings switched while a game runs, without a
    /// new build. Push a text file of <c>key=value</c> lines to the app's files folder; it's applied within half a second
    /// (and again after every scene load; <c>scene</c> loads a scene first, <c>activity</c> then starts that event of an
    /// activity game such as Sporte!), and the next <see cref="PerfLog"/> lines carry its tag:
    /// <code>adb push perflab.txt /sdcard/Android/data/al.tundu.games/files/perflab.txt</code>
    /// Keys: <c>tag</c> (the name in the [Perf] lines), <c>hdr</c> 0/1, <c>scale</c> (render scale), <c>shadows</c> 0/1
    /// (the cameras' shadows), <c>shadowres</c> (main light shadow map size), <c>shadowdist</c> (metres from the camera
    /// that get shadows), <c>nocast</c> (like <c>hide</c>, but those renderers only stop casting shadows), <c>post</c> 0/1 (post-processing, also
    /// where <see cref="BoxGraphics"/> switched it off), <c>sky</c> 0 (a plain background instead of the skybox),
    /// <c>fps</c> (target frame rate),
    /// <c>hide</c> (comma-separated: <c>particles</c>, <c>skinned</c>, <c>transparent</c>, or part of a GameObject's or
    /// a parent's name; those renderers are switched off), <c>bloom</c> / <c>vignette</c> / <c>grade</c> 0 (that
    /// post-processing effect off in every volume), <c>disable</c> (comma-separated script class names, e.g. a game's
    /// class to drop its IMGUI HUD; their Update stops too), <c>model</c> (the pose model: auto, full or lite). A missing
    /// key means the build's setting (BoxGraphics' for the scene); deleting the file restores everything. Players only:
    /// in the editor it would change the project's URP asset on disk.
    /// </summary>
    static class PerfLab
    {
        const string FileName = "perflab.txt";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Application.isEditor) return;
            var go = new GameObject("PerfLab") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Switcher>().Path = Path.Combine(Application.persistentDataPath, FileName);
        }

        sealed class Switcher : MonoBehaviour
        {
            public string Path;

            readonly Dictionary<string, string> _settings = new Dictionary<string, string>();
            readonly Dictionary<Camera, (bool shadows, bool post, CameraClearFlags clear)> _cameras =
                new Dictionary<Camera, (bool, bool, CameraClearFlags)>();
            readonly List<Renderer> _hidden = new List<Renderer>();
            readonly List<Behaviour> _disabled = new List<Behaviour>();
            readonly Dictionary<VolumeComponent, bool> _effects = new Dictionary<VolumeComponent, bool>();
            readonly Dictionary<Renderer, ShadowCastingMode> _casting = new Dictionary<Renderer, ShadowCastingMode>();
            UniversalRenderPipelineAsset _urp;
            bool _hdr;
            int _shadowRes;
            float _shadowDistance;
            DateTime _stamp;
            long _length = -1;
            float _nextPoll;

            void Awake()
            {
                _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (_urp != null)
                {
                    _hdr = _urp.supportsHDR;
                    _shadowRes = _urp.mainLightShadowmapResolution;
                    _shadowDistance = _urp.shadowDistance;
                }
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

            void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                _cameras.Clear();
                _hidden.Clear();
                _disabled.Clear();
                _casting.Clear();
                if (_settings.Count > 0) Apply();
            }

            void Update()
            {
                if (Time.unscaledTime < _nextPoll) return;
                _nextPoll = Time.unscaledTime + 0.5f;
                var info = new FileInfo(Path);
                long length = info.Exists ? info.Length : -1;
                DateTime stamp = info.Exists ? info.LastWriteTimeUtc : default;
                if (length == _length && stamp == _stamp)
                {
                    if (_settings.ContainsKey("hide")) Hide();  // things spawned since (particles, crowds built later)
                    StartActivity();
                    return;
                }
                _length = length;
                _stamp = stamp;
                _settings.Clear();
                if (info.Exists)
                    foreach (string line in File.ReadAllLines(Path))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) _settings[line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1).Trim();
                    }
                string scene = Text("scene");
                if (scene != "" && scene != SceneManager.GetActiveScene().name)
                    SceneManager.LoadScene(scene);  // the rest is applied once it has loaded
                else
                    Apply();
            }

            void Apply()
            {
                if (_urp != null)
                {
                    _urp.supportsHDR = Flag("hdr", _hdr);
                    _urp.renderScale = Number("scale", BoxGraphics.Current.Scale);
                    _urp.mainLightShadowmapResolution = (int)Number("shadowres", _shadowRes);
                    _urp.shadowDistance = Number("shadowdist", _shadowDistance);
                }
                if (_settings.ContainsKey("fps")) Application.targetFrameRate = (int)Number("fps", BoxGraphics.Current.Fps);
                else BoxGraphics.RestoreFps();  // the scene's own rate, idle or not
                string model = Text("model");
                MoveIt.Motion.AndroidMotionService.SetModel(model == "" ? "auto" : model);

                foreach (var cam in Camera.allCameras)
                {
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (!_cameras.TryGetValue(cam, out var built))
                        _cameras[cam] = built = (data.renderShadows, data.renderPostProcessing, cam.clearFlags);
                    data.renderShadows = Flag("shadows", built.shadows);
                    data.renderPostProcessing = Flag("post", built.post);
                    cam.clearFlags = built.clear == CameraClearFlags.Skybox && !Flag("sky", true) ? CameraClearFlags.SolidColor : built.clear;
                }

                foreach (var r in _hidden)
                    if (r != null) r.enabled = true;
                _hidden.Clear();
                Hide();

                foreach (var pair in _casting)
                    if (pair.Key != null) pair.Key.shadowCastingMode = pair.Value;
                _casting.Clear();
                if (_settings.TryGetValue("nocast", out string cast) && cast != "")
                {
                    var tokens = cast.ToLowerInvariant().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                        if (r.shadowCastingMode != ShadowCastingMode.Off && Matches(r, tokens))
                        {
                            _casting[r] = r.shadowCastingMode;
                            r.shadowCastingMode = ShadowCastingMode.Off;
                        }
                }

                // Volume profiles are assets shared by the scenes: restore what the build had, then switch off
                foreach (var pair in _effects)
                    if (pair.Key != null) pair.Key.active = pair.Value;
                foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                {
                    var profile = volume.sharedProfile;
                    if (profile == null) continue;
                    Effect<Bloom>(profile, "bloom");
                    Effect<Vignette>(profile, "vignette");
                    Effect<ColorAdjustments>(profile, "grade");
                }

                foreach (var b in _disabled)
                    if (b != null) b.enabled = true;
                _disabled.Clear();
                if (_settings.TryGetValue("disable", out string scripts) && scripts != "")
                {
                    var names = new HashSet<string>(scripts.Split(','), StringComparer.OrdinalIgnoreCase);
                    foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                        if (b.enabled && names.Contains(b.GetType().Name))
                        {
                            b.enabled = false;
                            _disabled.Add(b);
                        }
                }

                string tag = Text("tag");
                Debug.Log($"[PerfLab] {(tag == "" ? "build settings" : tag)}: hdr {_urp?.supportsHDR}, scale {_urp?.renderScale:0.00}, " +
                          $"shadows {Text("shadows", "as built")}, post {Text("post", "as built")}, sky {Flag("sky", true)}, " +
                          $"fps {Application.targetFrameRate}, hide '{Text("hide")}', bloom {Flag("bloom", true)}, " +
                          $"vignette {Flag("vignette", true)}, grade {Flag("grade", true)}, disabled {_disabled.Count} '{Text("disable")}'");
                PerfLog.Restart(tag);
            }

            /// <summary>Sporte! and other activity games: starts the named event from their menu (the tests' way in).</summary>
            void StartActivity()
            {
                string id = Text("activity");
                if (id == "") return;
                var game = FindFirstObjectByType<ActivityGame>();
                if (game == null || !game.enabled || game.Current != ActivityGame.Phase.Choose) return;
                for (int i = 0; i < game.Activities.Count; i++)
                    if (game.Activities[i].Id == id)
                    {
                        game.SkipHowTo = true;
                        game.StartActivity(i);
                        return;
                    }
            }

            void Hide()
            {
                if (!_settings.TryGetValue("hide", out string list) || list == "") return;
                var tokens = list.ToLowerInvariant().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!r.enabled || !Matches(r, tokens)) continue;
                    r.enabled = false;
                    _hidden.Add(r);
                }
            }

            static bool Matches(Renderer r, string[] tokens)
            {
                foreach (string raw in tokens)
                {
                    string token = raw.Trim();
                    if (token == "particles" && r is ParticleSystemRenderer) return true;
                    if (token == "skinned" && r is SkinnedMeshRenderer) return true;
                    if (token == "transparent")
                    {
                        foreach (var m in r.sharedMaterials)
                            if (m != null && m.renderQueue >= (int)RenderQueue.Transparent) return true;
                        continue;
                    }
                    for (var t = r.transform; t != null; t = t.parent)
                        if (t.name.ToLowerInvariant().Contains(token)) return true;
                }
                return false;
            }

            void Effect<T>(VolumeProfile profile, string key) where T : VolumeComponent
            {
                if (!profile.TryGet<T>(out var effect)) return;
                if (!_effects.ContainsKey(effect)) _effects[effect] = effect.active;
                effect.active = _effects[effect] && Flag(key, true);
            }

            bool Flag(string key, bool fallback) => _settings.TryGetValue(key, out string v) ? v != "0" : fallback;

            string Text(string key, string missing = "") => _settings.TryGetValue(key, out string v) ? v : missing;

            float Number(string key, float fallback) =>
                _settings.TryGetValue(key, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                    ? f : fallback;
        }
    }
}
