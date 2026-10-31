using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SnapBar.Core;

namespace SnapBar.Windows;

public static class ClipboardSelectionAdapter
{
    public const string Source = "clipboard";
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);

    public static bool IsTarget(TargetIdentity target)
    {
        var path = Native.GetExecutablePath(target);
        if (path == null || SublimeCompatibility.IsTarget(target)) return false;
        var windowClass = Native.GetWindowClass((nint)target.Window);
        if (windowClass.Length == 0 || windowClass is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS") return false;
        var name = Path.GetFileNameWithoutExtension(path);
        return !new[] { "sublime_text", "cmd", "powershell", "pwsh", "WindowsTerminal", "OpenConsole", "conhost", "mintty", "wezterm-gui", "alacritty", "Hyper" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    public static ClipboardReadPlan ReadPlan(TargetIdentity expected) => new(Source,
        target => target == expected && IsTarget(target), () => true,
        [new(() => Native.SendControlKey(0x43), null)], "no-selection", () => RejectCopiedData(expected));

    // Called only while ClipboardSelectionReader owns the clipboard lock. Never log this metadata.
    private static string? RejectCopiedData(TargetIdentity target)
    {
        foreach (var name in new[] { "MSDEVLineSelect", "VisualStudioEditorOperationsLineCutCopyClipboardTag" })
            if (IsClipboardFormatAvailable(RegisterClipboardFormat(name))) return "no-selection";
        var chromiumFormat = RegisterClipboardFormat("Chromium Web Custom MIME Data Format");
        var processName = Path.GetFileNameWithoutExtension(Native.GetExecutablePath(target));
        var requiresMetadata = new[] { "Code", "Code - Insiders", "Cursor", "Windsurf", "VSCodium", "Trae" }.Contains(processName, StringComparer.OrdinalIgnoreCase);
        if (IsClipboardFormatAvailable(chromiumFormat))
        {
            var data = ReadFormat(chromiumFormat, 1048576);
            if (data == null || !ClipboardSelectionPolicy.IsChromiumSelectionData(data, requiresMetadata)) return "clipboard-selection-unverified";
        }
        var format = RegisterClipboardFormat("vscode-editor-data");
        if (!IsClipboardFormatAvailable(format)) return requiresMetadata && !IsClipboardFormatAvailable(chromiumFormat) ? "clipboard-selection-unverified" : null;
        var bytes = ReadFormat(format, 65536);
        if (bytes == null) return "clipboard-selection-unverified";
        try
        {
            var metadata = new UTF8Encoding(false, true).GetString(bytes).TrimEnd('\0');
            return ClipboardSelectionPolicy.IsSelectionMetadata(metadata) ? null : "no-selection";
        }
        catch (DecoderFallbackException) { return "clipboard-selection-unverified"; }
    }

    private static byte[]? ReadFormat(uint format, int maximum)
    {
        var memory = GetClipboardData(format); var size = memory == 0 ? 0 : GlobalSize(memory);
        if (size == 0 || size > (nuint)maximum) return null;
        var pointer = GlobalLock(memory); if (pointer == 0) return null;
        try { var bytes = new byte[(int)size]; Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes; }
        finally { GlobalUnlock(memory); }
    }
}
