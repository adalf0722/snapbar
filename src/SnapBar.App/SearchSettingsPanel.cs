using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class SearchSettingsPanel : ContentControl, ISettingsDraft
{
    internal const string BingTemplate = "https://www.bing.com/search?q={query}";
    private readonly TextBox template;
    private readonly ComboBox provider;
    private readonly StackPanel customFields;
    private readonly TextBlock state, error;
    private string baseline = WebActionPolicy.DefaultSearchTemplate;
    private string customDraft = WebActionPolicy.DefaultSearchTemplate;
    private bool syncing;
    internal Func<string, bool>? SaveTemplate { get; set; }
    internal Action? RevealRequested { get; set; }
    public event Action<string>? SaveRequested;
    public event Action? ResetRequested;
    internal event Action? Changed;
    public string DraftName => UiText.Get("SearchSettingsPanel_000");
    public bool IsDirty => DraftTemplate != baseline || (provider.SelectedIndex != 2 && customDraft != BaselineCustom);
    private string BaselineCustom => baseline == WebActionPolicy.DefaultSearchTemplate || baseline == BingTemplate ? WebActionPolicy.DefaultSearchTemplate : baseline;
    internal string DraftTemplate => provider.SelectedIndex switch { 0 => WebActionPolicy.DefaultSearchTemplate, 1 => BingTemplate, _ => template.Text };

    public SearchSettingsPanel()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "SearchSettingsPanel");
        var panel = new DockPanel();
        var footer = new StackPanel { Margin = new(0, 16, 0, 0) };
        state = ActionUi.Text("", 12, ActionUi.Muted); footer.Children.Add(state);
        AutomationProperties.SetAutomationId(state, "SearchDirtyStatus");
        error = ActionUi.Text("", 12, System.Windows.Media.Brushes.Firebrick); footer.Children.Add(error);
        AutomationProperties.SetAutomationId(error, "SearchSettingsError");
        var buttons = new WrapPanel { Margin = new(0, 8, 0, 0) };
        var save = ActionUi.Button(UiText.Get("SearchSettingsPanel_001"), () => TrySave(), primary: true);
        AutomationProperties.SetAutomationId(save, "SaveSearchTemplate"); buttons.Children.Add(save);
        var discard = ActionUi.Button(UiText.Get("SearchSettingsPanel_002"), Discard); AutomationProperties.SetAutomationId(discard, "DiscardSearchTemplate"); buttons.Children.Add(discard);
        var reset = ActionUi.Button(UiText.Get("SearchSettingsPanel_003"), () =>
        {
            ShowTemplate(WebActionPolicy.DefaultSearchTemplate, setBaseline: false);
            if (SaveTemplate != null) TrySave(); else ResetRequested?.Invoke();
        });
        AutomationProperties.SetAutomationId(reset, "ResetSearchTemplate"); buttons.Children.Add(reset);
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var fields = new StackPanel(); fields.Children.Add(ActionUi.Heading(UiText.Get("SearchSettingsPanel_004"), 14));
        provider = new ComboBox { ItemsSource = new[] { "Google", "Bing", UiText.Get("SearchSettingsPanel_005") }, SelectedIndex = 0, Margin = new(0, 6, 0, 12) };
        AutomationProperties.SetAutomationId(provider, "SearchProvider"); AutomationProperties.SetName(provider, UiText.Get("SearchSettingsPanel_006")); fields.Children.Add(provider);
        customFields = new StackPanel { Visibility = Visibility.Collapsed };
        customFields.Children.Add(ActionUi.Heading(UiText.Get("SearchSettingsPanel_007"), 14));
        var note = ActionUi.Text(UiText.Get("SearchSettingsPanel_008"), 12, ActionUi.Muted); note.Margin = new(0, 5, 0, 8); customFields.Children.Add(note);
        template = ActionUi.Input(max: 2048); template.Margin = new(0, 0, 0, 12);
        AutomationProperties.SetAutomationId(template, "SearchUrlTemplate"); AutomationProperties.SetName(template, UiText.Get("SearchSettingsPanel_009"));
        customFields.Children.Add(template); fields.Children.Add(customFields);
        provider.SelectionChanged += (_, _) =>
        {
            if (syncing || UiText.IsRefreshing) return;
            customFields.Visibility = provider.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (provider.SelectedIndex == 2) template.Text = customDraft;
            UpdateState();
        };
        template.TextChanged += (_, _) => { if (!syncing && provider.SelectedIndex == 2) { customDraft = template.Text; UpdateState(); } };
        panel.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = panel;
    }

    public void ShowTemplate(string value) => ShowTemplate(value, true);
    private void ShowTemplate(string value, bool setBaseline)
    {
        syncing = true;
        try
        {
            if (setBaseline) baseline = value;
            customDraft = value == WebActionPolicy.DefaultSearchTemplate || value == BingTemplate ? WebActionPolicy.DefaultSearchTemplate : value;
            provider.SelectedIndex = value == WebActionPolicy.DefaultSearchTemplate ? 0 : value == BingTemplate ? 1 : 2;
            template.Text = customDraft; customFields.Visibility = provider.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { syncing = false; }
        UpdateState();
    }
    private void UpdateState() { state.Text = IsDirty ? UiText.Get("SearchSettingsPanel_010") : ""; Changed?.Invoke(); }
    internal void FocusEditor() => provider.Focus();
    public bool TrySave()
    {
        if (!WebActionPolicy.IsValidSearchTemplate(DraftTemplate)) { error.Text = UiText.Get("SearchSettingsPanel_011"); return false; }
        var value = DraftTemplate;
        if (SaveTemplate != null && !SaveTemplate(value)) { error.Text = UiText.Get("SearchSettingsPanel_012"); return false; }
        SaveRequested?.Invoke(value); ShowTemplate(value); error.Text = ""; state.Text = UiText.Get("SearchSettingsPanel_013"); return true;
    }
    public void Discard() { ShowTemplate(baseline); error.Text = ""; }
    public void Reveal() => RevealRequested?.Invoke();
}
