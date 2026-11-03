using System.IO;
using System.Security;
using Microsoft.Win32;

namespace SnapBar.Windows;

public enum StartupState { Off, On, DisabledByWindows, Unavailable }
public sealed record StartupStatus(StartupState State, bool DifferentLocation = false);

public sealed class StartupRegistration
{
    private const string WindowsRoot = @"Software\Microsoft\Windows\CurrentVersion";
    private readonly string executable, root;
    private readonly Func<string, bool> validateLaunch;
    private const string ValueName = "SnapBar";
    public string Command => "\"" + executable + "\" --background";

    // Tests supply a unique root outside Windows' actual startup configuration.
    public StartupRegistration(string executable, string? registryRoot = null, Func<string, bool>? validateLaunch = null)
    {
        this.executable = Path.GetFullPath(executable);
        if (this.executable.Contains('"')) throw new ArgumentException("Invalid executable path");
        root = registryRoot ?? WindowsRoot;
        this.validateLaunch = validateLaunch ?? StartupLaunchValidator.CanLaunch;
    }

    public StartupStatus Read()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(root + @"\Run");
            var command = run?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (command == null) return new(StartupState.Off);
            if (command is not string text) return new(StartupState.Unavailable);
            using var approval = Registry.CurrentUser.OpenSubKey(root + @"\Explorer\StartupApproved\Run");
            var value = approval?.GetValue(ValueName);
            // Explorer's private approval format is read only. Unknown formats fail closed.
            if (value != null)
            {
                if (value is not byte[] bytes || bytes.Length != 12 || bytes[1] != 0 || bytes[2] != 0 || bytes[3] != 0)
                    return new(StartupState.Unavailable);
                if (bytes[0] is 3 or 7) return new(StartupState.DisabledByWindows);
                if (bytes[0] is not (2 or 6)) return new(StartupState.Unavailable);
            }
            return string.Equals(text, Command, StringComparison.OrdinalIgnoreCase)
                ? new(StartupState.On) : new(StartupState.Off, DifferentLocation: true);
        }
        catch (Exception e) when (IsAccessError(e)) { return new(StartupState.Unavailable); }
    }

    public StartupStatus SetEnabled(bool enabled)
    {
        // Never rewrite StartupApproved: system-level disabling is the user's decision.
        if (enabled)
        {
            if (!File.Exists(executable)) throw new IOException("Startup executable not found");
            if (!validateLaunch(executable)) throw new StartupLaunchException();
            using var run = Registry.CurrentUser.CreateSubKey(root + @"\Run", writable: true);
            run.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        else
        {
            using var run = Registry.CurrentUser.OpenSubKey(root + @"\Run", writable: true);
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        return Read();
    }

    public static bool IsAccessError(Exception e) => e is IOException or UnauthorizedAccessException or SecurityException;
}
