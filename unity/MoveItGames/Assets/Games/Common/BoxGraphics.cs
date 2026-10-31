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
    /// </summary>
    public static class BoxGraphics
    {
        /// <summary>Render scale, post-processing and real-time shadows kept or not, and the target frame rate of one
        /// scene.</summary>
        public readonly struct Settings
        {
            public readonly float Scale;
            public readonly bool Post;
            public readonly int Fps;
            public readonly bool Shadows;
            /// <summary>A player in front of the camera keeps the full frame rate (else only the remote does).</summary>
            public readonly bool BodyKeepsFps;

            public Settings(float scale, bool post, int fps, bool shadows = true, bool bodyKeepsFps = true)
            {
                Scale = scale;
                Post = post;
                Fps = fps;
                Shadows = shadows;
                BodyKeepsFps = bodyKeepsFps;
            }
        }

        public static Settings For(string scene) => scene switch
        {
            "Runner" or "Sword" or "Fly" or "Tennis" => new Settings(0.8f, false, 30),
            // Sporte!: every surface sampling the shadow map cost ~14 ms a frame (the athletes get blob shadows instead),
            // and up to nine athletes in a full stadium need a little less resolution
            "Sports" => new Settings(0.72f, false, 30, shadows: false),
            // Vishu!: the camera picture behind the clothes is 640 px wide, so 70 % loses nothing there (the clothes get a
            // touch softer; the HUD stays sharp); at full size the GPU barely made 30 fps and any throttling took it to
            // 10-17
            "Dress" => new Settings(0.7f, false, 30),
            // the home screen: 60 fps while the remote is in use (the selection slides); steps and raised hands arrive at
            // the camera's 30 fps, and with someone in front its main thread needed ~17 ms a frame (43 fps, 39 % of the
            // frames late), so a steady 30 then
            "Menu" => new Settings(1f, false, 60, bodyKeepsFps: false),
            _ => new Settings(1f, false, 60),  // Bowling, Pikturo!, the camera test
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
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = Current.Scale;
            Application.targetFrameRate = _appliedFps = Current.Fps;
            Idle = false;
            _lastActive = Time.unscaledTime;
            foreach (var cam in Camera.allCameras)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing &= Current.Post;
                data.renderShadows &= Current.Shadows;
            }
        }

        /// <summary>Back to the scene's own frame rate (idle or not), after something else set one (PerfLab).</summary>
        public static void RestoreFps() => Application.targetFrameRate = _appliedFps = Idle ? IdleFps : Current.Fps;

        sealed class IdleWatch : MonoBehaviour
        {
            void Update()
            {
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
