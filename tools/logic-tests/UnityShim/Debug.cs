// Stand-in for UnityEngine.Debug, for compiling game rules outside Unity (see LogicTests.csproj): logs go to the
// console (dotnet test shows them with --logger "console;verbosity=detailed").
namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) => System.Console.WriteLine(message);
        public static void LogWarning(object message) => System.Console.WriteLine("Warning: " + message);
        public static void LogError(object message) => System.Console.Error.WriteLine("Error: " + message);
    }
}
