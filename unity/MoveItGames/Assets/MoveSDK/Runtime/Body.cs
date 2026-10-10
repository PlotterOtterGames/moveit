using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// A tracked player skeleton in <b>view space</b>: mirrored like a mirror (x grows to the screen's right = the
    /// player's right), y up, units = camera image heights. x is in [0, Aspect], y in [0, 1].
    ///
    /// Two tracks of the same joints:
    /// <list type="bullet">
    /// <item><see cref="Joints"/>: smooth, for drawing the player (no jitter).</item>
    /// <item><see cref="Fast"/>: low-lag, for gestures and hand cursors (reacts sooner, a little noisier),
    /// with <see cref="FastVelocity"/> for prediction.</item>
    /// </list>
    /// Plus optional <see cref="World"/> joints (metres) when the camera service sends them.
    /// </summary>
    public sealed class Body
    {
        /// <summary>
        /// Speeds are measured over at least this many seconds of camera time. Samples closer together (a burst
        /// after a stall, a duplicated frame) are skipped and the next one spans the whole gap: dividing camera noise
        /// by a 1 ms gap would look like a lightning-fast swing. Below a 60 fps camera's 16.7 ms, so no real frame is lost.
        /// </summary>
        public const float MinSpeedInterval = 0.012f;

        public int Id { get; internal set; }
        public float Score { get; internal set; }
        public float Aspect { get; internal set; } = 16f / 9f;

        /// <summary>Capture time of the latest sample, seconds (from the packet's Unix ms timestamp).</summary>
        public double SampleTime { get; internal set; }

        /// <summary>Time.unscaledTime when this body was last seen by the camera.</summary>
        public float LastSeen { get; internal set; }

        /// <summary>False while the camera has briefly lost this player (the body is kept for a moment).</summary>
        public bool IsTracking => Time.unscaledTime - LastSeen < 0.25f;

        public readonly Vector3[] Joints = new Vector3[Skeleton.JointCount];
        public readonly Vector3[] Fast = new Vector3[Skeleton.JointCount];
        /// <summary>View units per second, from the low-lag filter.</summary>
        public readonly Vector3[] FastVelocity = new Vector3[Skeleton.JointCount];
        /// <summary>The latest camera sample, unfiltered: for cursors that do their own smoothing (<see cref="HandInterpolator"/>).</summary>
        public readonly Vector3[] Raw = new Vector3[Skeleton.JointCount];
        public readonly float[] Visibility = new float[Skeleton.JointCount];

        /// <summary>
        /// World joints in metres, origin at the hips, mirrored like the view: x = screen right, y = up,
        /// z = MediaPipe depth (negative = toward the camera). Only valid when <see cref="HasWorld"/>.
        /// </summary>
        public readonly Vector3[] World = new Vector3[Skeleton.JointCount];
        public bool HasWorld { get; internal set; }

        internal readonly OneEuroFilter3[] SmoothFilters = new OneEuroFilter3[Skeleton.JointCount];
        internal readonly OneEuroFilter3[] FastFilters = new OneEuroFilter3[Skeleton.JointCount];
        internal readonly OneEuroFilter3[] WorldFilters = new OneEuroFilter3[Skeleton.JointCount];
        /// <summary>Slowly smoothed torso length (view units / metres): the "body size" the filters measure speed in.</summary>
        internal float FilterScale, WorldFilterScale;

        public Vector3 this[Joint j] => Joints[(int)j];

        public bool IsVisible(Joint j, float min = 0.5f) => Visibility[(int)j] >= min;

        public Vector3 ShoulderCenter => Mid(Joints, Joint.LeftShoulder, Joint.RightShoulder);
        public Vector3 HipCenter => Mid(Joints, Joint.LeftHip, Joint.RightHip);
        public Vector3 TorsoCenter => (ShoulderCenter + HipCenter) * 0.5f;

        /// <summary>Shoulder-center to hip-center distance. The body-size unit used by gestures.</summary>
        public float TorsoLength => Vector2.Distance(ShoulderCenter, HipCenter);

        public float ShoulderWidth => Vector2.Distance(this[Joint.LeftShoulder], this[Joint.RightShoulder]);

        public bool ShouldersVisible => IsVisible(Joint.LeftShoulder) && IsVisible(Joint.RightShoulder);
        public bool HipsVisible => IsVisible(Joint.LeftHip) && IsVisible(Joint.RightHip);

        // ---- low-lag track (gestures) ----
        public Vector3 FastShoulderCenter => Mid(Fast, Joint.LeftShoulder, Joint.RightShoulder);
        public Vector3 FastHipCenter => Mid(Fast, Joint.LeftHip, Joint.RightHip);
        public Vector3 FastTorsoCenter => (FastShoulderCenter + FastHipCenter) * 0.5f;
        public float FastTorsoLength => Vector2.Distance(FastShoulderCenter, FastHipCenter);

        /// <summary>
        /// A point past the wrist toward the fingertips, <paramref name="extension"/> forearm lengths further, on
        /// <paramref name="track"/> (<see cref="Raw"/>, <see cref="Fast"/> or <see cref="Joints"/>). It fades in with
        /// the elbow's visibility: switching it on and off at a threshold moved hand cursors ~100 px at once.
        /// </summary>
        public Vector2 HandTip(Vector3[] track, Joint elbow, Joint wrist, float extension)
        {
            Vector2 w = track[(int)wrist], e = track[(int)elbow];
            return w + (w - e) * (extension * Mathf.InverseLerp(0.2f, 0.4f, Visibility[(int)elbow]));
        }

        /// <summary>Low-lag joint position extrapolated <paramref name="seconds"/> ahead (latency compensation).</summary>
        public Vector3 Predict(Joint j, float seconds) => Fast[(int)j] + FastVelocity[(int)j] * seconds;

        // ---- world track ----
        public Vector3 WorldHipCenter => Mid(World, Joint.LeftHip, Joint.RightHip);
        public float WorldTorsoLength => Vector3.Distance(Mid(World, Joint.LeftShoulder, Joint.RightShoulder), WorldHipCenter);

        /// <summary>Joint position in 0..1 screen coordinates (x right, y up), e.g. for hand cursors.</summary>
        public Vector2 Screen01(Joint j)
        {
            var p = this[j];
            return new Vector2(p.x / Aspect, p.y);
        }

        static Vector3 Mid(Vector3[] track, Joint a, Joint b) => (track[(int)a] + track[(int)b]) * 0.5f;
    }
}
