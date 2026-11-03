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
    internal bool AutomaticDetectionEnabled => controller?.Automatic == true;
    internal bool ClipboardCompatibilityEnabled => controller?.ClipboardCompatibilityEnabled == true;
    internal bool SublimeCompatibilityEnabled => controller?.SublimeClipboardEnabled == true;
    internal IReadOnlyList<ExcludedApplication> ExcludedApplications => preferences.ExcludedApplications;

    public MainWindow(SettingsStore? settingsStore = null, Action? configureSublime = null, Func<bool>? sublimeIsReady = null,
        StartupRegistration? startupRegistration = null)
    {
        this.configureSublime = configureSublime ?? SublimeCompatibility.InstallBinding;
        this.sublimeIsReady = sublimeIsReady ?? SublimeCompatibility.Ready;
        this.startupRegistration = startupRegistration ?? new StartupRegistration(Path.Combine(AppContext.BaseDirectory, "SnapBar.App.exe"));
        this.settingsStore = settingsStore ?? new SettingsStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapBar", "settings.json"));
        var loaded = this.settingsStore.Load();
        aiPreferences = new(Path.GetDirectoryName(this.settingsStore.FilePath)!);
        preferences = loaded.Settings;
        if (loaded.Warning != null && loaded.Warning != "hotkey-invalid") preferences = preferences with { ClipboardCompatibilityEnabled = false, SublimeCompatibilityEnabled = false };
        // Existing valid guarded bindings are evidence of the legacy user's explicit choice.
        preferences = preferences with { SublimeCompatibilityEnabled = preferences.SublimeCompatibilityEnabled ?? this.sublimeIsReady() };
        BuildSettingsPages();
        SourceInitialized += (_, _) =>
        {
            try { tray = CreateTrayIcon(ShowSettings, ToggleAutomatic, () => _ = ExitAsync()); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            { lifetimeHint.Text = "無法建立系統匣圖示；關閉視窗會完整結束 SnapBar。"; }
            try
            {
                var executable = Path.Combine(AppContext.BaseDirectory, "Probe", "SnapBar.Probe.exe");
                controller = new(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle), executable, initialHotkey: preferences.Hotkey);
                controller.Status += AddStatus;
                controller.HotkeyRecorded += generalPage.Record;
                controller.Automatic = preferences.AutomaticDetectionEnabled;
                controller.SetExcludedApplications(preferences.ExcludedApplications);
                controller.SearchUrlTemplate = preferences.SearchUrlTemplate;
                controller.ConfigureActions(preferences.Actions);
                controller.ConfigureAi(aiPreferences);
                ApplyClipboardCompatibility();
                controller.ActionsChanged += value => { preferences = preferences with { Actions = value }; actionSettingsPanel.ShowSettings(value); SavePreferences(); };
                controller.ManageActionsRequested += ShowActionSettings;
                UpdateAutomaticControls();
                if (controller.HotkeyRegistered) AddStatus("準備完成 · " + preferences.Hotkey.Display);
                else { ShowNotice(preferences.Hotkey.Display + " 無法使用；請到一般設定更換，仍可啟用自動偵測。"); hotkeyNotice = true; }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                toggle.IsEnabled = false; mode.Text = "● 啟動失敗"; mode.Foreground = Brushes.Firebrick;
                tray?.Update(false, false); ShowNotice("無法初始化輸入監聽，請結束後重新啟動。");
            }
            if (loaded.Warning == "hotkey-invalid")
            {
                ShowNotice("快捷鍵設定損壞，暫用 Ctrl + Alt + V；其他偏好已保留。下次儲存會先備份原檔。"
                    + (controller?.HotkeyRegistered == true ? "" : "目前預設快捷鍵也無法使用，請到一般設定更換。"));
                hotkeyNotice = true;
            }
            else if (loaded.Warning != null) ShowNotice("設定無法讀取或版本不相容；本次使用預設值，原檔保留且不會覆寫。");
        };
        Activated += (_, _) => generalPage.RefreshStartup();
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
        Closed += (_, _) => { aiSettingsPanel.Detach(); aiCommandsPanel.Detach(); controller?.Dispose(); tray?.Dispose(); };
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
                { enabled = false; ShowNotice("無法設定 Sublime 快捷鍵（檔案格式、權限或按鍵衝突），未啟用 Sublime 支援。"); }
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
            ShowNotice("Sublime 快捷鍵設定不存在或已變更；請關閉再啟用 Sublime 支援以重新設定。");
    }

    internal bool AddExcludedApplication(string path, string? displayName = null)
    {
        if (exitRequested) return false;
        var entry = AppExclusionPolicy.Create(path, displayName);
        if (preferences.ExcludedApplications.Any(app =>
            string.Equals(app.ExecutablePath, entry.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
        { ShowNotice("此 App 已在排除清單中。"); return false; }
        if (preferences.ExcludedApplications.Length >= AppExclusionPolicy.MaximumApplications)
        { ShowNotice("排除清單已達 128 項，請先移除不需要的項目。"); return false; }
        preferences = preferences with { ExcludedApplications = [.. preferences.ExcludedApplications, entry] };
        ApplyExcludedApplications();
        ShowNotice("已加入排除 App；手動快捷鍵仍可使用。");
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
        ShowNotice("已移除排除 App；啟用自動偵測時將恢復正常觸發。");
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
        { ShowNotice("本次設定已生效，但無法保存；下次啟動將使用原設定。"); }
    }

    private void ShowActionSettings() => ShowCategory(SettingsCategory.Actions);

    private void ShowAiSettings() => ShowCategory(SettingsCategory.AiServices);

    internal bool SetSearchUrlTemplate(string value)
    {
        if (exitRequested) return false;
        if (!WebActionPolicy.IsValidSearchTemplate(value))
        { ShowNotice("搜尋網址無效：請使用 HTTP／HTTPS，並將一個 {query} 放在參數值中。"); return false; }
        var updated = preferences with { SearchUrlTemplate = value };
        try { settingsStore.Save(updated); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ShowNotice("搜尋設定無法保存；草稿仍保留，請檢查檔案權限或設定大小。"); return false; }
        preferences = updated;
        if (controller != null) controller.SearchUrlTemplate = value;
        searchSettingsPanel.ShowTemplate(value);
        return true;
    }

    private void UpdateAutomaticControls()
    {
        var automatic = controller?.Automatic == true;
        toggle.IsChecked = automatic;
        mode.Text = controller?.HotkeyRegistered != true ? "● 選字偵測：快捷鍵不可用" : automatic ? "● 選字偵測：自動" : "● 選字偵測：快捷鍵待命";
        mode.Foreground = controller?.HotkeyRegistered == true ? ActionUi.Muted : Brushes.Firebrick;
        UpdateModeVisibility();
        tray?.Update(automatic, controller != null);
    }

    private void UpdateModeVisibility() => mode.Visibility = CurrentCategory == SettingsCategory.General && controller?.HotkeyRegistered == true
        ? Visibility.Collapsed : Visibility.Visible;

    public void ShowSettings()
    {
        if (exitRequested) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
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
        controller?.BeginShutdown();
        foreach (var draft in aiPreferences.Drafts.ToArray()) draft.Discard();
        while (controller?.ClipboardReadPending == true) await Task.Delay(25);
        Close();
        Application.Current.Shutdown();
    }

    public void PrepareSessionEnd()
    {
        exitRequested = true;
        controller?.BeginShutdown();
    }

    private void AddStatus(string message)
    {
        status.Text = message;
        events.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (events.Items.Count > 100) events.Items.RemoveAt(events.Items.Count - 1);
    }

    private void ShowNotice(string message)
    {
        hotkeyNotice = false;
        notice.Text = message; notice.Visibility = Visibility.Visible;
        AddStatus(message);
    }
}
