using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapBar.App;

internal sealed class SearchSettingsPanel : Expander
{
    private readonly TextBox template;
    public event Action<string>? SaveRequested;
    public event Action? ResetRequested;

    public SearchSettingsPanel()
    {
        Header = "搜尋設定"; Margin = new(0, 0, 0, 16);
        AutomationProperties.SetAutomationId(this, "SearchSettingsPanel");
        var panel = new StackPanel { Margin = new(0, 10, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "將 {query} 放在搜尋網址的參數值中，選取文字會自動編碼。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new(0, 0, 0, 8) });
        template = new TextBox { MaxLength = 2048, Padding = new(8), Margin = new(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(template, "SearchUrlTemplate");
        AutomationProperties.SetName(template, "搜尋網址範本");
        panel.Children.Add(template);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var save = new Button { Content = "儲存搜尋網址", Padding = new(12, 6, 12, 6), Margin = new(0, 0, 10, 0) };
        AutomationProperties.SetAutomationId(save, "SaveSearchTemplate");
        save.Click += (_, _) => SaveRequested?.Invoke(template.Text);
        var reset = new Button { Content = "恢復 Google 預設", Padding = new(12, 6, 12, 6) };
        AutomationProperties.SetAutomationId(reset, "ResetSearchTemplate");
        reset.Click += (_, _) => ResetRequested?.Invoke();
        buttons.Children.Add(save); buttons.Children.Add(reset);
        panel.Children.Add(buttons); Content = panel;
    }

    public void ShowTemplate(string value) => template.Text = value;
}
