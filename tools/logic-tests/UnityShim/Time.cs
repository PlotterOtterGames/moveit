// Stand-in for UnityEngine.Time, for compiling game rules outside Unity (see LogicTests.csproj).
// The clocks run on real time from the first read; deltaTime is a steady 60 fps frame. Rules that should be
// tested deterministically take their times as arguments (like HandInterpolator) instead of reading Time.
using System.Diagnostics;

namespace UnityEngine
{
    public static class Time
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();

        public static double unscaledTimeAsDouble => Clock.Elapsed.TotalSeconds;
        public static float unscaledTime => (float)unscaledTimeAsDouble;
        public static double timeAsDouble => unscaledTimeAsDouble * timeScale;
        public static float time => (float)timeAsDouble;
        public static double realtimeSinceStartupAsDouble => unscaledTimeAsDouble;
        public static float realtimeSinceStartup => unscaledTime;
        public static float unscaledDeltaTime => 1f / 60f;
        public static float deltaTime => unscaledDeltaTime * timeScale;
        public static float smoothDeltaTime => deltaTime;
        public static float timeScale { get; set; } = 1f;
        public static int frameCount => (int)(unscaledTimeAsDouble * 60.0);
    }
}
