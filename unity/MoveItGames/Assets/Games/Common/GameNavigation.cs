using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MoveIt.Games
{
    /// <summary>Keys every game handles the same way: Esc = back to the menu, L = Albanian/English.</summary>
    public static class GameNavigation
    {
        public const string MenuScene = "Menu";

        /// <summary>Call from Update. Returns true if the scene is being left (stop processing this frame).</summary>
        public static bool HandleCommonKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            if (kb.lKey.wasPressedThisFrame) CommonStrings.Albanian = !CommonStrings.Albanian;
            if (kb.escapeKey.wasPressedThisFrame && SceneManager.GetActiveScene().name != MenuScene)
                return BackToMenu();
            return false;
        }

        public static bool BackToMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(MenuScene)) return false;
            SceneManager.LoadScene(MenuScene);
            return true;
        }
    }
}
