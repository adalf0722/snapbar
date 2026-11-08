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

internal sealed partial class AiPanelWindow : Window
{
    private readonly UiLanguage language = UiText.Capture();
    private string[] categoryValues = [];
    private readonly AiPreferences preferences;
    private readonly IAiTextClient client;
    private readonly bool ownsClient;
    private readonly Func<string, Task<bool>>? apply;
    private readonly SelectionSnapshot? selection;
    private readonly Func<bool>? localCanApply;
    private readonly TextBox input = null!, original;
    private readonly TextBlock basis, hint, count, status, version, execution, placeholder, modelLabel;
    private readonly Button direct, stop, restart, resume, originalTab, resultTab, previous, next, copy, replace, model;
    private readonly Grid commandRows;
    private readonly List<Button> commandButtons = [];
    private readonly Dictionary<long, string> instructionNames = [];
    private readonly ComboBox category;
    private readonly ContentControl resultContent;
    private readonly Border resultCard;
    private readonly ProgressBar progress;
    private readonly Grid main;
    private readonly Expander filter;
    private readonly TextBlock deletionText;
    private readonly Border deletionNotice;
    private CancellationTokenSource? request;
    private AiProviderProfile lastProfile;
    private string? renderedText;
    private bool syncing, composing, closed, targetValid = true, applying;
    private long interactionRevision;
    private Window? managementWindow;
    internal AiSession Session { get; }
    internal TextBox CommandInput => input;
    internal bool TargetValid => targetValid;

    internal AiPanelWindow(SelectionSnapshot selection, AiPreferences preferences, Func<string, Task<bool>>? apply = null, IAiTextClient? client = null)
        : this(selection.Text, preferences, selection, null, apply, client) { }

    internal AiPanelWindow(string text, AiPreferences preferences, Func<bool> canApply, Func<string, Task<bool>> apply, IAiTextClient? client = null)
        : this(text, preferences, null, canApply, apply, client) { }

