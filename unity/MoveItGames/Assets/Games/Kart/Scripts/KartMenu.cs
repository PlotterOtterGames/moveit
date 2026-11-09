using UnityEngine;

namespace MoveIt.Games.Kart
{
    /// <summary>
    /// The choices on Gara!'s waiting and results screens (Tenis!'s and Volejboll!'s menu, plus the track): who plays (one
    /// player, two against each other, two together in one kart), what (one race or a cup of three), how hard, which track
    /// (a race: the coast, the village, the dino valley or a random one; a cup races all three), and the camera (behind the
    /// kart, or higher). The remote's ↑ ↓ pick a row, ← → change it, everything is remembered. Two-player games need the
    /// camera. One plain panel (the box's GPU pays for every layer of IMGUI).
    /// </summary>
    public sealed class KartMenu
    {
        public enum Row { Players, Game, Difficulty, Track, View }
        public const int Rows = 5, Views = 2, TrackChoices = 4, RandomTrack = 3;

        public KartMode Mode = KartMode.Solo;
        public KartFormat Format = KartFormat.Race;
        public KartDifficulty Difficulty = KartDifficulty.Easy;
        /// <summary>0..2 a track, 3 = a random one.</summary>
        public int Track = RandomTrack;
        public int View;
        public Row Selected;

        static readonly KartMode[] PlayerChoices = { KartMode.Solo, KartMode.Versus, KartMode.Together };
        const string ModeKey = "kart.mode", FormatKey = "kart.format", DifficultyKey = "kart.difficulty", TrackKey = "kart.track", ViewKey = "kart.view";

        public void Load()
        {
            Mode = (KartMode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, (int)KartMode.Solo), 0, 2);
            Format = (KartFormat)Mathf.Clamp(PlayerPrefs.GetInt(FormatKey, (int)KartFormat.Race), 0, 1);
            Difficulty = (KartDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(DifficultyKey, (int)KartDifficulty.Easy), 0, 2);
            Track = Mathf.Clamp(PlayerPrefs.GetInt(TrackKey, RandomTrack), 0, TrackChoices - 1);
            View = Mathf.Clamp(PlayerPrefs.GetInt(ViewKey, 0), 0, Views - 1);
        }

        public void Save()
        {
            PlayerPrefs.SetInt(ModeKey, (int)Mode);
            PlayerPrefs.SetInt(FormatKey, (int)Format);
            PlayerPrefs.SetInt(DifficultyKey, (int)Difficulty);
            PlayerPrefs.SetInt(TrackKey, Track);
            PlayerPrefs.SetInt(ViewKey, View);
            PlayerPrefs.Save();
        }

