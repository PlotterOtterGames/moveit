using UnityEngine;

namespace MoveIt.Games.Space
{
    /// <summary>One camera sample of a player, as <see cref="SpaceMoves"/> reads it (view space: mirrored like a mirror,
    /// x to the screen's right = the player's right, y up; raw, unfiltered).</summary>
    public struct SpaceBodySample
    {
        public Vector2 LeftShoulder, RightShoulder, LeftWrist, RightWrist, LeftHip, RightHip, Nose;
        public float LeftWristVisibility, RightWristVisibility, HipVisibility, NoseVisibility;
        /// <summary>Shoulders' middle to hips' middle (view units).</summary>
        public float Torso;
    }

    /// <summary>
    /// Reads Yjet!'s flying from a front camera (2026-11-14, the exploration rework; the steering of the first Yjet!,
    /// after Freefall Racers' squirrels and Kinect's Peter Pan flight, without what didn't work there: nothing toward or
    /// away from the camera, no pose to hold all the time).
    /// <list type="bullet">
    /// <item><b>Steering</b> (<see cref="Steer"/>): the whole body, where it stands and how it leans (the shoulders' and
    /// hips' middle against where it stood, in torso lengths, and the shoulders' tilt; the stronger of the two). A dead
    /// zone, a gentle curve, full at <see cref="FullSide"/>: a lean turns gently, a step turns hard.</item>
    /// <item><b>Boost</b>: both wrists above the nose (after two samples; it ends after three without).</item>
    /// <item><b>Slowing down</b>: ducking (the torso's middle below standing, on at <see cref="DuckOn"/>, off below
    /// <see cref="DuckOff"/>).</item>
    /// </list>
    /// In torso lengths, so the same numbers fit a child and an adult at any distance; the middle is set when flying
    /// starts (<see cref="Calibrate"/>) and the standing height follows slow changes. Pure logic, tested.
    /// </summary>
    public sealed class SpaceMoves
    {
        public float FullSide = 0.6f, SideDead = 0.06f, LeanDead = 3f, LeanFull = 22f, LeanShare = 0.8f, Curve = 1.15f;
        public float RocketAboveNose = 0.05f, Visibility = 0.3f;
        public float DuckOn = 0.2f, DuckOff = 0.12f;

        public float Steer { get; private set; }
        public bool Boosting { get; private set; }
        public bool Ducking { get; private set; }
        /// <summary>The body's middle against where it stood (torso lengths, + = right).</summary>
        public float Lateral { get; private set; }
        public float Lean { get; private set; }

        const float MinInterval = 0.012f;
        double _last;
        bool _based;
        float _baseX, _baseY, _baseTorso, _neutralLean, _lastX;
        int _upCount, _downCount;

        public void Reset()
        {
            _last = 0;
            _based = false;
            _upCount = _downCount = 0;
            Steer = Lean = Lateral = 0f;
            Boosting = Ducking = false;
        }

        /// <summary>Where the body is now becomes the middle; the lean as it is becomes level (within 10 degrees).</summary>
        public void Calibrate()
        {
            if (!_based) return;
            _baseX = _lastX;
            _neutralLean = Mathf.Clamp(_neutralLean + Lean, -10f, 10f);
            Lean = 0f;
            Lateral = 0f;
            Steer = 0f;
        }

        public void Update(double time, in SpaceBodySample b)
        {
            float torso = b.Torso;
            if (torso < 1e-4f) return;
            float dt = _last > 0 ? (float)(time - _last) : 0f;
            if (_last > 0 && dt < MinInterval) return;
            _last = time;
            if (dt > 0.5f) dt = 0f;
            var shoulders = (b.LeftShoulder + b.RightShoulder) * 0.5f;
            var hips = (b.LeftHip + b.RightHip) * 0.5f;
            bool hipsSeen = b.HipVisibility >= Visibility;
            float x = hipsSeen ? (shoulders.x + hips.x) * 0.5f : shoulders.x;
            float y = hipsSeen ? (shoulders.y + hips.y) * 0.5f : shoulders.y;
            _lastX = x;
            if (!_based)
            {
                _based = true;
                _baseX = x;
                _baseY = y;
                _baseTorso = torso;
            }
            float unit = Mathf.Max(1e-4f, _baseTorso);

            // ---- steering: the body's place and lean, the stronger of the two
            Lateral = (x - _baseX) / unit;
            float tilt = Mathf.Atan2(b.LeftShoulder.y - b.RightShoulder.y, Mathf.Max(1e-4f, b.RightShoulder.x - b.LeftShoulder.x)) * Mathf.Rad2Deg;
            Lean = Mathf.Lerp(Lean, tilt - _neutralLean, 0.5f);
            float side = Shaped(Lateral, SideDead, FullSide);
            float lean = Shaped(Lean, LeanDead, LeanFull) * LeanShare;
            Steer = Mathf.Clamp(Mathf.Abs(side) >= Mathf.Abs(lean) ? side : lean, -1f, 1f);

            // ---- boost: both wrists above the nose (two samples on, three off: one frame's glitch changes nothing)
            bool seen = b.LeftWristVisibility >= Visibility && b.RightWristVisibility >= Visibility;
            float noseLine = b.NoseVisibility >= Visibility ? (b.Nose.y - shoulders.y) / torso + RocketAboveNose : 0.55f;
            bool up = seen && (b.LeftWrist.y - shoulders.y) / torso > noseLine && (b.RightWrist.y - shoulders.y) / torso > noseLine;
            if (up)
            {
                _upCount++;
                _downCount = 0;
                if (_upCount >= 2) Boosting = true;
            }
            else
            {
                _downCount++;
                _upCount = 0;
                if (_downCount >= 3) Boosting = false;
            }

            // ---- ducking: the torso's middle below standing
            float offset = (y - _baseY) / unit;
            if (!Ducking && -offset >= DuckOn) Ducking = true;
            else if (Ducking && -offset < DuckOff) Ducking = false;
            // the standing height follows slow changes (stepping closer), never a duck
            if (dt > 0f && Mathf.Abs(offset) < 0.1f)
            {
                float k = 0.25f * dt;
                _baseY = Mathf.Lerp(_baseY, y, k);
                _baseTorso = Mathf.Lerp(_baseTorso, torso, k);
            }
        }

        float Shaped(float v, float dead, float full)
        {
            float a = Mathf.Abs(v);
            if (a <= dead) return 0f;
            float k = Mathf.Clamp01((a - dead) / (full - dead));
            return Mathf.Sign(v) * Mathf.Pow(k, Curve);
        }
    }
}
