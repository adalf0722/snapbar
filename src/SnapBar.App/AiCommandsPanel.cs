using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class AiCommandsPanel : ContentControl
{
    private readonly AiPreferences preferences;
    private readonly TextBox search;
    private readonly ComboBox category;
    private readonly StackPanel rows = new();
    private readonly TextBlock count, status, deletion, serviceStatus;
    private readonly Border undoNotice, serviceNotice;
    private readonly Button add;
    private bool syncing;
    internal IReadOnlyList<string> VisiblePromptIds { get; private set; } = [];
    private sealed record CategoryItem(string Label, string? Value) { public override string ToString() => Label; }

    internal AiCommandsPanel(AiPreferences preferences)
    {
        this.preferences = preferences; ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "AiCommandsSettingsPanel");
        var body = new DockPanel(); var top = new StackPanel();
        var tools = new DockPanel { Margin = new(0, 0, 0, 12) };
        add = ActionUi.Button("新增命令", () => preferences.OpenEditor(Window.GetWindow(this), null), "plus", true);
        AutomationProperties.SetAutomationId(add, "AddAiCommand"); DockPanel.SetDock(add, Dock.Right); tools.Children.Add(add);
        count = ActionUi.Text("", 13, ActionUi.Muted); AutomationProperties.SetAutomationId(count, "AiCommandsCount"); tools.Children.Add(count); top.Children.Add(tools);
        serviceStatus = ActionUi.Text("設定 AI 服務後即可使用", 12, ActionUi.Muted);
        var serviceLine = new DockPanel(); var configure = ActionUi.Button("設定服務…", () => preferences.RequestSettings(SettingsCategory.AiServices));
        AutomationProperties.SetName(configure, "設定 AI 服務");
        AutomationProperties.SetAutomationId(configure, "ConfigureAiServiceFromCommands"); DockPanel.SetDock(configure, Dock.Right); serviceLine.Children.Add(configure); serviceLine.Children.Add(serviceStatus);
        serviceNotice = new Border { Child = serviceLine, Margin = new(0, 0, 0, 8) }; top.Children.Add(serviceNotice);
        top.Children.Add(ActionUi.Text("搜尋名稱、指示與關鍵字", 12, ActionUi.Muted));
        var filters = new Grid { Margin = new(0, 0, 0, 12) }; filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new() { Width = new(170) });
        search = ActionUi.Input("", 256); search.Margin = new(0, 0, 10, 0); search.ToolTip = "搜尋名稱、指示與關鍵字";
        AutomationProperties.SetAutomationId(search, "AiCommandsSearch"); AutomationProperties.SetName(search, "搜尋 AI 命令"); filters.Children.Add(search);
        category = new ComboBox { MinHeight = 36 }; AutomationProperties.SetAutomationId(category, "AiCommandsCategory"); AutomationProperties.SetName(category, "命令分類"); Grid.SetColumn(category, 1); filters.Children.Add(category);
        top.Children.Add(filters); DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        var footer = new StackPanel { Margin = new(0, 10, 0, 0) };
        var undoLine = new DockPanel(); var undo = ActionUi.Button("復原", RestoreDeleted); AutomationProperties.SetAutomationId(undo, "RestoreDeletedAiCommand"); DockPanel.SetDock(undo, Dock.Right); undoLine.Children.Add(undo);
        deletion = ActionUi.Text("", 13); AutomationProperties.SetAutomationId(deletion, "DeletedAiCommandNotice"); undoLine.Children.Add(deletion);
        undoNotice = ActionUi.Card(undoLine, new Thickness(12, 8, 12, 8)); footer.Children.Add(undoNotice);
        status = ActionUi.Text("", 12, ActionUi.Muted); status.Margin = new(0, 8, 0, 0); AutomationProperties.SetAutomationId(status, "AiCommandsStatus"); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var list = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(list, "AiCommandsSettingsList"); body.Children.Add(list); Content = body;
        search.TextChanged += (_, _) => { if (!syncing) Render(); };
        category.SelectionChanged += (_, _) => { if (!syncing) Render(); };
        preferences.Changed += Refresh; preferences.UndoChanged += Render;
        Refresh();
    }

    internal void SetFilter(string query, string? categoryValue = null)
    {
        syncing = true; search.Text = query;
        category.SelectedItem = category.Items.Cast<CategoryItem>().FirstOrDefault(c => c.Value == categoryValue) ?? category.Items[0];
        syncing = false; Render();
    }
    internal void Refresh()
    {
        var chosen = (category.SelectedItem as CategoryItem)?.Value;
        syncing = true;
        var categories = new List<CategoryItem> { new("全部分類", null), new("未分類", "") };
        categories.AddRange(preferences.Settings.Prompts.Select(p => p.Category).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).Select(c => new CategoryItem(c, c)));
        if (chosen != null && categories.All(c => c.Value != chosen)) categories.Add(new(chosen, chosen));
        category.ItemsSource = categories; category.SelectedItem = categories.FirstOrDefault(c => c.Value == chosen) ?? categories[0];
        syncing = false; Render();
    }
    private void Render()
    {
        var focused = Keyboard.FocusedElement as FrameworkElement;
        var focusId = focused != null && rows.IsKeyboardFocusWithin ? AutomationProperties.GetAutomationId(focused) : null;
        var settings = preferences.Settings;
        var filtered = AiPromptCatalog.Search(settings, search.Text, (category.SelectedItem as CategoryItem)?.Value);
        VisiblePromptIds = filtered.Select(p => p.Id).ToArray(); rows.Children.Clear();
        add.IsEnabled = settings.Prompts.Length < 128;
        var hasFilter = !string.IsNullOrWhiteSpace(search.Text) || (category.SelectedItem as CategoryItem)?.Value != null;
        count.Text = (hasFilter ? $"找到 {filtered.Count} 個，共 {settings.Prompts.Length} 個" : $"{settings.Prompts.Length} 個命令")
            + (settings.Prompts.Length >= 128 ? " · 已達 128 筆上限" : "");
        var profile = preferences.SelectedProfile;
        serviceNotice.Visibility = AiPolicy.ValidateProfile(profile) != null || (profile.Protocol == AiProtocol.Responses && preferences.Secrets.Get(profile) == null)
            ? Visibility.Visible : Visibility.Collapsed;
        undoNotice.Visibility = preferences.LastDeletion == null ? Visibility.Collapsed : Visibility.Visible;
        deletion.Text = preferences.LastDeletion is { } removed ? $"已刪除「{removed.Prompt.Name}」" : "";
        foreach (var prompt in filtered)
        {
            var row = new Grid { MinHeight = 56 }; row.ColumnDefinitions.Add(new() { Width = new(42) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            AutomationProperties.SetAutomationId(row, "AiCommandRow_" + prompt.Id);
            var icon = ActionUi.Button("", () => ChooseIcon(prompt.Id)); icon.Content = new AiIconPresenter { IconId = AiIconCatalog.ForPrompt(prompt), Width = 22, Height = 22 };
            icon.Padding = new(6); icon.MinWidth = 34; icon.Margin = new(0, 0, 6, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(icon, "選擇圖示 " + prompt.Name); AutomationProperties.SetAutomationId(icon, "AiCommandIcon_" + prompt.Id); row.Children.Add(icon);
            var text = new StackPanel { Margin = new(8), VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(ActionUi.Text(prompt.Name, 14));
            text.Children.Add(ActionUi.Text(prompt.Category.Length == 0 ? "未分類" : prompt.Category, 12, ActionUi.Muted)); Grid.SetColumn(text, 1); row.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pinned = settings.PinnedIds.Contains(prompt.Id);
            var pinLabel = (pinned ? "取消釘選 " : "釘選 ") + prompt.Name;
            var pin = new ToggleButton { Content = ActionUi.Icon("pin", pinned ? ActionUi.Accent : ActionUi.Muted, 16),
                IsChecked = pinned, Style = (Style)FindResource("AiCommandPin"), ToolTip = pinLabel };
            AutomationProperties.SetName(pin, pinLabel); AutomationProperties.SetAutomationId(pin, "AiCommandPin_" + prompt.Id);
            var resettingPin = false;
            RoutedEventHandler changePin = (_, _) =>
            {
                if (resettingPin) return;
                if (!preferences.TogglePin(prompt.Id, out var error))
                { resettingPin = true; pin.IsChecked = pinned; resettingPin = false; status.Text = error; }
            };
            pin.Checked += changePin; pin.Unchecked += changePin;
            buttons.Children.Add(pin);
            var edit = ActionUi.Button("編輯", () => preferences.OpenEditor(Window.GetWindow(this), preferences.Settings.Prompts.FirstOrDefault(p => p.Id == prompt.Id)));
            AutomationProperties.SetAutomationId(edit, "AiCommandEdit_" + prompt.Id); buttons.Children.Add(edit);
            var more = ActionUi.Button("⋯", () => { }); AutomationProperties.SetName(more, "命令選項 " + prompt.Name); AutomationProperties.SetAutomationId(more, "AiCommandOptions_" + prompt.Id);
            var menu = new ContextMenu(); var delete = new MenuItem { Header = "刪除命令" }; delete.Click += (_, _) => DeletePrompt(prompt.Id); menu.Items.Add(delete); more.ContextMenu = menu;
            more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; }; buttons.Children.Add(more);
            Grid.SetColumn(buttons, 2); row.Children.Add(buttons);
            var card = ActionUi.Card(row, new Thickness(10, 4, 4, 4)); card.Margin = new(0, 0, 0, 5); rows.Children.Add(card);
        }
        if (filtered.Count == 0) rows.Children.Add(ActionUi.Text(settings.Prompts.Length == 0 ? "尚無命令，請新增第一個 AI 命令。" : "沒有符合搜尋與分類的命令。", 13, ActionUi.Muted));
        if (focusId != null)
        {
            var target = Descendants(rows).OfType<FrameworkElement>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == focusId);
            if (target != null) target.Focus(); else search.Focus();
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    internal bool DeletePrompt(string id)
    { var result = preferences.DeletePrompt(id, out var error); if (!result) status.Text = error; else status.Text = ""; return result; }
    internal bool RestoreDeletedCommand()
    {
        var id = preferences.LastDeletion?.Prompt.Id;
        if (!preferences.TryRestoreDeleted(out var error)) { status.Text = error; return false; }
        status.Text = id != null && !VisiblePromptIds.Contains(id) ? "已復原；目前篩選未顯示" : "命令已復原。"; return true;
    }
    private void RestoreDeleted() => RestoreDeletedCommand();
    private void ChooseIcon(string id)
    {
        var prompt = preferences.Settings.Prompts.FirstOrDefault(p => p.Id == id); if (prompt == null) return;
        var picker = new AiIconPickerWindow(AiIconCatalog.ForPrompt(prompt)) { Owner = Window.GetWindow(this) };
        picker.ShowDialog();
        if (picker.SelectedId is not { } icon) return;
        var latest = preferences.Settings.Prompts.FirstOrDefault(p => p.Id == id);
        if (latest == null) { status.Text = "此命令已被刪除。"; return; }
        if (!preferences.TrySavePrompt(latest with { Icon = icon }, false, out var error)) status.Text = error;
    }
    internal void Detach() { preferences.Changed -= Refresh; preferences.UndoChanged -= Render; }
}
