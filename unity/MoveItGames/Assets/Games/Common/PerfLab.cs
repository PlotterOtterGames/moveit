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
    /// class to drop its IMGUI HUD; their Update stops too), <c>model</c> (the pose model: auto, full or lite),
    /// <c>camera</c> (the camera frame size the pose AI asks for, e.g. 1280x1024; the camera restarts), <c>video</c>
    /// (the width of the camera picture a game shows, instead of the game's own: 1024, 640, 512), <c>feed</c> copy (the
    /// camera picture copied through the game's memory as before 2026-11-12, not uploaded by the render thread), <c>people</c>
    /// (1 or 2 followed whatever the game asks), <c>npurate</c> (the NPU's landmark runs per second shared by two people,
    /// 0 = both every frame), <c>twin</c> 1 (one person's landmarks run twice: a second player's cost), <c>npubench</c> 1
    /// (with <c>people</c>: that many fixed crops run like people, their cost with nobody in front of the camera),
    /// <c>res</c> (a factor of the screen's own resolution: the game draws that many pixels straight into the screen's
    /// buffer and the TV's display hardware scales it up, instead of URP's render scale and a copy; use with
    /// <c>scale=1</c>), <c>boxres</c> 0/1 (<see cref="BoxGraphics"/>' own choice of the two, off or on), <c>census</c> 1
    /// (every 10 s, per camera, the renderers in its view by kind: the object under the scene's root they belong to,
    /// static-batched ones and LOD groups' apart; a guide to its draw calls, which the player can't count),
    /// <c>volumes</c> 1 (URP blends the volumes for every camera every frame again, as before 2026-11-13: without
    /// post-processing nothing reads them), <c>clouds</c> 0 (Tundu/Sky without its clouds), and switches read where they
    /// act (<see cref="PerfSwitches"/>: Tenis!'s <c>split</c> 1, Gara!'s <c>hudbatch</c> 0, <c>hudtext</c> 0,
    /// <c>merge</c> 0 (with a scene load)). A missing
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
            float _nextPoll, _nextCensus;
            Material _cloudSky;
            float _cloudsWere = -1f;

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
                    if (Flag("census", false) && Time.unscaledTime >= _nextCensus)
                    {
                        _nextCensus = Time.unscaledTime + 10f;
                        Census(Text("tag"));
                    }
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
                // the screen's resolution first (BoxGraphics' own, unless res= or boxres= say otherwise), then the render
                // scale on top of it
                BoxGraphics.ScreenOverride = _settings.ContainsKey("res") ? Number("res", 1f)
                    : _settings.ContainsKey("boxres") ? (Flag("boxres", true) ? -1f : 1f) : 0f;
                BoxGraphics.ApplyScreen();
                BoxGraphics.LiteOverride = _settings.ContainsKey("lite") ? (Flag("lite", true) ? 1 : 0) : -1;
                BoxGraphics.ApplyLite();
                if (_urp != null)
                {
                    _urp.supportsHDR = Flag("hdr", _hdr);
                    _urp.renderScale = Number("scale", BoxGraphics.RenderScale);
                    _urp.mainLightShadowmapResolution = (int)Number("shadowres", _shadowRes);
                    _urp.shadowDistance = Number("shadowdist", _shadowDistance);
                }
                if (_settings.ContainsKey("fps")) Application.targetFrameRate = (int)Number("fps", BoxGraphics.Current.Fps);
                else BoxGraphics.RestoreFps();  // the scene's own rate, idle or not
                string model = Text("model");
                MoveIt.Motion.AndroidMotionService.SetModel(model == "" ? BoxGraphics.Current.Model : model);
                MoveIt.Motion.AndroidMotionService.SetCameraSize(Text("camera"));   // e.g. camera=1280x1024
                MoveIt.Motion.CameraFeed.WidthOverride = (int)Number("video", 0f);  // the camera picture's width for the game
                MoveIt.Motion.CameraFeed.CopyFrames = Text("feed") == "copy";        // the picture copied through the game
                // two players: people=2 follows up to two whatever the game asks, npurate= the NPU's landmark runs per
                // second shared by them (0 = both every frame), twin=1 runs one person's landmarks twice (their cost)
                if (_settings.ContainsKey("people") || _settings.ContainsKey("npurate") || _settings.ContainsKey("twin") ||
                    _settings.ContainsKey("npubench"))
                    MoveIt.Motion.AndroidMotionService.PeopleOverride = ((int)Number("people", 2f),
                        Number("npurate", BoxGraphics.Current.TwoPlayerRuns), Flag("twin", false), Flag("npubench", false));
                else
                    MoveIt.Motion.AndroidMotionService.PeopleOverride = null;
                PerfSwitches.Set(_settings);
                // clouds=0: the sky without its clouds (what they cost); put back when the key goes
                var sky = RenderSettings.skybox;
                if (_settings.ContainsKey("clouds") && sky != null && sky.HasProperty("_Clouds"))
                {
                    if (_cloudSky != sky)
                    {
                        _cloudSky = sky;
                        _cloudsWere = sky.GetFloat("_Clouds");
                    }
                    sky.SetFloat("_Clouds", Flag("clouds", true) ? _cloudsWere : 0f);
                }
                else if (_cloudSky != null)
                {
                    _cloudSky.SetFloat("_Clouds", _cloudsWere);
                    _cloudSky = null;
                }

                foreach (var cam in Camera.allCameras)
                {
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (!_cameras.TryGetValue(cam, out var built))
                        _cameras[cam] = built = (data.renderShadows, data.renderPostProcessing, cam.clearFlags);
                    data.renderShadows = Flag("shadows", built.shadows);
                    data.renderPostProcessing = Flag("post", built.post);
                    // BoxGraphics stops the volumes' per-frame blending for cameras without post-processing (volumes=1
                    // brings it back, to compare)
                    if (VolumeManager.instance.isInitialized)
                        cam.SetVolumeFrameworkUpdateMode(data.renderPostProcessing || Flag("volumes", false)
                            ? VolumeFrameworkUpdateMode.EveryFrame : VolumeFrameworkUpdateMode.ViaScripting);
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
                Debug.Log($"[PerfLab] {(tag == "" ? "build settings" : tag)}: screen {Screen.width}x{Screen.height} (asked {BoxGraphics.ScreenSize}), " +
                          $"hdr {_urp?.supportsHDR}, scale {_urp?.renderScale:0.00}, " +
                          $"shadows {Text("shadows", "as built")}, post {Text("post", "as built")}, sky {Flag("sky", true)}, " +
                          $"fps {Application.targetFrameRate}, hide '{Text("hide")}', bloom {Flag("bloom", true)}, " +
                          $"vignette {Flag("vignette", true)}, grade {Flag("grade", true)}, disabled {_disabled.Count} '{Text("disable")}', " +
                          $"people {Text("people", "as the game asks")} npurate {Text("npurate", "-")} twin {Text("twin", "0")}");
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

            /// <summary>
            /// Per enabled camera, the renderers in its view (enabled, in its culling mask, bounds inside its frustum) by the
            /// object under the scene's root they belong to (digits dropped: "Road 60" counts as "Road"), with their
            /// triangles. Static-batched renderers are drawn in merged ranges (fewer draws than renderers) and a LOD group's
            /// renderers are counted at every level (the camera draws one): both are shown apart.
            /// </summary>
            static void Census(string tag)
            {
                var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                var planes = new Plane[6];
                var counts = new SortedDictionary<string, (int count, long triangles)>(StringComparer.Ordinal);
                var line = new System.Text.StringBuilder();
                foreach (var cam in Camera.allCameras)
                {
                    GeometryUtility.CalculateFrustumPlanes(cam, planes);
                    counts.Clear();
                    int total = 0;
                    foreach (var r in renderers)
                    {
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || (cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                        if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                        float cull = cam.layerCullDistances[r.gameObject.layer];
                        if (cull > 0f && (r.bounds.center - cam.transform.position).sqrMagnitude > cull * cull) continue;
                        string kind = Kind(r.transform);
                        if (r.isPartOfStaticBatch) kind += " (static)";
                        else if (r.GetComponentInParent<LODGroup>() != null) kind += " (lod)";
                        else if (r is ParticleSystemRenderer) kind += " (particles)";
                        else if (r is SkinnedMeshRenderer) kind += " (skinned)";
                        long triangles = 0;
                        Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                                  : !r.isPartOfStaticBatch && r.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                        if (mesh != null)
                            for (int i = 0; i < mesh.subMeshCount; i++) triangles += mesh.GetIndexCount(i) / 3;
                        counts.TryGetValue(kind, out var c);
                        counts[kind] = (c.count + 1, c.triangles + triangles);
                        total++;
                    }
                    line.Clear();
                    line.Append($"[Perf] census{(tag == "" ? "" : $" [{tag}]")} {cam.name}: {total} renderers in view");
                    foreach (var pair in counts)
                        line.Append($", {pair.Key} {pair.Value.count}{(pair.Value.triangles > 0 ? $" ({pair.Value.triangles / 1000f:0.#}k tris)" : "")}");
                    Debug.Log(line.ToString());
                }
            }

            /// <summary>"root/child": the scene root's first word and the object under it, digits dropped ("Track 1/Road 60" =
            /// "Track/Road", "Kart Ana/Pivot" = "Kart/Pivot").</summary>
            static string Kind(Transform t)
            {
                var top = t;
                while (top.parent != null && top.parent.parent != null) top = top.parent;
                var root = top.parent ?? top;
                string first = root.name;
                int space = first.IndexOf(' ');
                if (space > 0) first = first.Substring(0, space);
                return top == root ? first : first + "/" + Stripped(top.name);
            }

            static string Stripped(string name)
            {
                int end = name.Length;
                while (end > 0 && (char.IsDigit(name[end - 1]) || name[end - 1] == ' ' || name[end - 1] == '-')) end--;
                return end > 0 ? name.Substring(0, end) : name;
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
