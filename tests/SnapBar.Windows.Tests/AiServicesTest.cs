using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;

internal static class AiServicesTest
{
    private sealed class DeferredClient : IAiTextClient, IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
        internal readonly List<TaskCompletionSource<AiTextResult>> Requests = [];
        internal readonly List<(AiProviderProfile Profile, string Key, string Source, string Instruction)> Calls = [];
        public Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string key,
            string source, string instruction, CancellationToken token)
        {
            Calls.Add((profile, key, source, instruction));
            var completion = new TaskCompletionSource<AiTextResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(completion);
            return completion.Task;
        }
    }

    private static async Task RunControllerChecks(Action<bool, string> check)
    {
        var profile = AiProviderCatalog.CreateProfiles()[0];

        foreach (var action in new[] { "reset", "cancel", "dispose" })
        {
            var reentrantClient = new DeferredClient();
            var reentrantController = new AiServiceTestController(reentrantClient);
            var handled = false;
            reentrantController.Changed += () =>
            {
                if (handled || reentrantController.Status != "testing") return;
                handled = true;
                switch (action)
                {
                    case "reset": reentrantController.Reset(); break;
                    case "cancel": reentrantController.Cancel(); break;
                    case "dispose": reentrantController.Dispose(); break;
                }
            };
            await reentrantController.StartAsync(profile, "reentrant-key");
            check(reentrantClient.Calls.Count == 0, $"synchronous {action} prevents service invocation");
            check(!reentrantController.Busy && reentrantController.Status == "untested",
                $"synchronous {action} leaves controller idle and untested");
            reentrantController.Dispose();
        }

        var client = new DeferredClient();
        var controller = new AiServiceTestController(client);
        var changed = 0;
        controller.Changed += () => changed++;

        var first = controller.StartAsync(profile, "fixture-key");
        check(controller.Busy && controller.Status == "testing" && client.Calls.Count == 1,
            "service test starts one fixed-text request");
        check(client.Calls[0] == (profile, "fixture-key", AiServiceTestController.Source, AiServiceTestController.Instruction),
            "service test never sends user selection or instruction text");
        var callsWhileBusy = client.Calls.Count;
        await controller.StartAsync(profile, "ignored-key");
        check(client.Calls.Count == callsWhileBusy, "service test ignores duplicate start while busy");

        controller.Reset();
        check(!controller.Busy && controller.Status == "untested", "reset cancels pending service test");
        var changedAfterReset = changed;
        var replacement = controller.StartAsync(profile, "replacement-key");
        await first;
        check(controller.Busy && controller.Status == "testing" && changed == changedAfterReset + 1,
            "old request unwinding cannot clear busy or notify the pending replacement");
        client.Requests[0].SetResult(new(true, "must not be retained"));
        await Task.Yield();
        check(controller.Busy && controller.Status == "testing", "late old completion cannot change pending replacement");
        client.Requests[1].SetResult(new(true, "transient success body"));
        await replacement;
        check(!controller.Busy && controller.Status == "success", "replacement request can finish successfully");

        var cancelled = controller.StartAsync(profile, "cancel-key");
        controller.Cancel();
        await cancelled;
        check(!controller.Busy && controller.Status == "untested", "cancel releases wait when client ignores cancellation");
        client.Requests[2].SetResult(new(true, "late cancelled response"));
        await Task.Yield();
        check(controller.Status == "untested", "cancelled late response cannot change status");

        var failed = controller.StartAsync(profile, "failure-key");
        client.Requests[3].SetResult(new(false, "private diagnostic body", "network"));
        await failed;
        check(controller.Status == "network", "service failure exposes status without response text");

        var beforeDispose = changed;
        var disposed = controller.StartAsync(profile, "dispose-key");
        controller.Dispose();
        await disposed;
        client.Requests[4].SetResult(new(true, "post-dispose response"));
        await Task.Yield();
        check(changed == beforeDispose + 1, "dispose cancels wait and late response raises no change");
        check(controller.Status == "untested", "dispose leaves no response status behind");
    }

    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(item, i))) yield return child;
        if (item is Expander expander && !expander.IsExpanded && expander.Content is DependencyObject content)
            foreach (var child in Children<T>(content)) yield return child;
    }
    private static T Find<T>(Window window, string id) where T : DependencyObject => Children<T>(window).Single(x => AutomationProperties.GetAutomationId(x) == id);
    public static async Task RunAsync(string directory, Action<bool, string> check, Action<Window, string> snapshot)
    {
        await RunControllerChecks(check);
        await RunDraftTestChecks(directory, check, snapshot);
        var preferences = new AiPreferences(Path.Combine(directory, "services"));
        var original = preferences.Settings;
        var legacy = original.Profiles[0] with { Model = "legacy-private-model" };
        check(preferences.TrySave(original with { Profiles = original.Profiles.Select(p => p.Id == legacy.Id ? legacy : p).ToArray() }, out _), "service fixture saves isolated legacy model");
        var links = new List<Uri>();
        var testClient = new DeferredClient();
        var panel = new AiSettingsPanel(preferences, links.Add, testClient);
        var window = new Window { Title = "AI services fixture", Width = 720, Height = 740, Content = panel };
        try
        {
            window.Show(); await Task.Delay(80); window.UpdateLayout();
            var advanced = Find<Expander>(window, "AiAdvancedSettings");
            var model = Find<ComboBox>(window, "AiModel");
            var endpoint = Find<TextBox>(window, "AiEndpoint");
            model.Text = "navigation-draft";
            window.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
            check(Find<ComboBox>(window, "AiProvider").IsKeyboardFocusWithin,
                "dirty service settings keyboard navigation starts at the provider rather than the footer; actual="
                + (System.Windows.Input.Keyboard.FocusedElement is DependencyObject focused
                    ? focused.GetType().Name + "/" + AutomationProperties.GetAutomationId(focused) : "none"));
            model.Text = "legacy-private-model";
            check(!advanced.IsExpanded, "named provider starts with advanced settings collapsed");
            check(model.IsEditable && model.Text == "legacy-private-model", "model editor preserves a value absent from recommendations");
            var editor = (TextBox)model.Template.FindName("PART_EditableTextBox", model);
            check(editor.IsVisible && editor.MaxLength == 128 && editor.Text == "legacy-private-model" && editor.Focus(),
                "editable model template visibly renders legacy text accepts focus and limits input to 128 characters");
            editor.Text = "keyboard-model";
            check(model.Text == "keyboard-model", "editing the actual model text box updates the draft");
            model.Text = "typed-model"; advanced.IsExpanded = true; advanced.IsExpanded = false;
            check(model.Text == "typed-model" && endpoint.Text == legacy.Endpoint, "collapsing advanced settings preserves field values");
            Find<Button>(window, "AiTestService").Focus();
            Find<Button>(window, "AiTestService").MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            check(Find<Button>(window, "DiscardAiSettings").IsKeyboardFocused,
                "service test tab navigation continues to the fixed draft action row");
            snapshot(window, "services-normal.png");
            check(testClient.Calls.Count == 0, "opening and editing settings never tests automatically");
            Find<Button>(window, "AiTestService").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(testClient.Calls.Count == 1 && testClient.Calls[0].Profile.Model == "typed-model", "explicit test sends unsaved model draft");
            testClient.Requests[0].SetResult(new(true, "private response"));
            await Task.Delay(40);
            check(Find<TextBlock>(window, "AiServiceTestStatus").Text == "✓ 連線成功" && panel.IsDirty, "successful test preserves dirty draft");
            var providers = Find<ComboBox>(window, "AiProvider");
            check(providers.Items.Count == 9 && links.Count == 0, "nine services load without opening a browser");
            var key = Find<PasswordBox>(window, "AiApiKey");
            void Click(string id) => Find<Button>(window, id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            for (var i = 0; i < original.Profiles.Length; i++)
            {
                providers.SelectedIndex = i; window.UpdateLayout();
                var profile = original.Profiles[i]; var definition = AiProviderCatalog.Find(profile.Id)!;
                check(advanced.IsExpanded == (profile.Id == "compatible"), profile.Id + " uses the expected advanced default");
                check(model.Text == (i == 0 ? "typed-model" : profile.Model), profile.Id + " does not auto-select a model");
                if (definition.Models.Length > 0)
                {
                    model.SelectedValue = definition.Models[0].Id;
                    check(model.Text == definition.Models[0].Id, profile.Id + " suggestion selects its exact model ID");
                }
                model.Text = "manual-" + i;
                endpoint.Text = "https://fixture.invalid/provider/" + i;
                key.Password = "disposable-key-" + i;
                check(Find<TextBlock>(window, "AiCustomEndpointNotice").Visibility == (profile.Id == "compatible" ? Visibility.Collapsed : Visibility.Visible), profile.Id + " marks a custom endpoint");
                if (definition.KeyUrl != null)
                {
                    var before = links.Count; Click("AiKeyLink"); Click("AiModelsLink");
                    check(links.Count == before + 2 && links[^2].AbsoluteUri == definition.KeyUrl && links[^1].AbsoluteUri == definition.ModelsUrl
                        && links.TakeLast(2).All(u => u.Scheme == "https"), profile.Id + " opens only its catalog HTTPS links");
                }
                else check(Find<Button>(window, "AiKeyLink").Visibility == Visibility.Collapsed && Find<Button>(window, "AiModelsLink").Visibility == Visibility.Collapsed,
                    "custom service hides absent official links");
            }
            var calls = links.Count;
            for (var i = 0; i < original.Profiles.Length; i++)
            {
                providers.SelectedIndex = i;
                check(model.Text == "manual-" + i && endpoint.Text == "https://fixture.invalid/provider/" + i && key.Password == "disposable-key-" + i,
                    original.Profiles[i].Id + " preserves independent model endpoint and key drafts");
            }
            check(links.Count == calls, "switching services never opens official links");
            providers.SelectedIndex = 0; endpoint.Text = legacy.Endpoint.ToUpperInvariant();
            check(Find<TextBlock>(window, "AiCustomEndpointNotice").Visibility == Visibility.Visible, "endpoint path case remains significant");
            endpoint.Text = "https://API.OPENAI.COM:443/v1/responses";
            check(Find<TextBlock>(window, "AiCustomEndpointNotice").Visibility == Visibility.Collapsed, "endpoint comparison normalizes host and default port");
            advanced.IsExpanded = true; window.Width = 540; window.Height = 420; window.UpdateLayout();
            var scroll = Children<ScrollViewer>(window).First(s => s.Content is StackPanel);
            scroll.ScrollToBottom(); window.UpdateLayout();
            check(Find<Button>(window, "SaveAiSettings").IsVisible && scroll.ScrollableHeight > 0, "minimum service window scrolls fields and keeps save footer visible");
            check(Find<Button>(window, "AiTestService").Focusable && Find<Button>(window, "AiTestService").Focus()
                && Find<Button>(window, "SaveAiSettings").Focusable && Find<Button>(window, "SaveAiSettings").Focus(),
                "minimum window test and save controls accept keyboard focus");
            snapshot(window, "services-small-advanced.png");
        }
        finally { panel.Detach(); window.Close(); }
        var failurePanel = new AiSettingsPanel(preferences, _ => throw new InvalidOperationException("private diagnostic"));
        var failureWindow = new Window { Width = 600, Height = 650, Content = failurePanel };
        try
        {
            failureWindow.Show(); await Task.Delay(40);
            Find<Button>(failureWindow, "AiKeyLink").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(Find<TextBlock>(failureWindow, "AiSettingsError").Text == "無法開啟官方頁面，請稍後再試。", "link failure uses fixed safe Chinese feedback");
        }
        finally { failurePanel.Detach(); failureWindow.Close(); }
    }

    private static async Task RunDraftTestChecks(string directory, Action<bool, string> check, Action<Window, string> snapshot)
    {
        var root = Path.Combine(directory, "service-testing");
        var preferences = new AiPreferences(root);
        var profile = preferences.Settings.Profiles[0];
        preferences.Secrets.Set(profile, "stored-fixture-key");
        Dictionary<string, byte[]> Files() => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, File.ReadAllBytes);
        var before = Files();
        var client = new DeferredClient();
        var panel = new AiSettingsPanel(preferences, _ => { }, client);
        var window = new Window { Width = 720, Height = 800, Content = panel };
        try
        {
            window.Show(); await Task.Delay(50); window.UpdateLayout();
            var model = Find<ComboBox>(window, "AiModel");
            var endpoint = Find<TextBox>(window, "AiEndpoint");
            var key = Find<PasswordBox>(window, "AiApiKey");
            var remove = Find<CheckBox>(window, "AiRemoveKey");
            var providers = Find<ComboBox>(window, "AiProvider");
            var status = Find<TextBlock>(window, "AiServiceTestStatus");
            void Click(string id) => Find<Button>(window, id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            void Test() => Click("AiTestService");
            var save = Find<Button>(window, "SaveAiSettings");
            var discard = Find<Button>(window, "DiscardAiSettings");
            var replace = Find<Button>(window, "AiReplaceKey");
            var keyStatus = Find<TextBlock>(window, "AiKeyStatus");
            var cancel = Find<Button>(window, "AiCancelServiceTest");
            check(!save.IsEnabled && !discard.IsEnabled && key.Visibility == Visibility.Collapsed
                && key.Password.Length == 0 && replace.IsVisible && keyStatus.Text == "✓ 已儲存金鑰",
                "clean stored-key settings show summary without prefilling the secret or enabling save");
            check(cancel.Visibility == Visibility.Collapsed, "idle service test hides cancellation");
            snapshot(window, "services-saved-key.png");
            Click("AiReplaceKey"); window.UpdateLayout();
            check(key.IsVisible && key.IsKeyboardFocused && !panel.IsDirty && !save.IsEnabled,
                "replace key reveals and focuses the editor without creating a dirty draft");
            key.Password = "replacement-draft";
            check(save.IsEnabled && discard.IsEnabled && keyStatus.Text == "新金鑰尚未儲存",
                "typing replacement enables draft actions and marks the key pending");
            providers.SelectedIndex = 1; providers.SelectedIndex = 0;
            check(key.Password == "replacement-draft" && key.IsVisible && keyStatus.Text == "新金鑰尚未儲存",
                "switching services restores the pending key and its editing presentation");
            panel.Discard(); window.UpdateLayout();
            check(!save.IsEnabled && !discard.IsEnabled && key.Visibility == Visibility.Collapsed,
                "discard restores the stored-key summary and disables clean actions");
            endpoint.Text = "https://fixture.invalid/new-scope"; window.UpdateLayout();
            check(key.IsVisible && replace.Visibility == Visibility.Collapsed && keyStatus.Text == "尚未設定金鑰",
                "a different endpoint never displays another endpoint's saved credential");
            panel.Discard();
            remove.IsChecked = true;
            check(keyStatus.Text == "金鑰將在儲存後移除" && save.IsEnabled && key.Visibility == Visibility.Collapsed,
                "pending removal is explicit and hides the editor until unchecked");
            remove.IsChecked = false;
            check(keyStatus.Text == "✓ 已儲存金鑰" && !save.IsEnabled,
                "unchecking removal restores the stored-key summary without dirtying the draft");
            model.Text = "draft-model";
            Test(); Test();
            check(cancel.Visibility == Visibility.Visible, "active service test exposes cancellation");
            check(client.Calls.Count == 1 && client.Calls[^1].Key == "stored-fixture-key", "blank draft key uses only matching stored secret and duplicate test is ignored");
            check(client.Calls[^1].Source == AiServiceTestController.Source && client.Calls[^1].Instruction == AiServiceTestController.Instruction,
                "settings test uses fixed source and instruction");
            Find<Expander>(window, "AiAdvancedSettings").IsExpanded = true;
            check(status.Text == "測試中…", "advanced expansion does not cancel test");
            client.Requests[^1].SetResult(new(true, "private test output")); await Task.Delay(40);
            check(status.Text == "✓ 連線成功" && panel.IsDirty && cancel.Visibility == Visibility.Collapsed, "testing success is separate from dirty state and hides cancellation");
            snapshot(window, "services-test-success.png");
            var editor = (TextBox)model.Template.FindName("PART_EditableTextBox", model);
            editor.Text = "keyboard-edited-model";
            check(status.Text == "尚未測試", "typing in actual editable model resets test status");
            Click("AiReplaceKey");
            key.Password = "draft-fixture-key";
            check(status.Text == "尚未測試", "key edit clears previous test success");
            endpoint.Text = "https://fixture.invalid/v1/responses";
            Test();
            check(client.Calls[^1].Profile.Endpoint == endpoint.Text && client.Calls[^1].Profile.Model == model.Text
                && client.Calls[^1].Key == key.Password, "test uses unsaved endpoint model and key");
            key.Clear(); await Task.Delay(20);
            check(status.Text == "尚未測試", "editing pending key cancels test");
            client.Requests[^1].SetResult(new(true, "late response")); await Task.Delay(20);
            check(status.Text == "尚未測試", "late completion cannot update edited draft");
            Test(); check(client.Calls[^1].Key == "", "new endpoint cannot reuse old endpoint secret");
            Click("AiCancelServiceTest"); await Task.Delay(20);
            endpoint.Text = profile.Endpoint; remove.IsChecked = true;
            Test(); check(client.Calls[^1].Key == "", "explicit removal passes empty key despite saved secret");
            panel.Discard(); await Task.Delay(20);
            check(status.Text == "尚未測試", "discard cancels pending test");
            model.Text = "draft-model";
            Test(); providers.SelectedIndex = 1; await Task.Delay(20);
            check(status.Text == "尚未測試", "switching provider cancels pending test");
            providers.SelectedIndex = 0; Test();
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); await Task.Delay(20);
            check(status.Text == "尚未測試", "unload cancels pending test without disposing page");
            Test(); Click("AiCancelServiceTest"); await Task.Delay(20);
            check(client.Calls.Count > 5 && status.Text == "尚未測試", "page can test again after unload");
            var callsBeforeInvalid = client.Calls.Count;
            model.Text = ""; Test();
            check(client.Calls.Count == callsBeforeInvalid, "invalid draft is rejected before sending");
            model.Text = "draft-model";
            var after = Files();
            check(before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out var bytes) && p.Value.SequenceEqual(bytes)),
                "all service test edits and requests preserve every isolated settings file byte");
            Click("AiReplaceKey"); key.Password = "saved-new-key"; Test();
            client.Requests[^1].SetResult(new(true, "private success")); await Task.Delay(40);
            check(panel.TrySave() && status.Text == "✓ 連線成功" && !panel.IsDirty && key.Password == ""
                && !save.IsEnabled && !discard.IsEnabled && key.Visibility == Visibility.Collapsed,
                "save clears password programmatically while preserving effective-key test success");
            model.Text = "failure-model"; Test();
            client.Requests[^1].SetResult(new(false, "private failure", "network")); await Task.Delay(40);
            snapshot(window, "services-test-failure.png");
            check(status.Text.StartsWith("測試失敗") && panel.TrySave() && status.Text.StartsWith("測試失敗"), "failed test still permits saving and save does not claim test success");
            Test(); panel.Detach(); await Task.Delay(20);
            client.Requests[^1].SetResult(new(true, "late detached output")); await Task.Delay(20);
            check(status.Text == "尚未測試", "detach cancels and ignores late response");
            check(!client.Disposed, "settings panel never disposes injected client");
        }
        finally { panel.Detach(); window.Close(); }
        var reopened = new AiSettingsPanel(preferences, _ => { }, client);
        var reopenedWindow = new Window { Content = reopened };
        try
        {
            reopenedWindow.Show(); await Task.Delay(20);
            check(Find<TextBlock>(reopenedWindow, "AiServiceTestStatus").Text == "尚未測試", "reopening settings never restores test success");
        }
        finally { reopened.Detach(); reopenedWindow.Close(); }
    }
}
