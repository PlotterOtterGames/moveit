using UnityEngine;

namespace MoveIt.Motion
{
    public struct ThrowInfo
    {
        public bool RightHand;
        /// <summary>Peak upward wrist speed during the swing, torso lengths per second.</summary>
        public float Speed;
        /// <summary>Average sideways wrist speed during the upswing, torso lengths per second (+ = screen right).</summary>
        public float SideSpeed;
    }

    /// <summary>
    /// Detects an underhand throw (bowling roll) from a front camera: the throwing wrist hangs low, then swings
    /// up in front of the body fast. The backswing goes behind the body, so depth (z) rejects it. Raising both
    /// hands (the "start" gesture) is rejected because the other hand rises too.
    ///
    /// With 3D world joints (protocol v1.1) depth is reliable, so a throw also needs a backswing behind the body
    /// (or a very fast swing): one hand going up to the face or head is not a throw.
    /// </summary>
    public sealed class UnderhandThrowDetector
    {
        /// <summary>Wrist this far below the hip line (torso lengths) arms the throw.</summary>
        public float LowBelowHips = 0.1f;
        /// <summary>The throw releases when the wrist rises past this line (torso lengths above the hips).</summary>
        public float ReleaseAboveHips = 0.45f;
        /// <summary>Minimum peak upward speed, torso lengths per second.</summary>
        public float MinUpSpeed = 1.8f;
        /// <summary>Max time from the last low point to the release.</summary>
        public float MaxSwingSeconds = 1.5f;
        /// <summary>If the other hand rises faster than this at release, it's a two-hand raise, not a throw.</summary>
        public float OtherHandMaxUpSpeed = 1.2f;
        public bool RequireInFront = true;
        /// <summary>Wrist depth relative to the hips at release, torso lengths. MediaPipe depth is noisy, so allow some slack.</summary>
        public float InFrontTolerance = 0.15f;
        /// <summary>With world joints: require the wrist to have been behind the body during the swing.</summary>
        public bool RequireBackswing = true;
        /// <summary>How far behind the hips (torso lengths) counts as a backswing.</summary>
        public float BackswingDepth = 0.15f;
        /// <summary>A swing this fast (torso lengths/s) counts even without a backswing (a quick push).</summary>
        public float FastThrowSpeed = 4f;

        sealed class HandState
        {
            public bool Visible;
            public bool HasPrevious;
            public Vector3 Previous;
            public float Height;   // torso lengths above the hips
            public float Depth;    // torso lengths in front (-) / behind (+) the hips
            public float UpSpeed;
            public float SideSpeed;
            public double ArmedAt = -1;
            public float PeakUp;
            public float SideSum;  // sideways speed summed over the upswing: averaging beats single-frame jitter
            public int SideSamples;
            public bool HasWorld;
            public double BackswingAt = -10;
        }

        readonly HandState _left = new HandState();
        readonly HandState _right = new HandState();
        double _lastSampleTime;

        public void Reset()
        {
            foreach (var s in new[] { _left, _right })
            {
                s.HasPrevious = false;
                s.ArmedAt = -1;
                s.PeakUp = 0f;
                s.BackswingAt = -10;
            }
            _lastSampleTime = 0;
        }

        /// <summary>Call every frame with the active body. Returns true once per detected throw.</summary>
        public bool Update(Body body, float torso, out ThrowInfo info)
        {
            info = default;
            if (body == null || torso < 0.01f || body.SampleTime == _lastSampleTime) return false;
            float dt = _lastSampleTime > 0 ? (float)(body.SampleTime - _lastSampleTime) : 0f;
            if (dt > 0f && dt < Body.MinSpeedInterval) return false;
            _lastSampleTime = body.SampleTime;

            Sample(body, torso, Joint.RightWrist, _right, dt);
            Sample(body, torso, Joint.LeftWrist, _left, dt);

            bool right = Released(body.SampleTime, _right, _left);
            bool left = !right && Released(body.SampleTime, _left, _right);
            if (!right && !left) return false;

            var s = right ? _right : _left;
            info = new ThrowInfo { RightHand = right, Speed = s.PeakUp, SideSpeed = s.SideSamples > 0 ? s.SideSum / s.SideSamples : 0f };
            _left.ArmedAt = _right.ArmedAt = -1;
            return true;
        }

        void Sample(Body body, float torso, Joint wrist, HandState s, float dt)
        {
            // Low-lag track: the release is detected sooner.
            var w = body.Fast[(int)wrist];
            var hips = body.FastHipCenter;
            s.Visible = body.IsVisible(wrist, 0.3f);
            if (s.HasPrevious && dt > 0f && dt < 0.25f)
            {
                s.UpSpeed = (w.y - s.Previous.y) / dt / torso;
                s.SideSpeed = (w.x - s.Previous.x) / dt / torso;
            }
            else s.UpSpeed = s.SideSpeed = 0f;
            s.Previous = w;
            s.HasPrevious = true;
            s.Height = (w.y - hips.y) / torso;
            s.HasWorld = body.HasWorld;
            s.Depth = body.HasWorld
                ? (body.World[(int)wrist].z - body.WorldHipCenter.z) / Mathf.Max(body.WorldTorsoLength, 0.2f)
                : (w.z - hips.z) / torso;
            if (s.Depth > BackswingDepth) s.BackswingAt = body.SampleTime;
        }

        bool Released(double now, HandState s, HandState other)
        {
            if (!s.Visible)
            {
                s.ArmedAt = -1;
                return false;
            }
            if (s.Height < -LowBelowHips)
            {
                s.ArmedAt = now;
                s.PeakUp = 0f;
                s.SideSum = 0f;
                s.SideSamples = 0;
                return false;
            }
            if (s.ArmedAt < 0) return false;
            if (now - s.ArmedAt > MaxSwingSeconds)
            {
                s.ArmedAt = -1;
                return false;
            }

            s.PeakUp = Mathf.Max(s.PeakUp, s.UpSpeed);
            s.SideSum += s.SideSpeed;
            s.SideSamples++;
            if (s.Height < ReleaseAboveHips) return false;

            s.ArmedAt = -1;
            bool twoHanded = other.UpSpeed > OtherHandMaxUpSpeed || other.Height > ReleaseAboveHips;
            bool inFront = !RequireInFront || s.Depth < InFrontTolerance;
            bool swungBack = !RequireBackswing || !s.HasWorld || now - s.BackswingAt <= MaxSwingSeconds
                             || s.PeakUp >= FastThrowSpeed;
            return s.PeakUp >= MinUpSpeed && !twoHanded && inFront && swungBack;
        }
    }
}
