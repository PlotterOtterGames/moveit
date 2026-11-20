using UnityEngine;

namespace MoveIt.Games.Arcade
{
    /// <summary>
    /// The fun fair at night behind Lunapark!'s menu: a gradient sky with twinkling stars and the moon, the ferris wheel
    /// turning with its lit cabins, circus tents, strings of light bulbs, and the six booths in a row (a striped awning in
    /// the booth's colour over a sign with its picture and the best's stars; the chosen one raised and glowing), with the
    /// prize shelf of plush toys at the top. All of it in the sprite batches (a few draw calls); the words are the game's
    /// IMGUI on top, at <see cref="Booths"/>.
    /// </summary>
    public sealed class HallView
    {
        public static readonly Color[] BoothColours =
        {
            MachineView.Coral, MachineView.Leaf, MachineView.Grape, MachineView.Sky, MachineView.Sun, MachineView.Orange,
        };
        public static readonly string[] Icons = { "icon_whack", "icon_fruit", "icon_wall", "icon_catch", "icon_bird", "icon_ball" };
        public const float BoothBottom = 245f, BoothHeight = 300f;

        readonly ArcadeArt _art;
        readonly Vector2[] _stars = new Vector2[90];
        readonly float[] _lift = new float[6];
        /// <summary>Each booth's sign, in world pixels (y up), as drawn this frame.</summary>
        public readonly Rect[] Booths = new Rect[6];
        /// <summary>Where the plush toys sit on the shelf (world), and the shelf.</summary>
        public Rect Shelf;

        public HallView(ArcadeArt art)
        {
            _art = art;
            var rng = new System.Random(11);
            for (int i = 0; i < _stars.Length; i++) _stars[i] = new Vector2((float)rng.NextDouble(), 0.45f + 0.55f * (float)rng.NextDouble());
        }

        public void Draw(SpriteBatch back, SpriteBatch glow, float width, float time, float dt, int selected, bool boothsFocused, ArcadeProgress progress)
        {
            var white = _art["white"];
            back.Gradient(white, new Rect(0f, 0f, width, ArcadeStage.HudHeight), new Color(0.27f, 0.15f, 0.43f), new Color(0.03f, 0.05f, 0.16f));
            foreach (var st in _stars)
            {
                float tw = 0.5f + 0.5f * Mathf.Sin(time * 2f + st.x * 37f);
                glow.Draw(_art["spark"], new Vector2(st.x * width, st.y * 1080f), 6f + 6f * tw, 0f, new Color(1f, 1f, 1f, 0.3f + 0.5f * tw));
            }
            back.Draw(_art["moon"], new Vector2(width * 0.5f - 420f, 1000f), 95f);
            // the ferris wheel on the left: its legs, the turning rim, cabins that hang upright, blinking bulbs on the rim
            var hub = new Vector2(310f, 640f);
            const float radius = 300f;
            float turn = time * 7f;
            back.Draw(_art["wheel_legs"], hub, 300f, 0f, new Color(0.62f, 0.55f, 0.95f));
            back.Draw(_art["wheel_rim"], hub, radius / 0.96f, turn, new Color(0.75f, 0.7f, 1f));
            for (int k = 0; k < 16; k++)
            {
                float a = (turn + k * 22.5f) * Mathf.Deg2Rad;
                var at = hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                bool on = ((int)(time * 5f) + k) % 4 != 0;
                if (on) glow.Draw(_art["glow"], at, 14f, 0f, new Color(1f, 0.85f, 0.45f, 0.6f));
                back.Draw(_art[on ? "bulb_on" : "bulb_off"], at, 13f);
            }
            for (int k = 0; k < 8; k++)
            {
                float a = (turn + k * 45f + 11f) * Mathf.Deg2Rad;
                back.Draw(_art["cabin" + (k % 4)], hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * 0.96f, 34f);
            }
            // tents on the right, the ground, the strings of lights across the top
            for (int k = 0; k < 2; k++)
                back.Draw(_art["tent"], new Vector2(width - 150f - k * 230f, 650f - k * 12f), 62f - k * 9f, 0f, Color.Lerp(Color.white, new Color(0.55f, 0.45f, 0.75f), 0.35f + k * 0.15f));
            back.Gradient(white, new Rect(0f, 0f, width, 660f), new Color(0.07f, 0.06f, 0.17f), new Color(0.2f, 0.13f, 0.34f, 0.0f));
            for (int s = 0; s < 1; s++)
            {
                float y0 = 1064f, sag = 30f;
                for (int k = 0; k <= 26; k++)
                {
                    float x = width * k / 26f + (s == 1 ? width / 52f : 0f), u = x / width;
                    var at = new Vector2(x, y0 - sag * 4f * u * (1f - u));
                    bool on = ((int)(time * 3f) + k + s) % 3 != 0;
                    if (on) glow.Draw(_art["glow"], at, 16f, 0f, MachineView.PlayerColour(k % 2) * new Color(1f, 1f, 1f, 0.5f));
                    back.Draw(_art[on ? "bulb_on" : "bulb_off"], at, 12f);
                }
            }
            DrawBooths(back, glow, width, time, dt, selected, boothsFocused, progress);
            DrawShelf(back, glow, width, progress);
        }

