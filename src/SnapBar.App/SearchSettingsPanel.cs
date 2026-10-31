using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class SearchSettingsPanel : Expander
{
    internal const string BingTemplate = "https://www.bing.com/search?q={query}";
    private readonly TextBox template;
    private readonly ComboBox provider;
    private readonly StackPanel customFields;
    private string customDraft = WebActionPolicy.DefaultSearchTemplate;
    private bool syncing;
    public event Action<string>? SaveRequested;
    public event Action? ResetRequested;

    public SearchSettingsPanel()
    {
        SetResourceReference(StyleProperty, typeof(Expander));
        Header = "搜尋";
        AutomationProperties.SetAutomationId(this, "SearchSettingsPanel");
        var panel = new StackPanel { Margin = new(0, 10, 0, 0) };
        var label = ActionUi.Text("搜尋引擎", 13, ActionUi.Muted); label.Margin = new(0, 0, 0, 6); panel.Children.Add(label);
        provider = new ComboBox { ItemsSource = new[] { "Google", "Bing", "自訂" }, SelectedIndex = 0, Margin = new(0, 0, 0, 12) };
        AutomationProperties.SetAutomationId(provider, "SearchProvider"); AutomationProperties.SetName(provider, "搜尋引擎"); panel.Children.Add(provider);
        customFields = new StackPanel { Visibility = Visibility.Collapsed };
        var note = ActionUi.Text("將 {query} 放在網址參數值中，選取文字會自動編碼。", 12, ActionUi.Muted); note.Margin = new(0, 0, 0, 8); customFields.Children.Add(note);
        template = ActionUi.Input(max: 2048); template.Margin = new(0, 0, 0, 12);
        AutomationProperties.SetAutomationId(template, "SearchUrlTemplate");
        AutomationProperties.SetName(template, "搜尋網址範本");
        customFields.Children.Add(template); panel.Children.Add(customFields);
        provider.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            customFields.Visibility = provider.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (provider.SelectedIndex == 2) template.Text = customDraft;
        };
        template.TextChanged += (_, _) => { if (!syncing && provider.SelectedIndex == 2) customDraft = template.Text; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var save = ActionUi.Button("儲存搜尋設定", () => SaveRequested?.Invoke(provider.SelectedIndex switch
        { 0 => WebActionPolicy.DefaultSearchTemplate, 1 => BingTemplate, _ => template.Text }), primary: true);
        AutomationProperties.SetAutomationId(save, "SaveSearchTemplate");
        var reset = ActionUi.Button("恢復 Google 預設", () => ResetRequested?.Invoke());
        AutomationProperties.SetAutomationId(reset, "ResetSearchTemplate");
        buttons.Children.Add(save); buttons.Children.Add(reset);
        panel.Children.Add(buttons); Content = panel;
    }

    public void ShowTemplate(string value)
    {
        syncing = true;
        try
        {
            provider.SelectedIndex = value == WebActionPolicy.DefaultSearchTemplate ? 0 : value == BingTemplate ? 1 : 2;
            if (provider.SelectedIndex == 2) customDraft = value;
            template.Text = value; customFields.Visibility = provider.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { syncing = false; }
    }
}
