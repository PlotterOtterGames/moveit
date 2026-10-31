using UnityEngine;

namespace MoveIt.Motion
{
    /// <summary>
    /// A string stabilizer, as in drawing programs (Krita's stabilizer, Procreate's StreamLine, Lazy Nezumi, Paint Tool
    /// SAI): the point hangs on a string of <see cref="Radius"/> behind the cursor and moves only when the string is
    /// pulled tight. A hand held still moves nothing, and shake smaller than the string never reaches the paint; a
    /// moving hand drags the point along its path, <see cref="Radius"/> behind.
    ///
    /// Pikturo's brush and pointing hands use 6 px (of a 1080 px tall screen): the camera's noise at rest is 1.4 px a
    /// frame in the hand zone, which magnifies it 1.3-1.8x more than Vishu!'s mirror. In tools/motion/cursor_sim.py
    /// (report_2026_10_19): rest jitter 2.1 -> 1.5 px/frame, overshoot at stops 10 -> 5 px, ~8 ms more lag.
    /// </summary>
    public sealed class CursorRope
    {
        public float Radius;

        Vector2 _tip;
        bool _has;

        public CursorRope(float radius) => Radius = radius;

        public bool HasTip => _has;
        public Vector2 Tip => _tip;

        /// <summary>The cursor is at <paramref name="cursor"/> this frame; returns where the point hangs.</summary>
        public Vector2 Follow(Vector2 cursor)
        {
            if (!_has)
            {
                _tip = cursor;
                _has = true;
                return _tip;
            }
            Vector2 d = cursor - _tip;
            float length = d.magnitude;
            if (length > Radius) _tip += d * (1f - Radius / length);
            return _tip;
        }

        /// <summary>The next cursor starts a new string (the hand was lost, or the cursor moved to another picture).</summary>
        public void Reset() => _has = false;
    }
}
