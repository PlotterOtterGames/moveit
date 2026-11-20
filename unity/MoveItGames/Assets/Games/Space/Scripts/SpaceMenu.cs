using UnityEngine;

namespace MoveIt.Games.Space
{
    /// <summary>
    /// The choices on Yjet!'s waiting screen: one astronaut or two, the suit (those the planets found have unlocked). The
    /// remote's ↑ ↓ pick a row, ← → change it, everything is remembered. Two players need the camera. One plain panel.
    /// </summary>
    public sealed class SpaceMenu
    {
        public enum Row { Players, Suit }
        public const int Rows = 2;

        public SpaceMode Mode = SpaceMode.Solo;
        public Row Selected;

        const string ModeKey = "space.mode";

        public void Load() => Mode = (SpaceMode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, (int)SpaceMode.Solo), 0, 1);

        public void Save()
        {
            PlayerPrefs.SetInt(ModeKey, (int)Mode);
            PlayerPrefs.Save();
        }

        /// <summary>The ← → arrows change the row picked by ↑ ↓ (the suit among those <paramref name="progress"/> has
        /// unlocked); true when a choice changed (saved), <paramref name="moved"/> when only the row did.</summary>
        public bool Handle(bool up, bool down, bool left, bool right, bool camera, GalaxyProgress progress, out bool moved)
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
            if (Selected == Row.Players)
            {
                if (!camera) return false;
                Mode = Mode == SpaceMode.Solo ? SpaceMode.Together : SpaceMode.Solo;
                Save();
                return true;
            }
            int unlocked = progress != null ? progress.Suits : 1;
            if (unlocked < 2 || progress == null) return false;
            progress.Suit = (progress.Suit + step + unlocked) % unlocked;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ drawing

        public const float Width = 900f, RowHeight = 64f, Top = 14f;
        public static float Height => Top * 2f + Rows * RowHeight;

        public void Draw(float x, float y, bool camera, GalaxyProgress progress)
        {
            var panel = new Rect(x, y, Width, Height);
            GameHud.Box(panel, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.86f));
            GameHud.Box(new Rect(panel.x, panel.y, panel.width, 5f), SportHud.Sun);
            for (int i = 0; i < Rows; i++)
            {
                var row = (Row)i;
                bool selected = row == Selected;
                float ry = y + Top + i * RowHeight;
                if (selected) SportHud.Rounded(new Rect(panel.x + 12f, ry + 3f, Width - 24f, RowHeight - 6f), new Color(1f, 1f, 1f, 0.1f), 20f);
                string label = row == Row.Players ? SpaceStrings.PlayersRow : SpaceStrings.SuitRow;
                SportHud.Text(new Rect(panel.x + 36f, ry, 200f, RowHeight), label, 30, selected ? SportHud.Sun : new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft, 0f);
                const float optionsX = 240f, optionsRight = 56f, gap = 10f;
                float x0 = panel.x + optionsX, span = Width - optionsX - optionsRight;
                if (row == Row.Players)
                {
                    Option(Slot(x0, span, gap, 2, 0, ry), SpaceStrings.SoloMode, Mode == SpaceMode.Solo, true);
                    Option(Slot(x0, span, gap, 2, 1, ry), SpaceStrings.TogetherMode, Mode == SpaceMode.Together, camera);
                }
                else
                {
                    int count = SpaceLooks.Suits.Length, unlocked = progress != null ? progress.Suits : 1, suit = progress != null ? progress.Suit : 0;
                    for (int k = 0; k < count; k++)
                    {
                        var r = Slot(x0, span, gap, count, k, ry);
                        bool open = k < unlocked;
                        if (k == suit) SportHud.Rounded(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), SportHud.Sun, (r.height + 8f) / 2f);
                        SportHud.Rounded(r, open ? SpaceLooks.Accent(k) : new Color(1f, 1f, 1f, 0.12f), r.height / 2f);
                        if (!open)
                            SportHud.Text(r, SpaceStrings.SuitLocked(GalaxyProgress.SuitAt[k]), 17, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter, 0f);
                    }
                }
                if (selected)
                {
                    SportHud.Text(new Rect(x0 - 44f, ry, 40f, RowHeight), "‹", 48, SportHud.Sun, TextAnchor.MiddleCenter, 0f);
                    SportHud.Text(new Rect(x0 + span + 6f, ry, 40f, RowHeight), "›", 48, SportHud.Sun, TextAnchor.MiddleCenter, 0f);
                }
            }
        }

        static Rect Slot(float x0, float span, float gap, int count, int index, float rowY)
        {
            float w = (span - gap * (count - 1)) / count;
            return new Rect(x0 + index * (w + gap), rowY + 7f, w, RowHeight - 14f);
        }

        static void Option(Rect r, string text, bool chosen, bool usable, int size = 26)
        {
            if (chosen) SportHud.Rounded(r, SportHud.Sun, r.height / 2f);
            var colour = chosen ? SportHud.Night : new Color(1f, 1f, 1f, usable ? 0.8f : 0.4f);
            SportHud.Text(r, text, size, colour, TextAnchor.MiddleCenter, 0f);
        }
    }
}
