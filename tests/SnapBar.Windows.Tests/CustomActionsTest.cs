using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class CustomActionsTest
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(item, i))) yield return child;
    }
    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var passed = 0; var result = 1;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); passed++; }
        app.Startup += async (_, _) =>
        {
            Process? fixture = null;
            HwndSource? owner = null;
            using var clipboardBackup = new ClipboardBackup();
            var clipboardCaptured = false;
            nint ownerHandle = 0;
            void Locked(Action action)
            {
                if (!OpenClipboard(ownerHandle)) throw new InvalidOperationException("Clipboard busy");
                try { action(); } finally { CloseClipboard(); }
            }
            try
            {
                owner = new HwndSource(new HwndSourceParameters("SnapBar custom action checks") { Width = 1, Height = 1, WindowStyle = 0 });
                ownerHandle = owner.Handle;
                var start = new ProcessStartInfo(Path.GetFullPath("artifacts/p0/SnapBar.App.exe")) { UseShellExecute = false };
                start.ArgumentList.Add("--fixture"); start.ArgumentList.Add("--web-search"); fixture = Process.Start(start)!;
                var deadline = DateTime.UtcNow.AddSeconds(60);
                Console.WriteLine("WAITING: activate the dedicated SnapBar custom fixture (up to 60 seconds)");
                while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != fixture.Id)
                { if (fixture.HasExited || DateTime.UtcNow > deadline) throw new InvalidOperationException("Fixture must be foreground"); await Task.Delay(25); }
                await Task.Delay(120);
                var launched = new List<Uri>(); var calls = 0; var cancelled = false;
                Func<CustomActionDefinition, string, CancellationToken, Task<ScriptExecutionResult>> runner = (_, _, _) => { calls++; return Task.FromResult(new ScriptExecutionResult("ok", "# Result\n\n- Example")); };
                using var controller = new SelectionController(owner, Path.GetFullPath("artifacts/p0/Probe/SnapBar.Probe.exe"), launched.Add,
                    runCustom: (action, text, token) => runner(action, text, token));
                var url = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "搜尋測試", CustomActionKind.Url, "globe", "https://example.com/search?q=***");
                var script = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "JSON 測試", CustomActionKind.JavaScript, "code", "console.log(process.env.POPCLIP_TEXT)", ActionOutput.Markdown);
                var replace = script with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "替換測試", Output = ActionOutput.ReplaceSelection };
                var actions = ActionCatalog.Normalize(new() { CustomActions = [url, script, replace] }); controller.ConfigureActions(actions);
                async Task Capture() { await controller.CaptureAsync(false, 0, 0); Check(controller.ActiveSelection is { Editable: false }, "capture retains readonly source"); }
                PopupWindow Popup() => app.Windows.OfType<PopupWindow>().Single(w => w.IsVisible);
                await Capture();
                var popup = Popup(); var oldOrder = Children<Button>(popup).Where(b => b.IsVisible).Select(AutomationProperties.GetName).ToArray();
                Check(oldOrder.Take(2).SequenceEqual(new[] { "複製", "搜尋" }), "compact bar hides readonly writes without losing order");
                Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(popup.MenuOpen && Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == fixture.Id, "More opens without stealing foreground");
                Check(popup.HandleMenuKey(0x4A) && Children<Button>(popup).Any(b => AutomationProperties.GetAutomationId(b) == "MoreAction_" + script.Id), "More keyboard filter searches custom action name");
                popup.HandleMenuKey(0x1B); Check(popup.MenuOpen, "first Escape clears query"); popup.HandleMenuKey(0x1B); Check(!popup.MenuOpen, "second Escape closes More");
                await controller.ExecuteCustomAsync(url.Id);
                Check(launched.Count == 1 && Uri.UnescapeDataString(launched[0].Query[3..]) == FixtureWindow.SearchFixtureText, "custom URL revalidates and encodes full Unicode selection");
                await Capture(); await controller.ExecuteCustomAsync(script.Id);
                Check(calls == 1 && app.Windows.OfType<ActionResultWindow>().Any(w => w.IsVisible), "script result routes to actual result window");
                Check(Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == fixture.Id, "result window preserves original foreground");
                var resultWindow = app.Windows.OfType<ActionResultWindow>().Single(w => w.IsVisible);
                Check(!Children<Button>(resultWindow).Single(b => AutomationProperties.GetName(b) == "套用至原文").IsEnabled, "readonly result cannot apply");
                resultWindow.Close(); await Capture(); await controller.ExecuteCustomAsync(replace.Id);
                Check(calls == 1, "readonly replacement is rejected before executing script");
                foreach (var mode in new[] { ActionOutput.None, ActionOutput.Clipboard, ActionOutput.Hud, ActionOutput.Window, ActionOutput.Markdown })
                {
                    controller.ConfigureActions(actions with { CustomActions = [url, script with { Output = mode }, replace] });
                    runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("script-failed", "partial stdout", "original failure\nsecond line", 7));
                    await Capture(); var sequence = Native.GetClipboardSequenceNumber();
                    await controller.ExecuteCustomAsync(script.Id);
                    var errorWindow = app.Windows.OfType<ActionResultWindow>().Single(w => w.IsVisible);
                    Check(!Children<Button>(errorWindow).Any(b => AutomationProperties.GetName(b) == "套用至原文"), mode + " failure routes to diagnostic without Apply");
                    Children<Expander>(errorWindow).Single().IsExpanded = true; errorWindow.UpdateLayout();
                    Check(Children<TextBox>(errorWindow).Any(t => t.Text.Contains("original failure\nsecond line") && t.Text.Contains("partial stdout") && t.Text.Contains("結束代碼：7")), mode + " failure retains complete diagnostics");
                    Check(Native.GetClipboardSequenceNumber() == sequence && Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == fixture.Id,
                        mode + " failure does not alter clipboard or original foreground");
                    errorWindow.Close();
                }
                controller.ConfigureActions(actions);
                await Capture();
                runner = async (_, _, token) => { controller.Automatic = false; try { await Task.Delay(5000, token); } catch (OperationCanceledException) { cancelled = true; } return new("ok", "late result"); };
                await controller.ExecuteCustomAsync(script.Id);
                Check(cancelled && !app.Windows.OfType<ActionResultWindow>().Any(w => w.IsVisible), "invalidated script cancels token and never presents late result");
                await Capture();
                runner = (_, _, _) => { controller.Automatic = false; return Task.FromResult(new ScriptExecutionResult("script-failed", Error: "late failure")); };
                await controller.ExecuteCustomAsync(script.Id);
                Check(!app.Windows.OfType<ActionResultWindow>().Any(w => w.IsVisible), "invalidated script never presents late failure diagnostics");
                await Capture();
                popup = Popup(); Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                popup.UpdateLayout(); var search = Children<TextBox>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActionSearch");
                var point = search.PointToScreen(new Point(20, Math.Max(1, search.ActualHeight / 2)));
                SetCursorPos((int)point.X, (int)point.Y); mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0); await Task.Delay(250);
                Check(search.IsKeyboardFocused && popup.HasSearchForeground && controller.ActiveSelection != null,
                    "search owns native focus without invalidating source session across foreground timer ticks");
                search.Text = "搜尋測試";
                var completion = new TaskCompletionSource();
                void Completed(string _) => completion.TrySetResult();
                controller.Status += Completed;
                try
                {
                    Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreAction_" + url.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await completion.Task.WaitAsync(TimeSpan.FromSeconds(4));
                }
                finally { controller.Status -= Completed; }
                Check(launched.Count == 2 && Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == fixture.Id,
                    "search result action restores original foreground and revalidates before URL launch");
                await Capture(); popup = Popup();
                Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                popup.UpdateLayout(); search = Children<TextBox>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActionSearch");
                point = search.PointToScreen(new Point(20, Math.Max(1, search.ActualHeight / 2)));
                SetCursorPos((int)point.X, (int)point.Y); mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0); await Task.Delay(100);
                var sourceElement = AutomationElement.FromHandle(fixture.MainWindowHandle).FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "EditableSample"));
                var sourcePattern = (TextPattern)sourceElement.GetCurrentPattern(TextPattern.Pattern); var originalRange = sourcePattern.GetSelection()[0].Clone();
                var changedRange = originalRange.Clone(); changedRange.MoveEndpointByUnit(System.Windows.Automation.Text.TextPatternRangeEndpoint.End,
                    System.Windows.Automation.Text.TextUnit.Character, -1); changedRange.Select();
                await controller.ExecuteCustomAsync(url.Id);
                Check(launched.Count == 2, "source range changed during search is rejected after focus restoration"); originalRange.Select();
                await Capture(); controller.Automatic = false; await controller.ExecuteCustomAsync(url.Id);
                Check(launched.Count == 2, "stale URL action never launches");

                await Capture(); popup = Popup();
                Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                popup.UpdateLayout(); search = Children<TextBox>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActionSearch");
                point = search.PointToScreen(new Point(20, Math.Max(1, search.ActualHeight / 2)));
                SetCursorPos((int)point.X, (int)point.Y); mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0); await Task.Delay(100);
                var unrelated = new Window { Title = "SnapBar unrelated search fixture", Content = new TextBox(), Width = 260, Height = 150 };
                unrelated.Show(); unrelated.Activate(); await Task.Delay(250);
                var unrelatedHandle = new WindowInteropHelper(unrelated).Handle;
                Check(controller.ActiveSelection == null && !popup.IsVisible, "leaving focused search invalidates session and closes popup");
                await controller.ExecuteCustomAsync(url.Id);
                Check(launched.Count == 2 && Native.GetForegroundWindow() == unrelatedHandle, "stale search action cannot pull focus away from unrelated window"); unrelated.Close();

                fixture.CloseMainWindow(); await fixture.WaitForExitAsync(); fixture.Dispose();
                var editableStart = new ProcessStartInfo(Path.GetFullPath("artifacts/p0/SnapBar.App.exe")) { UseShellExecute = false };
                editableStart.ArgumentList.Add("--fixture"); fixture = Process.Start(editableStart)!;
                Console.WriteLine("WAITING: activate the dedicated editable SnapBar fixture (up to 60 seconds)");
                deadline = DateTime.UtcNow.AddSeconds(60);
                while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != fixture.Id)
                { if (fixture.HasExited || DateTime.UtcNow > deadline) throw new InvalidOperationException("Editable fixture must be foreground"); await Task.Delay(25); }
                await Task.Delay(120);
                Locked(() => { var captured = clipboardBackup.Capture(); if (captured != "ok") throw new InvalidOperationException("Clipboard backup failed"); clipboardCaptured = true; });
                var editElement = AutomationElement.FromHandle(fixture.MainWindowHandle).FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "EditableSample"));
                var value = (ValuePattern)editElement.GetCurrentPattern(ValuePattern.Pattern);
                var originalValue = value.Current.Value;
                runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("script-failed", "partial", "replacement failure 中文", 9));
                await controller.CaptureAsync(false, 0, 0);
                var beforeFailure = Native.GetClipboardSequenceNumber();
                await controller.ExecuteCustomAsync(replace.Id);
                var replacementError = app.Windows.OfType<ActionResultWindow>().Single(w => w.IsVisible);
                Check(value.Current.Value == originalValue && Native.GetClipboardSequenceNumber() == beforeFailure
                    && !Children<Button>(replacementError).Any(b => AutomationProperties.GetName(b) == "套用至原文"),
                    "editable replacement failure preserves source and clipboard and omits Apply");
                Children<Button>(replacementError).Single(b => AutomationProperties.GetName(b) == "複製詳細資訊").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Clipboard.GetText().Contains("replacement failure 中文") && Clipboard.GetText().Contains("結束代碼：9"),
                    "explicit copy includes full original error and exit code");
                replacementError.Close();

                var prompt = script with { Kind = CustomActionKind.PowerShell, Content = "curl ifconfig.me", Output = ActionOutput.ReplaceSelection };
                controller.ConfigureActions(actions with { CustomActions = [url, prompt, replace] });
                runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("ok", Error: "curl : Windows PowerShell is in NonInteractive mode. Read and Prompt functionality is not available.", ExitCode: 0));
                await controller.CaptureAsync(false, 0, 0); beforeFailure = Native.GetClipboardSequenceNumber();
                await controller.ExecuteCustomAsync(prompt.Id);
                var promptError = app.Windows.OfType<ActionResultWindow>().Single(w => w.IsVisible);
                Check(value.Current.Value == originalValue && Native.GetClipboardSequenceNumber() == beforeFailure
                    && !Children<Button>(promptError).Any(b => AutomationProperties.GetName(b) == "套用至原文"),
                    "unavailable PowerShell prompt with exit zero cannot paste an empty error result");
                promptError.Close(); controller.ConfigureActions(actions);
                runner = (_, _, _) => Task.FromResult(new ScriptExecutionResult("ok", "Changed"));
                await controller.CaptureAsync(false, 0, 0);
                Check(controller.ActiveSelection is { Editable: true }, "editable fixture authorizes result application");
                await controller.ExecuteCustomAsync(script.Id);
                var editableResult = app.Windows.OfType<ActionResultWindow>().Single(w => w.IsVisible);
                var apply = Children<Button>(editableResult).Single(b => AutomationProperties.GetName(b) == "套用至原文");
                Check(apply.IsEnabled, "editable original selection enables Apply");
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                deadline = DateTime.UtcNow.AddSeconds(4);
                while (!value.Current.Value.StartsWith("Changed Windows", StringComparison.Ordinal) && DateTime.UtcNow < deadline) await Task.Delay(30);
                Check(value.Current.Value.StartsWith("Changed Windows", StringComparison.Ordinal), "Apply replaces only the original selected range in disposable editor");
                Check(Clipboard.GetText() == "Changed", "Apply leaves expected result on clipboard");
                editableResult.Close();
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error.Message); }
            finally
            {
                if (clipboardCaptured)
                {
                    try { Locked(() => { if (!clipboardBackup.Restore()) throw new InvalidOperationException("Clipboard restore failed"); }); }
                    catch (Exception error) { result = 1; Console.WriteLine("FAIL " + error.Message); }
                }
                if (fixture != null) { if (!fixture.HasExited) fixture.CloseMainWindow(); fixture.Dispose(); }
                owner?.Dispose();
                Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed"); app.Shutdown();
            }
        };
        app.Run(); return result;
    }
}
