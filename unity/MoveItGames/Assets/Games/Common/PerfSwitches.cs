using System.Collections.Generic;

namespace MoveIt.Games
{
    /// <summary>
    /// PerfLab's switches that games read themselves, for measuring on the box (players only; always empty in the editor
    /// and in normal play): Tenis!'s <c>split=1</c> shows its two-player split screen on any screen, so its cost can be
    /// measured without two players.
    /// </summary>
    public static class PerfSwitches
    {
        static Dictionary<string, string> _values = new Dictionary<string, string>();

        internal static void Set(Dictionary<string, string> values) => _values = new Dictionary<string, string>(values);

        /// <summary>The switch's value, or "".</summary>
        public static string Get(string key) => _values.TryGetValue(key, out string v) ? v : "";
    }
}
