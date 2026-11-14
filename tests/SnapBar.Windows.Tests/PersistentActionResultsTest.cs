using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class PersistentActionResultsTest
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();

    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(item, i))) yield return child;
    }

    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var passed = 0; var exit = 1;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed++; Console.WriteLine("PASS " + name);
        }
        app.Startup += async (_, _) =>
        {
            Process? fixture = null;
            SelectionController? controller = null;
            using var owner = new HwndSource(new HwndSourceParameters("SnapBar retained results checks") { Width = 1, Height = 1, WindowStyle = 0 });
            using var backup = new ClipboardBackup();
            var captured = false;
            ActionResultWindow[] Results() => app.Windows.OfType<ActionResultWindow>().Where(window => window.IsVisible).ToArray();
            void Locked(Action action)
            {
                if (!OpenClipboard(owner.Handle)) throw new InvalidOperationException("Clipboard busy");
                try { action(); } finally { CloseClipboard(); }
            }
            async Task StartFixture(bool editable)
            {
                if (fixture != null)
                {
                    fixture.CloseMainWindow(); await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); fixture.Dispose();
                }
                var start = new ProcessStartInfo(TestPaths.AppExecutable) { UseShellExecute = false };
                start.Environment["SNAPBAR_FOREGROUND_TEST_RUNNER_PID"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                start.ArgumentList.Add("--fixture");
                if (!editable) start.ArgumentList.Add("--web-search");
                fixture = Process.Start(start)!;
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (fixture.MainWindowHandle == 0 && !fixture.HasExited && DateTime.UtcNow < deadline)
                { await Task.Delay(30); fixture.Refresh(); }
                Console.WriteLine("WAITING: activate the dedicated retained-result fixture (up to 60 seconds)");
                while (!fixture.HasExited && Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != fixture.Id && DateTime.UtcNow < deadline)
                { fixture.Refresh(); Native.SetForegroundWindow(fixture.MainWindowHandle); await Task.Delay(50); }
                Check(!fixture.HasExited && Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == fixture.Id,
                    $"dedicated fixture is foreground (exited={fixture.HasExited}, handle={fixture.MainWindowHandle})");
                await Task.Delay(150);
            }
            try
            {
                Locked(() => { if (backup.Capture() != "ok") throw new InvalidOperationException("Clipboard backup failed"); captured = true; });
                // Disposable output only: layout checks do not call an external API.
                const string sample = "IP: 192.0.2.1\n位置: New Taipei City, Taiwan (TW)\n座標: 25.0693, 121.4626\nISP: Example Internet\n組織: Example Network\nAS: AS64500 Example\n判定: 一般網路";
                var preview = new ActionResultWindow("GeoIP fixture", sample, false, false, persistent: true);
                preview.Show(); await Task.Delay(250); preview.UpdateLayout();
                var previewDocument = Children<FlowDocumentScrollViewer>(preview).Single().Document;
                Check(new System.Windows.Documents.TextRange(previewDocument.ContentStart, previewDocument.ContentEnd).Text.Contains("New Taipei City"),
                    "field output remains readable in selectable content");
                Check(preview.ActualHeight < 480, "seven-line result fits content instead of fixed large window");
                Check(Children<Button>(preview).Single(button => AutomationProperties.GetName(button) == "複製結果").Background == ActionUi.Accent,
                    "copy is the primary result action");
                Check(Children<Button>(preview).Single(button => AutomationProperties.GetName(button) == "套用至原文").Visibility == Visibility.Collapsed,
                    "unavailable Apply does not occupy the result footer");
                UiAppearanceTest.Snapshot(preview, "action-result-layout-zh.png");
                Children<Button>(preview).Single(button => AutomationProperties.GetName(button) == "複製結果").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Clipboard.GetText() == sample, "field presentation copies the complete original output in its original order");
                preview.Width = 320; await Task.Delay(100);
                Check(preview.ActualWidth <= 321, "result remains resizable at its narrow minimum");
                UiAppearanceTest.Snapshot(preview, "action-result-layout-narrow.png");
                preview.Close();
                var longResult = new ActionResultWindow("Long output fixture", string.Join('\n', Enumerable.Repeat("A long result line for scrolling.", 100)), false, false, persistent: true);
                longResult.Show(); await Task.Delay(200);
                var longScroll = Children<ScrollViewer>(longResult).First(scroll => scroll.ExtentHeight > scroll.ViewportHeight);
                longScroll.ScrollToEnd(); await Task.Delay(100);
                Check(longScroll.VerticalOffset > 0 && longResult.ActualHeight <= 620, "long output scrolls inside a bounded result window");
                longResult.SizeToContent = SizeToContent.Manual;
                longResult.Height = 280; await Task.Delay(100);
                Check(longScroll.ViewportHeight < 200 && longScroll.ScrollableHeight > 0,
                    "manually shortened window keeps its output scrollable");
                longResult.Close();
                Func<CustomActionDefinition, string, CancellationToken, Task<ScriptExecutionResult>> runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("ok", "first fixture result"));
                controller = new SelectionController(owner, TestPaths.ProbeExecutable,
                    runCustom: (action, text, token) => runner(action, text, token));
                var action = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "Retained result fixture",
                    CustomActionKind.PowerShell, "terminal", "[Console]::Write('fixture')", ActionOutput.Window);
                void Configure(ActionOutput mode) => controller.ConfigureActions(ActionCatalog.Normalize(new() { CustomActions = [action with { Output = mode }] }));
                async Task Execute()
                {
                    await controller.CaptureAsync(false, 0, 0);
                    Check(controller.ActiveSelection != null, "fixture selection passes initial capture");
                    await controller.ExecuteCustomAsync(action.Id);
                }
                await StartFixture(false); Configure(ActionOutput.Window);
                await Execute(); var first = Results().Single();
                Check(first.ShowInTaskbar && !first.ShowActivated && first.Topmost, "retained result has taskbar entry and does not activate");
                runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("ok", "# Second fixture result"));
                Configure(ActionOutput.Markdown); await Execute(); var second = Results().Single(window => window != first);
                Check(first.IsVisible && Children<FlowDocumentScrollViewer>(second).Any(), "new Markdown result keeps previous text result open");
                Check(first.Left != second.Left || first.Top != second.Top, "results are offset rather than exactly overlapping");
                var onTop = Children<ToggleButton>(first).Single(box => AutomationProperties.GetAutomationId(box) == "KeepActionResultOnTop");
                onTop.IsChecked = false;
                Check(!first.Topmost && second.Topmost && first.IsVisible, "individual topmost toggle does not close results");
                await Task.Delay(2700);
                Check(Results().Length == 2, "retained windows stay open beyond the transient HUD timeout");
                Children<Button>(first).Single(button => AutomationProperties.GetName(button) == "複製結果").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Clipboard.GetText() == "first fixture result", "old result remains independently copyable");
                UiAppearanceTest.Snapshot(first, "action-result-retained-zh.png");
                first.Close(); Check(second.IsVisible && Results().Length == 1, "closing one result does not close another"); second.Close();

                foreach (var mode in new[] { ActionOutput.Window, ActionOutput.Markdown, ActionOutput.Clipboard, ActionOutput.ReplaceSelection })
                {
                    // Use a real runner for window output, so its process lifetime is exercised as well.
                    Configure(mode);
                    var switched = new Window { Title = "SnapBar unrelated result fixture", Content = new TextBox(), Width = 260, Height = 150 };
                    try
                    {
                        var cancelled = false;
                        runner = async (definition, text, token) =>
                        {
                            var running = CustomActionRunner.RunAsync(definition with { Content = "Start-Sleep -Milliseconds 500; [Console]::Write('detached fixture result')" }, text, token);
                            switched.Show(); switched.Activate(); await Task.Delay(300);
                            Check(Native.GetForegroundWindow() == new WindowInteropHelper(switched).Handle, mode + " fixture actually switches foreground");
                            cancelled = token.IsCancellationRequested;
                            return await running;
                        };
                        var sequence = Native.GetClipboardSequenceNumber();
                        await Execute();
                        if (mode is ActionOutput.Window or ActionOutput.Markdown)
                        {
                            var window = Results().Single();
                            Check(!cancelled && controller.ActiveSelection == null, mode + " completes after source foreground is invalidated");
                            Check(Children<Button>(window).Single(button => AutomationProperties.GetName(button) == "套用至原文") is { IsEnabled: false, Visibility: Visibility.Collapsed },
                                mode + " detached result has no Apply permission");
                            Check(Native.GetForegroundWindow() == new WindowInteropHelper(switched).Handle, mode + " presentation does not activate");
                            window.Close();
                        }
                        else if (mode == ActionOutput.Clipboard)
                            Check(cancelled && Results().Length == 0, "foreground loss still cancels automatic clipboard output");
                        else Check(Results().Length == 0, "readonly source cannot execute replacement output");
                        Check(Native.GetClipboardSequenceNumber() == sequence, mode + " does not modify clipboard on foreground loss");
                    }
                    finally { switched.Close(); Native.SetForegroundWindow(fixture!.MainWindowHandle); await Task.Delay(150); }
                }

                Configure(ActionOutput.Window);
                runner = (_, _, _) => { controller.CancelSelection(); return Task.FromResult(new ScriptExecutionResult("ok", "cancelled fixture")); };
                await Execute(); Check(Results().Length == 0, "explicit cancellation suppresses a late readonly result");
                runner = (_, _, _) => { Configure(ActionOutput.Window); return Task.FromResult(new ScriptExecutionResult("script-failed", Error: "old failure")); };
                await Execute(); Check(Results().Length == 0, "configuration change suppresses stale diagnostics");
                runner = async (_, _, token) => { await controller.CaptureAsync(false, 0, 0); return new("ok", "superseded fixture"); };
                await Execute(); Check(Results().Length == 0, "new capture supersedes a running readonly query");

                // Short HUD output retains its original automatic dismissal behavior.
                Configure(ActionOutput.Hud);
                runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("ok", "short fixture"));
                await Execute(); Check(Results().Length == 1, "transient HUD still presents");
                await Task.Delay(2800); Check(Results().Length == 0, "transient HUD still closes automatically");

                await StartFixture(true); Configure(ActionOutput.Window);
                var edit = AutomationElement.FromHandle(fixture!.MainWindowHandle).FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "EditableSample"));
                var value = (ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern);
                var original = value.Current.Value;
                runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("ok", "Changed"));
                await Execute(); var old = Results().Single();
                var oldApply = Children<Button>(old).Single(button => AutomationProperties.GetName(button) == "套用至原文");
                Check(oldApply.IsEnabled, "current editable source enables Apply");
                UiText.SetLanguage("en");
                await Execute(); var latest = Results().Single(window => window != old);
                Check(old.IsVisible && !oldApply.IsEnabled, "new query retains old output but invalidates old Apply");
                Check(AutomationProperties.GetName(Children<ToggleButton>(latest).Single()) == "Keep on top"
                    && AutomationProperties.GetName(Children<ToggleButton>(old).Single()) == "保持置頂", "each retained window keeps its creation language");
                UiAppearanceTest.Snapshot(latest, "action-result-retained-en.png");
                var before = Native.GetClipboardSequenceNumber();
                oldApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
                Check(value.Current.Value == original && Native.GetClipboardSequenceNumber() == before, "forced stale Apply cannot modify source or clipboard");
                var apply = Children<Button>(latest).Single(button => AutomationProperties.GetName(button) == "Apply to selection");
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (!value.Current.Value.StartsWith("Changed Windows", StringComparison.Ordinal) && DateTime.UtcNow < deadline) await Task.Delay(25);
                Check(value.Current.Value.StartsWith("Changed Windows", StringComparison.Ordinal) && old.IsVisible && latest.IsVisible,
                    "valid Apply still replaces original selection without closing retained results");
                controller.BeginShutdown(); Check(Results().Length == 0, "shutdown closes all retained results");
                exit = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error.Message); }
            finally
            {
                controller?.Dispose();
                foreach (var window in Results()) window.Close();
                if (fixture != null) { if (!fixture.HasExited) fixture.CloseMainWindow(); fixture.Dispose(); }
                if (captured)
                {
                    try { Locked(() => { if (!backup.Restore()) throw new InvalidOperationException("Clipboard restore failed"); }); }
                    catch (Exception error) { exit = 1; Console.WriteLine("FAIL " + error.Message); }
                }
                UiText.SetLanguage("zh-Hant");
                Console.WriteLine($"{passed} passed, {(exit == 0 ? 0 : 1)} failed"); app.Shutdown();
            }
        };
        app.Run(); return exit;
    }
}
