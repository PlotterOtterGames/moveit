using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>One camera sample of a player, as <see cref="GardenMoves"/> reads it (view space: mirrored like a mirror,
    /// x to the screen's right = the player's right, y up; raw, unfiltered).</summary>
    public struct FairyBodySample
    {
        public Vector2 LeftShoulder, RightShoulder, LeftWrist, RightWrist, LeftHip, RightHip, Nose;
        public float LeftWristVisibility, RightWristVisibility, HipVisibility, NoseVisibility;
        /// <summary>Shoulders' middle to hips' middle (view units).</summary>
        public float Torso;
    }

    /// <summary>
    /// The body's own moves in Zana!'s garden (the hands point the wand: <see cref="Wand"/>).
    /// <list type="bullet">
    /// <item><b>Both arms up</b> (<see cref="ArmsUp"/>): both wrists above the nose for two samples (three without, to
    /// end): the fairy's transformation after every fifth wave.</item>
    /// <item><b>The lean</b> (<see cref="Lean"/>, degrees of the shoulders' line).</item>
    /// </list>
    /// The wrists go through a median of three (the pose model's one-frame wrist jumps never get through), in torso
    /// lengths round the shoulders' middle, so the same numbers fit a child and an adult at any distance. Pure logic,
    /// tested on simulated arms. (Until 2026-11-16 also a clap, for the seeds: the spells are traced with the wand now.)
    /// </summary>
    public sealed class GardenMoves
    {
        public float Visibility = 0.3f, AboveNose = 0.05f;

        public bool ArmsUp { get; private set; }
        public float Lean { get; private set; }
        /// <summary>The wrists round the shoulders' middle (torso lengths, filtered), and whether each was seen.</summary>
        public Vector2 Left { get; private set; }
        public Vector2 Right { get; private set; }
        public bool LeftSeen { get; private set; }
        public bool RightSeen { get; private set; }

        const float MinInterval = 0.012f;
        readonly Vector2[] _ml = new Vector2[3], _mr = new Vector2[3];
        int _mn;
        double _last;
        int _upCount, _downCount;

        public void Reset()
        {
            _mn = 0;
            _last = 0;
            _upCount = _downCount = 0;
            ArmsUp = false;
            Lean = 0f;
        }

        public void Update(double time, in FairyBodySample b)
        {
            float torso = b.Torso;
            if (torso < 1e-4f) return;
            if (_last > 0 && time - _last < MinInterval) return;
            _last = time;
            var shoulders = (b.LeftShoulder + b.RightShoulder) * 0.5f;
            float tilt = Mathf.Atan2(b.LeftShoulder.y - b.RightShoulder.y, Mathf.Max(1e-4f, b.RightShoulder.x - b.LeftShoulder.x)) * Mathf.Rad2Deg;
            Lean = Mathf.Lerp(Lean, tilt, 0.4f);
            LeftSeen = b.LeftWristVisibility >= Visibility;
            RightSeen = b.RightWristVisibility >= Visibility;
            // the wrists: a median of three samples
            _ml[0] = _ml[1];
            _ml[1] = _ml[2];
            _ml[2] = (b.LeftWrist - shoulders) / torso;
            _mr[0] = _mr[1];
            _mr[1] = _mr[2];
            _mr[2] = (b.RightWrist - shoulders) / torso;
            if (_mn < 3) _mn++;
            if (_mn < 3) return;
            Left = Median(_ml);
            Right = Median(_mr);
            // ---- both arms up
            float noseLine = b.NoseVisibility >= Visibility ? (b.Nose.y - shoulders.y) / torso + AboveNose : 0.55f;
            bool up = LeftSeen && RightSeen && Left.y > noseLine && Right.y > noseLine;
            if (up)
            {
                _upCount++;
                _downCount = 0;
                if (_upCount >= 2) ArmsUp = true;
            }
            else
            {
                _downCount++;
                _upCount = 0;
                if (_downCount >= 3) ArmsUp = false;
            }
        }

        static Vector2 Median(Vector2[] v) => new Vector2(Median3(v[0].x, v[1].x, v[2].x), Median3(v[0].y, v[1].y, v[2].y));

        static float Median3(float a, float b, float c) => Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
    }
}
