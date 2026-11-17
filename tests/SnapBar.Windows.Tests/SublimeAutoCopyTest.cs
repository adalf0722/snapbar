using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

// Opt-in real Sublime test. Coordinates must come from an observed disposable fixture.
// Never edits the document, keymap or persisted preferences; preserves the clipboard.
internal static class SublimeAutoCopyTest
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    internal static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--auto-copy-gesture");
        if (index + 4 >= args.Length) throw new ArgumentException("Expected screen x1 y1 x2 y2 after --auto-copy-gesture");
        var x1 = int.Parse(args[index + 1]); var y1 = int.Parse(args[index + 2]);
        var x2 = int.Parse(args[index + 3]); var y2 = int.Parse(args[index + 4]);
        var window = Native.GetForegroundWindow();
        var target = Native.GetIdentity(window);
        var title = new StringBuilder(1024); GetWindowText(window, title, title.Capacity);
        var fixture = System.IO.Path.GetFullPath("tests/fixtures/sublime-actions.txt");
        if (target == null || !SublimeCompatibility.IsTarget(target)
            || !title.ToString().StartsWith(fixture + " ", StringComparison.OrdinalIgnoreCase)
            || !Native.GetWindowRect(window, out var rect)
            || x1 < rect.Left || x1 >= rect.Right || y1 < rect.Top || y1 >= rect.Bottom
            || x2 < rect.Left || x2 >= rect.Right || y2 < rect.Top || y2 >= rect.Bottom)
            throw new InvalidOperationException("Foreground and coordinates must belong to the dedicated Sublime fixture");
        if (!SublimeCompatibility.Ready()) throw new InvalidOperationException("Existing guarded Sublime bindings required");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exit = 1; var passed = 0;
        app.Startup += async (_, _) =>
        {
            using var owner = new HwndSource(new HwndSourceParameters("Sublime auto-copy test") { WindowStyle = 0 });
            using var backup = new ClipboardBackup(); var saved = false;
            void Locked(Action action)
            {
                if (!OpenClipboard(owner.Handle)) throw new InvalidOperationException("Clipboard busy");
                try { action(); } finally { CloseClipboard(); }
            }
            void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException(name);
                passed++; Console.WriteLine("PASS " + name);
            }
            try
            {
                Locked(() => { if (backup.Capture() != "ok") throw new InvalidOperationException("Clipboard backup failed"); saved = true; });
                using var controller = new SelectionController(owner, TestPaths.ProbeExecutable);
                controller.SublimeClipboardEnabled = true;
                controller.Automatic = true;
                controller.Status += message => Console.WriteLine("STATUS " + message);
                bool Current() => Native.GetIdentity(Native.GetForegroundWindow()) == target;
                async Task Gesture(string controlTiming, int releaseDelay)
                {
                    Check(Current(), "disposable Sublime fixture retains foreground");
                    // Keep the hook-owning dispatcher free while input is injected.
                    await Task.Run(() =>
                    {
                        void RequireSource()
                        { if (!Current()) throw new InvalidOperationException("Source changed during gesture"); }
                        RequireSource();
                        // Collapse old ranges before Ctrl can add a range.
                        SetCursorPos(x1, y1); mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0);
                        Thread.Sleep(550); RequireSource();
                        if (controlTiming == "before") keybd_event(0x11, 0, 0, 0);
                        Thread.Sleep(40); RequireSource();
                        mouse_event(2, 0, 0, 0, 0);
                        for (var n = 1; n <= 10; n++)
                        {
                            RequireSource();
                            SetCursorPos(x1 + (x2 - x1) * n / 10, y1 + (y2 - y1) * n / 10);
                            if (n == 5 && controlTiming == "during") keybd_event(0x11, 0, 0, 0);
                            Thread.Sleep(15);
                        }
                        mouse_event(4, 0, 0, 0, 0);
                        Thread.Sleep(releaseDelay);
                        if (controlTiming != "none") keybd_event(0x11, 0, 2, 0);
                    });
                    await Task.Delay(3000);
                }
                foreach (var controlTiming in new[] { "none", "before", "during" })
                    foreach (var release in new[] { 50, 1100 })
                        for (var n = 0; n < 3; n++)
                        {
                            controller.ConfigureAutoCopy(new(true, AutoCopyMode.RequireControl, false));
                            Clipboard.SetText("before sublime fixture");
                            await Gesture(controlTiming, release);
                            var popup = app.Windows.OfType<PopupWindow>().Any(w => w.IsVisible);
                            var snapshot = controller.ActiveSelection;
                            var copied = snapshot is { Text.Length: > 0 } && Clipboard.GetText() == snapshot.Text;
                            Console.WriteLine($"OBSERVED controlTiming={controlTiming}; releaseMs={release}; toolbar={popup}; copied={copied}; captured={snapshot != null}");
                            if (controlTiming == "before" && !popup)
                            {
                                Check(snapshot == null, "rejected Sublime Ctrl-before-drag exposes no candidate");
                                Check(Clipboard.GetText() == "before sublime fixture", "rejected Sublime Ctrl-before-drag preserves clipboard");
                            }
                            else
                            {
                                Check(popup && snapshot?.Source == SublimeSelectionAdapter.Source, "Sublime single drag presents toolbar");
                                Check(controlTiming != "none" ? copied : Clipboard.GetText() == "before sublime fixture", "Sublime single drag respects Ctrl copying intent");
                            }
                        }
                exit = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error.Message); }
            finally
            {
                keybd_event(0x11, 0, 2, 0); mouse_event(4, 0, 0, 0, 0);
                if (saved) Locked(() => { if (!backup.Restore()) { exit = 1; Console.WriteLine("FAIL clipboard cleanup"); } });
                Console.WriteLine($"{passed} passed, {(exit == 0 ? 0 : 1)} failed");
                app.Shutdown();
            }
        };
        app.Run(); return exit;
    }
}
