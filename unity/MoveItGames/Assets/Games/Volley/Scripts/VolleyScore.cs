using System;

namespace MoveIt.Games.Volley
{
    /// <summary>
    /// Rally scoring (beach volleyball's): every rally is a point, whoever wins it serves next. A match is one set: first
    /// to <see cref="Target"/> points with two clear, at most <see cref="Cap"/> (7 points: at 9-9 the next point wins, so a
    /// close set can't go on and on with children). Pure logic.
    /// </summary>
    public sealed class VolleyScore
    {
        public readonly int Target, Cap;
        readonly int[] _points = new int[2];

        /// <summary>The team serving the next rally.</summary>
        public int Server { get; private set; }
        public int Points(int team) => _points[team];
        public int Winner { get; private set; } = -1;
        public bool MatchOver => Winner >= 0;

        public VolleyScore(int target = 7, int firstServer = 0)
        {
            Target = Math.Max(1, target);
            Cap = Target + 3;
            Server = firstServer;
        }

        /// <summary>A rally won by <paramref name="team"/>: its point, its serve; true when it wins the match.</summary>
        public bool PointTo(int team)
        {
            if (MatchOver) return true;
            _points[team]++;
            Server = team;
            if (Wins(_points[team], _points[1 - team])) Winner = team;
            return MatchOver;
        }

        bool Wins(int a, int b) => (a >= Target && a - b >= 2) || a >= Cap;

        /// <summary><paramref name="team"/> wins the match with its next point.</summary>
        public bool MatchPoint(int team) => !MatchOver && Wins(_points[team] + 1, _points[1 - team]);
    }
}
