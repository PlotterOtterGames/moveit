using System;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace MoveIt.Games
{
    /// <summary>
    /// The frame rate on the box: one <c>[Perf]</c> line every 10 s in every scene (on the box: <c>adb logcat -s Unity</c>).
    /// Frames per second against the target and the share of frames shown at least one refresh longer than planned, the
    /// worst frame, the share of frames that missed 50 and 30 fps, the average main thread and
    /// render thread time per frame and how long the main thread waited to present (frame timing stats, switched on by
    /// BoxBuild: a long wait with little work means the GPU limits the game), GPU time where the driver reports it,
    /// garbage collections and memory. Loading a scene and its first frames don't count.
    /// </summary>
    static class PerfLog
    {
        const float Interval = 10f;
        const int SkipFrames = 30;

        static string _tag = "";
        static bool _restart;

        /// <summary>Starts a new measurement (after the settling frames), its lines marked with the tag (PerfLab).</summary>
        public static void Restart(string tag)
        {
            _tag = tag ?? "";
            _restart = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            Debug.Log($"[Perf] {SystemInfo.deviceModel}: {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceVersion}, " +
                      $"{SystemInfo.processorCount} cores, {SystemInfo.systemMemorySize} MB, {Screen.width}x{Screen.height} at " +
                      $"{Screen.currentResolution.refreshRateRatio.value:0.#} Hz, frame timing {(FrameTimingManager.IsFeatureEnabled() ? "on" : "off")}");
            var go = new GameObject("PerfLog") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Meter>();
        }

        sealed class Meter : MonoBehaviour
        {
            readonly FrameTiming[] _timing = new FrameTiming[1];
            string _scene;
            int _skip, _frames, _over20, _over34, _missed, _timed, _gpuTimed, _collections;
            float _time, _worst;
            double _main, _wait, _render, _gpu;

            void Update()
            {
                FrameTimingManager.CaptureFrameTimings();
                string scene = SceneManager.GetActiveScene().name;
                if (scene != _scene || _restart)
                {
                    _scene = scene;
                    _skip = SkipFrames;
                    _restart = false;
                }
                if (_skip > 0)
                {
                    if (--_skip == 0) Restart();
                    return;
                }

                float dt = Time.unscaledDeltaTime;
                _frames++;
                _time += dt;
                _worst = Mathf.Max(_worst, dt);
                if (dt > 0.020f) _over20++;
                if (dt > 0.034f) _over34++;
                int target = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
                if (dt > 1.25f / target) _missed++;  // a frame shown for at least one refresh longer than planned
                if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                {
                    var t = _timing[0];
                    _timed++;
                    // Kept apart: the wait belongs to another frame than the main thread's time (on the X98K Pro,
                    // GPU-bound, it was longer than it), so their difference isn't the work.
                    _main += t.cpuMainThreadFrameTime;
                    _wait += t.cpuMainThreadPresentWaitTime;
                    _render += t.cpuRenderThreadFrameTime;
                    if (t.gpuFrameTime > 0)
                    {
                        _gpu += t.gpuFrameTime;
                        _gpuTimed++;
                    }
                }
                if (_time >= Interval)
                {
                    Write();
                    Restart();
                }
            }

            void Restart()
            {
                _frames = _over20 = _over34 = _missed = _timed = _gpuTimed = 0;
                _time = _worst = 0f;
                _main = _wait = _render = _gpu = 0;
                _collections = GC.CollectionCount(0);
            }

            void Write()
            {
                string timing = _timed == 0 ? "" :
                    $" | main {_main / _timed:0.0} ms, present wait {_wait / _timed:0.0} ms, render {_render / _timed:0.0} ms, " +
                    (_gpuTimed > 0 ? $"GPU {_gpu / _gpuTimed:0.0} ms" : "GPU -");
                int target = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
                Debug.Log($"[Perf] {_scene}{(_tag == "" ? "" : $" [{_tag}]")}: {_frames / _time:0.0} fps (target {target}{(BoxGraphics.Idle ? " idle" : "")}, " +
                          $"{100f * _missed / _frames:0.0}% missed), worst {_worst * 1000f:0} ms, " +
                          $"{100f * _over20 / _frames:0.0}% over 20 ms, {100f * _over34 / _frames:0.0}% over 34 ms{timing} | " +
                          $"GC {GC.CollectionCount(0) - _collections}, managed {GC.GetTotalMemory(false) >> 20} MB, " +
                          $"native {Profiler.GetTotalAllocatedMemoryLong() >> 20} MB");
            }
        }
    }
}
