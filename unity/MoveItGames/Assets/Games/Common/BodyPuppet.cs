using MoveIt.Motion;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games
{
    /// <summary>
    /// The player's pose for a figure standing in a game's world (Aventura!, Sporte!): the 17 camera joints in metres in
    /// the figure's frame (x = the screen's right = the player's right, like a mirror; y up; z into the scene, toward the
    /// TV), each joint smoothed on the camera's clock like a hand cursor (<see cref="HandInterpolator"/>), so the figure
    /// moves every frame instead of in 30 Hz steps.
    /// <list type="bullet">
    /// <item>The body's shape is the camera's, scaled so the torso is always <see cref="ToyPoses.Torso"/> (a child and an
    /// adult get the same figure): up/down and sideways from the picture, depth from the camera AI's 3D joints.</item>
    /// <item>Where it stands follows the player: a step to the side moves it <see cref="SideGain"/> metres per metre of
    /// figure (from where they stood when calibrated), a jump lifts it (<see cref="JumpGain"/>), a crouch lowers it.</item>
    /// <item>Without a camera (or while nobody is seen) it eases into a pose the game sets: <see cref="KeyRoot"/>,
    /// <see cref="KeyRig"/> (standing at ease by default).</item>
    /// </list>
    /// Pure logic apart from the clock the caller passes: no scene.
    /// </summary>
    public sealed class BodyPuppet
    {
        public const int Count = Skeleton.JointCount;

        /// <summary>A step to the side moves the figure this many times as far as the player moved (in figure metres).</summary>
        public float SideGain = 1f;
        /// <summary>A jump lifts the figure this many times as high as the player's hips rose.</summary>
        public float JumpGain = 1.4f;
        /// <summary>The figure stays within this distance (metres) of the middle.</summary>
        public float MaxSide = 10f;
        /// <summary>How much of the camera AI's depth is used (it is noisier than up/down).</summary>
        public float DepthAmount = 0.6f;

        /// <summary>The joints (<see cref="Joint"/> order), metres in the figure's frame.</summary>
        public readonly Vector3[] Pose = new Vector3[Count];
        /// <summary>How sure the camera is about each joint (smoothed; 1 without a camera).</summary>
        public readonly float[] Visibility = new float[Count];
        /// <summary>The camera sees the player (false: the keyboard pose).</summary>
        public bool Tracked { get; private set; }
        /// <summary>Without a camera: where the figure stands (x, metres) and how far its hips are lifted (y).</summary>
        public Vector2 KeyRoot;
        /// <summary>Without a camera: the pose (null = standing at ease). Its hips are moved by <see cref="KeyRoot"/>.</summary>
        public ToyRig? KeyRig;
        /// <summary>How quickly the keyboard pose is reached (per second).</summary>
        public float KeyEase = 14f;

        readonly HandInterpolator[] _tracks = new HandInterpolator[Count];
        readonly Vector3[] _target = new Vector3[Count];
        double _lastSample;
        float _t;

        public BodyPuppet(float delayFrames = 0.5f, float minCutoff = 1f, float beta = 2.5f)
        {
            for (int i = 0; i < Count; i++)
            {
                _tracks[i] = new HandInterpolator(minCutoff, beta, 2f) { Margin = delayFrames, MaxExtrapolation = 0.08f };
                Visibility[i] = 1f;
            }
            ToyPoses.Build(ToyRig.Standing, Pose);
        }

        public Vector3 this[Joint j] => Pose[(int)j];

        /// <summary>Where the figure stands (x) and how far its hips are above standing height (y), metres.</summary>
        public Vector2 Root => new Vector2(Mid(Joint.LeftHip, Joint.RightHip).x, Mid(Joint.LeftHip, Joint.RightHip).y - ToyPoses.HipHeight);

        public Vector3 Hips => Mid(Joint.LeftHip, Joint.RightHip);
        public Vector3 Shoulders => Mid(Joint.LeftShoulder, Joint.RightShoulder);
        public Vector3 Chest => Vector3.Lerp(Shoulders, Hips, 0.3f);

        /// <summary>The middle of the head (from the ears and the nose; above the shoulders when the face isn't seen).</summary>
        public Vector3 Head
        {
            get
            {
                if (Visibility[(int)Joint.LeftEar] + Visibility[(int)Joint.RightEar] < 0.6f && Visibility[(int)Joint.Nose] < 0.4f)
                    return Shoulders + (Shoulders - Hips).normalized * ToyPoses.Neck;
                return Vector3.Lerp(Mid(Joint.LeftEar, Joint.RightEar), this[Joint.Nose], 0.25f) + Vector3.up * 0.03f;
            }
        }

        /// <summary>The middle of a hand: a little past the wrist, along the forearm.</summary>
        public Vector3 Hand(bool right)
        {
            Vector3 w = this[right ? Joint.RightWrist : Joint.LeftWrist], e = this[right ? Joint.RightElbow : Joint.LeftElbow];
            var d = w - e;
            return d.sqrMagnitude > 1e-6f ? w + d.normalized * 0.07f : w;
        }

        public Vector3 Foot(bool right) => this[right ? Joint.RightAnkle : Joint.LeftAnkle] + new Vector3(0f, -0.02f, 0.05f);

        /// <summary>The player has both shoulders and the hips in view enough to draw a body.</summary>
        public bool BodySeen => Mathf.Min(Visibility[(int)Joint.LeftShoulder], Visibility[(int)Joint.RightShoulder]) > 0.3f;

        /// <summary>
        /// Once a frame: <paramref name="body"/> is the active player (null = nobody), <paramref name="gestures"/> their
        /// calibration (where they stood, for the steps and jumps), <paramref name="now"/> the local clock in seconds
        /// (Time.unscaledTimeAsDouble).
        /// </summary>
        public void Update(Body body, BodyGestures gestures, double now, float dt)
        {
            _t += dt;
            Tracked = body != null && body.IsTracking && body.TorsoLength > 0.02f;
            if (Tracked)
            {
                if (body.SampleTime != _lastSample)
                {
                    _lastSample = body.SampleTime;
                    Sample(body, gestures, now);
                }
                for (int i = 0; i < Count; i++)
                    if (_tracks[i].HasSample) Pose[i] = _tracks[i].Evaluate(now, dt, out _);
                return;
            }

            foreach (var t in _tracks) t.Reset();
            _lastSample = 0;
            var rig = KeyRig ?? ToyPoses.Idle(_t);
            rig.Hips += new Vector3(KeyRoot.x, KeyRoot.y, 0f);
            ToyPoses.Build(rig, _target);
            float k = 1f - Mathf.Exp(-KeyEase * dt);
            for (int i = 0; i < Count; i++)
            {
                Pose[i] = Vector3.Lerp(Pose[i], _target[i], k);
                Visibility[i] = Mathf.Lerp(Visibility[i], 1f, k);
            }
        }

        /// <summary>Jumps the keyboard pose there at once (a new round: no easing across the world).</summary>
        public void SnapToKeys()
        {
            var rig = KeyRig ?? ToyPoses.Idle(_t);
            rig.Hips += new Vector3(KeyRoot.x, KeyRoot.y, 0f);
            ToyPoses.Build(rig, Pose);
        }

        /// <summary>One camera sample: every joint around the hips, mirrored, scaled to the figure, moved to where the
        /// player stands now compared with where they calibrated.</summary>
        void Sample(Body body, BodyGestures g, double now)
        {
            Vector2 hips = body.HipCenter;                        // the smooth body anchors the figure (no shaking)
            float k = ToyPoses.Torso / body.TorsoLength;
            float kz = body.HasWorld && body.WorldTorsoLength > 0.05f ? ToyPoses.Torso / body.WorldTorsoLength * DepthAmount : 0f;
            float hipsZ = body.HasWorld ? body.WorldHipCenter.z : 0f;
            var root = Vector2.zero;
            if (g != null && g.IsCalibrated && g.Body == body)
            {
                root.x = Mathf.Clamp(g.LateralOffset * ToyPoses.Torso * SideGain, -MaxSide, MaxSide);
                float up = g.VerticalOffset * ToyPoses.Torso;
                root.y = up > 0f ? up * JumpGain : up;
            }
            for (int i = 0; i < Count; i++)
            {
                Vector3 raw = body.Raw[i];
                float z = kz > 0f ? -(body.World[i].z - hipsZ) * kz : 0f;   // toward the TV = forward, into the scene
                var p = new Vector3((raw.x - hips.x) * k + root.x, (raw.y - hips.y) * k + ToyPoses.HipHeight + root.y, z);
                _tracks[i].Sample(body.SampleTime, p, false, now);
                Visibility[i] = Mathf.Lerp(Visibility[i], body.Visibility[i], 0.25f);
            }
        }

        Vector3 Mid(Joint a, Joint b) => (Pose[(int)a] + Pose[(int)b]) * 0.5f;
    }
}
