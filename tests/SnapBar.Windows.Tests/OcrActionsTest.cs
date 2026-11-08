using System.Reflection;
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
    private sealed class FixtureAi : IAiTextClient
    {
        public string? Source; public string? Instruction;
        public Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string key, string source, string instruction, CancellationToken token)
        { Source = source; Instruction = instruction; return Task.FromResult(new AiTextResult(true, "AI replacement")); }
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
                var englishCard = new OcrResultWindow("", null, new OcrActionDependencies(() => noInputSettings, () => "", _ => true, () => { }, null));
                englishCard.SetResult(new OcrResult("ok", "fixture text")); englishCard.Show();
                var englishMore = Children<Button>(englishCard).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                englishMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
                Snapshot(englishCard, "en-card.png"); Snapshot(englishMore.ContextMenu, "en-more.png");
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
                Check("local AI rejects changed text", aiEditor.Text == "edited source"); aiPanel.Close(); aiCard.Close();

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
                        editor.Text = "changed after result";
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
            }
            catch (Exception error) { Console.WriteLine("FAIL exception " + error); failed++; }
            finally { foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close(); app.Shutdown(); }
        };
        app.Run(); Console.WriteLine($"{passed} passed, {failed} failed"); return failed == 0 ? 0 : 1;
    }
}
