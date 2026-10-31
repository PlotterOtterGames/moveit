using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>
    /// Broadcast-style HUD pieces for the activity games (IMGUI on <see cref="GameHud"/>'s 1080-high screen, until the
    /// UI Toolkit rewrite): rounded gradient panels with a soft shadow, outlined bold text, a TV scoreboard, banners that
    /// slam in ("GOL!"), pills, a timing meter. Colours are the brand's (brand.json).
    /// </summary>
    public static class SportHud
    {
        public static readonly Color Night = new Color(0.078f, 0.098f, 0.22f), NightLight = new Color(0.15f, 0.18f, 0.36f);
        public static readonly Color Sun = new Color(1f, 0.79f, 0.24f), Coral = new Color(1f, 0.35f, 0.31f),
                                     Sky = new Color(0.19f, 0.78f, 0.96f), Grape = new Color(0.48f, 0.38f, 1f),
                                     Leaf = new Color(0.44f, 0.84f, 0.42f);

        static Texture2D _gradient;
        static readonly Dictionary<int, GUIStyle> Styles = new Dictionary<int, GUIStyle>();

        /// <summary>A white-to-grey vertical gradient (tinted by the panel's colour).</summary>
        static Texture2D Gradient
        {
            get
            {
                if (_gradient != null) return _gradient;
                _gradient = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < 32; y++)
                {
                    float k = y / 31f;   // texture rows start at the bottom
                    float v = Mathf.Lerp(0.78f, 1f, Mathf.SmoothStep(0f, 1f, k));
                    _gradient.SetPixel(0, y, new Color(v, v, v, 1f));
                }
                _gradient.Apply();
                return _gradient;
            }
        }

        public static GUIStyle Style(int size, bool bold = true)
        {
            int key = size * 2 + (bold ? 1 : 0);
            if (Styles.TryGetValue(key, out var s)) return s;
            s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, wordWrap = false, clipping = TextClipping.Overflow };
            Styles[key] = s;
            return s;
        }

        /// <summary>A rounded panel: a shadow, a gradient body, an optional rim.</summary>
        public static void Panel(Rect r, Color colour, float radius = 22f, Color? rim = null, float rimWidth = 3f, bool shadow = true, float alpha = 1f)
        {
            if (shadow)
            {
                // One layer as dark as the three it replaced where they overlapped: every layer is a draw of the whole
                // panel, and the box's GPU spent ~20 ms a frame on Sporte!'s menu HUD (three cards).
                var sr = new Rect(r.x - 4f, r.y + 8f, r.width + 8f, r.height + 4f);
                GUI.DrawTexture(sr, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0f, 0f, 0f, 0.22f * alpha), 0f, radius + 4f);
            }
            var c = colour;
            c.a *= alpha;
            GUI.DrawTexture(r, Gradient, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
            if (rim.HasValue && rim.Value.a >= 0.2f)  // a faint rim hardly showed, and it's a draw of the whole panel
            {
                var rc = rim.Value;
                rc.a *= alpha;
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, rc, rimWidth, radius);
            }
        }

        /// <summary>A flat rounded rectangle (bars, chips).</summary>
        public static void Rounded(Rect r, Color colour, float radius)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, 0f, radius);
        }

        /// <summary>Bold text with a dark outline (readable on anything).</summary>
        public static void Text(Rect r, string text, int size, Color colour, TextAnchor anchor = TextAnchor.MiddleCenter, float outline = 3f,
                                Color? outlineColour = null, bool bold = true)
        {
            var s = Style(size, bold);
            s.alignment = anchor;
            var oc = outlineColour ?? new Color(Night.r, Night.g, Night.b, 0.9f * colour.a);
            if (outline > 0f)
            {
                s.normal.textColor = oc;
                // Each copy draws the whole text again: thin outlines (up to 3 px), and big text whose strokes are wider
                // than its outline, get by with the 4 diagonal ones
                int step = outline > 3f && size < 80 ? 1 : 2;
                for (int i = step - 1; i < 8; i += step)
                {
                    float a = i * Mathf.PI / 4f;
                    GUI.Label(new Rect(r.x + Mathf.Cos(a) * outline, r.y + Mathf.Sin(a) * outline, r.width, r.height), text, s);
                }
                if (outline > 3f)  // the drop shadow shows under thick outlines only
                {
                    var shadow = oc;
                    shadow.a *= 0.6f;
                    s.normal.textColor = shadow;
                    GUI.Label(new Rect(r.x + outline * 0.6f, r.y + outline * 2f, r.width, r.height), text, s);
                }
            }
            s.normal.textColor = colour;
            GUI.Label(r, text, s);
        }

        public static float Width(string text, int size, bool bold = true) => Style(size, bold).CalcSize(new GUIContent(text)).x;

        /// <summary>
        /// A TV scoreboard at the top left: the two sides' colour chips and names, their scores in a white box, and a
        /// line under it (a clock, a round). Returns its rectangle.
        /// </summary>
        public static Rect Scoreboard(float x, float y, string home, Color homeColour, int homeScore, string away, Color awayColour, int awayScore,
                                      string under = null, float flashHome = 0f, float flashAway = 0f)
        {
            float nameW = Mathf.Max(Width(home, 38), Width(away, 38), 90f) + 30f;
            float w = 24f + nameW * 2f + 150f, h = 72f;
            var r = new Rect(x, y, w, h);
            Panel(r, Night, 18f, new Color(1f, 1f, 1f, 0.15f));
            // the home side
            Rounded(new Rect(x + 12f, y + 14f, 10f, h - 28f), homeColour, 5f);
            Text(new Rect(x + 30f, y, nameW, h), home, 38, Color.white, TextAnchor.MiddleLeft, 2f);
            // the score box
            var box = new Rect(x + 30f + nameW, y + 9f, 136f, h - 18f);
            Rounded(box, Color.white, 12f);
            var s = Style(46);
            s.alignment = TextAnchor.MiddleCenter;
            s.normal.textColor = Color.Lerp(Night, homeColour, flashHome);
            GUI.Label(new Rect(box.x, box.y, box.width / 2f - 6f, box.height), homeScore.ToString(), s);
            s.normal.textColor = Night;
            GUI.Label(new Rect(box.x, box.y, box.width, box.height), "–", s);
            s.normal.textColor = Color.Lerp(Night, awayColour, flashAway);
            GUI.Label(new Rect(box.center.x + 6f, box.y, box.width / 2f - 6f, box.height), awayScore.ToString(), s);
            // the away side
            Text(new Rect(box.xMax + 14f, y, nameW, h), away, 38, Color.white, TextAnchor.MiddleLeft, 2f);
            Rounded(new Rect(r.xMax - 22f, y + 14f, 10f, h - 28f), awayColour, 5f);
            if (!string.IsNullOrEmpty(under))
            {
                float uw = Width(under, 28) + 40f;
                var ur = new Rect(x + 18f, y + h - 4f, uw, 42f);
                Rounded(ur, Sun, 12f);
                var us = Style(28);
                us.alignment = TextAnchor.MiddleCenter;
                us.normal.textColor = Night;
                GUI.Label(ur, under, us);
            }
            return r;
        }

        /// <summary>
        /// A big word slamming in (scaled down from big with a little overshoot), held, then sliding up and fading;
        /// <paramref name="t"/> = seconds since it came, <paramref name="life"/> = how long it stays. A ribbon in its colour
        /// behind it.
        /// </summary>
        public static void Banner(float width, float centreY, string text, Color colour, float t, float life = 1.6f, int size = 140, string sub = null)
        {
            if (t < 0f || t > life) return;
            float inK = Mathf.Clamp01(t / 0.18f);
            float scale = t < 0.18f ? Mathf.Lerp(2.1f, 0.94f, EaseOut(inK)) : t < 0.3f ? Mathf.Lerp(0.94f, 1f, (t - 0.18f) / 0.12f) : 1f;
            float outK = Mathf.Clamp01((t - (life - 0.3f)) / 0.3f);
            float alpha = Mathf.Clamp01(t / 0.08f) * (1f - outK);
            float lift = -outK * 60f;
            var old = GUI.matrix;
            var pivot = new Vector2(width / 2f, centreY + lift);
            GUI.matrix = old * Matrix4x4.TRS(pivot, Quaternion.identity, new Vector3(scale, scale, 1f)) * Matrix4x4.Translate(-pivot);
            float tw = Width(text, size) + 120f;
            var ribbon = new Rect(width / 2f - tw / 2f, centreY + lift - size * 0.42f, tw, size * 0.84f);
            var rc = colour;
            rc.a = 0.92f * alpha;
            // a slanted ribbon: two overlapping rounded bars, the back one darker
            var back = Color.Lerp(colour, Night, 0.45f);
            back.a = rc.a;
            Rounded(new Rect(ribbon.x + 14f, ribbon.y + 10f, ribbon.width, ribbon.height), back, size * 0.3f);
            Rounded(ribbon, rc, size * 0.3f);
            Text(new Rect(0f, centreY + lift - size * 0.6f, width, size * 1.2f), text, size, new Color(1f, 1f, 1f, alpha), TextAnchor.MiddleCenter, 5f,
                 new Color(Night.r, Night.g, Night.b, alpha));
            GUI.matrix = old;
            if (!string.IsNullOrEmpty(sub))
                Text(new Rect(0f, centreY + lift + size * 0.5f, width, 70f), sub, 40, new Color(1f, 1f, 1f, alpha), TextAnchor.MiddleCenter, 3f);
        }

        /// <summary>A small rounded label centred at x.</summary>
        public static void Pill(float centreX, float y, string text, Color colour, int size = 34, Color? textColour = null, float alpha = 1f)
        {
            float w = Width(text, size) + size * 1.2f, h = size * 1.6f;
            var r = new Rect(centreX - w / 2f, y, w, h);
            var c = colour;
            c.a *= alpha;
            Panel(r, c, h / 2f, null, 0f, true, alpha);
            var s = Style(size);
            s.alignment = TextAnchor.MiddleCenter;
            var tc = textColour ?? Night;
            tc.a *= alpha;
            s.normal.textColor = tc;
            GUI.Label(r, text, s);
        }

        /// <summary>A meter (speed, power): a rounded track, a gradient fill, tick marks.</summary>
        public static void Meter(Rect r, float k, Color low, Color high, string label = null)
        {
            Panel(r, Night, r.height / 2f, new Color(1f, 1f, 1f, 0.2f));
            k = Mathf.Clamp01(k);
            var inner = new Rect(r.x + 6f, r.y + 6f, Mathf.Max(r.height - 12f, (r.width - 12f) * k), r.height - 12f);
            if (k > 0.01f) GUI.DrawTexture(inner, Gradient, ScaleMode.StretchToFill, true, 0f, Color.Lerp(low, high, k), 0f, inner.height / 2f);
            for (int i = 1; i < 10; i++)
                Rounded(new Rect(r.x + r.width * i / 10f - 1f, r.y + r.height * 0.3f, 2f, r.height * 0.4f), new Color(1f, 1f, 1f, 0.25f), 1f);
            if (!string.IsNullOrEmpty(label)) Text(new Rect(r.x, r.y - 52f, r.width, 48f), label, 34, Color.white, TextAnchor.MiddleLeft, 2f);
        }

        static float EaseOut(float k) => 1f - (1f - k) * (1f - k) * (1f - k);
    }
}
