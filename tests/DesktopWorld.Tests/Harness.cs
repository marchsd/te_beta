using System.Reflection;

namespace DesktopWorld.Tests;

[AttributeUsage(AttributeTargets.Method)] public sealed class FactAttribute : Attribute { }

public static class Assert
{
    public static void True(bool c, string? msg = null) { if (!c) throw new Exception(msg ?? "Expected true"); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    public static void Near(double expected, double actual, double eps = 1e-9) { if (Math.Abs(expected - actual) > eps) throw new Exception($"Expected {expected}, got {actual}"); }
}

public static class Program
{
    public static int Main()
    {
        int pass = 0, fail = 0;
        foreach (var t in Assembly.GetExecutingAssembly().GetTypes())
            foreach (var m in t.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() != null))
            {
                try { m.Invoke(Activator.CreateInstance(t), null); pass++; Console.WriteLine($"  PASS {t.Name}.{m.Name}"); }
                catch (Exception ex) { fail++; Console.WriteLine($"  FAIL {t.Name}.{m.Name}: {(ex.InnerException ?? ex).Message}"); }
            }
        Console.WriteLine($"\n{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }
}