        /// <summary>The ← → arrows change the row picked by ↑ ↓; true when a choice changed (saved), <paramref name="moved"/>
        /// when only the row did.</summary>
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
                    if (!camera) return false;
                    int i = System.Array.IndexOf(PlayerChoices, Mode);
                    Mode = PlayerChoices[(i + step + PlayerChoices.Length) % PlayerChoices.Length];
                    break;
                case Row.Game:
                    Format = Format == KartFormat.Race ? KartFormat.Cup : KartFormat.Race;
                    break;
                case Row.Difficulty:
                    Difficulty = (KartDifficulty)Mathf.Clamp((int)Difficulty + step, 0, 2);
                    break;
                case Row.Track:
                    if (Format == KartFormat.Cup) return false;
                    Track = (Track + step + TrackChoices) % TrackChoices;
                    break;
                default:
                    View = (View + step + Views) % Views;
                    break;
            }
            Save();
            return true;
        }

        // ------------------------------------------------------------------------------------------------ drawing

        public const float Width = 1100f, RowHeight = 62f, Top = 14f;
        public static float Height => Top * 2f + Rows * RowHeight;

        public void Draw(float centreX, float y, bool camera, int[] cups, Texture cup)
        {
            var panel = new Rect(centreX - Width / 2f, y, Width, Height);
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
                    Row.Players => KartStrings.PlayersRow,
                    Row.Game => KartStrings.GameRow,
                    Row.Difficulty => KartStrings.DifficultyRow,
                    Row.Track => KartStrings.TrackRow,
                    _ => KartStrings.ViewRow,
                };
                SportHud.Text(new Rect(panel.x + 40f, ry, 250f, RowHeight), label, 32, selected ? SportHud.Sun : new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft, 0f);
                const float optionsX = 290f, optionsRight = 60f, gap = 12f;
                float x0 = panel.x + optionsX, span = Width - optionsX - optionsRight;
                switch (row)
                {
                    case Row.Players:
                        for (int k = 0; k < 3; k++)
                        {
                            var mode = PlayerChoices[k];
                            string name = mode switch { KartMode.Versus => KartStrings.VersusMode, KartMode.Together => KartStrings.TogetherMode, _ => KartStrings.SoloMode };
                            Option(Slot(x0, span, gap, 3, k, ry), name, Mode == mode, camera || mode == KartMode.Solo);
                        }
                        break;
                    case Row.Game:
                        Option(Slot(x0, span, gap, 2, 0, ry), KartStrings.SingleRace, Format == KartFormat.Race, true);
                        Option(Slot(x0, span, gap, 2, 1, ry), KartStrings.CupMode, Format == KartFormat.Cup, true);
                        break;
                    case Row.Difficulty:
                        for (int k = 0; k < 3; k++)
                        {
                            var r = Slot(x0, span, gap, 3, k, ry);
                            bool chosen = (int)Difficulty == k, won = cups != null && cups[k] > 0 && cup != null;
                            Option(r, KartStrings.Difficulty((KartDifficulty)k), chosen, true, won ? 62f : 0f);
                            if (!won) continue;
                            var c = new Rect(r.xMax - 66f, r.y + (r.height - 30f) / 2f, 30f, 30f);
                            var old = GUI.color;
                            GUI.color = chosen ? SportHud.Night : Color.white;
                            GUI.DrawTexture(c, cup);
                            GUI.color = old;
                            SportHud.Text(new Rect(c.xMax + 2f, r.y, 34f, r.height), cups[k].ToString(), 24, chosen ? SportHud.Night : SportHud.Sun, TextAnchor.MiddleLeft, 0f);
                        }
                        break;
                    case Row.Track:
                        if (Format == KartFormat.Cup)
                        {
                            Option(new Rect(x0, ry + 7f, span, RowHeight - 14f), KartStrings.AllThree, false, false);
                            break;
                        }
                        for (int k = 0; k < TrackChoices; k++) Option(Slot(x0, span, gap, TrackChoices, k, ry), KartStrings.TrackOption(k), Track == k, true, 0f, 24);
                        break;
                    default:
                        for (int k = 0; k < Views; k++) Option(Slot(x0, span, gap, Views, k, ry), KartStrings.View(k), View == k, true);
                        break;
                }
                if (selected)
                {
                    SportHud.Text(new Rect(x0 - 46f, ry, 40f, RowHeight), "‹", 48, SportHud.Sun, TextAnchor.MiddleCenter, 0f);
                    SportHud.Text(new Rect(x0 + span + 8f, ry, 40f, RowHeight), "›", 48, SportHud.Sun, TextAnchor.MiddleCenter, 0f);
                }
            }
        }

        static Rect Slot(float x0, float span, float gap, int count, int index, float rowY)
        {
            float w = (span - gap * (count - 1)) / count;
            return new Rect(x0 + index * (w + gap), rowY + 7f, w, RowHeight - 14f);
        }

        static void Option(Rect r, string text, bool chosen, bool usable, float rightInset = 0f, int size = 28)
        {
            if (chosen) SportHud.Rounded(r, SportHud.Sun, r.height / 2f);
            var colour = chosen ? SportHud.Night : new Color(1f, 1f, 1f, usable ? 0.8f : 0.4f);
            SportHud.Text(new Rect(r.x, r.y, r.width - rightInset, r.height), text, size, colour, TextAnchor.MiddleCenter, 0f);
        }
    }
}
