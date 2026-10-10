using System.Collections.Generic;

namespace MoveIt.Games
{
    public enum GameTier { Free, Pass }

    /// <summary>Whether a game tile shows, opens, or shows a Game Pass lock.</summary>
    public enum Access { Hidden, Play, Locked }

    /// <summary>One entry of the signed game catalog (the channel manifest's "games", see docs/cloud.md).</summary>
    public struct GameRule
    {
        public string Id;
        public GameTier Tier;
        /// <summary>Hidden on app versions below this (e.g. one where the game crashes).</summary>
        public int MinApp;
        /// <summary>Kill switch: hidden everywhere.</summary>
        public bool Hidden;
        /// <summary>Unix seconds when it appears (scheduled launch); 0 = now.</summary>
        public long From;
    }

    /// <summary>
    /// Which games a box shows and opens: the signed catalog from the cloud where it says something, else the
    /// built-in defaults below. No engine code, so it is tested outside Unity (tools/logic-tests).
    /// </summary>
    public static class GameAccess
    {
        /// <summary>
        /// Tiers for games the catalog doesn't mention, e.g. on a box that has never been online. Until Game Pass
        /// launches every game is free. At launch, return GameTier.Pass here for the Game Pass games too, in the same
        /// release that the catalog marks them, so a box that was never online can't open them for free.
        /// </summary>
        public static GameTier DefaultTier(string id) => GameTier.Free;

        /// <param name="appVersion">This app's versionCode (0 in the editor).</param>
        /// <param name="now">Trusted time from the box's PassClock (unix seconds).</param>
        public static Access For(string id, IReadOnlyList<GameRule> catalog, int appVersion, long now, bool passActive)
        {
            GameTier tier = DefaultTier(id);
            if (catalog != null)
            {
                foreach (var rule in catalog)
                {
                    if (rule.Id != id) continue;
                    if (rule.Hidden || appVersion < rule.MinApp || now < rule.From) return Access.Hidden;
                    tier = rule.Tier;
                    break;
                }
            }
            return tier == GameTier.Pass && !passActive ? Access.Locked : Access.Play;
        }

        /// <summary>Catalog id of a game scene ("Runner" -> "runner").</summary>
        public static string IdOf(string scene) => scene.ToLowerInvariant();
    }
}
