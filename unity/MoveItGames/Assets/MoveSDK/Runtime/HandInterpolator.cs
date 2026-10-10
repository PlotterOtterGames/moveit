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
    /// <item>Fruta and the hand cursors (<see cref="BodyHands"/>): Margin 0.5, glide 80 ms, filter 1 / 4 / 2.</item>
    /// <item>Pikturo's brush: Margin 1, no glide, filter 4 / 1 / 1: only real samples, so it never slides past the
    /// point where the hand stopped (lines and corners land where they were meant), for ~one camera frame more lag.</item>
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

        const int Capacity = 6;
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
            return new Vector3(_x.Filter(p.x, frameDt), _y.Filter(p.y, frameDt), _z.Filter(p.z, frameDt));
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
