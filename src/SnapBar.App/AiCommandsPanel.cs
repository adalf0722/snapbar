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
        add = ActionUi.Button(UiText.Get("AiCommandsPanel_000"), () => preferences.OpenEditor(Window.GetWindow(this), null), "plus", true);
        AutomationProperties.SetAutomationId(add, "AddAiCommand"); DockPanel.SetDock(add, Dock.Right); tools.Children.Add(add);
        count = ActionUi.Text("", 13, ActionUi.Muted); AutomationProperties.SetAutomationId(count, "AiCommandsCount"); tools.Children.Add(count); top.Children.Add(tools);
        serviceStatus = ActionUi.Text("", 13);
        var serviceLine = new DockPanel(); var configure = ActionUi.Button(UiText.Get("AiCommandsPanel_002"), () => preferences.RequestSettings(SettingsCategory.AiServices));
        AutomationProperties.SetName(configure, UiText.Get("AiCommandsPanel_003"));
        AutomationProperties.SetAutomationId(configure, "ConfigureAiServiceFromCommands"); DockPanel.SetDock(configure, Dock.Right); serviceLine.Children.Add(configure); serviceLine.Children.Add(serviceStatus);
        serviceNotice = ActionUi.Card(serviceLine, new Thickness(12)); serviceNotice.Background = ActionUi.Pale;
        serviceNotice.Margin = new(0, 0, 0, 12); top.Children.Add(serviceNotice);
        var filters = new Grid { Margin = new(0, 0, 0, 12) }; filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new() { Width = new(170) });
        search = ActionUi.Input("", 256); SettingsFeedback.Hint(search, UiText.Get("AiCommandsPanel_005"));
        AutomationProperties.SetAutomationId(search, "AiCommandsSearch"); AutomationProperties.SetName(search, UiText.Get("AiCommandsPanel_006"));
        filters.Children.Add(new SettingsSearchBox(search, UiText.Get("AiCommandsPanel_004"), UiText.Get("SettingsSearch_Clear"), "ClearAiCommandsSearch")
        { Margin = new(0, 0, 12, 0) });
        category = new ComboBox { MinHeight = 36 }; AutomationProperties.SetAutomationId(category, "AiCommandsCategory"); AutomationProperties.SetName(category, UiText.Get("AiCommandsPanel_007")); Grid.SetColumn(category, 1); filters.Children.Add(category);
        top.Children.Add(filters); DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        var footer = new StackPanel { Margin = new(0, 10, 0, 0) };
        var undoLine = new DockPanel(); var undo = ActionUi.Button(UiText.Get("AiCommandsPanel_008"), RestoreDeleted); AutomationProperties.SetAutomationId(undo, "RestoreDeletedAiCommand"); DockPanel.SetDock(undo, Dock.Right); undoLine.Children.Add(undo);
        deletion = ActionUi.Text("", 13); AutomationProperties.SetAutomationId(deletion, "DeletedAiCommandNotice"); undoLine.Children.Add(deletion);
        undoNotice = ActionUi.Card(undoLine, new Thickness(12, 8, 12, 8)); footer.Children.Add(undoNotice);
        status = ActionUi.Text("", 12, ActionUi.Muted); status.Margin = new(0, 8, 0, 0); AutomationProperties.SetAutomationId(status, "AiCommandsStatus"); footer.Children.Add(status);
        SettingsFeedback.Live(status); SettingsFeedback.Live(deletion);
        DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var list = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(list, "AiCommandsSettingsList"); body.Children.Add(list); Content = body;
        search.TextChanged += (_, _) => { if (!syncing) Render(); };
        category.SelectionChanged += (_, _) => { if (!syncing && !UiText.IsRefreshing) Render(); };
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
        var categories = new List<CategoryItem> { new(UiText.Get("AiCommandsPanel_009"), null), new(UiText.Get("AiCommandsPanel_010"), "") };
        categories.AddRange(preferences.Settings.Prompts.Where(p => p.Category.Length > 0).DistinctBy(p => p.Category)
            .Select(p => new CategoryItem(UiNames.Category(p), p.Category)));
        if (chosen != null && categories.All(c => c.Value != chosen)) categories.Add(new(chosen, chosen));
        category.ItemsSource = categories; category.SelectedItem = categories.FirstOrDefault(c => c.Value == chosen) ?? categories[0];
        syncing = false; Render();
    }
    private void Render()
    {
        var focused = Keyboard.FocusedElement as FrameworkElement;
        var focusId = focused != null && rows.IsKeyboardFocusWithin ? AutomationProperties.GetAutomationId(focused) : null;
        var settings = preferences.Settings;
        var filtered = UiNames.SearchPrompts(settings, search.Text, (category.SelectedItem as CategoryItem)?.Value);
        VisiblePromptIds = filtered.Select(p => p.Id).ToArray(); rows.Children.Clear();
        add.IsEnabled = settings.Prompts.Length < 128;
        var hasFilter = !string.IsNullOrWhiteSpace(search.Text) || (category.SelectedItem as CategoryItem)?.Value != null;
        count.Text = UiText.Join((hasFilter ? UiText.Get("AiCommandsPanel_011",(filtered.Count),(settings.Prompts.Length)): UiText.Get("AiCommandsPanel_012",(settings.Prompts.Length))), (settings.Prompts.Length >= 128 ? UiText.Get("AiCommandsPanel_013") : ""));
        var profile = preferences.SelectedProfile;
        serviceStatus.Text = UiText.Get("SettingsFeedback_Connect", settings.Prompts.Length);
        serviceNotice.Visibility = AiPolicy.ValidateProfile(profile) != null || (profile.Protocol == AiProtocol.Responses && preferences.Secrets.Get(profile) == null)
            ? Visibility.Visible : Visibility.Collapsed;
        undoNotice.Visibility = preferences.LastDeletion == null ? Visibility.Collapsed : Visibility.Visible;
        deletion.Text = preferences.LastDeletion is { } removed ? UiText.Get("AiCommandsPanel_014",(UiNames.Prompt(removed.Prompt))): "";
        foreach (var prompt in filtered)
        {
            var row = new Grid { MinHeight = 52 }; row.ColumnDefinitions.Add(new() { Width = new(42) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            AutomationProperties.SetAutomationId(row, "AiCommandRow_" + prompt.Id);
            var icon = SettingsFeedback.IconButton("", UiText.Join(UiText.Get("AiCommandsPanel_015"), UiNames.Prompt(prompt)), () => ChooseIcon(prompt.Id));
            var iconPreview = new Grid { Width = 24, Height = 24 };
            iconPreview.Children.Add(new AiIconPresenter { IconId = AiIconCatalog.ForPrompt(prompt), Width = 22, Height = 22 });
            var iconHint = new Border { Child = ActionUi.Icon("edit", ActionUi.Accent, 10), Background = System.Windows.Media.Brushes.White,
                Width = 13, Height = 13, CornerRadius = new(3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Style = (Style)FindResource("CommandIconHint"), IsHitTestVisible = false };
            iconPreview.Children.Add(iconHint); icon.Content = iconPreview; icon.Padding = new(5); icon.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(icon, UiText.Join(UiText.Get("AiCommandsPanel_015"), UiNames.Prompt(prompt))); AutomationProperties.SetAutomationId(icon, "AiCommandIcon_" + prompt.Id); row.Children.Add(icon);
            SettingsFeedback.Hint(icon, UiText.Join(UiText.Get("AiCommandsPanel_015"), UiNames.Prompt(prompt))); SettingsFeedback.Quiet(icon);
            var text = new StackPanel { Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
            var name = ActionUi.Heading(UiNames.Prompt(prompt), 14); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            SettingsFeedback.Hint(name, UiNames.Prompt(prompt)); text.Children.Add(name); Grid.SetColumn(text, 1); row.Children.Add(text);
            var description = ActionUi.Text(UiNames.PromptDescription(prompt), 12, ActionUi.Muted);
            description.TextWrapping = TextWrapping.NoWrap; description.TextTrimming = TextTrimming.CharacterEllipsis;
            SettingsFeedback.Hint(description, prompt.Instruction); text.Children.Add(description);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pinned = settings.PinnedIds.Contains(prompt.Id);
            var pinLabel = UiText.Get(pinned ? "SettingsPolish_Unpin" : "SettingsPolish_Pin", UiNames.Prompt(prompt));
            var pin = new ToggleButton { Content = ActionUi.Icon("pin", pinned ? ActionUi.Accent : ActionUi.Muted, 16),
                IsChecked = pinned, Style = (Style)FindResource("AiCommandPin"), ToolTip = pinLabel };
            AutomationProperties.SetName(pin, pinLabel); AutomationProperties.SetAutomationId(pin, "AiCommandPin_" + prompt.Id);
            var resettingPin = false;
            RoutedEventHandler changePin = (_, _) =>
            {
                if (resettingPin) return;
                if (!preferences.TogglePin(prompt.Id, out var error))
                { resettingPin = true; pin.IsChecked = pinned; resettingPin = false; status.Text = error; }
                else status.Text = UiText.Get(pinned ? "SettingsFeedback_Unpinned" : "SettingsFeedback_Pinned", UiNames.Prompt(prompt));
            };
            pin.Checked += changePin; pin.Unchecked += changePin;
            buttons.Children.Add(pin);
            var edit = SettingsFeedback.IconButton("edit", UiText.Join(UiText.Get("AiCommandsPanel_019"), " · ", UiNames.Prompt(prompt)), () => preferences.OpenEditor(Window.GetWindow(this), preferences.Settings.Prompts.FirstOrDefault(p => p.Id == prompt.Id)));
            AutomationProperties.SetAutomationId(edit, "AiCommandEdit_" + prompt.Id); SettingsFeedback.Quiet(edit); buttons.Children.Add(edit);
            var delete = SettingsFeedback.IconButton("delete", UiText.Join(UiText.Get("AiCommandsPanel_021"), " · ", UiNames.Prompt(prompt)), () => DeletePrompt(prompt.Id));
            AutomationProperties.SetName(delete, UiText.Get("AiCommandsPanel_021") + " · " + UiNames.Prompt(prompt));
            AutomationProperties.SetAutomationId(delete, "AiCommandDelete_" + prompt.Id);
            SettingsFeedback.Hint(delete, UiText.Get("AiCommandsPanel_021") + " · " + UiNames.Prompt(prompt)); SettingsFeedback.Quiet(delete);
            delete.Resources["ControlHover"] = ActionUi.Brush("#FCE8E8"); delete.Resources["ControlAccent"] = System.Windows.Media.Brushes.Firebrick; buttons.Children.Add(delete);
            Grid.SetColumn(buttons, 2); row.Children.Add(buttons);
            var card = ActionUi.Card(row, new Thickness(10, 4, 4, 4)); card.Margin = new(0, 0, 0, 5); rows.Children.Add(card);
        }
        if (filtered.Count == 0) rows.Children.Add(ActionUi.Text(settings.Prompts.Length == 0 ? UiText.Get("AiCommandsPanel_022") : UiText.Get("AiCommandsPanel_023"), 13, ActionUi.Muted));
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
        status.Text = id != null && !VisiblePromptIds.Contains(id) ? UiText.Get("AiCommandsPanel_024") : UiText.Get("AiCommandsPanel_025"); return true;
    }
    private void RestoreDeleted() => RestoreDeletedCommand();
    private void ChooseIcon(string id)
    {
        var prompt = preferences.Settings.Prompts.FirstOrDefault(p => p.Id == id); if (prompt == null) return;
        var picker = new AiIconPickerWindow(AiIconCatalog.ForPrompt(prompt)) { Owner = Window.GetWindow(this) };
        picker.ShowDialog();
        if (picker.SelectedId is not { } icon) return;
        var latest = preferences.Settings.Prompts.FirstOrDefault(p => p.Id == id);
        if (latest == null) { status.Text = UiText.Get("AiCommandsPanel_026"); return; }
        if (!preferences.TrySavePrompt(latest with { Icon = icon }, false, out var error)) status.Text = error;
    }
    internal void Detach() { preferences.Changed -= Refresh; preferences.UndoChanged -= Render; }
}
