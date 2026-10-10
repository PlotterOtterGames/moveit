using System;
using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>
    /// The product's name and colors, loaded from <c>Assets/Resources/brand.json</c>. The same file feeds the Android
    /// build settings (editor) and the boot animation / icon generator (<c>tools/branding</c>), so renaming the product
    /// means editing one file.
    /// </summary>
    [Serializable]
    public sealed class Brand
    {
        public string name = "Tundu";
        public string company = "Tundu";
        public string androidPackage = "al.tundu.games";
        public string taglineSq = "Luaj me gjithë trupin!";
        public string taglineEn = "Play with your whole body!";
        public string primary = "#FFC93C";     // Sun: focus, highlights, the figure in the logo
        public string secondary = "#FF5A4E";   // Coral: the logo's head, warnings, second accent
        public string accent = "#31C6F4";      // Sky
        public string grape = "#7B61FF";
        public string leaf = "#6FD66A";        // success, "well done"
        public string background = "#141938";  // Night: TV screens
        public string surface = "#232B5E";     // cards and panels on Night
        public string text = "#FFFFFF";
        public string textMuted = "#B8C0E6";
        public string[] players = { "#FFC93C", "#31C6F4", "#FF5A4E", "#6FD66A" };
        public string fontDisplay = "Fredoka";
        public string fontBody = "Nunito";

        static Brand _current;

        public static Brand Current => _current ??= Load();

        public string Tagline => CommonStrings.Albanian ? taglineSq : taglineEn;
        public Color PrimaryColor => Parse(primary, new Color(1f, 0.79f, 0.24f));
        public Color SecondaryColor => Parse(secondary, new Color(1f, 0.35f, 0.31f));
        public Color AccentColor => Parse(accent, new Color(0.19f, 0.78f, 0.96f));
        public Color BackgroundColor => Parse(background, new Color(0.08f, 0.1f, 0.22f));
        public Color SurfaceColor => Parse(surface, new Color(0.14f, 0.17f, 0.37f));
        public Color TextColor => Parse(text, Color.white);
        public Color MutedTextColor => Parse(textMuted, new Color(0.72f, 0.75f, 0.9f));

        /// <summary>Player 1-4 colors (index 0-3), e.g. for Bowling's multi-player scores.</summary>
        public Color PlayerColor(int index) =>
            players != null && players.Length > 0 ? Parse(players[Mathf.Abs(index) % players.Length], PrimaryColor) : PrimaryColor;

        static Brand Load()
        {
            var asset = Resources.Load<TextAsset>("brand");
            if (asset == null) return new Brand();
            try
            {
                return JsonUtility.FromJson<Brand>(asset.text) ?? new Brand();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Brand] Could not read brand.json: {e.Message}");
                return new Brand();
            }
        }

        static Color Parse(string hex, Color fallback) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }
}