    private AiPanelWindow(string text, AiPreferences preferences, SelectionSnapshot? selection, Func<bool>? localCanApply,
        Func<string, Task<bool>>? apply, IAiTextClient? client)
    {
        this.selection = selection; this.localCanApply = localCanApply; this.preferences = preferences; this.apply = apply; ownsClient = client == null;
        this.client = client ?? new AiHttpClient(); lastProfile = preferences.SelectedProfile; Session = new(text);
        ActionUi.Setup(this, language.Get("AiPanelWindow_000"), 800, 650); MinWidth = 680; MinHeight = 500; Topmost = true;
        var root = new DockPanel { Margin = new(24, 18, 24, 20) }; var header = new DockPanel { Margin = new(0, 0, 0, 20) };
        model = QuietButton("", ShowSettings, "settings"); model.HorizontalAlignment = HorizontalAlignment.Right;
        model.MaxWidth = 180; model.Padding = new(8); model.MinHeight = 36;
        modelLabel = ActionUi.Text(language.Get("AiPanelWindow_001"), 13, ActionUi.Muted); modelLabel.TextWrapping = TextWrapping.NoWrap; modelLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        var modelContent = new DockPanel(); var settingsIcon = ActionUi.Icon("settings", ActionUi.Muted, 16);
        settingsIcon.Margin = new(8, 0, 0, 0); DockPanel.SetDock(settingsIcon, Dock.Right); modelContent.Children.Add(settingsIcon); modelContent.Children.Add(modelLabel); model.Content = modelContent;
        DockPanel.SetDock(model, Dock.Right); header.Children.Add(model); header.Children.Add(ActionUi.Heading(language.Get("AiPanelWindow_002"), 24));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        progress = new ProgressBar { Height = 2, IsIndeterminate = true, Visibility = Visibility.Hidden };
        DockPanel.SetDock(progress, Dock.Top); root.Children.Add(progress);
        main = new Grid(); main.ColumnDefinitions.Add(new() { Width = new(280) }); main.ColumnDefinitions.Add(new() { Width = new(18) }); main.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var rail = new DockPanel();
        var railHeader = new StackPanel(); var heading = new DockPanel();
        var add = QuietButton(language.Get("AiPanelWindow_003"), () => EditPrompt(null), "plus"); add.Padding = new(6); add.MinHeight = 28;
        stop = ActionUi.Button(language.Get("AiPanelWindow_004"), Stop); stop.Padding = new(5); stop.MinHeight = 28; stop.Margin = new(0);
        DockPanel.SetDock(add, Dock.Right); heading.Children.Add(add); DockPanel.SetDock(stop, Dock.Right); heading.Children.Add(stop);
        var manageCommands = QuietButton("", () => RequestManagement(SettingsCategory.AiCommands), "settings");
        manageCommands.ToolTip = language.Get("AiPanelWindow_005"); AutomationProperties.SetName(manageCommands, language.Get("AiPanelWindow_006"));
        AutomationProperties.SetAutomationId(manageCommands, "ManageAiCommands"); DockPanel.SetDock(manageCommands, Dock.Right); heading.Children.Add(manageCommands);
        heading.Children.Add(ActionUi.Heading(language.Get("AiPanelWindow_007"), 14)); railHeader.Children.Add(heading);
        var basisRow = new DockPanel { MinHeight = 28, Margin = new(0, 8, 0, 6) };
        var basisButtons = new StackPanel { Orientation = Orientation.Horizontal };
        restart = QuietButton(language.Get("AiPanelWindow_008"), () => { Session.FromOriginal(); Refresh(); input.Focus(); });
        resume = QuietButton(language.Get("AiPanelWindow_009"), () => { Session.ContinueCurrent(); Refresh(); input.Focus(); });
        basisButtons.Children.Add(resume); basisButtons.Children.Add(restart); DockPanel.SetDock(basisButtons, Dock.Right); basisRow.Children.Add(basisButtons);
        basis = ActionUi.Text(language.Get("AiPanelWindow_010"), 12, ActionUi.Muted);
        basis.TextWrapping = TextWrapping.NoWrap; basis.TextTrimming = TextTrimming.CharacterEllipsis;
        basisRow.Children.Add(basis); railHeader.Children.Add(basisRow);
        input = ActionUi.Input("", 8192); input.MinHeight = 60; input.MaxHeight = 120; input.MinLines = 2; input.MaxLines = 5;
        input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap;
        input.VerticalContentAlignment = VerticalAlignment.Top;
        input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; AutomationProperties.SetAutomationId(input, "AiCommandInput");
        AutomationProperties.SetName(input, language.Get("AiPanelWindow_011"));
        placeholder = ActionUi.Text(language.Get("AiPanelWindow_012"), 12, ActionUi.Muted);
        placeholder.Margin = new(10, 8, 10, 0); placeholder.VerticalAlignment = VerticalAlignment.Top; placeholder.IsHitTestVisible = false;
        var inputShell = new Grid(); inputShell.Children.Add(input); inputShell.Children.Add(placeholder); railHeader.Children.Add(inputShell);
        var shortcuts = ActionUi.Text(language.Get("AiPanelWindow_013"), 12, ActionUi.Muted); shortcuts.Margin = new(0, 6, 0, 0);
        shortcuts.ToolTip = language.Get("AiPanelWindow_014");
        input.ToolTip = language.Get("AiPanelWindow_015"); railHeader.Children.Add(shortcuts);
        direct = ActionUi.Button(language.Get("AiPanelWindow_016"), () => _ = SubmitAsync(input.Text), primary: true); direct.Margin = new(0, 8, 0, 0);
        AutomationProperties.SetAutomationId(direct, "AiDirectCommand"); railHeader.Children.Add(direct);
        category = new ComboBox { MinHeight = 30, Margin = new(0, 6, 0, 0) };
        filter = new Expander { Header = language.Get("AiPanelWindow_017"), Content = category, Margin = new(0, 6, 0, 4), IsExpanded = false };
        railHeader.Children.Add(filter); DockPanel.SetDock(railHeader, Dock.Top); rail.Children.Add(railHeader);
        var commandFooter = new StackPanel();
        var deletionLine = new DockPanel(); var restoreCommand = QuietButton(language.Get("AiPanelWindow_018"), () => { if (!preferences.TryRestoreDeleted(out var error)) hint!.Text = error == null ? "" : UiText.Current(error, language.Culture); });
        AutomationProperties.SetAutomationId(restoreCommand, "AiRestoreDeletedPrompt"); DockPanel.SetDock(restoreCommand, Dock.Right); deletionLine.Children.Add(restoreCommand);
        deletionText = ActionUi.Text("", 12, ActionUi.Muted); AutomationProperties.SetAutomationId(deletionText, "AiDeletedPromptNotice"); deletionLine.Children.Add(deletionText);
        deletionNotice = new Border { Child = deletionLine, Margin = new(0, 8, 0, 0) }; commandFooter.Children.Add(deletionNotice);
        count = ActionUi.Text("", 12, ActionUi.Muted); count.Margin = new(4, 6, 0, 0); commandFooter.Children.Add(count); DockPanel.SetDock(commandFooter, Dock.Bottom); rail.Children.Add(commandFooter);
        commandRows = new Grid { VerticalAlignment = VerticalAlignment.Top };
        commandRows.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        commandRows.ColumnDefinitions.Add(new() { Width = new(8) });
        commandRows.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var commandScroll = new ScrollViewer { Content = commandRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(commandScroll, "AiCommandList"); rail.Children.Add(commandScroll);
        var commandArea = ActionUi.Card(rail, new(14)); AutomationProperties.SetAutomationId(commandArea, "AiCommandArea"); main.Children.Add(commandArea);
        var right = new DockPanel(); var readerArea = ActionUi.Card(right, new(16));
        AutomationProperties.SetAutomationId(readerArea, "AiReaderArea"); Grid.SetColumn(readerArea, 2); main.Children.Add(readerArea);
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(12, 0, 0, 0) };
        copy = ActionUi.Button(language.Get("AiPanelWindow_019"), Copy); replace = ActionUi.Button(language.Get(selection == null ? "Ocr_Apply" : "AiPanelWindow_020"), () => _ = ApplyAsync(), primary: true);
        AutomationProperties.SetAutomationId(copy, "AiCopy"); AutomationProperties.SetAutomationId(replace, "AiReplace");
        copy.Margin = new(0); replace.Margin = new(8, 0, 0, 0);
        actions.Children.Add(copy); actions.Children.Add(replace); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        status = ActionUi.Text("", 12, ActionUi.Muted); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); right.Children.Add(footer);
        AutomationProperties.SetAutomationId(status, "AiSourceStatus");
        var body = new DockPanel();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 10) };
        originalTab = ActionUi.Button(language.Get("AiPanelWindow_021"), () => { Session.ShowOriginal(); Refresh(); }); resultTab = ActionUi.Button(language.Get("AiPanelWindow_022"), () => { Session.ShowResult(); Refresh(); });
        foreach (var tab in new[] { originalTab, resultTab })
        {
            tab.Style = (Style)FindResource("AiViewTab"); tab.ClearValue(BackgroundProperty); tab.ClearValue(BorderBrushProperty);
            tab.ClearValue(ForegroundProperty); tab.ClearValue(PaddingProperty); tab.Content = tab == originalTab ? language.Get("AiPanelWindow_023") : language.Get("AiPanelWindow_024");
        }
        AutomationProperties.SetAutomationId(originalTab, "AiOriginalTab"); AutomationProperties.SetAutomationId(resultTab, "AiResultTab");
        tabs.Children.Add(originalTab); tabs.Children.Add(resultTab); DockPanel.SetDock(tabs, Dock.Top); body.Children.Add(tabs);
        hint = ActionUi.Text("", 12, ActionUi.Muted); hint.Margin = new(0, 10, 0, 0); DockPanel.SetDock(hint, Dock.Bottom); body.Children.Add(hint);
        original = AiMarkdownView.Plain(text); AutomationProperties.SetAutomationId(original, "AiOriginalText");
        var resultBody = new DockPanel(); var resultHeader = new DockPanel { Margin = new(20, 8, 12, 0) };
        var history = new StackPanel { Orientation = Orientation.Horizontal };
        previous = QuietButton("‹", () => { Session.MoveHistory(-1); Refresh(); }); previous.ToolTip = language.Get("AiPanelWindow_025");
        next = QuietButton("›", () => { Session.MoveHistory(1); Refresh(); }); next.ToolTip = language.Get("AiPanelWindow_026");
        AutomationProperties.SetAutomationId(previous, "AiPreviousVersion"); AutomationProperties.SetAutomationId(next, "AiNextVersion");
        version = ActionUi.Text("", 12, ActionUi.Muted); history.Children.Add(previous); history.Children.Add(version); history.Children.Add(next);
        DockPanel.SetDock(history, Dock.Left); resultHeader.Children.Add(history);
        var details = QuietButton(language.Get("AiPanelWindow_027"), ShowInstruction); details.ToolTip = language.Get("AiPanelWindow_028");
        AutomationProperties.SetName(details, language.Get("AiPanelWindow_029")); AutomationProperties.SetAutomationId(details, "AiInstructionDetails");
        DockPanel.SetDock(details, Dock.Right); resultHeader.Children.Add(details);
        execution = ActionUi.Text("", 12, ActionUi.Muted); execution.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetAutomationId(execution, "AiExecutionLabel");
        execution.Margin = new(8, 0, 4, 0); execution.TextTrimming = TextTrimming.CharacterEllipsis; execution.MaxHeight = 24; resultHeader.Children.Add(execution);
        DockPanel.SetDock(resultHeader, Dock.Top); resultBody.Children.Add(resultHeader);
        resultContent = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetAutomationId(resultContent, "AiResultContent");
        resultBody.Children.Add(resultContent); resultCard = new Border { Child = resultBody };
        var originalCard = new Border { Child = original };
        var content = new Grid(); content.Children.Add(originalCard); content.Children.Add(resultCard); body.Children.Add(content);
        right.Children.Add(body); root.Children.Add(main); Content = root;
        preferences.Changed += OnPreferencesChanged;
        preferences.UndoChanged += RenderCommands;
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
        Activated += (_, _) => Topmost = true;
        Closed += (_, _) => { closed = true; Stop(); preferences.Changed -= OnPreferencesChanged; preferences.UndoChanged -= RenderCommands; if (ownsClient && client is IDisposable disposable) disposable.Dispose(); };
        RefreshCategories(); Refresh();
        if (preferences.Warning != null) hint.Text = language.Get("AiPanelWindow_030");
    }

    private static Button QuietButton(string text, Action click, string? icon = null)
    {
        var button = ActionUi.Button(text, click, icon); button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent; button.Padding = new(6, 4, 6, 4); button.MinHeight = 28; button.Margin = new(0); return button;
    }
    internal bool HasForeground => IsOwnedForeground(this);
    // Settings are a trusted capture-monitoring surface, never an Apply authorization surface.
    internal bool HasCaptureForeground => HasForeground || managementWindow != null && IsOwnedForeground(managementWindow);
    internal bool ContainsCaptureSurface(int x, int y) => Contains(x, y)
        || managementWindow != null && ContainsWindow(managementWindow, x, y);
    internal bool RequestManagement(SettingsCategory category)
    {
        if (preferences.SettingsHost == null) return false;
        managementWindow = preferences.SettingsHost; Topmost = false;
        if (preferences.RequestSettings(category)) return true;
        managementWindow = null; Topmost = true; return false;
    }
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
        var handle = new WindowInteropHelper(this).Handle; var anchor = selection?.Bounds;
        Native.GetCursorPos(out var cursor); var x = (int)(anchor?.X ?? cursor.X); var y = (int)(anchor?.Bottom ?? cursor.Y);
        var work = Native.WorkArea(x, y); var dpi = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
        var width = Math.Min(800 * dpi, work.Width); var height = Math.Min(650 * dpi, work.Height);
        MinWidth = Math.Min(MinWidth, work.Width / dpi); MinHeight = Math.Min(MinHeight, work.Height / dpi);
        Native.SetWindowPos(handle, 0, (int)Math.Clamp(x, work.X, Math.Max(work.X, work.Right - width)),
            (int)Math.Clamp(y + 8, work.Y, Math.Max(work.Y, work.Bottom - height)), (int)width, (int)height, 0x0014);
    }
    private void ShowSettings()
    {
        if (RequestManagement(SettingsCategory.AiServices)) return;
        var window = new AiSettingsWindow(preferences) { Owner = this }; window.ShowDialog(); Refresh();
    }
    private void ShowInstruction()
    {
        if (Session.Current is not { } current) return;
        var detailLanguage = UiText.Capture();
        var window = new Window { Owner = this };
        ActionUi.Setup(window, detailLanguage.Get("AiPanelWindow_031", current.Id), 570, 420);
        var body = new DockPanel { Margin = new(16) };
        var close = ActionUi.Button(detailLanguage.Get("AiPanelWindow_032"), window.Close); close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new(0, 10, 0, 0); DockPanel.SetDock(close, Dock.Bottom); body.Children.Add(close);
        var basisLabel = ActionUi.Text(UiText.Join(detailLanguage.Get("AiPanelWindow_033"), current.BasisId == null ? detailLanguage.Get("Ai_Original") : detailLanguage.Get("Ai_Version", current.BasisId)), 12, ActionUi.Muted); basisLabel.Margin = new(0, 0, 0, 10);
        DockPanel.SetDock(basisLabel, Dock.Top); body.Children.Add(basisLabel);
        var text = AiMarkdownView.Plain(current.Instruction); AutomationProperties.SetAutomationId(text, "AiFullInstruction"); body.Children.Add(text);
        window.Content = body; window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
        window.Show(); window.Activate();
    }
    private void OnPreferencesChanged()
    {
        if (lastProfile != preferences.SelectedProfile) { Stop(); lastProfile = preferences.SelectedProfile; }
        RefreshCategories(); RenderCommands(); model.ToolTip = UiNames.Provider(preferences.SelectedProfile, language) + " · " + preferences.SelectedProfile.Model;
    }
    private void RefreshCategories()
    {
        var chosen = categoryValues.ElementAtOrDefault(category.SelectedIndex) ?? ""; syncing = true;
        var categories = preferences.Settings.Prompts.Where(p => p.Category.Length > 0).DistinctBy(p => p.Category).ToArray();
        categoryValues = new[] { "" }.Concat(categories.Select(p => p.Category)).ToArray();
        category.ItemsSource = new[] { language.Get("AiPanelWindow_035") }.Concat(categories.Select(p => UiNames.Category(p, language))).ToArray();
        category.SelectedIndex = Math.Max(0, Array.IndexOf(categoryValues, chosen)); syncing = false;
    }
    private void Refresh()
    {
        if (closed) return;
        syncing = true; if (input.Text != Session.Draft) { input.Text = Session.Draft; input.CaretIndex = input.Text.Length; } syncing = false;
        basis.Text = UiText.Join(language.Get("AiPanelWindow_037"), BasisName(Session.BasisId)); basis.ToolTip = basis.Text;
        restart.Visibility = Session.BasisId == null ? Visibility.Collapsed : Visibility.Visible;
        var profile = preferences.SelectedProfile;
        modelLabel.Text = profile.Model.Length == 0 ? language.Get("AiPanelWindow_038") : UiNames.Provider(profile, language);
        AutomationProperties.SetName(model, language.Get("AiPanelWindow_039")); model.ToolTip = UiNames.Provider(profile, language) + " · " + profile.Model;
        resume.Visibility = Session.Current != null && Session.BasisId != Session.Current.Id ? Visibility.Visible : Visibility.Collapsed;
        resume.Content = Session.Current is { } shown ? language.Get("AiPanelWindow_040",(shown.Id)): language.Get("AiPanelWindow_041");
        restart.IsEnabled = resume.IsEnabled = !Session.Busy;
        stop.Visibility = Session.Busy ? Visibility.Visible : Visibility.Collapsed;
        progress.Visibility = Session.Busy ? Visibility.Visible : Visibility.Hidden;
        original.Parent.SetValue(VisibilityProperty, Session.ViewingOriginal ? Visibility.Visible : Visibility.Collapsed);
        resultCard.Visibility = Session.ViewingOriginal ? Visibility.Collapsed : Visibility.Visible;
        resultTab.IsEnabled = Session.Current != null; resultTab.Content = Session.HasNewResult ? language.Get("AiPanelWindow_042") : Session.Busy ? language.Get("AiPanelWindow_043") : language.Get("AiPanelWindow_044");
        originalTab.Tag = Session.ViewingOriginal ? "Selected" : ""; resultTab.Tag = !Session.ViewingOriginal ? "Selected" : "";
        previous.IsEnabled = !Session.Busy && Session.Index > 0; next.IsEnabled = !Session.Busy && Session.Index + 1 < Session.Versions.Count;
        previous.Visibility = next.Visibility = Session.Versions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (Session.Current is { } current)
        {
            version.Text = language.Get("AiPanelWindow_045",(current.Id)); execution.Text = UiText.Join("· ", instructionNames.GetValueOrDefault(current.Id, language.Get("AiPanelWindow_046")));
            if (renderedText != current.Text) { renderedText = current.Text; resultContent.Content = language.Run(() => AiMarkdownView.Create(current.Text)); }
        }
        copy.Content = Session.ViewingOriginal ? language.Get("AiPanelWindow_047") : language.Get("AiPanelWindow_048");
        var writable = targetValid && CanApplyTarget() && apply != null;
        replace.Visibility = !Session.ViewingOriginal && writable ? Visibility.Visible : Visibility.Collapsed; replace.IsEnabled = !Session.Busy && !applying;
        var primaryCopy = !Session.ViewingOriginal && !writable;
        copy.Background = copy.BorderBrush = primaryCopy ? ActionUi.Accent : Brushes.White;
        if (!primaryCopy) copy.BorderBrush = ActionUi.Line;
        copy.Foreground = primaryCopy ? Brushes.White : ActionUi.Ink;
        copy.Resources["ControlHover"] = primaryCopy ? ActionUi.Brush("#2E58BE") : ActionUi.Brush("#EEF1F6");
        copy.Resources["ControlPressed"] = primaryCopy ? ActionUi.Brush("#24489F") : ActionUi.Brush("#E2E7F0");
        status.Text = !targetValid
            ? Session.ViewingOriginal ? language.Get("AiPanelWindow_049") : language.Get("AiPanelWindow_050")
            : !CanApplyTarget()
                ? Session.ViewingOriginal ? language.Get("AiPanelWindow_051") : language.Get("AiPanelWindow_052")
                : Session.ViewingOriginal ? language.Get("AiPanelWindow_053") : language.Get("AiPanelWindow_054");
        RenderCommands();
    }
    internal async Task SubmitAsync(string instruction, string? promptId = null)
    {
        if (closed || Session.Busy || string.IsNullOrWhiteSpace(instruction)) return;
        var savedPrompt = preferences.Settings.Prompts.FirstOrDefault(p => p.Id == promptId);
        var instructionName = savedPrompt == null ? language.Get("AiPanelWindow_055") : UiNames.Prompt(savedPrompt, language);
        var profile = preferences.SelectedProfile;
        if (AiPolicy.ValidateProfile(profile) != null) { hint.Text = language.Get("AiPanelWindow_056"); return; }
        var key = preferences.Secrets.Get(profile) ?? "";
        if (profile.Protocol == AiProtocol.Responses && key.Length == 0) { hint.Text = language.Get("AiPanelWindow_057"); return; }
        AiRequestTicket ticket;
        try { ticket = Session.Begin(instruction); } catch (ArgumentException) { hint.Text = language.Get("AiPanelWindow_058"); return; }
        using var cancellation = new CancellationTokenSource(); request = cancellation; var interaction = interactionRevision;
        runningPromptId = promptId; hint.Text = language.Get("AiPanelWindow_059"); Refresh();
        try
        {
            var result = await client.ExecuteAsync(profile, key, ticket.SourceText, ticket.Instruction, cancellation.Token);
            if (closed || cancellation.IsCancellationRequested) return;
            if (!result.Success) { Session.Cancel(); hint.Text = ErrorMessage(result.Status); return; }
            if (!Session.Complete(ticket, result.Text)) { hint.Text = language.Get("AiPanelWindow_060"); return; }
            instructionNames[Session.Current!.Id] = instructionName;
            foreach (var id in instructionNames.Keys.Where(id => !Session.Versions.Any(v => v.Id == id)).ToArray()) instructionNames.Remove(id);
            hint.Text = "";
            if (promptId != null)
            {
                var settings = preferences.Settings;
                if (!preferences.TrySave(settings with { RecentIds = new[] { promptId }.Concat(settings.RecentIds).Distinct().Take(10).ToArray() }, out _))
                    hint.Text = language.Get("AiPanelWindow_061");
            }
            Refresh();
            if (interaction == interactionRevision && !Session.ViewingOriginal && Session.Draft.Length == 0 && HasForeground) input.Focus();
        }
        catch (OperationCanceledException) { if (!closed && ReferenceEquals(request, cancellation)) hint.Text = language.Get("AiPanelWindow_062"); }
        catch (Exception e) when (e is InvalidOperationException or System.IO.IOException or HttpRequestException or ObjectDisposedException)
        { if (!closed && ReferenceEquals(request, cancellation) && !cancellation.IsCancellationRequested) hint.Text = language.Get("AiPanelWindow_063"); }
        finally { if (ReferenceEquals(request, cancellation)) { request = null; runningPromptId = null; Session.Cancel(); if (!closed) Refresh(); } }
    }
    private string BasisName(long? id) => id == null ? language.Get("Ai_Original") : language.Get("Ai_Version", id);
    private string ErrorMessage(string code) => UiText.Join(language.Run(() => AiErrorMessages.Describe(code)), language.Get("AiPanelWindow_064"));
    private void Stop() { request?.Cancel(); Session.Cancel(); if (!closed) Refresh(); }
    internal void InvalidateTarget() { targetValid = false; Stop(); if (!closed) { hint.Text = language.Get("AiPanelWindow_065"); Refresh(); } }
    internal async Task ApplyAsync()
    {
        if (closed || applying || Session.Busy || !targetValid || apply == null || Session.Current == null || Session.ViewingOriginal
            || !CanApplyTarget()) return;
        applying = true; Refresh();
        try { if (await apply(Session.Current.Text)) Close(); else { targetValid = false; hint.Text = language.Get("AiPanelWindow_066"); } }
        finally { applying = false; Refresh(); }
    }
    private bool CanApplyTarget() => selection != null
        ? SessionPolicy.CanWrite(selection, selection.Target)
        : localCanApply?.Invoke() == true;
    private void Copy()
    { try { Clipboard.SetText(Session.ViewingOriginal ? Session.Original : Session.Current?.Text ?? ""); hint.Text = language.Get("AiPanelWindow_067"); } catch (ExternalException) { hint.Text = language.Get("AiPanelWindow_068"); } }
    private void TogglePin(AiPrompt prompt)
    {
        if (!preferences.TogglePin(prompt.Id, out var error)) hint!.Text = error == null ? "" : UiText.Current(error, language.Culture);
    }
    private void EditPrompt(AiPrompt? prompt)
    {
        preferences.OpenEditor(this, prompt);
    }
    private void DeletePrompt(AiPrompt prompt)
    { if (!preferences.DeletePrompt(prompt.Id, out var error)) hint!.Text = error == null ? "" : UiText.Current(error, language.Culture); }
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
        if (e.Key == Key.Escape && commandRows.IsKeyboardFocusWithin)
        { e.Handled = true; input.Focus(); return; }
        if (e.Key == Key.Escape) { e.Handled = true; if (input.Text.Length > 0) input.Clear(); else Close(); }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; _ = ApplyAsync(); }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && !input.IsKeyboardFocusWithin)
        {
            var selected = Keyboard.FocusedElement is TextBox box && box.SelectionLength > 0 || resultContent.Content is FlowDocumentScrollViewer viewer && !viewer.Selection.IsEmpty;
            if (!selected) { e.Handled = true; Copy(); }
        }
    }
}
