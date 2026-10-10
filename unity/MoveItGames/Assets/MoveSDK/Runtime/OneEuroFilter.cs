using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// One Euro filter (Casiez et al. 2012): strong smoothing when a joint is still, little lag when it moves
    /// fast. minCutoff lowers jitter at rest, beta lowers lag during fast motion.
    ///
    /// Pass a <c>scale</c> (e.g. the player's torso length) to make beta body-relative: the speed that opens the
    /// filter is then measured in body sizes per second, so a small child far away and an adult close to the camera
    /// get the same responsiveness.
    /// </summary>
    public sealed class OneEuroFilter
    {
        public float MinCutoff;
        public float Beta;
        public float DerivativeCutoff;

        float _x, _dx;
        bool _initialized;

        public OneEuroFilter(float minCutoff = 1.7f, float beta = 0.3f, float derivativeCutoff = 1f)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DerivativeCutoff = derivativeCutoff;
        }

        public void Reset() => _initialized = false;

        /// <summary>Smoothed rate of change of the input (units per second).</summary>
        public float Velocity => _initialized ? _dx : 0f;

        public float Filter(float value, float dt) => Filter(value, dt, 1f);

        /// <param name="scale">Units of one "body size": beta acts on speed / scale.</param>
        public float Filter(float value, float dt, float scale)
        {
            if (!_initialized || dt <= 0f)
            {
                _x = value;
                _dx = 0f;
                _initialized = true;
                return value;
            }

            float rawDx = (value - _x) / dt;
            _dx = Mathf.Lerp(_dx, rawDx, Alpha(DerivativeCutoff, dt));
            float cutoff = MinCutoff + Beta * Mathf.Abs(_dx) / Mathf.Max(scale, 1e-4f);
            _x = Mathf.Lerp(_x, value, Alpha(cutoff, dt));
            return _x;
        }

        static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }
    }

    public sealed class OneEuroFilter3
    {
        readonly OneEuroFilter _x, _y, _z;

        public OneEuroFilter3(float minCutoff, float beta, float derivativeCutoff = 1f)
        {
            _x = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
            _y = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
            _z = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
        }

        public void Reset()
        {
            _x.Reset();
            _y.Reset();
            _z.Reset();
        }

        public Vector3 Filter(Vector3 v, float dt, float scale = 1f) =>
            new Vector3(_x.Filter(v.x, dt, scale), _y.Filter(v.y, dt, scale), _z.Filter(v.z, dt, scale));

        public Vector3 Velocity => new Vector3(_x.Velocity, _y.Velocity, _z.Velocity);
    }
}
