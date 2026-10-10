using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>App-wide runtime defaults for a TV box. Runs before the first scene loads.</summary>
    static class BoxRuntime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Application.targetFrameRate = 60;              // Android defaults to 30 fps
            Screen.sleepTimeout = SleepTimeout.NeverSleep; // the box is a console: no screen dimming mid-game
        }
    }
}
