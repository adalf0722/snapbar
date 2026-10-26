using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed class MainWindow : Window
{
    private readonly TextBlock status, mode, lifetimeHint;
    private readonly Button toggle;
    private readonly ListBox events;
    private readonly ExcludedAppsPanel excludedAppsPanel;
    private readonly SearchSettingsPanel searchSettingsPanel;
    private SelectionController? controller;
    private TrayIcon? tray;
    private readonly SettingsStore settingsStore;
    private ApplicationSettings preferences;
    private bool exitRequested, hiddenNoticeShown;
    internal bool AutomaticDetectionEnabled => controller?.Automatic == true;
    internal IReadOnlyList<ExcludedApplication> ExcludedApplications => preferences.ExcludedApplications;

    public MainWindow(SettingsStore? settingsStore = null)
    {
        this.settingsStore = settingsStore ?? new SettingsStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapBar", "settings.json"));
        var loaded = this.settingsStore.Load();
        preferences = loaded.Settings;
        Title = "SnapBar · 設定"; Width = 700; Height = 760; MinWidth = 560; MinHeight = 520;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 251));
        FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = "SNAPBAR / WINDOWS", FontSize = 12, Foreground = Brushes.SlateGray });
        panel.Children.Add(new TextBlock { Text = "選取文字，直接動作。", FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new(0, 10, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "在系統匣待命 · 複製／剪下／貼上／搜尋", FontSize = 15 });
        panel.Children.Add(new TextBlock { Text = "到其他 App 選取文字，按 Ctrl+Alt+V 叫出動作列。\n也可以啟用自動偵測，於拖曳或雙擊選字後顯示。", FontSize = 15, LineHeight = 25, Margin = new(0, 22, 0, 14) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        mode = new TextBlock { Text = "自動偵測已暫停 · 僅使用手動快捷鍵", Foreground = Brushes.SlateGray, Margin = new(0, 12, 0, 24) };
        toggle = new Button { Content = "啟用自動偵測", Padding = new(18, 10, 18, 10), Margin = new(0, 0, 12, 0) };
        toggle.Click += (_, _) => ToggleAutomatic();
        buttons.Children.Add(toggle);
        var fixture = new Button { Content = "開啟測試視窗", Padding = new(18, 10, 18, 10) };
        fixture.Click += (_, _) =>
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add("--fixture"); Process.Start(start);
        };
        buttons.Children.Add(fixture); panel.Children.Add(buttons);
        panel.Children.Add(mode);
        panel.Children.Add(new TextBlock { Text = "自動偵測偏好會自動保存；Sublime 相容模式每次需自行啟用。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new(0, -12, 0, 16) });
        var sublime = new CheckBox { Content = "Sublime 剪貼簿相容模式（可編輯時提供剪下／貼上）", Margin = new(0, 0, 0, 14) };
        sublime.Checked += (_, _) =>
        {
            try
            {
                SublimeCompatibility.InstallBinding();
                if (controller != null) controller.SublimeClipboardEnabled = true;
                AddStatus("已啟用 Sublime 相容模式；已設定有條件的複製／剪下／貼上，未安裝外掛。");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
            { sublime.IsChecked = false; AddStatus("無法設定 Sublime 快捷鍵（檔案格式、權限或按鍵衝突），未啟用相容模式。"); }
        };
        sublime.Unchecked += (_, _) => { if (controller != null) controller.SublimeClipboardEnabled = false; };
        panel.Children.Add(sublime);
        excludedAppsPanel = new ExcludedAppsPanel();
        excludedAppsPanel.ShowApplications(preferences.ExcludedApplications);
        excludedAppsPanel.AddRequested += entry => AddExcludedApplication(entry.ExecutablePath, entry.DisplayName);
        excludedAppsPanel.RemoveRequested += entry => RemoveExcludedApplication(entry.ExecutablePath);
        excludedAppsPanel.ValidationFailed += AddStatus;
        panel.Children.Add(excludedAppsPanel);
        searchSettingsPanel = new SearchSettingsPanel();
        searchSettingsPanel.ShowTemplate(preferences.SearchUrlTemplate);
        searchSettingsPanel.SaveRequested += value => SetSearchUrlTemplate(value);
        searchSettingsPanel.ResetRequested += () => SetSearchUrlTemplate(WebActionPolicy.DefaultSearchTemplate);
        panel.Children.Add(searchSettingsPanel);
        status = new TextBlock { Text = "正在初始化…", TextWrapping = TextWrapping.Wrap, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 10) };
        panel.Children.Add(status);
        events = new ListBox { Height = 120, BorderThickness = new Thickness(0), Background = Brushes.White };
        panel.Children.Add(events);
        lifetimeHint = new TextBlock { Text = "關閉此視窗後，SnapBar 會繼續在系統匣待命。\n按系統匣圖示可返回；右鍵選單可完整結束。\n預設使用 UI Automation；Sublime 相容模式會暫借剪貼簿。", Foreground = Brushes.SlateGray, Margin = new(0, 16, 0, 0), LineHeight = 22, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(lifetimeHint);
        var exit = new Button { Content = "結束 SnapBar", HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 6, 12, 6), Margin = new(0, 12, 0, 0) };
        exit.Click += async (_, _) => await ExitAsync();
        panel.Children.Add(exit);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SourceInitialized += (_, _) =>
        {
            try { tray = new TrayIcon(ShowSettings, ToggleAutomatic, () => _ = ExitAsync()); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            { lifetimeHint.Text = "無法建立系統匣圖示；關閉視窗會完整結束 SnapBar。"; }
            try
            {
                var executable = Path.Combine(AppContext.BaseDirectory, "Probe", "SnapBar.Probe.exe");
                controller = new(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle), executable);
                controller.Status += AddStatus;
                controller.Automatic = preferences.AutomaticDetectionEnabled;
                controller.SetExcludedApplications(preferences.ExcludedApplications);
                controller.SearchUrlTemplate = preferences.SearchUrlTemplate;
                UpdateAutomaticControls();
                AddStatus(controller.HotkeyRegistered ? "準備完成 · Ctrl+Alt+V" : "Ctrl+Alt+V 已被占用；仍可啟用自動偵測。P0 尚無改鍵設定。");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            { toggle.IsEnabled = false; tray?.Update(false, false); AddStatus("無法初始化輸入監聽，請結束後重新啟動。"); }
            if (loaded.Warning != null) AddStatus("設定無法讀取或版本不相容；本次使用預設值，原檔保留且不會覆寫。");
        };
        Closing += (_, e) =>
        {
            if (exitRequested) return;
            e.Cancel = true;
            // Closing is cancelled first; request a fresh close after this event returns.
            if (tray == null) { Dispatcher.BeginInvoke(() => _ = ExitAsync()); return; }
            Hide();
            if (!hiddenNoticeShown) { hiddenNoticeShown = true; tray.NotifyHidden(); }
        };
        Closed += (_, _) => { controller?.Dispose(); tray?.Dispose(); };
    }

    internal void ToggleAutomatic()
    {
        if (controller == null || exitRequested) return;
        controller.Automatic = !controller.Automatic;
        preferences = preferences with { AutomaticDetectionEnabled = controller.Automatic };
        UpdateAutomaticControls();
        SavePreferences();
    }

    internal bool AddExcludedApplication(string path, string? displayName = null)
    {
        if (exitRequested) return false;
        var entry = AppExclusionPolicy.Create(path, displayName);
        if (preferences.ExcludedApplications.Any(app =>
            string.Equals(app.ExecutablePath, entry.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
        { AddStatus("此 App 已在排除清單中。"); return false; }
        if (preferences.ExcludedApplications.Length >= AppExclusionPolicy.MaximumApplications)
        { AddStatus("排除清單已達 128 項，請先移除不需要的項目。"); return false; }
        preferences = preferences with { ExcludedApplications = [.. preferences.ExcludedApplications, entry] };
        ApplyExcludedApplications();
        AddStatus("已加入排除 App；手動快捷鍵仍可使用。");
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
        AddStatus("已移除排除 App；啟用自動偵測時將恢復正常觸發。");
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
        { AddStatus("本次設定已生效，但無法保存；下次啟動將使用原設定。"); }
    }

    internal bool SetSearchUrlTemplate(string value)
    {
        if (exitRequested) return false;
        if (!WebActionPolicy.IsValidSearchTemplate(value))
        { AddStatus("搜尋網址無效：請使用 HTTP／HTTPS，並將一個 {query} 放在參數值中。"); return false; }
        preferences = preferences with { SearchUrlTemplate = value };
        if (controller != null) controller.SearchUrlTemplate = value;
        searchSettingsPanel.ShowTemplate(value);
        AddStatus("已更新搜尋網址；點擊搜尋後交由預設瀏覽器處理。");
        SavePreferences();
        return true;
    }

    private void UpdateAutomaticControls()
    {
        var automatic = controller?.Automatic == true;
        toggle.Content = automatic ? "暫停自動偵測" : "啟用自動偵測";
        mode.Text = automatic ? "自動偵測已啟用 · 手動快捷鍵仍可使用" : "自動偵測已暫停 · 僅使用手動快捷鍵";
        tray?.Update(automatic, controller != null);
    }

    public void ShowSettings()
    {
        if (exitRequested) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal async Task ExitAsync()
    {
        if (exitRequested) return;
        exitRequested = true;
        IsEnabled = false;
        controller?.BeginShutdown();
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
}
