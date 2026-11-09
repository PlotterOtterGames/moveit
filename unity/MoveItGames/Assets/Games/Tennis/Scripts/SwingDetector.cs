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
        /// <summary>A small, gentle move (slower than <see cref="SwingDetector.StrongSpeed"/>), like a flick of a Wii
        /// remote: the game takes it as a swing only at a ball in reach (anywhere else the hand is just moving).</summary>
        public bool Gentle;

        /// <summary>How fast the hand went sideways (torso lengths per second): a tennis swing's power.</summary>
        public float SidewaysSpeed => Speed * Mathf.Abs(Direction.x);
    }

    /// <summary>
    /// Racket swings from a front camera, forgiving like <c>PunchDetector</c>: any quick move of either hand is a swing,
    /// down to a small flick of the forearm like a Wii remote's (the user, 2026-11-06: "moving the whole arm is
    /// tiring"), while the pose model's glitches are not. Wii Sports Tennis works the same way: the Mii runs by itself,
    /// picks forehand or backhand by where the ball is, and only the moment of the swing (and roughly its speed and
    /// whether it goes up or down) matters.
    /// <para><b>Glitches</b> (measured on the box's recordings, 2026-11-06): the pose model now and then puts a wrist
    /// somewhere it isn't for one to a few frames: pulled onto its elbow when the hand is in front of the chest, flipped
    /// to another spot (once 1.2 torso lengths up by the head and back, twice), or lost in a blur (visibility 0.6). The
    /// old detector (a hand faster than 3 torso lengths per second on the low-lag track) read most of those as swings:
    /// in hand-circle recordings 9 of 15 swings were glitches, some straight up or down (an instant miss at a ball in
    /// reach). Real moves are different: a hand speeds up and slows down over several frames, while a glitch moves in
    /// one step and comes back. So:</para>
    /// <list type="bullet">
    /// <item>samples: the wrists' raw camera samples (not the low-lag filter, which smears a one-frame jump into a
    /// two-frame move) relative to the raw shoulders, in torso lengths, each through a median of three samples (one
    /// sample late: a one-frame glitch never gets through, a real move does, unchanged); unsure ones
    /// (<see cref="MinVisibility"/>) are skipped;</item>
    /// <item>a swing starts when the hand moves at least <see cref="SwingSpeed"/> over <see cref="SpeedWindow"/>, carried
    /// by both of its last two steps (<see cref="StepShare"/>: a jump is one step), measured from where the hand
    /// trustably was before (an anchor <see cref="AnchorAge"/> back);</item>
    /// <item>it is confirmed while its fastest moment is looked for (no extra delay): far enough from the anchor
    /// (<see cref="MinTravel"/>, waiting up to <see cref="ConfirmWithin"/> while the hand is still on its way), not all
    /// in one step (<see cref="MaxStepShare"/>), and not straight back (<see cref="BackShare"/> for
    /// <see cref="BackSamples"/> samples). Rejected, its samples lose their trust: a hand flipping to a wrong spot and back
    /// is no move at all, not two (<see cref="Ignored"/> counts them);</item>
    /// <item>one move, one swing: a hand swings again once it has slowed down (<see cref="ReArmShare"/> of the start
    /// speed, two samples), turned back, or after <see cref="RepeatAfter"/> of moving fast all along, and never within
    /// <see cref="Cooldown"/> (short enough for the stroke right after a backswing);</item>
    /// <item>the racket hand (<see cref="RightHanded"/>) comes first: the other hand going up or down (balance, a wave, the
    /// toss) is never a swing, and a swing of the other hand while the racket hand's is under way is left to it;</item>
    /// <item>its speed (the shot's power) and direction are the swing's fastest moment; slower than
    /// <see cref="StrongSpeed"/> it is <see cref="SwingEvent.Gentle"/>. Its time stays the start's (plus
    /// <see cref="ContactLead"/>): the game judges the timing by capture times, so reporting it a few frames later
    /// changes nothing there.</item>
    /// </list>
    /// Simulated (`fake_sender`-like arms, the box's noise, 40 runs each): forearm flicks of 0.25-0.5 torso lengths in
    /// 0.15-0.25 s are found 37-40 times in 40 (the old detector: never), full swings always, a still hand with
    /// one- to three-frame glitches never swings (the old: every glitch), a one-frame glitch in the middle of a swing
    /// keeps it 35-40 times in 40; a two-frame glitch there loses it (rare: ~1-2 % of swings in the recordings). On the
    /// routine recorded with the box camera, where two pose models on the same frames tell real moves from glitches:
    /// 122 real swings and 1 glitch (the old: 63 and 2). In torso lengths, so the same numbers fit a child and an adult
    /// at any distance. Pure logic: tests feed it samples.
    /// </summary>
    public sealed class SwingDetector
    {
        /// <summary>A swing: the wrist this fast (torso lengths per second). A flick of the forearm is ~2-4, a child's lazy
        /// swing ~4, an adult's forehand 8-12, a still hand's camera noise well under 0.5.</summary>
        public float SwingSpeed = 1.6f;
        /// <summary>A swing slower than this is <see cref="SwingEvent.Gentle"/> (the old threshold: what an arm swinging
        /// at nothing in particular reaches).</summary>
        public float StrongSpeed = 3f;
        /// <summary>A swing takes the hand at least this far (torso lengths) from where it was: a flick of ~10 cm.</summary>
        public float MinTravel = 0.18f;
        /// <summary>Speeds are measured over about this much camera time.</summary>
        public float SpeedWindow = 0.08f;
        /// <summary>A swing is measured from where the hand was at least this long (s) before it got fast.</summary>
        public float AnchorAge = 0.12f;
        /// <summary>Both of the last two steps carry the move: each at least this share of its speed (or of
        /// <see cref="SwingSpeed"/>, if faster) the swing's way.</summary>
        public float StepShare = 0.3f;
        /// <summary>No single step is more than this share of the swing (a glitch jumps in one), give or take
        /// <see cref="StepNoise"/> (torso lengths: the camera's noise on a quick flick's few steps) when the pose model is
        /// sure of every sample (<see cref="SureVisibility"/>).</summary>
        public float MaxStepShare = 0.7f, StepNoise = 0.05f;
        /// <summary>A swing with a sample the pose model is less sure of than this (a hand in a blur, half hidden) gets no
        /// <see cref="StepNoise"/> and waits a sample more before it counts: such glitches came back within two frames.</summary>
        public float SureVisibility = 0.8f;
        /// <summary>A swing whose hand comes back under this share of how far it went, for <see cref="BackSamples"/>
        /// samples in a row, was a glitch and its return.</summary>
        public float BackShare = 0.45f;
        public int BackSamples = 2;
        /// <summary>A swing still short of <see cref="MinTravel"/> waits this long (s) while the hand keeps going.</summary>
        public float ConfirmWithin = 0.2f;
        /// <summary>Ready again once the hand moves slower than this share of <see cref="SwingSpeed"/> (two samples)...</summary>
        public float ReArmShare = 0.5f;
        /// <summary>...or after this long (s) of moving fast all along (flailing keeps swinging).</summary>
        public float RepeatAfter = 0.7f;
        /// <summary>No second swing from the same hand within this time (s).</summary>
        public float Cooldown = 0.22f;
        /// <summary>The racket is in the right hand (else the left).</summary>
        public bool RightHanded = true;
        /// <summary>A move more up or down than this (its direction's vertical share) is vertical: the free hand's are
        /// never swings (<see cref="TennisMatch.VerticalLimit"/>).</summary>
        public float VerticalLimit = 0.77f;
        /// <summary>The racket meets the ball this long (s) after the hand gets fast: the middle of the swing.</summary>
        public float ContactLead = 0.05f;
        /// <summary>Samples the pose model is less sure of are skipped (its glitches came with ~0.6-0.85).</summary>
        public float MinVisibility = 0.6f;
        /// <summary>A hand just found (or found again after a gap) doesn't swing for this long (s).</summary>
        public float SettleSeconds = 0.25f;
        /// <summary>A swing is reported once its speed stops growing, or after this many more samples (or 0.1 s).</summary>
        public int PeakSamples = 3;

        /// <summary>How many moves were taken for glitches and ignored (for the log).</summary>
        public int Ignored { get; private set; }

        /// <summary>A median of three samples per axis, a sample late.</summary>
        sealed class Median3
        {
            readonly double[] _t = new double[3];
            readonly Vector2[] _p = new Vector2[3];
            readonly float[] _v = new float[3];
            int _count;

            public void Reset() => _count = 0;

            public bool Push(double t, Vector2 p, float v, out double time, out Vector2 point, out float visibility)
            {
                if (_count == 3)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        _t[i] = _t[i + 1];
                        _p[i] = _p[i + 1];
                        _v[i] = _v[i + 1];
                    }
                    _count = 2;
                }
                _t[_count] = t;
                _p[_count] = p;
                _v[_count] = v;
                _count++;
                time = 0;
                point = default;
                visibility = 0f;
                if (_count < 3) return false;
                time = _t[1];
                if (_t[2] - _t[1] > 0.25 || _t[1] - _t[0] > 0.25)
                {
                    // across a gap the neighbours say nothing about this sample
                    point = _p[1];
                    visibility = _v[1];
                    return true;
                }
                point = new Vector2(Mid(_p[0].x, _p[1].x, _p[2].x), Mid(_p[0].y, _p[1].y, _p[2].y));
                visibility = Mathf.Min(_v[1], Mathf.Max(_v[0], _v[2]));
                return true;
            }

            static float Mid(float a, float b, float c) => Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
        }

        sealed class Hand
        {
            public const int History = 16;
            public readonly double[] Times = new double[History];
            public readonly Vector2[] Points = new Vector2[History];
            public readonly float[] Visibility = new float[History];
            /// <summary>Trusted: not part of a move taken for a glitch.</summary>
            public readonly bool[] Ok = new bool[History];
            public readonly Median3 Median = new Median3();
            public int Count;
            public double FoundAt, SwungAt = -10;
            public bool Armed = true, Pending;
            public float Speed, PeakSpeed, MaxOut;
            public Vector2 PeakVelocity, Direction = Vector2.right, Anchor;
            public int Samples, AnchorIndex, CandidateFrom, Back, Slow;

            public Vector2 Newest => Points[Count - 1];

            public void Push(double time, Vector2 p, float visibility)
            {
                if (Count == History)
                {
                    System.Array.Copy(Times, 1, Times, 0, History - 1);
                    System.Array.Copy(Points, 1, Points, 0, History - 1);
                    System.Array.Copy(Visibility, 1, Visibility, 0, History - 1);
                    System.Array.Copy(Ok, 1, Ok, 0, History - 1);
                    Count--;
                    CandidateFrom = Mathf.Max(0, CandidateFrom - 1);
                    AnchorIndex = Mathf.Max(0, AnchorIndex - 1);
                }
                Times[Count] = time;
                Points[Count] = p;
                Visibility[Count] = visibility;
                Ok[Count] = true;
                Count++;
            }

            /// <summary>The newest trusted sample (before the newest one) at least <paramref name="age"/> s older than the
            /// newest, or -1.</summary>
            public int TrustedBefore(double age)
            {
                for (int i = Count - 2; i >= 0; i--)
                    if (Ok[i] && Times[Count - 1] - Times[i] >= age) return i;
                return -1;
            }

            public int LastTrusted()
            {
                for (int i = Count - 2; i >= 0; i--)
                    if (Ok[i]) return i;
                return -1;
            }

            public void Reset()
            {
                Count = 0;
                Speed = 0f;
                Armed = true;
                Pending = false;
                SwungAt = -10;
                PeakVelocity = Vector2.zero;
                Slow = Back = 0;
            }
        }

        readonly Hand _left = new Hand(), _right = new Hand();
        double _lastSwing = -10;

        /// <summary>The hand's speed right now (torso lengths per second), for debugging.</summary>
        public float Speed(bool right) => (right ? _right : _left).Speed;

        /// <summary>The capture time a swing still being confirmed started (either hand), or +infinity: every swing that
        /// started before it has been reported (Volejboll! waits for the camera up to it before it decides a touch).</summary>
        public double PendingSince => System.Math.Min(_left.Pending ? _left.SwungAt : double.PositiveInfinity,
                                                      _right.Pending ? _right.SwungAt : double.PositiveInfinity);

        public void Reset()
        {
            _left.Reset();
            _right.Reset();
            _left.Median.Reset();
            _right.Median.Reset();
            _lastSwing = -10;
        }

        /// <summary>
        /// One camera sample (once per sample, its capture time in seconds): the shoulders' middle, the torso length (view
        /// units) and both wrists with their visibility, all as the camera saw them (raw: the detector filters them
        /// itself). Returns true when a swing is seen (the faster hand if both).
        /// </summary>
        public bool Update(double time, Vector2 shoulders, float torso, Vector2 leftWrist, float leftVisibility,
                           Vector2 rightWrist, float rightVisibility, out SwingEvent swing)
        {
            swing = default;
            if (torso < 1e-4f) return false;
            bool lm = _left.Median.Push(time, (leftWrist - shoulders) / torso, leftVisibility, out double lt, out var lp, out float lv);
            bool rm = _right.Median.Push(time, (rightWrist - shoulders) / torso, rightVisibility, out _, out var rp, out float rv);
            if (!lm || !rm) return false;
            time = lt;
            bool l = Sample(_left, time, lp, lv);
            bool r = Sample(_right, time, rp, rv);
            // the free hand: up or down is never a swing (and doesn't use up the racket hand's), and while the racket
            // hand's swing is under way that one is the swing
            var racket = RightHanded ? _right : _left;
            var free = RightHanded ? _left : _right;
            if ((RightHanded ? l : r) && (Mathf.Abs(Direction(free).y) >= VerticalLimit || racket.Pending))
            {
                if (RightHanded) l = false;
                else r = false;
            }
            if (!l && !r) return false;
            bool right = r && (!l || RightHanded);   // both at once: the racket hand
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
                Time = h.SwungAt + ContactLead, RightHand = right, Speed = h.PeakSpeed, Direction = Direction(h),
                Gentle = h.PeakSpeed < StrongSpeed,
            };
            return true;
        }

        /// <summary>A hand's swing direction (its fastest moment), unit length.</summary>
        static Vector2 Direction(Hand h) => h.PeakVelocity.sqrMagnitude > 1e-8f ? h.PeakVelocity.normalized : Vector2.right;

        /// <summary>A move taken for a glitch: its samples lose their trust.</summary>
        void Reject(Hand h)
        {
            h.Pending = false;
            for (int i = h.CandidateFrom; i < h.Count; i++) h.Ok[i] = false;
            Ignored++;
        }

        bool Sample(Hand h, double time, Vector2 p, float visibility)
        {
            if (visibility < MinVisibility)
            {
                if (h.Pending) Reject(h);
                return false;
            }
            if (h.Count > 0)
            {
                double since = time - h.Times[h.Count - 1];
                if (since < 0.012) return false;     // a burst after a stall: wait for a real interval
                int last = h.LastTrusted();
                // a long gap says nothing about speed, nor does a hand that hasn't been trustable for a while
                if (since > 0.25 || (last >= 0 && time - h.Times[last] > 0.3) || (last < 0 && h.Count > 4)) h.Reset();
            }
            if (h.Count == 0) h.FoundAt = time;
            h.Push(time, p, visibility);
            if (time - h.FoundAt < SettleSeconds || h.Count < 3)
            {
                h.Speed = 0f;
                return false;
            }
            // the speed over the last ~window, from a trusted sample to the newest
            int then = h.TrustedBefore(SpeedWindow * 0.6f);
            if (then < 0) return false;
            var velocity = (p - h.Points[then]) / (float)(time - h.Times[then]);
            float speed = velocity.magnitude;
            h.Speed = speed;

            if (h.Pending)
            {
                h.Samples++;
                float outward = Vector2.Dot(p - h.Anchor, h.Direction);
                h.MaxOut = Mathf.Max(h.MaxOut, outward);
                if (speed > h.PeakSpeed && Vector2.Dot(velocity, h.Direction) > 0f)
                {
                    h.PeakSpeed = speed;
                    h.PeakVelocity = velocity;
                }
                // straight back to where it started: a glitch and its return
                h.Back = outward < BackShare * h.MaxOut ? h.Back + 1 : 0;
                if (h.Back >= BackSamples)
                {
                    Reject(h);
                    return false;
                }
                if (h.Back > 0) return false;
                // looking for the swing's fastest moment: decided once it has passed (or soon anyway)
                if (speed >= h.PeakSpeed * 0.9f && h.Samples < PeakSamples && time - h.SwungAt < 0.1) return false;
                // a sample the pose model wasn't sure of: one more sample to see the hand doesn't come straight back
                float sure = 1f;
                for (int i = h.AnchorIndex; i < h.Count; i++) sure = Mathf.Min(sure, h.Visibility[i]);
                bool unsure = sure < SureVisibility;
                if (unsure && h.Samples < 2 && time - h.SwungAt < ConfirmWithin) return false;
                if (h.MaxOut < MinTravel)
                {
                    // not far enough yet: wait while the hand is still on its way
                    if (time - h.SwungAt < ConfirmWithin && Vector2.Dot(velocity, h.Direction) >= SwingSpeed * ReArmShare) return false;
                    Reject(h);
                    return false;
                }
                // not all in one step (a glitch jumps there at once)
                float distance = (p - h.Anchor).magnitude, biggest = 0f;
                for (int i = h.AnchorIndex + 1; i < h.Count; i++) biggest = Mathf.Max(biggest, (h.Points[i] - h.Points[i - 1]).magnitude);
                if (biggest > MaxStepShare * distance + (unsure ? 0f : StepNoise))
                {
                    Reject(h);
                    return false;
                }
                h.Pending = false;
                return true;
            }

            // ready again once the hand slowed down, turned back (a backswing into the stroke may not slow down enough in
            // the camera's frames), or kept moving fast a while
            h.Slow = speed < SwingSpeed * ReArmShare ? h.Slow + 1 : 0;
            var peak = h.PeakVelocity.sqrMagnitude > 1e-8f ? h.PeakVelocity.normalized : Vector2.zero;
            float along = Vector2.Dot(velocity, peak);
            bool turned = along < -SwingSpeed * ReArmShare || (speed >= SwingSpeed && along < 0f);
            if (!h.Armed && (h.Slow >= 2 || turned || time - h.SwungAt >= RepeatAfter)) h.Armed = true;
            if (!h.Armed || time - h.SwungAt < Cooldown || speed < SwingSpeed) return false;
            int anchor = h.TrustedBefore(AnchorAge);
            if (anchor < 0) return false;
            // carried by both of the last two steps, one way (a jump is one step), from trusted samples
            int n = h.Count;
            if (!h.Ok[n - 2] || !h.Ok[n - 3]) return false;
            var direction = velocity / speed;
            var step1 = (h.Points[n - 1] - h.Points[n - 2]) / (float)(h.Times[n - 1] - h.Times[n - 2]);
            var step0 = (h.Points[n - 2] - h.Points[n - 3]) / (float)(h.Times[n - 2] - h.Times[n - 3]);
            float need = StepShare * Mathf.Max(SwingSpeed, speed);
            if (Vector2.Dot(step1, direction) < need || Vector2.Dot(step0, direction) < need) return false;
            h.Armed = false;
            h.SwungAt = time;
            h.Pending = true;
            h.Samples = 0;
            h.PeakSpeed = speed;
            h.PeakVelocity = velocity;
            h.Direction = direction;
            h.Anchor = h.Points[anchor];
            h.AnchorIndex = anchor;
            h.CandidateFrom = anchor + 1;
            h.MaxOut = Vector2.Dot(p - h.Anchor, direction);
            h.Back = 0;
            return false;
        }
    }
}
