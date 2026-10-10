using UnityEngine;

namespace MoveIt.Games.Sword
{
    /// <summary>Which way a swing goes on the screen. <see cref="Right"/> = the blade sweeps from left to right.</summary>
    public enum Cut { None, Right, Left, Down, Up }

    /// <summary>Where a knight's blow comes from (as the player sees it), so where the shield must go.</summary>
    public enum AttackFrom { Overhead, Left, Right }

    /// <summary>
    /// The sword and shield rules, on the screen (x right, y up, units = screen heights), so they match what the player
    /// sees and work the same for every camera angle:
    /// <list type="bullet">
    /// <item>a <b>swing</b> is the blade's tip moving faster than <see cref="SwingSpeed"/>;</item>
    /// <item>a <b>hit</b> is a swing whose outer blade sweeps through the knight between two frames;</item>
    /// <item>a <b>shield block</b> is the shield over the spot where the blow lands (<see cref="ShieldCovers"/>);
    /// a <b>parry</b> with the sword also counts: held across the blow (level above the shoulders for an overhead
    /// blow, upright on that side for a blow from the left or right).</item>
    /// </list>
    /// Pure logic (no scene): tests feed it blade and shield positions.
    /// </summary>
    public sealed class SwordMoves
    {
        /// <summary>Screen heights per second of the tip: faster = a swing.</summary>
        public float SwingSpeed = 1.6f;
        /// <summary>A swing ends when the tip slows below this.</summary>
        public float SwingEndSpeed = 0.8f;
        /// <summary>Degrees from level (overhead) or upright (side) that still parry.</summary>
        public float ParryTolerance = 35f;
        /// <summary>A parry is a held pose: the tip moves slower than this (screen heights per second).</summary>
        public float ParryMaxSpeed = 1.2f;
        /// <summary>How far the shield may be from the spot, in shield radii (the shield's own size, plus this much of
        /// the spot's): forgiving, children hold a shield roughly.</summary>
        public float ShieldReach = 0.6f;

        Vector2 _hilt, _tip;
        bool _has;

        public Vector2 Hilt => _hilt;
        public Vector2 Tip => _tip;
        public Vector2 PreviousHilt { get; private set; }
        public Vector2 PreviousTip { get; private set; }
        /// <summary>Screen heights per second, lightly smoothed.</summary>
        public Vector2 TipVelocity { get; private set; }
        public float TipSpeed => TipVelocity.magnitude;
        public bool Swinging { get; private set; }
        /// <summary>Counts swings: one swing can hit the knight only once.</summary>
        public int SwingId { get; private set; }
        /// <summary>The direction of the current (or last) swing.</summary>
        public Cut SwingCut { get; private set; }
        /// <summary>The shield on the screen: centre and radius.</summary>
        public Vector2 Shield { get; private set; }
        public float ShieldRadius { get; private set; }

        /// <summary>This frame's blade on the screen.</summary>
        public void Update(Vector2 hilt, Vector2 tip, float dt)
        {
            PreviousHilt = _has ? _hilt : hilt;
            PreviousTip = _has ? _tip : tip;
            _hilt = hilt;
            _tip = tip;
            if (_has && dt > 1e-4f)
                TipVelocity = Vector2.Lerp(TipVelocity, (tip - PreviousTip) / dt, 0.6f);
            _has = true;

            float speed = TipSpeed;
            if (!Swinging && speed >= SwingSpeed)
            {
                Swinging = true;
                SwingId++;
            }
            else if (Swinging && speed < SwingEndSpeed) Swinging = false;
            if (Swinging) SwingCut = Classify(TipVelocity);
        }

        /// <summary>This frame's shield on the screen.</summary>
        public void UpdateShield(Vector2 center, float radius)
        {
            Shield = center;
            ShieldRadius = radius;
        }

        public void Reset()
        {
            _has = false;
            Swinging = false;
            TipVelocity = Vector2.zero;
        }

        /// <summary>The nearest of the four directions to a movement.</summary>
        public static Cut Classify(Vector2 v)
        {
            if (v.sqrMagnitude < 1e-8f) return Cut.None;
            if (Mathf.Abs(v.x) >= Mathf.Abs(v.y)) return v.x > 0f ? Cut.Right : Cut.Left;
            return v.y > 0f ? Cut.Up : Cut.Down;
        }

        public static Vector2 Direction(Cut cut) => cut switch
        {
            Cut.Right => Vector2.right,
            Cut.Left => Vector2.left,
            Cut.Up => Vector2.up,
            Cut.Down => Vector2.down,
            _ => Vector2.zero,
        };

        /// <summary>
        /// Did the blade's outer part pass through the circle between the last frame and this one? Checked along the
        /// paths of a few points on the blade (the hilt end is the player's own hand, it doesn't count).
        /// </summary>
        public bool SweptThrough(Vector2 center, float radius)
        {
            for (int i = 0; i <= 4; i++)
            {
                float k = Mathf.Lerp(0.35f, 1f, i / 4f);
                var from = Vector2.Lerp(PreviousHilt, PreviousTip, k);
                var to = Vector2.Lerp(_hilt, _tip, k);
                if (DistanceToSegment(center, from, to) <= radius) return true;
            }
            return false;
        }

        /// <summary>Is the shield over the spot where the blow lands (a circle of <paramref name="spotRadius"/>)?</summary>
        public bool ShieldCovers(Vector2 spot, float spotRadius) =>
            ShieldRadius > 0f && Vector2.Distance(Shield, spot) <= ShieldRadius + ShieldReach * spotRadius;

        /// <summary>The blade's angle from level, 0..90 degrees (which end points where doesn't matter).</summary>
        public static float Tilt(Vector2 hilt, Vector2 tip)
        {
            var d = tip - hilt;
            return Mathf.Atan2(Mathf.Abs(d.y), Mathf.Abs(d.x)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Is the sword held across a blow from <paramref name="from"/>? <paramref name="chest"/> is the figure's chest on
        /// the screen, <paramref name="shoulderY"/> its shoulder height (screen heights).
        /// </summary>
        public bool Parries(AttackFrom from, Vector2 chest, float shoulderY)
        {
            if (!_has || TipSpeed > ParryMaxSpeed) return false;
            float tilt = Tilt(_hilt, _tip);
            var middle = (_hilt + _tip) * 0.5f;
            float length = (_tip - _hilt).magnitude;
            switch (from)
            {
                case AttackFrom.Overhead:
                    // level, and up where the blow lands: the middle of the blade at least about shoulder height
                    return tilt <= ParryTolerance && middle.y >= shoulderY - 0.1f * length;
                case AttackFrom.Left:
                    return tilt >= 90f - ParryTolerance && middle.x <= chest.x + 0.15f * length && middle.y >= chest.y - 0.4f * length;
                default:
                    return tilt >= 90f - ParryTolerance && middle.x >= chest.x - 0.15f * length && middle.y >= chest.y - 0.4f * length;
            }
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
