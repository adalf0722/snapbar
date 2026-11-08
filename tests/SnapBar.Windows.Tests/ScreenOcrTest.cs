using System.IO;
using System.Diagnostics;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;
using SnapBar.Windows.Ocr;

internal static class ScreenOcrTest
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        internal uint Size, Usage, ProcessId;
        internal nuint Heap;
        internal uint Module, Threads, ParentId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Exe;
    }
    [DllImport("kernel32.dll")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    private static long OwnedWorkerPeak()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == -1) return 0;
        long peak = 0;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Exe = "" };
            if (!Process32First(snapshot, ref entry)) return 0;
            do
            {
                if (entry.ParentId != Environment.ProcessId || !entry.Exe.Equals("SnapBar.App.exe", StringComparison.OrdinalIgnoreCase)) continue;
                try { using var process = Process.GetProcessById((int)entry.ProcessId); peak = Math.Max(peak, process.PeakWorkingSet64); }
                catch (ArgumentException) { } catch (InvalidOperationException) { }
            } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return peak;
    }

    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int passed = 0, exit = 1;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
            passed++;
        }

        app.Startup += async (_, _) =>
        {
            var path = Path.Combine(Path.GetTempPath(), "SnapBar.Ocr.Tests." + Guid.NewGuid().ToString("N") + ".json");
            var store = new SettingsStore(path);
            var hotkey = new HotkeySettings(3, 0x55);
            store.Load();
            store.Save(new() { OcrHotkey = hotkey });
            var content = new StackPanel { Background = Brushes.White };
            content.Children.Add(new TextBlock { Text = "SnapBar OCR test 123", FontFamily = new("Segoe UI"),
                FontSize = 45, Margin = new(35, 25, 0, 8), Foreground = Brushes.Black });
            content.Children.Add(new TextBlock { Text = "繁體中文辨識測試", FontFamily = new("Microsoft JhengHei"),
                FontSize = 45, Margin = new(35, 4, 0, 0), Foreground = Brushes.Black });
            var fixture = new Window { Title = "SnapBar OCR desktop fixture", Content = content, Width = 700, Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
            var backdrops = new List<Window>();
            MainWindow? snapBar = null;
            try
            {
                snapBar = new MainWindow(store, () => { }, () => false);
                snapBar.InitializeBackground();
                var hwnd = new WindowInteropHelper(snapBar).Handle;
                foreach (var monitor in ScreenOcr.Monitors())
                {
                    var backdrop = new Window { Background = Brushes.White, WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true };
                    backdrop.Show();
                    Native.SetWindowPos(new WindowInteropHelper(backdrop).Handle, -1, (int)monitor.X, (int)monitor.Y,
                        (int)monitor.Width, (int)monitor.Height, 0x40);
                    backdrops.Add(backdrop);
                    Console.WriteLine($"ENV monitorWidthPx={monitor.Width:F0} monitorHeightPx={monitor.Height:F0}");
                }
                fixture.Show();
                fixture.Activate();
                var fixtureHandle = new WindowInteropHelper(fixture).Handle;
                Console.WriteLine("WAITING: activate the dedicated SnapBar OCR desktop fixture (up to 60 seconds)");
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (Native.GetForegroundWindow() != fixtureHandle && DateTime.UtcNow < deadline) await Task.Delay(25);
                Check(Native.GetForegroundWindow() == fixtureHandle, "desktop OCR fixture owns foreground");

                void Trigger() => SendMessage(hwnd, Native.WM_HOTKEY, 9, (hotkey.Key << 16) | hotkey.Modifiers);
                async Task Ready()
                {
                    var until = DateTime.UtcNow.AddSeconds(5);
                    while (!snapBar.OcrCaptureReady && DateTime.UtcNow < until) await Task.Delay(15);
                }
                var coldClock = Stopwatch.StartNew();
                Trigger();
                await Ready();
                var coldReadyMs = coldClock.Elapsed.TotalMilliseconds;
                UiAppearanceTest.Snapshot(app.Windows.OfType<OcrOverlay>().First(), "ocr-frozen-overlay.png");
                Check(app.Windows.OfType<OcrOverlay>().Count(window => window.IsVisible) == ScreenOcr.Monitors().Count,
                    "OCR hotkey opens a capture overlay on each monitor");
                var gesturePoint = content.PointToScreen(new(100, 80));
                SetCursorPos((int)gesturePoint.X, (int)gesturePoint.Y);
                mouse_event(2, 0, 0, 0, 0);
                await Task.Delay(20);
                mouse_event(4, 0, 0, 0, 0);
                await Task.Delay(20);
                Check(snapBar.OcrCaptureReady && !app.Windows.OfType<OcrResultWindow>().Any(),
                    "undersized selection keeps capture ready without recognition");
                mouse_event(2, 0, 0, 0, 0);
                await Task.Delay(20);
                try { System.Windows.Input.Mouse.Capture(null); }
                finally { mouse_event(4, 0, 0, 0, 0); }
                await Task.Delay(20);
                Check(snapBar.OcrCaptureReady && app.Windows.OfType<OcrOverlay>().All(window =>
                    ((Canvas)window.Content).Children.OfType<System.Windows.Shapes.Rectangle>().All(edge => !edge.IsVisible)),
                    "lost mouse capture clears selection and permits another drag");
                await Task.Delay(100);
                keybd_event(0x1B, 0, 0, 0);
                keybd_event(0x1B, 0, 2, 0);
                await Task.Delay(100);
                Check(!app.Windows.OfType<OcrOverlay>().Any(window => window.IsVisible)
                    && !app.Windows.OfType<Window>().Any(window => window.Title == "框選 OCR · SnapBar"),
                    "Escape cancels capture without opening results");

                Trigger(); await Ready();
                Check(snapBar.OcrCaptureReady, "capture is ready before right-click cancellation");
                mouse_event(8, 0, 0, 0, 0);
                mouse_event(16, 0, 0, 0, 0);
                await Task.Delay(30);
                Check(!snapBar.OcrCaptureReady && !app.Windows.OfType<OcrResultWindow>().Any(),
                    "right click cancels every overlay without recognition");

                var recognize = snapBar.OcrRecognize;
                Trigger();
                await Ready();
                ((TextBlock)content.Children[0]).Text = "Changed source 987";
                await fixture.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                var from = content.PointToScreen(new(12, 12));
                var to = content.PointToScreen(new(content.ActualWidth - 12, 190));
                Check(SetCursorPos((int)to.X, (int)to.Y), "reverse drag starts over the disposable image");
                await Task.Delay(50);
                mouse_event(2, 0, 0, 0, 0);
                try
                {
                    await Task.Delay(50);
                    Check(SetCursorPos((int)from.X, (int)from.Y), "reverse drag ends over the disposable image");
                    await Task.Delay(80);
                    var selectedOverlay = app.Windows.OfType<OcrOverlay>().Single(window =>
                    {
                        Native.GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds);
                        return from.X >= bounds.Left && from.X < bounds.Right && from.Y >= bounds.Top && from.Y < bounds.Bottom;
                    });
                    UiAppearanceTest.Snapshot(selectedOverlay, "ocr-selection.png");
                }
                finally { mouse_event(4, 0, 0, 0, 0); }
                var coldRelease = Stopwatch.StartNew();

                Window? result = null;
                var sawProcessing = false;
                OcrResultWindow? processingCard = null;
                long coldWorkerPeak = 0;
                double coldFeedbackMs = 0;
                deadline = DateTime.UtcNow.AddSeconds(8);
                while (result == null && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(5);
                    coldWorkerPeak = Math.Max(coldWorkerPeak, OwnedWorkerPeak());
                    var processing = app.Windows.OfType<OcrResultWindow>().SingleOrDefault(window => window.IsProcessing);
                    if (!sawProcessing && processing != null)
                    {
                        coldFeedbackMs = coldRelease.Elapsed.TotalMilliseconds;
                        UiAppearanceTest.Snapshot(processing, "ocr-processing-card.png");
                        processingCard = processing;
                        sawProcessing = true;
                    }
                    result = app.Windows.OfType<OcrResultWindow>().SingleOrDefault(window => !window.IsProcessing);
                }
                Check(result != null && !app.Windows.OfType<OcrOverlay>().Any(window => window.IsVisible),
                    "drag closes capture overlays and displays OCR results");
                Check(sawProcessing && ReferenceEquals(processingCard, result),
                    "recognition exposes processing feedback before transitioning in place");
                Console.WriteLine($"METRIC fixture=ocr-disposable run=cold triggerReadyMs={coldReadyMs:F1} releaseFeedbackMs={coldFeedbackMs:F1} releaseResultMs={coldRelease.Elapsed.TotalMilliseconds:F1} workerPeakBytes={coldWorkerPeak} parentPeakBytes={Process.GetCurrentProcess().PeakWorkingSet64} selectionWidthPx={to.X-from.X:F0} selectionHeightPx={to.Y-from.Y:F0} dpi={Native.GetDpiForWindow(fixtureHandle)} monitors={ScreenOcr.Monitors().Count}");
                var body = (DockPanel)result!.Content;
                var editor = body.Children.OfType<TextBox>().SingleOrDefault();
                if (editor == null) throw new InvalidOperationException(body.Children.OfType<TextBlock>().Single().Text);
                var normalized = string.Concat(editor.Text.Where(c => !char.IsWhiteSpace(c)));
                Check(normalized == "SnapBarOCRtest123繁體中文辨識測試", "actual desktop crop recognizes English and Traditional Chinese");
                Check(!editor.IsReadOnly, "recognized text can be corrected without modifying the source");
                var buttons = body.Children.OfType<WrapPanel>().Single().Children.OfType<Button>()
                    .Where(button => button.IsVisible)
                    .Select(System.Windows.Automation.AutomationProperties.GetName).ToArray();
                Check(buttons.SequenceEqual(new[] { "複製文字", "重新框選", "更多" }),
                    "OCR result offers text actions without a write-back action");
                UiAppearanceTest.Snapshot(result, "ocr-result-card.png");
                WindowWorkArea.SetWorkAreaOverride(result, new Rect(0, 0, 360, 240));
                result.UpdateLayout();
                Check(result.ActualWidth <= 360 && result.ActualHeight <= 240, "result remains usable inside a restricted work area");
                UiAppearanceTest.Snapshot(result, "ocr-result-restricted.png");
                WindowWorkArea.SetWorkAreaOverride(result, null);
                Check(normalized.Contains("123") && !normalized.Contains("987"), "source mutation after capture cannot change frozen recognition");
                async Task WithClipboardLock(Action operation)
                {
                    var opened = false;
                    for (var attempt = 0; attempt < 20 && !opened; attempt++)
                    {
                        opened = OpenClipboard(fixtureHandle);
                        if (!opened) await Task.Delay(15);
                    }
                    if (!opened) throw new InvalidOperationException("Fixture could not lock clipboard for backup or restore.");
                    try { operation(); }
                    finally { CloseClipboard(); }
                }
                using var savedClipboard = new ClipboardBackup();
                await WithClipboardLock(() =>
                {
                    if (savedClipboard.Capture() != "ok") throw new InvalidOperationException("Original clipboard could not be preserved; copy checks were not started.");
                });
                try
                {
                    editor.Select(0, 7);
                    Button Action(Window window, string id) => ((DockPanel)window.Content).Children.OfType<WrapPanel>().Single()
                        .Children.OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == id);
                    Action(result, "OcrCopy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(Clipboard.GetText() == editor.SelectedText, "copy uses selected recognized text");
                    Check(OpenClipboard(fixtureHandle), "fixture acquires clipboard for copy failure check");
                    try
                    {
                        Action(result, "OcrCopy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(((DockPanel)result.Content).Children.OfType<TextBlock>().Any(text => text.Text == UiText.Capture().Get("MainWindow_Ocr_009"))
                            && editor.Text.Length > 0, "clipboard failure preserves recognized text and offers retry guidance");
                    }
                    finally { CloseClipboard(); }
                    editor.Text = " ";
                    Action(result, "OcrMore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var blankMenu = Action(result, "OcrMore").ContextMenu;
                    Check(!Action(result, "OcrCopy").IsEnabled && !blankMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_search").IsEnabled,
                        "blank edited text disables copy and search");
                    blankMenu.IsOpen = false;
                    editor.Text = "Disposable replacement";
                    Action(result, "OcrReselect").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Ready();
                    Check(snapBar.OcrCaptureReady, "reselect starts a new capture");
                }
                finally
                {
                    await WithClipboardLock(() =>
                    {
                        if (!savedClipboard.Restore()) throw new InvalidOperationException("Original clipboard could not be restored.");
                    });
                }
                async Task Drag()
                {
                    SetCursorPos((int)from.X, (int)from.Y); await Task.Delay(25); mouse_event(2, 0, 0, 0, 0);
                    try { await Task.Delay(25); SetCursorPos((int)to.X, (int)to.Y); await Task.Delay(25); }
                    finally { mouse_event(4, 0, 0, 0, 0); }
                }
                async Task<OcrResultWindow> Completed()
                {
                    var until = Stopwatch.StartNew();
                    while (until.Elapsed < TimeSpan.FromSeconds(10))
                    {
                        var card = app.Windows.OfType<OcrResultWindow>().SingleOrDefault(window => !window.IsProcessing);
                        if (card != null) return card;
                        await Task.Delay(5);
                    }
                    throw new TimeoutException("OCR result readiness");
                }
                snapBar.OcrRecognize = recognize;
                await Drag();
                var refreshed = await Completed();
                Check(((DockPanel)refreshed.Content).Children.OfType<TextBox>().Single().Text.Contains("987"), "reselect recognizes changed source from a new snapshot");
                refreshed.Close();
                var stale = new TaskCompletionSource<OcrResult>();
                CancellationToken observedToken = default;
                snapBar.OcrRecognize = (_, _, token) => { observedToken = token; return stale.Task; };
                Trigger(); await Ready(); await Drag();
                var pendingDeadline = Stopwatch.StartNew();
                while (!app.Windows.OfType<OcrResultWindow>().Any() && pendingDeadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(5);
                var pending = app.Windows.OfType<OcrResultWindow>().Single();
                await Task.Delay(350);
                UiAppearanceTest.Snapshot(pending, "ocr-processing-card.png");
                Trigger();
                Check(!snapBar.OcrCaptureReady && app.Windows.OfType<OcrResultWindow>().Count() == 1, "repeated trigger cannot queue recognition");
                pending.Close();
                Check(observedToken.IsCancellationRequested, "closing processing card cancels its owned operation");
                stale.SetResult(new("ok", "Disposable late result")); await Task.Delay(30);
                Check(!app.Windows.OfType<OcrResultWindow>().Any(), "late completion cannot reopen a cancelled card");
                foreach (var state in new[] { "timeout", "runtime", "empty" })
                {
                    snapBar.OcrRecognize = (_, _, _) => Task.FromResult(new OcrResult(state));
                    Trigger(); await Ready(); await Drag(); var recovery = await Completed();
                    var recoveryBody = (DockPanel)recovery.Content;
                    var statusKey = state switch { "timeout" => "Ocr_Timeout", "runtime" => "MainWindow_Ocr_003", _ => "Ocr_Empty" };
                    var retry = recoveryBody.Children.OfType<WrapPanel>().Single().Children.OfType<Button>()
                        .Single(button => AutomationProperties.GetAutomationId(button) == "OcrReselect");
                    Check(!recovery.IsProcessing && retry.IsVisible && retry.IsEnabled
                        && recoveryBody.Children.OfType<TextBlock>().Any(text => text.Text == UiText.Capture().Get(statusKey)),
                        state + " returns a localized actionable recovery card");
                    UiAppearanceTest.Snapshot(recovery, "ocr-" + state + "-card.png");
                    retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Ready();
                    Check(snapBar.OcrCaptureReady && !app.Windows.OfType<OcrResultWindow>().Any(), state + " reselect starts a fresh capture");
                    keybd_event(0x1B, 0, 0, 0); keybd_event(0x1B, 0, 2, 0);
                    var cancelDeadline = Stopwatch.StartNew();
                    while (snapBar.OcrCaptureReady && cancelDeadline.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(5);
                }
                snapBar.OcrRecognize = recognize;
                var process = Process.GetCurrentProcess();
                for (var run = 0; run < 5; run++)
                {
                    fixture.Activate();
                    var clock = Stopwatch.StartNew(); Trigger(); await Ready(); var readyMs = clock.Elapsed.TotalMilliseconds;
                    Check(snapBar.OcrCaptureReady, "measured capture readiness " + run);
                    var releaseClock = Stopwatch.StartNew(); await Drag(); releaseClock.Restart();
                    OcrResultWindow? first = null;
                    while (first == null && releaseClock.Elapsed < TimeSpan.FromSeconds(5))
                    { first = app.Windows.OfType<OcrResultWindow>().SingleOrDefault(); if (first == null) await Task.Delay(1); }
                    var feedbackMs = releaseClock.Elapsed.TotalMilliseconds;
                    long workerPeak = 0;
                    var completion = Completed();
                    while (!completion.IsCompleted) { workerPeak = Math.Max(workerPeak, OwnedWorkerPeak()); await Task.Delay(5); }
                    var measured = await completion; var resultMs = releaseClock.Elapsed.TotalMilliseconds;
                    process.Refresh();
                    Console.WriteLine($"METRIC fixture=ocr-disposable run=warm{run+1} triggerReadyMs={readyMs:F1} releaseFeedbackMs={feedbackMs:F1} releaseResultMs={resultMs:F1} parentPeakBytes={process.PeakWorkingSet64} workerPeakBytes={workerPeak} selectionWidthPx={to.X-from.X:F0} selectionHeightPx={to.Y-from.Y:F0} dpi={Native.GetDpiForWindow(fixtureHandle)} monitors={ScreenOcr.Monitors().Count}");
                    measured.Close();
                }
                result.Close();
                exit = 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); }
            finally
            {
                if (snapBar != null) { snapBar.PrepareSessionEnd(); snapBar.Close(); }
                fixture.Close();
                foreach (var backdrop in backdrops) backdrop.Close();
                File.Delete(path);
                Console.WriteLine($"{passed} passed, {(exit == 0 ? 0 : 1)} failed");
                app.Shutdown();
            }
        };
        app.Run();
        return exit;
    }
}
