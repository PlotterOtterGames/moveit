namespace MoveIt.Motion
{
    /// <summary>
    /// Is a hand seen? The camera AI rates every joint every frame ("visibility"), and a wrist's rating dips below
    /// 0.5 for single frames, more often during fast moves. Dropping the cursor on each dip made it vanish for a few
    /// frames and snap back from a standing start (tools/motion/cursor_sim.py: Fruta's blade jumped ~270 px, Pikturo's
    /// strokes broke apart). So, like the skeleton mirror: a hand is found at 0.5, kept down to 0.3, samples below that
    /// are skipped (the cursor glides on or waits), and it is lost only after <see cref="Grace"/> without a good one.
    /// </summary>
    public sealed class HandPresence
    {
        public const float StartVisibility = 0.5f, KeepVisibility = 0.3f;
        /// <summary>Seconds without a usable sample before the hand counts as lost.</summary>
        public const double Grace = 0.3;

        bool _seen;
        double _lastSeen;

        /// <summary>A new camera sample's wrist visibility, at local time <paramref name="now"/>. Returns whether this
        /// sample's position is good enough to use.</summary>
        public bool Sample(float visibility, double now)
        {
            if (visibility < (Tracked(now) ? KeepVisibility : StartVisibility)) return false;
            _seen = true;
            _lastSeen = now;
            return true;
        }

        public bool Tracked(double now) => _seen && now - _lastSeen <= Grace;

        public void Reset() => _seen = false;
    }
}
