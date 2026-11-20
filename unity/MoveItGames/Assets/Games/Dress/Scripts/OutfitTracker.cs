using System;
using MoveIt.Motion;
using UnityEngine;
using Joint = MoveIt.Motion.Joint;

namespace MoveIt.Games.Dress
{
    /// <summary>
    /// The player's joints for the clothes: steady, and without lag against the camera picture they are drawn on.
    ///
    /// The picture is shown a couple of camera frames late (<see cref="Lookahead"/>), so every joint can be fitted
    /// with the camera AI's samples from before AND after the picture's moment: a straight line through them, weighted
    /// by how near in time they are and how sure the AI was, with glitches left out (the pose model's hands jump for a
    /// frame or two, mostly in front of the chest), passes through the joint where it was in that picture. A causal
    /// filter (One Euro) can only trade shaking for trailing; this does neither. A still joint is fitted over a longer
    /// window (rock steady), a moving one over a short one (it keeps up); the window is chosen from the joint's speed,
    /// which the samples after the picture already show when a move starts.
    ///
    /// Joints are in view space (image heights, y up, mirrored like the picture on the screen).
    /// </summary>
    public sealed class OutfitTracker
    {
        /// <summary>Camera frames the picture waits for the samples after it (~33 ms each on the box).</summary>
        public int Lookahead = 2;
        /// <summary>The time weight's width (a Gaussian's sigma, seconds) for a still joint and for a fast one.</summary>
        public float StillSigma = 0.10f, MovingSigma = 0.045f;
        /// <summary>Speeds (torso lengths a second) between which the window narrows from still to moving.</summary>
        public float SlowSpeed = 0.2f, FastSpeed = 1.5f;
        /// <summary>A sample this far off the fitted line (torso lengths) is a glitch and left out.</summary>
        public float GlitchTorso = 0.3f;
        /// <summary>The body's turn (from the world joints) is averaged over this window (sigma, seconds).</summary>
        public float WorldSigma = 0.15f;
        /// <summary>A sample's turn this far (radians) from the others' is the pose model swapping left and right for
        /// a frame or two (it happens when someone walks up close): left out.</summary>
        public float FlipLimit = 0.7f;

        const int Capacity = 48;
        readonly double[] _time = new double[Capacity];
        readonly Vector2[][] _joints = new Vector2[Capacity][];
        readonly float[][] _vis = new float[Capacity][];
        readonly Vector3[][] _world = new Vector3[Capacity][];
        readonly bool[] _hasWorld = new bool[Capacity];
        int _start, _count;
        float _torso;
        double _interval = 1.0 / 30.0;

        // scratch for one joint's fit
        readonly double[] _dt = new double[Capacity];
        readonly float[] _w = new float[Capacity], _u = new float[Capacity];
        readonly Vector2[] _p = new Vector2[Capacity];

        public OutfitTracker()
        {
            for (int i = 0; i < Capacity; i++)
            {
                _joints[i] = new Vector2[Skeleton.JointCount];
                _vis[i] = new float[Skeleton.JointCount];
                _world[i] = new Vector3[Skeleton.JointCount];
            }
        }

        public int Count => _count;
        public double Newest => _count > 0 ? _time[Slot(_count - 1)] : 0;
        public double Oldest => _count > 0 ? _time[Slot(0)] : 0;
        /// <summary>The camera's frame interval (seconds), from the samples.</summary>
        public double Interval => _interval;
        /// <summary>Torso length (view units) the glitch and speed thresholds are measured in.</summary>
        public float Torso => _torso;

        /// <summary>The moment to show: the newest sample's, <see cref="Lookahead"/> frames back.</summary>
        public double ShowTime => _count > 0 ? Newest - Lookahead * _interval : 0;

        public void Clear()
        {
            _count = 0;
            _start = 0;
            _torso = 0f;
            _interval = 1.0 / 30.0;
            _turn = float.NaN;
        }

        int Slot(int k) => (_start + k) % Capacity;

