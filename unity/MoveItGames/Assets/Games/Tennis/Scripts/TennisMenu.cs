using UnityEngine;

namespace MoveIt.Games.Tennis
{
    /// <summary>
    /// The choices on Tenis!'s waiting and results screens (2026-11-07, user: "3 difficulties, a single game and a
    /// tournament mode; a more top-down view"): who plays (one player, two against each other, two together), what (one
    /// match or a tournament), how hard (easy, medium, hard) and from where (the classic camera, a little steeper than
    /// Wii's, or from above). The remote's ↑ ↓ pick a row, ← → change it, and everything is remembered. Two-player games
    /// need the camera (without it the row stays on one player); two against each other always play one match. Drawn as
    /// a broadcast-style panel (<see cref="SportHud"/>).
    /// </summary>
    public sealed class TennisMenu
    {
        public enum Row { Players, Game, Difficulty, View }
        public const int Rows = 4, Views = 2;

        public TennisMode Mode = TennisMode.Solo;
        public TennisFormat Format = TennisFormat.Match;
        public TennisDifficulty Difficulty = TennisDifficulty.Easy;
        /// <summary>The camera: 0 = classic (a little steeper than Wii's), 1 = from above.</summary>
        public int View;
        public Row Selected;

        static readonly TennisMode[] PlayerChoices = { TennisMode.Solo, TennisMode.Versus, TennisMode.Together };

        const string ModeKey = "tennis.mode", FormatKey = "tennis.format", DifficultyKey = "tennis.difficulty", ViewKey = "tennis.view";

        public void Load()
        {
            Mode = (TennisMode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, (int)TennisMode.Solo), 0, 2);
            Format = (TennisFormat)Mathf.Clamp(PlayerPrefs.GetInt(FormatKey, (int)TennisFormat.Match), 0, 1);
            Difficulty = (TennisDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(DifficultyKey, (int)TennisDifficulty.Easy), 0, 2);
            View = Mathf.Clamp(PlayerPrefs.GetInt(ViewKey, 0), 0, Views - 1);
        }

        public void Save()
        {
            PlayerPrefs.SetInt(ModeKey, (int)Mode);
            PlayerPrefs.SetInt(FormatKey, (int)Format);
            PlayerPrefs.SetInt(DifficultyKey, (int)Difficulty);
            PlayerPrefs.SetInt(ViewKey, View);
            PlayerPrefs.Save();
        }

        /// <summary>What is played with <paramref name="mode"/>: two against each other play one match.</summary>
        public TennisFormat FormatFor(TennisMode mode) => mode == TennisMode.Versus ? TennisFormat.Match : Format;

        /// <summary>
        /// The remote's arrows: ↑ ↓ pick a row, ← → change its choice (the players' row only with the camera running).
        /// True when a choice changed (it's saved); <paramref name="moved"/> when only the row did.
        /// </summary>
        public bool Handle(bool up, bool down, bool left, bool right, bool camera, out bool moved)
        {
            moved = false;
            if (up || down)
            {
                Selected = (Row)(((int)Selected + (down ? 1 : -1) + Rows) % Rows);
                moved = true;
                return false;
            }
            int step = right ? 1 : left ? -1 : 0;
            if (step == 0) return false;
            switch (Selected)
            {
                case Row.Players:
                    if (!camera) return false;   // without the camera: one player only
                    int i = System.Array.IndexOf(PlayerChoices, Mode);
                    Mode = PlayerChoices[(i + step + PlayerChoices.Length) % PlayerChoices.Length];
                    break;
                case Row.Game:
                    if (Mode == TennisMode.Versus) return false;
                    Format = Format == TennisFormat.Match ? TennisFormat.Tournament : TennisFormat.Match;
                    break;
                case Row.Difficulty:
                    Difficulty = (TennisDifficulty)Mathf.Clamp((int)Difficulty + step, 0, 2);
                    break;
                default:
                    View = (View + step + Views) % Views;
                    break;
            }
            Save();
            return true;
        }

        // ------------------------------------------------------------------------------------------------ drawing

        public const float Width = 1060f, RowHeight = 64f, Top = 16f;
        public static float Height => Top * 2f + Rows * RowHeight;

