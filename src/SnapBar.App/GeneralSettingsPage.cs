using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class GeneralSettingsPage : ContentControl
{
    internal CheckBox AutomaticToggle { get; }
    private readonly StackPanel preview = new() { Orientation = Orientation.Horizontal };
    private readonly Border previewShell = new();
    private readonly FrameworkElement previewSection;
    private readonly ScrollViewer scroll;
    private readonly TextBlock help;
    private readonly Expander expandedHelp;

    internal GeneralSettingsPage(bool automatic, Action toggle, Action manage)
    {
        ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var panel = new StackPanel();
        var detection = new DockPanel();
        AutomaticToggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = automatic,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "自動偵測選取文字" };
        AutomationProperties.SetName(AutomaticToggle, "自動偵測選取文字"); AutomationProperties.SetAutomationId(AutomaticToggle, "AutomaticDetectionToggle");
        AutomaticToggle.Click += (_, _) => toggle(); DockPanel.SetDock(AutomaticToggle, Dock.Right); detection.Children.Add(AutomaticToggle);
        var title = new StackPanel(); title.Children.Add(ActionUi.Heading("自動偵測", 16));
        title.Children.Add(ActionUi.Text("拖曳或雙擊選字後，顯示動作列。", 13, ActionUi.Muted)); detection.Children.Add(title);
        var detectionCard = ActionUi.Card(detection); detectionCard.Margin = new(0, 0, 0, 14); panel.Children.Add(detectionCard);
        var shortcut = new DockPanel { Margin = new(0, 2, 0, 16) };
        var keys = new Border { Child = ActionUi.Text("Ctrl + Alt + V", 13), Background = ActionUi.Canvas, BorderBrush = ActionUi.Line,
            BorderThickness = new(1), CornerRadius = new(6), Padding = new(10, 6, 10, 6) };
        DockPanel.SetDock(keys, Dock.Right); shortcut.Children.Add(keys); shortcut.Children.Add(ActionUi.Text("隨時用快捷鍵叫出動作列", 13, ActionUi.Muted)); panel.Children.Add(shortcut);
        var section = new StackPanel(); section.Children.Add(ActionUi.Heading("你的動作列", 16));
        previewShell.Child = preview; previewShell.CornerRadius = new(11); previewShell.Padding = new(6); previewShell.BorderThickness = new(1);
        var previewScroll = new ScrollViewer { Content = previewShell, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(0, 10, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 680 };
        AutomationProperties.SetAutomationId(previewScroll, "GeneralActionPreview"); section.Children.Add(previewScroll);
        section.Children.Add(ActionUi.Text("反映目前排列與外觀；正式顯示依選取情境決定。此預覽不執行動作。", 12, ActionUi.Muted));
        previewSection = section; panel.Children.Add(section);
        var manageButton = ActionUi.Button("管理動作", manage, "settings"); manageButton.Margin = new(0, 12, 0, 16); manageButton.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(manageButton, "ManageActions"); panel.Children.Add(manageButton);
        help = ActionUi.Text("關閉設定視窗後，SnapBar 會在背景待命。按系統匣圖示可返回目前的設定頁與未儲存內容。", 13, ActionUi.Muted); panel.Children.Add(help);
        expandedHelp = new Expander { Header = "背景待命說明", Content = ActionUi.Text(help.Text, 12, ActionUi.Muted), Visibility = Visibility.Collapsed }; panel.Children.Add(expandedHelp);
        scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(scroll, "GeneralSettingsContent"); Content = scroll;
        SizeChanged += (_, _) => SetCompact(ActualHeight < 420 || ActualWidth < 580);
    }
    internal void SetCompact(bool compact)
    {
        previewSection.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        help.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        expandedHelp.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        scroll.VerticalScrollBarVisibility = compact ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }
    internal void ShowPreview(ActionSettings settings)
    {
        settings = ActionCatalog.Normalize(settings); preview.Children.Clear(); var dark = settings.Theme == "Dark";
        previewShell.Background = dark ? ActionUi.DarkSurface : Brushes.White; previewShell.BorderBrush = dark ? ActionUi.DarkLine : ActionUi.Line;
        foreach (var action in ActionCatalog.GetPinned(settings))
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Height = 36, Margin = new(8, 0, 8, 0) };
            item.Children.Add(ActionUi.Icon(action.Icon, dark ? Brushes.White : ActionUi.Ink, 17));
            if (settings.ShowLabels) { var label = ActionUi.Text(action.Name, 13, dark ? Brushes.White : ActionUi.Ink); label.Margin = new(6, 0, 0, 0); item.Children.Add(label); }
            preview.Children.Add(item);
        }
        var more = ActionUi.Text("⋯", 18, dark ? Brushes.White : ActionUi.Ink); more.Margin = new(10, 0, 10, 0); preview.Children.Add(more);
    }
}
