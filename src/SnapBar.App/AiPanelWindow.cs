using System.Runtime.InteropServices;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class AiPanelWindow : Window
{
    private readonly AiPreferences preferences;
    private readonly IAiTextClient client;
    private readonly bool ownsClient;
    private readonly Func<string, Task<bool>>? apply;
    private readonly SelectionSnapshot selection;
    private readonly TextBox input = null!, original;
    private readonly TextBlock basis, hint, count, status, version, execution, placeholder, modelLabel;
    private readonly Button direct, stop, restart, resume, originalTab, resultTab, previous, next, copy, replace, model;
    private readonly StackPanel commandRows;
    private readonly List<Button> commandButtons = [];
    private readonly Dictionary<long, string> instructionNames = [];
    private readonly ComboBox category;
    private readonly ContentControl resultContent;
    private readonly Border resultCard;
    private readonly ProgressBar progress;
    private readonly Grid main;
    private readonly Expander filter;
    private CancellationTokenSource? request;
    private AiProviderProfile lastProfile;
    private string? renderedText;
    private bool syncing, composing, closed, targetValid = true, applying;
    private long interactionRevision;
    internal AiSession Session { get; }
    internal TextBox CommandInput => input;
    internal bool TargetValid => targetValid;

    internal AiPanelWindow(SelectionSnapshot selection, AiPreferences preferences, Func<string, Task<bool>>? apply = null, IAiTextClient? client = null)
    {
        this.selection = selection; this.preferences = preferences; this.apply = apply; ownsClient = client == null;
        this.client = client ?? new AiHttpClient(); lastProfile = preferences.SelectedProfile; Session = new(selection.Text);
        ActionUi.Setup(this, "SnapBar · AI 改寫", 800, 650); MinWidth = 680; MinHeight = 500; Topmost = true;
        var root = new DockPanel { Margin = new(24, 18, 24, 20) }; var header = new DockPanel { Margin = new(0, 0, 0, 20) };
        model = QuietButton("", ShowSettings, "settings"); model.HorizontalAlignment = HorizontalAlignment.Right;
        model.MaxWidth = 180; model.Padding = new(8); model.MinHeight = 36;
        modelLabel = ActionUi.Text("AI 設定", 13, ActionUi.Muted); modelLabel.TextWrapping = TextWrapping.NoWrap; modelLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        var modelContent = new DockPanel(); var settingsIcon = ActionUi.Icon("settings", ActionUi.Muted, 16);
        settingsIcon.Margin = new(8, 0, 0, 0); DockPanel.SetDock(settingsIcon, Dock.Right); modelContent.Children.Add(settingsIcon); modelContent.Children.Add(modelLabel); model.Content = modelContent;
        DockPanel.SetDock(model, Dock.Right); header.Children.Add(model); header.Children.Add(ActionUi.Heading("AI 改寫", 24));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        progress = new ProgressBar { Height = 2, IsIndeterminate = true, Visibility = Visibility.Hidden };
        DockPanel.SetDock(progress, Dock.Top); root.Children.Add(progress);
        main = new Grid(); main.ColumnDefinitions.Add(new() { Width = new(280) }); main.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var rail = new DockPanel { Margin = new(0, 0, 20, 0) };
        var railHeader = new StackPanel(); var heading = new DockPanel();
        var add = QuietButton("新增", () => EditPrompt(null), "plus"); add.Padding = new(6); add.MinHeight = 28;
        stop = ActionUi.Button("停止", Stop); stop.Padding = new(5); stop.MinHeight = 28; stop.Margin = new(0);
        DockPanel.SetDock(add, Dock.Right); heading.Children.Add(add); DockPanel.SetDock(stop, Dock.Right); heading.Children.Add(stop);
        heading.Children.Add(ActionUi.Heading("想怎麼處理？", 14)); railHeader.Children.Add(heading);
        var basisRow = new DockPanel { MinHeight = 28, Margin = new(0, 8, 0, 6) };
        var basisButtons = new StackPanel { Orientation = Orientation.Horizontal };
        restart = QuietButton("改用原文", () => { Session.FromOriginal(); Refresh(); input.Focus(); });
        resume = QuietButton("改用這一版", () => { Session.ContinueCurrent(); Refresh(); input.Focus(); });
        basisButtons.Children.Add(resume); basisButtons.Children.Add(restart); DockPanel.SetDock(basisButtons, Dock.Right); basisRow.Children.Add(basisButtons);
        basis = ActionUi.Text("下次改寫：原文", 12, ActionUi.Muted);
        basis.TextWrapping = TextWrapping.NoWrap; basis.TextTrimming = TextTrimming.CharacterEllipsis;
        basisRow.Children.Add(basis); railHeader.Children.Add(basisRow);
        input = ActionUi.Input("", 8192); input.MinHeight = 60; input.MaxHeight = 120; input.MinLines = 2; input.MaxLines = 5;
        input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap;
        input.VerticalContentAlignment = VerticalAlignment.Top;
        input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; AutomationProperties.SetAutomationId(input, "AiCommandInput");
        AutomationProperties.SetName(input, "搜尋指令或輸入自己的要求");
        placeholder = ActionUi.Text("搜尋指令，或說明怎麼處理原文…", 12, ActionUi.Muted);
        placeholder.Margin = new(10, 8, 10, 0); placeholder.VerticalAlignment = VerticalAlignment.Top; placeholder.IsHitTestVisible = false;
        var inputShell = new Grid(); inputShell.Children.Add(input); inputShell.Children.Add(placeholder); railHeader.Children.Add(inputShell);
        var shortcuts = ActionUi.Text("Enter 執行 · ↓ 選指令", 12, ActionUi.Muted); shortcuts.Margin = new(0, 6, 0, 0);
        shortcuts.ToolTip = "Shift+Enter 換行"; input.ToolTip = "Enter 執行 · ↓ 選指令 · Shift+Enter 換行"; railHeader.Children.Add(shortcuts);
        direct = ActionUi.Button("直接執行這段指令 ↵", () => _ = SubmitAsync(input.Text), primary: true); direct.Margin = new(0, 8, 0, 0);
        AutomationProperties.SetAutomationId(direct, "AiDirectCommand"); railHeader.Children.Add(direct);
        category = new ComboBox { MinHeight = 30, Margin = new(0, 6, 0, 0) };
        filter = new Expander { Header = "分類篩選", Content = category, Margin = new(0, 6, 0, 4), IsExpanded = false };
        railHeader.Children.Add(filter); DockPanel.SetDock(railHeader, Dock.Top); rail.Children.Add(railHeader);
        count = ActionUi.Text("", 12, ActionUi.Muted); count.Margin = new(4, 6, 0, 0); DockPanel.SetDock(count, Dock.Bottom); rail.Children.Add(count);
        commandRows = new StackPanel(); var commandScroll = new ScrollViewer { Content = commandRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetAutomationId(commandScroll, "AiCommandList"); rail.Children.Add(commandScroll);
        main.Children.Add(rail);
        var right = new DockPanel(); Grid.SetColumn(right, 1); main.Children.Add(right);
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(12, 0, 0, 0) };
        copy = ActionUi.Button("複製結果", Copy); replace = ActionUi.Button("替換原文", () => _ = ApplyAsync(), primary: true);
        AutomationProperties.SetAutomationId(copy, "AiCopy"); AutomationProperties.SetAutomationId(replace, "AiReplace");
        copy.Margin = new(0); replace.Margin = new(8, 0, 0, 0);
        actions.Children.Add(copy); actions.Children.Add(replace); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        status = ActionUi.Text("", 12, ActionUi.Muted); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); right.Children.Add(footer);
        AutomationProperties.SetAutomationId(status, "AiSourceStatus");
        var body = new DockPanel();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 10) };
        originalTab = ActionUi.Button("原文", () => { Session.ShowOriginal(); Refresh(); }); resultTab = ActionUi.Button("結果", () => { Session.ShowResult(); Refresh(); });
        foreach (var tab in new[] { originalTab, resultTab })
        {
            tab.Style = (Style)FindResource("AiViewTab"); tab.ClearValue(BackgroundProperty); tab.ClearValue(BorderBrushProperty);
            tab.ClearValue(ForegroundProperty); tab.ClearValue(PaddingProperty); tab.Content = tab == originalTab ? "原文" : "結果";
        }
        AutomationProperties.SetAutomationId(originalTab, "AiOriginalTab"); AutomationProperties.SetAutomationId(resultTab, "AiResultTab");
        tabs.Children.Add(originalTab); tabs.Children.Add(resultTab); DockPanel.SetDock(tabs, Dock.Top); body.Children.Add(tabs);
        hint = ActionUi.Text("", 12, ActionUi.Muted); hint.Margin = new(0, 10, 0, 0); DockPanel.SetDock(hint, Dock.Bottom); body.Children.Add(hint);
        original = AiMarkdownView.Plain(selection.Text); AutomationProperties.SetAutomationId(original, "AiOriginalText");
        var resultBody = new DockPanel(); var resultHeader = new DockPanel { Margin = new(20, 8, 12, 0) };
        var history = new StackPanel { Orientation = Orientation.Horizontal };
        previous = QuietButton("‹", () => { Session.MoveHistory(-1); Refresh(); }); previous.ToolTip = "上一版";
        next = QuietButton("›", () => { Session.MoveHistory(1); Refresh(); }); next.ToolTip = "下一版";
        AutomationProperties.SetAutomationId(previous, "AiPreviousVersion"); AutomationProperties.SetAutomationId(next, "AiNextVersion");
        version = ActionUi.Text("", 12, ActionUi.Muted); history.Children.Add(previous); history.Children.Add(version); history.Children.Add(next);
        DockPanel.SetDock(history, Dock.Left); resultHeader.Children.Add(history);
        var details = QuietButton("指令詳情", ShowInstruction); details.ToolTip = "查看完整指令";
        AutomationProperties.SetName(details, "查看完整指令"); AutomationProperties.SetAutomationId(details, "AiInstructionDetails");
        DockPanel.SetDock(details, Dock.Right); resultHeader.Children.Add(details);
        execution = ActionUi.Text("", 12, ActionUi.Muted); execution.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetAutomationId(execution, "AiExecutionLabel");
        execution.Margin = new(8, 0, 4, 0); execution.TextTrimming = TextTrimming.CharacterEllipsis; execution.MaxHeight = 24; resultHeader.Children.Add(execution);
        DockPanel.SetDock(resultHeader, Dock.Top); resultBody.Children.Add(resultHeader);
        resultContent = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetAutomationId(resultContent, "AiResultContent");
        resultBody.Children.Add(resultContent); resultCard = ActionUi.Card(resultBody, new(0));
        var content = new Grid(); content.Children.Add(ActionUi.Card(original, new(0))); content.Children.Add(resultCard); body.Children.Add(content);
        right.Children.Add(body); root.Children.Add(main); Content = root;
        preferences.Changed += OnPreferencesChanged;
        input.TextChanged += (_, _) => { if (!syncing) Session.Draft = input.Text; RenderCommands(); };
        category.SelectionChanged += (_, _) => { if (!syncing) RenderCommands(); };
        TextCompositionManager.AddPreviewTextInputStartHandler(input, (_, _) => composing = true);
        input.PreviewTextInput += (_, _) => composing = false;
        input.PreviewKeyUp += (_, e) => { if (e.Key is Key.Enter or Key.Escape || e.Key == Key.ImeProcessed) composing = false; };
        input.PreviewKeyDown += InputKey;
        PreviewKeyDown += WindowKey;
        PreviewMouseDown += (_, _) => interactionRevision++;
        GotKeyboardFocus += (_, _) => interactionRevision++;
        SourceInitialized += (_, _) => PlaceNearSelection();
        Loaded += (_, _) => input.Focus();
        Closed += (_, _) => { closed = true; Stop(); preferences.Changed -= OnPreferencesChanged; if (ownsClient && client is IDisposable disposable) disposable.Dispose(); };
        RefreshCategories(); Refresh();
        if (preferences.Warning != null) hint.Text = "AI 設定無法讀取，本次使用預設值並保留原檔。";
    }

    private static Button QuietButton(string text, Action click, string? icon = null)
    {
        var button = ActionUi.Button(text, click, icon); button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent; button.Padding = new(6, 4, 6, 4); button.MinHeight = 28; button.Margin = new(0); return button;
    }
    internal bool HasForeground => IsOwnedForeground(this);
    private static bool IsOwnedForeground(Window window) => window.IsVisible && Native.GetForegroundWindow() == new WindowInteropHelper(window).Handle
        || window.OwnedWindows.Cast<Window>().Any(IsOwnedForeground);
    internal bool Contains(int x, int y) => ContainsWindow(this, x, y);
    private static bool ContainsWindow(Window window, int x, int y)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return window.IsVisible && handle != 0 && Native.GetWindowRect(handle, out var r) && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom
            || window.OwnedWindows.Cast<Window>().Any(w => ContainsWindow(w, x, y));
    }
    private void PlaceNearSelection()
    {
        var handle = new WindowInteropHelper(this).Handle; var anchor = selection.Bounds;
        Native.GetCursorPos(out var cursor); var x = (int)(anchor?.X ?? cursor.X); var y = (int)(anchor?.Bottom ?? cursor.Y);
        var work = Native.WorkArea(x, y); var dpi = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
        var width = Math.Min(800 * dpi, work.Width); var height = Math.Min(650 * dpi, work.Height);
        MinWidth = Math.Min(MinWidth, work.Width / dpi); MinHeight = Math.Min(MinHeight, work.Height / dpi);
        Native.SetWindowPos(handle, 0, (int)Math.Clamp(x, work.X, Math.Max(work.X, work.Right - width)),
            (int)Math.Clamp(y + 8, work.Y, Math.Max(work.Y, work.Bottom - height)), (int)width, (int)height, 0x0014);
    }
    private void ShowSettings() { var window = new AiSettingsWindow(preferences) { Owner = this }; window.ShowDialog(); Refresh(); }
    private void ShowInstruction()
    {
        if (Session.Current is not { } current) return;
        var window = new Window { Owner = this };
        ActionUi.Setup(window, $"SnapBar · 第 {current.Id} 版指令", 570, 420);
        var body = new DockPanel { Margin = new(16) };
        var close = ActionUi.Button("關閉", window.Close); close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new(0, 10, 0, 0); DockPanel.SetDock(close, Dock.Bottom); body.Children.Add(close);
        var basisLabel = ActionUi.Text("基於" + current.BasisLabel, 12, ActionUi.Muted); basisLabel.Margin = new(0, 0, 0, 10);
        DockPanel.SetDock(basisLabel, Dock.Top); body.Children.Add(basisLabel);
        var text = AiMarkdownView.Plain(current.Instruction); AutomationProperties.SetAutomationId(text, "AiFullInstruction"); body.Children.Add(text);
        window.Content = body; window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
        window.Show(); window.Activate();
    }
    private void OnPreferencesChanged()
    {
        if (lastProfile != preferences.SelectedProfile) { Stop(); lastProfile = preferences.SelectedProfile; }
        RefreshCategories(); RenderCommands(); model.ToolTip = preferences.SelectedProfile.Name + " · " + preferences.SelectedProfile.Model;
    }
    private void RefreshCategories()
    {
        var chosen = (string?)category.SelectedItem ?? "全部分類"; syncing = true;
        category.ItemsSource = new[] { "全部分類" }.Concat(preferences.Settings.Prompts.Select(p => p.Category).Where(c => c.Length > 0).Distinct()).ToArray();
        category.SelectedItem = category.Items.Contains(chosen) ? chosen : "全部分類"; syncing = false;
    }
    private void RenderCommands()
    {
        if (closed) return;
        direct.Visibility = string.IsNullOrWhiteSpace(input.Text) ? Visibility.Collapsed : Visibility.Visible;
        direct.IsEnabled = !Session.Busy; placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        placeholder.Text = Session.BasisId == null ? "搜尋指令，或說明怎麼處理原文…" : "接著怎麼改？也能搜尋指令…";
        direct.ToolTip = input.Text; commandRows.Children.Clear(); commandButtons.Clear();
        var settings = preferences.Settings; var chosen = (string?)category.SelectedItem;
        var items = AiPolicy.Search(settings, input.Text, chosen == "全部分類" ? "" : chosen ?? "");
        string? group = null;
        foreach (var prompt in items)
        {
            var name = settings.PinnedIds.Contains(prompt.Id) ? "已釘選" : settings.RecentIds.Contains(prompt.Id) ? "最近使用" : prompt.Category;
            if (name != group) { var label = ActionUi.Text(name, 12, ActionUi.Muted); label.Margin = new(5, 6, 0, 2); commandRows.Children.Add(label); group = name; }
            var row = new Border { Style = (Style)FindResource("AiPromptRow"), Margin = new(0, 0, 0, 1) };
            var line = new DockPanel(); row.Child = line;
            Button RowTool(string icon, Action click)
            {
                var tool = QuietButton("", click, icon); tool.Style = (Style)FindResource("AiRowTool");
                tool.Width = 28; tool.MinHeight = 32; return tool;
            }
            var options = RowTool("more", () => { }); options.ToolTip = "指令選項";
            AutomationProperties.SetName(options, "指令選項 " + prompt.Name);
            var menu = new ContextMenu(); var edit = new MenuItem { Header = "編輯指令" }; edit.Click += (_, _) => EditPrompt(prompt);
            var delete = new MenuItem { Header = "刪除指令" }; delete.Click += (_, _) => DeletePrompt(prompt);
            menu.Items.Add(edit); menu.Items.Add(delete); options.ContextMenu = menu; options.Click += (_, _) => { menu.PlacementTarget = options; menu.IsOpen = true; };
            DockPanel.SetDock(options, Dock.Right); line.Children.Add(options);
            var pinned = settings.PinnedIds.Contains(prompt.Id);
            var pin = RowTool("pin", () => TogglePin(prompt)); pin.ToolTip = pinned ? "取消釘選" : "釘選";
            AutomationProperties.SetAutomationId(pin, "AiPin_" + prompt.Id);
            AutomationProperties.SetName(pin, "釘選 " + prompt.Name); DockPanel.SetDock(pin, Dock.Right); line.Children.Add(pin);
            var button = ActionUi.Button(prompt.Name, () => _ = SubmitAsync(prompt.Instruction, prompt.Id), prompt.Icon);
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.BorderThickness = new(0); button.Margin = new(0); button.IsEnabled = !Session.Busy;
            button.Background = Brushes.Transparent; button.Padding = new(8, 9, 4, 9); button.MinHeight = 40;
            if (pinned && button.Content is StackPanel commandContent)
            { var indicator = ActionUi.Icon("pin", ActionUi.Muted, 11); indicator.Margin = new(6, 0, 0, 0); commandContent.Children.Add(indicator); }
            button.ToolTip = prompt.Name; AutomationProperties.SetAutomationId(button, "AiPrompt_" + prompt.Id); commandButtons.Add(button);
            button.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Up or Key.Down) { var index = commandButtons.IndexOf(button) + (e.Key == Key.Down ? 1 : -1); if (index < 0) input.Focus(); else if (index < commandButtons.Count) commandButtons[index].Focus(); e.Handled = true; }
                else if (e.Key == Key.Enter) { e.Handled = true; _ = SubmitAsync(prompt.Instruction, prompt.Id); }
            };
            line.Children.Add(button); commandRows.Children.Add(row);
        }
        if (items.Count == 0) commandRows.Children.Add(ActionUi.Text(input.Text.Length > 0 ? "沒有符合的已存指令，仍可直接執行輸入內容。" : "這個分類尚無指令。", 12, ActionUi.Muted));
        count.Text = (input.Text.Trim().Length > 0 ? "搜尋全部 · " : "") + $"{items.Count} / {settings.Prompts.Length} 個指令";
    }
    private void Refresh()
    {
        if (closed) return;
        syncing = true; if (input.Text != Session.Draft) { input.Text = Session.Draft; input.CaretIndex = input.Text.Length; } syncing = false;
        basis.Text = "下次改寫：" + Session.BasisLabel; basis.ToolTip = basis.Text;
        restart.Visibility = Session.BasisId == null ? Visibility.Collapsed : Visibility.Visible;
        var profile = preferences.SelectedProfile;
        modelLabel.Text = profile.Model.Length == 0 ? "設定 AI 服務" : profile.Name;
        AutomationProperties.SetName(model, "AI 服務與模型設定"); model.ToolTip = profile.Name + " · " + profile.Model;
        resume.Visibility = Session.Current != null && Session.BasisId != Session.Current.Id ? Visibility.Visible : Visibility.Collapsed;
        resume.Content = Session.Current is { } shown ? $"改用第 {shown.Id} 版" : "改用這一版";
        restart.IsEnabled = resume.IsEnabled = !Session.Busy;
        stop.Visibility = Session.Busy ? Visibility.Visible : Visibility.Collapsed;
        progress.Visibility = Session.Busy ? Visibility.Visible : Visibility.Hidden;
        original.Parent.SetValue(VisibilityProperty, Session.ViewingOriginal ? Visibility.Visible : Visibility.Collapsed);
        resultCard.Visibility = Session.ViewingOriginal ? Visibility.Collapsed : Visibility.Visible;
        resultTab.IsEnabled = Session.Current != null; resultTab.Content = Session.HasNewResult ? "結果 · 新" : Session.Busy ? "結果 · 產生中" : "結果";
        originalTab.Tag = Session.ViewingOriginal ? "Selected" : ""; resultTab.Tag = !Session.ViewingOriginal ? "Selected" : "";
        previous.IsEnabled = !Session.Busy && Session.Index > 0; next.IsEnabled = !Session.Busy && Session.Index + 1 < Session.Versions.Count;
        previous.Visibility = next.Visibility = Session.Versions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (Session.Current is { } current)
        {
            version.Text = $"第 {current.Id} 版"; execution.Text = "· " + instructionNames.GetValueOrDefault(current.Id, "自訂指令");
            if (renderedText != current.Text) { renderedText = current.Text; resultContent.Content = AiMarkdownView.Create(current.Text); }
        }
        copy.Content = Session.ViewingOriginal ? "複製原文" : "複製結果";
        var writable = targetValid && SessionPolicy.CanWrite(selection, selection.Target) && apply != null;
        replace.Visibility = !Session.ViewingOriginal && writable ? Visibility.Visible : Visibility.Collapsed; replace.IsEnabled = !Session.Busy && !applying;
        var primaryCopy = !Session.ViewingOriginal && !writable;
        copy.Background = copy.BorderBrush = primaryCopy ? ActionUi.Accent : Brushes.White;
        if (!primaryCopy) copy.BorderBrush = ActionUi.Line;
        copy.Foreground = primaryCopy ? Brushes.White : ActionUi.Ink;
        copy.Resources["ControlHover"] = primaryCopy ? ActionUi.Brush("#2E58BE") : ActionUi.Brush("#EEF1F6");
        copy.Resources["ControlPressed"] = primaryCopy ? ActionUi.Brush("#24489F") : ActionUi.Brush("#E2E7F0");
        status.Text = !targetValid
            ? Session.ViewingOriginal ? "原選取已失效，仍可複製原文。" : "原選取已失效，請複製結果後自行貼上。"
            : !SessionPolicy.CanWrite(selection, selection.Target)
                ? Session.ViewingOriginal ? "此處無法直接替換，可複製原文。" : "此處無法直接替換，請複製結果。"
                : Session.ViewingOriginal ? "選擇指令後才傳送文字。" : "替換時也會複製結果。";
        RenderCommands();
    }
    internal async Task SubmitAsync(string instruction, string? promptId = null)
    {
        if (closed || Session.Busy || string.IsNullOrWhiteSpace(instruction)) return;
        var instructionName = preferences.Settings.Prompts.FirstOrDefault(p => p.Id == promptId)?.Name ?? "自訂指令";
        var profile = preferences.SelectedProfile;
        if (AiPolicy.ValidateProfile(profile) != null) { hint.Text = "請先設定 AI 服務網址與模型 ID。"; return; }
        var key = preferences.Secrets.Get(profile) ?? "";
        if (profile.Protocol == AiProtocol.Responses && key.Length == 0) { hint.Text = "尚未設定 API Key，請開啟上方服務／模型設定。"; return; }
        AiRequestTicket ticket;
        try { ticket = Session.Begin(instruction); } catch (ArgumentException) { hint.Text = "指令或來源文字超過可處理的長度。"; return; }
        using var cancellation = new CancellationTokenSource(); request = cancellation; var interaction = interactionRevision;
        hint.Text = "正在處理，可按停止取消。"; Refresh();
        try
        {
            var result = await client.ExecuteAsync(profile, key, ticket.SourceText, ticket.Instruction, cancellation.Token);
            if (closed || cancellation.IsCancellationRequested) return;
            if (!result.Success) { Session.Cancel(); hint.Text = ErrorMessage(result.Status); return; }
            if (!Session.Complete(ticket, result.Text)) { hint.Text = "沒有可用的新結果，上一版已保留。"; return; }
            instructionNames[Session.Current!.Id] = instructionName;
            foreach (var id in instructionNames.Keys.Where(id => !Session.Versions.Any(v => v.Id == id)).ToArray()) instructionNames.Remove(id);
            hint.Text = "";
            if (promptId != null)
            {
                var settings = preferences.Settings;
                if (!preferences.TrySave(settings with { RecentIds = new[] { promptId }.Concat(settings.RecentIds).Distinct().Take(10).ToArray() }, out _))
                    hint.Text = "結果已完成，但最近使用無法保存。";
            }
            Refresh();
            if (interaction == interactionRevision && !Session.ViewingOriginal && Session.Draft.Length == 0 && HasForeground) input.Focus();
        }
        catch (OperationCanceledException) { if (!closed && ReferenceEquals(request, cancellation)) hint.Text = "已停止，上一版已保留。"; }
        catch (Exception e) when (e is InvalidOperationException or System.IO.IOException or HttpRequestException or ObjectDisposedException)
        { if (!closed && ReferenceEquals(request, cancellation) && !cancellation.IsCancellationRequested) hint.Text = "AI 請求未完成，上一版已保留。"; }
        finally { if (ReferenceEquals(request, cancellation)) { request = null; Session.Cancel(); if (!closed) Refresh(); } }
    }
    private static string ErrorMessage(string code) => code switch
    {
        "key-required" or "authentication" => "API Key 無效或未設定，請開啟服務設定。",
        "settings-required" => "請檢查服務網址與模型 ID。", "rate-limit" => "服務目前限制請求，請稍後再試。",
        "timeout" => "服務回應逾時，上一版已保留。", "network" => "無法連上 AI 服務，請檢查連線及網址。",
        "too-large" => "文字或回應超過限制，上一版已保留。", "incomplete" => "服務回傳的結果不完整，上一版已保留。",
        "refused" => "服務未提供可用結果，上一版已保留。", _ => "服務回應格式無效，上一版已保留。"
    };
    private void Stop() { request?.Cancel(); Session.Cancel(); if (!closed) Refresh(); }
    internal void InvalidateTarget() { targetValid = false; Stop(); if (!closed) { hint.Text = "來源已變更，結果仍可複製。"; Refresh(); } }
    internal async Task ApplyAsync()
    {
        if (closed || applying || Session.Busy || !targetValid || apply == null || Session.Current == null || Session.ViewingOriginal
            || !SessionPolicy.CanWrite(selection, selection.Target)) return;
        applying = true; Refresh();
        try { if (await apply(Session.Current.Text)) Close(); else { targetValid = false; hint.Text = "無法替換原文，請複製結果後自行貼上。"; } }
        finally { applying = false; Refresh(); }
    }
    private void Copy()
    { try { Clipboard.SetText(Session.ViewingOriginal ? Session.Original : Session.Current?.Text ?? ""); hint.Text = "已複製。"; } catch (ExternalException) { hint.Text = "剪貼簿忙碌，請稍後再試。"; } }
    private void TogglePin(AiPrompt prompt)
    {
        var settings = preferences.Settings; var pins = settings.PinnedIds.Contains(prompt.Id) ? settings.PinnedIds.Where(id => id != prompt.Id).ToArray() : settings.PinnedIds.Append(prompt.Id).ToArray();
        if (!preferences.TrySave(settings with { PinnedIds = pins }, out var error)) hint.Text = error;
    }
    private void EditPrompt(AiPrompt? prompt)
    {
        var window = new AiPromptEditorWindow(prompt, changed =>
        {
            var settings = preferences.Settings;
            var prompts = prompt == null ? settings.Prompts.Append(changed).ToArray() : settings.Prompts.Select(p => p.Id == changed.Id ? changed : p).ToArray();
            return preferences.TrySave(settings with { Prompts = prompts }, out var error) ? null : error;
        }) { Owner = this }; window.ShowDialog();
    }
    private void DeletePrompt(AiPrompt prompt)
    { var settings = preferences.Settings; if (!preferences.TrySave(settings with { Prompts = settings.Prompts.Where(p => p.Id != prompt.Id).ToArray() }, out var error)) hint.Text = error; }
    private void InputKey(object sender, KeyEventArgs e)
    {
        if (composing || e.Key == Key.ImeProcessed) return;
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; _ = SubmitAsync(input.Text); }
        else if (e.Key == Key.Down && input.CaretIndex == input.Text.Length && input.SelectionLength == 0 && commandButtons.Count > 0 && !Session.Busy)
        { commandButtons[0].Focus(); e.Handled = true; }
    }
    private void WindowKey(object sender, KeyEventArgs e)
    {
        if (composing || e.Key == Key.ImeProcessed) return;
        if (e.Key == Key.Escape) { e.Handled = true; if (input.Text.Length > 0) input.Clear(); else Close(); }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; _ = ApplyAsync(); }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && !input.IsKeyboardFocusWithin)
        {
            var selected = Keyboard.FocusedElement is TextBox box && box.SelectionLength > 0 || resultContent.Content is FlowDocumentScrollViewer viewer && !viewer.Selection.IsEmpty;
            if (!selected) { e.Handled = true; Copy(); }
        }
    }
}
