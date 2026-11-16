using System.Runtime.InteropServices;
using Accessibility;
using SnapBar.Core;

namespace SnapBar.Windows;

// Native metadata only. Never request accName, accValue or any text from these objects.
public sealed record NativeClipboardSource(long FocusWindow, long PointWindow, string RootClass,
    string FocusClass, int? PointRole, int? PointState, int? FocusRole, int? FocusState, bool PasswordStyle,
    int? AccessibilityError = null, int? PointChildId = null, int? FocusChildId = null);

public static class NativeClipboardSourceReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size; public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Native.Rect CaretRect;
    }

    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Native.Point point);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(nint window, uint objectId,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);

    public static NativeClipboardSource? Read(TargetIdentity target, int x, int y)
    {
        if (Native.GetIdentity(Native.GetForegroundWindow()) != target) return null;
        var thread = Native.GetWindowThreadProcessId((nint)target.Window, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(thread, ref info) || info.Focus == 0
            || !Owns(info.Focus, target)) return null;
        var point = new Native.Point { X = x, Y = y };
        var hit = WindowFromPoint(point);
        if (hit == 0 || !Owns(hit, target)) return null;
        int? pointRole = null, pointState = null, focusRole = null, focusState = null;
        int? accessibilityError = null;
        int? pointChildId = null, focusChildId = null;
        var iid = typeof(IAccessible).GUID;
        try
        {
            if (AccessibleObjectFromWindow(hit, 0xFFFFFFFC, ref iid, out var accessible) == 0)
            {
                try
                {
                    // WindowFromPoint already identifies the owned native control. Read that
                    // control's own capabilities; some native proxies omit accHitTest entirely.
                    pointRole = accessible.get_accRole(0) as int?;
                    pointState = accessible.get_accState(0) as int?;
                    pointChildId = 0;
                    // Never overwrite a protected parent with a less restrictive child state.
                    var child = IsProtected(pointState) ? null : accessible.accHitTest(x, y);
                    // Prefer more specific child metadata when the provider supplies it.
                    if (child is int childId)
                    {
                        pointChildId = childId;
                        pointRole = accessible.get_accRole(childId) as int?;
                        pointState = accessible.get_accState(childId) as int?;
                    }
                    else if (child is IAccessible hitObject)
                    {
                        try { pointRole = hitObject.get_accRole(0) as int?; pointState = hitObject.get_accState(0) as int?; }
                        finally { Marshal.ReleaseComObject(hitObject); }
                    }
                }
                finally { Marshal.ReleaseComObject(accessible); }
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or InvalidComObjectException or ArgumentException)
        { accessibilityError = e.HResult; }
        try
        {
            IAccessible accessible;
            if (AccessibleObjectFromWindow(info.Focus, 0xFFFFFFFC, ref iid, out accessible) == 0)
            {
                try
                {
                    focusRole = accessible.get_accRole(0) as int?;
                    focusState = accessible.get_accState(0) as int?;
                    focusChildId = 0;
                    var focusedChild = IsProtected(focusState) ? null : accessible.accFocus;
                    if (focusedChild is int childId)
                    { focusChildId = childId; focusRole = accessible.get_accRole(childId) as int?; focusState = accessible.get_accState(childId) as int?; }
                    else if (focusedChild is IAccessible focused)
                    {
                        try { focusRole = focused.get_accRole(0) as int?; focusState = focused.get_accState(0) as int?; }
                        finally { Marshal.ReleaseComObject(focused); }
                    }
                }
                finally { Marshal.ReleaseComObject(accessible); }
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or InvalidComObjectException or ArgumentException)
        { accessibilityError = e.HResult; }
        return new((long)info.Focus, (long)hit, Native.GetWindowClass((nint)target.Window),
            Native.GetWindowClass(info.Focus), pointRole, pointState, focusRole, focusState,
            HasPasswordStyle(info.Focus), accessibilityError, pointChildId, focusChildId);
    }

    private static bool IsProtected(int? state) => (state.GetValueOrDefault() & 0x20000000) != 0;

    private static bool HasPasswordStyle(nint window)
    {
        var name = Native.GetWindowClass(window);
        return (name.Equals("Edit", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
            && ((long)Native.GetWindowLongPtr(window, -16) & 0x20) != 0;
    }

    private static bool Owns(nint window, TargetIdentity target)
    {
        Native.GetWindowThreadProcessId(window, out var process);
        return process == target.ProcessId && Native.GetAncestor(window, 2) == (nint)target.Window;
    }

    public static string Check(TargetIdentity target, NativeClipboardSource? source)
    {
        var owned = source != null && Owns((nint)source.FocusWindow, target) && Owns((nint)source.PointWindow, target);
        var generic = NativeClipboardPolicy.Check(owned, source?.FocusWindow == source?.PointWindow,
            source?.PasswordStyle == true, source?.PointRole, source?.PointState, source?.FocusRole, source?.FocusState);
        return generic is "ok" or "password" ? generic
            : owned ? WorkChatClipboardSource.Check(target, source) : "clipboard-unavailable";
    }

    public static bool SameSource(NativeClipboardSource previous, NativeClipboardSource current) =>
        previous.FocusWindow == current.FocusWindow && previous.PointWindow == current.PointWindow
        && previous.RootClass == current.RootClass && previous.FocusClass == current.FocusClass
        && previous.PointChildId == current.PointChildId && previous.FocusChildId == current.FocusChildId
        && previous.PointRole == current.PointRole && previous.FocusRole == current.FocusRole;
}

public static class WorkChatClipboardSource
{
    public static bool IsTarget(TargetIdentity target) =>
        string.Equals(System.IO.Path.GetFileName(Native.GetExecutablePath(target)), "WXWork.exe", StringComparison.OrdinalIgnoreCase)
        && Native.GetWindowClass((nint)target.Window) == "WeWorkWindow";

    public static string Check(TargetIdentity target, NativeClipboardSource? source) =>
        WorkChatClipboardPolicy.Check(IsTarget(target), source != null && source.FocusWindow != 0 && source.PointWindow != 0,
            source?.RootClass == "WeWorkWindow", source?.PasswordStyle == true,
            source?.PointRole, source?.PointState, source?.FocusRole, source?.FocusState);

}
