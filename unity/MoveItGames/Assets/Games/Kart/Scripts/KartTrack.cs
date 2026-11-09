using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Kart
{
    /// <summary>Where a track is (its scenery and colours; <see cref="KartTracks"/>).</summary>
    public enum KartTheme { Coast, Village, Dino }

    /// <summary>What lies on the road (<see cref="TrackFeature"/>).</summary>
    public enum FeatureKind
    {
        /// <summary>A jump: a wedge across the road from <see cref="TrackFeature.S"/> - <see cref="TrackFeature.Length"/>
        /// up to its lip at <see cref="TrackFeature.S"/>. A jump (or the wheel lifted) as the kart reaches it = a trick in
        /// the air and a boost on landing.</summary>
        Ramp,
        /// <summary>A boost pad (glowing arrows): driving over it = a short boost.</summary>
        Boost,
        /// <summary>A row of gift boxes across the road: driving through one gives an item.</summary>
        Items,
        /// <summary>A line of stars along the road.</summary>
        Stars,
    }

    /// <summary>Something on the road, in track coordinates: along it (m from the start line) and across it (m from the
    /// middle, + = the left side as the karts drive).</summary>
    [Serializable]
    public struct TrackFeature
    {
        public FeatureKind Kind;
        /// <summary>Along the track (m): where it starts (a ramp: its lip; boxes: the row).</summary>
        public float S;
        /// <summary>Across (m): its middle.</summary>
        public float D;
        /// <summary>Across (m): how wide (a ramp, a pad, the row of boxes).</summary>
        public float Width;
        /// <summary>Along (m): a ramp's run-up, a pad's length, a line of stars' length.</summary>
        public float Length;
        /// <summary>Boxes in the row, stars in the line.</summary>
        public int Count;

        public static TrackFeature Ramp(float s, float d = 0f, float width = 13f, float length = 6f) =>
            new TrackFeature { Kind = FeatureKind.Ramp, S = s, D = d, Width = width, Length = length };
        public static TrackFeature Pad(float s, float d, float width = 3.4f, float length = 5f) =>
            new TrackFeature { Kind = FeatureKind.Boost, S = s, D = d, Width = width, Length = length };
        public static TrackFeature Boxes(float s, int count = 4, float width = 11f) =>
            new TrackFeature { Kind = FeatureKind.Items, S = s, D = 0f, Width = width, Count = count };
        public static TrackFeature StarLine(float s, float d, float length = 24f, int count = 5) =>
            new TrackFeature { Kind = FeatureKind.Stars, S = s, D = d, Length = length, Count = count };
    }

    /// <summary>
    /// A closed race track as the rules see it: a centreline through control points (a closed centripetal Catmull-Rom
    /// spline, sampled every metre of its length), the road's width with a strip of grass or sand on each side before the
    /// barrier, gentle hills, and what lies on the road. Karts live in track coordinates: <c>s</c> along the centreline
    /// from the start line (wrapping at <see cref="Length"/>), <c>d</c> across it (+ = left). World positions are x and z
    /// of the plan (z = the plan's second axis), y up. Pure logic, tested (KartTrackTests).
    /// </summary>
    public sealed class KartTrack
    {
        public readonly string Id;
        public readonly KartTheme Theme;
        /// <summary>The road's width (m): six karts side by side and a little.</summary>
        public readonly float Width;
        /// <summary>The grass or sand strip each side, between the road and the barrier (m): slower.</summary>
        public readonly float Shoulder;
        public float Length { get; }
        public float HalfWidth => Width * 0.5f;
        /// <summary>The barrier's distance from the middle (m).</summary>
        public float Wall => HalfWidth + Shoulder;
        public readonly List<TrackFeature> Features;
        public readonly Vector2[] Control;

        readonly Vector2[] _p;
        readonly float[] _heading, _curvature, _height;
        readonly float _step;

        /// <param name="control">The plan's control points (m), in driving order, the first on the start line.</param>
        /// <param name="hills">(fraction of the length, height m) keys, smoothly joined (none = flat).</param>
        public KartTrack(string id, KartTheme theme, IList<Vector2> control, IList<Vector2> hills, List<TrackFeature> features,
                         float width = 13f, float shoulder = 3.5f)
        {
            Id = id;
            Theme = theme;
            Width = width;
            Shoulder = shoulder;
            Control = new List<Vector2>(control).ToArray();
            Features = features ?? new List<TrackFeature>();
            Features.Sort((a, b) => a.S.CompareTo(b.S));

            var dense = CatmullRom(Control, 48);
            // arc length along the dense polyline, then even samples about a metre apart
            var acc = new float[dense.Count + 1];
            for (int i = 0; i < dense.Count; i++) acc[i + 1] = acc[i] + Vector2.Distance(dense[i], dense[(i + 1) % dense.Count]);
            float total = acc[dense.Count];
            int n = Mathf.Max(16, Mathf.RoundToInt(total));
            _step = total / n;
            Length = total;
            _p = new Vector2[n];
            int j = 0;
            for (int i = 0; i < n; i++)
            {
                float s = i * _step;
                while (j < dense.Count - 1 && acc[j + 1] < s) j++;
                float f = (s - acc[j]) / Mathf.Max(1e-6f, acc[j + 1] - acc[j]);
                _p[i] = Vector2.Lerp(dense[j], dense[(j + 1) % dense.Count], f);
            }
            _heading = new float[n];
            for (int i = 0; i < n; i++)
            {
                var d = _p[(i + 1) % n] - _p[(i - 1 + n) % n];
                _heading[i] = Mathf.Atan2(d.y, d.x);
            }
            _curvature = new float[n];
            for (int i = 0; i < n; i++)
            {
                // over a few metres: a smoother curvature (what a driver feels)
                float dh = DeltaAngle(_heading[(i - 3 + n) % n], _heading[(i + 3) % n]);
                _curvature[i] = dh / (6f * _step);
            }
            _height = new float[n];
            if (hills != null && hills.Count > 0)
            {
                var keys = new List<Vector2>(hills);
                keys.Sort((a, b) => a.x.CompareTo(b.x));
                for (int i = 0; i < n; i++) _height[i] = Hill(keys, (float)i / n);
            }
        }

        /// <summary>A smooth closed loop of heights through the keys (fraction of the length, m): smoothstep between keys,
        /// round the end back to the first.</summary>
        static float Hill(List<Vector2> keys, float f)
        {
            int k = keys.Count;
            if (k == 1) return keys[0].y;
            for (int i = 0; i < k; i++)
            {
                var a = keys[i];
                var b = keys[(i + 1) % k];
                float fa = a.x, fb = i + 1 < k ? b.x : b.x + 1f;
                float ff = f < fa ? f + 1f : f;   // before this key: the stretch from the last key round to the first
                if (ff >= fa && ff <= fb) return Mathf.Lerp(a.y, b.y, Smooth(Mathf.InverseLerp(fa, fb, ff)));
            }
            return keys[0].y;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % (2f * Mathf.PI);
            if (d > Mathf.PI) d -= 2f * Mathf.PI;
            if (d < -Mathf.PI) d += 2f * Mathf.PI;
            return d;
        }

        /// <summary>Along the track, wrapped into [0, <see cref="Length"/>).</summary>
        public float Wrap(float s)
        {
            s %= Length;
            return s < 0f ? s + Length : s;
        }

        /// <summary>The signed distance from <paramref name="from"/> to <paramref name="to"/> along the track, the short way
        /// round (m).</summary>
        public float Ahead(float from, float to)
        {
            float d = Wrap(to) - Wrap(from);
            if (d > Length * 0.5f) d -= Length;
            if (d < -Length * 0.5f) d += Length;
            return d;
        }

        void Index(float s, out int i0, out int i1, out float f)
        {
            float u = Wrap(s) / _step;
            i0 = (int)u;
            f = u - i0;
            i0 %= _p.Length;
            i1 = (i0 + 1) % _p.Length;
        }

        /// <summary>The centreline at <paramref name="s"/> (the plan, m).</summary>
        public Vector2 Point(float s)
        {
            Index(s, out int a, out int b, out float f);
            return Vector2.Lerp(_p[a], _p[b], f);
        }

        /// <summary>The road's direction at <paramref name="s"/> (radians, the plan's x toward its second axis).</summary>
        public float Heading(float s)
        {
            Index(s, out int a, out int b, out float f);
            return _heading[a] + DeltaAngle(_heading[a], _heading[b]) * f;
        }

        public Vector2 Tangent(float s)
        {
            float h = Heading(s);
            return new Vector2(Mathf.Cos(h), Mathf.Sin(h));
        }

        /// <summary>Unit vector to the road's left at <paramref name="s"/>.</summary>
        public Vector2 Left(float s)
        {
            float h = Heading(s);
            return new Vector2(-Mathf.Sin(h), Mathf.Cos(h));
        }

        /// <summary>1 / radius (m), + = the road turns left.</summary>
        public float Curvature(float s)
        {
            Index(s, out int a, out int b, out float f);
            return Mathf.Lerp(_curvature[a], _curvature[b], f);
        }

        /// <summary>The road's height (m) at <paramref name="s"/>.</summary>
        public float Height(float s)
        {
            Index(s, out int a, out int b, out float f);
            return Mathf.Lerp(_height[a], _height[b], f);
        }

        /// <summary>How steep the road is at <paramref name="s"/> (rise per metre).</summary>
        public float Slope(float s) => (Height(s + 1f) - Height(s - 1f)) * 0.5f;

        /// <summary>The world point (x, y up, z) at <paramref name="s"/>, <paramref name="d"/> across, <paramref name="up"/>
        /// above the road.</summary>
        public Vector3 World(float s, float d, float up = 0f)
        {
            var p = Point(s) + Left(s) * d;
            return new Vector3(p.x, Height(s) + up, p.y);
        }

        /// <summary>The samples (about a metre apart): for drawing the road.</summary>
        public int Samples => _p.Length;
        public float SampleStep => _step;

        /// <summary>The tightest radius anywhere (m).</summary>
        public float TightestRadius
        {
            get
            {
                float k = 0f;
                foreach (float c in _curvature) k = Mathf.Max(k, Mathf.Abs(c));
                return 1f / Mathf.Max(1e-6f, k);
            }
        }

        /// <summary>The largest curvature (1/m, either way) in the next <paramref name="ahead"/> metres after
        /// <paramref name="s"/>, with its sign.</summary>
        public float CurvatureAhead(float s, float from, float to)
        {
            float best = 0f;
            for (float x = from; x <= to; x += 2f)
            {
                float c = Curvature(s + x);
                if (Mathf.Abs(c) > Mathf.Abs(best)) best = c;
            }
            return best;
        }

        /// <summary>The closest the centreline comes to itself between points more than <paramref name="apart"/> m apart
        /// along the track (m): the road must never touch another part of itself.</summary>
        public float ClosestApproach(float apart = 80f)
        {
            float best = float.MaxValue;
            int n = _p.Length, skip = Mathf.CeilToInt(apart / _step);
            for (int i = 0; i < n; i += 2)
                for (int j = i + skip; j < n; j += 2)
                {
                    if (n - (j - i) < skip) continue;
                    best = Mathf.Min(best, Vector2.Distance(_p[i], _p[j]));
                }
            return best;
        }

        // ------------------------------------------------------------------------------------------------ the spline

        /// <summary>A closed centripetal Catmull-Rom spline through <paramref name="pts"/>, <paramref name="per"/> points
        /// per span (no cusps or loops between close points).</summary>
        public static List<Vector2> CatmullRom(IList<Vector2> pts, int per)
        {
            int n = pts.Count;
            var out_ = new List<Vector2>(n * per);
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
                float t0 = 0f;
                float t1 = t0 + Mathf.Sqrt(Mathf.Max(1e-6f, Vector2.Distance(p0, p1)));
                float t2 = t1 + Mathf.Sqrt(Mathf.Max(1e-6f, Vector2.Distance(p1, p2)));
                float t3 = t2 + Mathf.Sqrt(Mathf.Max(1e-6f, Vector2.Distance(p2, p3)));
                for (int k = 0; k < per; k++)
                {
                    float t = t1 + (t2 - t1) * k / per;
                    var a1 = Lerp(p0, p1, t0, t1, t);
                    var a2 = Lerp(p1, p2, t1, t2, t);
                    var a3 = Lerp(p2, p3, t2, t3, t);
                    var b1 = Lerp(a1, a2, t0, t2, t);
                    var b2 = Lerp(a2, a3, t1, t3, t);
                    out_.Add(Lerp(b1, b2, t1, t2, t));
                }
            }
            return out_;
        }

        static Vector2 Lerp(Vector2 a, Vector2 b, float ta, float tb, float t) => a * ((tb - t) / (tb - ta)) + b * ((t - ta) / (tb - ta));
    }
}
