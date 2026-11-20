using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace WhyDidItBuild.Tests;

internal static class SdkMsBuildResolver
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var sdk = FindSdkDirectory();
        if (sdk is null)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            if (name.Name is not ("Microsoft.Build" or "Microsoft.Build.Framework" or "Microsoft.NET.StringTools"))
            {
                return null;
            }

            var path = Path.Combine(sdk, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
    }

    private static string? FindSdkDirectory()
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet", "--list-sdks")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(start);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        string? chosen = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var open = line.IndexOf('[');
            var close = line.IndexOf(']');
            if (open < 0 || close < open)
            {
                continue;
            }

            var version = line[..open].Trim();
            var root = line[(open + 1)..close];
            chosen = Path.Combine(root, version);
        }

        return chosen is not null && File.Exists(Path.Combine(chosen, "Microsoft.Build.dll")) ? chosen : null;
    }
}
