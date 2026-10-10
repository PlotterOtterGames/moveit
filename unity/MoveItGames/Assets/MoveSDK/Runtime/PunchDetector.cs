using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>One arm in one camera sample: the low-lag track (view space) and, when sent, world joints (metres).</summary>
    public struct ArmSample
    {
        public Vector3 Shoulder, Elbow, Wrist;
        public Vector3 WorldShoulder, WorldElbow, WorldWrist;
        /// <summary>How sure the camera is about the forearm (the lower of the elbow's and the wrist's visibility).</summary>
        public float Visibility;

        /// <summary>The player's <paramref name="right"/> or left arm from <paramref name="body"/>.</summary>
        public static ArmSample From(Body body, bool right)
        {
            var s = right ? Joint.RightShoulder : Joint.LeftShoulder;
            var e = right ? Joint.RightElbow : Joint.LeftElbow;
            var w = right ? Joint.RightWrist : Joint.LeftWrist;
            return new ArmSample
            {
                Shoulder = body.Fast[(int)s], Elbow = body.Fast[(int)e], Wrist = body.Fast[(int)w],
                WorldShoulder = body.World[(int)s], WorldElbow = body.World[(int)e], WorldWrist = body.World[(int)w],
                Visibility = Mathf.Min(body.Visibility[(int)e], body.Visibility[(int)w]),
            };
        }
    }

    public struct PunchInfo
    {
        public bool RightHand;
        /// <summary>How fast the fist moved away from its shoulder at its fastest, torso lengths per second.</summary>
        public float Speed;
        /// <summary>True for a straight-arm swing toward the TV; false for a bent arm punching out.</summary>
        public bool Swing;
    }

    /// <summary>
    /// Punches from a front camera, for either hand, in any direction that isn't down (straight at the TV, sideways, an
    /// uppercut). A punch is a <b>bent arm stretching out fast</b>: the fist shoots away from the shoulder until the arm
    /// is nearly straight. Measured in 3D when the camera service sends world joints (protocol v1.1), so a jab straight at
    /// the camera counts although the fist barely moves in the picture; in the picture otherwise. In torso lengths and
    /// as a share of the arm's own length, so the same numbers fit a child and an adult at any distance.
    /// <list type="bullet">
    /// <item>"bent" and "straight": the shoulder-wrist distance over the arm's length (upper arm + forearm);</item>
    /// <item>"fast": the fist moves away from the shoulder at least <see cref="MinOutSpeed"/>. Moving the whole body to
    /// steer, raising the arms (the fist circles the shoulder) or bringing the fists up (toward it) never does;</item>
    /// <item>pointing down doesn't count (letting the arms drop stretches them out too).</item>
    /// </list>
    /// With world joints a fast straight-arm swing toward the TV also counts (how many children punch).
    /// After a punch the arm has to bend again (or the swing slow down) before that hand punches again.
    /// Pure logic: tests feed it arm samples (PunchDetectorTests holds the evidence for the numbers).
    /// </summary>
    public sealed class PunchDetector
    {
        /// <summary>An arm at most this straight is "bent": a punch starts from here (an elbow at 90° is about 0.71).</summary>
        public float BentReach = 0.74f;
        /// <summary>The punch is seen when the arm is this straight...</summary>
        public float PunchReach = 0.86f;
        /// <summary>...within this time of being bent...</summary>
        public float MaxExtendSeconds = 0.45f;
        /// <summary>...with the fist having moved away from the shoulder this fast (torso lengths per second) on the way.</summary>
        public float MinOutSpeed = 2.6f;
        /// <summary>Seen sooner: this straight already, and the fist moving away this fast right now.</summary>
        public float EarlyReach = 0.78f, EarlyOutSpeed = 4.5f;
        /// <summary>A straight-arm swing (world joints only): the fist moved this fast (torso lengths per second), mostly
        /// toward the TV...</summary>
        public float SwingSpeed = 6f;
        /// <summary>...within this time (s), and stopped (half that speed or less) with the arm pointing at the TV: at least
        /// this share of the shoulder-to-fist direction toward the camera. (Raising the arms in front passes "pointing at
        /// the TV" at full speed, on its way up.)</summary>
        public float SwingWithin = 0.3f, SwingForward = 0.6f;
        /// <summary>After a swing the fist must slow below this before another can start.</summary>
        public float SwingReArmSpeed = 2f;
        /// <summary>A punch may point at most this much down: the downward share of the shoulder-to-fist direction.</summary>
        public float MaxDown = 0.7f;
        /// <summary>No second punch from the same hand within this time (s).</summary>
        public float Cooldown = 0.25f;
        /// <summary>Samples less sure than this are skipped.</summary>
        public float MinVisibility = 0.3f;

        /// <summary>Speeds are measured over about this much camera time: a frame-to-frame difference is mostly noise.</summary>
        public float SpeedWindow = 0.15f;

        sealed class Hand
        {
            public const int History = 8;
            public readonly double[] Times = new double[History];
            public readonly float[] Distances = new float[History];
            public readonly Vector3[] Relatives = new Vector3[History];
            public int Count, Newest = -1;
            public float Reach, OutSpeed, PeakOutSpeed, SwingPeak;
            public Vector3 Velocity;
            public double BentAt = -10, PunchedAt = -10, FastAt = -10;
            public bool SwingArmed = true;

            public void Push(double time, float distance, Vector3 relative)
            {
                Newest = (Newest + 1) % History;
                Times[Newest] = time;
                Distances[Newest] = distance;
                Relatives[Newest] = relative;
                Count = Mathf.Min(Count + 1, History);
            }

            /// <summary>The newest sample at least <paramref name="window"/> older than the newest (or the oldest kept, if
            /// that spans most of the window); -1 while there's too little history to measure a speed.</summary>
            public int Before(float window)
            {
                int best = -1;
                for (int k = 1; k < Count; k++)
                {
                    int i = (Newest - k + History) % History;
                    best = i;
                    if (Times[Newest] - Times[i] >= window) break;
                }
                return best >= 0 && Times[Newest] - Times[best] >= window * 0.6f ? best : -1;
            }
        }

        readonly Hand _left = new Hand(), _right = new Hand();

        /// <summary>The arm's current straightness (1 = straight), for drawing and debugging.</summary>
        public float Reach(bool right) => (right ? _right : _left).Reach;

        public void Reset()
        {
            foreach (var h in new[] { _left, _right })
            {
                h.Count = 0;
                h.BentAt = h.PunchedAt = h.FastAt = -10;
                h.SwingPeak = 0f;
                h.SwingArmed = true;
            }
        }

        /// <summary>
        /// One camera sample (call once per sample, with its capture time in seconds). <paramref name="torso"/> is the
        /// torso length in the units of the joints used (metres with world joints, view units without). Returns true when
        /// a punch is seen. Both fists at once count as one punch (reported for the faster one).
        /// </summary>
        public bool Update(double time, ArmSample left, ArmSample right, float torso, bool hasWorld, out PunchInfo info)
        {
            info = default;
            if (torso < 1e-3f) return false;
            bool l = Sample(_left, left, time, torso, hasWorld, out var li);
            bool r = Sample(_right, right, time, torso, hasWorld, out var ri);
            if (!l && !r) return false;
            info = l && r ? (li.Speed > ri.Speed ? li : ri) : l ? li : ri;
            return true;
        }

        bool Sample(Hand h, ArmSample a, double time, float torso, bool hasWorld, out PunchInfo info)
        {
            info = default;
            info.RightHand = h == _right;
            if (a.Visibility < MinVisibility)
            {
                h.Count = 0;
                return false;
            }

            Vector3 s = hasWorld ? a.WorldShoulder : a.Shoulder, e = hasWorld ? a.WorldElbow : a.Elbow, w = hasWorld ? a.WorldWrist : a.Wrist;
            if (!hasWorld)
            {
                // the picture has no depth (its z is a rough guess): flat
                s.z = e.z = w.z = 0f;
            }
            var relative = (w - s) / torso;
            float bones = (Vector3.Distance(s, e) + Vector3.Distance(e, w)) / torso;
            float distance = relative.magnitude;
            float reach = bones > 1e-4f ? distance / bones : 1f;
            if (h.Count > 0)
            {
                double sinceNewest = time - h.Times[h.Newest];
                if (sinceNewest < Body.MinSpeedInterval) return false;   // a burst after a stall: wait for a real interval
                if (sinceNewest > 0.25) h.Count = 0;                       // a long gap says nothing about speed: start over
            }
            h.Push(time, distance, relative);
            h.Reach = reach;
            int then = h.Before(SpeedWindow);
            if (then < 0)
            {
                h.OutSpeed = h.PeakOutSpeed = 0f;
                h.Velocity = Vector3.zero;
                return false;
            }
            float span = (float)(time - h.Times[then]);
            h.OutSpeed = (distance - h.Distances[then]) / span;
            h.Velocity = (relative - h.Relatives[then]) / span;

            if (reach <= BentReach)
            {
                h.BentAt = time;
                h.PeakOutSpeed = 0f;
            }
            else h.PeakOutSpeed = Mathf.Max(h.PeakOutSpeed, h.OutSpeed);
            float speed = h.Velocity.magnitude;
            if (!h.SwingArmed && speed < SwingReArmSpeed) h.SwingArmed = true;
            if (h.SwingArmed && speed >= SwingSpeed && -h.Velocity.z >= 0.5f * speed)   // fast, and toward the TV
            {
                h.FastAt = time;
                h.SwingPeak = Mathf.Max(h.SwingPeak, speed);
            }
            else if (time - h.FastAt > SwingWithin) h.SwingPeak = 0f;
            if (time - h.PunchedAt < Cooldown || PointsDown(relative)) return false;

            // a bent arm punching out
            bool fresh = time - h.BentAt <= MaxExtendSeconds;
            bool straight = reach >= PunchReach && h.PeakOutSpeed >= MinOutSpeed;
            bool early = reach >= EarlyReach && h.OutSpeed >= EarlyOutSpeed;
            bool punch = fresh && (straight || early);
            // or a straight arm swung at the TV, stopping there (world joints: z < 0 is toward the camera)
            bool swing = hasWorld && h.SwingArmed && reach >= PunchReach && time - h.FastAt <= SwingWithin &&
                         speed <= 0.5f * h.SwingPeak && -relative.z >= SwingForward * distance;
            if (!punch && !swing) return false;

            info.Speed = Mathf.Max(h.PeakOutSpeed, h.OutSpeed, h.SwingPeak);
            h.PunchedAt = time;
            h.BentAt = h.FastAt = -10;
            h.SwingPeak = 0f;
            h.SwingArmed = false;
            info.Swing = !punch;
            return true;
        }

        bool PointsDown(Vector3 v)
        {
            float length = v.magnitude;
            return length > 1e-5f && -v.y > MaxDown * length;
        }
    }
}
