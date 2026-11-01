using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SnapBar.App;

internal sealed class GeneralSettingsPage : ContentControl
{
    internal CheckBox AutomaticToggle { get; }
    private readonly ScrollViewer scroll;

    internal GeneralSettingsPage(bool automatic, Action toggle)
    {
        ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var panel = new StackPanel();
        var detection = new DockPanel();
        AutomaticToggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = automatic,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "自動偵測選取文字" };
        AutomationProperties.SetName(AutomaticToggle, "自動偵測選取文字"); AutomationProperties.SetAutomationId(AutomaticToggle, "AutomaticDetectionToggle");
        AutomaticToggle.Click += (_, _) => toggle(); DockPanel.SetDock(AutomaticToggle, Dock.Right); detection.Children.Add(AutomaticToggle);
        var title = new StackPanel(); title.Children.Add(ActionUi.Heading("自動顯示動作列", 16));
        title.Children.Add(ActionUi.Text("拖曳或雙擊選字後，顯示動作列。", 13, ActionUi.Muted));
        detection.Children.Add(title);
        var detectionCard = ActionUi.Card(detection); detectionCard.Margin = new(0, 0, 0, 14); panel.Children.Add(detectionCard);
        var shortcut = new DockPanel();
        var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(keys, "GeneralShortcutKeys"); AutomationProperties.SetName(keys, "Ctrl + Alt + V");
        foreach (var key in new[] { "Ctrl", "Alt", "V" })
        {
            if (keys.Children.Count > 0) { var plus = ActionUi.Text("+", 12, ActionUi.Muted); plus.Margin = new(6, 0, 6, 0); keys.Children.Add(plus); }
            keys.Children.Add(new Border { Child = ActionUi.Text(key, 13), Background = ActionUi.Canvas, BorderBrush = ActionUi.Line,
                BorderThickness = new(1, 1, 1, 2), CornerRadius = new(4), Padding = new(8, 5, 8, 5) });
        }
        DockPanel.SetDock(keys, Dock.Right); shortcut.Children.Add(keys);
        var shortcutTitle = new StackPanel(); shortcutTitle.Children.Add(ActionUi.Heading("快捷鍵", 16));
        shortcutTitle.Children.Add(ActionUi.Text("選取文字後，按快捷鍵叫出動作列。", 13, ActionUi.Muted)); shortcut.Children.Add(shortcutTitle);
        panel.Children.Add(ActionUi.Card(shortcut));
        scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(scroll, "GeneralSettingsContent"); Content = scroll;
        SizeChanged += (_, _) => SetCompact(ActualHeight < 420 || ActualWidth < 580);
    }
    internal void SetCompact(bool compact)
    {
        scroll.VerticalScrollBarVisibility = compact ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }
}
