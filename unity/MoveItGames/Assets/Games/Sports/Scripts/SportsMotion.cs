using UnityEngine;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// Running on the spot (Kinect Sports' sprint and hurdles), from a front camera: each knee (or ankle) that comes up
    /// past the other one is a step; arms pumping (the wrists taking turns going up) count too, for players whose feet
    /// the camera can't see well. The steps a second over the last moment give a running speed. Forgiving: any honest
    /// running on the spot moves the runner, and nothing but alternating legs or arms does (a jump lifts both knees
    /// together: no step). In torso lengths, so a child and an adult run alike. Pure logic: tests feed it samples.
    /// </summary>
    public sealed class RunDetector
    {
        /// <summary>A step: the lifted knee this far above the other one (torso lengths); the ankles need more.</summary>
        public float KneeLift = 0.07f, AnkleLift = 0.1f, ArmSwing = 0.18f;
        /// <summary>Steps are counted over this long (s).</summary>
        public float Window = 1.1f;
        /// <summary>Metres per second for each step a second, and the fastest a runner goes.</summary>
        public float SpeedPerStep = 2.5f, MaxSpeed = 11f;

        const int History = 24;
        readonly double[] _steps = new double[History];
        int _count, _newest = -1;
        int _legSide, _armSide;
        double _lastTime, _lastLegStep = -10;

        /// <summary>Steps per second right now.</summary>
        public float Cadence { get; private set; }
        /// <summary>How fast those steps run (m/s), smoothed.</summary>
        public float Speed { get; private set; }
        public int Steps { get; private set; }

        public void Reset()
        {
            _count = 0;
            _newest = -1;
            _legSide = _armSide = 0;
            _lastLegStep = -10;
            Cadence = Speed = 0f;
            Steps = 0;
        }

        /// <summary>
        /// One camera sample: the capture time (s), the knees', ankles' and wrists' heights (view units, y up) with how
        /// sure the camera is of the legs, and the torso length.
        /// </summary>
        public void Update(double time, float kneeL, float kneeR, float ankleL, float ankleR, float legVisibility,
                           float wristL, float wristR, float torso)
        {
            if (torso < 1e-4f) return;
            float dt = _lastTime > 0 ? (float)(time - _lastTime) : 0f;
            _lastTime = time;
            if (legVisibility >= 0.3f)
            {
                float knee = (kneeL - kneeR) / torso, ankle = (ankleL - ankleR) / torso;
                int side = knee > KneeLift || ankle > AnkleLift ? 1 : knee < -KneeLift || ankle < -AnkleLift ? -1 : 0;
                if (side != 0 && side != _legSide)
                {
                    if (_legSide != 0)
                    {
                        Step(time);
                        _lastLegStep = time;
                    }
                    _legSide = side;
                }
            }
            float arm = (wristL - wristR) / torso;
            int armSide = arm > ArmSwing ? 1 : arm < -ArmSwing ? -1 : 0;
            if (armSide != 0 && armSide != _armSide)
            {
                // the arms count only when the legs aren't seen stepping (no double counting)
                if (_armSide != 0 && time - _lastLegStep > 0.6) Step(time);
                _armSide = armSide;
            }

            int recent = 0;
            double first = time, newest = _count > 0 ? _steps[_newest] : time;
            for (int k = 0; k < _count; k++)
            {
                double t = _steps[(_newest - k + History) % History];
                if (time - t > Window) break;
                recent++;
                first = t;
            }
            // steps a second: from the gaps between the recent steps, but never more than the time since the last one
            // allows (so it drops as soon as the steps stop)
            float between = recent >= 2 ? (recent - 1) / Mathf.Max(0.05f, (float)(newest - first)) : 0f;
            float sinceLast = 1.25f / Mathf.Max(0.05f, (float)(time - newest));
            Cadence = Mathf.Min(between, sinceLast);
            float target = Mathf.Min(MaxSpeed, Mathf.Max(0f, Cadence - 0.6f) * SpeedPerStep);
            float rate = target > Speed ? 3.5f : 1.6f;   // speeds up quicker than it slows down
            if (dt > 0f) Speed = Mathf.Lerp(Speed, target, 1f - Mathf.Exp(-rate * dt));
        }

        void Step(double time)
        {
            _newest = (_newest + 1) % History;
            _steps[_newest] = time;
            _count = Mathf.Min(_count + 1, History);
            Steps++;
        }
    }

    /// <summary>A quick move of a hand seen by <see cref="ArmFlick"/>.</summary>
    public struct Flick
    {
        public double Time;
        public bool RightHand;
        /// <summary>Torso lengths per second (in the picture, relative to the shoulders).</summary>
        public float Speed;
        /// <summary>Which way (x = the screen's right, y = up), unit length.</summary>
        public Vector2 Direction;
        /// <summary>Where the hand was, relative to the shoulders' middle (torso lengths; y up).</summary>
        public Vector2 At;
    }

    /// <summary>
    /// Any quick move of either hand (a throw, a bump, a spike, a serve), forgiving like the punches: the wrist moving at
    /// least <see cref="MinSpeed"/> torso lengths a second relative to the shoulders in the picture; one move, one flick
    /// (ready again once the hand slows down, or after <see cref="RepeatAfter"/>). Pure logic.
    /// </summary>
    public sealed class ArmFlick
    {
        public float MinSpeed = 3f, ReArmSpeed = 1.5f, RepeatAfter = 0.6f, Cooldown = 0.3f, MinVisibility = 0.3f;
        public float SpeedWindow = 0.08f;

        sealed class Hand
        {
            public const int History = 8;
            public readonly double[] Times = new double[History];
            public readonly Vector2[] At = new Vector2[History];
            public int Count, Newest = -1;
            public double FlickedAt = -10;
            public bool Armed = true;
            public float Speed;
        }

        readonly Hand _left = new Hand(), _right = new Hand();

        public float Speed(bool right) => (right ? _right : _left).Speed;

        public void Reset()
        {
            foreach (var h in new[] { _left, _right })
            {
                h.Count = 0;
                h.Armed = true;
                h.FlickedAt = -10;
                h.Speed = 0f;
            }
        }

        /// <summary>One camera sample (view units, y up). Returns true when a hand flicks (the faster one if both).</summary>
        public bool Update(double time, Vector2 shoulders, float torso, Vector2 wristL, float visL, Vector2 wristR, float visR, out Flick flick)
        {
            flick = default;
            if (torso < 1e-4f) return false;
            bool l = Sample(_left, time, (wristL - shoulders) / torso, visL, out var fl);
            bool r = Sample(_right, time, (wristR - shoulders) / torso, visR, out var fr);
            if (!l && !r) return false;
            flick = l && r ? (fr.Speed >= fl.Speed ? fr : fl) : r ? fr : fl;
            flick.RightHand = l && r ? fr.Speed >= fl.Speed : r;
            return true;
        }

        bool Sample(Hand h, double time, Vector2 at, float visibility, out Flick flick)
        {
            flick = default;
            if (visibility < MinVisibility) return false;
            if (h.Count > 0)
            {
                double since = time - h.Times[h.Newest];
                if (since < 0.012) return false;
                if (since > 0.25) h.Count = 0;
            }
            h.Newest = (h.Newest + 1) % Hand.History;
            h.Times[h.Newest] = time;
            h.At[h.Newest] = at;
            h.Count = Mathf.Min(h.Count + 1, Hand.History);
            int then = -1;
            for (int k = 1; k < h.Count; k++)
            {
                int i = (h.Newest - k + Hand.History) % Hand.History;
                then = i;
                if (time - h.Times[i] >= SpeedWindow) break;
            }
            if (then < 0 || time - h.Times[then] < SpeedWindow * 0.6f)
            {
                h.Speed = 0f;
                return false;
            }
            var v = (at - h.At[then]) / (float)(time - h.Times[then]);
            h.Speed = v.magnitude;
            if (!h.Armed && (h.Speed < ReArmSpeed || time - h.FlickedAt >= RepeatAfter)) h.Armed = true;
            if (!h.Armed || time - h.FlickedAt < Cooldown || h.Speed < MinSpeed) return false;
            h.Armed = false;
            h.FlickedAt = time;
            flick = new Flick { Time = time, Speed = h.Speed, Direction = v / h.Speed, At = at };
            return true;
        }
    }

    /// <summary>A kick seen by <see cref="KickDetector"/>.</summary>
    public struct Kick
    {
        public double Time;
        public bool RightFoot;
        /// <summary>Torso lengths per second.</summary>
        public float Speed;
        /// <summary>Which way the foot went (x = the screen's right, y = up), unit length.</summary>
        public Vector2 Direction;
    }

    /// <summary>
    /// A kick (Kinect Sports' football) from a front camera: one ankle moving fast relative to the hips (up and out, or
    /// across: a kick at the TV lifts the foot in the picture) while the other foot stays down: a jump lifts both, so it
    /// is no kick. One kick per move. Pure logic.
    /// </summary>
    public sealed class KickDetector
    {
        public float MinSpeed = 2.4f, ReArmSpeed = 1.2f, Cooldown = 0.5f, MinVisibility = 0.3f;
        /// <summary>The standing foot may rise at most this fast (torso lengths/s): faster = a jump.</summary>
        public float StandingFootMax = 1.2f;

        readonly ArmFlick _feet = new ArmFlick { MinSpeed = 2.4f, ReArmSpeed = 1.2f, Cooldown = 0.5f, RepeatAfter = 0.8f };

        public void Reset() => _feet.Reset();

        /// <summary>One camera sample (view units, y up): the hips' middle, the torso length, the ankles.</summary>
        public bool Update(double time, Vector2 hips, float torso, Vector2 ankleL, float visL, Vector2 ankleR, float visR, out Kick kick)
        {
            kick = default;
            _feet.MinSpeed = MinSpeed;
            _feet.ReArmSpeed = ReArmSpeed;
            _feet.Cooldown = Cooldown;
            if (!_feet.Update(time, hips, torso, ankleL, visL, ankleR, visR, out var f)) return false;
            // the other foot must stay on the ground (both feet fast = a jump or a stumble)
            float other = _feet.Speed(!f.RightHand);
            if (other > StandingFootMax && other > f.Speed * 0.6f) return false;
            if (f.Direction.y < -0.6f) return false;   // a foot coming down (landing) is no kick
            kick = new Kick { Time = f.Time, RightFoot = f.RightHand, Speed = f.Speed, Direction = f.Direction };
            return true;
        }
    }

    /// <summary>
    /// A lean at the finish line (the sprinter's dip) from a front camera: the head drops toward the hips (bending
    /// forward), at least <see cref="Drop"/> torso lengths below where it has been over the last moments. Running on the
    /// spot bobs head and hips together, so it is no lean. One lean, then it must come back up. Pure logic.
    /// </summary>
    public sealed class DipDetector
    {
        public float Drop = 0.24f, Recover = 0.1f;
        float _top;
        bool _has, _armed = true;
        double _last;

        public void Reset()
        {
            _has = false;
            _armed = true;
        }

        /// <summary>One camera sample (view units, y up): the nose's and the hips' heights and the torso length.</summary>
        public bool Update(double time, float noseY, float hipY, float torso)
        {
            if (torso < 1e-4f) return false;
            float h = (noseY - hipY) / torso;
            float dt = _has ? (float)(time - _last) : 0f;
            _last = time;
            if (!_has)
            {
                _top = h;
                _has = true;
                return false;
            }
            // the reference: the highest the head has been lately, sinking slowly
            _top = Mathf.Max(h, _top - dt * 0.12f);
            float down = _top - h;
            if (!_armed && down < Recover) _armed = true;
            if (!_armed || down < Drop) return false;
            _armed = false;
            return true;
        }
    }
}
