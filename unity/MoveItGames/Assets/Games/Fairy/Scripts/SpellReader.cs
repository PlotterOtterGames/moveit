using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>What a drawn stroke looked like to <see cref="SpellReader"/>.</summary>
    public struct SpellRead
    {
        /// <summary>Good enough to cast.</summary>
        public bool Found;
        /// <summary>The nearest spell's shape (also when not found).</summary>
        public Spell Spell;
        /// <summary>How near (1 = a perfect match, 0 = at the acceptance limit or worse).</summary>
        public float Score;
        /// <summary>The angular distance to the nearest template (radians, after the checks).</summary>
        public float Distance;
        /// <summary>The next nearest spell and its distance.</summary>
        public Spell Runner;
        public float RunnerDistance;
        /// <summary>The stroke's size: its bounding box's diagonal (HUD pixels), and the box's width and height.</summary>
        public float Size, Width, Height;
        /// <summary>Its middle on the screen (HUD pixels, y down): where its magic goes for the lightning and the rainbow.</summary>
        public Vector2 Centre;
        /// <summary>The part of the stroke read (indices into the points given: the way to its start and the arm dropping
        /// after it left out).</summary>
        public int From, To;
        /// <summary>Its total turning (degrees, + = counter-clockwise on the screen), its corners, how near the end came back
        /// to the start (a share of the size), the sharpest turn against its own way round (a heart's dip, a star's inner
        /// corners), the sharpest its own way at the bottom in the middle (a heart's point), the sharpest against it in the
        /// top half (a heart's dip), how much its turning swings both ways (a wave), how far it narrows from one end to the
        /// other round its middle (a spiral: 1 = not at all).</summary>
        public float Turning, Closure, Reverse, BottomTurn, TopReverse, Swing, Narrowing;
        /// <summary>Sharp corners, and how many times one turns the other way from the one before (a zigzag's do, a star's
        /// five lines' don't).</summary>
        public int Corners, ZigZags;
        /// <summary>How many times it swings out to a side of the line between its ends and back (by at least 12 % of that
        /// line's length): a wave's swells.</summary>
        public int Waves;
        /// <summary>An arch ∩: how far both ends are below its highest point (a share of its height), and where along the
        /// stroke that point is (0..1).</summary>
        public float ArchDepth, ArchTop;

        public override string ToString() =>
            $"{(Found ? "" : "(no) ")}{Spell} {Score:0.00} d {Distance:0.00} vs {Runner} {RunnerDistance:0.00}, size {Size:0} ({Width:0} x {Height:0}), " +
            $"turn {Turning:0}, swing {Swing:0}, corners {Corners}, closure {Closure:0.00}, reverse {Reverse:0}, bottom {BottomTurn:0}, " +
            $"top back {TopReverse:0}, narrowing {Narrowing:0.00}, arch {ArchDepth:0.00} at {ArchTop:0.00}, part {From}..{To}";
    }

    /// <summary>
    /// Reads a stroke drawn in the air with the wand as one of the spells' shapes (2026-11-17, the user: "I want the player
    /// to be able to swing the hand in the form of the magic, and then the game guesses which spell it was"). The shapes
    /// are the ones the game used to show to follow (<see cref="SpellShapes"/>): the lightning a zigzag bolt (a Z, an N,
    /// three or four strokes down), the rainbow an arch ∩, the shield a circle, the seeds a wave across, the big magic a
    /// star (five lines, or its outline), the freeze a spiral, the healing rain a heart; each from any start and either way
    /// round.
    /// <para>How: the stroke resampled to <see cref="Points"/> points along its path, smoothed a little, its middle moved
    /// to the origin and its size scaled away (both ways alike: a wave stays wide, a bolt tall), then compared with every
    /// template by Protractor (Li 2010: the cosine between the two as vectors at the best rotation, here at most
    /// <see cref="MaxRotation"/> each way, so a bolt can lean but never lies down as a wave). Then plain checks on the stroke
    /// itself push away the shapes it clearly isn't (<see cref="Check"/>): a bolt has two corners and stands up, a wave is
    /// wide and swings, a circle closes without corners or a point, a spiral turns on and narrows, a heart has its point at
    /// the bottom or its dip at the top, a star has corners all round. The templates are the RuneRecognizer's of the
    /// gesture version (2026-11-15) for the circle, the heart and the bolts, and new ones for the rest.</para>
    /// <para><see cref="ReadBest"/> also tries the stroke without its first or last part (from a corner in its first or
    /// last 45 %): a child's hand goes up from the hip to where the shape starts and drops after it without a stop, and
    /// those lines are no part of the shape. Pure logic: tests feed it strokes.</para>
    /// </summary>
    public sealed class SpellReader
    {
        public const int Points = 48;
        public const int SpellCount = 7;
        /// <summary>A stroke may be turned this far (radians) each way to fit a template.</summary>
        public float MaxRotation = 32f * Mathf.Deg2Rad;
        /// <summary>Found when the nearest template is nearer than this (radians: cos 0.55 = 0.85).</summary>
        public float Accept = 0.55f;
        /// <summary>... and nearer by this than the next spell's (a stroke between two shapes is neither).</summary>
        public float Margin = 0.03f;
        /// <summary>Smaller strokes are no shape at all (HUD pixels: the bounding box's diagonal; the screen is 1080 high).</summary>
        public float MinSize = 230f;
        /// <summary>How much a spell whose checks fail is moved away (radians): out of reach.</summary>
        public float CheckPush = 0.45f;
        /// <summary>Points of a stroke closer than this to the last one kept are left out (HUD pixels).</summary>
        public float MinStep = 10f;
        /// <summary>Leaving out part of the stroke costs this much (radians, times the share left out): the whole stroke is
        /// read when it fits.</summary>
        public float CutCost = 0.35f;
        /// <summary>A part of a stroke (its ends left out) must fit at least this well (radians).</summary>
        public float AcceptPart = 0.42f;

        sealed class Template
        {
            public Spell Spell;
            public string Name;
            public float[] V;
        }

        static List<Template> _templates;
        static List<Template> Templates => _templates ??= Build();
        public static int TemplateCount => Templates.Count;

        readonly Vector2[] _resampled = new Vector2[Points];
        readonly float[] _vector = new float[Points * 2];
        readonly float[] _best = new float[SpellCount];
        readonly List<Vector2> _part = new List<Vector2>();
        readonly Vector2[] _coarse = new Vector2[64];

        public static int Bit(Spell s) => 1 << (int)s;
        public const int All = (1 << SpellCount) - 1;

        // ------------------------------------------------------------------------------------------------ read

        /// <summary>
        /// The spell <paramref name="stroke"/> (HUD pixels, y down) is, among those in <paramref name="allowed"/> (a mask of
        /// <see cref="Bit"/>), the stroke read as it is and without a run-up or a drop at its ends; the best of those.
        /// </summary>
        public SpellRead ReadBest(IReadOnlyList<Vector2> stroke, int allowed = All)
        {
            var whole = Read(stroke, 0, stroke?.Count ?? 0, allowed);
            if (stroke == null || stroke.Count < 6) return whole;
            float length = PathLength(stroke, 0, stroke.Count);
            if (length < 1f) return whole;
            // the corners of the stroke, on 64 points along it: where a run-up or a drop may end or begin
            var starts = new List<int> { 0 };
            var ends = new List<int> { stroke.Count };
            if (Resample(stroke, 0, stroke.Count, _coarse))
            {
                const int span = 3;
                for (int i = span; i < _coarse.Length - span; i++)
                {
                    var a = _coarse[i] - _coarse[i - span];
                    var b = _coarse[i + span] - _coarse[i];
                    if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) continue;
                    float angle = Mathf.Abs(Vector2.SignedAngle(a, b));
                    if (angle < 50f) continue;
                    // a local peak of turning only
                    var a0 = _coarse[i - 1] - _coarse[Mathf.Max(0, i - 1 - span)];
                    var b0 = _coarse[Mathf.Min(_coarse.Length - 1, i - 1 + span)] - _coarse[i - 1];
                    var a1 = _coarse[i + 1] - _coarse[i + 1 - span];
                    var b1 = _coarse[Mathf.Min(_coarse.Length - 1, i + 1 + span)] - _coarse[i + 1];
                    if (Mathf.Abs(Vector2.SignedAngle(a0, b0)) > angle || Mathf.Abs(Vector2.SignedAngle(a1, b1)) > angle) continue;
                    float share = i / (float)(_coarse.Length - 1);
                    int index = IndexAt(stroke, share * length);
                    if (share <= 0.45f && starts.Count < 4) starts.Add(index);
                    else if (share >= 0.55f && ends.Count < 4) ends.Add(index + 1);
                }
            }
            // the straight run at each end (the way up from the hip, the arm dropping): where the shape may begin and end
            int run = StraightRun(stroke, 1);
            if (run > 0 && !starts.Contains(run)) starts.Add(run);
            int tail = StraightRun(stroke, -1);
            if (tail > 0 && !ends.Contains(tail + 1)) ends.Add(tail + 1);
            var best = whole;
            float bestCost = whole.Found ? whole.Distance : float.MaxValue;
            foreach (int s in starts)
            foreach (int e in ends)
            {
                if (s == 0 && e == stroke.Count) continue;
                if (e - s < 6) continue;
                float kept = PathLength(stroke, s, e) / length;
                if (kept < 0.4f || IsLine(stroke, s, e)) continue;
                var r = Read(stroke, s, e, allowed);
                if (!r.Found || r.Distance > AcceptPart) continue;
                float cost = r.Distance + CutCost * (1f - kept);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = r;
                }
            }
            return best;
        }

        /// <summary>The spell points <paramref name="from"/> .. <paramref name="to"/> (exclusive) of
        /// <paramref name="stroke"/> are (HUD pixels, y down).</summary>
        public SpellRead Read(IReadOnlyList<Vector2> stroke, int from, int to, int allowed = All)
        {
            var m = new SpellRead { Spell = Spell.Shield, Runner = Spell.Shield, Distance = 9f, RunnerDistance = 9f, From = from, To = to };
            if (stroke == null || to - from < 3 || allowed == 0) return m;
            // y up from here on (the templates are drawn that way), points closer than MinStep to the last one left out (a
            // still hand's jitter at a stop would make loops of a few pixels: corners and turning that aren't there)
            // Each point is first the median of the seven round it (one coordinate at a time): the box's glitches, a
            // hand thrown 10-20 cm for a sample or two, make spikes that would read as corners.
            _part.Clear();
            for (int i = from; i < to; i++)
            {
                var q = Median7(stroke, i, from, to);
                q.y = -q.y;
                if (_part.Count == 0 || (q - _part[_part.Count - 1]).sqrMagnitude >= MinStep * MinStep || i == to - 1) _part.Add(q);
            }
            if (!Resample(_part, 0, _part.Count, _resampled)) return m;
            Smooth(_resampled);
            Measure(_resampled, ref m);
            if (m.Size < MinSize) return m;
            Vectorize(_resampled, _vector);
            for (int i = 0; i < SpellCount; i++) _best[i] = 9f;
            foreach (var t in Templates)
            {
                if ((allowed & Bit(t.Spell)) == 0) continue;
                float d = Distance(_vector, t.V);
                if (d < _best[(int)t.Spell]) _best[(int)t.Spell] = d;
            }
            for (int i = 0; i < SpellCount; i++)
                if (_best[i] < 9f && !Check((Spell)i, m)) _best[i] += CheckPush;
            int first = -1, second = -1;
            for (int i = 0; i < SpellCount; i++)
            {
                if (_best[i] >= 9f) continue;
                if (first < 0 || _best[i] < _best[first])
                {
                    second = first;
                    first = i;
                }
                else if (second < 0 || _best[i] < _best[second]) second = i;
            }
            if (first < 0) return m;
            m.Spell = (Spell)first;
            m.Distance = _best[first];
            if (second >= 0)
            {
                m.Runner = (Spell)second;
                m.RunnerDistance = _best[second];
            }
            m.Score = Mathf.Clamp01(1f - m.Distance / Accept);
            m.Found = m.Distance < Accept && (second < 0 || m.RunnerDistance - m.Distance >= Margin);
            return m;
        }

        /// <summary>The plain checks on the stroke itself, a spell at a time (angles in degrees, shares of its size).</summary>
        public static bool Check(Spell s, in SpellRead m)
        {
            float turn = Mathf.Abs(m.Turning);
            float aspect = m.Height > 1e-3f ? m.Width / m.Height : 99f;
            switch (s)
            {
                case Spell.Lightning:
                    // two or more corners, standing up (no flatter than a square and a half), not round
                    return m.Corners >= 2 && m.ZigZags >= 1 && aspect < 1.5f && turn < 260f && m.Closure > 0.3f;
                case Spell.Rainbow:
                    // an arch: turning half round one way, open, its ends low (checked in Measure: the top in the middle)
                    return turn > 95f && turn < 290f && m.Corners <= 1 && m.Closure > 0.45f && aspect > 0.75f && aspect < 5.5f && m.Waves <= 1 &&
                           m.ArchDepth > 0.45f && m.ArchTop > 0.2f && m.ArchTop < 0.8f;
                case Spell.Seeds:
                    // a wave across: wide, open, swinging both ways
                    return aspect > 1.45f && m.Closure > 0.5f && turn < 220f && m.Waves >= 2;
                case Spell.Shield:
                    // round all the way: never bending back, no point at its bottom, not turning on into a spiral
                    return turn > 250f && turn < 520f && m.Corners <= 1 && (m.Closure < 0.5f || turn > 320f) && m.Reverse < 40f &&
                           m.BottomTurn < 54f && !(turn > 440f && m.Narrowing < 0.55f);
                case Spell.Freeze:
                    // round and on: more than a turn and a quarter, narrowing toward one end
                    return turn > 440f && m.Reverse < 45f && (m.Narrowing < 0.6f || turn > 600f);
                case Spell.Heal:
                    // round like a circle, but with its point at the bottom in the middle or its dip bending back at the top
                    return turn > 220f && turn < 640f && m.Closure < 0.6f && (m.BottomTurn > 46f || m.TopReverse > 24f);
                case Spell.Burst:
                    // corners all round
                    return m.Corners >= 4 && m.Closure < 0.6f;
            }
            return true;
        }

        /// <summary>The angular distance (radians) between a stroke and a template (both unit vectors), at the best
        /// rotation within <see cref="MaxRotation"/>.</summary>
        float Distance(float[] v, float[] t)
        {
            float a = 0f, b = 0f;
            for (int i = 0; i < v.Length; i += 2)
            {
                a += v[i] * t[i] + v[i + 1] * t[i + 1];
                b += v[i] * t[i + 1] - v[i + 1] * t[i];
            }
            float angle = Mathf.Clamp(Mathf.Atan2(b, a), -MaxRotation, MaxRotation);
            float s = a * Mathf.Cos(angle) + b * Mathf.Sin(angle);
            return Mathf.Acos(Mathf.Clamp(s, -1f, 1f));
        }

        // ------------------------------------------------------------------------------------------------ the stroke

        readonly float[] _mx = new float[7], _my = new float[7];

        Vector2 Median7(IReadOnlyList<Vector2> p, int i, int from, int to)
        {
            int a = Mathf.Max(from, i - 3), b = Mathf.Min(to - 1, i + 3), n = 0;
            for (int j = a; j <= b; j++)
            {
                _mx[n] = p[j].x;
                _my[n] = p[j].y;
                n++;
            }
            return new Vector2(MedianOf(_mx, n), MedianOf(_my, n));
        }

        static float MedianOf(float[] v, int n)
        {
            for (int i = 1; i < n; i++)
            {
                float x = v[i];
                int j = i - 1;
                while (j >= 0 && v[j] > x)
                {
                    v[j + 1] = v[j];
                    j--;
                }
                v[j + 1] = x;
            }
            return n % 2 == 1 ? v[n / 2] : 0.5f * (v[n / 2 - 1] + v[n / 2]);
        }

        /// <summary>Where the straight line at the start (<paramref name="dir"/> 1) or the end (-1) of the stroke ends: the
        /// index of its last point, or -1 when there is none at least <see cref="MinRun"/> pixels long (all points within
        /// 7 % of its length, at least 14 px, of the line between its ends).</summary>
        public int StraightRun(IReadOnlyList<Vector2> p, int dir)
        {
            int n = p.Count;
            int first = dir > 0 ? 0 : n - 1;
            int best = -1;
            for (int k = 2; k < n - 2; k++)
            {
                int end = first + dir * k;
                var a = p[first];
                var b = p[end];
                float len = Vector2.Distance(a, b);
                if (len < 1f) continue;
                float limit = Mathf.Max(24f, 0.1f * len);
                var d = (b - a) / len;
                bool straight = true;
                for (int j = 1; j < k && straight; j++)
                {
                    var q = p[first + dir * j] - a;
                    if (Mathf.Abs(q.x * d.y - q.y * d.x) > limit) straight = false;
                }
                if (!straight) break;
                if (len >= MinRun) best = end;
            }
            return best;
        }

        /// <summary>A line: every point near the line between its ends (and not swinging to and fro like a wave), or as
        /// thin as one and coming back (there and back). Points <paramref name="from"/> .. <paramref name="to"/>
        /// (exclusive).</summary>
        public static bool IsLine(IReadOnlyList<Vector2> stroke, int from, int to)
        {
            if (to - from < 3) return true;
            var a = stroke[from];
            var b = stroke[to - 1];
            float chord = Vector2.Distance(a, b);
            Vector2 min = a, max = a;
            for (int i = from; i < to; i++)
            {
                min = Vector2.Min(min, stroke[i]);
                max = Vector2.Max(max, stroke[i]);
            }
            var box = max - min;
            float extent = Mathf.Max(box.x, box.y);
            if (chord > 1f)
            {
                // every point near the line between its ends (a hand's wobble), and not swinging to and fro
                var d = (b - a) / chord;
                float far = 0f;
                for (int i = from; i < to; i++)
                {
                    var q = stroke[i] - a;
                    far = Mathf.Max(far, Mathf.Abs(q.x * d.y - q.y * d.x));
                }
                if (far < 0.15f * chord && Waves(stroke, from, to) < 2) return true;
            }
            return Mathf.Min(box.x, box.y) < 0.15f * extent && chord < 0.5f * extent;
        }

        /// <summary>A straight run at an end of a stroke at least this long (HUD pixels) is a way up or down.</summary>
        public float MinRun = 150f;

        static float PathLength(IReadOnlyList<Vector2> p, int from, int to)
        {
            float l = 0f;
            for (int i = from + 1; i < to; i++) l += Vector2.Distance(p[i - 1], p[i]);
            return l;
        }

        /// <summary>The index of the point <paramref name="along"/> of the path's length along it.</summary>
        static int IndexAt(IReadOnlyList<Vector2> p, float along)
        {
            float l = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                l += Vector2.Distance(p[i - 1], p[i]);
                if (l >= along) return i;
            }
            return p.Count - 1;
        }

        /// <summary>Points evenly along the path (false for a stroke with no length).</summary>
        public static bool Resample(IReadOnlyList<Vector2> stroke, int from, int to, Vector2[] into)
        {
            int n = into.Length;
            float length = PathLength(stroke, from, to);
            if (length < 1e-5f) return false;
            float step = length / (n - 1);
            into[0] = stroke[from];
            int k = 1;
            float carried = 0f;
            var prev = stroke[from];
            for (int i = from + 1; i < to && k < n; i++)
            {
                var next = stroke[i];
                float seg = Vector2.Distance(prev, next);
                while (seg > 1e-9f && carried + seg >= step && k < n)
                {
                    float f = (step - carried) / seg;
                    var p = prev + (next - prev) * f;
                    into[k++] = p;
                    prev = p;
                    seg = Vector2.Distance(prev, next);
                    carried = 0f;
                }
                carried += seg;
                prev = next;
            }
            while (k < n) into[k++] = stroke[to - 1];
            return true;
        }

        /// <summary>One pass of a gentle [¼ ½ ¼] smoothing, the ends kept: the camera's jitter off the resampled stroke.</summary>
        static void Smooth(Vector2[] p)
        {
            var prev = p[0];
            for (int i = 1; i < p.Length - 1; i++)
            {
                var here = p[i];
                p[i] = prev * 0.25f + here * 0.5f + p[i + 1] * 0.25f;
                prev = here;
            }
        }

        /// <summary>Its size, middle, turning, corners, closure, swing and narrowing, on the resampled points (y up).</summary>
        static void Measure(Vector2[] p, ref SpellRead m)
        {
            int n = p.Length;
            Vector2 min = p[0], max = p[0], sum = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                min = Vector2.Min(min, p[i]);
                max = Vector2.Max(max, p[i]);
                sum += p[i];
            }
            m.Size = Vector2.Distance(min, max);
            m.Width = max.x - min.x;
            m.Height = max.y - min.y;
            var centre = sum / n;
            m.Centre = new Vector2(centre.x, -centre.y);
            m.Closure = m.Size > 1e-5f ? Vector2.Distance(p[0], p[n - 1]) / m.Size : 1f;
            // turning: the signed angle between successive steps
            float turning = 0f;
            for (int i = 2; i < n; i++)
            {
                var a = p[i - 1] - p[i - 2];
                var b = p[i] - p[i - 1];
                if (a.sqrMagnitude < 1e-10f || b.sqrMagnitude < 1e-10f) continue;
                turning += Vector2.SignedAngle(a, b);
            }
            m.Turning = turning;
            // how much it swings both ways (a wave turns one way, then back the other): the turning between chords of three
            // points (the camera's jitter averaged out), all of it less what's left at the end, halved
            float total = 0f, net = 0f;
            for (int i = 6; i < n; i += 3)
            {
                var a = p[i - 3] - p[i - 6];
                var b = p[i] - p[i - 3];
                if (a.sqrMagnitude < 1e-10f || b.sqrMagnitude < 1e-10f) continue;
                float d = Vector2.SignedAngle(a, b);
                total += Mathf.Abs(d);
                net += d;
            }
            m.Swing = (total - Mathf.Abs(net)) * 0.5f;
            m.Corners = Corners(p, out m.ZigZags);
            m.Waves = Waves(p, p.Length);
            float way = Mathf.Sign(turning);
            var box = max - min;
            Vector2 Place(Vector2 q) => new Vector2(box.x > 1e-5f ? (q.x - min.x) / box.x : 0.5f, box.y > 1e-5f ? (q.y - min.y) / box.y : 0.5f);
            m.Reverse = m.BottomTurn = m.TopReverse = 0f;
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Max(0, i - 3), b = Mathf.Min(n - 1, i + 3);
                if (i - a < 2 || b - i < 2) continue;
                var u = p[i] - p[a];
                var v = p[b] - p[i];
                if (u.sqrMagnitude < 1e-10f || v.sqrMagnitude < 1e-10f) continue;
                float signed = way * Vector2.SignedAngle(u, v);
                if (-signed > m.Reverse) m.Reverse = -signed;
                var q = Place(p[i]);
                if (q.y < 0.25f && Mathf.Abs(q.x - 0.5f) < 0.25f) m.BottomTurn = Mathf.Max(m.BottomTurn, signed);
                if (q.y > 0.5f && Mathf.Abs(q.x - 0.5f) < 0.3f) m.TopReverse = Mathf.Max(m.TopReverse, -signed);
            }
            // a spiral narrows round its middle: the distance from the middle over its first fifth against its last fifth
            int fifth = n / 5;
            float r0 = 0f, r1 = 0f;
            for (int i = 0; i < fifth; i++)
            {
                r0 += Vector2.Distance(p[i], centre);
                r1 += Vector2.Distance(p[n - 1 - i], centre);
            }
            m.Narrowing = Mathf.Max(r0, r1) > 1e-5f ? Mathf.Min(r0, r1) / Mathf.Max(r0, r1) : 1f;
            // an arch ∩ has its top in its middle and both ends well below it (a "U" or a hook isn't one)
            int top = 0;
            for (int i = 1; i < n; i++)
                if (p[i].y > p[top].y) top = i;
            m.ArchDepth = m.Height > 1e-5f ? Mathf.Min(p[top].y - p[0].y, p[top].y - p[n - 1].y) / m.Height : 0f;
            m.ArchTop = top / (float)(n - 1);
        }

        /// <summary>How many times the points swing out to a side of the line between the first and the last and back: the
        /// peaks and valleys of their distance from it, each counted once the points come back from it by 12 % of the line's
        /// length (an arch has one, a wave of one swell two, of a swell and a half three).</summary>
        public static int Waves(IReadOnlyList<Vector2> p, int n) => Waves(p, 0, n);

        public static int Waves(IReadOnlyList<Vector2> p, int from, int to)
        {
            if (to - from < 3) return 0;
            var a = p[from];
            var line = p[to - 1] - a;
            float len = line.magnitude;
            if (len < 1e-3f) return 0;
            var d = line / len;
            float step = 0.12f * len;
            int count = 0, dir = 0;
            float extreme = 0f;
            for (int i = from + 1; i < to; i++)
            {
                var q = p[i] - a;
                float off = q.x * d.y - q.y * d.x;
                if (dir == 0)
                {
                    if (Mathf.Abs(off) >= step * 0.5f)
                    {
                        dir = off > 0f ? 1 : -1;
                        extreme = off;
                    }
                }
                else if (dir > 0 ? off > extreme : off < extreme) extreme = off;
                else if (Mathf.Abs(off - extreme) >= step)
                {
                    // came back from a peak (or a valley): it counts, now looking for the other
                    count++;
                    dir = -dir;
                    extreme = off;
                }
            }
            return count;
        }

        /// <summary>Sharp corners: where the way in (over three points) and the way out differ by more than 62 degrees, a
        /// local peak of that, kept apart from the next one and away from the very ends (a hook as the hand stops).</summary>
        public static int Corners(Vector2[] p, out int zigzags)
        {
            int n = p.Length;
            const int span = 3;
            var angle = new float[n];
            var sign = new float[n];
            for (int i = span; i < n - span; i++)
            {
                var a = p[i] - p[i - span];
                var b = p[i + span] - p[i];
                if (a.sqrMagnitude < 1e-10f || b.sqrMagnitude < 1e-10f) continue;
                float signed = Vector2.SignedAngle(a, b);
                angle[i] = Mathf.Abs(signed);
                sign[i] = Mathf.Sign(signed);
            }
            int count = 0, last = -10;
            float lastSign = 0f;
            zigzags = 0;
            // not in the first or last twelfth: a hook as the hand starts or stops is no corner
            int edge = Mathf.Max(span + 1, n / 12);
            for (int i = edge; i < n - edge; i++)
            {
                if (angle[i] < 62f || angle[i] < angle[i - 1] || angle[i] < angle[i + 1]) continue;
                if (i - last < 4) continue;
                count++;
                last = i;
                if (lastSign != 0f && sign[i] != lastSign) zigzags++;
                lastSign = sign[i];
            }
            return count;
        }

        /// <summary>The points as one vector of unit length, round their middle.</summary>
        static void Vectorize(Vector2[] p, float[] v)
        {
            var c = Vector2.zero;
            for (int i = 0; i < p.Length; i++) c += p[i];
            c /= p.Length;
            float sum = 0f;
            for (int i = 0; i < p.Length; i++)
            {
                float x = p[i].x - c.x, y = p[i].y - c.y;
                v[i * 2] = x;
                v[i * 2 + 1] = y;
                sum += x * x + y * y;
            }
            float k = sum > 1e-12f ? 1f / Mathf.Sqrt(sum) : 0f;
            for (int i = 0; i < v.Length; i++) v[i] *= k;
        }

        // ------------------------------------------------------------------------------------------------ the templates

        static List<Template> Build()
        {
            var list = new List<Template>();
            void Add(Spell s, string name, List<Vector2> path)
            {
                var pts = new Vector2[Points];
                if (!Resample(path, 0, path.Count, pts)) return;
                var v = new float[Points * 2];
                Vectorize(pts, v);
                list.Add(new Template { Spell = s, Name = name, V = v });
                // and drawn the other way
                path.Reverse();
                if (!Resample(path, 0, path.Count, pts)) return;
                var w = new float[Points * 2];
                Vectorize(pts, w);
                list.Add(new Template { Spell = s, Name = name + " back", V = w });
            }
            // ⚡ a Z and its mirror (three strokes across), an N and its mirror (three strokes up and down), the game's bolt
            // (down, back, down), and zigzags of three and four strokes down
            Add(Spell.Lightning, "z", Rounded(new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f)));
            Add(Spell.Lightning, "s", Rounded(new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f)));
            Add(Spell.Lightning, "n", Rounded(new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(1f, 1f)));
            Add(Spell.Lightning, "n mirrored", Rounded(new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, -1f), new Vector2(-1f, 1f)));
            foreach (float m in new[] { 1f, -1f })
            {
                Add(Spell.Lightning, $"bolt {m}", Rounded(new Vector2(-0.5f * m, 1.2f), new Vector2(0.45f * m, 0.05f), new Vector2(-0.45f * m, 0.05f), new Vector2(0.5f * m, -1.2f)));
                Add(Spell.Lightning, $"game bolt {m}", Rounded(new Vector2(-0.5f * m, 1.05f), new Vector2(0.5f * m, 0.05f), new Vector2(-0.8f * m, 0.05f), new Vector2(0.4f * m, -1.1f)));
                Add(Spell.Lightning, $"zigzag 3 {m}", Rounded(new Vector2(-0.9f * m, 1.2f), new Vector2(0.9f * m, 0.4f), new Vector2(-0.9f * m, -0.4f), new Vector2(0.9f * m, -1.2f)));
                Add(Spell.Lightning, $"zigzag 4 {m}", Rounded(new Vector2(-0.7f * m, 1.3f), new Vector2(0.7f * m, 0.65f), new Vector2(-0.7f * m, 0f), new Vector2(0.7f * m, -0.65f), new Vector2(-0.7f * m, -1.3f)));
            }
            // ∩ the rainbow: half a circle from either side (each Add adds the other way), shallower and deeper, wide, and a
            // pointed arch (∧ with a round top)
            foreach (var (from, to, sx) in new[] { (180f, 0f, 1f), (155f, 25f, 1f), (200f, -20f, 1f), (180f, 0f, 1.5f), (170f, 10f, 0.75f) })
                Add(Spell.Rainbow, $"arch {from}-{to} x{sx}", Scale(Arc(Vector2.zero, 1f, from, to, 32), sx, 1f));
            foreach (float w in new[] { 0.6f, 0.95f })
                Add(Spell.Rainbow, $"peak {w}", Rounded(new Vector2(-w, 0f), new Vector2(-w * 0.35f, 0.85f), new Vector2(w * 0.35f, 0.85f), new Vector2(w, 0f)));
            // ○ the shield: every start round it, both ways, round, wide and tall, a little short of closing or a little past
            for (int s = 0; s < 8; s++)
            {
                float start = s * 45f;
                foreach (var (sx, sy, turns) in new[] { (1f, 1f, 1f), (1.35f, 1f, 1f), (1f, 1.35f, 1f), (1f, 1f, 0.86f), (1f, 1f, 1.15f) })
                    Add(Spell.Shield, $"circle {start} {sx}x{sy} {turns}", Ellipse(sx, sy, start, turns, 48));
            }
            // ∿ the seeds: a wave across of one, one and a half or two swells, starting up or down, low and high
            foreach (float swells in new[] { 1f, 1.5f, 2f })
            foreach (float amp in new[] { 0.14f, 0.24f, 0.4f })
            foreach (float sign in new[] { 1f, -1f })
            {
                var path = new List<Vector2>();
                for (int i = 0; i <= 64; i++)
                {
                    float t = i / 64f;
                    path.Add(new Vector2(-1f + 2f * t, sign * amp * Mathf.Sin(t * Mathf.PI * 2f * swells)));
                }
                Add(Spell.Seeds, $"wave {swells} {amp} {sign}", path);
            }
            // a sharp wave (a zigzag across, W and M): still the seeds when it lies down
            Add(Spell.Seeds, "w", Rounded(new Vector2(-1.5f, 0.45f), new Vector2(-0.75f, -0.45f), new Vector2(0f, 0.45f), new Vector2(0.75f, -0.45f), new Vector2(1.5f, 0.45f)));
            Add(Spell.Seeds, "m", Rounded(new Vector2(-1.5f, -0.45f), new Vector2(-0.75f, 0.45f), new Vector2(0f, -0.45f), new Vector2(0.75f, 0.45f), new Vector2(1.5f, -0.45f)));
            // ★ the big magic: five lines from any of its points, and its outline from any outer point
            for (int s = 0; s < 5; s++)
            {
                var lines = new List<Vector2>();
                for (int i = 0; i <= 5; i++)
                {
                    float a = (90f + (s + i * 2) * 72f) * Mathf.Deg2Rad;
                    lines.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
                }
                Add(Spell.Burst, $"star lines {s}", Rounded(lines.ToArray()));
                var outline = new List<Vector2>();
                for (int i = 0; i <= 10; i++)
                {
                    float a = (90f + s * 72f + i * 36f) * Mathf.Deg2Rad;
                    float r = i % 2 == 0 ? 1f : 0.42f;
                    outline.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
                Add(Spell.Burst, $"star outline {s}", Rounded(outline.ToArray()));
            }
            // @ the freeze: a spiral of one and a half or two turns in from any of four starts, either way round (each Add adds
            // the way out)
            foreach (float turns in new[] { 1.5f, 2f })
            for (int s = 0; s < 4; s++)
            foreach (float dir in new[] { 1f, -1f })
            {
                var path = new List<Vector2>();
                int steps = Mathf.RoundToInt(48 * turns);
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float a = (s * 90f) * Mathf.Deg2Rad + dir * t * turns * Mathf.PI * 2f;
                    float r = 1f - 0.8f * t;
                    path.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
                Add(Spell.Freeze, $"spiral {turns} {s} {dir}", path);
            }
            // ♥ the healing rain: from the dip at its top or from its point, both ways, round and slim
            foreach (float start in new[] { 0f, Mathf.PI })
            foreach (var (sx, sy) in new[] { (1f, 1f), (1.25f, 1f), (1f, 1.25f) })
                Add(Spell.Heal, $"heart {start:0.0} {sx}x{sy}", Heart(start, sx, sy, 64));
            return list;
        }

        static List<Vector2> Scale(List<Vector2> p, float sx, float sy)
        {
            for (int i = 0; i < p.Count; i++) p[i] = new Vector2(p[i].x * sx, p[i].y * sy);
            return p;
        }

        /// <summary>A polyline whose corners are cut a little (a hand never turns on the spot).</summary>
        public static List<Vector2> Rounded(params Vector2[] corners)
        {
            var path = new List<Vector2>();
            for (int i = 0; i < corners.Length - 1; i++)
            {
                var a = corners[i];
                var b = corners[i + 1];
                for (int k = 0; k < 10; k++) path.Add(Vector2.Lerp(a, b, k / 10f));
            }
            path.Add(corners[corners.Length - 1]);
            // one round of Chaikin's corner cutting, the ends kept
            var cut = new List<Vector2> { path[0] };
            for (int i = 0; i < path.Count - 1; i++)
            {
                cut.Add(Vector2.Lerp(path[i], path[i + 1], 0.25f));
                cut.Add(Vector2.Lerp(path[i], path[i + 1], 0.75f));
            }
            cut.Add(path[path.Count - 1]);
            return cut;
        }

        static List<Vector2> Arc(Vector2 centre, float radius, float fromDeg, float toDeg, int steps)
        {
            var path = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                path.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            return path;
        }

        static List<Vector2> Ellipse(float sx, float sy, float startDeg, float turns, int steps)
        {
            var path = new List<Vector2>();
            int n = Mathf.RoundToInt(steps * turns);
            for (int i = 0; i <= n; i++)
            {
                float a = startDeg * Mathf.Deg2Rad + i / (float)steps * Mathf.PI * 2f;
                path.Add(new Vector2(Mathf.Cos(a) * sx, Mathf.Sin(a) * sy));
            }
            return path;
        }

        /// <summary>The classic heart curve (x = 16 sin³t, y = 13 cos t − 5 cos 2t − 2 cos 3t − cos 4t) from
        /// <paramref name="start"/> (0 = the dip at its top, π = its point) once round.</summary>
        static List<Vector2> Heart(float start, float sx, float sy, int steps)
        {
            var path = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float t = start + i / (float)steps * Mathf.PI * 2f;
                float s = Mathf.Sin(t);
                float x = 16f * s * s * s;
                float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                path.Add(new Vector2(x / 16f * sx, y / 16f * sy));
            }
            return path;
        }
    }
}
