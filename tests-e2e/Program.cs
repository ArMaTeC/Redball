using System.Reflection;
using System.Linq;
using NUnitLite;

namespace Redball.E2E.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var runArgs = args ?? [];

        var noWaitRequested = runArgs.Any(a =>
            string.Equals(a, "--nowait", StringComparison.OrdinalIgnoreCase));

        if (noWaitRequested)
        {
            runArgs = runArgs
                .Where(a => !string.Equals(a, "--nowait", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        else if (!runArgs.Any(a => string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase)))
        {
            runArgs = runArgs.Concat(["--wait"]).ToArray();
        }

        return new AutoRun(Assembly.GetExecutingAssembly()).Execute(runArgs);
    }
}
