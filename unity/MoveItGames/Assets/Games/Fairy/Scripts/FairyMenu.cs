using UnityEngine;

namespace MoveIt.Games.Fairy
{
    /// <summary>
    /// The choices on Zana!'s waiting screen: one fairy or two, the difficulty, the fairy's element (her spells' colours),
    /// the dress and the wings (those the stardust has opened). The remote's ↑ ↓ pick a row, ← → change it; everything is
    /// remembered. Two fairies need the camera.
    /// </summary>
    public sealed class FairyMenu
    {
        public enum Row { Players, Difficulty, Element, Dress, Wings }
        public const int Rows = 5;

        public bool Two;
        public FairyDifficulty Difficulty = FairyDifficulty.Easy;
        public Row Selected;

        const string TwoKey = "fairy.two", DifficultyKey = "fairy.difficulty";

        public void Load()
        {
            Two = PlayerPrefs.GetInt(TwoKey, 0) == 1;
            Difficulty = (FairyDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(DifficultyKey, (int)FairyDifficulty.Easy), 0, 2);
        }

        public void Save()
        {
            PlayerPrefs.SetInt(TwoKey, Two ? 1 : 0);
            PlayerPrefs.SetInt(DifficultyKey, (int)Difficulty);
            PlayerPrefs.Save();
        }

        static int Open(int[] at, int stardust)
        {
            int n = 0;
            while (n < at.Length && stardust >= at[n]) n++;
            return Mathf.Max(1, n);
        }

