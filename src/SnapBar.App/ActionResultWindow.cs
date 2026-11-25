using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly UiLanguage language = UiText.Capture();
    private readonly Button? applyButton;
    private readonly Button? copyButton;
    private readonly FlowDocumentScrollViewer? resultViewer;
    private readonly TextBlock? hint;
    private readonly bool diagnosticOnly;
    private readonly bool compactResult;
    private readonly string? staleApplyHint;
    private nint handle;
    private readonly DispatcherTimer? timer;
    internal bool IsLookupPinned { get; private set; }
    public ActionResultWindow(string name, string text, bool markdown, bool canApply, Action? apply = null, bool hud = false,
        ScriptDiagnostic? diagnostic = null, bool diagnosticOnly = false, bool lookup = false, bool messageOnly = false,
        Action? openSettings = null, string? openSettingsLabel = null,
        IReadOnlyList<DictionaryMatch>? lookupMatches = null, string? lookupWarning = null, string? applyLabel = null,
        string? applyHint = null, string? staleApplyHint = null, bool persistent = false)
    {
        this.diagnosticOnly = diagnosticOnly;
        compactResult = !hud && !lookup && !diagnosticOnly && diagnostic == null && !messageOnly;
        this.staleApplyHint = staleApplyHint;
        if (diagnostic != null) hud = false;
        ActionUi.Setup(this, UiText.Join(name, (diagnosticOnly ? language.Get("ActionResultWindow_001") : language.Get("ActionResultWindow_002"))),
            hud || lookup ? 420 : compactResult ? 560 : 650, hud ? 180 : 520);
        MinHeight = hud ? 120 : compactResult ? 200 : 280; MinWidth = 320; ShowActivated = false; ShowInTaskbar = !hud && !lookup; Topmost = true;
        if (compactResult)
        {
            Height = double.NaN;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 620;
        }
        var body = new DockPanel { Margin = new(20) };
        var header = new DockPanel { Margin = new(0, 0, 0, 12), LastChildFill = true };
        var heading = ActionUi.Text(compactResult ? language.Get("ActionResultWindow_Heading") : name, compactResult ? 14 : 18);
        heading.FontWeight = FontWeights.SemiBold;
        DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        if (persistent)
        {
            var onTop = new ToggleButton
            {
                Content = ActionUi.Icon("pin", ActionUi.Accent, 16), IsChecked = true,
                Style = (Style)FindResource("AiCommandPin"), Margin = new(12, 0, 0, 0),
                ToolTip = language.Get("ActionResult_KeepOnTopHint")
            };
            AutomationProperties.SetAutomationId(onTop, "KeepActionResultOnTop");
            AutomationProperties.SetName(onTop, language.Get("ActionResult_KeepOnTop"));
            onTop.Checked += (_, _) => Topmost = true;
            onTop.Unchecked += (_, _) => Topmost = false;
            DockPanel.SetDock(onTop, Dock.Right); header.Children.Add(onTop);
        }
        header.Children.Add(heading);
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
                var close = ActionUi.Button(language.Get("ActionResultWindow_003"), Close); close.HorizontalAlignment = HorizontalAlignment.Right;
                DockPanel.SetDock(close, Dock.Bottom); body.Children.Add(close);
            }
            var scroll = new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = diagnosticOnly ? double.PositiveInfinity : 200 };
            if (!diagnosticOnly) DockPanel.SetDock(scroll, Dock.Top);
            body.Children.Add(scroll);
        }
        if (!diagnosticOnly)
        {
            var footer = new StackPanel { Margin = new(0, 12, 0, 0) };
            Panel buttons = compactResult ? new WrapPanel() : new StackPanel { Orientation = Orientation.Horizontal };
            hint = ActionUi.Text(canApply ? applyHint ?? language.Get("ActionResultWindow_004") : staleApplyHint ?? language.Get("ActionResultWindow_005"), 12, ActionUi.Muted);
            hint.Margin = new(0, 8, 0, 0);
            if (!messageOnly)
            {
                copyButton = ActionUi.Button(language.Get(compactResult ? "ActionResult_CopyAll" : "ActionResultWindow_006"), () =>
                {
                    var output = resultViewer?.Selection is { IsEmpty: false } selected ? selected.Text : text;
                    try { Clipboard.SetText(output, TextDataFormat.UnicodeText); hint.Text = language.Get("ActionResultWindow_007"); }
                    catch (System.Runtime.InteropServices.ExternalException) { hint.Text = language.Get("ActionResultWindow_008"); }
                }, "copy", primary: compactResult);
                buttons.Children.Add(copyButton);
            }
            if (!lookup)
            {
                applyButton = ActionUi.Button(applyLabel ?? language.Get("ActionResultWindow_009"), () => { applyButton!.IsEnabled = false; apply?.Invoke(); });
                applyButton.IsEnabled = canApply;
                if (compactResult && !canApply) applyButton.Visibility = Visibility.Collapsed;
                buttons.Children.Add(applyButton);
            }
            if (openSettings != null) buttons.Children.Add(ActionUi.Button(openSettingsLabel ?? language.Get("ActionResultWindow_000"), () => { Close(); openSettings(); }));
            if (lookup)
            {
                var pin = ActionUi.Button(language.Get("ActionResultWindow_010"), () => { });
                pin.ToolTip = language.Get("ActionResultWindow_011");
                AutomationProperties.SetAutomationId(pin, "PinLookupResult");
                pin.Click += (_, _) =>
                {
                    IsLookupPinned = !IsLookupPinned;
                    var label = IsLookupPinned ? language.Get("ActionResultWindow_012") : language.Get("ActionResultWindow_013");
                    ((TextBlock)((StackPanel)pin.Content).Children[0]).Text = label;
                    AutomationProperties.SetName(pin, label);
                };
                buttons.Children.Add(pin);
            }
            buttons.Children.Add(ActionUi.Button(language.Get("ActionResultWindow_014"), Close)); footer.Children.Add(buttons);
            if (!lookup || !messageOnly) footer.Children.Add(hint);
            if (lookup) hint.Text = language.Get("ActionResultWindow_015");
            DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
            if (lookup) body.Children.Add(lookupMatches is { Count: > 0 }
                ? CreateLookupGroups(lookupMatches, lookupWarning) : CreateLookupContent(name, text, messageOnly));
            else if (compactResult)
            {
                body.Children.Add(ActionResultContent.Create(text, markdown, RenderMarkdown, out var viewer));
                resultViewer = viewer;
                AutomationProperties.SetAutomationId(viewer, "ActionResultText");
                // Keep presentation and footer clicks passive, but let intentional text interaction
                // activate the window so selection and keyboard commands work normally.
                viewer.PreviewMouseLeftButtonDown += (_, _) => { Activate(); viewer.Focus(); };
                viewer.Loaded += (_, _) => viewer.Selection.Changed += (_, _) =>
                {
                    var label = language.Get(viewer.Selection.IsEmpty ? "ActionResult_CopyAll" : "ActionResult_CopySelected");
                    ((StackPanel)copyButton!.Content).Children.OfType<TextBlock>().Last().Text = label;
                    AutomationProperties.SetName(copyButton, label);
                };
            }
            else if (markdown) body.Children.Add(new FlowDocumentScrollViewer { Document = RenderMarkdown(text), IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsSelectionEnabled = true });
            else body.Children.Add(new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new(0), Padding = new(10), FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Top,
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
        if (applyButton != null)
        {
            applyButton.IsEnabled = false;
            if (compactResult) applyButton.Visibility = Visibility.Collapsed;
        }
        if (hint != null) hint.Text = staleApplyHint ?? language.Get("ActionResultWindow_016");
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
