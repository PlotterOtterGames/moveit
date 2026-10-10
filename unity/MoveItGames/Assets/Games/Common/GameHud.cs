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

        static void EnsureStyles()
        {
            if (_big != null) return;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 96, fontStyle = FontStyle.Bold, wordWrap = true };
            _medium = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 32, wordWrap = true };
        }
    }
}
