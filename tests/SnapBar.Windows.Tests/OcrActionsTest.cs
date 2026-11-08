using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;
using SnapBar.Windows.Ocr;

internal static class OcrActionsTest
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    private sealed class FixtureAi : IAiTextClient
    {
        public string? Source; public string? Instruction;
        public Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string key, string source, string instruction, CancellationToken token)
        { Source = source; Instruction = instruction; return Task.FromResult(new AiTextResult(true, "AI replacement")); }
    }
    private sealed class FixturePath : IPathRevealSession
    {
        public TaskCompletionSource<string> Prepared = new(); public int Reveals;
        public Task<string> PrepareAsync(string path, CancellationToken token) => Prepared.Task;
        public Task<string> RevealAsync(TargetIdentity target, CancellationToken token) { Reveals++; return Task.FromResult("ok"); }
        public void Dispose() { }
    }
    private static object? Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance);
    private static void Snapshot(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = System.IO.Path.Combine("artifacts", "ocr-shared-actions");
        System.IO.Directory.CreateDirectory(directory);
        using var stream = System.IO.File.Create(System.IO.Path.Combine(directory, name)); encoder.Save(stream);
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Children<T>(child)) yield return item;
    }
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var passed = 0; var failed = 0;
        void Check(string name, bool condition)
        { Console.WriteLine((condition ? "PASS " : "FAIL ") + name); if (condition) passed++; else failed++; }
        app.Startup += async (_, _) =>
        {
            try
            {
                var url = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "Fixture URL", CustomActionKind.Url, "globe", "https://example.org/?q=***");
                var live = ActionCatalog.Normalize(new ActionSettings { CustomActions = [url] });
                var launched = new List<Uri>(); var saves = 0; var managed = 0;
                var urlCard = new OcrResultWindow("https://example.org/?q={query}", null,
                    new OcrActionDependencies(() => live, () => "https://example.org/?q={query}", value => { live = value; saves++; return true; }, () => managed++, null, LaunchWeb: launched.Add));
                urlCard.SetResult(new OcrResult("ok", "original")); urlCard.Show();
                var urlEditor = Children<TextBox>(urlCard).Single(); urlEditor.Text = "corrected words"; urlEditor.Select(10, 5);
                var urlMore = Children<Button>(urlCard).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                MenuItem OpenUrl() { urlMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return urlMore.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_" + url.Id); }
                var urlRow = OpenUrl();
                await Task.Delay(50); Snapshot(urlCard, "zh-card.png"); Snapshot(urlMore.ContextMenu, "zh-more.png");
                Check("More retains corrected selection", urlEditor.SelectionStart == 10 && urlEditor.SelectionLength == 5);
                var menuSearch = (TextBox)urlMore.ContextMenu.Items.OfType<MenuItem>().First().Header;
                var down = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(menuSearch), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                menuSearch.RaiseEvent(down);
                Check("Down moves from search to an action", down.Handled);
                var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(menuSearch), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                menuSearch.RaiseEvent(escape);
                Check("Escape closes More and preserves selection", !urlMore.ContextMenu.IsOpen && urlEditor.SelectionStart == 10 && urlEditor.SelectionLength == 5);
                urlRow = OpenUrl();
                urlRow.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
                Check("configured URL uses corrected selected input and saves once", launched.Count == 1 && Uri.UnescapeDataString(launched[0].Query[3..]) == "words" && saves == 1);
                urlEditor.Select(0, 0); OpenUrl().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
                Check("configured URL uses corrected full input", launched.Count == 2 && Uri.UnescapeDataString(launched[1].Query[3..]) == "corrected words");
                urlRow = OpenUrl(); live = live with { CustomActions = [url with { Name = "Renamed" }] };
                urlRow.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
                Check("stale menu definition cannot launch", launched.Count == 2);
                Check("reopening reflects renamed action", (string)OpenUrl().Header == "Renamed");
                urlRow = OpenUrl();
                live = live with { Layout = live.Layout.Where(entry => entry.Id != url.Id).Append(new ActionLayoutEntry(url.Id, ActionPlacement.Hidden)).ToArray() };
                urlRow.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
                Check("old menu cannot execute a hidden action", launched.Count == 2);
                urlMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("hidden action disappears on reopening", !urlMore.ContextMenu.Items.OfType<MenuItem>().Any(item => AutomationProperties.GetAutomationId(item) == "OcrAction_" + url.Id));
                live = live with { CustomActions = [], Layout = live.Layout.Where(entry => entry.Id != url.Id).ToArray() };
                Check("removed action stays absent", !ActionCatalog.GetLocalActions(live).Any(item => item.Id == url.Id));
                var searchBox = (TextBox)urlMore.ContextMenu.Items.OfType<MenuItem>().First().Header;
                searchBox.Text = "no-such-fixture-action";
                await Task.Delay(30); Snapshot(urlMore.ContextMenu, "zh-search-empty.png");
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(searchBox), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                searchBox.RaiseEvent(key);
                Check("Enter with no search matches does not open management", managed == 0);
                searchBox.Text = "";
                urlMore.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrManageActions").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check("management preserves editor", managed == 1 && urlEditor.Text == "corrected words"); urlCard.Close();

                var replacement = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "Replace fixture", CustomActionKind.PowerShell, "terminal", "fixture", ActionOutput.ReplaceSelection);
                var replacementSettings = ActionCatalog.Normalize(new ActionSettings { CustomActions = [replacement] });
                string? input = null;
                var replacementCard = new OcrResultWindow("", null, new OcrActionDependencies(() => replacementSettings, () => "", _ => true, () => { }, null,
                    RunCustom: (_, text, _) => { input = text; return Task.FromResult(new ScriptExecutionResult("ok", "new")); }));
                replacementCard.SetResult(new OcrResult("ok", "old tail")); replacementCard.Show();
                var replacementEditor = Children<TextBox>(replacementCard).Single(); replacementEditor.Select(0, 3);
                var replacementMore = Children<Button>(replacementCard).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                var sequence = Native.GetClipboardSequenceNumber();
                replacementMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                replacementMore.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_" + replacement.Id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Task.Delay(30);
                Check("selected replacement uses captured input and preserves clipboard", input == "old" && replacementEditor.Text == "new tail" && Native.GetClipboardSequenceNumber() == sequence);
                replacementEditor.Undo(); Check("local replacement is undoable", replacementEditor.Text == "old tail"); replacementCard.Close();

                var noInput = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "No input fixture", CustomActionKind.PowerShell,
                    "terminal", "fixture", ActionOutput.None, UseSelection: false);
                var noInputSettings = ActionCatalog.Normalize(new ActionSettings { CustomActions = [noInput] });
                string? noInputReceived = null;
                var noInputCard = new OcrResultWindow("", null, new OcrActionDependencies(() => noInputSettings, () => "", _ => true, () => { }, null,
                    RunCustom: (_, text, _) => { noInputReceived = text; return Task.FromResult(new ScriptExecutionResult("ok", "not displayed")); }));
                noInputCard.SetResult(new OcrResult("ok", "fixture")); noInputCard.Show();
                Children<TextBox>(noInputCard).Single().Text = "";
                var noInputMore = Children<Button>(noInputCard).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                noInputMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var noInputRow = noInputMore.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_" + noInput.Id);
                Check("blank editor permits no-input action", noInputRow.IsEnabled);
                noInputRow.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
                Check("no-input None action receives empty input and opens no result", noInputReceived == "" && Field(noInputCard, "actionResult") == null);
                noInputCard.Close();

                using (var backup = new ClipboardBackup())
                {
                    if (!OpenClipboard(0)) throw new InvalidOperationException("Clipboard backup lock failed");
                    try { if (backup.Capture() != "ok") throw new InvalidOperationException("Clipboard backup failed"); }
                    finally { CloseClipboard(); }
                    try
                    {
                        var clipAction = noInput with { Id = "custom-" + Guid.NewGuid().ToString("N"), Output = ActionOutput.Clipboard };
                        var clipSettings = ActionCatalog.Normalize(new ActionSettings { CustomActions = [clipAction] });
                        var clipPending = new TaskCompletionSource<ScriptExecutionResult>();
                        var clipCard = new OcrResultWindow("", null, new OcrActionDependencies(() => clipSettings, () => "", _ => true, () => { }, null,
                            RunCustom: (_, _, _) => clipPending.Task));
                        clipCard.SetResult(new OcrResult("ok", "source")); clipCard.Show();
                        var clipEditor = Children<TextBox>(clipCard).Single();
                        var clipMore = Children<Button>(clipCard).Single(b => AutomationProperties.GetAutomationId(b) == "OcrMore");
                        void InvokeClip() { clipMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); clipMore.ContextMenu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "OcrAction_" + clipAction.Id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
                        InvokeClip(); clipEditor.Text = "edited while independent action runs";
                        clipPending.SetResult(new ScriptExecutionResult("ok", "clipboard fixture")); await Task.Delay(50);
                        Check("independent delayed clipboard output survives editor edits", Clipboard.GetText() == "clipboard fixture");
                        clipPending = new(); InvokeClip();
                        Check("fixture locks clipboard for output failure", OpenClipboard(0));
                        try { clipPending.SetResult(new ScriptExecutionResult("ok", "recoverable fixture output")); await Task.Delay(100); }
                        finally { CloseClipboard(); }
                        var retained = (ActionResultWindow?)Field(clipCard, "actionResult");
                        Check("busy clipboard retains output in result window", retained?.IsVisible == true && Children<TextBox>(retained).Any(t => t.Text.Contains("recoverable fixture output")) && Clipboard.GetText() == "clipboard fixture");
                        retained?.Close(); clipPending = new(); InvokeClip(); clipCard.Close();
                        var next = new OcrResultWindow("", null, new OcrActionDependencies(() => clipSettings, () => "", _ => true, () => { }, null));
                        next.SetResult(new OcrResult("ok", "new card")); next.Show();
                        clipPending.SetResult(new ScriptExecutionResult("ok", "late clipboard output")); await Task.Delay(50);
                        Check("closed card late output cannot change clipboard or new card", Clipboard.GetText() == "clipboard fixture" && Children<TextBox>(next).Single().Text == "new card");
                        next.Close();
                    }
                    finally
                    {
                        if (!OpenClipboard(0)) throw new InvalidOperationException("Clipboard restore lock failed");
                        try { if (!backup.Restore()) throw new InvalidOperationException("Clipboard restore failed"); }
                        finally { CloseClipboard(); }
                    }
                }

                var pathSession = new FixturePath();
                var pathCard = new OcrResultWindow("", null, new OcrActionDependencies(() => new ActionSettings(), () => "", _ => true, () => { }, null, CreatePathSession: () => pathSession));
                pathCard.SetResult(new OcrResult("ok", System.IO.Path.GetTempPath())); pathCard.Show();
                var pathEditor = Children<TextBox>(pathCard).Single();
                var pathMore = Children<Button>(pathCard).Single(b => AutomationProperties.GetAutomationId(b) == "OcrMore");
                pathMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pathMore.ContextMenu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "OcrAction_path").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                pathEditor.Text = "changed"; pathSession.Prepared.SetResult("ok"); await Task.Delay(50);
                Check("path preparation revalidates local ticket before reveal", pathSession.Reveals == 0); pathCard.Close();

                var dictionaryFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snapbar-ocr-dict-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dictionaryFolder);
                try
                {
                    var dictionary = new OfflineDictionaryLibrary(System.IO.Path.Combine(dictionaryFolder, "stored.bin"));
                    dictionary.Import(LookupTest.WriteDictionary(dictionaryFolder, "first", "First fixture", "first definition"));
                    dictionary.Import(LookupTest.WriteDictionary(dictionaryFolder, "second", "Second fixture", "second definition"));
                    var lookupCard = new OcrResultWindow("", null, new OcrActionDependencies(() => new ActionSettings(), () => "", _ => true, () => { }, null, Dictionary: dictionary));
                    lookupCard.SetResult(new OcrResult("ok", FixtureWindow.LookupFixtureText)); lookupCard.Show();
                    var lookupMore = Children<Button>(lookupCard).Single(b => AutomationProperties.GetAutomationId(b) == "OcrMore");
                    lookupMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    lookupMore.ContextMenu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "OcrAction_lookup").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    for (var n = 0; n < 100 && Field(lookupCard, "actionResult") == null; n++) await Task.Delay(20);
                    var lookupResult = (ActionResultWindow?)Field(lookupCard, "actionResult");
                    Check("OCR lookup groups results from both real fixture dictionaries", lookupResult != null && Children<TextBlock>(lookupResult).Any(t => t.Text.Contains("First fixture")) && Children<TextBlock>(lookupResult).Any(t => t.Text.Contains("Second fixture")));
                    lookupResult?.Close(); lookupCard.Close();
                }
                finally { System.IO.Directory.Delete(dictionaryFolder, true); }

                var delayed = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "Delayed fixture", CustomActionKind.JavaScript, "code", "fixture", ActionOutput.ReplaceSelection);
                var delayedSettings = ActionCatalog.Normalize(new ActionSettings { CustomActions = [delayed] });
                var pending = new TaskCompletionSource<ScriptExecutionResult>(); var invocations = 0;
                var delayedCard = new OcrResultWindow("", null, new OcrActionDependencies(() => delayedSettings, () => "", _ => true, () => { }, null,
                    RunCustom: (_, _, _) => { invocations++; return pending.Task; }));
                delayedCard.SetResult(new OcrResult("ok", "fixture text")); delayedCard.Show();
                var delayedEditor = Children<TextBox>(delayedCard).Single();
                var delayedMore = Children<Button>(delayedCard).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                void InvokeDelayed() { delayedMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); delayedMore.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_" + delayed.Id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
                InvokeDelayed(); InvokeDelayed();
                await Task.Delay(30); Snapshot(delayedCard, "zh-running.png");
                Check("busy action rejects duplicate invocation", invocations == 1 && !delayedMore.IsEnabled);
                delayedEditor.Text = "edited while running"; pending.SetResult(new ScriptExecutionResult("ok", "late")); await Task.Delay(30);
                Check("late replacement rejects changed text", delayedEditor.Text == "edited while running" && delayedMore.IsEnabled);
                pending = new(); InvokeDelayed();
                Children<Button>(delayedCard).Single(button => AutomationProperties.GetAutomationId(button) == "OcrActionCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pending.SetResult(new ScriptExecutionResult("ok", "cancelled late")); await Task.Delay(30);
                Check("cancel rejects late replacement", delayedEditor.Text == "edited while running" && delayedMore.IsEnabled);
                pending = new(); InvokeDelayed(); pending.SetResult(new ScriptExecutionResult("ok", "next success")); await Task.Delay(30);
                Check("next action succeeds after cancellation", delayedEditor.Text == "next success" && invocations == 3);
                Snapshot(delayedCard, "zh-applied.png"); delayedCard.Close();

                UiText.SetLanguage("en");
                var englishPending = new TaskCompletionSource<ScriptExecutionResult>();
                var englishRunning = new OcrResultWindow("", null, new OcrActionDependencies(() => delayedSettings, () => "", _ => true, () => { }, null, RunCustom: (_, _, _) => englishPending.Task));
                englishRunning.SetResult(new OcrResult("ok", "fixture text")); englishRunning.Show();
                var englishRunningMore = Children<Button>(englishRunning).Single(b => AutomationProperties.GetAutomationId(b) == "OcrMore");
                englishRunningMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                englishRunningMore.ContextMenu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "OcrAction_" + delayed.Id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Task.Delay(30); Snapshot(englishRunning, "en-running.png");
                englishPending.SetResult(new ScriptExecutionResult("ok", "applied fixture")); await Task.Delay(30); Snapshot(englishRunning, "en-applied.png"); englishRunning.Close();
                var englishCard = new OcrResultWindow("", null, new OcrActionDependencies(() => noInputSettings, () => "", _ => true, () => { }, null));
                englishCard.SetResult(new OcrResult("ok", "fixture text")); englishCard.Show();
                WindowWorkArea.SetWorkAreaOverride(englishCard, new Rect(0, 0, 360, 240)); englishCard.UpdateLayout();
                Check("OCR card remains within restricted work area", englishCard.ActualWidth <= 360 && englishCard.ActualHeight <= 240);
                Snapshot(englishCard, "en-restricted.png");
                var englishMore = Children<Button>(englishCard).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                englishMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
                Snapshot(englishCard, "en-card.png"); Snapshot(englishMore.ContextMenu, "en-more.png");
                var englishSearch = (TextBox)englishMore.ContextMenu.Items.OfType<MenuItem>().First().Header;
                englishSearch.Text = "no-such-fixture-action"; await Task.Delay(30); Snapshot(englishMore.ContextMenu, "en-search-empty.png");
                Check("restricted More menu is visible and bounded", englishMore.ContextMenu.IsOpen && englishMore.ContextMenu.ActualHeight <= SystemParameters.WorkArea.Height);
                englishCard.Close(); UiText.SetLanguage("zh-Hant");

                var aiDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snapbar-ocr-ai-" + Guid.NewGuid().ToString("N"));
                var preferences = new AiPreferences(aiDirectory); var aiSettings = preferences.Settings;
                var profile = aiSettings.Profiles.Single(p => p.Id == "compatible") with { Model = "fixture-model" };
                preferences.TrySave(aiSettings with { SelectedProfileId = profile.Id, Profiles = aiSettings.Profiles.Select(p => p.Id == profile.Id ? profile : p).ToArray() }, out _);
                var aiClient = new FixtureAi();
                var aiCard = new OcrResultWindow("", null, new OcrActionDependencies(() => new ActionSettings(), () => "", _ => true, () => { }, preferences, AiClient: aiClient));
                aiCard.SetResult(new OcrResult("ok", "source tail")); aiCard.Show();
                var aiEditor = Children<TextBox>(aiCard).Single(); aiEditor.Select(0, 6);
                var aiMore = Children<Button>(aiCard).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                void OpenAi() { aiMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); aiMore.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_ai").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
                OpenAi(); var aiPanel = (AiPanelWindow)Field(aiCard, "actionAi")!;
                await aiPanel.SubmitAsync("fixture instruction");
                Check("local AI receives selected source and instruction", aiClient.Source == "source" && aiClient.Instruction == "fixture instruction");
                await aiPanel.ApplyAsync(); Check("local AI applies to selected OCR range", aiEditor.Text == "AI replacement tail");
                aiEditor.Text = "source tail"; aiEditor.Select(0, 6); OpenAi(); aiPanel = (AiPanelWindow)Field(aiCard, "actionAi")!;
                await aiPanel.SubmitAsync("fixture instruction"); aiEditor.Select(1, 3); await aiPanel.ApplyAsync();
                Check("local AI rejects changed selection", aiEditor.Text == "source tail"); aiPanel.Close();
                aiEditor.Text = "source tail"; aiEditor.Select(0, 6); OpenAi(); aiPanel = (AiPanelWindow)Field(aiCard, "actionAi")!;
                await aiPanel.SubmitAsync("fixture instruction"); aiEditor.Text = "edited source"; await aiPanel.ApplyAsync();
                Check("local AI rejects changed text", aiEditor.Text == "edited source"); aiPanel.Close();
                aiEditor.Text = "source tail"; aiEditor.Select(0, 6); OpenAi(); aiPanel = (AiPanelWindow)Field(aiCard, "actionAi")!;
                await aiPanel.SubmitAsync("fixture instruction"); aiCard.Close(); await aiPanel.ApplyAsync();
                var newAiCard = new OcrResultWindow("", null, new OcrActionDependencies(() => new ActionSettings(), () => "", _ => true, () => { }, preferences, AiClient: aiClient));
                newAiCard.SetResult(new OcrResult("ok", "new generation")); newAiCard.Show();
                Check("closed AI target cannot modify new generation", Children<TextBox>(newAiCard).Single().Text == "new generation"); newAiCard.Close();
                var reselectCard = new OcrResultWindow("", null, new OcrActionDependencies(() => new ActionSettings(), () => "", _ => true, () => { }, preferences, AiClient: aiClient));
                reselectCard.SetResult(new OcrResult("ok", "old generation")); reselectCard.Show();
                var reselectMore = Children<Button>(reselectCard).Single(b => AutomationProperties.GetAutomationId(b) == "OcrMore");
                reselectMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                reselectMore.ContextMenu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "OcrAction_ai").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var reselectPanel = (AiPanelWindow)Field(reselectCard, "actionAi")!; await reselectPanel.SubmitAsync("fixture instruction");
                OcrResultWindow? reselected = null;
                reselectCard.Reselect += () => { reselectCard.Close(); reselected = new OcrResultWindow("", null); reselected.SetResult(new OcrResult("ok", "reselected generation")); reselected.Show(); };
                Children<Button>(reselectCard).Single(b => AutomationProperties.GetAutomationId(b) == "OcrReselect").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await reselectPanel.ApplyAsync();
                Check("reselect closes old AI target and preserves new generation", reselected != null && Children<TextBox>(reselected).Single().Text == "reselected generation"); reselected?.Close();

                foreach (var output in new[] { ActionOutput.Window, ActionOutput.Markdown, ActionOutput.Hud, ActionOutput.ReplaceSelection })
                {
                    var action = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "Fixture", CustomActionKind.PowerShell,
                        "terminal", "fixture", output);
                    var settings = ActionCatalog.Normalize(new ActionSettings { CustomActions = [action] });
                    var calls = 0;
                    var dependencies = new OcrActionDependencies(() => settings, () => "https://example.org/?q={query}",
                        value => { settings = value; return true; }, () => { }, null,
                        RunCustom: (_, _, _) => { calls++; return Task.FromResult(new ScriptExecutionResult("ok", "fixture output", "fixture warning", 0)); });
                    var card = new OcrResultWindow("https://example.org/?q={query}", null, dependencies);
                    card.SetResult(new OcrResult("ok", "fixture input")); card.Show();
                    var editor = Children<TextBox>(card).Single();
                    var more = Children<Button>(card).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                    if (output == ActionOutput.Window)
                    {
                        Check("More shows its icon with accessible label", ((StackPanel)more.Content).Children.OfType<System.Windows.Controls.Viewbox>().Any()
                            && AutomationProperties.GetName(more) == UiText.Get("Ocr_More"));
                    }
                    more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var invalidLink = more.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_link");
                    if (output == ActionOutput.Window)
                        Check("disabled action exposes its reason", !invalidLink.IsEnabled && ToolTipService.GetShowOnDisabled(invalidLink)
                            && AutomationProperties.GetHelpText(invalidLink) == (string)invalidLink.ToolTip);
                    more.ContextMenu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_" + action.Id)
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    await Task.Delay(50);
                    var result = (ActionResultWindow?)Field(card, "actionResult");
                    var diagnostic = result == null ? null : Children<ActionDiagnosticView>(result).SingleOrDefault();
                    Check(output + " presents successful stderr", calls == 1 && result?.IsVisible == true && diagnostic != null
                        && Children<TextBox>(diagnostic).Single().Text.Contains("fixture warning", StringComparison.Ordinal));
                    if (output == ActionOutput.ReplaceSelection)
                    {
                        Check("replacement is committed once and diagnostic has no apply", editor.Text == "fixture output"
                            && Field(result!, "applyButton") == null);
                    }
                    else
                    {
                        Check(output + " preserves input before explicit apply", editor.Text == "fixture input");
                        Check(output + " describes local application", ((TextBlock)Field(result!, "hint")!).Text == UiText.Get("Ocr_ApplyHint"));
                        editor.Text = "changed after result";
                        Check(output + " describes stale local target", ((TextBlock)Field(result!, "hint")!).Text == UiText.Get("Ocr_ApplyStaleHint"));
                        var apply = (Button?)Field(result!, "applyButton");
                        var disabled = apply?.IsEnabled == false;
                        apply?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(output + " invalidates stale apply", disabled && editor.Text == "changed after result");
                    }
                    if (output == ActionOutput.Hud)
                    {
                        await Task.Delay(2700);
                        Check("HUD warning remains visible beyond ordinary HUD timeout", result?.IsVisible == true);
                    }
                    result?.Close(); card.Close();
                }
                var nativeHint = new ActionResultWindow("fixture", "output", false, true);
                Check("native result retains original apply hint", ((TextBlock)Field(nativeHint, "hint")!).Text == UiText.Get("ActionResultWindow_004")); nativeHint.Close();

                var fixtureStart = new ProcessStartInfo(TestPaths.AppExecutable) { UseShellExecute = false };
                fixtureStart.ArgumentList.Add("--fixture"); fixtureStart.ArgumentList.Add("--web-search");
                using var fixture = Process.Start(fixtureStart)!;
                try
                {
                    Console.WriteLine("WAITING: activate the dedicated SnapBar custom fixture (up to 60 seconds)");
                    var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (Native.GetIdentity(Native.GetForegroundWindow())?.ProcessId != fixture.Id)
                    { if (fixture.HasExited || DateTime.UtcNow > deadline) throw new InvalidOperationException("Fixture must be foreground"); await Task.Delay(25); }
                    using var owner = new HwndSource(new HwndSourceParameters("SnapBar shared action fixture") { Width = 1, Height = 1, WindowStyle = 0 });
                    var sharedUrl = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "Shared URL", CustomActionKind.Url, "globe", "https://example.org/?q=***");
                    var shared = ActionCatalog.Normalize(new ActionSettings { CustomActions = [sharedUrl] }); var destinations = new List<Uri>();
                    using var controller = new SelectionController(owner, TestPaths.ProbeExecutable, destinations.Add); controller.ConfigureActions(shared);
                    await controller.CaptureAsync(false, 0, 0); await controller.ExecuteCustomAsync(sharedUrl.Id);
                    Check("shared URL runs through native selection", destinations.Count == 1 && Uri.UnescapeDataString(destinations[0].Query[3..]) == FixtureWindow.SearchFixtureText);
                    var sharedCard = new OcrResultWindow("", null, new OcrActionDependencies(() => shared, () => "", value => { shared = value; return true; }, () => { }, null, LaunchWeb: destinations.Add));
                    sharedCard.SetResult(new OcrResult("ok", "OCR fixture")); sharedCard.Show();
                    var sharedMore = Children<Button>(sharedCard).Single(b => AutomationProperties.GetAutomationId(b) == "OcrMore");
                    MenuItem SharedRow() { sharedMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return sharedMore.ContextMenu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "OcrAction_" + sharedUrl.Id); }
                    SharedRow().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
                    Check("same shared URL runs through OCR", destinations.Count == 2 && Uri.UnescapeDataString(destinations[1].Query[3..]) == "OCR fixture");
                    shared = shared with { CustomActions = [sharedUrl with { Name = "Shared renamed" }] }; controller.ConfigureActions(shared);
                    Check("shared rename appears in OCR", (string)SharedRow().Header == "Shared renamed");
                    shared = shared with { Layout = shared.Layout.Where(e => e.Id != sharedUrl.Id).Append(new ActionLayoutEntry(sharedUrl.Id, ActionPlacement.Hidden)).ToArray() }; controller.ConfigureActions(shared);
                    sharedMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check("shared hide removes OCR menu row", !sharedMore.ContextMenu.Items.OfType<MenuItem>().Any(i => AutomationProperties.GetAutomationId(i) == "OcrAction_" + sharedUrl.Id));
                    shared = shared with { CustomActions = [], Layout = shared.Layout.Where(e => e.Id != sharedUrl.Id).ToArray() }; controller.ConfigureActions(shared);
                    sharedMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check("shared removal removes OCR menu row", !sharedMore.ContextMenu.Items.OfType<MenuItem>().Any(i => AutomationProperties.GetAutomationId(i) == "OcrAction_" + sharedUrl.Id)); sharedCard.Close();
                }
                finally { if (!fixture.HasExited) fixture.CloseMainWindow(); }
            }
            catch (Exception error) { Console.WriteLine("FAIL exception " + error); failed++; }
            finally { foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close(); app.Shutdown(); }
        };
        app.Run(); Console.WriteLine($"{passed} passed, {failed} failed"); return failed == 0 ? 0 : 1;
    }
}
