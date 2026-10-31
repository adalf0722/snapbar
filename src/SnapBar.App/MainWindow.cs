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
    private readonly TextBlock status, mode, lifetimeHint, notice;
    private readonly CheckBox toggle;
    private readonly ListBox events;
    private readonly ExcludedAppsPanel excludedAppsPanel;
    private readonly SearchSettingsPanel searchSettingsPanel;
    private SelectionController? controller;
    private TrayIcon? tray;
    private readonly SettingsStore settingsStore;
    private ApplicationSettings preferences;
    private bool exitRequested, hiddenNoticeShown;
    private ActionSettingsWindow? actionSettingsWindow;
    internal bool AutomaticDetectionEnabled => controller?.Automatic == true;
    internal IReadOnlyList<ExcludedApplication> ExcludedApplications => preferences.ExcludedApplications;

    public MainWindow(SettingsStore? settingsStore = null)
    {
        this.settingsStore = settingsStore ?? new SettingsStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapBar", "settings.json"));
        var loaded = this.settingsStore.Load();
        preferences = loaded.Settings;
        ActionUi.Setup(this, "SnapBar · 設定", 740, 760); MinWidth = 580; MinHeight = 520;
        var panel = new StackPanel { Margin = new Thickness(28) };
        var heading = new DockPanel { Margin = new(0, 0, 0, 22) };
        mode = ActionUi.Text("正在啟動", 12, ActionUi.Muted);
        var badge = new Border { Background = ActionUi.Pale, CornerRadius = new(12), Padding = new(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center, Child = mode };
        DockPanel.SetDock(badge, Dock.Right); heading.Children.Add(badge);
        var title = new StackPanel(); title.Children.Add(ActionUi.Heading("SnapBar", 26));
        title.Children.Add(ActionUi.Text("選取文字，直接動作。", 13, ActionUi.Muted)); heading.Children.Add(title); panel.Children.Add(heading);

        void AddCard(UIElement content, bool compact = false)
        {
            var card = ActionUi.Card(content, compact ? new Thickness(18, 6, 18, 6) : new Thickness(20));
            card.Margin = new(0, 0, 0, 12); panel.Children.Add(card);
        }
        var detection = new StackPanel(); var detectionRow = new DockPanel();
        toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = preferences.AutomaticDetectionEnabled,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "自動偵測選取文字" };
        System.Windows.Automation.AutomationProperties.SetName(toggle, "自動偵測選取文字");
        System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "AutomaticDetectionToggle");
        toggle.Click += (_, _) => ToggleAutomatic(); DockPanel.SetDock(toggle, Dock.Right); detectionRow.Children.Add(toggle);
        var detectionText = new StackPanel(); detectionText.Children.Add(ActionUi.Heading("自動偵測"));
        var detectionHelp = ActionUi.Text("拖曳或雙擊選字後，顯示動作列。", 13, ActionUi.Muted); detectionHelp.Margin = new(0, 5, 12, 0);
        detectionText.Children.Add(detectionHelp); detectionRow.Children.Add(detectionText); detection.Children.Add(detectionRow);
        var shortcut = new DockPanel { Margin = new(0, 18, 0, 0) };
        var keys = new Border { Child = ActionUi.Text("Ctrl + Alt + V", 12), Background = ActionUi.Canvas, BorderBrush = ActionUi.Line,
            BorderThickness = new(1), CornerRadius = new(6), Padding = new(10, 6, 10, 6) };
        DockPanel.SetDock(keys, Dock.Right); shortcut.Children.Add(keys); shortcut.Children.Add(ActionUi.Text("隨時用快捷鍵叫出動作列", 13, ActionUi.Muted));
        detection.Children.Add(shortcut); AddCard(detection);

        var actions = new DockPanel(); var manageActions = ActionUi.Button("管理動作", ShowActionSettings, "settings", true);
        manageActions.Margin = new(16, 0, 0, 0); manageActions.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(manageActions, Dock.Right); actions.Children.Add(manageActions);
        var actionText = new StackPanel(); actionText.Children.Add(ActionUi.Heading("動作列"));
        var actionHelp = ActionUi.Text("安排常用動作、外觀與自訂腳本。", 13, ActionUi.Muted); actionHelp.Margin = new(0, 5, 0, 0);
        actionText.Children.Add(actionHelp); actions.Children.Add(actionText); AddCard(actions);

        searchSettingsPanel = new SearchSettingsPanel();
        searchSettingsPanel.ShowTemplate(preferences.SearchUrlTemplate);
        searchSettingsPanel.SaveRequested += value => SetSearchUrlTemplate(value);
        searchSettingsPanel.ResetRequested += () => SetSearchUrlTemplate(WebActionPolicy.DefaultSearchTemplate);
        AddCard(searchSettingsPanel, compact: true);

        var compatibility = new StackPanel(); compatibility.Children.Add(ActionUi.Heading("相容性與排除 App"));
        var sublime = new CheckBox { Content = "Sublime 剪貼簿相容模式", Margin = new(0, 12, 0, 0) };
        sublime.Checked += (_, _) =>
        {
            try
            {
                SublimeCompatibility.InstallBinding();
                if (controller != null) controller.SublimeClipboardEnabled = true;
                AddStatus("已啟用 Sublime 相容模式；已設定有條件的複製／剪下／貼上，未安裝外掛。");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
            { sublime.IsChecked = false; ShowNotice("無法設定 Sublime 快捷鍵（檔案格式、權限或按鍵衝突），未啟用相容模式。"); }
        };
        sublime.Unchecked += (_, _) => { if (controller != null) controller.SublimeClipboardEnabled = false; };
        compatibility.Children.Add(sublime);
        var compatibilityHelp = ActionUi.Text("暫借剪貼簿讀取選取，可編輯時提供剪下／貼上。每次啟動需自行啟用。", 12, ActionUi.Muted);
        compatibilityHelp.Margin = new(27, 3, 0, 10); compatibility.Children.Add(compatibilityHelp);
        excludedAppsPanel = new ExcludedAppsPanel();
        excludedAppsPanel.ShowApplications(preferences.ExcludedApplications);
        excludedAppsPanel.AddRequested += entry => AddExcludedApplication(entry.ExecutablePath, entry.DisplayName);
        excludedAppsPanel.RemoveRequested += entry => RemoveExcludedApplication(entry.ExecutablePath);
        excludedAppsPanel.ValidationFailed += ShowNotice;
        compatibility.Children.Add(excludedAppsPanel); AddCard(compatibility);

        var diagnostics = new StackPanel();
        status = ActionUi.Text("正在初始化…", 12, ActionUi.Muted); status.Margin = new(0, 8, 0, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "LatestDiagnosticStatus");
        diagnostics.Children.Add(status);
        events = new ListBox { Height = 140, BorderThickness = new(0), Background = ActionUi.Canvas, FontSize = 12,
            Foreground = ActionUi.Muted, Margin = new(0, 8, 0, 12) };
        diagnostics.Children.Add(events);
        var fixture = ActionUi.Button("開啟測試視窗", () =>
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add("--fixture"); Process.Start(start);
        }, "document");
        fixture.HorizontalAlignment = HorizontalAlignment.Left; diagnostics.Children.Add(fixture);
        AddCard(new Expander { Header = "診斷與測試", Content = diagnostics }, compact: true);

        notice = ActionUi.Text("", 12, ActionUi.Ink); notice.Margin = new(2, 0, 2, 10); notice.Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetAutomationId(notice, "SettingsNotice"); panel.Children.Add(notice);
        var footer = new DockPanel { Margin = new(0, 8, 0, 0) };
        var exit = ActionUi.Button("結束 SnapBar", () => _ = ExitAsync()); exit.Margin = new(12, 0, 0, 0);
        DockPanel.SetDock(exit, Dock.Right); footer.Children.Add(exit);
        lifetimeHint = ActionUi.Text("關閉視窗後會繼續待命。\n按系統匣圖示可返回設定。", 12, ActionUi.Muted);
        footer.Children.Add(lifetimeHint); panel.Children.Add(footer);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
                controller.ConfigureActions(preferences.Actions);
                controller.ActionsChanged += value => { preferences = preferences with { Actions = value }; SavePreferences(); };
                controller.ManageActionsRequested += ShowActionSettings;
                UpdateAutomaticControls();
                if (controller.HotkeyRegistered) AddStatus("準備完成 · Ctrl+Alt+V");
                else ShowNotice("Ctrl+Alt+V 已被占用；仍可啟用自動偵測。");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                toggle.IsEnabled = false; mode.Text = "● 啟動失敗"; mode.Foreground = Brushes.Firebrick;
                tray?.Update(false, false); ShowNotice("無法初始化輸入監聽，請結束後重新啟動。");
            }
            if (loaded.Warning != null) ShowNotice("設定無法讀取或版本不相容；本次使用預設值，原檔保留且不會覆寫。");
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

    private void ShowActionSettings()
    {
        if (exitRequested) return;
        if (actionSettingsWindow != null) { actionSettingsWindow.Activate(); return; }
        var window = new ActionSettingsWindow(preferences.Actions, value =>
        {
            preferences = preferences with { Actions = value }; controller?.ConfigureActions(value);
            try { settingsStore.Save(preferences); return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            { ShowNotice("本次動作設定已生效，但無法保存；請檢查權限或 64 KiB 設定上限。"); return false; }
        });
        actionSettingsWindow = window; window.Closed += (_, _) => actionSettingsWindow = null; window.Show();
    }

    internal bool SetSearchUrlTemplate(string value)
    {
        if (exitRequested) return false;
        if (!WebActionPolicy.IsValidSearchTemplate(value))
        { ShowNotice("搜尋網址無效：請使用 HTTP／HTTPS，並將一個 {query} 放在參數值中。"); return false; }
        preferences = preferences with { SearchUrlTemplate = value };
        if (controller != null) controller.SearchUrlTemplate = value;
        searchSettingsPanel.ShowTemplate(value);
        ShowNotice("已更新搜尋網址；點擊搜尋後交由預設瀏覽器處理。");
        SavePreferences();
        return true;
    }

    private void UpdateAutomaticControls()
    {
        var automatic = controller?.Automatic == true;
        toggle.IsChecked = automatic;
        mode.Text = automatic ? "● 自動偵測中" : controller?.HotkeyRegistered == true ? "● 快捷鍵待命" : "● 快捷鍵不可用";
        mode.Foreground = automatic ? ActionUi.Accent : controller?.HotkeyRegistered == true ? ActionUi.Muted : Brushes.Firebrick;
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
        actionSettingsWindow?.Close();
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
        notice.Text = message; notice.Visibility = Visibility.Visible;
        AddStatus(message);
    }
}
