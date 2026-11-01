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
    private RadioButton actionTab = null!, searchTab = null!;
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
        var product = ActionUi.Heading("SnapBar", 22); product.Margin = new(12, 0, 0, 26); DockPanel.SetDock(product, Dock.Top); sidebar.Children.Add(product);
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
        var badge = new Border { Background = ActionUi.Pale, CornerRadius = new(12), Padding = new(10, 6, 10, 6), Child = mode, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(badge, Dock.Right); heading.Children.Add(badge); pageHeading = ActionUi.Heading("一般", 24); heading.Children.Add(pageHeading); workspace.Children.Add(heading);
        notice = ActionUi.Text("", 12, Brushes.Firebrick); notice.Margin = new(0, 0, 0, 12); notice.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(notice, "SettingsNotice"); Grid.SetRow(notice, 1); workspace.Children.Add(notice);
        pageHost = new Grid(); Grid.SetRow(pageHost, 2); workspace.Children.Add(pageHost);
        generalPage = new(preferences.AutomaticDetectionEnabled, ToggleAutomatic, ShowActionSettings); toggle = generalPage.AutomaticToggle;
        generalPage.ShowPreview(preferences.Actions); AddPage(SettingsCategory.General, generalPage);
        actionSettingsPanel = new(preferences.Actions, SaveActionSettings);
        searchSettingsPanel = new(); searchSettingsPanel.ShowTemplate(preferences.SearchUrlTemplate);
        searchSettingsPanel.SaveTemplate = SetSearchUrlTemplate;
        searchSettingsPanel.RevealRequested = () => { ShowCategory(SettingsCategory.Actions); ShowSearchSettings(); };
        AddPage(SettingsCategory.Actions, BuildActionsPage());
        aiCommandsPanel = new(aiPreferences); AddPage(SettingsCategory.AiCommands, aiCommandsPanel);
        aiSettingsPanel = new(aiPreferences); aiSettingsPanel.RevealRequested = () => ShowCategory(SettingsCategory.AiServices); AddPage(SettingsCategory.AiServices, aiSettingsPanel);
        AddPage(SettingsCategory.Compatibility, BuildCompatibilityPage());
        var support = new SettingsSupportPage(() => _ = ExitAsync()); status = support.Status; events = support.Events; AddPage(SettingsCategory.About, support);
        lifetimeHint = ActionUi.Text("關閉視窗後繼續待命 · 搜尋與 AI 服務請按儲存", 12, ActionUi.Muted); lifetimeHint.Margin = new(0, 16, 0, 0); Grid.SetRow(lifetimeHint, 3); workspace.Children.Add(lifetimeHint);
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
        var page = new DockPanel(); var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 12) };
        var host = new Grid(); host.Children.Add(actionSettingsPanel); host.Children.Add(searchSettingsPanel); searchSettingsPanel.Visibility = Visibility.Collapsed;
        actionTab = new RadioButton { Content = "動作", GroupName = "ActionsSubpage", Style = (Style)FindResource("ActionPlacementTab"), IsChecked = true, Margin = new(0, 0, 6, 0) };
        searchTab = new RadioButton { Content = "搜尋", GroupName = "ActionsSubpage", Style = (Style)FindResource("ActionPlacementTab") };
        AutomationProperties.SetAutomationId(actionTab, "ActionsSubpageActions"); AutomationProperties.SetAutomationId(searchTab, "ActionsSubpageSearch");
        actionTab.Checked += (_, _) => { actionSettingsPanel.Visibility = Visibility.Visible; searchSettingsPanel.Visibility = Visibility.Collapsed; };
        searchTab.Checked += (_, _) => { actionSettingsPanel.Visibility = Visibility.Collapsed; searchSettingsPanel.Visibility = Visibility.Visible; searchSettingsPanel.IsExpanded = true; };
        tabs.Children.Add(actionTab); tabs.Children.Add(searchTab); DockPanel.SetDock(tabs, Dock.Top); page.Children.Add(tabs); page.Children.Add(host); return page;
    }

    internal void ShowSearchSettings() { NavigateTo(SettingsCategory.Actions); searchTab.IsChecked = true; searchSettingsPanel.IsExpanded = true; }
    internal void NavigateTo(SettingsCategory category)
    {
        if (!settingsPages.ContainsKey(category)) return;
        var moveFocus = settingsPages.TryGetValue(CurrentCategory, out var previous) && previous.IsKeyboardFocusWithin && CurrentCategory != category;
        CurrentCategory = category;
        foreach (var (key, page) in settingsPages) page.Visibility = key == category ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (key, button) in navigation) if (button.IsChecked != (key == category)) button.IsChecked = key == category;
        pageHeading.Text = CategoryName(category);
        if (moveFocus) navigation[category].Focus();
    }
    private void ShowCategory(SettingsCategory category) { if (exitRequested) return; ShowSettings(); NavigateTo(category); }

    private bool SaveActionSettings(ActionSettings value)
    {
        preferences = preferences with { Actions = value }; controller?.ConfigureActions(value); generalPage.ShowPreview(value);
        try { settingsStore.Save(preferences); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ShowNotice("本次動作設定已生效，但無法保存；請檢查權限或 64 KiB 設定上限。"); return false; }
    }

    private FrameworkElement BuildCompatibilityPage()
    {
        var page = new DockPanel(); var content = new StackPanel();
        compatibilityToggle = new CheckBox { Content = "選字相容模式", IsChecked = preferences.ClipboardCompatibilityEnabled, Margin = new(0, 0, 0, 0) };
        AutomationProperties.SetAutomationId(compatibilityToggle, "ClipboardCompatibilityToggle");
        compatibilityToggle.Checked += (_, _) => SetClipboardCompatibility(true); compatibilityToggle.Unchecked += (_, _) => SetClipboardCompatibility(false); content.Children.Add(compatibilityToggle);
        var compatibilityHelp = ActionUi.Text("原生讀取不足時，暫借剪貼簿取得文字。會記住此設定；相容讀取可能出現在剪貼簿歷程。", 12, ActionUi.Muted); compatibilityHelp.Margin = new(27, 4, 0, 10); content.Children.Add(compatibilityHelp);
        sublimeToggle = new CheckBox { Content = "Sublime 支援", IsChecked = preferences.SublimeCompatibilityEnabled, IsEnabled = preferences.ClipboardCompatibilityEnabled, Margin = new(27, 0, 0, 0) };
        AutomationProperties.SetAutomationId(sublimeToggle, "SublimeCompatibilityToggle"); sublimeToggle.Checked += (_, _) => SetSublimeCompatibility(true); sublimeToggle.Unchecked += (_, _) => SetSublimeCompatibility(false); content.Children.Add(sublimeToggle);
        var sublimeHelp = ActionUi.Text("首次啟用會設定有條件的複製／剪下／貼上快捷鍵；之後自動恢復，未安裝外掛。", 12, ActionUi.Muted); sublimeHelp.Margin = new(54, 4, 0, 10); content.Children.Add(sublimeHelp);
        DockPanel.SetDock(content, Dock.Top); page.Children.Add(content);
        excludedAppsPanel = new(); excludedAppsPanel.ShowApplications(preferences.ExcludedApplications);
        excludedAppsPanel.AddRequested += entry => AddExcludedApplication(entry.ExecutablePath, entry.DisplayName);
        excludedAppsPanel.RemoveRequested += entry => RemoveExcludedApplication(entry.ExecutablePath); excludedAppsPanel.ValidationFailed += ShowNotice; page.Children.Add(excludedAppsPanel);
        return page;
    }
}
