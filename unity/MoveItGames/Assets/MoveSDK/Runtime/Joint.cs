namespace MoveIt.Motion
{
    /// <summary>COCO-17 joints, in protocol order (docs/protocol.md). Left/Right are the player's own sides.</summary>
    public enum Joint
    {
        Nose = 0,
        LeftEye, RightEye,
        LeftEar, RightEar,
        LeftShoulder, RightShoulder,
        LeftElbow, RightElbow,
        LeftWrist, RightWrist,
        LeftHip, RightHip,
        LeftKnee, RightKnee,
        LeftAnkle, RightAnkle,
    }

    public static class Skeleton
    {
        public const int JointCount = 17;

        public static readonly (Joint a, Joint b)[] Bones =
        {
            (Joint.LeftShoulder, Joint.RightShoulder),
            (Joint.LeftShoulder, Joint.LeftElbow), (Joint.LeftElbow, Joint.LeftWrist),
            (Joint.RightShoulder, Joint.RightElbow), (Joint.RightElbow, Joint.RightWrist),
            (Joint.LeftShoulder, Joint.LeftHip), (Joint.RightShoulder, Joint.RightHip),
            (Joint.LeftHip, Joint.RightHip),
            (Joint.LeftHip, Joint.LeftKnee), (Joint.LeftKnee, Joint.LeftAnkle),
            (Joint.RightHip, Joint.RightKnee), (Joint.RightKnee, Joint.RightAnkle),
            (Joint.Nose, Joint.LeftEye), (Joint.Nose, Joint.RightEye),
            (Joint.LeftEye, Joint.LeftEar), (Joint.RightEye, Joint.RightEar),
        };

        /// <summary>True for the player's left-side joints (odd indices).</summary>
        public static bool IsLeft(Joint j) => (int)j % 2 == 1;

        /// <summary>True for the player's right-side joints (even indices except the nose).</summary>
        public static bool IsRight(Joint j) => j != Joint.Nose && (int)j % 2 == 0;
    }
}
