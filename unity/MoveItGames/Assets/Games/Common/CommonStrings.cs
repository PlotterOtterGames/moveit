namespace MoveIt.Games
{
    /// <summary>UI text shared by all games. Albanian first (the product language), English for development. L toggles.</summary>
    public static class CommonStrings
    {
        const string LanguageKey = "settings.albanian";
        static bool? _albanian;

        /// <summary>Language choice, saved on the box (Settings, or L on the keyboard).</summary>
        public static bool Albanian
        {
            get => _albanian ??= UnityEngine.PlayerPrefs.GetInt(LanguageKey, 1) == 1;
            set
            {
                _albanian = value;
                UnityEngine.PlayerPrefs.SetInt(LanguageKey, value ? 1 : 0);
                UnityEngine.PlayerPrefs.Save();
            }
        }

        public static string T(string sq, string en) => Albanian ? sq : en;

        public static string StepIn => T("Dil para kamerës, që të të shihet i gjithë trupi", "Step in front of the camera so your whole body is visible");
        public static string HoldStill => T("Qëndro drejt dhe mos lëviz…", "Stand straight and keep still…");
        public static string RaiseHands => T("Ngri të dy duart lart për të filluar", "Raise both hands to start");
        public static string OrPressEnter => T("ose shtyp ENTER", "or press ENTER");
        public static string PressEnter => T("Shtyp ENTER për të filluar", "Press ENTER to start");
        public static string NoMotionService => T("Kamera nuk është aktive: luaj me tastierë", "Camera service not running: play with keyboard");
        public static string ComeBack => T("Kthehu para kamerës", "Come back in front of the camera");
        public static string GameOver => T("Loja mbaroi!", "Game over!");
        public static string Score => T("Pikët", "Score");
        public static string Best => T("Rekordi", "Best");
        public static string NewBest => T("REKORD I RI!", "NEW BEST!");
        public static string PlayAgain => T("Ngri duart lart për të luajtur përsëri", "Raise both hands to play again");
        public static string EscToMenu => T("Esc = menuja", "Esc = menu");
        public static string Go => T("NISU!", "GO!");
    }
}
