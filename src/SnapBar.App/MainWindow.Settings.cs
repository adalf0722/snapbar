using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<SettingsCategory, FrameworkElement> settingsPages = [];
    private readonly Dictionary<SettingsCategory, RadioButton> navigation = [];
    private ActionSettingsPanel actionSettingsPanel = null!;
    private AiSettingsPanel aiSettingsPanel = null!;
    private AiCommandsPanel aiCommandsPanel = null!;
    private GeneralSettingsPage generalPage = null!;
    private TextBlock pageHeading = null!;
    private FrameworkElement searchEditor = null!;
    private Grid pageHost = null!;
    internal SettingsCategory CurrentCategory { get; private set; } = SettingsCategory.General;
    internal ActionSettingsPanel ActionPanel => actionSettingsPanel;
    internal SearchSettingsPanel SearchPanel => searchSettingsPanel;
    internal AiSettingsPanel AiServicesPanel => aiSettingsPanel;
    internal AiCommandsPanel AiCommandsPanel => aiCommandsPanel;
    internal AiPreferences AiPreferences => aiPreferences;
    internal Func<IReadOnlyList<string>, ExitChoice>? ExitChoiceProvider { get; set; }
    internal void ConstrainToWorkArea(Rect workArea) => WindowWorkArea.SetWorkAreaOverride(this, workArea);
    internal void ClearWorkAreaConstraint() => WindowWorkArea.SetWorkAreaOverride(this, null);

    private static string CategoryName(SettingsCategory category) => category switch
    {
        SettingsCategory.General => "一般", SettingsCategory.Actions => "動作列", SettingsCategory.AiCommands => "AI 命令",
        SettingsCategory.AiServices => "AI 服務", SettingsCategory.Compatibility => "相容性", _ => "關於與支援"
    };
    private void BuildSettingsPages()
    {
        ActionUi.Setup(this, "SnapBar · 設定", 980, 720); MinWidth = 860; MinHeight = 600;
        WindowWorkArea.Attach(this);
        var shell = new Grid(); shell.ColumnDefinitions.Add(new() { Width = new(180) }); shell.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        AutomationProperties.SetAutomationId(shell, "SettingsShell");
        var sidebar = new DockPanel { Margin = new(14, 24, 12, 16) };
        var product = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(12, 0, 0, 26) };
        var brandIcon = ActionUi.BrandIcon(24); brandIcon.Margin = new(0, 0, 10, 0);
        product.Children.Add(brandIcon); product.Children.Add(ActionUi.Heading("SnapBar", 22));
        DockPanel.SetDock(product, Dock.Top); sidebar.Children.Add(product);
        var navigationPanel = new StackPanel();
        foreach (var category in Enum.GetValues<SettingsCategory>())
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new AiIconPresenter { Width = 19, Height = 19, Margin = new(0, 0, 10, 0), IconId = category switch
            { SettingsCategory.General => "settings", SettingsCategory.Actions => "copy", SettingsCategory.AiCommands => "spark", SettingsCategory.AiServices => "globe", SettingsCategory.Compatibility => "shield_20_regular", _ => "info_20_regular" } };
            content.Children.Add(icon); content.Children.Add(ActionUi.Text(CategoryName(category), 14));
            var button = new RadioButton { GroupName = "SettingsNavigation", Content = content, Margin = new(0, 0, 0, 5), Padding = new(12, 11, 8, 11),
                Style = (Style)FindResource("ActionPlacementTab"), HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetAutomationId(button, "SettingsNav_" + category); AutomationProperties.SetName(button, CategoryName(category));
            button.Checked += (_, _) => { if (settingsPages.Count > 0) NavigateTo(category); };
            button.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.ImeProcessed || Keyboard.Modifiers != ModifierKeys.None) return;
                if (e.Key is Key.Up or Key.Down or Key.Home or Key.End)
                {
                    var values = Enum.GetValues<SettingsCategory>(); var index = Array.IndexOf(values, category);
                    index = e.Key switch { Key.Up => Math.Max(0, index - 1), Key.Down => Math.Min(values.Length - 1, index + 1), Key.Home => 0, _ => values.Length - 1 };
                    NavigateTo(values[index]); navigation[values[index]].Focus(); e.Handled = true;
                }
                else if (e.Key is Key.Enter or Key.Space) { NavigateTo(category); e.Handled = true; }
            };
            navigation.Add(category, button); navigationPanel.Children.Add(button);
        }
        sidebar.Children.Add(navigationPanel);
        var sidebarSurface = new Border { Background = Brushes.White, BorderBrush = ActionUi.Line, BorderThickness = new(0, 0, 1, 0), Child = sidebar }; shell.Children.Add(sidebarSurface);
        var workspace = new Grid { Margin = new(28, 24, 28, 18) };
        for (var row = 0; row < 4; row++) workspace.RowDefinitions.Add(new() { Height = row == 2 ? new(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetColumn(workspace, 1); shell.Children.Add(workspace);
        var heading = new DockPanel { Margin = new(0, 0, 0, 18) };
        mode = ActionUi.Text("正在啟動", 12, ActionUi.Muted);
        AutomationProperties.SetAutomationId(mode, "SettingsMode");
        DockPanel.SetDock(mode, Dock.Right); heading.Children.Add(mode); pageHeading = ActionUi.Heading("一般", 24); heading.Children.Add(pageHeading); workspace.Children.Add(heading);
        notice = ActionUi.Text("", 12, Brushes.Firebrick); notice.Margin = new(0, 0, 0, 12); notice.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(notice, "SettingsNotice"); Grid.SetRow(notice, 1); workspace.Children.Add(notice);
        pageHost = new Grid(); Grid.SetRow(pageHost, 2); workspace.Children.Add(pageHost);
        generalPage = new(preferences.AutomaticDetectionEnabled, ToggleAutomatic, preferences.Hotkey, SaveHotkey, startupRegistration);
        generalPage.RecordingChanged = recording => { if (controller != null) controller.SetHotkeyRecording(recording); };
        toggle = generalPage.AutomaticToggle;
        AddPage(SettingsCategory.General, generalPage);
        actionSettingsPanel = new(preferences.Actions, SaveActionSettings, ShowSearchSettings);
        searchSettingsPanel = new(); searchSettingsPanel.ShowTemplate(preferences.SearchUrlTemplate);
        searchSettingsPanel.SaveTemplate = SetSearchUrlTemplate;
        searchSettingsPanel.RevealRequested = () => { ShowSettings(); ShowSearchSettings(); };
        searchSettingsPanel.Changed += () => actionSettingsPanel.ShowSearchDraft(searchSettingsPanel.IsDirty);
        AddPage(SettingsCategory.Actions, BuildActionsPage());
        aiCommandsPanel = new(aiPreferences); AddPage(SettingsCategory.AiCommands, aiCommandsPanel);
        aiSettingsPanel = new(aiPreferences); aiSettingsPanel.RevealRequested = () => ShowCategory(SettingsCategory.AiServices); AddPage(SettingsCategory.AiServices, aiSettingsPanel);
        AddPage(SettingsCategory.Compatibility, BuildCompatibilityPage());
        var support = new SettingsSupportPage(() => _ = ExitAsync()); status = support.Status; events = support.Events; AddPage(SettingsCategory.About, support);
        lifetimeHint = ActionUi.Text("關閉設定後，SnapBar 會繼續在背景執行。", 13, ActionUi.Brush("#4F5968")); lifetimeHint.Margin = new(0, 16, 0, 0); Grid.SetRow(lifetimeHint, 3); workspace.Children.Add(lifetimeHint);
        Content = shell;
        aiPreferences.SettingsHost = this;
        aiPreferences.SettingsRequested += ShowCategory;
        SizeChanged += (_, _) =>
        {
            var compact = ActualHeight < 600 || ActualWidth < 860;
            workspace.Margin = compact ? new(16, 12, 16, 10) : new(28, 24, 28, 18);
            heading.Margin = new(0, 0, 0, compact ? 10 : 18);
            generalPage.SetCompact(compact || generalPage.ActualHeight < 420 || generalPage.ActualWidth < 580);
        };
        NavigateTo(SettingsCategory.General);
    }

    private void AddPage(SettingsCategory category, FrameworkElement page)
    { page.Visibility = Visibility.Collapsed; settingsPages.Add(category, page); pageHost.Children.Add(page); }

    private FrameworkElement BuildActionsPage()
    {
        var host = new Grid(); host.Children.Add(actionSettingsPanel);
        var editor = new DockPanel();
        var back = ActionUi.Button("← 返回動作列", () => ShowActionList(returnFocus: true));
        AutomationProperties.SetAutomationId(back, "BackToActions");
        back.HorizontalAlignment = HorizontalAlignment.Left; back.Margin = new(0, 0, 0, 16);
        DockPanel.SetDock(back, Dock.Top); editor.Children.Add(back); editor.Children.Add(searchSettingsPanel);
        searchEditor = editor; searchEditor.Visibility = Visibility.Collapsed; host.Children.Add(editor); return host;
    }

    private void ShowActionList(bool returnFocus = false)
    {
        searchEditor.Visibility = Visibility.Collapsed; actionSettingsPanel.Visibility = Visibility.Visible;
        pageHeading.Text = CategoryName(SettingsCategory.Actions);
        if (returnFocus && !actionSettingsPanel.FocusSearchEditor()) navigation[SettingsCategory.Actions].Focus();
    }
    internal void ShowSearchSettings()
    {
        NavigateTo(SettingsCategory.Actions);
        actionSettingsPanel.Visibility = Visibility.Collapsed; searchEditor.Visibility = Visibility.Visible;
        pageHeading.Text = "編輯搜尋動作"; searchSettingsPanel.FocusEditor();
    }
    internal void NavigateTo(SettingsCategory category)
    {
        if (!settingsPages.ContainsKey(category)) return;
        var moveFocus = settingsPages.TryGetValue(CurrentCategory, out var previous) && previous.IsKeyboardFocusWithin && CurrentCategory != category;
        CurrentCategory = category;
        foreach (var (key, page) in settingsPages) page.Visibility = key == category ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (key, button) in navigation) if (button.IsChecked != (key == category)) button.IsChecked = key == category;
        pageHeading.Text = CategoryName(category);
        if (category == SettingsCategory.Actions) ShowActionList();
        UpdateModeVisibility();
        if (moveFocus) navigation[category].Focus();
    }
    private void ShowCategory(SettingsCategory category) { if (exitRequested) return; ShowSettings(); NavigateTo(category); }

    private bool SaveActionSettings(ActionSettings value)
    {
        preferences = preferences with { Actions = value }; controller?.ConfigureActions(value);
        try { settingsStore.Save(preferences); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ShowNotice("本次動作設定已生效，但無法保存；請檢查權限或 64 KiB 設定上限。"); return false; }
    }

    private FrameworkElement BuildCompatibilityPage()
    {
        var page = new DockPanel(); var content = new StackPanel { Margin = new(0, 0, 0, 16) };
        compatibilityToggle = new CheckBox { Content = "選字相容模式", IsChecked = preferences.ClipboardCompatibilityEnabled, Margin = new(0, 0, 0, 0) };
        compatibilityToggle.Style = (Style)FindResource("Switch");
        AutomationProperties.SetAutomationId(compatibilityToggle, "ClipboardCompatibilityToggle");
        AutomationProperties.SetName(compatibilityToggle, "選字相容模式");
        compatibilityToggle.Checked += (_, _) => SetClipboardCompatibility(true); compatibilityToggle.Unchecked += (_, _) => SetClipboardCompatibility(false);
        Border SettingRow(string title, string help, CheckBox control)
        {
            var row = new DockPanel(); control.VerticalAlignment = VerticalAlignment.Center; control.Margin = new(16, 0, 0, 0);
            DockPanel.SetDock(control, Dock.Right); row.Children.Add(control);
            var text = new StackPanel(); text.Children.Add(ActionUi.Heading(title, 15));
            var description = ActionUi.Text(help, 12, ActionUi.Muted); description.Margin = new(0, 5, 0, 0); text.Children.Add(description); row.Children.Add(text);
            return ActionUi.Card(row, new Thickness(16, 12, 16, 12));
        }
        var compatibilityRow = SettingRow("選字相容模式", "原生讀取不足時，暫借剪貼簿取得文字。相容讀取可能出現在剪貼簿歷程。", compatibilityToggle);
        compatibilityRow.Margin = new(0, 0, 0, 8); content.Children.Add(compatibilityRow);
        sublimeToggle = new CheckBox { Content = "Sublime 支援", IsChecked = preferences.SublimeCompatibilityEnabled, IsEnabled = preferences.ClipboardCompatibilityEnabled,
            Style = (Style)FindResource("Switch") };
        AutomationProperties.SetAutomationId(sublimeToggle, "SublimeCompatibilityToggle"); sublimeToggle.Checked += (_, _) => SetSublimeCompatibility(true); sublimeToggle.Unchecked += (_, _) => SetSublimeCompatibility(false);
        AutomationProperties.SetName(sublimeToggle, "Sublime 支援");
        content.Children.Add(SettingRow("Sublime 支援", "需先啟用選字相容模式。首次啟用會設定有條件的複製／剪下／貼上快捷鍵；之後自動恢復，未安裝外掛。", sublimeToggle));
        DockPanel.SetDock(content, Dock.Top); page.Children.Add(content);
        excludedAppsPanel = new(); excludedAppsPanel.ShowApplications(preferences.ExcludedApplications);
        excludedAppsPanel.AddRequested += entry => AddExcludedApplication(entry.ExecutablePath, entry.DisplayName);
        excludedAppsPanel.RemoveRequested += entry => RemoveExcludedApplication(entry.ExecutablePath); excludedAppsPanel.ValidationFailed += ShowNotice; page.Children.Add(excludedAppsPanel);
        return page;
    }
}
