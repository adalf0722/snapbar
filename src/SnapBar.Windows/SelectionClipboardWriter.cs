using System.Runtime.InteropServices;
using System.Text;

namespace SnapBar.Windows;

// No await or retry while holding the clipboard. The sequence check and write share one lock.
public static class SelectionClipboardWriter
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint data);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);

    public static bool TryWrite(nint owner, string text, uint expectedSequence, Func<bool> isCurrent)
    {
        if (owner == 0 || text.Length == 0 || !isCurrent()) return false;
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = GlobalAlloc(0x42, (nuint)bytes.Length);
        if (memory == 0) return false;
        try
        {
            var pointer = GlobalLock(memory);
            if (pointer == 0) return false;
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            if (!OpenClipboard(owner)) return false;
            try
            {
                if (Native.GetClipboardSequenceNumber() != expectedSequence || !isCurrent()) return false;
                if (!EmptyClipboard() || SetClipboardData(13, memory) == 0) return false;
                memory = 0; // Windows now owns the movable memory.
                return true;
            }
            finally { CloseClipboard(); }
        }
        finally { if (memory != 0) GlobalFree(memory); }
    }
}
