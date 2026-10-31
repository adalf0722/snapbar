using System.IO;
using System.Windows;
using System.Windows.Controls;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class AiSettingsWindow : Window
{
    private readonly AiPreferences preferences;
    private readonly ComboBox profiles;
    private readonly TextBox endpoint, model;
    private readonly PasswordBox key;
    private readonly CheckBox removeKey;
    private readonly TextBlock hint, keyStatus;
    private readonly List<AiProviderProfile> drafts;
    private readonly Dictionary<string, (AiProviderProfile Profile, string? Key)> keyChanges = [];
    private int selected = -1;

    public AiSettingsWindow(AiPreferences preferences)
    {
        this.preferences = preferences; drafts = preferences.Settings.Profiles.ToList();
        ActionUi.Setup(this, "SnapBar · AI 設定", 600, 620); MinWidth = 480; MinHeight = 470;
        var body = new DockPanel { Margin = new(24) };
        var footer = new StackPanel { Margin = new(0, 16, 0, 0) };
        hint = ActionUi.Text("", 12, ActionUi.Muted); footer.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        buttons.Children.Add(ActionUi.Button("取消", Close)); buttons.Children.Add(ActionUi.Button("儲存 AI 設定", Save, primary: true));
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var fields = new StackPanel(); fields.Children.Add(ActionUi.Heading("AI 服務", 22));
        fields.Children.Add(ActionUi.Text("選字只在執行指令後傳送至你設定的服務。", 13, ActionUi.Muted));
        void Label(string text) { var label = ActionUi.Text(text, 12, ActionUi.Muted); label.Margin = new(0, 14, 0, 5); fields.Children.Add(label); }
        Label("服務"); profiles = new ComboBox { ItemsSource = drafts.Select(p => p.Name).ToArray(), MinHeight = 36 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(profiles, "AiProvider"); fields.Children.Add(profiles);
        Label("完整 API 網址"); endpoint = ActionUi.Input("", 2048); fields.Children.Add(endpoint);
        fields.Children.Add(ActionUi.Text("OpenAI 使用 /v1/responses；相容服務使用完整 /chat/completions 網址。", 12, ActionUi.Muted));
        Label("模型 ID"); model = ActionUi.Input("", 128); fields.Children.Add(model);
        System.Windows.Automation.AutomationProperties.SetAutomationId(model, "AiModel");
        Label("API Key"); key = new PasswordBox { MaxLength = 8192, Padding = new(10, 8, 10, 8), MinHeight = 36 };
        fields.Children.Add(key); keyStatus = ActionUi.Text("", 12, ActionUi.Muted); fields.Children.Add(keyStatus);
        fields.Children.Add(ActionUi.Text("留白保留已存金鑰。金鑰依服務與網址分開保護；本機相容服務可不填。", 12, ActionUi.Muted));
        removeKey = new CheckBox { Content = "移除此網址已保存的 API Key", Margin = new(0, 12, 0, 0) }; fields.Children.Add(removeKey);
        profiles.SelectionChanged += (_, _) => { CaptureDraft(); selected = profiles.SelectedIndex; ShowDraft(); };
        endpoint.LostKeyboardFocus += (_, _) => ShowKeyStatus();
        body.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = body;
        profiles.SelectedIndex = Math.Max(0, drafts.FindIndex(p => p.Id == preferences.Settings.SelectedProfileId));
    }

    private AiProviderProfile CurrentDraft() => drafts[selected] with { Endpoint = endpoint.Text.Trim(), Model = model.Text.Trim() };
    private void CaptureDraft()
    {
        if (selected < 0) return;
        var profile = CurrentDraft(); drafts[selected] = profile;
        if (removeKey.IsChecked == true || key.Password.Length > 0)
            keyChanges[profile.Id + "|" + profile.Protocol + "|" + profile.Endpoint] = (profile, removeKey.IsChecked == true ? null : key.Password);
        key.Clear(); removeKey.IsChecked = false;
    }
    private void ShowDraft()
    {
        if (selected < 0) return;
        endpoint.Text = drafts[selected].Endpoint; model.Text = drafts[selected].Model; ShowKeyStatus();
    }
    private void ShowKeyStatus()
    {
        if (selected < 0) return;
        keyStatus.Text = preferences.Secrets.Get(CurrentDraft()) == null ? "此網址尚未保存金鑰。" : "此網址已有加密保存的金鑰。";
    }
    private void Save()
    {
        CaptureDraft();
        var current = drafts[selected];
        if (AiPolicy.ValidateProfile(current) is { } validation) { hint.Text = validation; return; }
        var settings = preferences.Settings with { Profiles = drafts.ToArray(), SelectedProfileId = current.Id };
        if (!preferences.TrySave(settings, out var error)) { hint.Text = error; return; }
        try { foreach (var change in keyChanges.Values) preferences.Secrets.Set(change.Profile, change.Key); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { hint.Text = "服務設定已保存，但金鑰無法保存。請檢查權限後重試。"; return; }
        key.Clear(); keyChanges.Clear(); Close();
    }
}
