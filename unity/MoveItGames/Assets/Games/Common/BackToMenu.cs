using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>For scenes without their own game script (e.g. the camera test): Esc / remote Back returns to the menu.</summary>
    public sealed class BackToMenu : MonoBehaviour
    {
        void Update() => GameNavigation.HandleCommonKeys();
    }
}
