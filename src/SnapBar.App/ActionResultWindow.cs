using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class ActionResultWindow : Window
{
    private readonly Button? applyButton;
    private readonly TextBlock? hint;
    private readonly bool diagnosticOnly;
    private nint handle;
    private readonly DispatcherTimer? timer;
    internal bool IsLookupPinned { get; private set; }
    public ActionResultWindow(string name, string text, bool markdown, bool canApply, Action? apply = null, bool hud = false,
        ScriptDiagnostic? diagnostic = null, bool diagnosticOnly = false, bool lookup = false, bool messageOnly = false,
        Action? openSettings = null, string openSettingsLabel = "管理詞典",
        IReadOnlyList<DictionaryMatch>? lookupMatches = null, string? lookupWarning = null)
    {
        this.diagnosticOnly = diagnosticOnly;
        if (diagnostic != null) hud = false;
        ActionUi.Setup(this, name + (diagnosticOnly ? " · SnapBar 執行診斷" : " · SnapBar 結果"),
            hud || lookup ? 420 : 650, hud ? 180 : 520);
        MinHeight = hud ? 120 : 280; MinWidth = 320; ShowActivated = false; ShowInTaskbar = !hud && !lookup; Topmost = true;
        var body = new DockPanel { Margin = new(20) }; var heading = ActionUi.Text(name, 18); heading.Margin = new(0, 0, 0, 15); DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading);
        if (lookup)
        {
            WindowStyle = WindowStyle.None;
            MinHeight = 0;
            Height = double.NaN;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            body.Margin = new(16);
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new(0, 0, 0, 8);
        }
        if (diagnostic != null)
        {
            var view = new ActionDiagnosticView(); view.Show(diagnostic);
            if (diagnosticOnly)
            {
                var close = ActionUi.Button("關閉", Close); close.HorizontalAlignment = HorizontalAlignment.Right;
                DockPanel.SetDock(close, Dock.Bottom); body.Children.Add(close);
            }
            var scroll = new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = diagnosticOnly ? double.PositiveInfinity : 200 };
            if (!diagnosticOnly) DockPanel.SetDock(scroll, Dock.Top);
            body.Children.Add(scroll);
        }
        if (!diagnosticOnly)
        {
            var footer = new StackPanel { Margin = new(0, 12, 0, 0) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            hint = ActionUi.Text(canApply ? "套用前會重新驗證原視窗、焦點及選取；結果也會放入剪貼簿。" : "來源不可寫入或已變更；可以複製結果。", 11, ActionUi.Muted);
            if (!messageOnly) buttons.Children.Add(ActionUi.Button("複製結果", () =>
            { try { Clipboard.SetText(text, TextDataFormat.UnicodeText); hint.Text = "已複製結果。"; } catch (System.Runtime.InteropServices.ExternalException) { hint.Text = "剪貼簿忙碌，請稍後再試。"; } }, "copy"));
            if (!lookup)
            {
                applyButton = ActionUi.Button("套用至原文", () => { applyButton!.IsEnabled = false; apply?.Invoke(); });
                applyButton.IsEnabled = canApply;
                buttons.Children.Add(applyButton);
            }
            if (openSettings != null) buttons.Children.Add(ActionUi.Button(openSettingsLabel, () => { Close(); openSettings(); }));
            if (lookup)
            {
                var pin = ActionUi.Button("釘住", () => { });
                pin.ToolTip = "釘住查詞小窗，切換視窗或截圖時保持顯示";
                AutomationProperties.SetAutomationId(pin, "PinLookupResult");
                pin.Click += (_, _) =>
                {
                    IsLookupPinned = !IsLookupPinned;
                    var label = IsLookupPinned ? "取消釘住" : "釘住";
                    ((TextBlock)((StackPanel)pin.Content).Children[0]).Text = label;
                    AutomationProperties.SetName(pin, label);
                };
                buttons.Children.Add(pin);
            }
            buttons.Children.Add(ActionUi.Button("關閉", Close)); footer.Children.Add(buttons);
            if (!lookup || !messageOnly) footer.Children.Add(hint);
            if (lookup) hint.Text = "離線詞典";
            DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
            if (lookup) body.Children.Add(lookupMatches is { Count: > 0 }
                ? CreateLookupGroups(lookupMatches, lookupWarning) : CreateLookupContent(name, text, messageOnly));
            else if (markdown) body.Children.Add(new FlowDocumentScrollViewer { Document = RenderMarkdown(text), IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsSelectionEnabled = true });
            else body.Children.Add(new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new(0), Padding = new(10), FontSize = 14,
                Background = Brushes.White, FontFamily = new("Consolas, Microsoft JhengHei UI") });
            if (hud) { footer.Visibility = Visibility.Collapsed; timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) }; timer.Tick += (_, _) => Close(); Loaded += (_, _) => timer.Start(); }
        }
        Content = lookup ? new Border { Background = ActionUi.Canvas, BorderBrush = ActionUi.Line, BorderThickness = new(1), Child = body } : body;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, (nint)((long)Native.GetWindowLongPtr(handle, -20) | 0x08000000));
            HwndSource.FromHwnd(handle).AddHook((nint hwnd, int message, nint w, nint l, ref bool handled) =>
            { if (message == Native.WM_MOUSEACTIVATE) { handled = true; return 3; } return 0; });
        };
        Closed += (_, _) => timer?.Stop();
    }
    public void InvalidateTarget()
    {
        if (diagnosticOnly) return;
        if (applyButton != null) applyButton.IsEnabled = false;
        if (hint != null) hint.Text = "原選取已失效；請複製結果後自行貼上。";
    }
    public bool Contains(int x, int y) => IsVisible && Native.GetWindowRect(handle, out var r) && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    internal void PlaceNear(ScreenRect? bounds)
    {
        if (handle == 0) return;
        if (bounds == null)
        {
            if (!Native.GetCursorPos(out var point)) return;
            bounds = new(point.X, point.Y, 1, 1);
        }
        void Place()
        {
            var scale = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
            var work = Native.WorkArea((int)bounds.X, (int)bounds.Y);
            var placed = PopupPlacement.Place(bounds, work, ActualWidth * scale, ActualHeight * scale);
            Native.SetWindowPos(handle, 0, (int)placed.X, (int)placed.Y, (int)placed.Width, (int)placed.Height, 0x14);
        }
        var dpi = Native.GetDpiForWindow(handle);
        Place();
        if (dpi != Native.GetDpiForWindow(handle)) Place();
    }

    private static readonly Regex LookupLabel = new(
        @"^(?:(?:n|v|vi|vt|a|adj|adv|prep|pron|conj|int|interj|num|aux|pl|abbr|vbl)\.|【[^】]{1,12}】|相關詞組[：:])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static FrameworkElement CreateLookupGroups(IReadOnlyList<DictionaryMatch> matches, string? warning)
    {
        var groups = new StackPanel();
        foreach (var match in matches)
        {
            var source = ActionUi.Text(match.DictionaryName, 12, ActionUi.Muted);
            source.FontWeight = FontWeights.SemiBold;
            source.TextWrapping = TextWrapping.Wrap;
            source.Margin = new(0, groups.Children.Count == 0 ? 0 : 12, 0, 6);
            AutomationProperties.SetAutomationId(source, "LookupDictionarySource");
            groups.Children.Add(source);
            if (!string.Equals(match.Headword, matches[0].Headword, StringComparison.OrdinalIgnoreCase))
                groups.Children.Add(ActionUi.Text(match.Headword, 14));
            groups.Children.Add(CreateLookupContent(match.Headword, match.Text, false, wrap: false));
        }
        if (warning != null)
        {
            var message = ActionUi.Text(warning, 12, ActionUi.Muted);
            message.TextWrapping = TextWrapping.Wrap;
            message.Margin = new(0, 12, 0, 0);
            groups.Children.Add(message);
        }
        return new Border { Background = Brushes.White, CornerRadius = new(6), Padding = new(10),
            Child = new ScrollViewer { Content = groups, MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
    }

    private static FrameworkElement CreateLookupContent(string headword, string text, bool messageOnly, bool wrap = true)
    {
        var content = ActionUi.Text("", 14);
        content.Name = "LookupContent";
        content.TextWrapping = TextWrapping.Wrap;
        if (messageOnly)
        {
            content.Text = text;
            return content;
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n', 160);
        var first = 0;
        if (lines.Length > 1 && string.Equals(lines[0].Trim(), headword, StringComparison.OrdinalIgnoreCase))
        {
            first = 1;
            while (first < lines.Length - 1 && string.IsNullOrWhiteSpace(lines[first])) first++;
        }
        for (var index = first; index < lines.Length; index++)
        {
            if (index != first) content.Inlines.Add(new LineBreak());
            var line = lines[index].TrimEnd();
            var label = LookupLabel.Match(line);
            if (index == first && (line.StartsWith("*[") || line.StartsWith('[') && line.EndsWith(']')))
                content.Inlines.Add(new Run(line.StartsWith("*[") ? line[1..] : line) { Foreground = ActionUi.Muted });
            else if (label.Success)
            {
                content.Inlines.Add(new Run(label.Value) { FontWeight = FontWeights.SemiBold });
                content.Inlines.Add(new Run(line[label.Length..]));
            }
            else content.Inlines.Add(new Run(line));
        }
        if (!wrap) return content;
        return new Border
        {
            Background = Brushes.White,
            CornerRadius = new(6),
            Padding = new(10),
            Child = new ScrollViewer
            {
                Content = content,
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };
    }
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
