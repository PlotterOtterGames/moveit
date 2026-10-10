#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoveIt.Games
{
    /// <summary>
    /// Development tool for hands-off visual checks. If <c>Temp/moveit_capture.txt</c> exists when Play starts, screenshots
    /// are saved at the times it lists (seconds after start, separated by commas or new lines) to <c>unity/captures/</c>.
    /// The folder is outside Assets so saving doesn't trigger an asset import. Delete the file to turn it off.
    /// </summary>
    static class DevCapture
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            string marker = Path.Combine(Application.dataPath, "../Temp/moveit_capture.txt");
            if (!File.Exists(marker)) return;

            var times = new List<float>();
            foreach (var part in File.ReadAllText(marker).Split(',', '\n', '\r', ' '))
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float t)) times.Add(t);
            if (times.Count == 0) return;
            times.Sort();

            var go = new GameObject("DevCapture") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Capturer>().Times = times;
        }

        sealed class Capturer : MonoBehaviour
        {
            public List<float> Times;

            IEnumerator Start()
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
                Directory.CreateDirectory(dir);
                float start = Time.realtimeSinceStartup;
                foreach (float t in Times)
                {
                    while (Time.realtimeSinceStartup - start < t) yield return null;
                    yield return new WaitForEndOfFrame();
                    string path = Path.Combine(dir, $"{SceneManager.GetActiveScene().name}_{t:00}s.png");
                    ScreenCapture.CaptureScreenshot(path);
                    Debug.Log($"[DevCapture] {path}");
                }
            }
        }
    }
}
#endif