        void DrawBooths(SpriteBatch back, SpriteBatch glow, float width, float time, float dt, int selected, bool focused, ArcadeProgress progress)
        {
            const float gap = 26f;
            float bw = Mathf.Min(270f, (width - 140f - gap * 5f) / 6f);
            float x0 = (width - (bw * 6f + gap * 5f)) / 2f;
            for (int i = 0; i < 6; i++)
            {
                bool chosen = i == selected;
                _lift[i] = Mathf.Lerp(_lift[i], chosen ? 26f : 0f, 1f - Mathf.Exp(-dt * 12f));
                float cx = x0 + i * (bw + gap) + bw / 2f, lift = _lift[i];
                var board = new Rect(cx - bw / 2f, BoothBottom + lift, bw, BoothHeight);
                Booths[i] = board;
                var colour = BoothColours[i];
                if (chosen)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(time * 4f);
                    glow.Draw(_art["glow"], board.center + new Vector2(0f, 30f), bw * 0.95f, 0f, colour * new Color(1f, 1f, 1f, (focused ? 0.75f : 0.4f) * pulse));
                }
                back.Nine(_art["panel"], new Rect(board.x - 6f, board.y - 8f, board.width + 12f, board.height + 4f), 40f, new Color(0.05f, 0.04f, 0.15f, 0.45f));
                back.Nine(_art["panel"], board, 40f, chosen ? Color.white : new Color(0.9f, 0.9f, 0.97f));
                back.Draw(_art[Icons[i]], new Vector2(cx, board.y + 185f), 66f * Mathf.Min(1f, bw / 250f));
                // the best's stars
                int stars = progress.Best[i] >= 0 ? ArcadeProgress.Stars((Machine)i, progress.Best[i]) : 0;
                for (int s = 0; s < 3; s++)
                {
                    var at = new Vector2(cx + (s - 1) * 46f, board.y + 34f);
                    back.Draw(_art[s < stars ? "star" : "star_white"], at, 18f, 0f, s < stars ? Color.white : new Color(0.1f, 0.1f, 0.25f, 0.18f));
                }
                // the awning: white and the booth's colour, bulbs along its edge (chasing on the chosen booth)
                var awningAt = new Vector2(cx, board.yMax + 32f);
                float awningUnit = (bw + 26f) / 520f * 86f;
                back.Draw(_art["awning"], awningAt, awningUnit);
                back.Draw(_art["awning_tint"], awningAt, awningUnit, 0f, colour);
                if (chosen)
                    for (int k = 0; k < 7; k++)
                    {
                        var at = new Vector2(cx - bw / 2f + (k + 0.5f) * bw / 7f, board.yMax + 2f);
                        bool on = ((int)(time * 8f) + k) % 2 == 0;
                        if (on) glow.Draw(_art["glow"], at, 15f, 0f, new Color(1f, 0.85f, 0.45f, 0.7f));
                        back.Draw(_art[on ? "bulb_on" : "bulb_off"], at, 12f);
                    }
            }
        }

        void DrawShelf(SpriteBatch back, SpriteBatch glow, float width, ArcadeProgress progress)
        {
            const float shelfW = 520f;
            var at = new Vector2(width - shelfW / 2f - 40f, 880f);
            Shelf = new Rect(at.x - shelfW / 2f, at.y, shelfW, 90f);
            back.Draw(_art["shelf"], at, shelfW / 760f * 30f);
            int won = progress.PrizesWon;
            for (int i = 0; i < ArcadeProgress.Prizes; i++)
            {
                var p = new Vector2(Shelf.xMin + (i + 0.5f) * shelfW / ArcadeProgress.Prizes, at.y + 42f);
                bool has = i < won;
                if (has) glow.Draw(_art["glow"], p, 30f, 0f, new Color(1f, 0.9f, 0.6f, 0.35f));
                back.Draw(_art["plush" + i], p, 30f, 0f, has ? Color.white : new Color(0.2f, 0.17f, 0.38f, 0.9f));
            }
        }
    }
}
