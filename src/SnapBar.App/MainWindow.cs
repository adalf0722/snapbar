using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class MainWindow : Window
{
    private TextBlock status = null!, mode = null!, lifetimeHint = null!, notice = null!;
    private CheckBox toggle = null!, compatibilityToggle = null!, sublimeToggle = null!;
    private ListBox events = null!;
    private ExcludedAppsPanel excludedAppsPanel = null!;
    private SearchSettingsPanel searchSettingsPanel = null!;
    private SelectionController? controller;
    private TrayIcon? tray;
    private readonly SettingsStore settingsStore;
    private readonly StartupRegistration startupRegistration;
    private readonly AiPreferences aiPreferences;
    private ApplicationSettings preferences;
    private bool exitRequested, exitResolutionInProgress, hiddenNoticeShown;
    private bool updatingCompatibility, updatingSublime;
    private bool hotkeyNotice;
    private readonly Action configureSublime;
    private readonly Func<bool> sublimeIsReady;
    private readonly Func<string> readSystemUiLanguage;
    internal string InterfaceLanguagePreference => preferences.InterfaceLanguage;
    internal bool AutomaticDetectionEnabled => controller?.Automatic == true;
    internal bool ClipboardCompatibilityEnabled => controller?.ClipboardCompatibilityEnabled == true;
    internal bool SublimeCompatibilityEnabled => controller?.SublimeClipboardEnabled == true;
    internal IReadOnlyList<ExcludedApplication> ExcludedApplications => preferences.ExcludedApplications;

    public MainWindow(SettingsStore? settingsStore = null, Action? configureSublime = null, Func<bool>? sublimeIsReady = null,
        StartupRegistration? startupRegistration = null, Func<string>? systemUiLanguage = null)
    {
        this.configureSublime = configureSublime ?? SublimeCompatibility.InstallBinding;
        this.sublimeIsReady = sublimeIsReady ?? SublimeCompatibility.Ready;
        this.startupRegistration = startupRegistration ?? new StartupRegistration(Path.Combine(AppContext.BaseDirectory, "SnapBar.App.exe"));
        this.settingsStore = settingsStore ?? new SettingsStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapBar", "settings.json"));
        var loaded = this.settingsStore.Load();
        aiPreferences = new(Path.GetDirectoryName(this.settingsStore.FilePath)!);
        offlineDictionary = new(Path.Combine(Path.GetDirectoryName(this.settingsStore.FilePath)!, "offline-dictionary.bin"));
        preferences = loaded.Settings;
        readSystemUiLanguage = systemUiLanguage ?? UiText.SystemLanguageSource;
        UiText.SetLanguage(InterfaceLanguage.Resolve(preferences.InterfaceLanguage, readSystemUiLanguage()));
        if (loaded.Warning != null && loaded.Warning != "hotkey-invalid") preferences = preferences with { ClipboardCompatibilityEnabled = false, SublimeCompatibilityEnabled = false };
        // Existing valid guarded bindings are evidence of the legacy user's explicit choice.
        preferences = preferences with { SublimeCompatibilityEnabled = preferences.SublimeCompatibilityEnabled ?? this.sublimeIsReady() };
        BuildSettingsPages();
        generalPage.ConfigureLanguage(preferences.InterfaceLanguage, ChangeInterfaceLanguage);
        UiText.Changed += RefreshInterfaceLanguage;
        SourceInitialized += (_, _) =>
        {
            try { tray = CreateTrayIcon(ShowSettings, ToggleAutomatic, () => _ = ExitAsync()); tray.AddOcr(StartOcr); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            { lifetimeHint.Text = UiText.Get("MainWindow_000"); }
            try
            {
                var executable = Path.Combine(AppContext.BaseDirectory, "Probe", "SnapBar.Probe.exe");
                controller = new(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle), executable, initialHotkey: preferences.Hotkey,
                    initialSearchHotkey: preferences.SearchHotkey, initialCopyHotkey: preferences.CopyHotkey,
                    initialOcrHotkey: preferences.OcrHotkey);
                controller.OcrRequested += StartOcr;
                controller.Status += AddStatus;
                controller.HotkeyRecorded += generalPage.Record;
                controller.Automatic = preferences.AutomaticDetectionEnabled;
                controller.SetExcludedApplications(preferences.ExcludedApplications);
                controller.SearchUrlTemplate = preferences.SearchUrlTemplate;
                controller.ConfigureActions(preferences.Actions);
                controller.ConfigureAi(aiPreferences);
                controller.Dictionary = offlineDictionary;
                controller.OpenDictionarySettings = ShowDictionarySettings;
                ApplyClipboardCompatibility();
                controller.ActionsChanged += value => { preferences = preferences with { Actions = value }; actionSettingsPanel.ShowSettings(value); SavePreferences(); };
                controller.ManageActionsRequested += ShowActionSettings;
                UpdateAutomaticControls();
                if (controller.HotkeyRegistered) AddStatus(UiText.Join(UiText.Get("MainWindow_001"), preferences.Hotkey.Display));
                else { ShowNotice(UiText.Join(preferences.Hotkey.Display, UiText.Get("MainWindow_002"))); hotkeyNotice = true; }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                toggle.IsEnabled = false; mode.Text = UiText.Get("MainWindow_003"); mode.Foreground = Brushes.Firebrick;
                tray?.Update(false, false); ShowNotice(UiText.Get("MainWindow_004"));
            }
            if (loaded.Warning == "hotkey-invalid")
            {
                ShowNotice(UiText.Join(UiText.Get("MainWindow_005"), (controller?.HotkeyRegistered == true ? "" : UiText.Get("MainWindow_006"))));
                hotkeyNotice = true;
            }
            else if (loaded.Warning != null) ShowNotice(UiText.Get("MainWindow_007"));
        };
        Activated += (_, _) => { generalPage.RefreshStartup(); generalPage.RefreshShortcut(); };
        Deactivated += (_, _) => generalPage.CancelRecording();
        Closing += (_, e) =>
        {
            if (exitRequested) return;
            e.Cancel = true;
            // Closing is cancelled first; request a fresh close after this event returns.
            if (tray == null) { Dispatcher.BeginInvoke(() => _ = ExitAsync()); return; }
            Hide();
            if (!hiddenNoticeShown) { hiddenNoticeShown = true; tray.NotifyHidden(); }
        };
        Closed += (_, _) => { UiText.Changed -= RefreshInterfaceLanguage; aiSettingsPanel.Detach(); aiCommandsPanel.Detach(); controller?.Dispose(); tray?.Dispose(); };
    }

    internal void ToggleAutomatic()
    {
        if (controller == null || exitRequested) return;
        controller.Automatic = !controller.Automatic;
        preferences = preferences with { AutomaticDetectionEnabled = controller.Automatic };
        UpdateAutomaticControls();
        SavePreferences();
    }

    internal void SetClipboardCompatibility(bool enabled)
    {
        if (exitRequested || updatingCompatibility) return;
        updatingCompatibility = true;
        try
        {
            preferences = preferences with { ClipboardCompatibilityEnabled = enabled };
            compatibilityToggle.IsChecked = enabled; sublimeToggle.IsEnabled = enabled;
            ApplyClipboardCompatibility(); SavePreferences();
        }
        finally { updatingCompatibility = false; }
    }

    private void SetSublimeCompatibility(bool enabled)
    {
        if (exitRequested || updatingSublime) return;
        updatingSublime = true;
        try
        {
            if (enabled && !sublimeIsReady())
            {
                try { configureSublime(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
                { enabled = false; ShowNotice(UiText.Get("MainWindow_008")); }
            }
            preferences = preferences with { SublimeCompatibilityEnabled = enabled };
            sublimeToggle.IsChecked = enabled;
            ApplyClipboardCompatibility(); SavePreferences();
        }
        finally { updatingSublime = false; }
    }

    private void ApplyClipboardCompatibility()
    {
        if (controller == null) return;
        controller.ClipboardCompatibilityEnabled = preferences.ClipboardCompatibilityEnabled;
        var sublime = preferences.ClipboardCompatibilityEnabled && preferences.SublimeCompatibilityEnabled == true;
        controller.SublimeClipboardEnabled = sublime && sublimeIsReady();
        if (sublime && !controller.SublimeClipboardEnabled)
            ShowNotice(UiText.Get("MainWindow_009"));
    }

    internal bool AddExcludedApplication(string path, string? displayName = null)
    {
        if (exitRequested) return false;
        var entry = AppExclusionPolicy.Create(path, displayName);
        if (preferences.ExcludedApplications.Any(app =>
            string.Equals(app.ExecutablePath, entry.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
        { ShowNotice(UiText.Get("MainWindow_010")); return false; }
        if (preferences.ExcludedApplications.Length >= AppExclusionPolicy.MaximumApplications)
        { ShowNotice(UiText.Get("MainWindow_011")); return false; }
        preferences = preferences with { ExcludedApplications = [.. preferences.ExcludedApplications, entry] };
        ApplyExcludedApplications();
        ShowNotice(UiText.Get("MainWindow_012"));
        SavePreferences();
        return true;
    }

    internal bool RemoveExcludedApplication(string path)
    {
        if (exitRequested) return false;
        var normalized = AppExclusionPolicy.NormalizePath(path);
        var remaining = preferences.ExcludedApplications.Where(app =>
            !string.Equals(app.ExecutablePath, normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (remaining.Length == preferences.ExcludedApplications.Length) return false;
        preferences = preferences with { ExcludedApplications = remaining };
        ApplyExcludedApplications();
        ShowNotice(UiText.Get("MainWindow_013"));
        SavePreferences();
        return true;
    }

    private void ApplyExcludedApplications()
    {
        controller?.SetExcludedApplications(preferences.ExcludedApplications);
        excludedAppsPanel.ShowApplications(preferences.ExcludedApplications);
    }

    private void SavePreferences()
    {
        try { settingsStore.Save(preferences); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ShowNotice(UiText.Get("MainWindow_014")); }
    }

    private void ShowActionSettings() => ShowCategory(SettingsCategory.Actions);

    private void ShowAiSettings() => ShowCategory(SettingsCategory.AiServices);

    internal bool SetSearchUrlTemplate(string value)
    {
        if (exitRequested) return false;
        if (!WebActionPolicy.IsValidSearchTemplate(value))
        { ShowNotice(UiText.Get("MainWindow_015")); return false; }
        var updated = preferences with { SearchUrlTemplate = value };
        try { settingsStore.Save(updated); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ShowNotice(UiText.Get("MainWindow_016")); return false; }
        preferences = updated;
        if (controller != null) controller.SearchUrlTemplate = value;
        searchSettingsPanel.ShowTemplate(value);
        return true;
    }

    private void UpdateAutomaticControls()
    {
        var automatic = controller?.Automatic == true;
        toggle.IsChecked = automatic;
        mode.Text = controller?.HotkeyRegistered != true ? UiText.Get("MainWindow_017") : automatic ? UiText.Get("MainWindow_018") : UiText.Get("MainWindow_019");
        mode.Foreground = controller?.HotkeyRegistered == true ? ActionUi.Muted : Brushes.Firebrick;
        UpdateModeVisibility();
        tray?.Update(automatic, controller != null);
    }

    private void UpdateModeVisibility() => mode.Visibility = CurrentCategory == SettingsCategory.General && controller?.HotkeyRegistered == true
        ? Visibility.Collapsed : Visibility.Visible;

    public void ShowSettings()
    {
        if (exitRequested) return;
        UiText.SetLanguage(InterfaceLanguage.Resolve(preferences.InterfaceLanguage, readSystemUiLanguage()));
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal bool ChangeInterfaceLanguage(string preference)
    {
        if (exitRequested) return false;
        var updated = preferences with { InterfaceLanguage = InterfaceLanguage.Normalize(preference) };
        try { settingsStore.Save(updated); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
        preferences = updated;
        UiText.SetLanguage(InterfaceLanguage.Resolve(updated.InterfaceLanguage, readSystemUiLanguage()));
        return true;
    }

    private void RefreshInterfaceLanguage()
    {
        UiText.Refresh(this);
        aiCommandsPanel.Refresh();
        UpdateAutomaticControls();
        tray?.RefreshLanguage();
    }

    internal async Task ExitAsync()
    {
        if (exitRequested || exitResolutionInProgress) return;
        exitResolutionInProgress = true;
        try
        {
            if (!TryResolveDraftsForExit()) return;
            exitRequested = true;
        }
        finally { exitResolutionInProgress = false; }
        IsEnabled = false;
        StopOcr();
        controller?.BeginShutdown();
        foreach (var draft in aiPreferences.Drafts.ToArray()) draft.Discard();
        while (controller?.ClipboardReadPending == true) await Task.Delay(25);
        Close();
        Application.Current.Shutdown();
    }

    public void PrepareSessionEnd()
    {
        exitRequested = true;
        StopOcr();
        controller?.BeginShutdown();
    }

    private void AddStatus(string message)
    {
        status.Text = message;
        events.Items.Insert(0, UiText.Join($"{DateTime.Now:HH:mm:ss}  ", message));
        while (events.Items.Count > 100) events.Items.RemoveAt(events.Items.Count - 1);
    }

    private void ShowNotice(string message)
    {
        hotkeyNotice = false;
        notice.Text = message; notice.Visibility = Visibility.Visible;
        AddStatus(message);
    }
}
