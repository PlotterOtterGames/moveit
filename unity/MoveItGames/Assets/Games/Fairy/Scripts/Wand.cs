using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// The magic wand (2026-11-16, the user: "a magic wand as some type of controller like a mouse"): a player's raised
    /// hand points it at the screen.
    /// <list type="bullet">
    /// <item><b>Either hand</b> (<see cref="Hand"/>): the one raised (above the belly: <see cref="RaiseOn"/> torso lengths
    /// over the hips, down under <see cref="RaiseOff"/>); the other hand takes over when it drops. Hands resting at the
    /// sides point at nothing (<see cref="Active"/> false): the child can rest.</item>
    /// <item><b>The zone</b>: a hand's place round the shoulders' middle (torso lengths) maps onto the whole screen,
    /// Kinect's "physical interaction zone": across, from <see cref="HalfWidth"/> - <see cref="Inward"/> over the body to
    /// <see cref="HalfWidth"/> + <see cref="Inward"/> out to the hand's own side; up, from just over the hips
    /// (<see cref="Bottom"/>) to above the head (<see cref="Top"/>). In torso lengths, so a child and an adult reach every
    /// corner at any distance, and swaying doesn't move the wand.</item>
    /// <item><b>Smooth</b>: each hand's cursor runs on the camera's clock like the other hand cursors
    /// (<see cref="HandInterpolator"/>: a camera frame behind, between real samples rather than guessing past the newest,
    /// a light One Euro filter); unsure samples are skipped, a hand unseen <see cref="LoseAfter"/> seconds rests.</item>
    /// <item><b>The wrist</b> is the point (2026-11-17 evening, the user: "it jumps a lot during drawing the spell"): until
    /// then it was the wrist pushed 0.3 of the forearm further out, like the other hand cursors, which adds a third of the
    /// elbow's error, and the elbow is the model's worst joint when a hand draws in front of the body. On the user's
    /// spell drawing recorded on the box (tools/motion/wand_replay.py, the full model) the wand made 58 steps a minute of
    /// more than 40 px that the hand didn't (the heavy reference model's path), with the wrist 22, with the filter's
    /// quicker speed estimate (4 Hz, was 2) 18, a camera frame behind instead of half (17 ms more) 15; on the routine
    /// clip of punches and waves 280 → 117, the jumps over 150 px 12.6 → 3.0. The zone and the raise heights moved by what
    /// the tip had added while drawing (median: 0.07 torso lengths up, 0.06 inward), so the wand lands where it did.</item>
    /// </list>
    /// Pure logic (no engine): tested on simulated hands.
    /// </summary>
    public sealed class Wand
    {
        /// <summary>The zone in torso lengths round the shoulders' middle (x to the player's right, y up): half its width,
        /// how far its middle sits toward the hand's own side, its bottom and top (for the wrist).</summary>
        public float HalfWidth = 1.25f, Inward = 0.36f, Bottom = -0.82f, Top = 0.83f;
        /// <summary>A hand counts as raised when its wrist is this far above the hips (torso lengths), and down again under
        /// <see cref="RaiseOff"/> (the hips are a torso length under the shoulders' middle). Once up, the wand stays with
        /// the hand down to the zone's bottom.</summary>
        public float RaiseOn = 0.33f, RaiseOff = 0.15f;
        public float Visibility = 0.45f;
        public float LoseAfter = 0.35f;

        /// <summary>A hand holds the wand up.</summary>
        public bool Active { get; private set; }
        /// <summary>The hand holding it: 0 the left, 1 the right.</summary>
        public int Hand { get; private set; } = 1;
        /// <summary>Where it points: (0, 0) the screen's bottom left, (1, 1) its top right (a little past at most).</summary>
        public Vector2 Position { get; private set; }
        /// <summary>Screens a second.</summary>
        public Vector2 Velocity { get; private set; }
        public float Speed => Velocity.magnitude;

        readonly HandInterpolator[] _cursor = new HandInterpolator[2];
        readonly Vector2[] _shown = new Vector2[2];
        readonly bool[] _up = new bool[2];
        readonly double[] _seen = { -10, -10 };
        double _last;

        public Wand()
        {
            for (int k = 0; k < 2; k++)
                _cursor[k] = new HandInterpolator(1f, 4f, 4f) { Margin = 1f, MaxExtrapolation = 0.08f };
        }

        public void Reset()
        {
            for (int k = 0; k < 2; k++)
            {
                _cursor[k].Reset();
                _up[k] = false;
                _seen[k] = -10;
            }
            _last = 0;
            Active = false;
            Velocity = Vector2.zero;
        }

        /// <summary>
        /// A camera sample: each hand's wrist round the shoulders' middle in torso lengths (x to the player's right, y up)
        /// and its visibility, captured at <paramref name="time"/> (the camera's clock), received at <paramref name="now"/>
        /// (the game's clock, seconds).
        /// </summary>
        public void Sample(double time, double now, Vector2 left, float leftVisibility, Vector2 right, float rightVisibility)
        {
            if (time <= _last) return;
            _last = time;
            Feed(0, left, leftVisibility, time, now);
            Feed(1, right, rightVisibility, time, now);
            if (!_up[Hand] && _up[1 - Hand]) Hand = 1 - Hand;
        }

        /// <summary>The screen point a hand whose wrist is at <paramref name="wrist"/> points the wand at (both hands'
        /// zones).</summary>
        public Vector2 ToScreen(int hand, Vector2 wrist)
        {
            float side = hand == 1 ? 1f : -1f;
            return new Vector2(0.5f + (wrist.x - side * Inward) / (2f * HalfWidth), (wrist.y - Bottom) / (Top - Bottom));
        }

        void Feed(int k, Vector2 wrist, float visibility, double time, double now)
        {
            if (visibility < Visibility) return;   // unsure: skipped (the cursor glides on), lost if it goes on
            _seen[k] = now;
            float above = wrist.y + 1f;
            _up[k] = _up[k] ? above > RaiseOff : above > RaiseOn;
            _cursor[k].Sample(time, ToScreen(k, wrist), false, now);
        }

        /// <summary>Every frame: where the wand points at <paramref name="now"/> (the game's clock).</summary>
        public void Update(double now, float dt)
        {
            for (int k = 0; k < 2; k++)
            {
                if (now - _seen[k] > LoseAfter) _up[k] = false;
                // both cursors run every frame, so the other hand's is where it should be the moment it takes over
                if (_cursor[k].HasSample) _shown[k] = _cursor[k].Evaluate(now, dt, out _);
            }
            if (!_up[Hand] && _up[1 - Hand]) Hand = 1 - Hand;
            bool was = Active;
            Active = _up[Hand] && _cursor[Hand].HasSample;
            if (!Active)
            {
                Velocity = Vector2.zero;
                return;
            }
            var p = new Vector2(Mathf.Clamp(_shown[Hand].x, -0.03f, 1.03f), Mathf.Clamp(_shown[Hand].y, -0.03f, 1.03f));
            Velocity = was && dt > 0f ? (p - Position) / dt : Vector2.zero;
            Position = p;
        }
    }
}
