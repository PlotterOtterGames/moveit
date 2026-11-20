using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// A player's wand drawing spells in the air (2026-11-17): cuts the wand's path into strokes and reads each one
    /// (<see cref="SpellReader"/>).
    /// <list type="bullet">
    /// <item><b>Arriving</b>: a hand just raised is on its way up from the hip to where the shape will begin. Like
    /// Pikturo's brush (<c>PaintPen</c>), nothing counts until the wand has arrived: it slowed down
    /// (<see cref="RestSpeed"/> for <see cref="ArriveRest"/> s), or turned more than <see cref="ArriveTurn"/> degrees away
    /// from the way it came in (the shape begins there), or <see cref="ArriveMax"/> s passed.</item>
    /// <item><b>A stroke</b> starts when the wand moves off quickly (<see cref="StartSpeed"/>, measured as the distance
    /// covered in the last <see cref="Window"/> s, so a still hand's jitter is no speed) and ends when it rests (under
    /// <see cref="RestSpeed"/> for <see cref="RestTime"/> s: a short stop after the shape, "then the game guesses"), when the
    /// hand goes down (the quick drop at its end left out: <see cref="DropSpeed"/>), or after <see cref="MaxSeconds"/>.</item>
    /// <item>A twitch (shorter than <see cref="MinLength"/>) or a line (pointing, the way up or down) is nothing at all. A
    /// stroke that reads as nothing waits <see cref="JoinGap"/> s: one going on in that time joins it (a child who stopped
    /// in a corner of the bolt), else it fizzles.</item>
    /// </list>
    /// Positions in HUD pixels (1080 high, y down). Pure logic: tested with simulated wands.
    /// </summary>
    public sealed class SpellCaster
    {
        public float StartSpeed = 220f, RestSpeed = 115f, RestTime = 0.24f, Window = 0.15f, MaxSeconds = 7f, JoinGap = 0.45f, MinLength = 220f;
        public float ArriveRest = 0.12f, ArriveTurn = 65f, ArriveMax = 1.5f;
        /// <summary>A stroke ended by the hand going down loses its end while that heads down this many times quicker than
        /// the stroke's own pace (the arm dropping).</summary>
        public float DropSpeed = 1.5f;
        /// <summary>The wand is at the screen's bottom edge below this (HUD pixels: the hand on its way down).</summary>
        public float BottomEdge = 1060f;

        public enum Outcome { None, Read, Fizzled }

        /// <summary>The wand is drawing a stroke now.</summary>
        public bool Drawing { get; private set; }
        /// <summary>The hand is still on its way up (nothing counts yet).</summary>
        public bool Arriving { get; private set; }
        /// <summary>The stroke being drawn (and any joined before it): the trail's points.</summary>
        public readonly List<Vector2> Stroke = new List<Vector2>();
        /// <summary>Seconds since the stroke being drawn started.</summary>
        public float StrokeSeconds => Drawing ? (float)(_now - _strokeStart) : 0f;
        /// <summary>The last stroke read or fizzled (its points) and what it read as.</summary>
        public Vector2[] Last { get; private set; }
        public SpellRead LastRead { get; private set; }
        /// <summary>A stroke read as nothing is waiting for one to join it.</summary>
        public bool Waiting => _pending.Count > 0;
        /// <summary>The spells a stroke may be read as (<see cref="SpellReader.Bit"/>).</summary>
        public int Allowed = SpellReader.All;
        /// <summary>Tests: every stroke read is kept in <see cref="Log"/>.</summary>
        public bool KeepLog;
        public readonly List<Vector2[]> Log = new List<Vector2[]>(), Dropped = new List<Vector2[]>();

        readonly SpellReader _reader = new SpellReader();
        readonly List<Vector2> _pending = new List<Vector2>();
        readonly List<double> _times = new List<double>(), _pendingTimes = new List<double>();
        readonly List<(double t, Vector2 p)> _recent = new List<(double, Vector2)>();
        double _now, _strokeStart, _restSince = -1, _pendingSince, _lastActive = -10, _activeSince = -10, _slowSince = -1;
        Vector2 _arrivalFrom;
        int _restFrom;
        bool _wasActive;

        public SpellReader Reader => _reader;

        public void Reset()
        {
            Drawing = Arriving = false;
            Stroke.Clear();
            _times.Clear();
            _pending.Clear();
            _pendingTimes.Clear();
            _recent.Clear();
            _restSince = -1;
            _wasActive = false;
            Last = null;
        }

        /// <summary>Every frame: the wand at <paramref name="px"/> (HUD pixels) or down (<paramref name="active"/> false) at
        /// <paramref name="now"/> (seconds). Says when a stroke was read (<see cref="LastRead"/>, <see cref="Last"/>) or
        /// fizzled.</summary>
        public Outcome Update(double now, bool active, Vector2 px)
        {
            _now = now;
            if (!active)
            {
                // a short drop-out (the camera lost the hand a moment) doesn't end a stroke
                if (now - _lastActive > 0.15)
                {
                    _recent.Clear();
                    _wasActive = false;
                    Arriving = false;
                }
                if (Drawing && now - _lastActive > 0.15) return Finish(now, true);
                return Drawing ? Outcome.None : Lapse(now);
            }
            if (!_wasActive)
            {
                // raised: on its way up
                _wasActive = true;
                Arriving = true;
                _activeSince = now;
                _arrivalFrom = px;
                _slowSince = -1;
            }
            _lastActive = now;
            _recent.Add((now, px));
            while (_recent.Count > 2 && now - _recent[1].t >= Window * 2.5) _recent.RemoveAt(0);
            float speed = Speed(now);
            double from = now - Window * 1.5;
            if (Arriving && !Drawing)
            {
                if (!Arrived(now, px, speed)) return Lapse(now);
                Arriving = false;
                // turned off the way up: the shape begins at the turn
                from = now - Window;
            }
            if (!Drawing)
            {
                if (speed < StartSpeed) return Lapse(now);
                Drawing = true;
                _strokeStart = now;
                _restSince = -1;
                Stroke.Clear();
                _times.Clear();
                // a stroke read as nothing a moment ago and this one are one stroke
                if (_pending.Count > 0 && now - _pendingSince <= JoinGap)
                    for (int i = 0; i < _pending.Count; i++) Add(_pending[i], _pendingTimes[i]);
                _pending.Clear();
                _pendingTimes.Clear();
                // from where the move began (the last Window and a half, the still part before it left out)
                foreach (var (t, p) in _recent)
                    if (t >= from && (Stroke.Count > 0 || Vector2.Distance(p, px) > 6f)) Add(p, t);
                return Outcome.None;
            }
            Add(px, now);
            if (speed < RestSpeed)
            {
                if (_restSince < 0)
                {
                    _restSince = now;
                    _restFrom = Stroke.Count;
                }
                if (now - _restSince >= RestTime)
                {
                    // the rest is no part of the shape
                    if (_restFrom > 1 && _restFrom < Stroke.Count)
                    {
                        Stroke.RemoveRange(_restFrom, Stroke.Count - _restFrom);
                        _times.RemoveRange(_restFrom, _times.Count - _restFrom);
                    }
                    return Finish(now, false);
                }
            }
            else _restSince = -1;
            if (now - _strokeStart >= MaxSeconds) return Finish(now, false);
            return Outcome.None;
        }

        /// <summary>Has the hand arrived where its shape begins? (It slowed down, or turned off its way up, or it's been a
        /// while.) On a turn the stroke starts right away.</summary>
        bool Arrived(double now, Vector2 px, float speed)
        {
            if (now - _activeSince >= ArriveMax) return true;
            if (speed < RestSpeed)
            {
                if (_slowSince < 0) _slowSince = now;
                if (now - _slowSince >= ArriveRest)
                {
                    _arrivalFrom = px;
                    return true;
                }
                return false;
            }
            _slowSince = -1;
            // the way it came in: from where it was raised; the way it goes now: the last Window
            var came = px - _arrivalFrom;
            if (came.sqrMagnitude < 80f * 80f || _recent.Count < 3) return false;
            int i = _recent.Count - 1;
            while (i > 0 && now - _recent[i - 1].t <= Window) i--;
            var goes = px - _recent[Mathf.Max(0, i - 1)].p;
            return goes.sqrMagnitude > 25f * 25f && Vector2.Angle(came, goes) > ArriveTurn;
        }

        void Add(Vector2 p, double t)
        {
            if (Stroke.Count > 0 && (Stroke[Stroke.Count - 1] - p).sqrMagnitude < 4f) return;
            Stroke.Add(p);
            _times.Add(t);
        }

        /// <summary>The distance covered in the last <see cref="Window"/> seconds, a second.</summary>
        float Speed(double now)
        {
            if (_recent.Count < 2) return 0f;
            int i = _recent.Count - 1;
            while (i > 0 && now - _recent[i - 1].t <= Window) i--;
            if (i > 0) i--;
            double dt = now - _recent[i].t;
            return dt > 1e-3 ? Vector2.Distance(_recent[_recent.Count - 1].p, _recent[i].p) / (float)dt : 0f;
        }

        Outcome Finish(double now, bool handDown)
        {
            Drawing = false;
            _restSince = -1;
            if (handDown) TrimDrop();
            float length = 0f;
            for (int i = 1; i < Stroke.Count; i++) length += Vector2.Distance(Stroke[i - 1], Stroke[i]);
            if (Stroke.Count < 3 || length < MinLength || SpellReader.IsLine(Stroke, 0, Stroke.Count))
            {
                // a twitch, or a line (pointing somewhere, the way up from the hip, the arm dropping, there and back): no
                // shape, nothing to say
                if (KeepLog) Dropped.Add(Stroke.ToArray());
                Stroke.Clear();
                _times.Clear();
                return Lapse(now);
            }
            // read on an even clock: 60 points a second whatever the game's frame rate (the box draws 30 frames a second, a
            // test at 3x speed 20 a game second), so the reader's median of seven is always ~0.12 s of the hand
            Regular(Stroke, _times, _even);
            var read = _reader.ReadBest(_even, Allowed);
            if (KeepLog) Log.Add(Stroke.ToArray());
            Last = Stroke.ToArray();
            LastRead = read;
            _pending.Clear();
            _pendingTimes.Clear();
            if (!read.Found)
            {
                _pending.AddRange(Stroke);
                _pendingTimes.AddRange(_times);
                _pendingSince = now;
            }
            Stroke.Clear();
            _times.Clear();
            return read.Found ? Outcome.Read : Outcome.None;
        }

        readonly List<Vector2> _even = new List<Vector2>(512);

        /// <summary>The stroke at 60 points a second (straight lines between the points it has).</summary>
        static void Regular(List<Vector2> pts, List<double> times, List<Vector2> into)
        {
            into.Clear();
            int n = pts.Count;
            if (n == 0) return;
            const double step = 1.0 / 60.0;
            double t0 = times[0], t1 = times[n - 1];
            int j = 1;
            for (double t = t0; t <= t1 + 1e-9 && into.Count < 2000; t += step)
            {
                while (j < n - 1 && times[j] < t) j++;
                double a = times[j - 1], b = times[j];
                float k = b > a ? (float)((t - a) / (b - a)) : 1f;
                into.Add(Vector2.Lerp(pts[j - 1], pts[j], Mathf.Clamp01(k)));
            }
            if (into.Count == 0 || (into[into.Count - 1] - pts[n - 1]).sqrMagnitude > 1f) into.Add(pts[n - 1]);
        }

        /// <summary>The arm dropping at the end of a stroke the hand ended by going down: the quick run down at its end.</summary>
        void TrimDrop()
        {
            int n = Stroke.Count;
            if (n < 8) return;
            // the stroke's own pace: its median speed point to point
            var speeds = new List<float>(n);
            for (int i = 1; i < n; i++)
            {
                double dt = _times[i] - _times[i - 1];
                if (dt > 1e-4) speeds.Add(Vector2.Distance(Stroke[i - 1], Stroke[i]) / (float)dt);
            }
            if (speeds.Count < 4) return;
            speeds.Sort();
            float pace = speeds[speeds.Count / 2];
            int cut = n;
            // the wand held at the screen's bottom edge as the hand goes below it
            while (cut > 2 && Stroke[cut - 1].y > BottomEdge) cut--;
            for (int i = cut - 1; i > 0; i--)
            {
                var d = Stroke[i] - Stroke[i - 1];
                double dt = _times[i] - _times[i - 1];
                float v = dt > 1e-4 ? d.magnitude / (float)dt : 0f;
                bool down = d.y > 0.6f * d.magnitude;
                if (!down || v < DropSpeed * pace) break;
                cut = i;
            }
            // (keep the point the drop left from)
            if (cut < n && cut > 2)
            {
                Stroke.RemoveRange(cut, n - cut);
                _times.RemoveRange(cut, n - cut);
            }
        }

        /// <summary>A stroke read as nothing that nothing joined: it fizzles.</summary>
        Outcome Lapse(double now)
        {
            if (_pending.Count == 0 || now - _pendingSince <= JoinGap) return Outcome.None;
            _pending.Clear();
            _pendingTimes.Clear();
            return Outcome.Fizzled;
        }
    }
}
