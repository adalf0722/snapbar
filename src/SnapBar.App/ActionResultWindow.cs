using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class ActionResultWindow : Window
{
    private readonly Button applyButton;
    private readonly TextBlock hint;
    private nint handle;
    private readonly DispatcherTimer? timer;
    public ActionResultWindow(string name, string text, bool markdown, bool canApply, Action? apply = null, bool hud = false)
    {
        ActionUi.Setup(this, name + " · SnapBar 結果", hud ? 420 : 650, hud ? 180 : 520);
        MinHeight = hud ? 120 : 280; MinWidth = 320; ShowActivated = false; ShowInTaskbar = !hud; Topmost = true;
        var body = new DockPanel { Margin = new(20) }; var heading = ActionUi.Text(name, 18); heading.Margin = new(0, 0, 0, 15); DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading);
        var footer = new StackPanel { Margin = new(0, 12, 0, 0) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        hint = ActionUi.Text(canApply ? "套用前會重新驗證原視窗、焦點及選取；結果也會放入剪貼簿。" : "來源不可寫入或已變更；可以複製結果。", 11, ActionUi.Muted);
        buttons.Children.Add(ActionUi.Button("複製結果", () =>
        { try { Clipboard.SetText(text, TextDataFormat.UnicodeText); hint.Text = "已複製結果。"; } catch (System.Runtime.InteropServices.ExternalException) { hint.Text = "剪貼簿忙碌，請稍後再試。"; } }, "copy"));
        applyButton = ActionUi.Button("套用至原文", () => { applyButton!.IsEnabled = false; apply?.Invoke(); }); applyButton.IsEnabled = canApply; buttons.Children.Add(applyButton);
        buttons.Children.Add(ActionUi.Button("關閉", Close)); footer.Children.Add(buttons); footer.Children.Add(hint); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        if (markdown) body.Children.Add(new FlowDocumentScrollViewer { Document = RenderMarkdown(text), IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsSelectionEnabled = true });
        else body.Children.Add(new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new(0), Padding = new(10), FontSize = 14,
            Background = Brushes.White, FontFamily = new("Consolas, Microsoft JhengHei UI") });
        if (hud) { footer.Visibility = Visibility.Collapsed; timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) }; timer.Tick += (_, _) => Close(); Loaded += (_, _) => timer.Start(); }
        Content = body;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, (nint)((long)Native.GetWindowLongPtr(handle, -20) | 0x08000000));
            HwndSource.FromHwnd(handle).AddHook((nint hwnd, int message, nint w, nint l, ref bool handled) =>
            { if (message == Native.WM_MOUSEACTIVATE) { handled = true; return 3; } return 0; });
        };
        Closed += (_, _) => timer?.Stop();
    }
    public void InvalidateTarget() { applyButton.IsEnabled = false; hint.Text = "原選取已失效；請複製結果後自行貼上。"; }
    public bool Contains(int x, int y) => IsVisible && Native.GetWindowRect(handle, out var r) && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    private static FlowDocument RenderMarkdown(string text)
    {
        var document = new FlowDocument { FontFamily = new("Segoe UI, Microsoft JhengHei UI"), FontSize = 14, PagePadding = new(12), Background = Brushes.White };
        var code = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("```", StringComparison.Ordinal)) { code = !code; continue; }
            var line = raw; var size = 14d; var bold = false;
            if (!code && line.StartsWith('#')) { var n = line.TakeWhile(c => c == '#').Count(); if (n <= 6 && line.Length > n && line[n] == ' ') { size = Math.Max(15, 29 - n * 3); bold = true; line = line[(n + 1)..]; } }
            if (!code && (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))) line = "•  " + line[2..];
            var paragraph = new Paragraph(new Run(line)) { FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new(0, code ? 0 : 3, 0, code ? 0 : 8), Padding = code ? new(8, 2, 8, 2) : new(0) };
            if (code) { paragraph.FontFamily = new("Consolas"); paragraph.Background = ActionUi.Canvas; }
            document.Blocks.Add(paragraph);
        }
        return document;
    }
}
