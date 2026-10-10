using System.Collections.Generic;

namespace MoveIt.Games.Bowling
{
    /// <summary>
    /// Ten-pin scoring for one player, with any number of frames (5 for a quick family game, 10 for a full one).
    /// Strike = 10 + next two rolls, spare = 10 + next roll. The last frame gets bonus rolls after a strike/spare.
    /// </summary>
    public sealed class BowlingScore
    {
        public const int Pins = 10;

        readonly List<List<int>> _frames = new List<List<int>>();

        public BowlingScore(int frames = 10)
        {
            FrameCount = frames < 1 ? 1 : frames;
            _frames.Add(new List<int>());
        }

        public int FrameCount { get; }
        public bool IsComplete { get; private set; }

        /// <summary>0-based index of the frame being played (or the last frame once complete).</summary>
        public int CurrentFrame => _frames.Count - 1;

        bool IsLastFrame(int frame) => frame == FrameCount - 1;

        IReadOnlyList<int> Current => _frames[CurrentFrame];

        /// <summary>Pins standing before the next roll: 10 after a fresh rack, otherwise what the last roll left.</summary>
        public int PinsStanding
        {
            get
            {
                if (IsComplete || NeedsFullRack) return Pins;
                var f = Current;
                int sinceRack = f[f.Count - 1];
                if (IsLastFrame(CurrentFrame) && f.Count == 2 && f[0] == Pins) sinceRack = f[1];
                return Pins - sinceRack;
            }
        }

        /// <summary>True if all 10 pins must be set up before the next roll.</summary>
        public bool NeedsFullRack
        {
            get
            {
                if (IsComplete) return true;
                var f = Current;
                if (f.Count == 0) return true;
                if (!IsLastFrame(CurrentFrame)) return false;
                if (f.Count == 1) return f[0] == Pins;
                // Second roll of the last frame done: new rack after strike+strike or after a spare.
                return (f[0] == Pins && f[1] == Pins) || (f[0] < Pins && f[0] + f[1] == Pins);
            }
        }

        /// <summary>Record a roll. Returns true if this roll finished the player's frame (their turn is over).</summary>
        public bool Roll(int knocked)
        {
            if (IsComplete) return true;
            if (knocked < 0) knocked = 0;
            if (knocked > PinsStanding) knocked = PinsStanding;

            int frame = CurrentFrame;
            var f = _frames[frame];
            f.Add(knocked);

            bool frameDone;
            if (!IsLastFrame(frame)) frameDone = f[0] == Pins || f.Count == 2;
            else frameDone = f.Count == 3 || (f.Count == 2 && f[0] + f[1] < Pins);

            if (!frameDone) return false;
            if (IsLastFrame(frame)) IsComplete = true;
            else _frames.Add(new List<int>());
            return true;
        }

        /// <summary>All rolls of a frame (empty if not played yet).</summary>
        public IReadOnlyList<int> FrameRolls(int frame) => frame < _frames.Count ? _frames[frame] : (IReadOnlyList<int>)new int[0];

        /// <summary>Running total after each frame, or null where it can't be known yet (waiting for bonus rolls).</summary>
        public int?[] CumulativeScores()
        {
            var rolls = new List<int>();
            var starts = new int[_frames.Count];
            for (int i = 0; i < _frames.Count; i++)
            {
                starts[i] = rolls.Count;
                rolls.AddRange(_frames[i]);
            }

            var result = new int?[FrameCount];
            int total = 0;
            for (int i = 0; i < _frames.Count; i++)
            {
                var f = _frames[i];
                int s = starts[i];
                int? frameScore = null;
                if (IsLastFrame(i))
                {
                    if (IsComplete) frameScore = Sum(f);
                }
                else if (f.Count >= 1 && f[0] == Pins)
                {
                    if (rolls.Count >= s + 3) frameScore = Pins + rolls[s + 1] + rolls[s + 2];
                }
                else if (f.Count == 2 && f[0] + f[1] == Pins)
                {
                    if (rolls.Count >= s + 3) frameScore = Pins + rolls[s + 2];
                }
                else if (f.Count == 2) frameScore = f[0] + f[1];

                if (frameScore == null) break;
                total += frameScore.Value;
                result[i] = total;
            }
            return result;
        }

        /// <summary>Score so far (only frames whose score is known).</summary>
        public int Total
        {
            get
            {
                int total = 0;
                foreach (var s in CumulativeScores())
                    if (s.HasValue) total = s.Value;
                return total;
            }
        }

        /// <summary>Scoreboard marks for a frame: "X" strike, "/" spare, "-" miss, digits otherwise.</summary>
        public string Marks(int frame)
        {
            var f = FrameRolls(frame);
            var marks = new System.Text.StringBuilder();
            int rackStart = 0;
            for (int i = 0; i < f.Count; i++)
            {
                bool freshRack = i == rackStart;
                if (freshRack && f[i] == Pins)
                {
                    marks.Append('X');
                    rackStart = i + 1;
                }
                else if (!freshRack && f[i - 1] + f[i] == Pins)
                {
                    marks.Append('/');
                    rackStart = i + 1;
                }
                else
                {
                    marks.Append(f[i] == 0 ? '-' : (char)('0' + f[i]));
                    if (!freshRack) rackStart = i + 1;
                }
                if (i < f.Count - 1) marks.Append(' ');
            }
            return marks.ToString();
        }

        static int Sum(List<int> values)
        {
            int s = 0;
            foreach (int v in values) s += v;
            return s;
        }
    }
}
