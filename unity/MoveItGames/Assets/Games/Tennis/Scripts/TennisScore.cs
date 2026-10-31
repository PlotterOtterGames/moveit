namespace MoveIt.Games.Tennis
{
    /// <summary>What a point did to the score (for the umpire's call).</summary>
    public enum ScoreCall
    {
        /// <summary>A plain score: "15 - 0", "30 - 40".</summary>
        Points,
        /// <summary>40 - 40, or back to it from an advantage.</summary>
        Deuce,
        /// <summary>One point ahead after deuce.</summary>
        Advantage,
        /// <summary>The point won the game.</summary>
        Game,
        /// <summary>The point won the match.</summary>
        Match,
    }

    /// <summary>
    /// Tennis scoring for a match of games, like Wii Sports (1, 3 or 5 games, no sets, no tie-break): points go 0, 15,
    /// 30, 40, a game is won with at least four points and two ahead (40 - 40 is deuce, then advantage), the match by
    /// the first team to win <see cref="GamesToWin"/> games. The serve changes sides of the court after every point
    /// (deuce court first) and goes to the other team after every game. Team 0 is the player's. Pure logic, tested.
    /// </summary>
    public sealed class TennisScore
    {
        public readonly int GamesToWin;
        readonly int[] _points = new int[2];
        readonly int[] _games = new int[2];

        /// <summary>The team serving the current game.</summary>
        public int Server { get; private set; }
        /// <summary>The winner of the match, or -1 while it goes on.</summary>
        public int Winner { get; private set; } = -1;
        public bool MatchOver => Winner >= 0;
        /// <summary>Points played in the current game.</summary>
        public int PointsInGame => _points[0] + _points[1];
        /// <summary>The server serves from the right half of the court (the deuce court) after an even number of points.</summary>
        public bool DeuceCourt => PointsInGame % 2 == 0;
        public int Points(int team) => _points[team];
        public int Games(int team) => _games[team];
        /// <summary>Games played so far (the game being played is number GamesPlayed + 1).</summary>
        public int GamesPlayed => _games[0] + _games[1];

        public TennisScore(int gamesToWin = 2, int firstServer = 0)
        {
            GamesToWin = System.Math.Max(1, gamesToWin);
            Server = firstServer;
        }

        /// <summary>40 - 40 (or more, level).</summary>
        public bool IsDeuce => _points[0] >= 3 && _points[0] == _points[1];

        /// <summary>The team one point ahead after deuce, or -1.</summary>
        public int Advantage =>
            _points[0] >= 3 && _points[1] >= 3 && System.Math.Abs(_points[0] - _points[1]) == 1 ? (_points[0] > _points[1] ? 0 : 1) : -1;

        /// <summary>The team wins the game with its next point.</summary>
        public bool GamePoint(int team)
        {
            int mine = _points[team], theirs = _points[1 - team];
            return mine >= 3 && mine - theirs >= 1;
        }

        /// <summary>The team wins the match with its next point.</summary>
        public bool MatchPoint(int team) => !MatchOver && GamePoint(team) && _games[team] == GamesToWin - 1;

        /// <summary>The point to <paramref name="team"/>. Returns the call.</summary>
        public ScoreCall PointTo(int team)
        {
            if (MatchOver) return ScoreCall.Match;
            _points[team]++;
            int mine = _points[team], theirs = _points[1 - team];
            if (mine >= 4 && mine - theirs >= 2)
            {
                _games[team]++;
                _points[0] = _points[1] = 0;
                if (_games[team] >= GamesToWin)
                {
                    Winner = team;
                    return ScoreCall.Match;
                }
                Server = 1 - Server;
                return ScoreCall.Game;
            }
            if (IsDeuce) return ScoreCall.Deuce;
            return Advantage >= 0 ? ScoreCall.Advantage : ScoreCall.Points;
        }

        /// <summary>The team's points as the scoreboard shows them: "0", "15", "30", "40", "AD" (or "" for the other side
        /// of an advantage).</summary>
        public string PointsText(int team)
        {
            int adv = Advantage;
            if (adv >= 0) return adv == team ? "AD" : "40";
            return _points[team] switch { 0 => "0", 1 => "15", 2 => "30", _ => "40" };
        }
    }
}
