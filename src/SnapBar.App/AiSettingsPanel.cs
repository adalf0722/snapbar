using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed partial class AiSettingsPanel : ContentControl, ISettingsDraft
{
    private readonly UiLanguage? language;
    private string Text(string key, params object?[] values) => language?.Get(key, values) ?? UiText.Get(key, values);
    private readonly AiPreferences preferences;
    private ComboBox profiles = null!, model = null!;
    private TextBox endpoint = null!;
    private Expander advanced = null!;
    private TextBlock customEndpointNotice = null!, protocolHint = null!, keyHelp = null!, keyPlaceholder = null!;
    private Button keyLink = null!, modelsLink = null!, replaceKey = null!, save = null!, discard = null!;
    private PasswordBox key = null!;
    private CheckBox removeKey = null!;
    private TextBlock hint = null!, keyStatus = null!, state = null!;
    private string? editingKeyScope;
    private List<AiProviderProfile> drafts;
    private AiProviderProfile[] baseline;
    private string baselineSelected;
    private readonly Dictionary<string, (AiProviderProfile Profile, string? Key)> keyChanges = [];
    private int selected = -1;
    private bool syncing, saving;
    internal Action? RevealRequested { get; set; }
    internal event Action? Saved;
    public string DraftName => HasPendingKeyChanges ? Text("AiSettingsPanel_000") : Text("AiSettingsPanel_001");
    internal bool HasPendingKeyChanges => key.Password.Length > 0 || removeKey.IsChecked == true
        || keyChanges.Keys.Any(scope => selected < 0 || scope != Scope(CurrentDraft()));
    public bool IsDirty => selected >= 0 && (HasPendingKeyChanges || drafts[selected].Id != baselineSelected
        || !drafts.Select((p, i) => i == selected ? CurrentDraft() : p).SequenceEqual(baseline));

    public AiSettingsPanel(AiPreferences preferences, Action<Uri>? openLink = null, IAiTextClient? testClient = null, UiLanguage? language = null)
    {
        this.language = language;
        this.preferences = preferences;
        baseline = preferences.Settings.Profiles.ToArray(); baselineSelected = preferences.Settings.SelectedProfileId;
        drafts = baseline.ToList(); ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        Focusable = false;
        AutomationProperties.SetAutomationId(this, "AiSettingsPanel");
        InitializePresentation(openLink ?? BrowserLauncher.Open, testClient);
        profiles.SelectionChanged += (_, _) =>
        {
            if (syncing || UiText.IsRefreshing) return;
            ResetTest();
            CaptureDraft(); selected = profiles.SelectedIndex; ShowDraft(); UpdateState();
        };
        endpoint.TextChanged += (_, _) => { if (!syncing) { ResetTest(); ShowKeyStatus(); ShowEndpointNotice(); UpdateState(); } };
        model.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (!syncing) { ResetTest(); UpdateState(); } }));
        key.PasswordChanged += (_, _) => { if (!syncing) { ResetTest(); ShowKeyStatus(); UpdateState(); } };
        removeKey.Checked += (_, _) => { if (!syncing) { ResetTest(); ShowKeyStatus(); UpdateState(); } };
        removeKey.Unchecked += (_, _) => { if (!syncing) { ResetTest(); ShowKeyStatus(); UpdateState(); } };
        Unloaded += (_, _) => ResetTest();
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
            var profile = drafts[selected]; endpoint.Text = profile.Endpoint;
            editingKeyScope = null;
            var definition = AiProviderCatalog.Find(profile.Id);
            var text = profile.Model;
            model.ItemsSource = definition?.Models ?? []; model.Text = text;
            advanced.IsExpanded = profile.Id == "compatible";
            keyLink.Visibility = definition?.KeyUrl == null ? Visibility.Collapsed : Visibility.Visible;
            modelsLink.Visibility = definition?.ModelsUrl == null ? Visibility.Collapsed : Visibility.Visible;
            keyLink.ToolTip = definition?.KeyUrl;
            modelsLink.ToolTip = definition?.ModelsUrl;
            protocolHint.Text = profile.Protocol switch
            {
                AiProtocol.Responses => Text("AiSettingsPanel_002"),
                AiProtocol.Messages => Text("AiSettingsPanel_003"),
                _ => Text("AiSettingsPanel_004")
            };
            key.Clear(); removeKey.IsChecked = false;
            if (keyChanges.TryGetValue(Scope(profile), out var pending))
            { key.Password = pending.Key ?? ""; removeKey.IsChecked = pending.Key == null; }
        }
        finally { syncing = false; }
        ShowKeyStatus(); ShowEndpointNotice();
    }
    private void ShowEndpointNotice()
    {
        var definition = AiProviderCatalog.Find(drafts[selected].Id);
        var matches = definition != null && Uri.TryCreate(endpoint.Text.Trim(), UriKind.Absolute, out var current)
            && current.Equals(new Uri(definition.Profile.Endpoint));
        customEndpointNotice.Visibility = drafts[selected].Id != "compatible" && !matches ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdateState(bool saved = false)
    {
        var dirty = IsDirty;
        state.Text = dirty ? Text("AiSettingsPanel_005") : saved ? Text("AiSettingsPanel_006") : "";
        save.IsEnabled = dirty;
        discard.IsEnabled = dirty;
    }
    private void OnPreferencesChanged() { if (!saving && !IsDirty) Discard(); }
    public bool TrySave()
    {
        if (testController.Busy) ResetTest();
        CaptureDraft(); var current = drafts[selected];
        if (AiPolicy.ValidateProfile(current) is { } validation) { hint.Text = ValidationMessages.Describe(validation, language); return false; }
        saving = true;
        try
        {
            var settings = preferences.Settings with { Profiles = drafts.ToArray(), SelectedProfileId = current.Id };
            if (!preferences.TrySave(settings, out var error)) { hint.Text = error == null ? "" : UiText.Current(error, language?.Culture); return false; }
            baseline = preferences.Settings.Profiles.ToArray(); baselineSelected = preferences.Settings.SelectedProfileId;
            foreach (var (scope, change) in keyChanges.ToArray())
            {
                try { preferences.Secrets.Set(change.Profile, change.Key); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { ResetTest(); hint.Text = Text("AiSettingsPanel_007"); UpdateState(); return false; }
                keyChanges.Remove(scope);
                if (scope == Scope(CurrentDraft()))
                {
                    syncing = true;
                    try { key.Clear(); removeKey.IsChecked = false; }
                    finally { syncing = false; }
                }
            }
            syncing = true;
            try { key.Clear(); keyChanges.Clear(); removeKey.IsChecked = false; }
            finally { syncing = false; }
            editingKeyScope = null;
            hint.Text = ""; UpdateState(saved: true); ShowKeyStatus();
            preferences.NotifyChanged(); Saved?.Invoke(); return true;
        }
        finally { saving = false; }
    }
    public void Discard()
    {
        ResetTest();
        syncing = true;
        try
        {
            key.Clear(); keyChanges.Clear(); removeKey.IsChecked = false;
            baseline = preferences.Settings.Profiles.ToArray(); baselineSelected = preferences.Settings.SelectedProfileId;
            drafts = baseline.ToList(); selected = Math.Max(0, drafts.FindIndex(p => p.Id == baselineSelected));
            profiles.ItemsSource = drafts.Select(p => UiNames.Provider(p, language)).ToArray(); profiles.SelectedIndex = selected;
        }
        finally { syncing = false; }
        ShowDraft(); hint.Text = ""; UpdateState();
    }
    internal void Detach() { DisposeTesting(); preferences.Changed -= OnPreferencesChanged; preferences.UnregisterDraft(this); Discard(); }
    public void Reveal() => RevealRequested?.Invoke();
}
