// The quick runner for tools/logic-tests/quick/run.sh: every [Test] (not [Explicit]) of the compiled tests, by reflection.
using System;
using System.Linq;
using System.Reflection;

public static class Runner
{
    public static int Main(string[] args)
    {
        return Tests(args);
    }

    static int Tests(string[] args)
    {
        int pass = 0, fail = 0;
        string filter = args.Length > 0 ? args[0] : null;
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            var tests = type.GetMethods().Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "TestAttribute")
                                                     && !m.GetCustomAttributes().Any(a => a.GetType().Name == "ExplicitAttribute")).ToList();
            if (tests.Count == 0) continue;
            foreach (var m in tests)
            {
                if (filter != null && !m.Name.Contains(filter) && !type.Name.Contains(filter)) continue;
                var obj = Activator.CreateInstance(type);
                var setup = type.GetMethods().FirstOrDefault(x => x.GetCustomAttributes().Any(a => a.GetType().Name == "SetUpAttribute"));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    setup?.Invoke(obj, null);
                    m.Invoke(obj, null);
                    pass++;
                    Console.WriteLine($"PASS {type.Name}.{m.Name} ({sw.ElapsedMilliseconds} ms)");
                }
                catch (TargetInvocationException e)
                {
                    fail++;
                    var msg = e.InnerException?.Message ?? e.Message;
                    Console.WriteLine($"FAIL {type.Name}.{m.Name}: {msg.Split('\n')[0]}{(msg.Contains('\n') ? " | " + string.Join(" | ", msg.Split('\n').Skip(1).Take(3)) : "")}");
                }
            }
        }
        Console.WriteLine($"{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }
}