        /// <summary>A sample: its capture time (seconds), joints (view space; x, y used), the AI's visibility, and the
        /// world joints when it sends them. Older or repeated samples are ignored.</summary>
        public void Add(double time, Vector3[] joints, float[] visibility, Vector3[] world = null)
        {
            if (_count > 0)
            {
                double gap = time - Newest;
                if (gap <= 0) return;
                if (gap > 0.5)
                {
                    Clear();   // lost for a while: start over
                }
                else if (gap > 0.004)
                {
                    double g = Math.Min(Math.Max(gap, 1.0 / 120.0), 0.2);
                    _interval += (g - _interval) * (g < _interval ? 0.3 : 0.05);   // follows the shortest gaps (skipped frames are gaps of two)
                }
            }
            int previous = _count > 0 ? Slot(_count - 1) : -1;
            int s;
            if (_count == Capacity)
            {
                s = _start;
                _start = (_start + 1) % Capacity;
            }
            else s = Slot(_count++);
            _time[s] = time;
            for (int j = 0; j < Skeleton.JointCount; j++)
            {
                _joints[s][j] = new Vector2(joints[j].x, joints[j].y);
                _vis[s][j] = visibility[j];
            }
            _hasWorld[s] = world != null;
            if (world != null) Array.Copy(world, _world[s], Skeleton.JointCount);
            if (FixSwaps && world != null && TurnJumped(s))
            {
                SwapSides(s);
                Swaps++;
            }

            // the body's size for the thresholds: the torso when the shoulders and hips are seen
            const int ls = (int)Joint.LeftShoulder, rs = (int)Joint.RightShoulder, lh = (int)Joint.LeftHip, rh = (int)Joint.RightHip;
            if (visibility[ls] > 0.5f && visibility[rs] > 0.5f)
            {
                Vector2 sc = (_joints[s][ls] + _joints[s][rs]) * 0.5f;
                float torso = visibility[lh] > 0.3f && visibility[rh] > 0.3f
                    ? Vector2.Distance(sc, (_joints[s][lh] + _joints[s][rh]) * 0.5f)
                    : 1.25f * Vector2.Distance(_joints[s][ls], _joints[s][rs]);
                if (torso > 0.01f) _torso = _torso <= 0f ? torso : _torso + (torso - _torso) * 0.1f;
            }
        }

        /// <summary>Put the player's sides back when the pose model swaps them (left joints on the right side of the body
        /// and the other way round). It does that for a few frames to a second when someone walks up close or half
        /// leaves the picture (box recording 2026-11-01, 37.4-38.9 s): the clothes would flip, and the world joints would
        /// say the back faces the camera. Needs the world joints.</summary>
        public bool FixSwaps = true;
        /// <summary>Swaps put right so far.</summary>
        public int Swaps { get; private set; }
        float _turn = float.NaN;

        static readonly (int a, int b)[] Pairs =
        {
            ((int)Joint.LeftEye, (int)Joint.RightEye), ((int)Joint.LeftEar, (int)Joint.RightEar),
            ((int)Joint.LeftShoulder, (int)Joint.RightShoulder), ((int)Joint.LeftElbow, (int)Joint.RightElbow),
            ((int)Joint.LeftWrist, (int)Joint.RightWrist), ((int)Joint.LeftHip, (int)Joint.RightHip),
            ((int)Joint.LeftKnee, (int)Joint.RightKnee), ((int)Joint.LeftAnkle, (int)Joint.RightAnkle),
        };

        void SwapSides(int s)
        {
            foreach (var (a, b) in Pairs)
            {
                (_joints[s][a], _joints[s][b]) = (_joints[s][b], _joints[s][a]);
                (_vis[s][a], _vis[s][b]) = (_vis[s][b], _vis[s][a]);
                (_world[s][a], _world[s][b]) = (_world[s][b], _world[s][a]);
            }
        }

        /// <summary>
        /// Is sample <paramref name="s"/> one with the sides swapped? Its turn (from the world joints) is then ~180
        /// degrees from the body's running turn at once, while a real turn moves at most ~30 degrees from one camera
        /// frame to the next (it passes side on, smoothly). The running turn follows every sample (swapped ones put
        /// right), so a swap that lasts a second is still put right, and a person who turns during it too.
        /// </summary>
        bool TurnJumped(int s)
        {
            var w = _world[s];
            float yaw = OutfitPose.WorldYaw(w[(int)Joint.LeftShoulder], w[(int)Joint.RightShoulder], w[(int)Joint.LeftHip], w[(int)Joint.RightHip]);
            if (float.IsNaN(_turn))
            {
                _turn = yaw;
                return false;
            }
            float d = Wrap(yaw - _turn);
            bool swapped = Mathf.Abs(d) > SwapJump;
            if (swapped) d = Wrap(d + Mathf.PI);
            _turn = Wrap(_turn + 0.6f * d);
            return swapped;
        }

        /// <summary>A sample whose turn is this far (radians) from the running one has its sides swapped.</summary>
        public float SwapJump = 1.75f;

