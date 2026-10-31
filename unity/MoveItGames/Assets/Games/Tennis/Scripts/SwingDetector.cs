using UnityEngine;

namespace MoveIt.Games.Tennis
{
    /// <summary>A racket swing seen by the camera.</summary>
    public struct SwingEvent
    {
        /// <summary>Capture time (seconds, the camera's clock) of the moment the racket meets the ball: a little after
        /// the hand got fast (<see cref="SwingDetector.ContactLead"/>).</summary>
        public double Time;
        public bool RightHand;
        /// <summary>How fast the hand moved, torso lengths per second (in the picture, relative to the shoulders).</summary>
        public float Speed;
        /// <summary>Which way it moved on the (mirrored) screen: x = the screen's right, y = up. Unit length.</summary>
        public Vector2 Direction;
    }

    /// <summary>
    /// Racket swings from a front camera, made to be forgiving like <c>PunchDetector</c>: any quick move of either hand
    /// is a swing (a forehand, a backhand, an overhead serve, a scoop up for a lob, a chop down for a slice, or just a
    /// child's flail), seen as the hand gets fast. Wii Sports Tennis works the same way: the Mii runs by itself, picks
    /// forehand or backhand by where the ball is, and only the moment of the swing (and roughly its speed and whether it
    /// goes up or down) matters.
    /// <list type="bullet">
    /// <item>"quick": the wrist moving at least <see cref="SwingSpeed"/> torso lengths per second relative to the middle
    /// of the shoulders, in the picture (steadier than the world joints), over <see cref="SpeedWindow"/> of camera time.
    /// Relative to the shoulders, so stepping, jumping and swaying don't swing;</item>
    /// <item>one move, one swing: a hand swings again once it has slowed down (<see cref="ReArmSpeed"/>), or after
    /// <see cref="RepeatAfter"/> of moving fast all along, and never within <see cref="Cooldown"/>;</item>
    /// <item>its speed (the shot's power) and direction are the swing's fastest moment, a frame or two after it starts
    /// (<see cref="PeakSamples"/>): at the start, 30 fps catches a swing anywhere from 3 to 9 torso lengths per second.
    /// Its time stays the start's (plus <see cref="ContactLead"/>): the game judges the timing by capture times, so
    /// reporting it a few frames later changes nothing there.</item>
    /// </list>
    /// In torso lengths, so the same numbers fit a child and an adult at any distance. Pure logic: tests feed it samples.
    /// </summary>
    public sealed class SwingDetector
    {
        /// <summary>A swing: the wrist this fast (torso lengths per second). A child's lazy swing is ~4, an adult's
        /// forehand 8-12, a still hand's camera noise well under 0.5, lifting the hands to the face ~1.5-2.5.</summary>
        public float SwingSpeed = 3f;
        /// <summary>Ready again once the hand moves slower than this...</summary>
        public float ReArmSpeed = 1.5f;
        /// <summary>...or after this long (s) of moving fast all along (flailing keeps swinging).</summary>
        public float RepeatAfter = 0.7f;
        /// <summary>No second swing from the same hand within this time (s).</summary>
        public float Cooldown = 0.35f;
        /// <summary>The racket meets the ball this long (s) after the hand gets fast: the middle of the swing.</summary>
        public float ContactLead = 0.05f;
        public float MinVisibility = 0.3f;
        /// <summary>Speeds are measured over about this much camera time.</summary>
        public float SpeedWindow = 0.08f;
        /// <summary>A hand just found (or found again after a gap) doesn't swing for this long (s).</summary>
        public float SettleSeconds = 0.25f;
        /// <summary>A swing is reported once its speed stops growing, or after this many more samples (or 0.1 s).</summary>
        public int PeakSamples = 3;

        sealed class Hand
        {
            public const int History = 8;
            public readonly double[] Times = new double[History];
            public readonly Vector2[] Points = new Vector2[History];
            public int Count, Newest = -1;
            public double FoundAt, SwungAt = -10;
            public bool Armed = true;
            public float Speed;
            public Vector2 Velocity;
            /// <summary>A swing started (at <see cref="SwungAt"/>) and its fastest moment is being looked for.</summary>
            public bool Pending;
            public int PendingSamples;
            public float PeakSpeed;
            public Vector2 PeakVelocity;

