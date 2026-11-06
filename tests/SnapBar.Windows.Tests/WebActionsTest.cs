using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Controls;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class WebActionsTest
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int passed = 0, result = 1;
        void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name); passed++;
        }
        app.Startup += async (_, _) =>
        {
            Process? child = null;
            try
            {
                using var owner = new HwndSource(new HwndSourceParameters("SnapBar web action test")
                    { Width = 1, Height = 1, WindowStyle = 0 });
                async Task StartFixture(string mode)
                {
                    if (child != null) { if (!child.HasExited) child.CloseMainWindow(); child.Dispose(); }
                    var start = new ProcessStartInfo(TestPaths.AppExecutable) { UseShellExecute = false };
                    start.ArgumentList.Add("--fixture"); start.ArgumentList.Add("--web-" + mode);
                    child = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not start");
                    Console.WriteLine("WAITING: activate the dedicated " + mode + " fixture (up to 60 seconds)");
                    var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != child.Id)
                    {
                        if (child.HasExited || DateTime.UtcNow > deadline) throw new InvalidOperationException("Fixture must be foreground");
                        await Task.Delay(25);
                    }
                    await Task.Delay(100);
                }
                var launched = new List<Uri>();
                var statuses = new List<string>();
                var probe = TestPaths.ProbeExecutable;
                var searchHotkey = new HotkeySettings(3, 0x4A);
                var copyHotkey = new HotkeySettings(3, 0x42);
                using var controller = new SelectionController(owner, probe, launched.Add,
                    initialSearchHotkey: searchHotkey, initialCopyHotkey: copyHotkey);
                controller.ConfigureActions(ActionCatalog.SetPlacement(new(), "link", ActionPlacement.Bar));
                controller.Status += statuses.Add;
                Button FindButton(TextAction action)
                {
                    var popup = app.Windows.OfType<PopupWindow>().Single(window => window.IsVisible);
                    return popup.FindActionButton(action);
                }
                async Task Click(TextAction action)
                {
                    var button = FindButton(action);
                    if (!button.IsEnabled || !button.IsVisible) throw new InvalidOperationException("Action button is not available");
                    var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Completed(string status) => completion.TrySetResult(status);
                    controller.Status += Completed;
                    try
                    {
                        // Exercise the actual WPF Click -> ActionRequested -> controller path.
                        // This is a routed event test, not a physical pointer click.
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await completion.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    }
                    finally { controller.Status -= Completed; }
                }
                async Task Capture(string expected)
                {
                    await controller.CaptureAsync(false, 0, 0);
                    if (controller.ActiveSelection is not { Source: "uia", Editable: false } snapshot || snapshot.Text != expected)
                        throw new InvalidOperationException("Expected readonly native fixture selection");
                }

                await StartFixture("search");
                var sequence = Native.GetClipboardSequenceNumber();
                Require(controller.IsActionHotkeyRegistered(TextAction.Search), "native Search shortcut registers");
                SendMessage(owner.Handle, Native.WM_HOTKEY, 3, (searchHotkey.Key << 16) | searchHotkey.Modifiers);
                var searchDeadline = DateTime.UtcNow.AddSeconds(4);
                while (launched.Count == 0 && DateTime.UtcNow < searchDeadline) await Task.Delay(25);
                Require(launched.Count == 1 && Uri.UnescapeDataString(launched[0].Query[3..]) == FixtureWindow.SearchFixtureText
                    && !app.Windows.OfType<PopupWindow>().Any(window => window.IsVisible),
                    "Search shortcut acquires a fresh readonly selection and launches without showing the toolbar");
                launched.Clear();
                await Capture(FixtureWindow.SearchFixtureText);
                Require(FindButton(TextAction.Search).IsEnabled && !FindButton(TextAction.Cut).IsEnabled
                    && !FindButton(TextAction.Paste).IsEnabled && FindButton(TextAction.OpenLink).Visibility == Visibility.Collapsed
                    && FindButton(TextAction.RevealPath).Visibility == Visibility.Collapsed,
                    "readonly text offers Search while writes are disabled and conditional actions are hidden");
                Require(Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == child!.Id,
                    "web action bar preserves source foreground");
                await Click(TextAction.Search);
                Require(launched.Count == 1 && launched[0].Host == "www.google.com"
                    && Uri.UnescapeDataString(launched[0].Query[3..]) == FixtureWindow.SearchFixtureText,
                    "actual Search click event revalidates and launches encoded Unicode query");
                controller.SearchUrlTemplate = "https://example.com/search?term={query}";
                await Capture(FixtureWindow.SearchFixtureText);
                await Click(TextAction.Search);
                Require(launched.Count == 2 && launched[^1].Host == "example.com"
                    && Uri.UnescapeDataString(launched[^1].Query[6..]) == FixtureWindow.SearchFixtureText,
                    "configured search template reaches the browser launcher");
                await Capture(FixtureWindow.SearchFixtureText);
                await controller.ExecuteAsync(TextAction.OpenLink);
                Require(launched.Count == 2 && statuses.Last().Contains("不是完整"),
                    "non-URL cannot be opened even through direct action invocation");

                await StartFixture("link");
                await Capture(FixtureWindow.LinkFixtureText);
                Require(FindButton(TextAction.OpenLink).IsVisible && FindButton(TextAction.OpenLink).IsEnabled
                    && FindButton(TextAction.RevealPath).Visibility == Visibility.Collapsed,
                    "complete URL makes Open Link visible for readonly selection");
                await Click(TextAction.OpenLink);
                Require(launched.Count == 3 && launched[^1].AbsoluteUri == FixtureWindow.LinkFixtureText,
                    "actual Open Link click event retains URL query and fragment");
                await Capture(FixtureWindow.LinkFixtureText);
                controller.Automatic = false; // Invalidates the snapshot before an action can run.
                await controller.ExecuteAsync(TextAction.OpenLink);
                Require(launched.Count == 3 && controller.ActiveSelection == null,
                    "invalidated selection cannot launch a browser");
                await Capture(FixtureWindow.LinkFixtureText);
                await controller.ExecuteAsync((TextAction)999);
                Require(launched.Count == 3 && statuses.Last().Contains("不支援此動作"),
                    "unknown action is rejected rather than treated as Paste");
                Require(Native.GetClipboardSequenceNumber() == sequence,
                    "native web actions and rejected actions leave clipboard untouched");

                void WithClipboard(Action operation)
                {
                    for (var attempt = 0; attempt < 20; attempt++)
                    {
                        if (!OpenClipboard(owner.Handle)) { Thread.Sleep(15); continue; }
                        try { operation(); } finally { CloseClipboard(); }
                        return;
                    }
                    throw new InvalidOperationException("Clipboard busy");
                }

                using (var backup = new ClipboardBackup())
                {
                    Require(controller.IsActionHotkeyRegistered(TextAction.Copy), "native Copy shortcut registers");
                    WithClipboard(() =>
                    {
                        if (backup.Capture() != "ok") throw new InvalidOperationException("Clipboard backup failed");
                    });
                    try
                    {
                        Clipboard.SetText("SnapBar disposable shortcut fixture");
                        try
                        {
                            keybd_event(0x11, 0, 0, 0);
                            keybd_event(0x12, 0, 0, 0);
                            keybd_event((byte)copyHotkey.Key, 0, 0, 0);
                            await Task.Delay(50);
                        }
                        finally
                        {
                            keybd_event((byte)copyHotkey.Key, 0, 2, 0);
                            keybd_event(0x12, 0, 2, 0);
                            keybd_event(0x11, 0, 2, 0);
                        }
                        var copyDeadline = DateTime.UtcNow.AddSeconds(4);
                        while (statuses.LastOrDefault()?.StartsWith("已複製", StringComparison.Ordinal) != true && DateTime.UtcNow < copyDeadline)
                            await Task.Delay(25);
                        Require(Clipboard.GetText() == FixtureWindow.LinkFixtureText
                            && !app.Windows.OfType<PopupWindow>().Any(window => window.IsVisible),
                            "native Ctrl Alt B copies fresh readonly selection without showing the toolbar");
                    }
                    finally
                    {
                        WithClipboard(() =>
                        {
                            if (!backup.Restore()) throw new InvalidOperationException("Clipboard restore failed");
                        });
                    }
                }

                var focusTarget = new Window { Title = "SnapBar shortcut focus switch fixture", Width = 260, Height = 120,
                    ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    focusTarget.Show();
                    Require(Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == child!.Id,
                        "source remains foreground before dispatching the shortcut");
                    var beforeSwitch = launched.Count;
                    SendMessage(owner.Handle, Native.WM_HOTKEY, 3, (searchHotkey.Key << 16) | searchHotkey.Modifiers);
                    focusTarget.Activate();
                    Require(Native.GetForegroundWindow() == new WindowInteropHelper(focusTarget).Handle,
                        "focus moves to the other window before shortcut capture resumes");
                    await Task.Delay(250);
                    Require(launched.Count == beforeSwitch && controller.ActiveSelection == null
                        && !app.Windows.OfType<PopupWindow>().Any(window => window.IsVisible),
                        "focus switch cancels direct search without executing against either window");
                }
                finally { focusTarget.Close(); }
                Require(Native.SetForegroundWindow(child!.MainWindowHandle), "restore source for remaining fixture checks");
                controller.Dispose();
                using var failing = new SelectionController(owner, probe, _ => throw new Win32Exception("private URL detail"));
                var failureStatuses = new List<string>();
                failing.Status += failureStatuses.Add;
                await failing.CaptureAsync(false, 0, 0);
                await failing.ExecuteAsync(TextAction.OpenLink);
                Require(failureStatuses.Last().Contains("無法開啟預設瀏覽器")
                    && !failureStatuses.Any(status => status.Contains("private URL detail") || status.Contains("https://")),
                    "browser failure is reported without selected URL or OS exception text");
                failing.Dispose();
                using var emptySelection = new SelectionController(owner, probe, launched.Add, initialSearchHotkey: searchHotkey);
                var emptyResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                emptySelection.Status += status => emptyResult.TrySetResult(status);
                var source = AutomationElement.FromHandle(child.MainWindowHandle);
                var editor = source.FindFirst(TreeScope.Descendants, new PropertyCondition(
                    AutomationElement.AutomationIdProperty, "EditableSample"));
                var pattern = (TextPattern)editor.GetCurrentPattern(TextPattern.Pattern);
                var caret = pattern.DocumentRange.Clone();
                caret.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
                caret.Select();
                Require(pattern.GetSelection().All(range => range.GetText(1).Length == 0),
                    "fixture selection is collapsed before pressing Search shortcut");
                var beforeEmpty = launched.Count;
                SendMessage(owner.Handle, Native.WM_HOTKEY, 3, (searchHotkey.Key << 16) | searchHotkey.Modifiers);
                Require((await emptyResult.Task.WaitAsync(TimeSpan.FromSeconds(4))).Contains("未取得選取文字")
                    && launched.Count == beforeEmpty, "Search shortcut with no selection does not launch a browser");
                emptySelection.Dispose();
                pattern.DocumentRange.Select();

                result = 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); }
            finally
            {
                if (child != null) { if (!child.HasExited) child.CloseMainWindow(); child.Dispose(); }
                Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed");
                app.Shutdown();
            }
        };
        app.Run();
        return result;
    }
}
