using System.Diagnostics;
using System.IO;
using System.Text;
using SnapBar.Core;

namespace SnapBar.Windows;

public static class SublimeCompatibility
{
    public static string KeymapPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Sublime Text", "Packages", "User", "Default (Windows).sublime-keymap");

    public static bool IsTarget(TargetIdentity target)
    {
        try
        {
            using var process = Process.GetProcessById(target.ProcessId);
            // Portable installs use a different settings directory; leave those unsupported for now.
            var exe = process.MainModule?.FileName;
            return process.ProcessName.Equals("sublime_text", StringComparison.OrdinalIgnoreCase)
                && exe != null && !Directory.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "Data"));
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        { return false; }
    }

    public static bool Ready()
    {
        try
        {
            return File.Exists(KeymapPath) && SublimeKeymap.IsReady(File.ReadAllText(KeymapPath, Encoding.UTF8))
                && !HasOtherKeymapConflict(Path.GetDirectoryName(KeymapPath)!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { return false; }
    }

    public static void InstallBinding()
    {
        var directory = Path.GetDirectoryName(KeymapPath)!;
        if (HasOtherKeymapConflict(directory)) throw new InvalidOperationException("Sublime shortcut conflict");
        var previous = File.Exists(KeymapPath) ? File.ReadAllText(KeymapPath, Encoding.UTF8) : "[\n]\n";
        var updated = SublimeKeymap.Install(previous);
        if (previous == updated) return;
        Directory.CreateDirectory(directory);
        if (File.Exists(KeymapPath)) File.Copy(KeymapPath, KeymapPath + ".snapbar-" + DateTime.UtcNow.Ticks + ".bak");
        File.WriteAllText(KeymapPath, updated, new UTF8Encoding(false));
    }

    private static bool HasOtherKeymapConflict(string directory) => Directory.Exists(directory)
        && Directory.EnumerateFiles(directory, "Default*.sublime-keymap").Any(file =>
            !file.Equals(KeymapPath, StringComparison.OrdinalIgnoreCase)
            && SublimeKeymap.HasChord(File.ReadAllText(file, Encoding.UTF8)));
}
