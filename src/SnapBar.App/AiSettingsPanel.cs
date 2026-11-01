using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class AiSettingsPanel : ContentControl, ISettingsDraft
{
    private readonly AiPreferences preferences;
    private readonly ComboBox profiles;
    private readonly TextBox endpoint, model;
    private readonly PasswordBox key;
    private readonly CheckBox removeKey;
    private readonly TextBlock hint, keyStatus, state;
    private List<AiProviderProfile> drafts;
    private AiProviderProfile[] baseline;
    private string baselineSelected;
    private readonly Dictionary<string, (AiProviderProfile Profile, string? Key)> keyChanges = [];
    private int selected = -1;
    private bool syncing, saving;
    internal Action? RevealRequested { get; set; }
    internal event Action? Saved;
    public string DraftName => HasPendingKeyChanges ? "AI 服務（包含金鑰變更）" : "AI 服務";
    internal bool HasPendingKeyChanges => key.Password.Length > 0 || removeKey.IsChecked == true
        || keyChanges.Keys.Any(scope => selected < 0 || scope != Scope(CurrentDraft()));
    public bool IsDirty => selected >= 0 && (HasPendingKeyChanges || drafts[selected].Id != baselineSelected
        || !drafts.Select((p, i) => i == selected ? CurrentDraft() : p).SequenceEqual(baseline));

    public AiSettingsPanel(AiPreferences preferences)
    {
        this.preferences = preferences;
        baseline = preferences.Settings.Profiles.ToArray(); baselineSelected = preferences.Settings.SelectedProfileId;
        drafts = baseline.ToList(); ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "AiSettingsPanel");
        var body = new DockPanel();
        var footer = new StackPanel { Margin = new(0, 12, 0, 0) };
        state = ActionUi.Text("", 12, ActionUi.Muted); AutomationProperties.SetAutomationId(state, "AiDirtyStatus"); footer.Children.Add(state);
        hint = ActionUi.Text("", 12, System.Windows.Media.Brushes.Firebrick); AutomationProperties.SetAutomationId(hint, "AiSettingsError"); footer.Children.Add(hint);
        var buttons = new WrapPanel { Margin = new(0, 8, 0, 0) };
        var save = ActionUi.Button("儲存 AI 設定", () => TrySave(), primary: true); AutomationProperties.SetAutomationId(save, "SaveAiSettings"); buttons.Children.Add(save);
        var discard = ActionUi.Button("捨棄變更", Discard); AutomationProperties.SetAutomationId(discard, "DiscardAiSettings"); buttons.Children.Add(discard);
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var fields = new StackPanel(); fields.Children.Add(ActionUi.Text("選字只在執行指令後傳送至你設定的服務。", 13, ActionUi.Muted));
        void Label(string text) { var label = ActionUi.Heading(text, 14); label.Margin = new(0, 12, 0, 5); fields.Children.Add(label); }
        Label("服務"); profiles = new ComboBox { ItemsSource = drafts.Select(p => p.Name).ToArray(), MinHeight = 36 };
        AutomationProperties.SetAutomationId(profiles, "AiProvider"); AutomationProperties.SetName(profiles, "AI 服務"); fields.Children.Add(profiles);
        Label("完整 API 網址"); endpoint = ActionUi.Input("", 2048); AutomationProperties.SetAutomationId(endpoint, "AiEndpoint"); fields.Children.Add(endpoint);
        fields.Children.Add(ActionUi.Text("OpenAI 使用 /v1/responses；相容服務使用完整 /chat/completions 網址。", 12, ActionUi.Muted));
        Label("模型 ID"); model = ActionUi.Input("", 128); fields.Children.Add(model); AutomationProperties.SetAutomationId(model, "AiModel");
        Label("API Key"); key = new PasswordBox { MaxLength = 8192, Padding = new(10, 8, 10, 8), MinHeight = 36 };
        AutomationProperties.SetAutomationId(key, "AiApiKey"); AutomationProperties.SetName(key, "API Key"); fields.Children.Add(key);
        keyStatus = ActionUi.Text("", 12, ActionUi.Muted); AutomationProperties.SetAutomationId(keyStatus, "AiKeyStatus"); fields.Children.Add(keyStatus);
        fields.Children.Add(ActionUi.Text("留白保留已存金鑰。金鑰依服務與網址分開保護；本機相容服務可不填。", 12, ActionUi.Muted));
        removeKey = new CheckBox { Content = "移除此網址已保存的 API Key", Margin = new(0, 12, 0, 0) };
        AutomationProperties.SetAutomationId(removeKey, "AiRemoveKey"); fields.Children.Add(removeKey);
        profiles.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            CaptureDraft(); selected = profiles.SelectedIndex; ShowDraft(); UpdateState();
        };
        endpoint.TextChanged += (_, _) => { if (!syncing) { ShowKeyStatus(); UpdateState(); } };
        model.TextChanged += (_, _) => { if (!syncing) UpdateState(); };
        key.PasswordChanged += (_, _) => { if (!syncing) UpdateState(); };
        removeKey.Checked += (_, _) => { if (!syncing) UpdateState(); };
        removeKey.Unchecked += (_, _) => { if (!syncing) UpdateState(); };
        body.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = body;
        profiles.SelectedIndex = Math.Max(0, drafts.FindIndex(p => p.Id == baselineSelected));
        preferences.Changed += OnPreferencesChanged; preferences.RegisterDraft(this);
    }

    private static string Scope(AiProviderProfile profile) => profile.Id + "|" + profile.Protocol + "|" + profile.Endpoint;
    private AiProviderProfile CurrentDraft() => drafts[selected] with { Endpoint = endpoint.Text.Trim(), Model = model.Text.Trim() };
    private void CaptureDraft()
    {
        if (selected < 0) return;
        var profile = CurrentDraft(); drafts[selected] = profile;
        if (removeKey.IsChecked == true || key.Password.Length > 0)
            keyChanges[Scope(profile)] = (profile, removeKey.IsChecked == true ? null : key.Password);
        else keyChanges.Remove(Scope(profile));
    }
    private void ShowDraft()
    {
        if (selected < 0) return;
        syncing = true;
        try
        {
            var profile = drafts[selected]; endpoint.Text = profile.Endpoint; model.Text = profile.Model;
            key.Clear(); removeKey.IsChecked = false;
            if (keyChanges.TryGetValue(Scope(profile), out var pending))
            { key.Password = pending.Key ?? ""; removeKey.IsChecked = pending.Key == null; }
        }
        finally { syncing = false; }
        ShowKeyStatus();
    }
    private void ShowKeyStatus()
    {
        if (selected < 0) return;
        keyStatus.Text = preferences.Secrets.Get(CurrentDraft()) == null ? "此網址尚未保存金鑰。" : "此網址已有加密保存的金鑰。";
    }
    private void UpdateState() => state.Text = IsDirty ? "尚未儲存" : "";
    private void OnPreferencesChanged() { if (!saving && !IsDirty) Discard(); }
    public bool TrySave()
    {
        CaptureDraft(); var current = drafts[selected];
        if (AiPolicy.ValidateProfile(current) is { } validation) { hint.Text = validation; return false; }
        saving = true;
        try
        {
            var settings = preferences.Settings with { Profiles = drafts.ToArray(), SelectedProfileId = current.Id };
            if (!preferences.TrySave(settings, out var error)) { hint.Text = error; return false; }
            baseline = preferences.Settings.Profiles.ToArray(); baselineSelected = preferences.Settings.SelectedProfileId;
            foreach (var (scope, change) in keyChanges.ToArray())
            {
                try { preferences.Secrets.Set(change.Profile, change.Key); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { hint.Text = "服務設定已保存，但金鑰無法保存。請檢查權限後重試。"; UpdateState(); return false; }
                keyChanges.Remove(scope);
                if (scope == Scope(CurrentDraft()))
                {
                    syncing = true; key.Clear(); removeKey.IsChecked = false; syncing = false;
                }
            }
            key.Clear(); keyChanges.Clear(); removeKey.IsChecked = false;
            hint.Text = ""; state.Text = "已儲存"; ShowKeyStatus();
            preferences.NotifyChanged(); Saved?.Invoke(); return true;
        }
        finally { saving = false; }
    }
    public void Discard()
    {
        syncing = true;
        try
        {
            key.Clear(); keyChanges.Clear(); removeKey.IsChecked = false;
            baseline = preferences.Settings.Profiles.ToArray(); baselineSelected = preferences.Settings.SelectedProfileId;
            drafts = baseline.ToList(); selected = Math.Max(0, drafts.FindIndex(p => p.Id == baselineSelected));
            profiles.ItemsSource = drafts.Select(p => p.Name).ToArray(); profiles.SelectedIndex = selected;
        }
        finally { syncing = false; }
        ShowDraft(); hint.Text = ""; UpdateState();
    }
    internal void Detach() { preferences.Changed -= OnPreferencesChanged; preferences.UnregisterDraft(this); Discard(); }
    public void Reveal() => RevealRequested?.Invoke();
}
