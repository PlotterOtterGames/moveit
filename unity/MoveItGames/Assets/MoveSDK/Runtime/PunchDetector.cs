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
        /// <summary>The hand that punched (the faster one when both did).</summary>
        public bool RightHand;
        /// <summary>Both fists punched in the same sample (a double punch).</summary>
        public bool BothHands;
        /// <summary>How fast the fist was moving in the picture, torso lengths per second.</summary>
        public float Speed;
    }

    /// <summary>
    /// Punches from a front camera, made to be <b>forgiving</b>: any quick move of a fist counts, in any direction (at the
    /// TV, sideways, a hook, an uppercut, a swing of the whole arm, flailing), except pulling the fist back toward the
    /// shoulder (after a punch, raising the fists to guard) and dropping the arms. It's seen the moment the fist gets
    /// fast, at the start of the move rather than its end, so the hero hits back at once. How shipped motion games do it:
    /// Kinect games trigger at the middle of a move and let the animation catch up ("use the player's input to drive the
    /// action rather than directly reflect it"), lower their accuracy thresholds until every child's way of punching
    /// counts, and keep gestures big (whole-arm moves, not small ones that tracking noise can fake).
    /// <list type="bullet">
    /// <item>"quick": the fist moves relative to its own shoulder (so stepping, ducking and jumping never count) at least
    /// <see cref="StrikeSpeed"/> in the picture, or the arm straightens at least <see cref="StraightenSpeed"/> (in 3D
    /// with world joints). The second catches a jab straight at the TV, which barely moves in the picture and whose depth
    /// the camera service squeezes. Speeds come from the picture because its x and y are far steadier than the world
    /// joints' (a still hand's world joints jitter like a quick move);</item>
    /// <item>one move, one punch: after a punch the hand punches again once the fist has slowed down (a punch stops at
    /// its end) or after <see cref="RepeatAfter"/> of moving fast (windmilling arms keep punching); the way back (the
    /// arm bending, the fist heading back to the shoulder) is no punch.</item>
    /// </list>
    /// Without world joints (an old camera service) a jab straight at the TV can't be seen, everything sideways can. In
    /// torso lengths and as a share of the arm's own length, so the same numbers fit a child and an adult at any
    /// distance. Pure logic: tests feed it arm samples (PunchDetectorTests holds the evidence for the numbers).
    /// </summary>
    public sealed class PunchDetector
    {
        /// <summary>A punch: the fist moving this fast relative to its shoulder in the picture (torso lengths per
        /// second)...</summary>
        public float StrikeSpeed = 2.2f;
        /// <summary>...or the arm straightening this fast (its straightness, 0..1, per second), ending at least
        /// <see cref="StraightenTo"/> straight. A share of the arm's own length doesn't shrink when the camera service
        /// squeezes depth (MediaPipe shows a fist moving 30 cm toward the camera as 13-22 cm).</summary>
        public float StraightenSpeed = 1.8f, StraightenTo = 0.7f;
        /// <summary>Not a punch: the arm bending (in the picture) this fast (straightness, 0..1, per second) or by this much
        /// since the move started. Bringing a fist back toward the shoulder always bends the arm (pulling it back after a
        /// hook, say). Raising the fists to the chin from hanging arms starts exactly like a punch from a hanging arm, so
        /// it may count: forgiving is the point...</summary>
        public float BendSpeed = 1.5f, MaxBend = 0.35f;
        /// <summary>...or, within this long (s) after a punch, the fist heading back toward its shoulder in the picture (at
        /// least <see cref="MaxReturn"/> of the move): the way back from that punch. A forearm folding in after a hook
        /// swings toward the camera, so the picture hides the bend and the world joints show it late.</summary>
        public float ReturnSeconds = 0.7f, MaxReturn = 0.5f;
        /// <summary>...or down (at least this share of the speed): letting the arms drop.</summary>
        public float MaxDown = 0.7f;
        /// <summary>After a punch the hand is ready again once the fist moves slower than this (torso lengths per second)
        /// and the arm stops straightening...</summary>
        public float ReArmSpeed = 1.2f;
        /// <summary>...or after this long (s) of moving fast all along.</summary>
        public float RepeatAfter = 0.4f;
        /// <summary>No second punch from the same hand within this time (s).</summary>
        public float Cooldown = 0.18f;
        /// <summary>Samples less sure than this are skipped.</summary>
        public float MinVisibility = 0.3f;
        /// <summary>Speeds are measured over about this much camera time: a frame-to-frame difference is mostly noise.</summary>
        public float SpeedWindow = 0.1f;
        /// <summary>A hand just found (or found again after a gap) doesn't punch for this long (s): its filters settle.</summary>
        public float SettleSeconds = 0.2f;

        sealed class Hand
        {
            public const int History = 8;
            public readonly double[] Times = new double[History];
            public readonly float[] Reaches = new float[History];
            public readonly Vector2[] Pictures = new Vector2[History];
            /// <summary>How straight the arm looks in the picture (steadier and quicker than the world joints).</summary>
            public readonly float[] Flats = new float[History];
            public int Count, Newest = -1;
            public double FoundAt;
            public float Reach, Speed, ReachSpeed, FlatSpeed;
            /// <summary>How straight the arm looked in the picture when the current move started (the fist last still, or
            /// the last punch).</summary>
            public float MoveFlat;
            public Vector2 Velocity;
            public double PunchedAt = -10;
            public bool Armed = true;

            public void Push(double time, float reach, float flat, Vector2 picture)
            {
                if (Count == 0) FoundAt = time;
                Newest = (Newest + 1) % History;
                Times[Newest] = time;
                Reaches[Newest] = reach;
                Pictures[Newest] = picture;
                Flats[Newest] = flat;
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

        /// <summary>How fast the fist moves relative to its shoulder in the picture right now (torso lengths per second).</summary>
        public float Speed(bool right) => (right ? _right : _left).Speed;

        public void Reset()
        {
            foreach (var h in new[] { _left, _right })
            {
                h.Count = 0;
                h.PunchedAt = -10;
                h.Armed = true;
                h.Speed = h.ReachSpeed = 0f;
            }
        }

        /// <summary>
        /// One camera sample (call once per sample, with its capture time in seconds). <paramref name="pictureTorso"/> is
        /// the torso length in the picture (view units), <paramref name="worldTorso"/> in world joints (metres; 0 when
        /// the camera service sends none). Returns true when a punch is seen; both fists at once are reported together
        /// (<see cref="PunchInfo.BothHands"/>).
        /// </summary>
        public bool Update(double time, ArmSample left, ArmSample right, float pictureTorso, float worldTorso, out PunchInfo info)
        {
            info = default;
            if (pictureTorso < 1e-4f) return false;
            bool l = Sample(_left, left, time, pictureTorso, worldTorso);
            bool r = Sample(_right, right, time, pictureTorso, worldTorso);
            if (!l && !r) return false;
            info.BothHands = l && r;
            info.RightHand = l && r ? _right.Speed >= _left.Speed : r;
            info.Speed = (info.RightHand ? _right : _left).Speed;
            return true;
        }

        bool Sample(Hand h, ArmSample a, double time, float pictureTorso, float worldTorso)
        {
            // An unsure sample (a fast fist blurs) is skipped, not a reason to forget the move under way: a long gap
            // resets the history below.
            if (a.Visibility < MinVisibility) return false;

            Vector2 s2 = a.Shoulder, e2 = a.Elbow, w2 = a.Wrist;
            var picture = (w2 - s2) / pictureTorso;
            float flat = Straightness(s2, e2, w2);
            // the picture's z is a rough guess: without world joints the arm's straightness is the picture's
            float reach = worldTorso > 1e-3f ? Straightness(a.WorldShoulder, a.WorldElbow, a.WorldWrist) : flat;
            if (h.Count > 0)
            {
                double sinceNewest = time - h.Times[h.Newest];
                if (sinceNewest < Body.MinSpeedInterval) return false;   // a burst after a stall: wait for a real interval
                if (sinceNewest > 0.25) h.Count = 0;                       // a long gap says nothing about speed: start over
            }
            h.Push(time, reach, flat, picture);
            h.Reach = reach;
            int then = h.Before(SpeedWindow);
            if (then < 0 || time - h.FoundAt < SettleSeconds)
            {
                h.Speed = h.ReachSpeed = h.FlatSpeed = 0f;
                h.MoveFlat = flat;
                h.Velocity = Vector2.zero;
                return false;
            }
            float span = (float)(time - h.Times[then]);
            h.Velocity = (picture - h.Pictures[then]) / span;
            h.Speed = h.Velocity.magnitude;
            h.ReachSpeed = (reach - h.Reaches[then]) / span;
            h.FlatSpeed = (flat - h.Flats[then]) / span;
            if (h.Speed < ReArmSpeed) h.MoveFlat = flat;   // still: a move starts from here

            if (!h.Armed && (h.Speed < ReArmSpeed && h.ReachSpeed < StraightenSpeed * 0.5f || time - h.PunchedAt >= RepeatAfter))
                h.Armed = true;
            if (!h.Armed || time - h.PunchedAt < Cooldown) return false;

            bool bends = h.FlatSpeed <= -BendSpeed || flat - h.MoveFlat <= -MaxBend;
            bool returning = time - h.PunchedAt < ReturnSeconds && TowardShoulder(h.Pictures[then], picture);
            bool fast = h.Speed >= StrikeSpeed && !bends && !returning && !Drops(h.Velocity);
            bool straightens = h.ReachSpeed >= StraightenSpeed && reach >= StraightenTo && !Drops(h.Velocity);
            if (!fast && !straightens) return false;
            h.PunchedAt = time;
            h.MoveFlat = flat;
            h.Armed = false;
            return true;
        }

        /// <summary>Moving from <paramref name="from"/> to <paramref name="to"/> (the fist relative to its shoulder, in the
        /// picture) heads back toward the shoulder (a fist right over the shoulder has no direction: never).</summary>
        bool TowardShoulder(Vector2 from, Vector2 to)
        {
            var move = to - from;
            float length = move.magnitude, away = from.magnitude;
            return away > 0.3f && length > 1e-5f && -Vector2.Dot(move, from) >= MaxReturn * length * away;
        }

        /// <summary>The shoulder-to-fist distance over the arm's length (upper arm + forearm): 1 = straight.</summary>
        static float Straightness(Vector3 s, Vector3 e, Vector3 w)
        {
            float bones = Vector3.Distance(s, e) + Vector3.Distance(e, w);
            return bones > 1e-5f ? Vector3.Distance(s, w) / bones : 1f;
        }

        /// <summary>Moving down in the picture (a slow fist's direction is mostly noise: only a clear move counts).</summary>
        bool Drops(Vector2 velocity)
        {
            float speed = velocity.magnitude;
            return speed >= ReArmSpeed && -velocity.y >= MaxDown * speed;
        }
    }
}
