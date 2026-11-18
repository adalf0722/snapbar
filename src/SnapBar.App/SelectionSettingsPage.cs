using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SnapBar.App;

internal sealed partial class SelectionSettingsPage : ContentControl
{
    internal CheckBox AutomaticToggle { get; }
    private readonly StackPanel toolbarRows = new();

    internal SelectionSettingsPage(bool automatic, Action toggle)
    {
        ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var panel = new StackPanel();
        var introduction = Description(UiText.Get("SelectionSettings_Description"));
        introduction.Margin = new(2, 0, 0, 16);
        panel.Children.Add(introduction);
        var detection = new DockPanel();
        AutomaticToggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = automatic,
            VerticalAlignment = VerticalAlignment.Center, Margin = new(16, 0, 0, 0),
            ToolTip = UiText.Get("GeneralSettingsPage_008") };
        Identify(AutomaticToggle, "AutomaticDetectionToggle", UiText.Get("GeneralSettingsPage_009"));
        AutomaticToggle.Click += (_, _) => toggle();
        DockPanel.SetDock(AutomaticToggle, Dock.Right); detection.Children.Add(AutomaticToggle);
        var title = new StackPanel();
        title.Children.Add(ActionUi.Heading(UiText.Get("GeneralSettingsPage_010"), 16));
        title.Children.Add(Description(UiText.Get("GeneralSettingsPage_011")));
        detection.Children.Add(title);
        toolbarRows.Children.Add(SettingRow(detection));
        panel.Children.Add(Card(toolbarRows));
        copyRows = new StackPanel();
        panel.Children.Add(Card(copyRows));
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Identify(scroll, "SelectionSettingsContent", UiText.Get("General_GroupSelection"));
        Content = scroll;
    }

    private readonly StackPanel copyRows;
    private static Border Card(UIElement content)
    {
        var card = ActionUi.Card(content, new(0));
        card.Margin = new(0, 0, 0, 16);
        return card;
    }
    private static void Identify(DependencyObject control, string id, string name)
    { AutomationProperties.SetAutomationId(control, id); AutomationProperties.SetName(control, name); }
    private static TextBlock Description(string text) => ActionUi.Text(text, 13, ActionUi.Brush("#4F5968"));
    private static Border SettingRow(UIElement content) => new() { Padding = new(18, 14, 18, 14), Child = content };
}
