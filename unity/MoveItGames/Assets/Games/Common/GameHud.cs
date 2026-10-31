using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>IMGUI helpers for gray-box HUDs, laid out on a virtual 1080-pixel-high screen so text scales on any TV.</summary>
    public static class GameHud
    {
        public const float Height = 1080f;

        static GUIStyle _big, _medium, _small;

        public static GUIStyle Big { get { EnsureStyles(); return _big; } }
        public static GUIStyle Medium { get { EnsureStyles(); return _medium; } }
        public static GUIStyle Small { get { EnsureStyles(); return _small; } }

        /// <summary>Call at the start of OnGUI. Returns the virtual screen width.</summary>
        public static float Begin()
        {
            float scale = Screen.height / Height;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            return Screen.width / scale;
        }

        /// <summary>Converts a screen-pixel position (e.g. from Camera.WorldToScreenPoint) to virtual GUI coordinates.</summary>
        public static Vector2 FromScreen(Vector3 screen)
        {
            float scale = Screen.height / Height;
            return new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
        }

        /// <summary>Text with a drop shadow for readability on game scenes. Pass shadow: false for dark text on light boxes.</summary>
        public static void Text(Rect r, string text, GUIStyle style, TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null, bool shadow = true)
        {
            style.alignment = anchor;
            if (shadow)
            {
                style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), text, style);
            }
            style.normal.textColor = color ?? Color.white;
            GUI.Label(r, text, style);
        }

        /// <summary>Big centered message, with an optional smaller line underneath.</summary>
        public static void Message(float width, string title, string subtitle = null, Color? color = null)
        {
            Text(new Rect(0, 300, width, 140), title, title.Length > 26 ? Medium : Big, TextAnchor.MiddleCenter, color);
            if (!string.IsNullOrEmpty(subtitle)) Text(new Rect(0, 450, width, 80), subtitle, Small);
        }

        public static void Box(Rect r, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>
        /// A line in virtual coordinates. Rotates inside the HUD's scale (GUIUtility.RotateAroundPivot takes its pivot in
        /// screen pixels, so under a scaled GUI.matrix it turns lines around the wrong point).
        /// </summary>
        public static void Line(Vector2 a, Vector2 b, Color color, float width)
        {
            var d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) return;
            var matrix = GUI.matrix;
            var old = GUI.color;
            GUI.matrix = matrix * Matrix4x4.TRS(a, Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg), Vector3.one);
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, -width / 2f, length, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = old;
        }

        /// <summary>Three chevrons pointing left (-1) or right (+1) that light up one after another toward the tip: "go this way".</summary>
        public static void Chevrons(Vector2 center, int direction, float size, Color color)
        {
            float phase = Time.unscaledTime * 1.6f;
            float arm = size * 0.45f, stroke = size * 0.16f;
            for (int i = 0; i < 3; i++)
            {
                var c = color;
                c.a *= Mathf.Lerp(1f, 0.25f, Mathf.Repeat(phase - i / 3f, 1f));
                var tip = new Vector2(center.x + direction * ((i - 1) * size * 0.55f + size * 0.25f), center.y);
                var top = new Vector2(tip.x - direction * arm, center.y - arm);
                var bottom = new Vector2(tip.x - direction * arm, center.y + arm);
                var shadow = new Color(0f, 0f, 0f, 0.5f * c.a);
                var offset = new Vector2(3f, 3f);
                Line(tip + offset, top + offset, shadow, stroke);
                Line(tip + offset, bottom + offset, shadow, stroke);
                Line(tip, top, c, stroke);
                Line(tip, bottom, c, stroke);
            }
        }

        /// <summary>A short message on a dark bar with a coloured edge: a warning that mustn't hide the game.</summary>
        public static void Banner(Rect r, string text, Color color)
        {
            Box(r, new Color(0f, 0f, 0f, 0.6f));
            Box(new Rect(r.x, r.yMax - 5f, r.width, 5f), color);
            Text(r, text, Medium, TextAnchor.MiddleCenter, color);
        }

        static void EnsureStyles()
        {
            if (_big != null) return;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 96, fontStyle = FontStyle.Bold, wordWrap = true };
            _medium = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 32, wordWrap = true };
        }
    }
}
