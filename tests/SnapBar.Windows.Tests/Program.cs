using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using SnapBar.Core;
using SnapBar.Windows;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(nint handle);
    [DllImport("gdi32.dll")] static extern nint CreateBitmap(int width, int height, uint planes, uint bpp, byte[] data);
    [DllImport("gdi32.dll")] static extern int GetBitmapBits(nint handle, int size, byte[] data);
    static nint window;
    static void Locked(Action action)
    {
        bool opened = false;
        for (var attempt = 0; attempt < 20 && !opened; attempt++)
        { opened = OpenClipboard(window); if (!opened) Thread.Sleep(15); }
        if (!opened) throw new Exception("Clipboard is busy; stop this test and retry when idle");
        try { action(); } finally { CloseClipboard(); }
    }
    static void SetBytes(uint format, byte[] data)
    {
        var handle = GlobalAlloc(2, (nuint)data.Length);
        if (handle == 0) throw new Exception("Allocation failed");
        if (data.Length != 0)
        {
            var pointer = GlobalLock(handle);
            Marshal.Copy(data, 0, pointer, data.Length); GlobalUnlock(handle);
        }
        if (SetClipboardData(format, handle) == 0) throw new Exception("SetClipboardData failed");
    }
    static byte[] ReadBytes(uint format)
    {
        var handle = GetClipboardData(format);
        var data = new byte[(int)GlobalSize(handle)];
        if (data.Length == 0) return data;
        var pointer = GlobalLock(handle);
        Marshal.Copy(pointer, data, 0, data.Length); GlobalUnlock(handle); return data;
    }
    static void RoundTrip(Action seed, Action verify)
    {
        Locked(() => { EmptyClipboard(); seed(); });
        using var backup = new ClipboardBackup();
        Locked(() => { var status = backup.Capture(); if (status != "ok") throw new Exception(status); });
        Locked(() => { EmptyClipboard(); SetBytes(13, Encoding.Unicode.GetBytes("temporary selection\0")); });
        Locked(() => { if (!backup.Restore()) throw new Exception("Restore failed"); verify(); });
    }
    static void Equal(byte[] actual, byte[] expected)
    { if (!actual.SequenceEqual(expected)) throw new Exception("Restored bytes differ"); }
    static void RejectedProbe(string expectedStatus, uint? expectedSequence = null, bool current = true)
    {
        bool injected = false;
        var target = Native.GetIdentity(Native.GetForegroundWindow()) ?? throw new Exception("No foreground test target");
        var reader = new ClipboardSelectionReader(window);
        var plan = new ClipboardReadPlan("test", _ => true, () => true,
            [new(() => { injected = true; return true; }, null)], "no-response");
        var response = reader.CaptureAsync(target, () => current, plan, expectedSequence).GetAwaiter().GetResult();
        if (response.Status != expectedStatus || injected || reader.IsBusy)
            throw new Exception("Probe did not reject safely: " + response.Status);
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--action-layout")) return ActionLayoutTest.Run();
        if (args.Contains("--menu-keyboard")) return MenuKeyboardTest.Run();
        if (args.Contains("--custom-actions")) return CustomActionsTest.Run();
        if (args.Contains("--custom-action-runner")) return CustomActionRunnerTest.RunAsync().GetAwaiter().GetResult();
        if (args.Contains("--action-diagnostics")) return ActionDiagnosticsTest.Run();
        if (args.Contains("--path-worker")) return PathWorkerTest.RunFake();
        if (args.Contains("--path-worker-tests")) return PathWorkerTest.RunAsync().GetAwaiter().GetResult();
        if (args.Contains("--path-actions")) return PathActionsTest.Run();
        if (args.Contains("--path-shell-smoke")) return PathShellSmokeTest.Run();
        if (args.Contains("--web-actions")) return WebActionsTest.Run();
        if (args.Contains("--browser-launch-smoke")) return BrowserLaunchTest.RunAsync().GetAwaiter().GetResult();
        if (args.Contains("--app-exclusions")) return AppExclusionTest.Run();
        if (args.Contains("--tray-lifecycle")) return TrayLifecycleTest.Run();
        if (args.Contains("--clipboard-race-writer")) return ClipboardRaceTest.RunWriter();
        if (args.Contains("--clipboard-races")) return ClipboardRaceTest.Run();
        if (args.Contains("--sublime-action")) return SublimeActionTest.Run(args);
        if (args.Contains("--uia-readiness")) return UiaReadinessTest.Run();
        if (args.Contains("--uia-deep-tree")) return UiaReadinessTest.Run(deepTree: true);
        // Explicit opt-in: these tests briefly own the real desktop clipboard.
        if (!args.Contains("--clipboard-roundtrip")) { Console.WriteLine("Pass --clipboard-roundtrip to run integration tests."); return 2; }
        using var owner = new HwndSource(new HwndSourceParameters("SnapBar Clipboard Test") { Width = 1, Height = 1 });
        window = owner.Handle;
        using var original = new ClipboardBackup();
        Locked(() => { var status = original.Capture(); if (status != "ok") throw new Exception("Original clipboard could not be preserved: " + status); });
        int passed = 0, failed = 0;
        void Check(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS " + name); passed++; }
            catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
        }
        try
        {
            var text = Encoding.Unicode.GetBytes("SnapBar 備份測試 😀\0");
            Check("Unicode text bytes survive copy/restore", () => RoundTrip(() => SetBytes(13, text), () => Equal(ReadBytes(13), text)));
            var richFormat = RegisterClipboardFormat("SnapBar.Test.RichFormat");
            var rich = Encoding.UTF8.GetBytes("<b>test</b>\0");
            Check("multiple formats survive copy/restore", () => RoundTrip(() => { SetBytes(13, text); SetBytes(richFormat, rich); },
                () => { Equal(ReadBytes(13), text); Equal(ReadBytes(richFormat), rich); }));
            var marker = RegisterClipboardFormat("SnapBar.Test.ZeroMarker");
            Check("zero-valued registered metadata survives", () => RoundTrip(() => { SetBytes(13, text); SetBytes(marker, new byte[4]); },
                () => Equal(ReadBytes(marker), new byte[4])));
            var files = Encoding.Unicode.GetBytes("D:\\codex\\snapbar\\tests\\fixtures\\selection.txt\0\0");
            var drop = new byte[20 + files.Length]; BitConverter.GetBytes(20).CopyTo(drop, 0); BitConverter.GetBytes(1).CopyTo(drop, 16); files.CopyTo(drop, 20);
            Check("file-drop list survives copy/restore", () => RoundTrip(() => SetBytes(15, drop), () => Equal(ReadBytes(15), drop)));
            var pixels = Enumerable.Range(0, 16 * 16 * 4).Select(i => (byte)(i % 251)).ToArray();
            Check("GDI bitmap pixels survive copy/restore", () => RoundTrip(() =>
                { if (SetClipboardData(2, CreateBitmap(16, 16, 1, 32, pixels)) == 0) throw new Exception("Bitmap creation failed"); },
                () => { var actual = new byte[pixels.Length]; if (GetBitmapBits(GetClipboardData(2), actual.Length, actual) != actual.Length) throw new Exception("Bitmap unavailable"); Equal(actual, pixels); }));
            var dib = new byte[40 + 1024 * 1025 * 4];
            BitConverter.GetBytes(40).CopyTo(dib, 0); BitConverter.GetBytes(1024).CopyTo(dib, 4); BitConverter.GetBytes(1025).CopyTo(dib, 8);
            BitConverter.GetBytes((short)1).CopyTo(dib, 12); BitConverter.GetBytes((short)32).CopyTo(dib, 14); dib[40] = 73;
            Check("image larger than old 4 MiB limit survives", () => RoundTrip(() => SetBytes(8, dib), () => Equal(ReadBytes(8), dib)));
            Check("empty clipboard survives copy/restore", () => RoundTrip(() => { }, () => { if (GetClipboardData(13) != 0) throw new Exception("Clipboard not empty"); }));
            Check("paste sequence change aborts before copy injection", () =>
            {
                Locked(() => { EmptyClipboard(); SetBytes(13, text); });
                var sequence = Native.GetClipboardSequenceNumber();
                RejectedProbe("clipboard-changed", unchecked(sequence + 1));
                Locked(() => Equal(ReadBytes(13), text));
                if (Native.GetClipboardSequenceNumber() != sequence) throw new Exception("Rejected probe changed clipboard");
            });
            Check("invalidated capture aborts before copy injection", () =>
                RejectedProbe("target-changed", current: false));
            Check("unsupported display format is rejected without changing clipboard", () =>
            {
                Locked(() => { EmptyClipboard(); SetBytes(0x81, text); }); // CF_DSPTEXT: not in supported list.
                var sequence = Native.GetClipboardSequenceNumber();
                using var backup = new ClipboardBackup();
                Locked(() =>
                {
                    if (backup.Capture() != "clipboard-format-unsupported") throw new Exception("Unexpected capture status");
                    Equal(ReadBytes(0x81), text);
                    if (Native.GetClipboardSequenceNumber() != sequence) throw new Exception("Rejected backup changed clipboard");
                });
                RejectedProbe("clipboard-format-unsupported");
            });
            Check("incomplete backup cannot empty or partially restore clipboard", () =>
            {
                Locked(() => { EmptyClipboard(); SetBytes(13, text); SetBytes(0x81, text); });
                var sequence = Native.GetClipboardSequenceNumber();
                using var backup = new ClipboardBackup();
                Locked(() =>
                {
                    if (backup.Capture() != "clipboard-format-unsupported" || backup.Restore())
                        throw new Exception("Partial backup was treated as restorable");
                    Equal(ReadBytes(13), text); Equal(ReadBytes(0x81), text);
                    if (Native.GetClipboardSequenceNumber() != sequence) throw new Exception("Partial restore changed clipboard");
                });
            });
            Check("a restored backup cannot empty clipboard on a second restore", () =>
            {
                Locked(() => { EmptyClipboard(); SetBytes(13, text); });
                using var backup = new ClipboardBackup();
                Locked(() =>
                {
                    if (backup.Capture() != "ok" || !backup.Restore()) throw new Exception("Initial roundtrip failed");
                    var sequence = Native.GetClipboardSequenceNumber();
                    if (backup.Restore() || Native.GetClipboardSequenceNumber() != sequence)
                        throw new Exception("Consumed backup changed clipboard");
                    Equal(ReadBytes(13), text);
                });
            });
            Check("backup above 64 MiB is rejected without changing clipboard", () =>
            {
                var largeFormat = RegisterClipboardFormat("SnapBar.Test.TooLarge");
                Locked(() => { EmptyClipboard(); SetBytes(largeFormat, new byte[64 * 1024 * 1024 + 1]); });
                var sequence = Native.GetClipboardSequenceNumber();
                using var backup = new ClipboardBackup();
                Locked(() =>
                {
                    if (backup.Capture() != "clipboard-backup-too-large") throw new Exception("Unexpected capture status");
                    if (Native.GetClipboardSequenceNumber() != sequence) throw new Exception("Rejected backup changed clipboard");
                });
                RejectedProbe("clipboard-backup-too-large");
            });
        }
        finally { Locked(() => { if (!original.Restore()) throw new Exception("Failed to restore original desktop clipboard"); }); }
        Console.WriteLine($"{passed} passed, {failed} failed; original clipboard restored");
        return failed == 0 ? 0 : 1;
    }
}
