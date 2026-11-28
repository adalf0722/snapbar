using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed partial class AiPanelWindow
{
    private string? runningPromptId;
    private string commandView = "All";
    private readonly Dictionary<long, string?> versionPromptIds = [];

    private void RenderCommands()
    {
        if (closed) return;
        var focusedId = commandRows.IsKeyboardFocusWithin && Keyboard.FocusedElement is DependencyObject focused
            ? FindPromptId(focused) : null;
        direct.Visibility = string.IsNullOrWhiteSpace(input.Text) ? Visibility.Collapsed : Visibility.Visible;
        direct.IsEnabled = !Session.Busy; placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        placeholder.Text = language.Get("AiPanel_InstructionPlaceholder");
        direct.ToolTip = input.Text;
        commandRows.Children.Clear(); commandRows.RowDefinitions.Clear(); commandButtons.Clear();
        var settings = preferences.Settings; var chosen = categoryValues.ElementAtOrDefault(category.SelectedIndex) ?? "";
        deletionNotice.Visibility = preferences.LastDeletion == null ? Visibility.Collapsed : Visibility.Visible;
        deletionText.Text = preferences.LastDeletion is { } deleted ? language.Get("AiPanelWindow_Commands_002",(UiNames.Prompt(deleted.Prompt, language))): "";
        commandViews.Visibility = settings.Prompts.Length > 12 || commandView != "All" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var viewButton in commandViewButtons)
        {
            viewButton.Background = (string)viewButton.Tag == commandView ? ActionUi.Pale : Brushes.Transparent;
            AutomationProperties.SetItemStatus(viewButton, (string)viewButton.Tag == commandView ? language.Get("AiPanel_SelectedView") : "");
        }
        var searching = input.Text.Trim().Length > 0;
        var items = UiNames.SearchPrompts(settings, input.Text, chosen, recentFirst: true, language: language)
            .Where(p => searching || commandView == "All" || (commandView == "Pinned" ? settings.PinnedIds : settings.RecentIds).Contains(p.Id)).ToList();
        for (var index = 0; index < items.Count; index++)
        {
            var prompt = items[index];
            commandRows.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var active = Session.Busy ? runningPromptId == prompt.Id : Session.Current != null && versionPromptIds.GetValueOrDefault(Session.Current.Id) == prompt.Id;
            var tile = new Border { Background = active ? ActionUi.Pale : Brushes.Transparent, CornerRadius = new(6),
                BorderBrush = active ? ActionUi.Accent : Brushes.Transparent, BorderThickness = new(active ? 2 : 0, 0, 0, 0), Margin = new(0, 0, 0, 2), Tag = prompt.Id };
            tile.IsKeyboardFocusWithinChanged += (_, _) => tile.Background = active || tile.IsKeyboardFocusWithin ? ActionUi.Pale : Brushes.Transparent;
            Grid.SetRow(tile, index);
            var surface = new Grid(); surface.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            surface.ColumnDefinitions.Add(new() { Width = new(26) }); surface.ColumnDefinitions.Add(new() { Width = new(26) }); tile.Child = surface;
            var button = ActionUi.Button(UiNames.Prompt(prompt, language), () => _ = SubmitAsync(prompt.Instruction, prompt.Id));
            button.Style = (Style)FindResource("AiPanelCommandButton");
            button.BorderThickness = new(0); button.Margin = new(0); button.IsEnabled = !Session.Busy;
            button.Background = Brushes.Transparent; button.Padding = new(8, 6, 4, 6); button.MinHeight = 40;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var content = new DockPanel();
            var icon = new AiIconPresenter { IconId = AiIconCatalog.ForPrompt(prompt), Width = 16, Height = 16, Margin = new(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(icon, Dock.Left); content.Children.Add(icon);
            var title = ActionUi.Text(Session.Busy && runningPromptId == prompt.Id ? language.Get("AiPanelWindow_Commands_004") : UiNames.Prompt(prompt, language), 13);
            title.TextAlignment = TextAlignment.Left; title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.TextWrapping = TextWrapping.NoWrap; title.LineHeight = 18; title.MaxHeight = 24; content.Children.Add(title); button.Content = content;
            AutomationProperties.SetItemStatus(button, active ? language.Get("AiPanel_ActiveCommand") : "");
            button.ToolTip = UiNames.Prompt(prompt, language); button.Tag = prompt.Id; AutomationProperties.SetAutomationId(button, "AiPrompt_" + prompt.Id);
            ToolTipService.SetInitialShowDelay(button, 700);
            button.ToolTipOpening += (_, e) =>
            {
                var text = new FormattedText(title.Text, title.Language.GetSpecificCulture(), title.FlowDirection,
                    new Typeface(title.FontFamily, title.FontStyle, title.FontWeight, title.FontStretch), title.FontSize,
                    title.Foreground, VisualTreeHelper.GetDpi(title).PixelsPerDip);
                e.Handled = text.WidthIncludingTrailingWhitespace <= title.ActualWidth;
            };
            AutomationProperties.SetHelpText(button, language.Get("AiPanelWindow_Commands_005"));
            button.PreviewKeyDown += CommandKey;
            button.GotKeyboardFocus += (_, _) => button.BringIntoView();
            commandButtons.Add(button); surface.Children.Add(button);

            Button Tool(string iconName, Action click, HorizontalAlignment alignment)
            {
                var tool = QuietButton("", click, iconName); tool.Style = (Style)FindResource("AiPanelRowTool");
                tool.Width = 24; tool.Height = tool.MinHeight = 24; tool.Padding = new(4); tool.Margin = new(2);
                tool.VerticalAlignment = VerticalAlignment.Center; tool.HorizontalAlignment = alignment; return tool;
            }
            var pinned = settings.PinnedIds.Contains(prompt.Id);
            var pin = Tool("pin", () => TogglePin(prompt), HorizontalAlignment.Left);
            pin.Tag = pinned ? "Pinned" : null; pin.ToolTip = pinned ? language.Get("AiPanelWindow_Commands_006") : language.Get("AiPanelWindow_Commands_007");
            AutomationProperties.SetAutomationId(pin, "AiPin_" + prompt.Id); AutomationProperties.SetName(pin, UiText.Join(pin.ToolTip + " ", UiNames.Prompt(prompt, language)));
            Grid.SetColumn(pin, 1); surface.Children.Add(pin);
            var options = Tool("more", () => { }, HorizontalAlignment.Right); options.ToolTip = language.Get("AiPanelWindow_Commands_008");
            AutomationProperties.SetAutomationId(options, "AiOptions_" + prompt.Id); AutomationProperties.SetName(options, UiText.Join(language.Get("AiPanelWindow_Commands_009"), UiNames.Prompt(prompt, language)));
            var menu = new ContextMenu(); var edit = new MenuItem { Header = language.Get("AiPanelWindow_Commands_010") }; edit.Click += (_, _) => EditPrompt(prompt);
            var delete = new MenuItem { Header = language.Get("AiPanelWindow_Commands_011") }; delete.Click += (_, _) => DeletePrompt(prompt);
            menu.Items.Add(edit); menu.Items.Add(delete); options.ContextMenu = menu;
            options.Click += (_, _) => { menu.PlacementTarget = options; menu.IsOpen = true; }; Grid.SetColumn(options, 2); surface.Children.Add(options);
            commandRows.Children.Add(tile);
        }
        if (items.Count == 0)
        {
            var empty = ActionUi.Text(input.Text.Length > 0 ? language.Get("AiPanelWindow_Commands_012") : language.Get("AiPanelWindow_Commands_013"), 12, ActionUi.Muted);
            Grid.SetColumnSpan(empty, 3); commandRows.Children.Add(empty);
        }
        count.Text = UiText.Join((input.Text.Trim().Length > 0 ? language.Get("AiPanelWindow_Commands_014") : ""), language.Get("AiPanelWindow_Commands_015",(items.Count),(settings.Prompts.Length)));
        count.Visibility = searching || chosen.Length > 0 || settings.Prompts.Length > 12 || commandView != "All" ? Visibility.Visible : Visibility.Collapsed;
        if (focusedId != null && !Session.Busy)
        {
            var restore = commandButtons.FirstOrDefault(b => (string)b.Tag == focusedId);
            if (restore != null) restore.Focus(); else input.Focus();
        }
    }

    private string? FindPromptId(DependencyObject element)
    {
        for (var current = element; current != null && current != commandRows; current = VisualTreeHelper.GetParent(current))
            if (current is Border { Tag: string id }) return id;
        return null;
    }

    private void CommandKey(object sender, KeyEventArgs e)
    {
        if (composing || e.Key == Key.ImeProcessed || Keyboard.Modifiers != ModifierKeys.None || Session.Busy) return;
        var button = (Button)sender; var index = commandButtons.IndexOf(button);
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            e.Handled = true;
            if (e.Key == Key.Up && index == 0) { input.Focus(); return; }
            var target = e.Key switch
            {
                Key.Up => index - 1,
                Key.Down => Math.Min(index + 1, commandButtons.Count - 1),
                _ => index
            };
            commandButtons[target].Focus();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var prompt = preferences.Settings.Prompts.FirstOrDefault(p => p.Id == (string)button.Tag);
            if (prompt != null) _ = SubmitAsync(prompt.Instruction, prompt.Id);
        }
    }
}
