using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed class MainWindow : Window
{
    private readonly TextBlock status, mode;
    private readonly Button toggle;
    private readonly ListBox events;
    private SelectionController? controller;

    public MainWindow()
    {
        Title = "SnapBar Windows · P0"; Width = 700; Height = 600; MinWidth = 560; MinHeight = 520;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 251));
        FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = "SNAPBAR / WINDOWS", FontSize = 12, Foreground = Brushes.SlateGray });
        panel.Children.Add(new TextBlock { Text = "選取文字，直接動作。", FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new(0, 10, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "P0 技術原型 · Copy / Cut / Paste", FontSize = 15 });
        panel.Children.Add(new TextBlock { Text = "到其他 App 選取文字，按 Ctrl+Alt+V 叫出動作列。\n也可以啟用自動偵測，於拖曳或雙擊選字後顯示。", FontSize = 15, LineHeight = 25, Margin = new(0, 22, 0, 14) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        mode = new TextBlock { Text = "自動偵測已暫停 · 僅使用手動快捷鍵", Foreground = Brushes.SlateGray, Margin = new(0, 12, 0, 24) };
        toggle = new Button { Content = "啟用自動偵測", Padding = new(18, 10, 18, 10), Margin = new(0, 0, 12, 0) };
        toggle.Click += (_, _) =>
        {
            if (controller == null) return;
            controller.Automatic = !controller.Automatic;
            toggle.Content = controller.Automatic ? "暫停自動偵測" : "啟用自動偵測";
            mode.Text = controller.Automatic ? "自動偵測已啟用 · 手動快捷鍵仍可使用" : "自動偵測已暫停 · 僅使用手動快捷鍵";
        };
        buttons.Children.Add(toggle);
        var fixture = new Button { Content = "開啟測試視窗", Padding = new(18, 10, 18, 10) };
        fixture.Click += (_, _) =>
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add("--fixture"); Process.Start(start);
        };
        buttons.Children.Add(fixture); panel.Children.Add(buttons);
        panel.Children.Add(mode);
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
        status = new TextBlock { Text = "正在初始化…", TextWrapping = TextWrapping.Wrap, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 10) };
        panel.Children.Add(status);
        events = new ListBox { Height = 140, BorderThickness = new Thickness(0), Background = Brushes.White };
        panel.Children.Add(events);
        panel.Children.Add(new TextBlock { Text = "預設使用 UI Automation；Sublime 相容模式會暫借剪貼簿。\n診斷不記錄選取內容；關閉此視窗即結束原型。", Foreground = Brushes.SlateGray, Margin = new(0, 16, 0, 0), LineHeight = 22 });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SourceInitialized += (_, _) =>
        {
            try
            {
                var executable = Path.Combine(AppContext.BaseDirectory, "Probe", "SnapBar.Probe.exe");
                controller = new(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle), executable);
                controller.Status += AddStatus;
                AddStatus(controller.HotkeyRegistered ? "準備完成 · Ctrl+Alt+V" : "Ctrl+Alt+V 已被占用；仍可啟用自動偵測。P0 尚無改鍵設定。");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            { toggle.IsEnabled = false; AddStatus("無法初始化輸入監聽，請關閉後重新啟動。"); }
        };
        Closing += async (_, e) =>
        {
            if (controller?.ClipboardReadPending != true) return;
            e.Cancel = true;
            IsEnabled = false;
            while (controller.ClipboardReadPending) await Task.Delay(25);
            Close();
        };
        Closed += (_, _) => controller?.Dispose();
    }

    private void AddStatus(string message)
    {
        status.Text = message;
        events.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (events.Items.Count > 100) events.Items.RemoveAt(events.Items.Count - 1);
    }
}