            public void Push(double time, Vector2 p)
            {
                if (Count == 0) FoundAt = time;
                Newest = (Newest + 1) % History;
                Times[Newest] = time;
                Points[Newest] = p;
                Count = Mathf.Min(Count + 1, History);
            }

            public int Before(float window)
            {
                int best = -1;
                for (int k = 1; k < Count; k++)
                {
                    int i = (Newest - k + History) % History;
                    best = i;
                    if (Times[Newest] - Times[i] >= window) break;
                }
                return best >= 0 && Times[Newest] - Times[best] >= window * 0.6f ? best : -1;
            }

            public void Reset()
            {
                Count = 0;
                Newest = -1;
                Speed = 0f;
                Velocity = Vector2.zero;
                Armed = true;
                SwungAt = -10;
                Pending = false;
            }
        }

        readonly Hand _left = new Hand(), _right = new Hand();
        double _lastSwing = -10;

        /// <summary>The hand's speed right now (torso lengths per second), for debugging.</summary>
        public float Speed(bool right) => (right ? _right : _left).Speed;

        public void Reset()
        {
            _left.Reset();
            _right.Reset();
            _lastSwing = -10;
        }

        /// <summary>
        /// One camera sample (once per sample, its capture time in seconds): the shoulders' middle, the torso length (view
        /// units) and both wrists with their visibility. Returns true when a swing is seen (the faster hand if both).
        /// </summary>
        public bool Update(double time, Vector2 shoulders, float torso, Vector2 leftWrist, float leftVisibility,
                           Vector2 rightWrist, float rightVisibility, out SwingEvent swing)
        {
            swing = default;
            if (torso < 1e-4f) return false;
            bool l = Sample(_left, time, (leftWrist - shoulders) / torso, leftVisibility);
            bool r = Sample(_right, time, (rightWrist - shoulders) / torso, rightVisibility);
            if (!l && !r) return false;
            bool right = r && (!l || _right.PeakSpeed >= _left.PeakSpeed);
            var h = right ? _right : _left;
            // one racket: a swing spends the other hand too (both arms swing together, or one hand's blur)
            var other = right ? _left : _right;
            other.Armed = false;
            other.Pending = false;
            other.SwungAt = time;
            if (h.SwungAt - _lastSwing < Cooldown) return false;
            _lastSwing = h.SwungAt;
            swing = new SwingEvent
            {
                Time = h.SwungAt + ContactLead, RightHand = right, Speed = h.PeakSpeed,
                Direction = h.PeakVelocity.sqrMagnitude > 1e-8f ? h.PeakVelocity.normalized : Vector2.right,
            };
            return true;
        }

        bool Sample(Hand h, double time, Vector2 p, float visibility)
        {
            if (visibility < MinVisibility) return false;
            if (h.Count > 0)
            {
                double since = time - h.Times[h.Newest];
                if (since < 0.012) return false;     // a burst after a stall: wait for a real interval
                if (since > 0.25) h.Count = 0;       // a long gap says nothing about speed
            }
            h.Push(time, p);
            int then = h.Before(SpeedWindow);
            if (then < 0 || time - h.FoundAt < SettleSeconds)
            {
                h.Speed = 0f;
                h.Velocity = Vector2.zero;
                return false;
            }
            h.Velocity = (p - h.Points[then]) / (float)(time - h.Times[then]);
            h.Speed = h.Velocity.magnitude;
            if (h.Pending)
            {
                // looking for the swing's fastest moment: reported once it has passed (or soon anyway)
                h.PendingSamples++;
                if (h.Speed > h.PeakSpeed)
                {
                    h.PeakSpeed = h.Speed;
                    h.PeakVelocity = h.Velocity;
                }
                if (h.Speed >= h.PeakSpeed * 0.9f && h.PendingSamples < PeakSamples && time - h.SwungAt < 0.1) return false;
                h.Pending = false;
                return true;
            }
            if (!h.Armed && (h.Speed < ReArmSpeed || time - h.SwungAt >= RepeatAfter)) h.Armed = true;
            if (!h.Armed || time - h.SwungAt < Cooldown || h.Speed < SwingSpeed) return false;
            h.Armed = false;
            h.SwungAt = time;
            h.Pending = true;
            h.PendingSamples = 0;
            h.PeakSpeed = h.Speed;
            h.PeakVelocity = h.Velocity;
            return false;
        }
    }
}
