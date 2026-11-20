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
        /// <summary>The motion service's track for this person (protocol <c>tid</c>), 0 if it doesn't send one.</summary>
        public int ServiceTrack { get; internal set; }
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
        /// <summary>
        /// <see cref="Joints"/> as they should be drawn this frame: interpolated on the camera's clock
        /// (<see cref="JointTimeline"/>), so the body moves evenly every frame instead of in camera-rate steps. Updated by
        /// <see cref="MotionSystem"/> at the start of every frame. For drawing only: gestures read the samples themselves.
        /// </summary>
        public readonly Vector3[] Shown = new Vector3[Skeleton.JointCount];
        internal readonly JointTimeline ShownTimeline = new JointTimeline();
        public readonly float[] Visibility = new float[Skeleton.JointCount];

        /// <summary>
        /// World joints in metres, origin at the hips, mirrored like the view: x = screen right, y = up,
        /// z = MediaPipe depth (negative = toward the camera). Only valid when <see cref="HasWorld"/>.
        /// </summary>
        public readonly Vector3[] World = new Vector3[Skeleton.JointCount];
        /// <summary>The latest sample's world joints, unfiltered (like <see cref="Raw"/>): for code that does its own
        /// smoothing or looks for the pose model's glitches (Vishu!'s OutfitTracker). Valid when <see cref="HasWorld"/>.</summary>
        public readonly Vector3[] RawWorld = new Vector3[Skeleton.JointCount];
        public bool HasWorld { get; internal set; }

        internal readonly OneEuroFilter3[] SmoothFilters = new OneEuroFilter3[Skeleton.JointCount];
        internal readonly OneEuroFilter3[] FastFilters = new OneEuroFilter3[Skeleton.JointCount];
        internal readonly OneEuroFilter3[] WorldFilters = new OneEuroFilter3[Skeleton.JointCount];
        /// <summary>Slowly smoothed torso length (view units / metres): the "body size" the filters measure speed in.</summary>
        internal float FilterScale, WorldFilterScale;

        public Vector3 this[Joint j] => Joints[(int)j];

        /// <summary>Places <see cref="Shown"/> for the frame at <paramref name="localNow"/> (Time.unscaledTimeAsDouble).</summary>
        public void UpdateShown(double localNow)
        {
            if (!ShownTimeline.Evaluate(localNow, Shown)) System.Array.Copy(Joints, Shown, Skeleton.JointCount);
        }

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
        /// A hand's point for a cursor: the wrist on <paramref name="track"/> (<see cref="Raw"/>, <see cref="Fast"/> or
        /// <see cref="Joints"/>) carried on out along the arm from the steady shoulder (<see cref="Joints"/>), as if the
        /// arm were <paramref name="longer"/> longer: toward the hand's middle, and as far as a hand reaches.
        /// Until 2026-11-17 the cursors pushed the wrist along the forearm (0.3 of it), which brought in a third of the
        /// elbow's error, the joint the pose model gets most wrong while a hand moves in front of the body. On the box's
        /// recordings (tools/motion/hand_replay.py, docs/hardware.md "The other hand cursors") this point made Pikturo!'s
        /// brush and Lunapark!'s hands jump a fifth to a third less, as close to the hand in the picture and reaching as
        /// far (0.1 longer: 96 % of the old reach; the wrist alone jumps least but reaches 0.15 torso lengths less).
        /// </summary>
        public Vector2 HandPoint(Vector3[] track, Joint shoulder, Joint wrist, float longer = 0.1f)
        {
            Vector2 s = Joints[(int)shoulder];
            return s + ((Vector2)track[(int)wrist] - s) * (1f + longer);
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
