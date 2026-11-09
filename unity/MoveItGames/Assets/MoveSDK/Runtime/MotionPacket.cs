using System;

namespace MoveIt.Motion
{
    /// <summary>Motion Protocol v1.1 datagram, parsed with JsonUtility. Field names match docs/protocol.md.
    /// v1.1 fields (cam, model, wk) are optional: older senders simply leave them empty.</summary>
    [Serializable]
    public class MotionPacket
    {
        public int v;
        public string src;
        public long t;
        public long f;
        public int w;
        public int h;
        public float fps;
        public float inf;
        /// <summary>Frames per second the camera actually delivers (v1.1, 0 if unknown).</summary>
        public float cam;
        /// <summary>Pose model in use, e.g. "full" or "lite" (v1.1).</summary>
        public string model;
        public PersonPacket[] people;
    }

    [Serializable]
    public class PersonPacket
    {
        public int id;
        /// <summary>v1.1 (2026-11-05): the service's track for this person, the same while it follows them (the box's NPU
        /// engine; 0 = not known). With two people followed on the box, a packet may carry only one of them (they take
        /// turns on the NPU), so the SDK matches bodies by it rather than by position alone.</summary>
        public int tid;
        public float score;
        /// <summary>17 joints x [x, y, z, visibility], flat (68 floats), raw normalized camera coordinates.</summary>
        public float[] k;
        /// <summary>v1.1: 17 joints x [x, y, z] in metres, origin at the hips, MediaPipe axes
        /// (x image-right, y down, z toward the camera = negative). Empty if the sender has none.</summary>
        public float[] wk;
    }
}
