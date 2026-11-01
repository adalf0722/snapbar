using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SnapBar.App;
using SnapBar.Core;

internal static class SettingsNavigationTest
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Children<T>(child)) yield return descendant;
    }

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Children<T>(root).Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static async Task Layout(Window window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    public static async Task RunAsync(MainWindow main, string directory, Action<bool, string> check)
    {
        check(main.Content is not ScrollViewer, "settings navigation uses a fixed root rather than a scrolling settings document");
        main.NavigateTo(SettingsCategory.General); await Layout(main);
        check(!Children<FrameworkElement>(main).Any(element => AutomationProperties.GetAutomationId(element) is "GeneralActionPreview" or "ManageActions")
            && Find<StackPanel>(main, "GeneralShortcutKeys").Children.OfType<Border>().Count() == 3,
            "settings general page contains actual settings and distinct shortcut keycaps without an inert toolbar");
        check(!Find<TextBlock>(main, "SettingsMode").IsVisible,
            "settings general page avoids repeating normal detection status beside the switch");
        var sixActions = ActionCatalog.SetPlacement(ActionCatalog.SetPlacement(ActionCatalog.Normalize(new()), "link", ActionPlacement.Bar), "path", ActionPlacement.Bar);
        main.ActionPanel.ShowSettings(sixActions);
        main.NavigateTo(SettingsCategory.Actions); await Layout(main);
        check(!Children<RadioButton>(main).Any(button => button.GroupName == "ActionsSubpage"),
            "settings action manager has no second action/search navigation layer");
        var actionList = Children<ScrollViewer>(main.ActionPanel).Single(s => AutomationProperties.GetName(s) == "動作列中的動作");
        var actionRows = Children<Grid>(main.ActionPanel).Where(row => AutomationProperties.GetAutomationId(row).StartsWith("ActionRow_")).ToArray();
        check(actionRows.Length == 6 && actionList.ScrollableHeight < 1 && actionRows.All(row =>
        {
            var bounds = row.TransformToAncestor(actionList).TransformBounds(new Rect(row.RenderSize));
            return bounds.Top >= -1 && bounds.Bottom <= actionList.ViewportHeight + 1;
        }), "settings standard window displays all six action rows and sorting controls without scrolling");
        var appearance = Find<Expander>(main.ActionPanel, "ActionSettingsAppearance");
        var theme = Children<ComboBox>(main.ActionPanel).Single(c => AutomationProperties.GetName(c) == "動作列主題");
        check(!appearance.IsExpanded && !theme.IsVisible, "settings appearance options start collapsed to prioritize the action list");
        appearance.IsExpanded = true; await Layout(main);
        check(theme.IsVisible && theme.ActualWidth >= 100, "settings appearance options remain available through the explicit expander");
        UiAppearanceTest.Snapshot(main, "settings-actions-appearance.png");
        appearance.IsExpanded = false;
        Children<Button>(main.ActionPanel).Single(b => AutomationProperties.GetName(b) == "複製下移").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(main);
        var moved = Children<StackPanel>(main.ActionPanel).Where(p => AutomationProperties.GetAutomationId(p).StartsWith("ActionBarPreview_")).Select(p => (string)p.Tag).ToArray();
        check(moved.SequenceEqual(new[] { "cut", "copy", "paste", "search", "link", "path" }),
            "settings compact action rows retain working move buttons and synchronized preview order");
        Children<Button>(main.ActionPanel).Single(b => AutomationProperties.GetName(b) == "複製上移").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(main);
        foreach (var category in Enum.GetValues<SettingsCategory>())
        {
            main.NavigateTo(category); await Layout(main);
            check(main.CurrentCategory == category, "settings navigation reaches category " + category);
            if (category == SettingsCategory.AiCommands)
            {
                var list = Find<ScrollViewer>(main.AiCommandsPanel, "AiCommandsSettingsList");
                check(list.ScrollableHeight < 1 && main.AiCommandsPanel.VisiblePromptIds.Count == 5,
                    "settings standard window displays all five initial AI commands alongside the service setup hint");
                Find<Button>(main.AiCommandsPanel, "AddAiCommand").Focus(); await Layout(main);
                UiAppearanceTest.Snapshot(main, "settings-command-keyboard-focus.png");
            }
            if (category == SettingsCategory.Compatibility)
                check(Find<StackPanel>(main, "ExcludedAppsEmptyState").IsVisible,
                    "settings empty exclusions explain the list instead of presenting an unexplained blank surface");
            if (category == SettingsCategory.About)
                check(!Find<TextBlock>(main, "SettingsBuildVersion").IsVisible
                    && Children<TextBlock>(main).Any(text => text.Text.StartsWith("版本 ") && !text.Text.Contains('+')),
                    "settings about page presents a readable version while keeping full build details in diagnostics");
            UiAppearanceTest.Snapshot(main, "settings-" + category.ToString().ToLowerInvariant() + ".png");
        }

        main.NavigateTo(SettingsCategory.Actions); await Layout(main);
        Find<Button>(main.ActionPanel, "EditSearchAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(main);
        check(main.SearchPanel.IsVisible && !main.ActionPanel.IsVisible && Find<Button>(main, "BackToActions").IsVisible
            && ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, Find<ComboBox>(main.SearchPanel, "SearchProvider"))
            && Children<TextBlock>(main).Any(text => text.Text == "編輯搜尋動作"),
            "settings search action edit opens its settings with an explicit return path");
        check(!Children<Expander>(main.SearchPanel).Any(), "settings search action presents the form directly without a duplicate disclosure level");
        var searchProvider = Find<ComboBox>(main.SearchPanel, "SearchProvider");
        var searchUrl = Find<TextBox>(main.SearchPanel, "SearchUrlTemplate");
        searchProvider.SelectedIndex = 2; searchUrl.Text = "https://example.com/draft?q={query}";
        check(main.SearchPanel.IsDirty, "settings search records an explicit unsaved custom template");
        Find<Button>(main, "BackToActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(main);
        check(main.ActionPanel.IsVisible && !main.SearchPanel.IsVisible && main.SearchPanel.IsDirty
            && Find<TextBlock>(main.ActionPanel, "SearchActionDraftStatus").IsVisible
            && ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, Find<Button>(main.ActionPanel, "EditSearchAction")),
            "settings returning from search editing preserves and marks the draft and restores focus to its action");
        Find<Button>(main.ActionPanel, "EditSearchAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(main);
        check(searchUrl.Text == "https://example.com/draft?q={query}", "settings reopening the search action resumes the same unsaved draft");
        UiAppearanceTest.Snapshot(main, "settings-search-action-editor.png");
        main.NavigateTo(SettingsCategory.General); main.Close(); main.ShowSettings();
        main.SearchPanel.Reveal(); await Layout(main);
        check(main.SearchPanel.IsDirty && searchUrl.Text == "https://example.com/draft?q={query}",
            "settings switching pages and hiding to tray preserve the unsaved search draft");
        main.SearchPanel.Discard();
        check(!main.SearchPanel.IsDirty && searchProvider.SelectedIndex == 0,
            "settings discarding search changes restores the latest saved provider");
        Find<Button>(main, "BackToActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(main);
        foreach (var placement in new[] { ActionPlacement.More, ActionPlacement.Hidden, ActionPlacement.Bar })
        {
            Children<ComboBox>(main.ActionPanel).Single(combo => AutomationProperties.GetName(combo) == "搜尋顯示位置").SelectedIndex = (int)placement;
            Find<RadioButton>(main.ActionPanel, "ActionPlacementTab_" + placement).IsChecked = true; await Layout(main);
            Find<Button>(main.ActionPanel, "EditSearchAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(main);
            check(main.SearchPanel.IsVisible, "settings search remains editable when placed in " + placement);
            Find<Button>(main, "BackToActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(main);
        }

        main.NavigateTo(SettingsCategory.AiCommands); await Layout(main);
        var configureService = Find<Button>(main.AiCommandsPanel, "ConfigureAiServiceFromCommands");
        check(configureService.IsVisible && Find<Button>(main.AiCommandsPanel, "AddAiCommand").IsEnabled,
            "settings missing AI service offers setup while leaving command management available");
        configureService.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(main);
        check(main.CurrentCategory == SettingsCategory.AiServices,
            "settings missing-service action routes directly to the embedded service page");
        var services = main.AiServicesPanel;
        var provider = Find<ComboBox>(services, "AiProvider");
        var model = Find<TextBox>(services, "AiModel");
        var key = Find<PasswordBox>(services, "AiApiKey");
        provider.SelectedIndex = 1; model.Text = "disposable-local-model";
        check(services.IsDirty && main.AiPreferences.SelectedProfile.Id == "openai",
            "settings selecting a service changes only its draft before save");
        key.Password = "disposable-preview-key";
        main.NavigateTo(SettingsCategory.General); main.NavigateTo(SettingsCategory.AiServices);
        check(services.HasPendingKeyChanges && key.Password == "disposable-preview-key",
            "settings service keys remain pending in memory across page navigation");
        services.Discard();
        check(!services.IsDirty && !services.HasPendingKeyChanges && key.Password.Length == 0
            && provider.SelectedIndex == 0, "settings discarding service changes clears pending key controls and restores effective selection");
        provider.SelectedIndex = 1; model.Text = "disposable-local-model";
        check(services.TrySave() && !services.IsDirty && main.AiPreferences.SelectedProfile.Id == "compatible"
            && new AiPreferences(directory).SelectedProfile.Model == "disposable-local-model",
            "settings saving a local service persists fields without requiring a key");

        var aiPath = Path.Combine(directory, "ai.json");
        File.Delete(aiPath); Directory.CreateDirectory(aiPath); model.Text = "unsaved-retry-model";
        check(!services.TrySave() && services.IsDirty && model.Text == "unsaved-retry-model"
            && main.AiPreferences.SelectedProfile.Model == "disposable-local-model",
            "settings failed service save preserves both the draft and effective profile");
        Directory.Delete(aiPath); services.Discard();
        var secretsPath = Path.Combine(directory, "secrets");
        File.WriteAllText(secretsPath, "Disposable fixture blocking the secret directory.", new System.Text.UTF8Encoding(false));
        key.Password = "disposable-partial-save-key";
        check(!services.TrySave() && services.IsDirty && services.HasPendingKeyChanges
            && Find<TextBlock>(services, "AiSettingsError").Text.Contains("金鑰"),
            "settings partial service save keeps pending key changes and reports the failed stage");
        File.Delete(secretsPath);
        check(services.TrySave() && !services.IsDirty && !services.HasPendingKeyChanges && key.Password.Length == 0
            && main.AiPreferences.Secrets.Get(main.AiPreferences.SelectedProfile) == "disposable-partial-save-key",
            "settings retry stores the protected key and clears the pending key control");
        Find<CheckBox>(services, "AiRemoveKey").IsChecked = true;
        check(services.TrySave() && main.AiPreferences.Secrets.Get(main.AiPreferences.SelectedProfile) == null,
            "settings explicit key removal persists without removing the provider");
        Directory.Delete(secretsPath);

        main.NavigateTo(SettingsCategory.AiCommands); await Layout(main);
        var commands = main.AiCommandsPanel;
        var preferences = main.AiPreferences;
        check(Find<TextBlock>(commands, "AiCommandsCount").Text == $"{preferences.Settings.Prompts.Length} 個命令",
            "settings unfiltered command count omits a redundant fraction");
        var grammarWasPinned = preferences.Settings.PinnedIds.Contains("grammar");
        var pin = Find<ToggleButton>(commands, "AiCommandPin_grammar");
        check(pin.IsChecked == grammarWasPinned && pin.ToolTip is string && AutomationProperties.GetName(pin).Contains("修正文法拼字"),
            "settings icon pin exposes its selected state and command-specific accessible label");
        pin.Focus();
        ((IToggleProvider)new ToggleButtonAutomationPeer(pin).GetPattern(PatternInterface.Toggle)).Toggle(); await Layout(main);
        check(preferences.Settings.PinnedIds.Contains("grammar") != grammarWasPinned
            && Find<ToggleButton>(commands, "AiCommandPin_grammar").IsChecked == !grammarWasPinned
            && ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, Find<ToggleButton>(commands, "AiCommandPin_grammar")),
            "settings accessible pin toggle persists and refreshes the selected state without losing keyboard focus");
        ((IToggleProvider)new ToggleButtonAutomationPeer(Find<ToggleButton>(commands, "AiCommandPin_grammar")).GetPattern(PatternInterface.Toggle)).Toggle();
        check(!configureService.IsVisible, "settings a configured local service needs no key and clears the setup notice");
        var initialIds = preferences.Settings.Prompts.Select(prompt => prompt.Id).ToArray();
        check(commands.VisiblePromptIds.SequenceEqual(initialIds), "settings command manager uses stored order rather than runtime ranking");
        preferences.TogglePin("grammar", out _);
        preferences.TrySave(preferences.Settings with { RecentIds = ["translate-en", "summary"] }, out _);
        check(commands.VisiblePromptIds.SequenceEqual(initialIds), "settings command rows do not move when pins and recent executions change");
        commands.SetFilter("translate", "寫作");
        check(commands.VisiblePromptIds.Count == 0, "settings command search and category restrictions both apply");
        commands.SetFilter("translate", "翻譯");
        check(commands.VisiblePromptIds.SequenceEqual(new[] { "translate-en" }), "settings command filtering finds the matching category and query");
        check(Find<TextBlock>(commands, "AiCommandsCount").Text == $"找到 1 個，共 {preferences.Settings.Prompts.Length} 個",
            "settings filtered command count explains matches and total");
        commands.SetFilter(""); commands.DeletePrompt("improve");
        check(preferences.LastDeletion?.Prompt.Id == "improve" && !commands.VisiblePromptIds.Contains("improve"),
            "settings command deletion exposes a shared recoverable state");
        preferences.TrySavePrompt(new("settings-extra", "Disposable", "Preserve input.", ""), true, out _);
        commands.RestoreDeletedCommand();
        check(preferences.LastDeletion == null && commands.VisiblePromptIds.SequenceEqual(initialIds.Append("settings-extra")),
            "settings command undo restores original position without losing a later addition");
        commands.SetFilter("", "");
        check(commands.VisiblePromptIds.SequenceEqual(new[] { "settings-extra" }), "settings uncategorized filter differs from all categories");
        commands.SetFilter(""); UiAppearanceTest.Snapshot(main, "settings-ai-commands-populated.png");
        preferences.TrySavePrompt(new("filter-last", "Filter fixture", "Preserve input.", "暫存分類"), true, out _);
        commands.SetFilter("", "暫存分類"); commands.DeletePrompt("filter-last");
        check(commands.VisiblePromptIds.Count == 0,
            "settings deleting the final member keeps the selected category instead of exposing other commands");
        commands.RestoreDeletedCommand();
        check(commands.VisiblePromptIds.SequenceEqual(new[] { "filter-last" }),
            "settings restoring the final category member returns it to the unchanged active filter");
        commands.DeletePrompt("filter-last"); commands.SetFilter("");
        var sentinelPrompt = new AiPrompt("__new", "Legal stored identity", "Preserve input.", "");
        preferences.TrySavePrompt(sentinelPrompt, true, out _);
        var storedIdentityEditor = preferences.OpenEditor(main, sentinelPrompt);
        var createEditor = preferences.OpenEditor(main, null);
        check(!ReferenceEquals(storedIdentityEditor, createEditor),
            "settings a legal command ID cannot collide with the new-command editor registry entry");
        storedIdentityEditor.Close(); createEditor.Close(); preferences.DeletePrompt(sentinelPrompt.Id, out _);

        main.SearchPanel.Reveal(); searchProvider.SelectedIndex = 1;
        var exitChoices = 0;
        main.ExitChoiceProvider = _ =>
        {
            exitChoices++;
            if (exitChoices == 1) main.ExitAsync().GetAwaiter().GetResult();
            return ExitChoice.Cancel;
        };
        await main.ExitAsync();
        check(exitChoices == 1 && main.IsVisible && main.SearchPanel.IsDirty,
            "settings repeated exit during draft resolution reuses the pending decision instead of opening another");
        await main.ExitAsync();
        check(exitChoices == 2 && main.IsVisible,
            "settings canceled exit releases its resolution guard so a later exit can be attempted");
        main.ExitChoiceProvider = _ => ExitChoice.Cancel;
        check(!main.TryResolveDraftsForExit() && main.SearchPanel.IsDirty,
            "settings canceling true exit leaves the unsaved draft intact");
        searchProvider.SelectedIndex = 2; searchUrl.Text = "file:///invalid";
        main.ExitChoiceProvider = _ => ExitChoice.Save;
        check(!main.TryResolveDraftsForExit() && main.SearchPanel.IsDirty && main.CurrentCategory == SettingsCategory.Actions,
            "settings save-and-exit validation failure keeps the app running at its invalid form");
        main.ExitChoiceProvider = _ => ExitChoice.Discard;
        check(main.TryResolveDraftsForExit() && !main.SearchPanel.IsDirty,
            "settings discard-and-exit resolves pending forms without silently saving them");
        searchProvider.SelectedIndex = 1; main.ExitChoiceProvider = _ => ExitChoice.Save;
        check(main.TryResolveDraftsForExit() && !main.SearchPanel.IsDirty
            && new SettingsStore(Path.Combine(directory, "settings.json")).Load().Settings.SearchUrlTemplate == SearchSettingsPanel.BingTemplate,
            "settings save-and-exit persists validated drafts through the normal save route");
        main.SetSearchUrlTemplate(WebActionPolicy.DefaultSearchTemplate);
        main.ExitChoiceProvider = _ => throw new InvalidOperationException("Clean exit must not request a draft choice.");
        check(main.TryResolveDraftsForExit(), "settings clean exit needs no extra confirmation");
        main.ExitChoiceProvider = null;

        var editor = preferences.OpenEditor(main, null); await Layout(editor);
        Find<TextBox>(editor, "AiPromptName").Text = "Disposable pending command";
        main.ExitChoiceProvider = _ => ExitChoice.Save;
        check(!main.TryResolveDraftsForExit() && editor.IsVisible,
            "settings true exit includes open command editors and retains invalid drafts");
        main.ExitChoiceProvider = _ => ExitChoice.Discard;
        check(main.TryResolveDraftsForExit() && !editor.IsVisible,
            "settings discard-and-exit closes a pending command editor without creating its command");
        main.ExitChoiceProvider = null;

        var work = SystemParameters.WorkArea;
        var constrained = new Rect(work.Left, work.Top, Math.Min(work.Width, 1366d / 1.5), Math.Min(work.Height, 480));
        main.ConstrainToWorkArea(constrained); await Layout(main);
        check(main.ActualWidth <= constrained.Width + 1 && main.ActualHeight <= constrained.Height + 1
            && main.MinHeight <= constrained.Height,
            "settings workarea takes precedence over nominal minima at a simulated 150-percent laptop workarea");
        foreach (var category in Enum.GetValues<SettingsCategory>())
        {
            main.NavigateTo(category); await Layout(main);
            UiAppearanceTest.Snapshot(main, "settings-constrained-" + category.ToString().ToLowerInvariant() + ".png");
        }
        main.NavigateTo(SettingsCategory.Actions); await Layout(main);
        check(Children<ScrollViewer>(main.ActionPanel).Any(scroll => AutomationProperties.GetName(scroll) == "動作列中的動作" && scroll.ViewportHeight >= 56),
            "settings constrained actions retain at least one full row in the scrolling list");
        var compactPreview = Find<Expander>(main.ActionPanel, "ActionSettingsCompactPreview");
        check(compactPreview.IsVisible && !compactPreview.IsExpanded,
            "settings constrained actions keep an explicit collapsed preview entry");
        compactPreview.IsExpanded = true; await Layout(main);
        check(Find<ScrollViewer>(main.ActionPanel, "ActionSettingsPreview").IsVisible && actionList.ViewportHeight >= 56,
            "settings constrained preview can expand while retaining a full action row");
        appearance.IsExpanded = true; await Layout(main);
        check(!compactPreview.IsExpanded && theme.IsVisible && actionList.ViewportHeight >= 56,
            "settings constrained appearance and preview share space without hiding the action list");
        UiAppearanceTest.Snapshot(main, "settings-constrained-actions-appearance.png");
        appearance.IsExpanded = false;
        Find<Button>(main.ActionPanel, "EditSearchAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(main);
        var searchSave = Find<Button>(main.SearchPanel, "SaveSearchTemplate");
        var searchBack = Find<Button>(main, "BackToActions");
        check(new[] { searchSave, searchBack }.All(button =>
        {
            var bounds = button.TransformToAncestor((FrameworkElement)main.Content).TransformBounds(new Rect(button.RenderSize));
            return button.IsVisible && bounds.Top >= -1 && bounds.Bottom <= ((FrameworkElement)main.Content).ActualHeight + 1;
        }), "settings constrained search editor keeps both save and return actions inside the frame");
        UiAppearanceTest.Snapshot(main, "settings-constrained-search-editor.png");
        searchBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(main);
        main.NavigateTo(SettingsCategory.Compatibility); await Layout(main);
        var compatibilityRoot = (FrameworkElement)VisualTreeHelper.GetChild(main, 0);
        var addApp = Find<Button>(main, "AddExcludedApp");
        var addBounds = addApp.TransformToAncestor(compatibilityRoot).TransformBounds(new Rect(addApp.RenderSize));
        check(addApp.IsVisible && addBounds.Top >= 0 && addBounds.Bottom <= compatibilityRoot.ActualHeight + 1,
            "settings constrained exclusions keep the add-app operation visible without scrolling");
        main.NavigateTo(SettingsCategory.AiServices); await Layout(main);
        var content = (FrameworkElement)VisualTreeHelper.GetChild(main, 0);
        var save = Find<Button>(services, "SaveAiSettings");
        var saveBounds = save.TransformToAncestor(content).TransformBounds(new Rect(save.RenderSize));
        check(save.IsVisible && saveBounds.Top >= 0 && saveBounds.Bottom <= content.ActualHeight + 1,
            "settings constrained service page keeps its save row completely inside the frame");
        main.NavigateTo(SettingsCategory.AiCommands); await Layout(main);
        check(Find<ScrollViewer>(commands, "AiCommandsSettingsList").ViewportHeight >= 60,
            "settings constrained command page retains a usable local scrolling list");
        main.NavigateTo(SettingsCategory.About); await Layout(main);
        var diagnostics = Children<Expander>(main).Single(e => (string?)e.Header == "診斷與測試");
        diagnostics.IsExpanded = true; await Layout(main);
        var fixtureButton = Children<Button>(main).Single(b => AutomationProperties.GetName(b) == "開啟測試視窗");
        var fixtureBounds = fixtureButton.TransformToAncestor(content).TransformBounds(new Rect(fixtureButton.RenderSize));
        check(Find<TextBlock>(main, "SettingsBuildVersion").IsVisible && fixtureButton.IsVisible
            && fixtureBounds.Top >= 0 && fixtureBounds.Bottom <= content.ActualHeight + 1,
            "settings constrained diagnostics retain full build information and a reachable fixture button");
        UiAppearanceTest.Snapshot(main, "settings-constrained-about-diagnostics.png");
        diagnostics.IsExpanded = false;
        main.ClearWorkAreaConstraint(); main.Width = 980; main.Height = 720;
        main.NavigateTo(SettingsCategory.General); await Layout(main);
    }
}