        /// <summary>
        /// The panel centred at <paramref name="centreX"/>, its top at <paramref name="y"/> (virtual 1080-high screen): a row
        /// per choice, the selected row lit with chevrons at its ends, the chosen option a sun-yellow pill;
        /// <paramref name="cups"/> = tournaments won per difficulty (a cup and the count under its pill).
        /// </summary>
        public void Draw(float centreX, float y, bool camera, int[] cups, Texture cup)
        {
            var panel = new Rect(centreX - Width / 2f, y, Width, Height);
            // one plain layer with a sun-yellow edge on top: every layer of IMGUI is a draw of the whole panel, and a
            // rounded one shades every pixel of it (the box's GPU paid ~13 ms a frame for the first version of this
            // screen, 2026-11-07)
            GameHud.Box(panel, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.88f));
            GameHud.Box(new Rect(panel.x, panel.y, panel.width, 5f), SportHud.Sun);
            for (int i = 0; i < Rows; i++)
            {
                var row = (Row)i;
                bool selected = row == Selected;
                float ry = y + Top + i * RowHeight;
                if (selected) SportHud.Rounded(new Rect(panel.x + 12f, ry + 3f, Width - 24f, RowHeight - 6f), new Color(1f, 1f, 1f, 0.1f), 20f);
                string label = row switch
                {
                    Row.Players => TennisStrings.PlayersRow,
                    Row.Game => TennisStrings.GameRow,
                    Row.Difficulty => TennisStrings.DifficultyRow,
                    _ => TennisStrings.ViewRow,
                };
                SportHud.Text(new Rect(panel.x + 40f, ry, 250f, RowHeight), label, 32, selected ? SportHud.Sun : new Color(1f, 1f, 1f, 0.85f),
                              TextAnchor.MiddleLeft, 0f);
                const float optionsX = 300f, optionsRight = 60f, gap = 14f;
                float x0 = panel.x + optionsX, span = Width - optionsX - optionsRight;
                switch (row)
                {
                    case Row.Players:
                        for (int k = 0; k < 3; k++)
                        {
                            var mode = PlayerChoices[k];
                            string name = mode switch
                            {
                                TennisMode.Versus => TennisStrings.VersusMode,
                                TennisMode.Together => TennisStrings.TogetherMode,
                                _ => TennisStrings.SoloMode,
                            };
                            Option(Slot(x0, span, gap, 3, k, ry), name, Mode == mode, camera || mode == TennisMode.Solo);
                        }
                        break;
                    case Row.Game:
                        Option(Slot(x0, span, gap, 2, 0, ry), TennisStrings.SingleMatch, FormatFor(Mode) == TennisFormat.Match, true);
                        Option(Slot(x0, span, gap, 2, 1, ry), TennisStrings.Tournament, FormatFor(Mode) == TennisFormat.Tournament, Mode != TennisMode.Versus);
                        break;
                    case Row.Difficulty:
                        for (int k = 0; k < 3; k++)
                        {
                            var r = Slot(x0, span, gap, 3, k, ry);
                            bool chosen = (int)Difficulty == k, won = cups != null && cups[k] > 0 && cup != null;
                            Option(r, TennisStrings.Difficulty((TennisDifficulty)k), chosen, true, won ? 62f : 0f);
                            if (!won) continue;
                            // the tournaments won at this difficulty: a little cup and the count at the pill's end
                            var c = new Rect(r.xMax - 66f, r.y + (r.height - 30f) / 2f, 30f, 30f);
                            var old = GUI.color;
                            GUI.color = chosen ? SportHud.Night : Color.white;
                            GUI.DrawTexture(c, cup);
                            GUI.color = old;
                            SportHud.Text(new Rect(c.xMax + 2f, r.y, 34f, r.height), cups[k].ToString(), 24, chosen ? SportHud.Night : SportHud.Sun,
                                          TextAnchor.MiddleLeft, 0f);
                        }
                        break;
                    default:
                        for (int k = 0; k < Views; k++) Option(Slot(x0, span, gap, Views, k, ry), TennisStrings.View(k), View == k, true);
                        break;
                }
                if (selected)
                {
                    // ← → change this row: arrows at its ends (two characters: the animated chevrons were 24 draws)
                    SportHud.Text(new Rect(x0 - 50f, ry, 40f, RowHeight), "\u2039", 48, SportHud.Sun, TextAnchor.MiddleCenter, 0f);
                    SportHud.Text(new Rect(x0 + span + 10f, ry, 40f, RowHeight), "\u203A", 48, SportHud.Sun, TextAnchor.MiddleCenter, 0f);
                }
            }
        }

        static Rect Slot(float x0, float span, float gap, int count, int index, float rowY)
        {
            float w = (span - gap * (count - 1)) / count;
            return new Rect(x0 + index * (w + gap), rowY + 7f, w, RowHeight - 14f);
        }

        /// <summary>An option's pill: sun-yellow when chosen, dim when it can't be chosen; its text clear of
        /// <paramref name="rightInset"/> px at its right end (a cup).</summary>
        static void Option(Rect r, string text, bool chosen, bool usable, float rightInset = 0f)
        {
            if (chosen) SportHud.Rounded(r, SportHud.Sun, r.height / 2f);   // (the others: their text only, a draw less each)
            var colour = chosen ? SportHud.Night : new Color(1f, 1f, 1f, usable ? 0.8f : 0.28f);
            SportHud.Text(new Rect(r.x, r.y, r.width - rightInset, r.height), text, 28, colour, TextAnchor.MiddleCenter, 0f);
        }
    }
}
