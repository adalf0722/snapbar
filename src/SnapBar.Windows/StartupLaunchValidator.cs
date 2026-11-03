using System.Diagnostics;
using System.IO;

namespace SnapBar.Windows;

public static class StartupLaunchValidator
{
    public static bool CanLaunch(string executable)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--startup-check");
        // Explorer does not inherit the SDK/runtime overrides used by build and test shells.
        foreach (var name in start.Environment.Keys.Where(name => name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("COREHOST_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(name);
        start.Environment["DOTNET_DISABLE_GUI_ERRORS"] = "1";
        try
        {
            using var process = Process.Start(start);
            if (process == null) return false;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000)) { process.Kill(); process.WaitForExit(1000); return false; }
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return false; }
    }
}

public sealed class StartupLaunchException() : IOException("The application cannot start independently of the current development environment.");
