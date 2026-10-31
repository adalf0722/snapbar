using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

// Explicit desktop integration driver. Operates ONLY on the disposable Sublime fixture
// already in the foreground. Calls the same capture/action entry points as the toolbar;
// it does not claim to automate a physical toolbar click.
internal static class SublimeActionTest
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--sublime-action");
        if (index + 1 >= args.Length || !Enum.TryParse<TextAction>(args[index + 1], true, out var action)
            || action is not (TextAction.Copy or TextAction.Cut or TextAction.Paste))
            throw new ArgumentException("Expected Copy, Cut or Paste after --sublime-action");
        if (action == TextAction.Paste && !args.Contains("--expect-rejected") && !args.Contains("--after-action-file"))
            throw new ArgumentException("Paste requires --after-action-file: verify the fixture buffer before signaling clipboard cleanup");
        var foreground = Native.GetForegroundWindow();
        var title = new StringBuilder(1024); GetWindowText(foreground, title, title.Capacity);
        var target = Native.GetIdentity(foreground);
        var fixture = System.IO.Path.GetFullPath("tests/fixtures/sublime-actions.txt");
        if (target == null || !SublimeCompatibility.IsTarget(target)
            || !title.ToString().StartsWith(fixture + " ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Foreground must be the dedicated sublime-actions.txt fixture");
        if (!SublimeCompatibility.Ready()) throw new InvalidOperationException("Enable current Sublime key bindings first");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        app.Startup += async (_, _) =>
        {
            using var owner = new HwndSource(new HwndSourceParameters("SnapBar Action Test") { Width = 1, Height = 1 });
            using var original = new ClipboardBackup();
            bool saved = false;
            void Locked(Action work)
            {
                if (!OpenClipboard(owner.Handle)) throw new InvalidOperationException("Clipboard is busy");
                try { work(); } finally { CloseClipboard(); }
            }
            try
            {
                Locked(() =>
                {
                    var status = original.Capture();
                    if (status != "ok") throw new InvalidOperationException(status);
                    saved = true;
                });
                Clipboard.SetText("replacement", TextDataFormat.UnicodeText);
                using var delayed = args.Contains("--delayed-backup")
                    ? new ClipboardRaceTest.DelayedBackupFixture(RegisterClipboardFormat("SnapBar.Test.Sublime.Rich"),
                        eventuallyReady: true, originalText: "replacement") : null;
                using var controller = new SelectionController(owner,
                    TestPaths.ProbeExecutable);
                controller.SublimeClipboardEnabled = true;
                bool actionReported = false;
                controller.Status += status =>
                {
                    Console.WriteLine("STATUS " + status);
                    if (status.StartsWith(action switch
                        { TextAction.Copy => "已複製", TextAction.Cut => "已送出剪下指令", _ => "已送出貼上指令" }))
                        actionReported = true;
                };
                Native.GetCursorPos(out var cursor);
                await controller.CaptureAsync(false, cursor.X, cursor.Y);
                var snapshot = controller.ActiveSelection;
                if (snapshot is not { Text: "selection", Editable: true, Source: SublimeSelectionAdapter.Source })
                    throw new InvalidOperationException("Expected editable single fixture selection");
                if (Clipboard.GetText() != "replacement") throw new InvalidOperationException("Capture did not restore intended paste data");
                if (delayed != null)
                {
                    if (delayed.Renders != 2) throw new InvalidOperationException("Delayed backup did not recover through the real Sublime path");
                    Console.WriteLine("PASS Sublime: delayed clipboard backup recovers automatically and restores original text");
                }
                var signalIndex = Array.IndexOf(args, "--continue-file");
                if (signalIndex >= 0)
                {
                    var signal = args[signalIndex + 1];
                    if (System.IO.File.Exists(signal)) throw new InvalidOperationException("Continue file must not exist at test start");
                    Console.WriteLine("READY: change selection or foreground, then create continue file");
                    var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (!System.IO.File.Exists(signal))
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("No continue signal within 60 seconds");
                        await Task.Delay(25);
                    }
                }
                if (args.Contains("--expect-rejected"))
                {
                    if (controller.ActiveSelection != null) throw new InvalidOperationException("User input/foreground did not invalidate session");
                    await controller.ExecuteAsync(action);
                    if (Clipboard.GetText() != "replacement") throw new InvalidOperationException("Rejected action changed clipboard");
                    Console.WriteLine($"PASS {action}: changed target/selection rejected; verify fixture buffer separately");
                    result = 0;
                    return;
                }
                await controller.ExecuteAsync(action);
                if (!actionReported) throw new InvalidOperationException("Controller did not report the requested action");
                var afterAction = Array.IndexOf(args, "--after-action-file");
                if (afterAction >= 0)
                {
                    var signal = args[afterAction + 1];
                    if (System.IO.File.Exists(signal)) throw new InvalidOperationException("After-action file must not exist at test start");
                    Console.WriteLine("READY: verify fixture buffer, then create after-action file to allow clipboard cleanup");
                    var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (!System.IO.File.Exists(signal))
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("No after-action verification within 60 seconds");
                        await Task.Delay(25);
                    }
                }
                // SendInput only queues the command. Observe clipboard for Cut; Paste needs
                // a screenshot/buffer check after this driver exits to verify document content.
                if (action == TextAction.Cut)
                    for (int n = 0; n < 40 && Clipboard.GetText() != "selection"; n++) await Task.Delay(25);
                var expected = action == TextAction.Paste ? "replacement" : "selection";
                if (Clipboard.GetText() != expected) throw new InvalidOperationException("Unexpected clipboard after action");
                Console.WriteLine($"PASS {action}: controller path and expected clipboard; verify fixture buffer separately");
                result = 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); }
            finally
            {
                if (saved)
                {
                    try { Locked(() => { if (!original.Restore()) throw new InvalidOperationException("Restore failed"); }); }
                    catch (Exception e) { Console.WriteLine("FAIL clipboard cleanup: " + e.Message); result = 1; }
                }
                app.Shutdown();
            }
        };
        app.Run();
        return result;
    }
}
