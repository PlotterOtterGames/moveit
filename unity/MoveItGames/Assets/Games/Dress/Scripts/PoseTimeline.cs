using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games.Dress
{
    /// <summary>
    /// The last camera samples of the player's joints, on the camera's clock, so the clothes can be drawn for exactly
    /// the moment of the camera picture on the screen. The picture and the joints come from the same camera frame but
    /// travel separately (the JPEG stream and the pose packets); fitting the clothes to the newest joints while the
    /// picture is a frame older (or the other way round) makes them slide off the body on every move.
    /// </summary>
    public sealed class PoseTimeline
    {
        const int Capacity = 12;
        readonly double[] _time = new double[Capacity];
        readonly Vector3[][] _joints = new Vector3[Capacity][];
        readonly float[][] _visibility = new float[Capacity][];
        int _count;

        public PoseTimeline()
        {
            for (int i = 0; i < Capacity; i++)
            {
                _joints[i] = new Vector3[Skeleton.JointCount];
                _visibility[i] = new float[Skeleton.JointCount];
            }
        }

        public int Count => _count;
        public double Newest => _count > 0 ? _time[_count - 1] : 0;
        public double Oldest => _count > 0 ? _time[0] : 0;

        public void Clear() => _count = 0;

        /// <summary>A sample (capture time in seconds). Older or repeated samples are ignored.</summary>
        public void Add(double time, Vector3[] joints, float[] visibility)
        {
            if (_count > 0 && time <= _time[_count - 1]) return;
            if (_count == Capacity)
            {
                var j = _joints[0];
                var v = _visibility[0];
                for (int i = 1; i < Capacity; i++)
                {
                    _time[i - 1] = _time[i];
                    _joints[i - 1] = _joints[i];
                    _visibility[i - 1] = _visibility[i];
                }
                _joints[Capacity - 1] = j;
                _visibility[Capacity - 1] = v;
                _count--;
            }
            _time[_count] = time;
            System.Array.Copy(joints, _joints[_count], Skeleton.JointCount);
            System.Array.Copy(visibility, _visibility[_count], Skeleton.JointCount);
            _count++;
        }

        /// <summary>
        /// The joints at <paramref name="time"/>: between two samples interpolated, before the oldest the oldest,
        /// after the newest carried on at the last speed for at most <paramref name="maxAhead"/> seconds.
        /// </summary>
        public bool At(double time, Vector3[] joints, float[] visibility, float maxAhead = 0.05f)
        {
            if (_count == 0) return false;
            if (time <= _time[0] || _count == 1)
            {
                Copy(0, joints, visibility);
                return true;
            }
            if (time >= _time[_count - 1])
            {
                int a = _count - 2, b = _count - 1;
                float span = (float)(_time[b] - _time[a]);
                float k = span > 0f ? 1f + Mathf.Min((float)(time - _time[b]), maxAhead) / span : 1f;
                Lerp(a, b, k, joints, visibility);
                return true;
            }
            int i = 1;
            while (_time[i] < time) i++;
            Lerp(i - 1, i, (float)((time - _time[i - 1]) / (_time[i] - _time[i - 1])), joints, visibility);
            return true;
        }

        void Copy(int i, Vector3[] joints, float[] visibility)
        {
            System.Array.Copy(_joints[i], joints, Skeleton.JointCount);
            System.Array.Copy(_visibility[i], visibility, Skeleton.JointCount);
        }

        void Lerp(int a, int b, float k, Vector3[] joints, float[] visibility)
        {
            for (int j = 0; j < Skeleton.JointCount; j++)
            {
                joints[j] = Vector3.LerpUnclamped(_joints[a][j], _joints[b][j], k);
                visibility[j] = Mathf.Min(_visibility[a][j], _visibility[b][j]);
            }
        }
    }
}
