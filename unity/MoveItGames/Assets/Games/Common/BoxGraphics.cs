using MoveIt.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MoveIt.Games
{
    /// <summary>
    /// Render settings per game on the TV box (Android players only; the editor keeps the full look), from measurements
    /// on the first box, the X98K Pro (RK3566, Mali-G52 at 700 MHz; docs/hardware.md, 2026-10-30). Its GPU limits every
    /// 3D game: post-processing (bloom + grading + vignette) cost ~13 ms a frame and the 3D scenes can't reach 60 fps
    /// without it either, so they run without post at a steady 30 fps (frame pacing is on in the build); Sporte! also
    /// without real-time shadows (its athletes draw blob shadows then) and at a lower resolution. The home screen
    /// and the 2D games render at full resolution with HDR off, which lets URP draw straight to the screen instead of
    /// into a texture copied there afterwards (the copy alone kept the home screen under 60 fps).
    /// A scene can instead draw fewer pixels straight into the screen's buffer (<see cref="Settings.ScreenScale"/>,
    /// <see cref="Screen.SetResolution"/>): the TV's display hardware scales them up for nothing, where URP's render scale
    /// draws into a texture and copies it to the screen on the GPU (Tenis!, 2026-11-06: +16 % at the same resolution).
    /// The pose model is part of the same budget: the NPU and the GPU share the memory bus, and with the full model
    /// (~1.2 GB/s at 30 poses/s against lite's ~0.8) Vrapo! fell from 29.6–29.9 to 25–29 fps (2026-11-01), so the heavy
    /// 3D games use lite and the other scenes full (The Link's model: steadier arms, far steadier 3D joints).
    /// </summary>
    public static class BoxGraphics
    {
        /// <summary>Render scale, post-processing and real-time shadows kept or not, the target frame rate and the pose
        /// model of one scene.</summary>
        public readonly struct Settings
        {
            public readonly float Scale;
            public readonly bool Post;
            public readonly int Fps;
            public readonly bool Shadows;
            /// <summary>A player in front of the camera keeps the full frame rate (else only the remote does).</summary>
            public readonly bool BodyKeepsFps;
            /// <summary>"auto" (the full model, lite only if the NPU can't keep up) or "lite".</summary>
            public readonly string Model;
            /// <summary>Two players: the NPU's landmark runs per second shared by them (30 = they take turns, each ~15
            /// poses a second: the NPU as busy as with one player; 0 = both every frame).</summary>
            public readonly float TwoPlayerRuns;
            /// <summary>The scale while the screen is split for two players (<see cref="SetSplit"/>): the scene is drawn
            /// twice, so less resolution. With <see cref="ScreenScale"/> it's the screen's own resolution too (the surface
            /// is resized as a two-player match starts and ends), else URP's render scale.</summary>
            public readonly float SplitScale;
            /// <summary><see cref="Scale"/> is the screen's own resolution (<see cref="Screen.SetResolution"/>: the game
            /// draws that many pixels straight into the screen's buffer and the TV's display hardware scales them up)
            /// instead of URP's render scale (drawn into a texture, then copied to the screen on the GPU).</summary>
            public readonly bool ScreenScale;
            /// <summary>Tundu/Sky's lighter clouds (two octaves of noise, the global keyword TUNDU_LITE) all the time, not
            /// only in a split screen; the text keeps its outlines (<see cref="ApplyLite"/>).</summary>
            public readonly bool LiteSky;

            public Settings(float scale, bool post, int fps, bool shadows = true, bool bodyKeepsFps = true,
                            string model = "auto", float twoPlayerRuns = 30f, float splitScale = 0f, bool screenScale = false,
                            bool liteSky = false)
            {
                Scale = scale;
                Post = post;
                Fps = fps;
                Shadows = shadows;
                BodyKeepsFps = bodyKeepsFps;
                Model = model;
                TwoPlayerRuns = twoPlayerRuns;
                SplitScale = splitScale > 0f ? splitScale : scale;
                ScreenScale = screenScale;
                LiteSky = liteSky;
            }
        }

        // Every scene drawn below the screen's resolution draws straight into the screen's buffer at that resolution and
        // the TV's display hardware scales it up (ScreenScale), since 2026-11-06: the same pixels as URP's render scale
        // without its texture and copy. On the hot box (83 degrees C, the GPU at 200-270 MHz), uncapped, as built against
        // the scaler: Vrapo! 21.2 -> 24.6 fps, Shpata! 21.5 -> 25.0, Fluturo! 22.7 -> 28.6, Vishu! 20.4 -> 24.0, Sporte!'s
        // football 18.5 -> 18.5 at a clock the thermal cap had dropped from 250 to 217 MHz (+15 % a MHz); the HUD's small
        // text a touch softer (docs/hardware.md "Tenis!'s frame on the box")
        public static Settings For(string scene) => scene switch
        {
            "Runner" or "Sword" => new Settings(0.8f, false, 30, model: "lite", screenScale: true),
            // Superboks! (2026-11-18, it replaced Fluturo!): a ring and two chibis, the stage one mesh and its glows; the full
            // pose model (auto: lite only if the NPU can't keep up), because the guard holds the hands in front of the face
            // and chest, where lite's wrists jump most (Zana!'s measurements, docs/hardware.md "Zana!'s wand jumps"). Not
            // measured on the box yet
            "Boxing" => new Settings(0.75f, false, 30, model: "auto", screenScale: true),
            // Tenis!: Wii's camera looks down on the court, so nearly every pixel is shadow-receiving grass: without
            // real-time shadows (blob shadows under the players instead) 28.8-29 fps against 27.5 at 80 degrees C with
            // four chibis and the camera AI tracking (2026-11-05). Two players: the NPU's runs shared (30 a second: as
            // busy as with one player, the game as fast). Since 2026-11-06 drawn at 0.75 of the screen's resolution
            // straight into the screen's buffer, the TV's display hardware scaling it up (ScreenScale): at the hot box's
            // 200-250 MHz, uncapped, 28.4 fps against 23.4 with URP's 0.8 render scale and its copy (21.0 before the
            // blit type was Auto); the HUD text a touch softer. The split screen at 0.6 since 2026-11-12 (the scene drawn
            // twice; a live two-player match at the hot box's 200 MHz, uncapped: 19.6 fps at 0.75, 26.5 at 0.6, 29.9 at
            // 0.6 without the crowd; docs/hardware.md "Two players on a hot box")
            "Tennis" => new Settings(0.75f, false, 30, shadows: false, model: "lite", twoPlayerRuns: 30f, splitScale: 0.6f, screenScale: true),
            // Volejboll! (2026-11-07): Tenis!'s settings (four chibis, the sun's shadows drawn as soft shapes, two players'
            // NPU runs shared, the split screen at 0.6), on Sporte!'s beach (its crowd, the sea)
            "Volley" => new Settings(0.75f, false, 30, shadows: false, model: "lite", twoPlayerRuns: 30f, splitScale: 0.6f, screenScale: true),
            // Gara! (2026-11-08): Volejboll!'s settings (six chibis in karts, the computer's in low detail, the sun's shadows
            // drawn as soft shapes, two players' NPU runs shared, the split screen at 0.6 as the screen's resolution: it was
            // 0.7 as URP's render scale, a texture copied to the screen on top)
            "Kart" => new Settings(0.75f, false, 30, shadows: false, model: "lite", twoPlayerRuns: 30f, splitScale: 0.6f, screenScale: true),
            // Lunapark! (2026-11-09): sprites over the camera picture like Vishu! (one sheet, three batches, no 3D), so the
            // full pose model (steadier hands: the booths are all about where the hands are); not measured on the box yet
            "Arcade" => new Settings(0.8f, false, 30, screenScale: true),
            // Yjet! (2026-11-14): a 3D chase like Vrapo! and Gara! (lite pose model, no post, 30 fps), but mostly sky: the
            // planets one texture read a pixel, the stars one mesh, the things instanced; two players share one view (no
            // split screen), their NPU runs shared. Not measured on the box yet
            "Space" => new Settings(0.8f, false, 30, shadows: false, model: "lite", twoPlayerRuns: 30f, screenScale: true),
            // Zana! (2026-11-15): a 3D chase like Yjet! (lite pose model, no post, 30 fps; the creatures one mesh each,
            // the props instanced, the ground in chunks); two fairies share one view (no split screen), their NPU runs
            // shared. The shapes drawn in the air need the hands: full would be steadier, lite keeps the frame. On the box
            // (2026-11-16, the hot box's 200 MHz, uncapped, the game playing itself): 10.4 fps with the lands lit per pixel
            // at 0.8; lit per vertex ~17.5; the sky's two-octave clouds +20 %, 0.7 of the screen +28 % on top of that.
            // Tenis!'s 0.75 and the lighter clouds (the text keeps its outlines). The full pose model since 2026-11-17 (the
            // user: "hand tracking is not working correctly, especially when the hand goes over the body"): the wand is the
            // whole game, and on the box's own recording of the routine full put the wand > 150 px off the heavy model's
            // answer in 0.6 % of the frames over the body against lite's 3.3 % (tools/motion/wand_replay.py)
            "Fairy" => new Settings(0.75f, false, 30, shadows: false, model: "auto", twoPlayerRuns: 30f, screenScale: true, liteSky: true),
            // Sporte!: every surface sampling the shadow map cost ~14 ms a frame (the athletes get blob shadows instead),
            // and up to nine athletes in a full stadium need a little less resolution
            "Sports" => new Settings(0.72f, false, 30, shadows: false, model: "lite", screenScale: true),
            // Vishu!: the camera picture behind the clothes is 640 px wide, so 70 % loses nothing there (the clothes get a
            // touch softer, the labels too since the screen scale: legible); at full size the GPU barely made 30 fps and
            // any throttling took it to 10-17
            "Dress" => new Settings(0.7f, false, 30, screenScale: true),
            // the home screen: 60 fps while the remote is in use (the selection slides); steps and raised hands arrive at
            // the camera's 30 fps, and with someone in front its main thread needed ~17 ms a frame (43 fps, 39 % of the
            // frames late), so a steady 30 then
            "Menu" => new Settings(1f, false, 60, bodyKeepsFps: false),
            // the camera test (Settings → Kamera): its IMGUI (the full-screen skeleton, the latency graph, the panel)
            // needs 18-21 ms of the main thread, so at 60 it ran 34-45 fps with 53-70 % of the frames late, and the
            // skeleton juddered on the TV (a screen recording: a joint stalled or lurched in ~27 % of the moving frames)
            "MotionDebug" => new Settings(1f, false, 30),
            _ => new Settings(1f, false, 60),  // Bowling, Pikturo!
        };

        /// <summary>The loaded scene's settings (PerfLab starts from them).</summary>
        public static Settings Current { get; private set; } = For("");

        /// <summary>
        /// A 60 fps scene nobody uses (no key for <see cref="IdleAfter"/> s, and nobody in front of the camera where
        /// <see cref="Settings.BodyKeepsFps"/>) runs at this rate: the box sits on the home screen most of the time, and
        /// there the main thread, the render thread and the GPU did ~half of their work for frames nobody needed (heat).
        /// A key, or someone stepping in, brings 60 back at once.
        /// </summary>
        public const int IdleFps = 30;
        public const float IdleAfter = 10f;

        /// <summary>True while a 60 fps scene runs at <see cref="IdleFps"/> because nobody uses it.</summary>
        public static bool Idle { get; private set; }

        static float _lastActive;
        static int _appliedFps;
        static bool _split;
        static Vector2Int _native;

        /// <summary>
        /// PerfLab's say over the screen's resolution: 0 = the scene's settings, -1 = the scene's scale as the screen's
        /// resolution (<see cref="Settings.ScreenScale"/>) whatever the scene says, 1 = the screen's own resolution with
        /// the render scale, another factor = that as the screen's resolution.
        /// </summary>
        public static float ScreenOverride;

        /// <summary>The scale the scene wants now: its own, or the split screen's.</summary>
        static float Wanted => _split ? Current.SplitScale : Current.Scale;

        /// <summary>The factor of the screen's own resolution the game draws into (1 = all of it).</summary>
        static float ScreenFactor => ScreenOverride > 0f ? ScreenOverride
            : ScreenOverride < 0f || Current.ScreenScale ? Wanted : 1f;

        /// <summary>The render scale URP should draw at: the scene's (or the split screen's), less whatever the screen's
        /// resolution already takes off.</summary>
        public static float RenderScale => ScreenFactor < 1f ? Mathf.Min(1f, Wanted / ScreenFactor) : Wanted;

        /// <summary>The screen resolution asked for (PerfLab's log).</summary>
        public static string ScreenSize => $"{Mathf.RoundToInt(Native.x * ScreenFactor)}x{Mathf.RoundToInt(Native.y * ScreenFactor)}";

        static Vector2Int Native
        {
            get
            {
                if (_native.x <= 0)
                    _native = Display.main != null && Display.main.systemWidth > 0
                        ? new Vector2Int(Display.main.systemWidth, Display.main.systemHeight)
                        : new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
                return _native;
            }
        }

        /// <summary>Sets the screen's resolution for the scene (<see cref="Settings.ScreenScale"/>, PerfLab's
        /// <see cref="ScreenOverride"/>): Android players only, and only when it changes (the surface is resized).</summary>
        public static void ApplyScreen()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            float f = Mathf.Clamp(ScreenFactor, 0.4f, 1f);
            int w = Mathf.RoundToInt(Native.x * f / 2f) * 2, h = Mathf.RoundToInt(Native.y * f / 2f) * 2;
            if (Screen.width != w || Screen.height != h) Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            SceneManager.sceneLoaded += (scene, mode) => Apply(scene.name);
            InputSystem.onAnyButtonPress.Call(_ => _lastActive = Time.unscaledTime);
            var go = new GameObject("Box Graphics") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<IdleWatch>();
        }

        static void Apply(string scene)
        {
            Current = For(scene);
            AndroidMotionService.SetModel(Current.Model);
            AndroidMotionService.SetPeople(1);   // a two-player game asks for two itself
            _split = false;
            ApplyScreen();
            ApplyLite();
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = RenderScale;
            Application.targetFrameRate = _appliedFps = Current.Fps;
            Idle = false;
            _lastActive = Time.unscaledTime;
            foreach (var cam in Camera.allCameras)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing &= Current.Post;
                data.renderShadows &= Current.Shadows;
            }
            StillVolumes();
        }

        static bool _volumesLater;

        /// <summary>
        /// Without post-processing nothing reads the volumes, so URP needn't blend them for the scene's cameras every
        /// frame (~2 % of Gara!'s main thread in its split screen, two cameras; 2026-11-13). The first scene loads before
        /// URP has made its volume manager: then on the first frame (<see cref="IdleWatch"/>).
        /// </summary>
        static void StillVolumes()
        {
            _volumesLater = !VolumeManager.instance.isInitialized;
            if (_volumesLater) return;
            foreach (var cam in Camera.allCameras)
                if (!cam.GetUniversalAdditionalCameraData().renderPostProcessing)
                    cam.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);
        }

        /// <summary>
        /// The two-player split screen on or off (Tenis!, Volejboll! and Gara! versus): the scene is drawn twice, so it
        /// draws at <see cref="Settings.SplitScale"/> while it's on: as the screen's own resolution where the scene uses
        /// <see cref="Settings.ScreenScale"/> (the surface is resized once as the match starts and once as it ends), else as
        /// URP's render scale. Android players only, like the rest of these settings.
        /// </summary>
        public static void SetSplit(bool split)
        {
            if (Application.platform != RuntimePlatform.Android || split == _split) return;
            _split = split;
            ApplyScreen();
            ApplyLite();
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = RenderScale;
        }

        /// <summary>PerfLab's say over the split screens' lighter shader paths (<see cref="ApplyLite"/>): -1 = on with the
        /// split screen, 0 = never, 1 = always.</summary>
        public static int LiteOverride = -1;

        /// <summary>
        /// The lighter paths while the screen is split (2026-11-13: everything is drawn twice there): the global shader
        /// keyword TUNDU_LITE (Tundu/Sky's clouds from two octaves of noise instead of four, Tundu/Water without its
        /// drifting foam streaks) and <see cref="SportHud.LiteOutlines"/> (outlined text with one drop shadow); a scene with
        /// <see cref="Settings.LiteSky"/> keeps the keyword all the time (its text keeps the outlines). Android players only
        /// (the editor keeps the full look).
        /// </summary>
        public static void ApplyLite()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            bool lite = LiteOverride >= 0 ? LiteOverride == 1 : _split;
            // a scene with LiteSky keeps the lighter clouds all the time (PerfLab lite=0 turns them off to compare)
            if (lite || LiteOverride < 0 && Current.LiteSky) Shader.EnableKeyword("TUNDU_LITE");
            else Shader.DisableKeyword("TUNDU_LITE");
            SportHud.LiteOutlines = lite;
        }

        /// <summary>Back to the scene's own frame rate (idle or not), after something else set one (PerfLab).</summary>
        public static void RestoreFps() => Application.targetFrameRate = _appliedFps = Idle ? IdleFps : Current.Fps;

        sealed class IdleWatch : MonoBehaviour
        {
            void Update()
            {
                if (_volumesLater) StillVolumes();
                // a frame rate set by someone else (PerfLab) is left alone
                if (Current.Fps <= IdleFps || Application.targetFrameRate != _appliedFps) return;
                if (Current.BodyKeepsFps && MotionSystem.Instance != null && MotionSystem.Instance.ActiveBody != null)
                    _lastActive = Time.unscaledTime;
                bool idle = Time.unscaledTime - _lastActive > IdleAfter;
                if (idle == Idle) return;
                Idle = idle;
                Application.targetFrameRate = _appliedFps = idle ? IdleFps : Current.Fps;
            }
        }
    }
}