        /// <summary>The ← → arrows change the row picked by ↑ ↓; true when a choice changed (saved), <paramref name="moved"/>
        /// when only the row did.</summary>
        public bool Handle(bool up, bool down, bool left, bool right, bool camera, GardenProgress progress, out bool moved)
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
                    Two = !Two;
                    break;
                case Row.Difficulty:
                    Difficulty = (FairyDifficulty)Mathf.Clamp((int)Difficulty + step, 0, 2);
                    break;
                case Row.Element:
                    if (progress == null) return false;
                    progress.Element = (Element)(((int)progress.Element + step + 4) % 4);
                    break;
                case Row.Dress:
                {
                    int open = Open(GardenProgress.DressAt, progress?.Stardust ?? 0);
                    if (open < 2 || progress == null) return false;
                    progress.Dress = (progress.Dress + step + open) % open;
                    break;
                }
                default:
                {
                    int open = Open(GardenProgress.WingsAt, progress?.Stardust ?? 0);
                    if (open < 2 || progress == null) return false;
                    progress.Wings = (progress.Wings + step + open) % open;
                    break;
                }
            }
            Save();
            return true;
        }

        // ------------------------------------------------------------------------------------------------ drawing

        public const float Width = 900f, RowHeight = 60f, Top = 14f;
        public static float Height => Top * 2f + Rows * RowHeight;

        /// <summary>
        /// How the menu is drawn: into the HUD's batch (FairyGame: pictures from Zana!'s sheet, glyphs; since 2026-11-17
        /// night, when its ~40 IMGUI draws a frame were what held the waiting screen under 30 fps on a hot box) or with
        /// IMGUI (<see cref="Imgui"/>: as it was).
        /// </summary>
        public interface IPaint
        {
            /// <summary>A plain box.</summary>
            void Box(Rect r, Color colour);
            /// <summary>A flat rounded box (a pill when the radius is half its height).</summary>
            void Round(Rect r, Color colour, float radius);
            /// <summary>A rounded card (IMGUI: SportHud.Panel's gradient, no shadow).</summary>
            void Card(Rect r, Color colour, float radius);
            /// <summary>One line of bold words, no outline.</summary>
            void Text(Rect r, string text, int size, Color colour, TextAnchor anchor);
        }

        sealed class ImguiPaint : IPaint
        {
            public void Box(Rect r, Color colour) => GameHud.Box(r, colour);
            public void Round(Rect r, Color colour, float radius) => SportHud.Rounded(r, colour, radius);
            public void Card(Rect r, Color colour, float radius) => SportHud.Panel(r, colour, radius, null, 0f, false);
            public void Text(Rect r, string text, int size, Color colour, TextAnchor anchor) => SportHud.Text(r, text, size, colour, anchor, 0f);
        }

        /// <summary>The menu drawn with IMGUI, a draw call per box and per line (PerfLab menubatch=0).</summary>
        public static readonly IPaint Imgui = new ImguiPaint();

        public void Draw(float x, float y, bool camera, GardenProgress progress, IPaint paint)
        {
            var panel = new Rect(x, y, Width, Height);
            paint.Box(panel, new Color(SportHud.Night.r, SportHud.Night.g, SportHud.Night.b, 0.86f));
            paint.Box(new Rect(panel.x, panel.y, panel.width, 5f), FairyFx.Pink);
            int dust = progress?.Stardust ?? 0;
            for (int i = 0; i < Rows; i++)
            {
                var row = (Row)i;
                bool selected = row == Selected;
                float ry = y + Top + i * RowHeight;
                if (selected) paint.Round(new Rect(panel.x + 12f, ry + 3f, Width - 24f, RowHeight - 6f), new Color(1f, 1f, 1f, 0.1f), 20f);
                string label = row switch
                {
                    Row.Players => FairyStrings.PlayersRow,
                    Row.Difficulty => FairyStrings.DifficultyRow,
                    Row.Element => FairyStrings.ElementRow,
                    Row.Dress => FairyStrings.DressRow,
                    _ => FairyStrings.WingsRow,
                };
                paint.Text(new Rect(panel.x + 36f, ry, 220f, RowHeight), label, 28, selected ? FairyFx.Pink : new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
                const float optionsX = 250f, optionsRight = 56f, gap = 10f;
                float x0 = panel.x + optionsX, span = Width - optionsX - optionsRight;
                switch (row)
                {
                    case Row.Players:
                        Option(paint, Slot(x0, span, gap, 2, 0, ry), FairyStrings.SoloMode, !Two, true);
                        Option(paint, Slot(x0, span, gap, 2, 1, ry), FairyStrings.TogetherMode, Two, camera);
                        break;
                    case Row.Difficulty:
                        for (int d = 0; d < 3; d++) Option(paint, Slot(x0, span, gap, 3, d, ry), FairyStrings.Difficulty((FairyDifficulty)d), (int)Difficulty == d, true, 22);
                        break;
                    case Row.Element:
                        for (int e = 0; e < 4; e++)
                        {
                            var r = Slot(x0, span, gap, 4, e, ry);
                            bool chosen = progress != null && (int)progress.Element == e;
                            var c = FairyLooks.ElementColour((Element)e);
                            if (chosen) paint.Round(r, c, r.height / 2f);
                            paint.Text(r, FairyStrings.ElementName((Element)e), 22, chosen ? SportHud.Night : Color.Lerp(c, Color.white, 0.45f), TextAnchor.MiddleCenter);
                        }
                        break;
                    case Row.Dress:
                    {
                        int count = FairyLooks.Dresses.Length, open = Open(GardenProgress.DressAt, dust), chosen = progress?.Dress ?? 0;
                        for (int k = 0; k < count; k++) Swatch(paint, Slot(x0, span, gap, count, k, ry), FairyLooks.DressColour(k), k < open, k == chosen, GardenProgress.DressAt[k]);
                        break;
                    }
                    default:
                    {
                        int count = FairyLooks.AllWings.Length, open = Open(GardenProgress.WingsAt, dust), chosen = progress?.Wings ?? 0;
                        for (int k = 0; k < count; k++) Swatch(paint, Slot(x0, span, gap, count, k, ry), FairyLooks.AllWings[k].Edge, k < open, k == chosen, GardenProgress.WingsAt[k]);
                        break;
                    }
                }
                if (selected)
                {
                    paint.Text(new Rect(x0 - 44f, ry, 40f, RowHeight), "‹", 46, FairyFx.Pink, TextAnchor.MiddleCenter);
                    paint.Text(new Rect(x0 + span + 6f, ry, 40f, RowHeight), "›", 46, FairyFx.Pink, TextAnchor.MiddleCenter);
                }
            }
        }

        static Rect Slot(float x0, float span, float gap, int count, int index, float rowY)
        {
            float w = (span - gap * (count - 1)) / count;
            return new Rect(x0 + index * (w + gap), rowY + 7f, w, RowHeight - 14f);
        }

        static void Option(IPaint paint, Rect r, string text, bool chosen, bool usable, int size = 24)
        {
            if (chosen) paint.Round(r, FairyFx.Pink, r.height / 2f);
            var colour = chosen ? SportHud.Night : new Color(1f, 1f, 1f, usable ? 0.8f : 0.4f);
            paint.Text(r, text, size, colour, TextAnchor.MiddleCenter);
        }

        static void Swatch(IPaint paint, Rect r, Color colour, bool open, bool chosen, int dust)
        {
            if (chosen) paint.Round(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), SportHud.Sun, (r.height + 8f) / 2f);
            paint.Round(r, open ? new Color(colour.r, colour.g, colour.b, 1f) : new Color(1f, 1f, 1f, 0.12f), r.height / 2f);
            if (!open) paint.Text(r, FairyStrings.DustLocked(dust), 15, new Color(1f, 1f, 1f, 0.65f), TextAnchor.MiddleCenter);
        }
    }
}
