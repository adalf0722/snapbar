using System.Runtime.InteropServices;

namespace SnapBar.Windows;

// Caller holds the clipboard lock. Each duplicate remains ours until SetClipboardData succeeds.
internal sealed class ClipboardBackup : IDisposable
{
    private const long MaxBytes = 64L * 1024 * 1024;
    private readonly List<(uint Format, nint Handle)> entries = [];
    private bool complete;
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap
    { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
    [DllImport("ole32.dll")] private static extern nint OleDuplicateData(nint source, ushort format, uint flags);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(nint handle, int size, out Bitmap bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] private static extern uint GetPaletteEntries(nint palette, uint start, uint count, nint entries);
    [DllImport("gdi32.dll")] private static extern uint GetEnhMetaFileBits(nint metafile, uint size, nint buffer);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern nint CopyEnhMetaFile(nint metafile, string? file);
    [DllImport("gdi32.dll")] private static extern bool DeleteEnhMetaFile(nint metafile);

    public string Capture()
    {
        // A retry must start a fresh snapshot, never append to a partial backup.
        Dispose();
        long total = 0;
        uint format = 0;
        while (true)
        {
            format = EnumClipboardFormats(format);
            if (format == 0)
            {
                complete = Marshal.GetLastPInvokeError() == 0;
                return complete ? "ok" : "clipboard-backup-failed";
            }
            var source = GetClipboardData(format);
            if (source == 0) return "clipboard-format-unavailable";
            long size;
            if (format == 2) // CF_BITMAP is a GDI object, never HGLOBAL.
            {
                if (GetObject(source, Marshal.SizeOf<Bitmap>(), out var bitmap) == 0) return "clipboard-backup-failed";
                size = Math.Abs((long)bitmap.Height * bitmap.WidthBytes);
            }
            else if (format == 9) size = GetPaletteEntries(source, 0, 0, 0) * 4L;
            else if (format == 14) size = GetEnhMetaFileBits(source, 0, 0);
            else if (format is 1 or 7 or 8 or 13 or 15 or 16 or 17 || format >= 0xC000)
            {
                size = checked((long)GlobalSize(source));
                if (size == 0) return "clipboard-format-unavailable";
            }
            else return "clipboard-format-unsupported";
            total += size;
            if (total > MaxBytes) return "clipboard-backup-too-large";
            var copy = format == 14 ? CopyEnhMetaFile(source, null)
                : OleDuplicateData(source, (ushort)format, 2);
            if (copy == 0) return "clipboard-backup-failed";
            entries.Add((format, copy));
        }
    }

    public bool Restore()
    {
        // An incomplete snapshot must never empty the user's clipboard.
        if (!complete) return false;
        if (!EmptyClipboard()) return false;
        complete = false;
        bool success = true;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (SetClipboardData(entry.Format, entry.Handle) == 0) success = false;
            else entries[i] = (entry.Format, 0);
        }
        return success;
    }

    public void Dispose()
    {
        foreach (var (format, handle) in entries)
            if (handle != 0)
            {
                if (format is 2 or 9) DeleteObject(handle);
                else if (format == 14) DeleteEnhMetaFile(handle);
                else GlobalFree(handle);
            }
        entries.Clear();
        complete = false;
    }
}
