using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// A whole body's joints to draw this frame, from camera samples that arrive ~30 times a second and unevenly: the
    /// samples are kept on the <b>camera's clock</b> (capture time) and every frame the body is drawn at "now minus the
    /// measured latency minus <see cref="Margin"/> camera frames", interpolated between the two samples around that
    /// moment, or past the newest one gliding on at the joints' speed for at most <see cref="MaxExtrapolation"/>.
    /// <see cref="HandInterpolator"/>'s timing for every joint at once, without its filter: what it's given is filtered
    /// already (<see cref="Body.Joints"/>), so the body looks the same, only without the steps.
    ///
    /// Why (tools/motion/frame_timing_sim.py, the X98K Pro's timing: a 29.8 fps camera, the NPU ~23 ms a frame): drawn
    /// as they arrive, the poses move in camera-rate steps. On a 60 fps screen each pose of a 30 fps camera stays up for
    /// two frames and then jumps two frames' worth, on every frame of a move. On a 30 fps screen the camera and the game
    /// tick on separate clocks, so whenever the poses land near the start of a frame, one frame gets none and the next
    /// gets two: ~9 % of the frames stall or jump (more when the AI loses the player and the detector runs). Here every
    /// frame moves the same distance on a joint moving at a steady speed (within a few %).
    /// </summary>
    public sealed class JointTimeline
    {
        /// <summary>How many camera-frame intervals behind the newest sample the body is drawn (0.5: half a frame, gliding
        /// on when the next sample is late, like the hand cursors).</summary>
        public float Margin = 0.5f;
        /// <summary>Past the newest sample, glide on at the joints' speed at most this long (s).</summary>
        public float MaxExtrapolation = 0.08f;
        /// <summary>How much of the previous speed estimate is kept on each sample.</summary>
        public float VelocitySmoothing = 0.4f;

        const int Capacity = 6;
        readonly int _joints;
        readonly double[] _times = new double[Capacity];
        readonly Vector3[][] _samples = new Vector3[Capacity][];
        readonly Vector3[] _velocity;
        int _count;
        double _offset;       // local clock minus camera clock (the pipeline's latency), smoothed
        float _interval = 1f / 30f;

        public JointTimeline(int joints = Skeleton.JointCount)
        {
            _joints = joints;
            _velocity = new Vector3[joints];
            for (int i = 0; i < Capacity; i++) _samples[i] = new Vector3[joints];
        }

        public bool HasSample => _count > 0;
        /// <summary>Capture time of the newest sample (camera clock, seconds).</summary>
        public double Newest => _count > 0 ? _times[_count - 1] : 0;
        /// <summary>The camera's frame interval, smoothed (s).</summary>
        public float Interval => _interval;

        /// <summary>A camera sample: the joints at <paramref name="captureTime"/> (camera clock, seconds), handled at
        /// <paramref name="localNow"/> (seconds, e.g. Time.unscaledTimeAsDouble). Older or repeated samples are ignored.</summary>
        public void Add(double captureTime, Vector3[] joints, double localNow)
        {
            // more than a second away from the newest sample, either way: the player was gone for a while, or the
            // sender's clock was set (the capture times are Unix time): start over rather than hold or replay old poses
            if (_count > 0 && System.Math.Abs(captureTime - _times[_count - 1]) > 1.0) Clear();
            if (_count > 0)
            {
                double dt = captureTime - _times[_count - 1];
                if (dt <= 0) return;   // same or older frame
                _interval = Mathf.Lerp(_interval, Mathf.Clamp((float)dt, 0.01f, 0.1f), 0.1f);
                var last = _samples[_count - 1];
                for (int j = 0; j < _joints; j++)
                {
                    // a long gap says nothing about now; samples 1 ms apart would turn noise into speed
                    if (dt >= 0.25) _velocity[j] = Vector3.zero;
                    else if (dt >= Body.MinSpeedInterval)
                        _velocity[j] = Vector3.Lerp((joints[j] - last[j]) / (float)dt, _velocity[j], VelocitySmoothing);
                }
            }
            double offset = localNow - captureTime;
            _offset = _count == 0 ? offset : _offset + (offset - _offset) * 0.1;
            if (_count == Capacity)
            {
                var reuse = _samples[0];
                for (int i = 1; i < Capacity; i++)
                {
                    _times[i - 1] = _times[i];
                    _samples[i - 1] = _samples[i];
                }
                _samples[Capacity - 1] = reuse;
                _count--;
            }
            _times[_count] = captureTime;
            System.Array.Copy(joints, _samples[_count], _joints);
            _count++;
        }

        /// <summary>The joints to draw at <paramref name="localNow"/> into <paramref name="into"/>. False before the first
        /// sample.</summary>
        public bool Evaluate(double localNow, Vector3[] into)
        {
            if (_count == 0) return false;
            double t = localNow - _offset - Margin * _interval;   // a moment on the camera's clock
            if (t <= _times[0])
            {
                System.Array.Copy(_samples[0], into, _joints);
                return true;
            }
            if (t >= _times[_count - 1])
            {
                // past the newest sample: glide on (the next one is due) or wait there rather than guess
                float ahead = Mathf.Min((float)(t - _times[_count - 1]), MaxExtrapolation);
                var newest = _samples[_count - 1];
                for (int j = 0; j < _joints; j++) into[j] = newest[j] + _velocity[j] * ahead;
                return true;
            }
            int i = 1;
            while (_times[i] < t) i++;
            float k = (float)((t - _times[i - 1]) / (_times[i] - _times[i - 1]));
            var a = _samples[i - 1];
            var b = _samples[i];
            for (int j = 0; j < _joints; j++) into[j] = Vector3.LerpUnclamped(a[j], b[j], k);
            return true;
        }

        /// <summary>Forget the samples (the next one starts fresh).</summary>
        public void Clear()
        {
            _count = 0;
            for (int j = 0; j < _joints; j++) _velocity[j] = Vector3.zero;
        }
    }
}
