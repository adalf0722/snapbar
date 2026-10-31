using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using SnapBar.Windows;

internal static class DesktopPreflight
{
    [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder value, uint length, out uint needed);

    private static string Name(nint desktop)
    {
        var name = new StringBuilder(256);
        if (!GetUserObjectInformation(desktop, 2, name, 512, out _)) throw new InvalidOperationException("Cannot inspect the input desktop.");
        return name.ToString();
    }

    public static int Run()
    {
        try
        {
            if (!Environment.UserInteractive || Native.GetForegroundWindow() == 0)
                throw new InvalidOperationException("An unlocked, interactive Windows desktop is required.");
            var desktop = OpenInputDesktop(0, false, 1);
            if (desktop == 0) throw new InvalidOperationException("Cannot access the input desktop. Unlock the session and retry.");
            try
            {
                if (Name(desktop) != Name(GetThreadDesktop(GetCurrentThreadId())))
                    throw new InvalidOperationException("The test process is not on the active input desktop.");
            }
            finally { CloseDesktop(desktop); }
            using var owner = new HwndSource(new HwndSourceParameters("SnapBar desktop preflight") { Width = 1, Height = 1 });
            if (!Native.RegisterHotKey(owner.Handle, 91, 0x4000 | 1 | 2, 0x56))
                throw new InvalidOperationException("Ctrl+Alt+V is occupied. Exit SnapBar from its tray/settings and retry.");
            Native.UnregisterHotKey(owner.Handle, 91);
            Console.WriteLine("PASS interactive desktop and Ctrl+Alt+V preconditions");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL desktop preflight: " + error.Message); return 1; }
    }
}
