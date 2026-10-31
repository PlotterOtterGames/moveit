using System;
using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// A hand cursor (or any joint, 2D or 3D) that moves smoothly every frame from camera samples (~30 a second, noisy,
    /// arriving unevenly).
    /// Samples are kept on the <b>camera's clock</b> (capture time), and every frame the cursor is placed at "now minus
    /// the measured latency minus <see cref="Margin"/> camera frames": between two real samples it is interpolated,
    /// past the newest one it glides on at the hand's speed (at most <see cref="MaxExtrapolation"/>) or waits there.
    /// Then a light per-frame One Euro filter takes off the camera noise.
    ///
    /// Why the camera clock: timing a sample by the game frame that happened to receive it put each one 0–25 ms off
    /// (uneven inference time, 60 Hz frames), and a hand moving fast covers 50+ px in that time, so the cursor sped up
    /// and stalled on every sample. Feed it raw samples: smoothing them first (the tracker's low-lag track) and again
    /// here made fast moves uneven, because each filter's lag changes with its own noisy speed estimate.
    ///
    /// Settings (tools/motion/cursor_sim.py):
    /// <list type="bullet">
    /// <item>The hand cursors (<see cref="BodyHands"/>, Pikturo's and Vishu!'s camera cursors): Margin 0.5, glide 80 ms,
    /// filter 1 / 4 / 2 (tuned for Fruta's slashes, the game Vishu! replaced).</item>
    /// <item>Pikturo's brush: Margin 1, no glide, <see cref="FitWindow"/> 0.25 s, filter 2 / 4 / 2: a parabola through
    /// the last quarter second of samples, drawn one camera frame back, so it never glides past the point where the hand
    /// stopped (2026-10-19: as smooth as the old brush, which filtered the low-lag track and then again here, with
    /// 20-25 ms less lag). On the user's painting recording (2026-10-20) it scored jerkier than the old brush (3.45 vs
    /// 2.51 px/frame^2 of excess acceleration, but ~20 ms quicker); a per-sample filter that scored best felt worse to
    /// the user than this one, so this stays.</item>
    /// </list>
    ///
    /// Each sample can carry a flag (Pikturo: "the other hand is raised"), returned for the same moment as the
    /// position, so a stroke starts and stops exactly where the brush was when the switch changed.
    /// </summary>
    public sealed class HandInterpolator
    {
        /// <summary>How many camera-frame intervals behind the newest sample the cursor runs (1 = only real samples,
        /// 0 = right at the newest one, gliding on between samples).</summary>
        public float Margin = 1f;
        /// <summary>Past the newest sample, glide on at the hand's speed at most this long (s); 0 = wait there.</summary>
        public float MaxExtrapolation;
        /// <summary>How much of the previous speed estimate is kept on each sample (0 = the last two samples only).</summary>
        public float VelocitySmoothing = 0.4f;
        /// <summary>
        /// Seconds of samples (newest back) that a parabola is fitted through by least squares, drawn at the cursor's
        /// moment: a moving regression (Savitzky-Golay on uneven sample times; the Kinect SDK's joint smoothing paper
        /// recommends one where a few frames of delay are fine). It weighs ~8 (30 fps) to ~15 (60 fps) samples, a hand
        /// moving at a steady speed is drawn where it was, not behind it (a filter lags), and the path has no corner
        /// at every sample, which keeps the One Euro filter after it steady. Never extrapolated past the newest
        /// sample. 0 = straight lines between samples.
        /// </summary>
        public float FitWindow;

        const int Capacity = 24;   // 0.25 s of a 60 fps camera, with room
        readonly double[] _times = new double[Capacity];
        readonly Vector3[] _positions = new Vector3[Capacity];
        readonly bool[] _flags = new bool[Capacity];
        readonly OneEuroFilter _x, _y, _z;
        int _count;
        double _offset;       // local clock minus camera clock (≈ the pipeline latency), smoothed
        float _interval = 1f / 30f;
        Vector3 _velocity;

        public HandInterpolator(float minCutoff = 4f, float beta = 1f, float derivativeCutoff = 1f)
        {
            _x = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
            _y = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
            _z = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
        }

        public bool HasSample => _count > 0;

        /// <summary>The hand's speed from the last samples (units per second).</summary>
        public Vector3 Velocity => _velocity;

        /// <summary>
        /// A camera sample: <paramref name="captureTime"/> on the camera's clock (seconds), the hand's position and a
        /// flag, received at <paramref name="localNow"/> (seconds, e.g. Time.unscaledTimeAsDouble).
        /// </summary>
        public void Sample(double captureTime, Vector3 position, bool flag, double localNow)
        {
            if (_count > 0)
            {
                double dt = captureTime - _times[_count - 1];
                if (dt <= 0) return;   // same or older frame
                _interval = Mathf.Lerp(_interval, Mathf.Clamp((float)dt, 0.01f, 0.1f), 0.1f);
                // (a burst of samples 1 ms apart would turn noise into speed; a long gap says nothing about now)
                if (dt >= 0.25) _velocity = Vector3.zero;
                else if (dt >= Body.MinSpeedInterval)
                    _velocity = Vector3.Lerp((position - _positions[_count - 1]) / (float)dt, _velocity, VelocitySmoothing);
            }
            double offset = localNow - captureTime;
            _offset = _count == 0 ? offset : _offset + (offset - _offset) * 0.1;
            if (_count == Capacity)
            {
                for (int i = 1; i < Capacity; i++)
                {
                    _times[i - 1] = _times[i];
                    _positions[i - 1] = _positions[i];
                    _flags[i - 1] = _flags[i];
                }
                _count--;
            }
            _times[_count] = captureTime;
            _positions[_count] = position;
            _flags[_count] = flag;
            _count++;
        }

        /// <summary>The cursor for this frame and the flag at the same moment (2D callers just use it as a Vector2).</summary>
        public Vector3 Evaluate(double localNow, float frameDt, out bool flag)
        {
            flag = false;
            if (_count == 0) return default;
            double t = localNow - _offset - Margin * _interval;   // a moment on the camera's clock
            Vector3 p;
            if (t <= _times[0])
            {
                p = _positions[0];
                flag = _flags[0];
            }
            else if (t >= _times[_count - 1])
            {
                // Past the newest sample: glide on (the next one is due) or wait there rather than guess.
                float ahead = Mathf.Min((float)(t - _times[_count - 1]), MaxExtrapolation);
                p = _positions[_count - 1] + _velocity * ahead;
                flag = _flags[_count - 1];
            }
            else
            {
                int i = 1;
                while (_times[i] < t) i++;
                float k = (float)((t - _times[i - 1]) / (_times[i] - _times[i - 1]));
                p = Vector3.Lerp(_positions[i - 1], _positions[i], k);
                flag = _flags[i - 1];
            }
            if (FitWindow > 0f && Fit(t, out var fitted)) p = fitted;
            return new Vector3(_x.Filter(p.x, frameDt), _y.Filter(p.y, frameDt), _z.Filter(p.z, frameDt));
        }

        /// <summary>The least-squares parabola through the samples of the last <see cref="FitWindow"/> seconds (at least
        /// 4), at camera time <paramref name="t"/> (clamped to the samples used).</summary>
        bool Fit(double t, out Vector3 p)
        {
            p = default;
            if (_count < 3) return false;
            double newest = _times[_count - 1];
            int first = _count - 1;
            while (first > 0 && _times[first - 1] >= newest - FitWindow) first--;
            first = Mathf.Min(first, Mathf.Max(0, _count - 4));
            // normal equations of a + b u + c u^2, u = time relative to the newest sample (seconds)
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
            Vector3d y0 = default, y1 = default, y2 = default;
            for (int i = first; i < _count; i++)
            {
                double u = _times[i] - newest, u2 = u * u;
                s0 += 1; s1 += u; s2 += u2; s3 += u2 * u; s4 += u2 * u2;
                y0.Add(_positions[i], 1);
                y1.Add(_positions[i], u);
                y2.Add(_positions[i], u2);
            }
            double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s3 * s2) + s2 * (s1 * s3 - s2 * s2);
            if (Math.Abs(det) < 1e-18) return false;   // samples at (nearly) the same moment
            double x = Math.Max(_times[first], Math.Min(newest, t)) - newest;
            // Cramer's rule, then a + b x + c x^2, for each axis
            double Axis(double r0, double r1, double r2)
            {
                double a = r0 * (s2 * s4 - s3 * s3) - s1 * (r1 * s4 - s3 * r2) + s2 * (r1 * s3 - s2 * r2);
                double b = s0 * (r1 * s4 - s3 * r2) - r0 * (s1 * s4 - s3 * s2) + s2 * (s1 * r2 - r1 * s2);
                double c = s0 * (s2 * r2 - r1 * s3) - s1 * (s1 * r2 - r1 * s2) + r0 * (s1 * s3 - s2 * s2);
                return (a + (b + c * x) * x) / det;
            }
            p = new Vector3((float)Axis(y0.X, y1.X, y2.X), (float)Axis(y0.Y, y1.Y, y2.Y), (float)Axis(y0.Z, y1.Z, y2.Z));
            return true;
        }

        struct Vector3d
        {
            public double X, Y, Z;

            public void Add(Vector3 v, double w)
            {
                X += v.x * w;
                Y += v.y * w;
                Z += v.z * w;
            }
        }

        /// <summary>Forget the hand (lost): the next sample starts fresh.</summary>
        public void Reset()
        {
            _count = 0;
            _velocity = Vector3.zero;
            _x.Reset();
            _y.Reset();
            _z.Reset();
        }
    }
}
