using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;
using SnapBar.App;
using SnapBar.Core;

internal static class InterfaceLanguageTest
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        var visited = new HashSet<DependencyObject>();
        IEnumerable<T> Visit(DependencyObject node)
        {
            if (!visited.Add(node)) yield break;
            if (node is T match) yield return match;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                foreach (var item in Visit(child)) yield return item;
            if (node is Visual)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    foreach (var item in Visit(VisualTreeHelper.GetChild(node, i))) yield return item;
        }
        return Visit(root);
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Children<T>(root).Single(item => AutomationProperties.GetAutomationId(item) == id);
    private static object? Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static bool HasChinese(string value) => value.Any(c => c >= 0x4e00 && c <= 0x9fff);

    internal static int Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Language.Desktop." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int passed = 0, failed = 0;
        void Check(string name, bool condition)
        {
            Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
            if (condition) passed++; else failed++;
        }
        MainWindow? main = null;
        ActionEditorWindow? oldAction = null, newAction = null;
        AiPromptEditorWindow? oldPrompt = null, failedPrompt = null;
        ActionResultWindow? oldResult = null;
        AiPanelWindow? aiPanel = null;
        PopupWindow? popup = null;
        app.Startup += async (_, _) =>
        {
            try
            {
                var resourceRoot = Path.Combine(TestPaths.Repository, "src/SnapBar.App/Resources");
                Dictionary<string, string> Read(string file) => XDocument.Load(Path.Combine(resourceRoot, file)).Root!.Elements("data")
                    .ToDictionary(node => (string)node.Attribute("name")!, node => node.Element("value")!.Value, StringComparer.Ordinal);
                var english = Read("Strings.resx"); var chinese = Read("Strings.zh-Hant.resx");
                Check("bilingual resource sets have exactly the same keys", english.Keys.ToHashSet().SetEquals(chinese.Keys));
                string Signature(string text) => string.Join(",", Regex.Matches(text, @"(?<!\{)\{(\d+)(?:,(-?\d+))?(?::([^}]+))?\}")
                    .Select(match => match.Groups[1].Value + ":" + match.Groups[2].Value + ":" + match.Groups[3].Value).Order());
                Check("bilingual positional format signatures match", english.All(entry => chinese.TryGetValue(entry.Key, out var value) && Signature(value) == Signature(entry.Value)));
                Check("every English product resource is translated", english.Values.All(value => value.Length > 0 && !HasChinese(value)));
                Check("satellite loads explicitly without changing system culture", UiText.Get(CultureInfo.GetCultureInfo("en"), "Action_copy") == "Copy"
                    && UiText.Get(CultureInfo.GetCultureInfo("zh-Hant"), "Action_copy") == "複製");
                Check("OS UI language remains independent of process UI culture", ReadOsWithoutCultureChange());

                var os = "zh-TW"; int queries = 0;
                main = new MainWindow(new SettingsStore(path), () => { }, () => false, systemUiLanguage: () => { queries++; return os; });
                main.Show(); await Task.Delay(80);
                Check("new settings follow injected Windows UI language", main.InterfaceLanguagePreference == "system" && main.Title == "SnapBar · 設定");
                var controller = Field(main, "controller");
                var tray = (TrayIcon?)Field(main, "tray");
                var selector = Find<ComboBox>(main, "InterfaceLanguageSelector");
                Check("language selector choices follow Traditional Chinese interface", selector.Items.Cast<string>().SequenceEqual(new[] { "跟隨系統", "繁體中文", "英文" }));
                var ocrPriority = Find<ComboBox>(main, "OcrLanguagePriority"); ocrPriority.SelectedIndex = 1;
                var shortcut = Find<ComboBox>(main, "ShortcutTarget"); shortcut.SelectedIndex = 1;
                main.GeneralPage.BeginRecording(); main.GeneralPage.Record(new(3, 0x4A));
                var recorded = AutomationProperties.GetName(Find<StackPanel>(main, "GeneralShortcutKeys"));
                var preferencesBefore = new SettingsStore(path).Load().Settings;
                var search = main.SearchPanel; Find<ComboBox>(search, "SearchProvider").SelectedIndex = 2;
                Find<TextBox>(search, "SearchUrlTemplate").Text = "https://example.org/search?q={query}&draft=保留";
                var ai = main.AiServicesPanel;
                Find<ComboBox>(ai, "AiModel").Text = "draft-model-保留";
                var draftsBefore = main.AiPreferences.Settings;

                // Construct opening-language fixtures before switching; these do not touch selection or clipboard.
                oldAction = new ActionEditorWindow(ActionCatalog.GetItems(new()).Single(item => item.Id == "custom-62bf87b962054dc7bacd6588e37c2843").Custom);
                var defaultPrompt = new AiSettings().Prompts[0];
                string? SaveSharedPrompt(AiPrompt value) => main.AiPreferences.TrySavePrompt(value, false, out var error) ? null : error;
                oldPrompt = new AiPromptEditorWindow(defaultPrompt, SaveSharedPrompt);
                failedPrompt = new AiPromptEditorWindow(defaultPrompt, SaveSharedPrompt);
                oldResult = new ActionResultWindow("Fixture result", "Keep user text: 原文", false, false);
                var oldTitle = oldAction.Title; var oldPromptTitle = oldPrompt.Title; var oldResultTitle = oldResult.Title;
                var scope = UiText.Capture();
                selector.SelectedIndex = 2;
                Check("language is saved before settings refresh", new SettingsStore(path).Load().Settings.InterfaceLanguage == "en" && main.Title == "SnapBar · Settings");
                Check("language selector translates all choices in place without changing the selected preference",
                    selector.Items.Cast<string>().SequenceEqual(new[] { "System", "Traditional Chinese", "English" })
                    && selector.SelectedIndex == 2 && new SettingsStore(path).Load().Settings.InterfaceLanguage == "en"
                    && AutomationProperties.GetName(selector) == "Language");
                Check("controller identity and selection policies are preserved", controller != null && ReferenceEquals(controller, Field(main, "controller")));
                Check("OCR priority and hotkey draft survive refresh without callback writes", ocrPriority.SelectedIndex == 1 && shortcut.SelectedIndex == 1
                    && main.GeneralPage.IsRecording && AutomationProperties.GetName(Find<StackPanel>(main, "GeneralShortcutKeys")) == recorded
                    && new SettingsStore(path).Load().Settings.OcrPreferEnglish);
                Check("search and AI drafts survive in-place language refresh", search.IsDirty && search.DraftTemplate.EndsWith("draft=保留", StringComparison.Ordinal)
                    && ai.IsDirty && Find<ComboBox>(ai, "AiModel").Text == "draft-model-保留" && ReferenceEquals(draftsBefore, main.AiPreferences.Settings));
                Check("existing independent windows retain opening language", oldAction.Title == oldTitle && oldPrompt.Title == oldPromptTitle && oldResult.Title == oldResultTitle
                    && scope.Get("Action_copy") == "複製");
                oldResult.InvalidateTarget();
                var oldPromptName = Find<TextBox>(oldPrompt, "AiPromptName"); var originalPromptName = oldPromptName.Text; oldPromptName.Text = "";
                Check("old result and editor callbacks keep opening-language guidance", ((TextBlock)Field(oldResult, "hint")!).Text.Contains("原選取", StringComparison.Ordinal)
                    && !oldPrompt.TrySave() && Find<TextBlock>(oldPrompt, "AiPromptError").Text == "請填寫指令名稱。");
                oldPromptName.Text = originalPromptName;
                var previous = new SettingsStore(path).Load().Settings;
                Check("language save preserves unrelated preferences", previous.Hotkey == preferencesBefore.Hotkey && previous.OcrPreferEnglish == preferencesBefore.OcrPreferEnglish
                    && previous.ClipboardCompatibilityEnabled == preferencesBefore.ClipboardCompatibilityEnabled && previous.SearchUrlTemplate == preferencesBefore.SearchUrlTemplate);
                Check("tray text refreshes immediately", tray != null && TrayLabels(tray).Contains("Open settings") && TrayLabels(tray).Contains("Quit SnapBar"));
                var beforeFailure = File.ReadAllBytes(path);
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) selector.SelectedIndex = 1;
                Check("failed language save restores preference and previous-language error", selector.SelectedIndex == 2 && main.InterfaceLanguagePreference == "en"
                    && main.Title == "SnapBar · Settings" && beforeFailure.SequenceEqual(File.ReadAllBytes(path))
                    && Find<TextBlock>(main, "InterfaceLanguageStatus").Text.StartsWith("Could not save", StringComparison.Ordinal));
                main.NavigateTo(SettingsCategory.General); main.UpdateLayout(); UiAppearanceTest.Snapshot(main, "language-save-failure-en.png");
                selector.SelectedIndex = 0; selector.SelectedIndex = 2;

                main.GeneralPage.CancelRecording();
                // Restore fixture-only drafts so the representative English audit contains no intentional user CJK.
                search.Discard(); ai.Discard();
                Check("saving an old-language editor leaves shared settings in the current language", oldPrompt.TrySave()
                    && Find<ComboBox>(ai, "AiProvider").Items.Cast<string>().Contains("Custom compatible service")
                    && Children<TextBlock>(main.AiCommandsPanel).All(text => !HasChinese(text.Text)));
                using (var lockedAi = new FileStream(Path.Combine(directory, "ai.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                    Check("shared-save failure is translated only inside the old editor", !failedPrompt.TrySave()
                        && Find<TextBlock>(failedPrompt, "AiPromptError").Text.StartsWith("AI 設定無法保存", StringComparison.Ordinal)
                        && Find<ComboBox>(ai, "AiProvider").Items.Cast<string>().Contains("Custom compatible service"));
                failedPrompt.Close(); failedPrompt = null;
                main.NavigateTo(SettingsCategory.AiServices); main.UpdateLayout();
                var advanced = Find<Expander>(ai, "AiAdvancedSettings"); advanced.ApplyTemplate();
                var headerToggle = (ToggleButton)advanced.Template.FindName("HeaderToggle", advanced);
                Check("English expander toggle automation name follows its localized header", AutomationProperties.GetName(headerToggle) == "Advanced settings");
                selector.SelectedIndex = 1; main.UpdateLayout();
                Check("expander automation name refreshes with the existing settings page", AutomationProperties.GetName(headerToggle) == "進階設定");
                selector.SelectedIndex = 2; main.UpdateLayout();
                foreach (var category in Enum.GetValues<SettingsCategory>())
                {
                    main.NavigateTo(category); main.UpdateLayout();
                    var unexpected = Children<TextBlock>(main).Where(text => text.IsVisible && HasChinese(text.Text)).Select(text => text.Text).ToArray();
                    Check("English settings labels: " + category, unexpected.Length == 0);
                    if (unexpected.Length > 0) Console.WriteLine(string.Join(" | ", unexpected));
                }
                main.NavigateTo(SettingsCategory.General); main.UpdateLayout(); UiAppearanceTest.Snapshot(main, "language-settings-en.png");
                main.ShowDictionarySettings(); main.UpdateLayout();
                Check("English dictionary management labels", Children<TextBlock>(main).Where(text => text.IsVisible).All(text => !HasChinese(text.Text)));
                UiAppearanceTest.Snapshot(main, "language-dictionary-en.png");
                Func<Task<string>> dictionaryOperation = () => Task.FromResult(UiText.Get("MainWindow_Dictionary_019"));
                await (Task)typeof(MainWindow).GetMethod("UpdateDictionaryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [dictionaryOperation])!;
                var completedDictionaryStatus = (TextBlock)Field(main, "dictionaryStatus")!;
                Check("completed dictionary operation displays current-language status", completedDictionaryStatus.Text.Contains("Dictionary removed.", StringComparison.Ordinal));
                selector.SelectedIndex = 1;
                Check("completed dictionary status keeps translation metadata through language refresh", completedDictionaryStatus.Text.Contains("已移除詞典。", StringComparison.Ordinal)
                    && !completedDictionaryStatus.Text.Contains("Dictionary removed.", StringComparison.Ordinal));
                selector.SelectedIndex = 2;
                Check("Core validation codes map to complete English guidance", !HasChinese(ValidationMessages.Describe("action-name-invalid"))
                    && ValidationMessages.Describe("ai-endpoint-invalid").Contains("HTTPS", StringComparison.Ordinal)
                    && ValidationMessages.Describe(ProductError.InvalidData("dictionary-index-invalid")).Contains("dictionary", StringComparison.OrdinalIgnoreCase));

                var defaults = ActionCatalog.GetItems(new()); var gmail = defaults.Single(item => item.Id == "custom-62bf87b962054dc7bacd6588e37c2843").Custom!;
                var prompt = new AiSettings().Prompts[0];
                Check("bundled defaults use ID and original-name equality", UiNames.Action(gmail) == "Search Gmail" && UiNames.Action(gmail with { Name = "我的 Gmail" }) == "我的 Gmail"
                    && UiNames.Prompt(prompt) == "Correct grammar and spelling" && UiNames.Prompt(prompt with { Name = "我的命令" }) == "我的命令");
                var snapshot = new SelectionSnapshot("language-fixture", new(0, 0, 0), "Disposable source text", new(100, 100, 100, 20), true, 1, 22);
                Check("English bundled action and AI names are searchable", UiNames.More(new(), snapshot, "public").Any(item => UiNames.Action(item) == "Get public IP")
                    && UiNames.SearchPrompts(new(), "grammar", null).Any(item => item.Id == "grammar"));
                newAction = new ActionEditorWindow(gmail); newAction.Show(); await Task.Delay(20);
                Check("new editor uses current language including previously initialized output list", newAction.Title == "Edit custom action · SnapBar"
                    && Children<ComboBox>(newAction).Any(box => box.Items.Cast<object>().Any(item => Equals(item, "Show result window"))));
                UiAppearanceTest.Snapshot(newAction, "language-editor-en.png");
                newAction.Close(); newAction = null;
                var unmodifiedSave = new ActionEditorWindow(gmail);
                unmodifiedSave.Loaded += (_, _) => Children<Button>(unmodifiedSave).Single(button => AutomationProperties.GetName(button) == "Save action").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("saving a displayed bundled name preserves stored original", unmodifiedSave.ShowDialog() == true && unmodifiedSave.SavedAction?.Name == gmail.Name);
                var renamedSave = new ActionEditorWindow(gmail);
                renamedSave.Loaded += (_, _) =>
                {
                    Children<TextBox>(renamedSave).Single(box => AutomationProperties.GetName(box) == "Action name").Text = "My inbox";
                    Children<Button>(renamedSave).Single(button => AutomationProperties.GetName(button) == "Save action").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                Check("editing a bundled display name saves the actual user rename", renamedSave.ShowDialog() == true && renamedSave.SavedAction?.Name == "My inbox");
                AiPrompt? savedPrompt = null;
                var promptSave = new AiPromptEditorWindow(prompt, value => { savedPrompt = value; return null; });
                promptSave.Show(); Click(promptSave, "AiPromptSave");
                Check("saving bundled AI name and category preserves original content", savedPrompt?.Name == prompt.Name && savedPrompt.Category == prompt.Category && savedPrompt.Instruction == prompt.Instruction);
                typeof(MainWindow).GetMethod("ShowOcrResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [new SnapBar.Windows.Ocr.OcrResult("ok", "OCR user 原文")]);
                var ocrWindow = (Window)Field(main, "ocrResultWindow")!;
                Check("English OCR result preserves recognized content", ocrWindow.Title == "Capture text · SnapBar"
                    && Children<TextBox>(ocrWindow).Single().Text == "OCR user 原文"
                    && Children<TextBlock>(ocrWindow).Where(text => text.IsVisible).All(text => !HasChinese(text.Text)));
                UiAppearanceTest.Snapshot(ocrWindow, "language-ocr-en.png");
                var fixturePreferences = new AiPreferences(Path.Combine(directory, "async-ai"));
                var compatible = fixturePreferences.Settings.Profiles.Single(profile => profile.Id == "compatible") with { Model = "fixture-model" };
                fixturePreferences.TrySave(fixturePreferences.Settings with { SelectedProfileId = compatible.Id,
                    Profiles = fixturePreferences.Settings.Profiles.Select(profile => profile.Id == compatible.Id ? compatible : profile).ToArray() }, out _);
                var client = new DelayedClient();
                aiPanel = new AiPanelWindow(snapshot, fixturePreferences, client: client); aiPanel.Show();
                Check("English AI panel translates default commands categories and provider", !Children<TextBlock>(aiPanel).Where(text => text.IsVisible).Any(text => HasChinese(text.Text)));
                Find<Button>(aiPanel, "AiPrompt_grammar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("AI fixture started a pending request without network access", aiPanel.Session.Busy && client.Calls == 1);
                var storedSampleName = UiNames.KeepOriginalName(UiText.Get("Action_copy"), "", null);
                var customName = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), storedSampleName, CustomActionKind.PowerShell, "spark", "fixture");
                var customLabel = new TextBlock { Text = UiNames.Action(customName) };
                var userValue = new string("Copy".AsSpan()); var userLabel = new TextBlock { Text = userValue }; var productLabel = new TextBlock { Text = UiText.Get("Action_copy") };
                var identity = new StackPanel(); identity.Children.Add(userLabel); identity.Children.Add(productLabel); identity.Children.Add(customLabel);
                UiText.SetLanguage("zh-Hant"); UiText.Refresh(identity);
                Check("user text and adopted sample names are never translated", userLabel.Text == "Copy" && productLabel.Text == "複製" && customLabel.Text == "Copy");
                Children<TextBox>(ocrWindow).Single().Text = "";
                var ocrMore = Children<Button>(ocrWindow).Single(button => AutomationProperties.GetAutomationId(button) == "OcrMore");
                ocrMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var ocrMenu = ocrMore.ContextMenu;
                Check("empty OCR disables search", !((OcrActionMenu)ocrMenu).Rows.Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_search").IsEnabled);
                ocrMenu.IsOpen = false;
                Children<TextBox>(ocrWindow).Single().Text = new string('x', 20000);
                ocrMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((OcrActionMenu)ocrMore.ContextMenu).Rows.Single(item => AutomationProperties.GetAutomationId(item) == "OcrAction_search").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check("OCR result callbacks retain opening-language guidance", Children<TextBlock>(ocrWindow).Any(text => text.Text == "Action unavailable for this text."));
                ocrWindow.Close();
                client.Completion.SetResult(new AiTextResult(true, "Generated 原文 content remains unchanged."));
                for (int attempt = 0; aiPanel.Session.Busy && attempt < 50; attempt++) await Task.Delay(10);
                Check("AI asynchronous callbacks retain opening language and result text", !aiPanel.Session.Busy && aiPanel.Title == "SnapBar · AI rewrite"
                    && aiPanel.Session.Current?.Text == "Generated 原文 content remains unchanged."
                    && Find<TextBlock>(aiPanel, "AiExecutionLabel").Text.Contains("Correct grammar", StringComparison.Ordinal));
                aiPanel.UpdateLayout(); UiAppearanceTest.Snapshot(aiPanel, "language-ai-result-en.png");
                aiPanel.Close(); aiPanel = null;
                Check("existing editor callbacks keep their opening language after global switch", scope.Run(() => ScriptDiagnostic.Create(gmail, new("script-timeout"))).Summary.Contains("逾時", StringComparison.Ordinal));
                selector.SelectedIndex = 0;
                Check("English to System re-queries independent Windows source", queries >= 3 && UiText.Culture.Name == "zh-Hant" && main.InterfaceLanguagePreference == "system");
                main.NavigateTo(SettingsCategory.General); main.UpdateLayout(); UiAppearanceTest.Snapshot(main, "language-settings-zh-Hant.png");
                oldAction.Show(); await Task.Delay(20); UiAppearanceTest.Snapshot(oldAction, "language-editor-zh-Hant.png"); oldAction.Close(); oldAction = null;
                UiText.SetLanguage("en");
                popup = new PopupWindow(); popup.Present(snapshot, 140, 140); popup.UpdateLayout();
                Check("English toolbar names", Children<Button>(popup).Any(button => AutomationProperties.GetName(button) == "Copy"));
                UiAppearanceTest.Snapshot(popup, "language-toolbar-en.png");
                Click(popup, "MoreActions"); popup.UpdateLayout();
                Check("English More menu contains localized bundled names", Children<TextBlock>(popup).Any(text => text.Text == "Get public IP"));
                UiAppearanceTest.Snapshot(popup, "language-more-en.png");
                popup.Dismiss(); UiText.SetLanguage("zh-Hant"); popup.Present(snapshot, 140, 140); popup.UpdateLayout();
                Check("reused toolbar refreshes on its next presentation", Children<Button>(popup).Any(button => AutomationProperties.GetName(button) == "複製"));
                UiAppearanceTest.Snapshot(popup, "language-toolbar-zh-Hant.png"); popup.Close(); popup = null;
                selector.SelectedIndex = 2;
                main.PrepareSessionEnd(); main.Close(); main = null;
                main = new MainWindow(new SettingsStore(path), systemUiLanguage: () => "zh-TW");
                Check("explicit language survives a new process settings load", main.Title == "SnapBar · Settings" && main.InterfaceLanguagePreference == "en");
                Console.WriteLine("DPI " + VisualTreeHelper.GetDpi(main).DpiScaleX.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error); failed++; }
            finally
            {
                popup?.Close(); aiPanel?.Close(); oldAction?.Close(); newAction?.Close(); oldPrompt?.Close(); failedPrompt?.Close(); oldResult?.Close();
                main?.PrepareSessionEnd(); main?.Close();
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) File.Delete(file);
                foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(value => value.Length)) Directory.Delete(child);
                Directory.Delete(directory);
                Console.WriteLine($"{passed} passed, {failed} failed"); app.Shutdown();
            }
        };
        app.Run(); return failed == 0 ? 0 : 1;
    }

    private sealed class DelayedClient : IAiTextClient
    {
        internal int Calls;
        internal TaskCompletionSource<AiTextResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string key, string source, string instruction, CancellationToken cancellationToken)
        { Calls++; return Completion.Task.WaitAsync(cancellationToken); }
    }

    private static string[] TrayLabels(TrayIcon tray) => ((System.Windows.Forms.ContextMenuStrip)Field(tray, "menu")!).Items.Cast<System.Windows.Forms.ToolStripItem>().Select(item => item.Text ?? "").ToArray();
    private static bool ReadOsWithoutCultureChange()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            var before = SnapBar.Windows.SystemUiLanguage.Read();
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(before.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-Hant");
            return SnapBar.Windows.SystemUiLanguage.Read() == before;
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }
}
