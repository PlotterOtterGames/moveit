using MoveIt.Motion;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games
{
    /// <summary>One arm or leg of a <see cref="ToyRig"/>, as angles (degrees).</summary>
    public struct ToyLimb
    {
        /// <summary>Forward from hanging straight down: 90 = pointing ahead, 180 = straight up, negative = behind.</summary>
        public float Swing;
        /// <summary>Out to the side from the body, away from it (degrees).</summary>
        public float Out;
        /// <summary>The elbow or knee: 0 = straight. An elbow folds the forearm forward and up, a knee the shin back.</summary>
        public float Bend;

        public ToyLimb(float swing, float outward = 0f, float bend = 0f)
        {
            Swing = swing;
            Out = outward;
            Bend = bend;
        }

        public static ToyLimb Lerp(ToyLimb a, ToyLimb b, float t) =>
            new ToyLimb(Mathf.Lerp(a.Swing, b.Swing, t), Mathf.Lerp(a.Out, b.Out, t), Mathf.Lerp(a.Bend, b.Bend, t));
    }

    /// <summary>
    /// A body's pose as a few angles: turned into the 17 camera joints by <see cref="ToyPoses.Build"/>, so keys can
    /// stand in for the player's body (<see cref="BodyPuppet.KeyRig"/>: a keeper's stance without a camera).
    /// </summary>
    public struct ToyRig
    {
        /// <summary>The middle between the hips, in the figure's frame (metres; +z = the way it faces, +x = its right).</summary>
        public Vector3 Hips;
        /// <summary>The torso tipped forward (degrees; negative = back) and to its right.</summary>
        public float Lean, SideLean;
        /// <summary>The shoulders turned against the hips (degrees, + = the right shoulder back: a wind-up for a right punch).</summary>
        public float Twist;
        public ToyLimb LeftArm, RightArm, LeftLeg, RightLeg;
        /// <summary>The head nodding down (degrees) and turned to its right.</summary>
        public float HeadPitch, HeadYaw;

        public static ToyRig Standing => new ToyRig
        {
            Hips = new Vector3(0f, ToyPoses.HipHeight, 0f),
            LeftArm = new ToyLimb(4f, 10f, 12f), RightArm = new ToyLimb(4f, 10f, 12f),
            LeftLeg = new ToyLimb(0f, 2f), RightLeg = new ToyLimb(0f, 2f),
        };

        public static ToyRig Lerp(ToyRig a, ToyRig b, float t) => new ToyRig
        {
            Hips = Vector3.Lerp(a.Hips, b.Hips, t),
            Lean = Mathf.Lerp(a.Lean, b.Lean, t), SideLean = Mathf.Lerp(a.SideLean, b.SideLean, t), Twist = Mathf.Lerp(a.Twist, b.Twist, t),
            LeftArm = ToyLimb.Lerp(a.LeftArm, b.LeftArm, t), RightArm = ToyLimb.Lerp(a.RightArm, b.RightArm, t),
            LeftLeg = ToyLimb.Lerp(a.LeftLeg, b.LeftLeg, t), RightLeg = ToyLimb.Lerp(a.RightLeg, b.RightLeg, t),
            HeadPitch = Mathf.Lerp(a.HeadPitch, b.HeadPitch, t), HeadYaw = Mathf.Lerp(a.HeadYaw, b.HeadYaw, t),
        };
    }

    /// <summary>
    /// The figure's body measurements (the same as <see cref="BodyPuppet"/> scales the player to: a torso of 0.52 m, the
    /// hips 0.95 m up) and <see cref="Build"/>, which turns a <see cref="ToyRig"/> into the 17 joints, plus a few
    /// ready-made moves (standing, running, cheering). Pure maths, no scene.
    /// </summary>
    public static class ToyPoses
    {
        public const float HipHeight = 0.95f, Torso = 0.52f, ShoulderHalf = 0.19f, HipHalf = 0.11f;
        public const float UpperArm = 0.28f, Forearm = 0.27f, Thigh = 0.43f, Shin = 0.44f;
        /// <summary>From the shoulders' middle to the middle of the head.</summary>
        public const float Neck = 0.24f;

        /// <summary>The joints of <paramref name="rig"/> into <paramref name="joints"/> (17, <see cref="Joint"/> order).</summary>
        public static void Build(in ToyRig rig, Vector3[] joints)
        {
            var hips = rig.Hips;
            var torso = Quaternion.Euler(rig.Lean, 0f, -rig.SideLean);
            var shoulders = torso * Quaternion.Euler(0f, rig.Twist, 0f);
            var chest = hips + torso * new Vector3(0f, Torso, 0f);

            joints[(int)Joint.LeftHip] = hips + new Vector3(-HipHalf, 0f, 0f);
            joints[(int)Joint.RightHip] = hips + new Vector3(HipHalf, 0f, 0f);
            joints[(int)Joint.LeftShoulder] = chest + shoulders * new Vector3(-ShoulderHalf, 0f, 0f);
            joints[(int)Joint.RightShoulder] = chest + shoulders * new Vector3(ShoulderHalf, 0f, 0f);

            Arm(joints, shoulders, Joint.LeftShoulder, Joint.LeftElbow, Joint.LeftWrist, rig.LeftArm, -1f);
            Arm(joints, shoulders, Joint.RightShoulder, Joint.RightElbow, Joint.RightWrist, rig.RightArm, 1f);
            Leg(joints, Joint.LeftHip, Joint.LeftKnee, Joint.LeftAnkle, rig.LeftLeg, -1f);
            Leg(joints, Joint.RightHip, Joint.RightKnee, Joint.RightAnkle, rig.RightLeg, 1f);

            var head = shoulders * Quaternion.Euler(rig.HeadPitch, rig.HeadYaw, 0f);
            var centre = chest + shoulders * new Vector3(0f, Neck, 0f);
            joints[(int)Joint.Nose] = centre + head * new Vector3(0f, -0.02f, 0.1f);
            joints[(int)Joint.LeftEye] = centre + head * new Vector3(-0.035f, 0.02f, 0.085f);
            joints[(int)Joint.RightEye] = centre + head * new Vector3(0.035f, 0.02f, 0.085f);
            joints[(int)Joint.LeftEar] = centre + head * new Vector3(-0.08f, 0f, 0f);
            joints[(int)Joint.RightEar] = centre + head * new Vector3(0.08f, 0f, 0f);
        }

        static void Arm(Vector3[] j, Quaternion frame, Joint shoulder, Joint elbow, Joint wrist, ToyLimb l, float side)
        {
            // hanging down, swung forward around the shoulders' axis, then out to the side
            var upper = Quaternion.Euler(-l.Swing, 0f, 0f);
            var outward = Quaternion.Euler(0f, 0f, side * l.Out);
            var dir = frame * outward * upper * Vector3.down;
            var bend = frame * outward * Quaternion.Euler(-(l.Swing + l.Bend), 0f, 0f) * Vector3.down;
            j[(int)elbow] = j[(int)shoulder] + dir * UpperArm;
            j[(int)wrist] = j[(int)elbow] + bend * Forearm;
        }

        static void Leg(Vector3[] j, Joint hip, Joint knee, Joint ankle, ToyLimb l, float side)
        {
            var thigh = Quaternion.Euler(0f, 0f, side * l.Out) * Quaternion.Euler(-l.Swing, 0f, 0f) * Vector3.down;
            var shin = Quaternion.Euler(0f, 0f, side * l.Out) * Quaternion.Euler(-(l.Swing - l.Bend), 0f, 0f) * Vector3.down;
            j[(int)knee] = j[(int)hip] + thigh * Thigh;
            j[(int)ankle] = j[(int)knee] + shin * Shin;
        }

        /// <summary>
        /// A straight-ish arm pointing from <paramref name="shoulder"/> at <paramref name="target"/> (the figure's frame,
        /// an upright torso): for keyboard play, where a hand reaches for a point. <paramref name="right"/> = which arm.
        /// </summary>
        public static ToyLimb Aim(Vector3 shoulder, Vector3 target, bool right, float bend = 10f)
        {
            var d = target - shoulder;
            if (d.sqrMagnitude < 1e-6f) return new ToyLimb(0f, 10f, bend);
            d.Normalize();
            // the arm's direction is Rz(side * out) * Rx(-swing) * down = (cos s sin t, -cos s cos t, sin s)
            float swing = Mathf.Asin(Mathf.Clamp(d.z, -1f, 1f)) * Mathf.Rad2Deg;
            float turn = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
            return new ToyLimb(swing + bend * 0.5f, right ? turn : -turn, bend);
        }

        /// <summary>The height of the hips over the ankles' lowest point for a rig (to stand it on the ground).</summary>
        public static float FeetBelowHips(in ToyRig rig, Vector3[] scratch)
        {
            Build(rig, scratch);
            return rig.Hips.y - Mathf.Min(scratch[(int)Joint.LeftAnkle].y, scratch[(int)Joint.RightAnkle].y);
        }

        /// <summary>Standing at ease, breathing (<paramref name="t"/> in seconds).</summary>
        public static ToyRig Idle(float t)
        {
            var r = ToyRig.Standing;
            float breath = Mathf.Sin(t * 2.1f);
            r.Hips.y += 0.006f * breath;
            r.LeftArm.Out += 2f * breath;
            r.RightArm.Out += 2f * breath;
            r.HeadPitch = 2f * Mathf.Sin(t * 0.7f);
            return r;
        }

        /// <summary>Running: <paramref name="phase"/> in cycles (one cycle = two steps), <paramref name="effort"/> 0..1
        /// (a jog to a sprint: longer strides, more arm swing, more lean). The feet stay near the ground.</summary>
        public static ToyRig Run(float phase, float effort)
        {
            effort = Mathf.Clamp01(effort);
            float a = phase * Mathf.PI * 2f;
            float s = Mathf.Sin(a), c = Mathf.Cos(a);
            float stride = Mathf.Lerp(22f, 48f, effort), arms = Mathf.Lerp(30f, 62f, effort);
            var r = ToyRig.Standing;
            r.Lean = Mathf.Lerp(6f, 16f, effort);
            r.Hips.y = HipHeight - 0.05f + 0.035f * Mathf.Abs(c) * (0.6f + effort);
            r.LeftLeg = new ToyLimb(stride * s, 2f, 25f + 55f * Mathf.Max(0f, -s) * (0.5f + effort));
            r.RightLeg = new ToyLimb(-stride * s, 2f, 25f + 55f * Mathf.Max(0f, s) * (0.5f + effort));
            r.LeftArm = new ToyLimb(-arms * s * 0.8f + 10f, 8f, 85f);
            r.RightArm = new ToyLimb(arms * s * 0.8f + 10f, 8f, 85f);
            r.Twist = 6f * s * effort;
            return r;
        }

        /// <summary>Both arms up, bouncing on the toes (<paramref name="t"/> in seconds).</summary>
        public static ToyRig Cheer(float t)
        {
            var r = ToyRig.Standing;
            float bounce = Mathf.Abs(Mathf.Sin(t * 7f));
            r.Hips.y += 0.06f * bounce;
            r.LeftArm = new ToyLimb(165f + 10f * bounce, 25f, 10f);
            r.RightArm = new ToyLimb(165f + 10f * bounce, 25f, 10f);
            r.HeadPitch = -12f;
            return r;
        }

        /// <summary>Head down, arms hanging: a point lost.</summary>
        public static ToyRig Sulk(float t)
        {
            var r = ToyRig.Standing;
            r.Lean = 8f;
            r.HeadPitch = 25f;
            r.LeftArm = new ToyLimb(-4f, 4f, 5f);
            r.RightArm = new ToyLimb(-4f, 4f, 5f);
            r.Hips.y -= 0.01f * Mathf.Sin(t * 1.5f);
            return r;
        }
    }
}