        static float Wrap(float a)
        {
            while (a > Mathf.PI) a -= 2f * Mathf.PI;
            while (a < -Mathf.PI) a += 2f * Mathf.PI;
            return a;
        }

        /// <summary>
        /// The joints at <paramref name="time"/> (seconds, normally a camera picture's capture time): fitted from the
        /// samples around it. Visibility is the samples' time-weighted average.
        /// </summary>
        public bool At(double time, Vector2[] joints, float[] visibility)
        {
            if (_count == 0) return false;
            float torso = Mathf.Max(_torso, 0.02f);
            for (int j = 0; j < Skeleton.JointCount; j++)
                joints[j] = Fit(j, time, torso, out visibility[j]);
            return true;
        }

        /// <summary>
        /// The body's turn at <paramref name="time"/> (radians, 0 = facing the camera, + = the chest toward the screen's
        /// right) from the world joints' shoulders and hips (<see cref="OutfitPose.WorldYaw"/>): each sample's turn,
        /// averaged around the others, the ones far off left out. False when the samples carry no world joints.
        /// </summary>
        public bool WorldYawAt(double time, out float yaw)
        {
            yaw = 0f;
            int n = 0;
            double cs = 0, sn = 0;
            for (int k = 0; k < _count; k++)
            {
                int s = Slot(k);
                if (!_hasWorld[s]) continue;
                double d = _time[s] - time;
                if (Math.Abs(d) > 3 * WorldSigma) continue;
                var w = _world[s];
                float sure = Mathf.Min(Mathf.Min(_vis[s][(int)Joint.LeftShoulder], _vis[s][(int)Joint.RightShoulder]),
                                       Mathf.Min(_vis[s][(int)Joint.LeftHip], _vis[s][(int)Joint.RightHip]));
                _dt[n] = OutfitPose.WorldYaw(w[(int)Joint.LeftShoulder], w[(int)Joint.RightShoulder], w[(int)Joint.LeftHip], w[(int)Joint.RightHip]);
                _w[n] = Gauss((float)(d / WorldSigma)) * (0.1f + 0.9f * Mathf.Clamp01(sure));
                cs += _w[n] * Math.Cos(_dt[n]);
                sn += _w[n] * Math.Sin(_dt[n]);
                n++;
            }
            if (n == 0) return false;
            double mean = Math.Atan2(sn, cs);
            for (int iteration = 0; iteration < 3; iteration++)
            {
                double sum = 0, wsum = 0;
                for (int i = 0; i < n; i++)
                {
                    double a = _dt[i] - mean;
                    while (a > Math.PI) a -= 2 * Math.PI;
                    while (a < -Math.PI) a += 2 * Math.PI;
                    double r = Math.Abs(a) / FlipLimit;
                    if (r >= 1) continue;
                    double w = _w[i] * (1 - r * r) * (1 - r * r);
                    sum += w * a;
                    wsum += w;
                }
                if (wsum <= 1e-9) break;
                mean += sum / wsum;
            }
            yaw = (float)mean;
            return true;
        }

        Vector2 Fit(int joint, double time, float torso, out float visibility)
        {
            int n = 0;
            float vsum = 0f, gsum = 0f;
            double reach = 3.0 * StillSigma;
            for (int k = 0; k < _count; k++)
            {
                int s = Slot(k);
                double d = _time[s] - time;
                if (d < -reach || d > reach) continue;
                _dt[n] = d;
                _p[n] = _joints[s][joint];
                float v = _vis[s][joint];
                _w[n] = VisWeight(v);
                float g = Gauss((float)(d / StillSigma));
                vsum += g * v;
                gsum += g;
                n++;
            }
            if (n == 0)
            {
                // the picture is older than every sample kept (or newer than all by far): the nearest sample
                int s = time < Oldest ? Slot(0) : Slot(_count - 1);
                visibility = _vis[s][joint];
                return _joints[s][joint];
            }
            visibility = gsum > 0f ? vsum / gsum : 0f;

            // 1) a line that glitches can't pull: the median of the slopes between every two samples near the moment
            //    and the median offset (Theil-Sen: up to ~29 % of the samples may be anything)
            RobustLine(n, out Vector2 a, out Vector2 b);
            // 2) samples far off it are glitches; how fast the joint moves picks the window
            float limit = GlitchTorso * torso;
            for (int i = 0; i < n; i++)
            {
                float r = (_p[i] - (a + b * (float)_dt[i])).magnitude / limit;
                _u[i] = r >= 1f ? 0f : (1f - r * r) * (1f - r * r);
            }
            float speed = b.magnitude / torso;
            float sigma = Mathf.Lerp(StillSigma, MovingSigma, Smooth(SlowSpeed, FastSpeed, speed));
            // 3) the weighted line through the rest, read at the moment
            Line(n, sigma, out a, out b);
            return a;
        }

