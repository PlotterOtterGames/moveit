// Stand-in for the parts of Unity's test framework (com.unity.test-framework) the tests use, so they compile outside
// Unity (see check.py). Signatures only. NUnit itself comes from its NuGet package.
using System;
using System.Text.RegularExpressions;

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)] public class UnityTestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class UnitySetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class UnityTearDownAttribute : Attribute { }

    public static class LogAssert
    {
        public static bool ignoreFailingMessages { get; set; }
        public static void Expect(LogType type, string message) { }
        public static void Expect(LogType type, Regex message) { }
        public static void NoUnexpectedReceived() { }
    }
}
