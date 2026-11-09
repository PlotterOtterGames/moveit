using System.Collections.Generic;
using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games.Kart
{
    /// <summary>A move of the player's that Gara! reacts to.</summary>
    public enum KartMove
    {
        /// <summary>A jump (or the wheel lifted quickly with both hands, sitting down): a trick at a ramp, a hop
        /// elsewhere, a quick start at "go".</summary>
        Hop,
        /// <summary>Both hands raised above the head: the item.</summary>
        HandsUp,
    }

    public struct KartMoveEvent
    {
        public KartMove Move;
        /// <summary>Capture time (the camera's clock).</summary>
        public double Time;
        /// <summary>A hop: the whole body jumped (else the wheel was lifted).</summary>
        public bool Jumped;
    }

    /// <summary>One camera sample of a player, as <see cref="KartMoves"/> reads it (view space: mirrored like a mirror,
    /// x to the screen's right = the player's right, y up; raw, unfiltered).</summary>
    public struct KartBodySample
    {
        public Vector2 LeftShoulder, RightShoulder, LeftWrist, RightWrist, LeftHip, RightHip, Nose;
        public float LeftWristVisibility, RightWristVisibility, HipVisibility, NoseVisibility;
        /// <summary>Shoulders' middle to hips' middle (view units).</summary>
        public float Torso;
    }

    /// <summary>
    /// Reads Gara!'s moves from a front camera (2026-11-08): the <b>invisible steering wheel</b> (Nex Playground's Miniacs,
    /// Kinect Joy Ride, Rush's Cars levels, Forza 4's Kinect mode), a <b>jump</b> and <b>both hands up</b>.
    /// <list type="bullet">
    /// <item><b>The wheel</b> is the line between the two wrists: the left hand higher = the wheel turned clockwise =
    /// right (<see cref="WheelAngle"/>, degrees). Only the wrists' heights and their distance across matter, which the
    /// pose model gives best; a sample that turns the wheel more than <see cref="GlitchJump"/> degrees at once is held for
    /// a sample (a hand's one-frame glitch, measured on the box) and taken only if the next one agrees; then a light One
    /// Euro filter. A dead zone (<see cref="DeadZone"/>) and a full turn at <see cref="FullTurn"/> degrees make
    /// <see cref="Steer"/>. The player holds a wheel (<see cref="Holding"/>) while both wrists are up in front of the
    /// body (above the belly, not above the head) and neither too close nor spread wide.</item>
    /// <item><b>No wheel</b> (the hands down): the lean steers (the shoulders' tilt and the torso leaning, full at
    /// <see cref="LeanFull"/> degrees), so a child who lets go still drives; the game asks them to hold the wheel.</item>
    /// <item><b>A jump</b> is <see cref="BodyGestures"/>' rule on the torso's middle (a jump from a knee dip is a jump,
    /// at take-off), or, sitting down, both hands lifting the wheel quickly (<see cref="LiftRise"/> torso lengths within
    /// 0.3 s, both hands: one hand's glitch never does it).</item>
    /// <item><b>Both hands up</b>: both wrists above the nose for <see cref="UpSamples"/> samples (once per raise).</item>
    /// </list>
    /// The neutral wheel and lean are set at "go" (<see cref="Calibrate"/>): people hold an invisible wheel a little
    /// tilted. In torso lengths, so the same numbers fit a child and an adult at any distance. Pure logic, tested
    /// (KartMovesTests).
    /// </summary>
    public sealed class KartMoves
    {
        public float DeadZone = 4f, FullTurn = 60f, Curve = 1.15f, MaxAngle = 110f;
        /// <summary>Holding a wheel: both wrists at least this sure, their middle at least this high against the shoulders
        /// (torso lengths, - = below: the belly), at most <see cref="HoldTop"/> above them, between
        /// <see cref="HoldApartMin"/> and <see cref="HoldApartMax"/> apart across.</summary>
        public float HoldVisibility = 0.3f, HoldLow = -0.8f, HoldTop = 0.45f, HoldApartMin = 0.15f, HoldApartMax = 1.8f;
        /// <summary>The wheel turning more than this at once (degrees in one sample) is held a sample: a glitch unless the
        /// next sample agrees.</summary>
        public float GlitchJump = 25f;
        public float LeanDead = 3f, LeanFull = 20f;
        /// <summary>The wheel lifted (sitting): both hands rise this much (torso lengths) within <see cref="LiftWindow"/>
        /// s, each at least <see cref="LiftEach"/>.</summary>
        public float LiftRise = 0.24f, LiftEach = 0.16f, LiftWindow = 0.3f;
        public int UpSamples = 3;
        public float UpAboveNose = 0.1f;

        /// <summary>The wheel's angle (degrees, + = turned clockwise: right), filtered and less the neutral.</summary>
        public float WheelAngle { get; private set; }
        /// <summary>The lean (degrees, + = to the right), less the neutral.</summary>
        public float Lean { get; private set; }
        /// <summary>-1 = full left .. 1 = full right: the wheel's while holding one, else the lean's.</summary>
        public float Steer { get; private set; }
        public bool Holding { get; private set; }
        /// <summary>Wheel samples taken for glitches and held (tests, logs).</summary>
        public int Ignored { get; private set; }
        public int Hops { get; private set; }

        /// <summary>Samples closer together than this (s) are skipped (a burst after a stall: <c>Body.MinSpeedInterval</c>).</summary>
        const float MinInterval = 0.012f;

        readonly OneEuroFilter _filter = new OneEuroFilter(2.5f, 0.02f, 1.2f);
        float _raw;
        bool _hadAngle, _suspect;
        float _suspectAngle;
        double _last;
        float _neutralWheel, _neutralLean;
        int _holdOn, _holdOff;
        float _blend;   // 1 = the wheel steers, 0 = the lean
        // the jump (BodyGestures' rule)
        bool _based;
        float _baseY, _baseTorso, _prevCentre, _vSpeed;
        bool _rising, _jumping;
        float _riseStart;
        double _lastJump = -10;
        // the wheel lift
        readonly List<(double t, float l, float r)> _hands = new List<(double, float, float)>();
        double _lastLift = -10;
        // hands up
        int _upCount;
        bool _upLatched;

        public void Reset()
        {
            _filter.Reset();
            _raw = 0f;
            _hadAngle = _suspect = false;
            _last = 0;
            _holdOn = _holdOff = 0;
            Holding = false;
            _blend = 0f;
            _based = false;
            _rising = _jumping = false;
            _lastJump = _lastLift = -10;
            _hands.Clear();
            _upCount = 0;
            _upLatched = false;
            WheelAngle = Lean = Steer = 0f;
        }

        /// <summary>The wheel and the lean as they are now become level (at "go"; within 15 degrees: a wheel held tilted
        /// further than that is a turn).</summary>
        public void Calibrate()
        {
            _neutralWheel = Holding ? Mathf.Clamp(_neutralWheel + WheelAngle, -15f, 15f) : _neutralWheel;
            _neutralLean = Mathf.Clamp(_neutralLean + Lean, -10f, 10f);
            WheelAngle = 0f;
            Lean = 0f;
        }

        public void ClearCalibration()
        {
            _neutralWheel = _neutralLean = 0f;
        }

        /// <summary>One camera sample (once per sample, its capture time): adds what it saw to <paramref name="seen"/>
        /// (a hop, hands up) and updates <see cref="Steer"/>.</summary>
        public void Update(double time, in KartBodySample b, List<KartMoveEvent> seen)
        {
            float torso = b.Torso;
            if (torso < 1e-4f) return;
            float dt = _last > 0 ? (float)(time - _last) : 0f;
            if (_last > 0 && dt < MinInterval) return;
            _last = time;
            if (dt > 0.5f) dt = 0f;   // a gap: start the speeds over

            var shoulders = (b.LeftShoulder + b.RightShoulder) * 0.5f;
            var l = (b.LeftWrist - shoulders) / torso;
            var r = (b.RightWrist - shoulders) / torso;
            bool seenHands = b.LeftWristVisibility >= HoldVisibility && b.RightWristVisibility >= HoldVisibility;

            // ---- the wheel
            bool up = seenHands && l.y > HoldTop && r.y > HoldTop;
            float mid = (l.y + r.y) * 0.5f, apart = Mathf.Abs(r.x - l.x);
            bool holdNow = seenHands && !up && mid >= HoldLow && apart >= HoldApartMin && apart <= HoldApartMax &&
                           Vector2.Distance(l, r) >= HoldApartMin;
            if (holdNow)
            {
                _holdOff = 0;
                if (++_holdOn >= 2) Holding = true;
            }
            else
            {
                _holdOn = 0;
                if (++_holdOff >= 4) Holding = false;
            }
            if (seenHands && Vector2.Distance(l, r) >= HoldApartMin)
            {
                float angle = Mathf.Clamp(Mathf.Atan2(l.y - r.y, r.x - l.x) * Mathf.Rad2Deg, -MaxAngle, MaxAngle);
                // a big change at once is held a sample: the next sample agreeing = a real turn (a sample late), else the
                // held one was a glitch and is dropped
                float accepted;
                if (_suspect)
                {
                    _suspect = false;
                    if (Mathf.Abs(angle - _suspectAngle) < 20f) accepted = angle;
                    else
                    {
                        Ignored++;
                        if (Mathf.Abs(angle - _raw) <= GlitchJump) accepted = angle;
                        else
                        {
                            _suspect = true;
                            _suspectAngle = angle;
                            accepted = _raw;
                        }
                    }
                }
                else if (_hadAngle && Mathf.Abs(angle - _raw) > GlitchJump * Mathf.Max(1f, dt * 30f))
                {
                    _suspect = true;
                    _suspectAngle = angle;
                    accepted = _raw;
                }
                else accepted = angle;
                _raw = accepted;
                _hadAngle = true;
                float filtered = _filter.Filter(_raw, Mathf.Max(dt, 1f / 60f));
                WheelAngle = filtered - _neutralWheel;
            }

            // ---- the lean (the hands down)
            var hips = (b.LeftHip + b.RightHip) * 0.5f;
            float tilt = Mathf.Atan2(b.LeftShoulder.y - b.RightShoulder.y, Mathf.Max(1e-4f, b.RightShoulder.x - b.LeftShoulder.x)) * Mathf.Rad2Deg;
            float body = b.HipVisibility >= 0.3f ? Mathf.Atan2(shoulders.x - hips.x, Mathf.Max(1e-4f, shoulders.y - hips.y)) * Mathf.Rad2Deg : 0f;
            Lean = Mathf.Lerp(Lean, tilt + body - _neutralLean, 0.5f);

            // ---- steer: the wheel while held, else the lean (eased across)
            _blend = Mathf.MoveTowards(_blend, Holding ? 1f : 0f, Mathf.Max(dt, 1f / 30f) / 0.2f);
            float wheel = Shape(WheelAngle, DeadZone, FullTurn);
            float lean = Shape(Lean, LeanDead, LeanFull);
            Steer = Mathf.Lerp(lean, wheel, _blend);

            // ---- a jump of the whole body
            float centre = b.HipVisibility >= 0.3f ? (shoulders.y + hips.y) * 0.5f : shoulders.y;
            if (!_based)
            {
                _based = true;
                _baseY = centre;
                _baseTorso = torso;
                _prevCentre = centre;
            }
            else if (dt > 0f)
            {
                float offset = (centre - _baseY) / _baseTorso;
                _vSpeed = Mathf.Lerp(_vSpeed, (centre - _prevCentre) / dt / _baseTorso, 0.7f);
                bool risingNow = _vSpeed > 0.3f;
                if (risingNow && !_rising) _riseStart = offset;
                _rising = risingNow;
                bool fromDip = _riseStart < -0.1f;
                bool canJump = !_jumping && time - _lastJump > 0.35;
                bool takeoff = offset > 0.06f && _vSpeed > (fromDip ? 2.2f : 1.6f);
                bool high = !fromDip && offset > 0.2f && _vSpeed > 0.9f;
                if (canJump && (takeoff || high))
                {
                    _jumping = true;
                    _lastJump = time;
                    Hops++;
                    seen?.Add(new KartMoveEvent { Move = KartMove.Hop, Time = time, Jumped = true });
                }
                else if (_jumping && offset < 0.08f && time - _lastJump > 0.15) _jumping = false;
                if (!_jumping && Mathf.Abs(_vSpeed) < 0.3f && Mathf.Abs(offset) < 0.15f)
                {
                    float k = 0.25f * dt;
                    _baseY = Mathf.Lerp(_baseY, centre, k);
                    _baseTorso = Mathf.Lerp(_baseTorso, torso, k);
                }
                _prevCentre = centre;
            }

            // ---- the wheel lifted (sitting down): both hands up quickly, together
            if (seenHands)
            {
                _hands.Add((time, l.y, r.y));
                while (_hands.Count > 0 && time - _hands[0].t > LiftWindow) _hands.RemoveAt(0);
                if (_hands.Count >= 3 && time - _lastLift > 0.45 && time - _lastJump > 0.45 && !_jumping)
                {
                    float minL = float.MaxValue, minR = float.MaxValue;
                    foreach (var h in _hands)
                    {
                        minL = Mathf.Min(minL, h.l);
                        minR = Mathf.Min(minR, h.r);
                    }
                    var prev = _hands[_hands.Count - 2];
                    bool bothRising = l.y > prev.l && r.y > prev.r;
                    if (bothRising && l.y - minL >= LiftEach && r.y - minR >= LiftEach && (l.y + r.y - minL - minR) * 0.5f >= LiftRise)
                    {
                        _lastLift = time;
                        Hops++;
                        seen?.Add(new KartMoveEvent { Move = KartMove.Hop, Time = time, Jumped = false });
                    }
                }
            }
            else _hands.Clear();

            // ---- both hands up (the item), once per raise
            float noseLine = b.NoseVisibility >= 0.3f ? (b.Nose.y - shoulders.y) / torso + UpAboveNose : 0.55f;
            bool handsUp = seenHands && l.y > noseLine && r.y > noseLine;
            if (handsUp)
            {
                if (++_upCount >= UpSamples && !_upLatched)
                {
                    _upLatched = true;
                    seen?.Add(new KartMoveEvent { Move = KartMove.HandsUp, Time = time });
                }
            }
            else
            {
                _upCount = 0;
                if (l.y < 0.1f && r.y < 0.1f) _upLatched = false;
            }
        }

        /// <summary>A dead zone, a full value and a gentle curve (fine control near the middle).</summary>
        public float Shape(float angle, float dead, float full)
        {
            float a = Mathf.Abs(angle);
            if (a <= dead) return 0f;
            float k = Mathf.Clamp01((a - dead) / (full - dead));
            return Mathf.Sign(angle) * Mathf.Pow(k, Curve);
        }
    }
}
