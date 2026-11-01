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

    private void RenderCommands()
    {
        if (closed) return;
        var focusedId = commandRows.IsKeyboardFocusWithin && Keyboard.FocusedElement is DependencyObject focused
            ? FindPromptId(focused) : null;
        direct.Visibility = string.IsNullOrWhiteSpace(input.Text) ? Visibility.Collapsed : Visibility.Visible;
        direct.IsEnabled = !Session.Busy; placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        placeholder.Text = Session.BasisId == null ? "搜尋指令，或說明怎麼處理原文…" : "接著怎麼改？也能搜尋指令…";
        direct.ToolTip = input.Text;
        commandRows.Children.Clear(); commandRows.RowDefinitions.Clear(); commandButtons.Clear();
        var settings = preferences.Settings; var chosen = (string?)category.SelectedItem;
        var items = AiPolicy.Search(settings, input.Text, chosen == "全部分類" ? "" : chosen ?? "");
        for (var index = 0; index < items.Count; index++)
        {
            var prompt = items[index];
            if (index % 2 == 0) commandRows.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var tile = new Border { Style = (Style)FindResource("AiPromptRow"), Margin = new(0, 0, 0, 8), Tag = prompt.Id };
            Grid.SetRow(tile, index / 2); Grid.SetColumn(tile, index % 2 * 2);
            var surface = new Grid(); tile.Child = surface;
            var button = ActionUi.Button(prompt.Name, () => _ = SubmitAsync(prompt.Instruction, prompt.Id));
            button.BorderThickness = new(0); button.Margin = new(0); button.IsEnabled = !Session.Busy;
            button.Background = Brushes.Transparent; button.Padding = new(7, 9, 7, 8); button.MinHeight = 76;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var content = new Grid(); content.RowDefinitions.Add(new() { Height = new(18) }); content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            var icon = new AiIconPresenter { IconId = AiIconCatalog.ForPrompt(prompt), Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center }; content.Children.Add(icon);
            var title = ActionUi.Text(Session.Busy && runningPromptId == prompt.Id ? "處理中…" : prompt.Name, 13);
            title.TextAlignment = TextAlignment.Center; title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.LineHeight = 18; title.MaxHeight = 36; title.Margin = new(0, 5, 0, 0); Grid.SetRow(title, 1); content.Children.Add(title); button.Content = content;
            button.ToolTip = prompt.Name; button.Tag = prompt.Id; AutomationProperties.SetAutomationId(button, "AiPrompt_" + prompt.Id);
            AutomationProperties.SetHelpText(button, "方向鍵選擇指令，Enter 執行，Esc 返回輸入。");
            button.PreviewKeyDown += CommandKey;
            button.GotKeyboardFocus += (_, _) => button.BringIntoView();
            commandButtons.Add(button); surface.Children.Add(button);

            Button Tool(string iconName, Action click, HorizontalAlignment alignment)
            {
                var tool = QuietButton("", click, iconName); tool.Style = (Style)FindResource("AiRowTool");
                tool.Width = 24; tool.Height = tool.MinHeight = 24; tool.Padding = new(4); tool.Margin = new(2);
                tool.VerticalAlignment = VerticalAlignment.Top; tool.HorizontalAlignment = alignment; return tool;
            }
            var pinned = settings.PinnedIds.Contains(prompt.Id);
            var pin = Tool("pin", () => TogglePin(prompt), HorizontalAlignment.Left);
            pin.Tag = pinned ? "Pinned" : null; pin.ToolTip = pinned ? "取消釘選" : "釘選";
            AutomationProperties.SetAutomationId(pin, "AiPin_" + prompt.Id); AutomationProperties.SetName(pin, pin.ToolTip + " " + prompt.Name);
            surface.Children.Add(pin);
            var options = Tool("more", () => { }, HorizontalAlignment.Right); options.ToolTip = "指令選項";
            AutomationProperties.SetAutomationId(options, "AiOptions_" + prompt.Id); AutomationProperties.SetName(options, "指令選項 " + prompt.Name);
            var menu = new ContextMenu(); var edit = new MenuItem { Header = "編輯指令" }; edit.Click += (_, _) => EditPrompt(prompt);
            var delete = new MenuItem { Header = "刪除指令" }; delete.Click += (_, _) => DeletePrompt(prompt);
            menu.Items.Add(edit); menu.Items.Add(delete); options.ContextMenu = menu;
            options.Click += (_, _) => { menu.PlacementTarget = options; menu.IsOpen = true; }; surface.Children.Add(options);
            commandRows.Children.Add(tile);
        }
        if (items.Count == 0)
        {
            var empty = ActionUi.Text(input.Text.Length > 0 ? "沒有符合的已存指令，仍可直接執行輸入內容。" : "這個分類尚無指令。", 12, ActionUi.Muted);
            Grid.SetColumnSpan(empty, 3); commandRows.Children.Add(empty);
        }
        count.Text = (input.Text.Trim().Length > 0 ? "搜尋全部 · " : "") + $"{items.Count} / {settings.Prompts.Length} 個指令";
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
            if (e.Key == Key.Up && index < 2) { input.Focus(); return; }
            var target = e.Key switch
            {
                Key.Left when index % 2 == 1 => index - 1,
                Key.Right when index % 2 == 0 && index + 1 < commandButtons.Count => index + 1,
                Key.Up => index - 2,
                Key.Down when index / 2 < (commandButtons.Count - 1) / 2 => Math.Min(index + 2, commandButtons.Count - 1),
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
