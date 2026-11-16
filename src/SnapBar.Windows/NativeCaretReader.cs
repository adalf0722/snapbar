using System.Runtime.InteropServices;
using Accessibility;
using SnapBar.Core;

namespace SnapBar.Windows;

public sealed record NativeCaret(CaretIdentity Identity, int? Role, int? State);

public static class NativeCaretReader
{
    [StructLayout(LayoutKind.Sequential)] private struct GuiInfo
    {
        public int Size; public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Native.Rect CaretRect;
    }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint window, ref Native.Point point);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(nint window, uint objectId,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);

    // No COM calls in the final UI-thread check. Blink visibility is not an editability signal.
    public static CaretIdentity? ReadIdentity(TargetIdentity target)
    {
        if (Native.GetIdentity(Native.GetForegroundWindow()) != target) return null;
        var info = new GuiInfo { Size = Marshal.SizeOf<GuiInfo>() };
        if (!GetGUIThreadInfo(Native.GetWindowThreadProcessId((nint)target.Window, out _), ref info)
            || info.Caret == 0 || info.Focus != info.Caret || info.Capture != 0 || info.MenuOwner != 0 || info.MoveSize != 0
            || (info.Flags & 0x1E) != 0)
            return null;
        Native.GetWindowThreadProcessId(info.Caret, out var process);
        if (process != target.ProcessId || Native.GetAncestor(info.Caret, 2) != (nint)target.Window) return null;
        var rect = info.CaretRect;
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return null;
        return new((long)info.Caret, new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
    }

    public static NativeCaret? Read(TargetIdentity target)
    {
        var identity = ReadIdentity(target);
        if (identity == null) return null;
        var iid = typeof(IAccessible).GUID;
        try
        {
            if (AccessibleObjectFromWindow((nint)identity.Window, 0xFFFFFFF8, ref iid, out var accessible) != 0) return null;
            try { return new(identity, accessible.get_accRole(0) as int?, accessible.get_accState(0) as int?); }
            finally { Marshal.ReleaseComObject(accessible); }
        }
        catch (Exception error) when (error is COMException or ArgumentException or InvalidCastException or InvalidComObjectException)
        { return null; }
    }

    public static CaretIdentity? TryBind(TargetIdentity target, NativeClipboardSource source, int x, int y)
    {
        if (!WorkChatClipboardSource.IsTarget(target) || NativeClipboardSourceReader.Check(target, source) != "ok") return null;
        if (((source.PointState.GetValueOrDefault() | source.FocusState.GetValueOrDefault()) & 0x40) != 0) return null;
        var caret = Read(target);
        var point = new Native.Point { X = x, Y = y };
        if (caret == null || !ScreenToClient((nint)caret.Identity.Window, ref point)) return null;
        var scale = Native.GetDpiForWindow((nint)target.Window) / 96.0;
        // A double click lands inside the word, while the caret is at its endpoint.
        // Keep the row check tighter than the bounded horizontal allowance.
        var margin = Math.Clamp(scale * 8, 4, 32);
        var horizontal = Math.Clamp(scale * 32, 8, 64);
        return ClipboardWritePolicy.CanBindCaret(true, true, source.FocusWindow, source.PointWindow,
            caret.Identity, caret.Role, caret.State, point.X, point.Y, margin, horizontal) ? caret.Identity : null;
    }

    public static bool Matches(TargetIdentity target, CaretIdentity expected) => Read(target) is { } current
        && current.Identity == expected && current.Role == 7 && current.State is { } state
        && (state & (0x20000000 | 0x40 | 0x8000 | 0x10000 | 0x1)) == 0;
}
