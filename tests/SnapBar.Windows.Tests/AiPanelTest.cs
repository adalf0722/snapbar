using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class AiPanelTest
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        for (var n = 0; n < VisualTreeHelper.GetChildrenCount(item); n++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(item, n))) yield return child;
    }
    private static Button Button(Window window, string id) => Children<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == id);
    private static void Snapshot(Window window, string name)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap(Math.Max(1, (int)content.ActualWidth), Math.Max(1, (int)content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        var directory = Path.Combine(TestPaths.Repository, "artifacts", "ai-panel"); Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name)); png.Save(stream);
    }
    private sealed class Client : IAiTextClient
    {
        public int Calls;
        public Func<string, string, CancellationToken, Task<AiTextResult>> Handler = (_, _, _) => Task.FromResult(new AiTextResult(true, "A clearer English version."));
        public Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string key, string source, string instruction, CancellationToken token) { Calls++; return Handler(source, instruction, token); }
    }
    public static int Run(bool preview = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var exit = 1; var passed = 0;
        var directory = Path.Combine(Path.GetTempPath(), "snapbar-ai-panel-" + Guid.NewGuid().ToString("N"));
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS " + name); }
        app.Startup += async (_, _) =>
        {
            AiPanelWindow? panel = null; Window? unrelated = null; Process? fixture = null; SelectionController? controller = null; HwndSource? owner = null;
            using var backup = new ClipboardBackup(); var captured = false;
            try
            {
                owner = new HwndSource(new HwndSourceParameters("SnapBar AI checks") { Width = 1, Height = 1, WindowStyle = 0 });
                if (!OpenClipboard(owner.Handle)) throw new InvalidOperationException("Clipboard busy");
                try { Check(backup.Capture() == "ok", "AI fixture preserves original clipboard"); captured = true; } finally { CloseClipboard(); }
                var preferences = new AiPreferences(directory); var settings = preferences.Settings;
                var compatible = settings.Profiles.Single(p => p.Id == "compatible") with { Model = "fixture-model" };
                Check(preferences.TrySave(settings with { SelectedProfileId = compatible.Id, Profiles = settings.Profiles.Select(p => p.Id == compatible.Id ? compatible : p).ToArray() }, out _), "AI fixture uses isolated configured service without credentials");
                var client = new Client();
                var snapshot = new SelectionSnapshot("ui", new(0, 1, 1), "# Literal source\n**keep original markers**", new(100, 100, 50, 20), false, 1, 43);
                panel = new(snapshot, preferences, client: client); panel.Show(); panel.Activate(); await Task.Delay(100);
                Check(panel.Session.ViewingOriginal && !Button(panel, "AiResultTab").IsEnabled && Children<TextBox>(panel).Any(t => t.Text == snapshot.Text && t.IsReadOnly), "AI opens with literal original and no empty result page");
                Check(!Children<Expander>(panel).Single().IsExpanded, "AI category filter starts collapsed"); Snapshot(panel, "original.png");
                var composition = new TextComposition(InputManager.Current, panel.CommandInput, "測"); panel.CommandInput.Text = "composition fixture";
                panel.CommandInput.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
                panel.CommandInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(panel), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Check(client.Calls == 0 && !panel.Session.Busy, "AI IME composition confirmation Enter never submits a request");
                panel.CommandInput.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent }); panel.CommandInput.Clear();
                var category = (ComboBox)Children<Expander>(panel).Single().Content; category.SelectedItem = "寫作"; panel.CommandInput.Text = "translate";
                Check(Children<Button>(panel).Any(b => AutomationProperties.GetAutomationId(b) == "AiPrompt_translate-en"), "AI search ignores current browsing category");
                await panel.SubmitAsync("Translate to English", "translate-en");
                Check(panel.Session.Current?.Text == "A clearer English version." && panel.CommandInput.Text.Length == 0 && panel.Session.BasisId == panel.Session.Current.Id, "AI completion consumes submitted search and prepares continuation");
                string? seenSource = null; client.Handler = (source, _, _) => { seenSource = source; return Task.FromResult(new AiTextResult(true, "Clear English.")); };
                panel.CommandInput.Text = "再短一點"; await panel.SubmitAsync(panel.CommandInput.Text);
                Check(seenSource == "A clearer English version." && panel.Session.Current?.BasisLabel == "第 1 版", "AI next rewrite uses prior result rather than original");
                var delayed = new TaskCompletionSource<AiTextResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                client.Handler = (_, _, _) => delayed.Task; panel.CommandInput.Text = "pending"; var pending = panel.SubmitAsync("pending");
                panel.CommandInput.Text = "new draft"; Button(panel, "AiOriginalTab").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                delayed.SetResult(new(true, "third result")); await pending;
                Check(panel.CommandInput.Text == "new draft" && panel.Session.ViewingOriginal && panel.Session.HasNewResult && panel.Session.BasisLabel == "第 2 版", "AI completion preserves new draft and explicit original view");
                var versions = panel.Session.Versions.Count; client.Handler = (_, _, _) => Task.FromResult(new AiTextResult(false, Status: "authentication"));
                await panel.SubmitAsync("failure"); Check(panel.Session.Versions.Count == versions && panel.Session.Current?.Text == "third result", "AI failure preserves the latest completed result");
                Button(panel, "AiResultTab").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); panel.UpdateLayout();
                Check(!Button(panel, "AiReplace").IsVisible && Button(panel, "AiCopy").IsVisible, "AI readonly source offers copying without replacement");
                Snapshot(panel, "readonly-result.png"); panel.Width = 680; panel.Height = 500; panel.UpdateLayout(); Snapshot(panel, "small.png");
                Check(panel.CommandInput.ActualWidth > 200 && Button(panel, "AiCopy").ActualWidth > 0, "AI minimum window preserves command and footer controls");
                panel.Width = 800; panel.Height = 650;
                delayed = new(TaskCreationOptions.RunContinuationsAsynchronously); client.Handler = (_, _, _) => delayed.Task;
                pending = panel.SubmitAsync("late"); panel.InvalidateTarget(); delayed.SetResult(new(true, "late ignored")); await pending;
                Check(panel.Session.Current?.Text == "third result" && !panel.TargetValid, "AI invalidated target rejects a client completion that ignored cancellation");
                var settingsDialog = new AiSettingsWindow(preferences) { Owner = panel }; settingsDialog.Show(); settingsDialog.Activate(); await Task.Delay(100);
                Check(panel.HasForeground, "AI owned service settings remain part of panel foreground"); Snapshot(settingsDialog, "service-settings.png"); settingsDialog.Close();
                var savedPrompt = new AiPrompt("fixture-prompt", "測試指令", "Return concise text.", "自訂", "fixture");
                Check(preferences.TrySave(preferences.Settings with { Prompts = preferences.Settings.Prompts.Append(savedPrompt).ToArray(), PinnedIds = preferences.Settings.PinnedIds.Append(savedPrompt.Id).ToArray() }, out _)
                    && new AiPreferences(directory).Settings.Prompts.Any(p => p.Id == savedPrompt.Id), "AI prompt and pin persist independently of session text");
                var stored = File.ReadAllText(Path.Combine(directory, "ai.json")); Check(!stored.Contains("third result") && !stored.Contains("new draft") && !stored.Contains(snapshot.Text), "AI persisted settings omit source drafts and results");
                panel.Close(); panel = null;

                var start = new ProcessStartInfo(TestPaths.AppExecutable) { UseShellExecute = false }; start.ArgumentList.Add("--fixture"); fixture = Process.Start(start)!;
                async Task ForegroundFixture()
                {
                    fixture.Refresh(); Native.SetForegroundWindow(fixture.MainWindowHandle);
                    Console.WriteLine("WAITING: activate SnapBar P0 — Test Fixture if necessary (up to 60 seconds)");
                    var until = DateTime.UtcNow.AddSeconds(60);
                    while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != fixture.Id)
                    { if (fixture.HasExited || DateTime.UtcNow > until) throw new InvalidOperationException("AI test fixture must be foreground"); await Task.Delay(50); fixture.Refresh(); }
                }
                await ForegroundFixture(); await Task.Delay(150); fixture.Refresh();
                var element = AutomationElement.FromHandle(fixture.MainWindowHandle).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "EditableSample"));
                var value = (ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern); var before = value.Current.Value;
                client.Handler = (_, _, _) => Task.FromResult(new AiTextResult(true, "AI fixture replacement"));
                controller = new(owner, TestPaths.ProbeExecutable); controller.ConfigureAi(preferences, client);
                await controller.CaptureAsync(false, 0, 0); Check(controller.ActiveSelection is { Editable: true }, "AI captures an editable original selection");
                var selectedText = controller.ActiveSelection!.Text;
                await controller.ExecuteAsync(TextAction.Ai); panel = controller.ActiveAiWindow ?? throw new InvalidOperationException("AI panel did not open");
                async Task ForegroundPanel()
                {
                    panel.Activate(); Console.WriteLine("WAITING: activate SnapBar · AI 改寫 if necessary (up to 60 seconds)"); var until = DateTime.UtcNow.AddSeconds(60);
                    while (!panel.HasForeground) { if (DateTime.UtcNow > until) throw new InvalidOperationException("AI panel must be foreground"); await Task.Delay(50); }
                }
                await ForegroundPanel(); await Task.Delay(250);
                Check(controller.ActiveSelection != null && panel.TargetValid, "AI foreground timer preserves source while native panel owns focus");
                await panel.SubmitAsync("rewrite"); Snapshot(panel, "editable-result.png");
                await panel.ApplyAsync(); await Task.Delay(200);
                Check(value.Current.Value == "AI fixture replacement" + before[selectedText.Length..]
                    && Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId == fixture.Id && Clipboard.GetText() == "AI fixture replacement",
                    "AI replacement revalidates restores source and changes only original selected range");
                panel = null; value.SetValue(before); element.SetFocus();
                var range = ((TextPattern)element.GetCurrentPattern(TextPattern.Pattern)).DocumentRange.Clone();
                range.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start); range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 7); range.Select();
                await controller.CaptureAsync(false, 0, 0); await controller.ExecuteAsync(TextAction.Ai); panel = controller.ActiveAiWindow!;
                await ForegroundPanel(); await panel.SubmitAsync("rewrite");
                delayed = new(TaskCreationOptions.RunContinuationsAsynchronously); client.Handler = (_, _, _) => delayed.Task; pending = panel.SubmitAsync("late external");
                unrelated = new Window { Title = "SnapBar unrelated AI fixture", Content = new TextBox { Text = "Disposable unrelated text" }, Width = 300, Height = 180 };
                unrelated.Show(); unrelated.Activate(); await Task.Delay(250); var unrelatedHandle = new WindowInteropHelper(unrelated).Handle;
                Check(controller.ActiveSelection == null && !panel.TargetValid, "AI external foreground invalidates target and cancels pending generation");
                var sequence = Native.GetClipboardSequenceNumber(); delayed.SetResult(new(true, "late must not apply")); await pending; await panel.ApplyAsync();
                Check(panel.Session.Current?.Text == "AI fixture replacement" && value.Current.Value == before
                    && Native.GetForegroundWindow() == unrelatedHandle && Native.GetClipboardSequenceNumber() == sequence,
                    "AI stale completion cannot replace copy or steal unrelated foreground");
                unrelated.Close(); unrelated = null;
                if (preview)
                {
                    panel.Activate(); Console.WriteLine("PREVIEW: AI fixture panel uses fake responses; close panel to finish.");
                    var done = new TaskCompletionSource(); panel.Closed += (_, _) => done.TrySetResult(); await done.Task;
                }
                exit = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error); }
            finally
            {
                unrelated?.Close(); panel?.Close(); controller?.Dispose();
                if (fixture is { HasExited: false }) { fixture.CloseMainWindow(); await fixture.WaitForExitAsync(); } fixture?.Dispose();
                if (captured && owner != null)
                {
                    var locked = false;
                    for (var i = 0; i < 20 && !locked; i++) { locked = OpenClipboard(owner.Handle); if (!locked) await Task.Delay(20); }
                    if (!locked) { exit = 1; Console.WriteLine("FAIL AI fixture could not lock clipboard for restore"); }
                    else { try { if (!backup.Restore()) { exit = 1; Console.WriteLine("FAIL AI fixture clipboard restore failed"); } } finally { CloseClipboard(); } }
                }
                owner?.Dispose();
                var tempRoot = Path.GetFullPath(Path.GetTempPath());
                if (Directory.Exists(directory) && Path.GetFullPath(directory).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(directory).StartsWith("snapbar-ai-panel-", StringComparison.Ordinal)) Directory.Delete(directory, true);
                Console.WriteLine($"{passed} passed, {(exit == 0 ? 0 : 1)} failed"); app.Shutdown();
            }
        };
        app.Run(); return exit;
    }
}
