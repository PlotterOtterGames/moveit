using UnityEngine;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// A tournament's bracket on the TV (between its matches and on its results; IMGUI on the virtual 1080-high screen):
    /// the eight teams of the quarter-finals, the semi-finals, the final and the champion, joined by lines; the games each
    /// side won; winners bright and losers dim; the player's team in sun yellow, its next match pulsing.
    /// </summary>
    public static class TennisBracketView
    {
        const int Columns = 4;
        const float BoxHeight = 48f, Header = 44f;

        /// <summary>Draws <paramref name="t"/> inside <paramref name="area"/>; <paramref name="you"/> = the player's team's
        /// name ("Ti", "Ju"), <paramref name="time"/> drives the pulse.</summary>
        public static void Draw(TennisTournament t, Rect area, string you, Texture cup, float time)
        {
            const float gap = 46f, pairGap = 16f;
            // a dark panel behind it all: the bracket reads over the busy stadium
            GameHud.Box(new Rect(area.x - 30f, area.y - 12f, area.width + 60f, area.height + 24f),
                        new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.84f));
            float colW = (area.width - gap * (Columns - 1)) / Columns;
            // each column's box centres: the quarter-finals evenly (pairs a little apart), every next box between its two
            var ys = new float[Columns][];
            ys[0] = new float[8];
            float step = (area.height - Header - pairGap * 3f) / 8f;
            for (int k = 0; k < 8; k++) ys[0][k] = area.y + Header + step * (k + 0.5f) + pairGap * (k / 2);
            for (int c = 1; c < Columns; c++)
            {
                int n = 8 >> c;
                ys[c] = new float[n];
                for (int k = 0; k < n; k++) ys[c][k] = (ys[c - 1][2 * k] + ys[c - 1][2 * k + 1]) / 2f;
            }
            float X(int c) => area.x + c * (colW + gap);

            // the rounds' names over their columns
            for (int c = 0; c < Columns; c++)
            {
                string name = c < TennisTournament.Rounds ? TennisStrings.RoundName(c) : TennisStrings.Champion(false).TrimEnd('!');
                bool current = !t.Over && c == t.Round;
                SportHud.Text(new Rect(X(c), area.y, colW, Header - 8f), name, 26, current ? SportHud.Sun : new Color(1f, 1f, 1f, 0.75f),
                              TextAnchor.MiddleCenter, 0f);
            }

            // the lines from each match to the box its winner goes to (under the boxes)
            var line = new Color(1f, 1f, 1f, 0.35f);
            for (int c = 0; c < Columns - 1; c++)
                for (int m = 0; m < ys[c].Length / 2; m++)
                {
                    float x0 = X(c) + colW, xm = x0 + gap / 2f, x1 = X(c + 1);
                    float a = ys[c][2 * m], b = ys[c][2 * m + 1], to = ys[c + 1][m];
                    bool played = t.Results[c][m].Played;
                    var lc = played ? new Color(1f, 1f, 1f, 0.6f) : line;
                    GameHud.Line(new Vector2(x0, a), new Vector2(xm, a), lc, 3f);
                    GameHud.Line(new Vector2(x0, b), new Vector2(xm, b), lc, 3f);
                    GameHud.Line(new Vector2(xm, a - 1.5f), new Vector2(xm, b + 1.5f), lc, 3f);
                    GameHud.Line(new Vector2(xm, to), new Vector2(x1, to), lc, 3f);
                }

            // the matches' boxes
            float pulse = 0.55f + 0.45f * Mathf.Sin(time * 5f);
            for (int c = 0; c < TennisTournament.Rounds; c++)
                for (int m = 0; m < t.Results[c].Length; m++)
                {
                    var r = t.Results[c][m];
                    bool knownA = c == 0 || t.Results[c - 1][2 * m].Played, knownB = c == 0 || t.Results[c - 1][2 * m + 1].Played;
                    bool next = !t.Over && c == t.Round && m == 0;
                    Box(t, new Rect(X(c), ys[c][2 * m] - BoxHeight / 2f, colW, BoxHeight), knownA ? r.A : (int?)null, r.Played, r.Played && r.Winner == r.A,
                        r.GamesA, you, next ? pulse : 0f);
                    Box(t, new Rect(X(c), ys[c][2 * m + 1] - BoxHeight / 2f, colW, BoxHeight), knownB ? r.B : (int?)null, r.Played, r.Played && r.Winner == r.B,
                        r.GamesB, you, next ? pulse : 0f);
                }

            // the champion, with the cup
            var champ = new Rect(X(Columns - 1), ys[Columns - 1][0] - BoxHeight * 0.65f, colW, BoxHeight * 1.3f);
            var winner = t.Champion;
            bool ours = winner == TennisTournament.You;
            SportHud.Panel(champ, winner.HasValue ? (ours ? SportHud.Sun : new Color(1f, 1f, 1f, 0.9f)) : new Color(1f, 1f, 1f, 0.08f), champ.height / 2f,
                           null, 0f, false);
            if (cup != null) GUI.DrawTexture(new Rect(champ.x + 14f, champ.center.y - 22f, 44f, 44f), cup);
            string champName = winner.HasValue ? (ours ? you : t.NameOf(winner.Value)) : "?";
            SportHud.Text(new Rect(champ.x + 64f, champ.y, champ.width - 76f, champ.height), champName, 30,
                          winner.HasValue ? SportHud.Night : new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleCenter, 0f);
        }

        /// <summary>One side's box in a match: its name (the player's in sun yellow; a computer player, or a pair in
        /// doubles), its games once played, dim if it lost; a pulsing rim on the player's next match.</summary>
        static void Box(TennisTournament t, Rect r, int? team, bool played, bool won, int games, string you, float pulse)
        {
            bool ours = team == TennisTournament.You;
            bool lost = played && !won;
            var fill = ours ? new Color(SportHud.Sun.r, SportHud.Sun.g, SportHud.Sun.b, lost ? 0.45f : 0.95f)
                : new Color(1f, 1f, 1f, team.HasValue ? (lost ? 0.07f : 0.16f) : 0.05f);
            SportHud.Rounded(r, fill, 14f);
            if (pulse > 0f)
                GUI.DrawTexture(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                                new Color(SportHud.Sun.r, SportHud.Sun.g, SportHud.Sun.b, pulse), 4f, 18f);
            if (!team.HasValue) return;
            string name = ours ? you : t.NameOf(team.Value);
            var text = ours ? SportHud.Night : new Color(1f, 1f, 1f, lost ? 0.45f : 0.95f);
            SportHud.Text(new Rect(r.x + 18f, r.y, r.width - 70f, r.height), name, 26, text, TextAnchor.MiddleLeft, 0f);
            if (played)
                SportHud.Text(new Rect(r.xMax - 52f, r.y, 40f, r.height), games.ToString(), 28, won ? (ours ? SportHud.Night : SportHud.Sun) : text,
                              TextAnchor.MiddleCenter, 0f);
        }
    }
}
