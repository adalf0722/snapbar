using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class AutoCopyTest
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseEventInput
    { public uint Type; public MouseInput Mouse; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, MouseEventInput[] input, int size);

    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var result = 1; var passed = 0;
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS " + name); }
        app.Startup += async (_, _) =>
        {
            Process? child = null;
            using var backup = new ClipboardBackup(); var backedUp = false;
            using var owner = new HwndSource(new HwndSourceParameters("Auto-copy test owner")
                { Width = 1, Height = 1, WindowStyle = 0 });
            void Locked(Action action)
            {
                var opened = false;
                for (var n = 0; n < 20 && !opened; n++) { opened = OpenClipboard(owner.Handle); if (!opened) Thread.Sleep(15); }
                if (!opened) throw new InvalidOperationException("Clipboard busy");
                try { action(); } finally { CloseClipboard(); }
            }
            string ReadClipboard()
            {
                var value = "";
                Locked(() =>
                {
                    var memory = GetClipboardData(13);
                    var pointer = memory == 0 ? 0 : GlobalLock(memory);
                    if (pointer == 0) throw new InvalidOperationException("Fixture expected Unicode clipboard data");
                    try { value = Marshal.PtrToStringUni(pointer) ?? ""; }
                    finally { GlobalUnlock(memory); }
                });
                return value;
            }
            try
            {
                var settingsPage = new GeneralSettingsPage(false, () => { },
                    new StartupRegistration(Environment.ProcessPath!, @"Software\SnapBar.Tests\AutoCopy"), false, _ => { });
                settingsPage.ConfigureAutoCopy(new(), _ => false);
                IEnumerable<T> Controls<T>(DependencyObject node) where T : DependencyObject
                {
                    if (node is T item) yield return item;
                    foreach (var childNode in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                        foreach (var match in Controls<T>(childNode)) yield return match;
                }
                var enabled = Controls<CheckBox>(settingsPage).Single(c => AutomationProperties.GetAutomationId(c) == "AutoCopyEnabled");
                enabled.IsChecked = true; enabled.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(enabled.IsChecked == false && Controls<TextBlock>(settingsPage).Any(c =>
                    AutomationProperties.GetAutomationId(c) == "AutoCopySaveError" && c.Visibility == Visibility.Visible),
                    "failed preference save rolls the UI back and explains unchanged behavior");
                Locked(() => { if (backup.Capture() != "ok") throw new InvalidOperationException("Cannot preserve clipboard"); backedUp = true; });
                Clipboard.SetText("original fixture"); var sequence = Native.GetClipboardSequenceNumber();
                Check(!SelectionClipboardWriter.TryWrite(owner.Handle, "stale fixture", sequence, () => false)
                    && ReadClipboard() == "original fixture", "cancelled clipboard write preserves contents");
                Clipboard.SetText("external fixture");
                Check(!SelectionClipboardWriter.TryWrite(owner.Handle, "stale fixture", sequence, () => true)
                    && ReadClipboard() == "external fixture", "clipboard sequence race rejects old automatic copy");
                Check(SelectionClipboardWriter.TryWrite(owner.Handle, "Unicode fixture \u4e2d\u6587 \ud83d\ude00",
                    Native.GetClipboardSequenceNumber(), () => true) && ReadClipboard() == "Unicode fixture \u4e2d\u6587 \ud83d\ude00",
                    "guarded writer transfers Unicode text");

                var start = new ProcessStartInfo(TestPaths.AppExecutable) { UseShellExecute = false, RedirectStandardInput = true };
                start.ArgumentList.Add("--fixture"); start.ArgumentList.Add("--web-search");
                start.Environment["SNAPBAR_FOREGROUND_TEST_RUNNER_PID"] = Environment.ProcessId.ToString();
                start.Environment["SNAPBAR_TEST_EDITABILITY_COMMANDS"] = "1";
                child = Process.Start(start)!;
                Console.WriteLine("WAITING: activate the dedicated auto-copy fixture");
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != child.Id && DateTime.UtcNow < deadline)
                {
                    child.Refresh();
                    if (child.MainWindowHandle != 0) Native.SetForegroundWindow(child.MainWindowHandle);
                    await Task.Delay(25);
                }
                Check(Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == child.Id, "auto-copy fixture owns foreground");
                await Task.Delay(150);
                var target = Native.GetIdentity(Native.GetForegroundWindow())!;
                // Create and use UIA proxies only on MTA workers. A proxy created on this
                // hook-owning STA can marshal calls back here and starve low-level hooks.
                AutomationElement FindControl(string id) => AutomationElement.FromHandle((nint)target.Window)
                    .FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
                TextPattern ReadText() => (TextPattern)FindControl("EditableSample").GetCurrentPattern(TextPattern.Pattern);
                var bounds = await Task.Run(() => FindControl("EditableSample").Current.BoundingRectangle);
                var x = (int)bounds.Left + 35; var y = (int)bounds.Top + 25;
                SetCursorPos(x, y);
                var launched = false;
                using var controller = new SelectionController(owner, TestPaths.ProbeExecutable, _ => launched = true);
                controller.Status += message => Console.WriteLine("STATUS " + message);
                using var observerOwner = new HwndSource(new HwndSourceParameters("Auto-copy gesture observer")
                    { Width = 1, Height = 1, WindowStyle = 0 });
                using var observer = new InputMonitor(observerOwner, new HotkeySettings(3, 0x51));
                var observed = new List<InputEvent>();
                observer.Input += observed.Add;
                controller.ConfigureAutoCopy(new(true, AutoCopyMode.OnSelection));
                bool PopupVisible() => app.Windows.OfType<PopupWindow>().Any(w => w.IsVisible);
                bool FeedbackVisible() => app.Windows.OfType<CopyFeedbackWindow>().Any(w => w.IsVisible);
                Task SelectAll() => Task.Run(() => ReadText().DocumentRange.Select());
                async Task SetReadOnly(bool value)
                {
                    await child.StandardInput.WriteLineAsync(value ? "readonly" : "writable");
                    await child.StandardInput.FlushAsync();
                    var watch = Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < 1250)
                    {
                        var current = await Task.Run(() =>
                            ((ValuePattern)FindControl("EditableSample").GetCurrentPattern(ValuePattern.Pattern)).Current.IsReadOnly);
                        if (current == value) return;
                        await Task.Delay(20);
                    }
                    throw new InvalidOperationException("Fixture did not change editability");
                }
                async Task Capture(bool toolbar)
                {
                    controller.Automatic = toolbar; await SelectAll();
                    Clipboard.SetText("before fixture");
                    await controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                }
                await Capture(false);
                Check(ReadClipboard() == FixtureWindow.SearchFixtureText && !PopupVisible(), "copy-only mode copies readonly selection without toolbar");
                Check(FeedbackVisible() && Native.GetIdentity(Native.GetForegroundWindow()) == target, "copy feedback never takes source focus");
                await Task.Delay(1050);
                Check(!FeedbackVisible(), "copy feedback disappears after its timeout");
                controller.ConfigureAutoCopy(new(true, AutoCopyMode.OnSelection, false));
                await Capture(true);
                Check(ReadClipboard() == FixtureWindow.SearchFixtureText && PopupVisible() && !FeedbackVisible(),
                    "toolbar and copy coexist while feedback is disabled");
                await controller.ExecuteAsync(TextAction.Copy, direct: true);
                Check(ReadClipboard() == FixtureWindow.SearchFixtureText, "manual copy still works through shared writer");
                controller.ConfigureAutoCopy(new()); controller.Automatic = true; await SelectAll();
                Clipboard.SetText("toolbar fixture"); await controller.CaptureAsync(true, x, y);
                Check(ReadClipboard() == "toolbar fixture" && PopupVisible(), "toolbar-only capture never copies");

                controller.ConfigureAutoCopy(new(true, AutoCopyMode.OnSelection));
                controller.ConfigureAppearanceDelay(2000);
                async Task WaitForFeedback(Task capture)
                {
                    var limit = Stopwatch.StartNew();
                    while (!FeedbackVisible() && !capture.IsCompleted && limit.Elapsed < TimeSpan.FromSeconds(3))
                        await Task.Delay(20);
                    Check(FeedbackVisible() && !PopupVisible(), "copy feedback arrives while delayed toolbar is pending");
                }
                await SelectAll(); Clipboard.SetText("appearance timing fixture");
                var appearanceClock = Stopwatch.StartNew();
                var delayedCapture = controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                await WaitForFeedback(delayedCapture);
                Check(ReadClipboard() == FixtureWindow.SearchFixtureText && controller.ActiveSelection == null,
                    "pending toolbar exposes no active selection while clipboard already contains copied text");
                var copyElapsed = appearanceClock.ElapsedMilliseconds;
                SetCursorPos((int)bounds.Left + 8, (int)bounds.Bottom + 16);
                await Task.Delay(1000);
                Check(!FeedbackVisible() && !PopupVisible(), "feedback timeout and pointer movement leave toolbar pending");
                await delayedCapture;
                Check(PopupVisible() && appearanceClock.ElapsedMilliseconds >= 1925
                    && appearanceClock.ElapsedMilliseconds < 2000 + Math.Max(110, copyElapsed * 3 / 4),
                    $"automatic appearance counts capture and copy time ({appearanceClock.ElapsedMilliseconds} ms total, {copyElapsed} ms copying)");
                SetCursorPos(x, y);

                await SetReadOnly(false);
                await SelectAll();
                await controller.CaptureAsync(false, x, y);
                Check(controller.ActiveSelection?.Editable == true, "editability fixture initially permits writes");
                Clipboard.SetText("read-only transition fixture");
                delayedCapture = controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                await WaitForFeedback(delayedCapture);
                await SetReadOnly(true);
                await delayedCapture;
                Check(PopupVisible() && controller.ActiveSelection is { Editable: false },
                    "delayed toolbar presents refreshed read-only permission");

                await SelectAll(); Clipboard.SetText("focus delay fixture");
                delayedCapture = controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                await WaitForFeedback(delayedCapture);
                await Task.Run(() => FindControl("PasswordSample").SetFocus());
                await delayedCapture;
                Check(!PopupVisible(), "same-window focus change rejects delayed selection");
                await Task.Run(() => FindControl("EditableSample").SetFocus());

                await SelectAll(); Clipboard.SetText("foreground delay fixture");
                delayedCapture = controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                await WaitForFeedback(delayedCapture);
                var foreground = new Window { Title = "SnapBar foreground cancellation fixture", Width = 240, Height = 100 };
                foreground.Show(); foreground.Activate();
                await delayedCapture;
                Check(!PopupVisible() && !FeedbackVisible(), "foreground change cancels delayed selection and feedback");
                foreground.Close();
                Native.SetForegroundWindow((nint)target.Window);
                await Task.Run(() => FindControl("EditableSample").SetFocus());

                await SelectAll(); Clipboard.SetText("keyboard delay fixture");
                delayedCapture = controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                await WaitForFeedback(delayedCapture);
                keybd_event(0x27, 0, 0, 0); keybd_event(0x27, 0, 2, 0);
                await delayedCapture;
                Check(!PopupVisible() && controller.ActiveSelection == null, "arrow key cancels rather than navigating the pending toolbar");

                await SelectAll(); Clipboard.SetText("reselection delay fixture");
                delayedCapture = controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                await WaitForFeedback(delayedCapture);
                controller.ConfigureAppearanceDelay(0);
                Check(!delayedCapture.IsCompleted, "a preference update keeps the current appearance deadline");
                await controller.CaptureAsync(true, x, y);
                var replacement = controller.ActiveSelection;
                await delayedCapture;
                Check(PopupVisible() && replacement != null && controller.ActiveSelection == replacement,
                    "a new selection supersedes the pending generation");

                controller.ConfigureAppearanceDelay(2000);
                await SelectAll();
                appearanceClock.Restart();
                await controller.CaptureAsync(false, x, y);
                Check(PopupVisible() && appearanceClock.ElapsedMilliseconds < 1800, "toolbar shortcut bypasses automatic appearance delay");
                await controller.CaptureAsync(false, x, y, TextAction.Search);
                Check(launched && !PopupVisible() && appearanceClock.ElapsedMilliseconds < 1900, "direct shortcut bypasses automatic appearance delay");

                controller.ConfigureAppearanceDelay(900);
                await SelectAll(); Clipboard.SetText("slow appearance fixture");
                keybd_event(0x11, 0, 0, 0);
                await Task.Delay(40);
                delayedCapture = controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                await Task.Delay(1100);
                Check(!PopupVisible() && !delayedCapture.IsCompleted, "held Ctrl delays capture beyond the appearance deadline");
                appearanceClock.Restart();
                keybd_event(0x11, 0, 2, 0);
                await delayedCapture;
                Check(PopupVisible() && ReadClipboard() == FixtureWindow.SearchFixtureText && appearanceClock.ElapsedMilliseconds < 750,
                    $"slow capture presents without another full delay ({appearanceClock.ElapsedMilliseconds} ms after Ctrl release)");
                controller.ConfigureAppearanceDelay(ToolbarAppearanceDelay.DefaultMilliseconds);

                controller.ConfigureAutoCopy(new(true, AutoCopyMode.OnSelection)); controller.Automatic = false;
                await SelectAll(); Clipboard.SetText("race fixture");
                var pending = controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                Clipboard.SetText("new clipboard fixture"); await pending;
                Check(ReadClipboard() == "new clipboard fixture" && !FeedbackVisible(), "external copy during capture wins without success feedback");
                await SelectAll(); Clipboard.SetText("cancel fixture");
                pending = controller.CaptureAsync(true, x, y, autoCopyRequest: true); controller.CancelSelection(); await pending;
                Check(ReadClipboard() == "cancel fixture", "new interaction cancels pending auto-copy");
                await SelectAll(); Clipboard.SetText("disable fixture");
                pending = controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                controller.ConfigureAutoCopy(new()); await pending;
                Check(ReadClipboard() == "disable fixture", "disabling preference cancels pending copy");
                controller.ConfigureAutoCopy(new(true));
                keybd_event(0x11, 0, 0, 0); await Task.Delay(40);
                Clipboard.SetText("held key fixture"); await SelectAll();
                await controller.CaptureAsync(true, x, y, autoCopyRequest: true);
                Check(ReadClipboard() == "held key fixture" && !FeedbackVisible(), "holding Ctrl past deadline skips copy without success feedback");
                keybd_event(0x11, 0, 2, 0); await Task.Delay(40);
                var protectedBounds = await Task.Run(() =>
                {
                    var password = FindControl("PasswordSample");
                    password.SetFocus(); return password.Current.BoundingRectangle;
                });
                Clipboard.SetText("password fixture");
                await controller.CaptureAsync(true, (int)protectedBounds.Left + 10, (int)protectedBounds.Top + 10, autoCopyRequest: true);
                Check(ReadClipboard() == "password fixture" && !FeedbackVisible(), "password field never auto-copies");
                await Task.Run(() => FindControl("EditableSample").SetFocus());
                controller.SetExcludedApplications([new(Native.GetExecutablePath(target)!, "Fixture")]);
                await Capture(false);
                Check(ReadClipboard() == "before fixture", "excluded application cannot auto-copy");
                controller.SetExcludedApplications([]);

                // Exercise actual low-level mouse hooks and modifier snapshots, not just CaptureAsync.
                async Task SelectWord(bool control, bool repeatControl = false, int releaseDelay = 50)
                {
                    observed.Clear();
                    Check(Native.GetIdentity(Native.GetForegroundWindow()) == target, "gesture remains in disposable source");
                    await Task.Run(() =>
                    {
                        var caret = ReadText().DocumentRange.Clone();
                        caret.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.End,
                            caret, System.Windows.Automation.Text.TextPatternRangeEndpoint.Start);
                        caret.Select();
                    });
                    // Do not block the installing dispatcher while SendInput dispatches hooks.
                    var inserted = await Task.Run(() =>
                    {
                        SetCursorPos(x, y);
                        if (control) keybd_event(0x11, 0, 0, 0);
                        Thread.Sleep(40);
                        var clicks = new uint[] { 2, 4, 2, 4 }.Select(flags => new MouseEventInput
                            { Mouse = new() { Flags = flags } }).ToArray();
                        var sent = SendInput(4, clicks, Marshal.SizeOf<MouseEventInput>());
                        Thread.Sleep(releaseDelay);
                        if (repeatControl)
                            for (var n = 0; n < 3; n++)
                            {
                                keybd_event(0x11, 0, 0, 0);
                                Thread.Sleep(30);
                            }
                        if (control) keybd_event(0x11, 0, 2, 0);
                        return sent;
                    });
                    Check(inserted == 4, "complete mouse gesture is inserted");
                    await Task.Delay(1200);
                    foreach (var input in observed.ToArray()) Console.WriteLine($"INPUT {input.Kind} modifiers={input.Modifiers}");
                    var selectedLength = await Task.Run(() => ReadText().GetSelection().Sum(r => r.GetText(32768).Length));
                    Console.WriteLine($"GESTURE control={control}; modifiers={InputMonitor.ReadModifiers()}; selectedLength={selectedLength}; capturedLength={controller.ActiveSelection?.CharacterCount}");
                }
                controller.ConfigureAutoCopy(new(true)); Clipboard.SetText("gesture fixture");
                await SelectWord(false);
                Check(ReadClipboard() == "gesture fixture", "Ctrl mode skips ordinary mouse selection");
                await SelectWord(true);
                Check(ReadClipboard() != "gesture fixture" && !PopupVisible(), "Ctrl mouse selection triggers copy after key release");
                controller.Automatic = true; Clipboard.SetText("repeat fixture");
                controller.ConfigureAppearanceDelay(900);
                await SelectWord(true, repeatControl: true, releaseDelay: 1100);
                Check(ReadClipboard() != "repeat fixture" && PopupVisible(),
                    "slow capture consumes the appearance delay while held Ctrl repeats preserve auto-copy");
                controller.ConfigureAppearanceDelay(ToolbarAppearanceDelay.DefaultMilliseconds);
                controller.Automatic = false;
                controller.ConfigureAutoCopy(new(true, AutoCopyMode.OnSelection)); Clipboard.SetText("skip fixture");
                await SelectWord(true);
                Check(ReadClipboard() == "skip fixture", "selection mode uses Ctrl to skip actual mouse gesture");
                await SelectWord(false);
                Check(ReadClipboard() != "skip fixture", "selection mode copies ordinary mouse gesture");
                controller.ConfigureAutoCopy(new());
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error); }
            finally
            {
                keybd_event(0x11, 0, 2, 0);
                if (child != null) { if (!child.HasExited) { child.CloseMainWindow(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } child.Dispose(); }
                if (backedUp) Locked(() => backup.Restore());
                Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed"); app.Shutdown();
            }
        };
        app.Run(); return result;
    }
}