        /// <summary>Samples within this many seconds of the moment make the robust line (step 1 of <see cref="Fit"/>).</summary>
        public float RobustReach = 0.2f;

        readonly float[] _slopes = new float[Capacity * (Capacity - 1) / 2], _offsets = new float[Capacity];
        readonly int[] _near = new int[Capacity];

        void RobustLine(int n, out Vector2 a, out Vector2 b)
        {
            // the samples near the moment, the AI's sure ones if there are enough of them
            int m = 0;
            for (int pass = 0; pass < 2 && m < 3; pass++)
            {
                m = 0;
                for (int i = 0; i < n; i++)
                    if (Math.Abs(_dt[i]) <= RobustReach && (pass == 1 || _w[i] > 0.3f)) _near[m++] = i;
            }
            if (m == 0)
            {
                a = _p[0];
                b = Vector2.zero;
                return;
            }
            float bx = MedianSlope(m, true), by = MedianSlope(m, false);
            for (int k = 0; k < m; k++) _offsets[k] = _p[_near[k]].x - bx * (float)_dt[_near[k]];
            float ax = Median(_offsets, m);
            for (int k = 0; k < m; k++) _offsets[k] = _p[_near[k]].y - by * (float)_dt[_near[k]];
            float ay = Median(_offsets, m);
            a = new Vector2(ax, ay);
            b = new Vector2(bx, by);
        }

        float MedianSlope(int m, bool x)
        {
            int count = 0;
            for (int i = 0; i < m; i++)
                for (int j = i + 1; j < m; j++)
                {
                    int p = _near[i], q = _near[j];
                    double d = _dt[q] - _dt[p];
                    if (Math.Abs(d) < 1e-4) continue;
                    _slopes[count++] = (float)(((x ? _p[q].x : _p[q].y) - (x ? _p[p].x : _p[p].y)) / d);
                }
            return count == 0 ? 0f : Median(_slopes, count);
        }

        static float Median(float[] values, int count)
        {
            Array.Sort(values, 0, count);
            return count % 2 == 1 ? values[count / 2] : 0.5f * (values[count / 2 - 1] + values[count / 2]);
        }

        /// <summary>Weighted least-squares line p = a + b * dt over the gathered samples.</summary>
        void Line(int n, float sigma, out Vector2 a, out Vector2 b)
        {
            double s0 = 0, s1 = 0, s2 = 0;
            double px = 0, py = 0, qx = 0, qy = 0;
            float inv = 1f / Mathf.Max(sigma, 1e-3f);
            for (int i = 0; i < n; i++)
            {
                double w = Gauss((float)_dt[i] * inv) * _w[i] * _u[i];
                if (w <= 1e-6) continue;
                double d = _dt[i];
                s0 += w;
                s1 += w * d;
                s2 += w * d * d;
                px += w * _p[i].x;
                py += w * _p[i].y;
                qx += w * d * _p[i].x;
                qy += w * d * _p[i].y;
            }
            if (s0 <= 1e-9)
            {
                // nothing trusted near: plain average of what's there
                Vector2 m = Vector2.zero;
                for (int i = 0; i < n; i++) m += _p[i];
                a = m / n;
                b = Vector2.zero;
                return;
            }
            double det = s0 * s2 - s1 * s1;
            if (det <= 1e-12 * s0 * s0)
            {
                a = new Vector2((float)(px / s0), (float)(py / s0));
                b = Vector2.zero;
                return;
            }
            double bx = (s0 * qx - s1 * px) / det, by = (s0 * qy - s1 * py) / det;
            a = new Vector2((float)((px - bx * s1) / s0), (float)((py - by * s1) / s0));
            b = new Vector2((float)bx, (float)by);
        }

        /// <summary>How much a sample counts by the AI's visibility: unsure ones hardly, never quite nothing (a joint
        /// nobody sees is still placed somewhere; the rig fades what hangs on it).</summary>
        static float VisWeight(float v) => 0.03f + 0.97f * Smooth(0.25f, 0.75f, v);

        static float Gauss(float x) => Mathf.Exp(-0.5f * x * x);

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
