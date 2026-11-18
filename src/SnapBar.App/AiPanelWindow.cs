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
    private readonly TextBox input = null!;
    private readonly FrameworkElement original;
    private readonly TextBlock basis, hint, count, status, version, execution, placeholder, modelLabel;
    private readonly Button direct, stop, restart, resume, previous, next, copy, replace, model, toggleCommands;
    private readonly Grid commandRows;
    private readonly StackPanel commandViews;
    private readonly List<Button> commandViewButtons = [];
    private readonly List<Button> commandButtons = [];
    private readonly Dictionary<long, string> instructionNames = [];
    private readonly ComboBox category;
    private readonly ContentControl resultContent;
    private readonly Border resultCard;
    private readonly Border commandArea, originalCard;
    private readonly Grid comparison;
    private readonly TextBlock resultHeading, emptyResult;
    private readonly DockPanel resultMetadata;
    private readonly ContentControl originalContent;
    private readonly Button difference;
    private readonly TextBlock originalMetadata;
    private readonly GridSplitter readerSplitter;
    private bool? readerSideBySide;
    private bool? readerHasResult;
    private bool showingDifferences;
    private bool commandsCollapsed;
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
        var root = new DockPanel { Margin = new(20, 8, 20, 16) }; var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        model = QuietButton("", ShowSettings); model.HorizontalAlignment = HorizontalAlignment.Right;
        model.MaxWidth = 240; model.Padding = new(8); model.MinHeight = 28;
        modelLabel = ActionUi.Text(language.Get("AiPanelWindow_001"), 13, ActionUi.Muted); modelLabel.TextWrapping = TextWrapping.NoWrap; modelLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        model.Content = modelLabel;
        AutomationProperties.SetAutomationId(model, "AiServiceSettings");
        toggleCommands = QuietButton(language.Get("AiPanel_HideCommands"), ToggleCommands);
        toggleCommands.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(toggleCommands, "AiToggleCommands");
        DockPanel.SetDock(model, Dock.Right); header.Children.Add(model); header.Children.Add(toggleCommands);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        progress = new ProgressBar { Height = 2, IsIndeterminate = true, Visibility = Visibility.Hidden };
        DockPanel.SetDock(progress, Dock.Top); root.Children.Add(progress);
        main = new Grid(); main.ColumnDefinitions.Add(new() { Width = new(264) }); main.ColumnDefinitions.Add(new() { Width = new(18) }); main.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var rail = new DockPanel();
        var railHeader = new StackPanel(); var heading = new DockPanel();
        var add = QuietButton(language.Get("AiPanelWindow_003"), () => EditPrompt(null), "plus"); add.Padding = new(6); add.MinHeight = 28;
        stop = ActionUi.Button(language.Get("AiPanelWindow_004"), Stop); stop.Padding = new(5); stop.MinHeight = 28; stop.Margin = new(0);
        DockPanel.SetDock(add, Dock.Right); heading.Children.Add(add); DockPanel.SetDock(stop, Dock.Right); heading.Children.Add(stop);
        var manageCommands = QuietButton(language.Get("AiPanel_ManageCommands"), () => RequestManagement(SettingsCategory.AiCommands));
        manageCommands.ToolTip = language.Get("AiPanelWindow_005"); AutomationProperties.SetName(manageCommands, language.Get("AiPanelWindow_006"));
        AutomationProperties.SetAutomationId(manageCommands, "ManageAiCommands"); DockPanel.SetDock(manageCommands, Dock.Right); heading.Children.Add(manageCommands);
        heading.Children.Add(ActionUi.Heading(language.Get("AiPanelWindow_007"), 14)); railHeader.Children.Add(heading);
        var basisRow = new DockPanel { MinHeight = 28, Margin = new(0, 8, 0, 6) };
        var basisButtons = new StackPanel { Orientation = Orientation.Horizontal };
        restart = QuietButton(language.Get("AiPanel_UseOriginal"), () => { Session.FromOriginal(); Refresh(); input.Focus(); });
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
        commandViews = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 6, 0, 0) };
        foreach (var view in new[] { "All", "Pinned", "Recent" })
        {
            var viewId = view;
            var viewButton = QuietButton(language.Get("AiPanel_View" + view), () => { commandView = viewId; RenderCommands(); });
            viewButton.Tag = view; AutomationProperties.SetAutomationId(viewButton, "AiCommandsView" + view);
            commandViewButtons.Add(viewButton); commandViews.Children.Add(viewButton);
        }
        railHeader.Children.Add(commandViews);
        category = new ComboBox { MinHeight = 30, Margin = new(0, 6, 0, 0) };
        filter = new Expander { Header = language.Get("AiPanelWindow_017"), Content = category, Margin = new(0, 6, 0, 4), IsExpanded = false };
        filter.Resources["ControlButtonLine"] = Brushes.Transparent;
        railHeader.Children.Add(filter); DockPanel.SetDock(railHeader, Dock.Top); rail.Children.Add(railHeader);
        var commandFooter = new StackPanel();
        var deletionLine = new DockPanel(); var restoreCommand = QuietButton(language.Get("AiPanelWindow_018"), () => { if (!preferences.TryRestoreDeleted(out var error)) hint!.Text = error == null ? "" : UiText.Current(error, language.Culture); });
        AutomationProperties.SetAutomationId(restoreCommand, "AiRestoreDeletedPrompt"); DockPanel.SetDock(restoreCommand, Dock.Right); deletionLine.Children.Add(restoreCommand);
        deletionText = ActionUi.Text("", 12, ActionUi.Muted); AutomationProperties.SetAutomationId(deletionText, "AiDeletedPromptNotice"); deletionLine.Children.Add(deletionText);
        deletionNotice = new Border { Child = deletionLine, Margin = new(0, 8, 0, 0) }; commandFooter.Children.Add(deletionNotice);
        count = ActionUi.Text("", 12, ActionUi.Muted); count.Margin = new(4, 6, 0, 0); commandFooter.Children.Add(count); DockPanel.SetDock(commandFooter, Dock.Bottom); rail.Children.Add(commandFooter);
        commandRows = new Grid { VerticalAlignment = VerticalAlignment.Top };
        commandRows.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var commandScroll = new ScrollViewer { Content = commandRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(commandScroll, "AiCommandList"); rail.Children.Add(commandScroll);
        commandArea = ActionUi.Card(rail, new(14)); AutomationProperties.SetAutomationId(commandArea, "AiCommandArea"); main.Children.Add(commandArea);
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
        hint = ActionUi.Text("", 12, ActionUi.Muted); hint.Margin = new(0, 10, 0, 0); DockPanel.SetDock(hint, Dock.Bottom); body.Children.Add(hint);
        original = AiMarkdownView.Literal(text); AutomationProperties.SetAutomationId(original, "AiOriginalText");
        var originalBody = new DockPanel();
        var originalHeader = new DockPanel { Margin = new(4, 0, 4, 0), Height = 28 };
        var copyOriginal = QuietButton(language.Get("AiPanelWindow_047"), () => Copy(originalText: true));
        AutomationProperties.SetAutomationId(copyOriginal, "AiCopyOriginal"); DockPanel.SetDock(copyOriginal, Dock.Right); originalHeader.Children.Add(copyOriginal);
        originalHeader.Children.Add(ActionUi.Heading(language.Get("AiPanelWindow_023"), 14));
        DockPanel.SetDock(originalHeader, Dock.Top); originalBody.Children.Add(originalHeader);
        originalMetadata = ActionUi.Text(language.Get("AiPanel_OriginalMetadata"), 12, ActionUi.Muted);
        originalMetadata.Height = 28; originalMetadata.Margin = new(4, 0, 4, 0);
        DockPanel.SetDock(originalMetadata, Dock.Top); originalBody.Children.Add(originalMetadata);
        originalContent = new ContentControl { Content = original, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        originalBody.Children.Add(originalContent);
        var resultBody = new DockPanel();
        var resultTitle = new DockPanel { Margin = new(4, 0, 4, 0), Height = 28 };
        difference = QuietButton(language.Get("AiPanel_ShowDifferences"), () => { showingDifferences = !showingDifferences; renderedText = null; Refresh(); });
        AutomationProperties.SetAutomationId(difference, "AiToggleDifferences"); DockPanel.SetDock(difference, Dock.Right); resultTitle.Children.Add(difference);
        resultHeading = ActionUi.Heading(language.Get("AiPanel_CurrentResult"), 14); resultTitle.Children.Add(resultHeading);
        DockPanel.SetDock(resultTitle, Dock.Top); resultBody.Children.Add(resultTitle);
        var resultHeader = resultMetadata = new DockPanel { Margin = new(4, 0, 4, 0), Height = 28 };
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
        emptyResult = ActionUi.Text(language.Get("AiPanel_EmptyResult"), 13, ActionUi.Muted);
        emptyResult.Margin = new(20); emptyResult.VerticalAlignment = VerticalAlignment.Top;
        resultContent.Content = emptyResult;
        resultBody.Children.Add(resultContent); resultCard = new Border { Child = resultBody };
        AutomationProperties.SetAutomationId(resultCard, "AiResultPane");
        originalCard = new Border { Child = originalBody };
        AutomationProperties.SetAutomationId(originalCard, "AiOriginalPane");
        comparison = new Grid(); comparison.Children.Add(originalCard); comparison.Children.Add(resultCard); body.Children.Add(comparison);
        readerSplitter = new GridSplitter { Background = Brushes.Transparent, ShowsPreview = true, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ToolTip = language.Get("AiPanel_ResizeReader"), Focusable = true };
        AutomationProperties.SetAutomationId(readerSplitter, "AiReaderSplitter");
        AutomationProperties.SetName(readerSplitter, language.Get("AiPanel_ResizeReader"));
        readerSplitter.DragCompleted += (_, _) => SaveReaderRatio();
        readerSplitter.PreviewKeyUp += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) SaveReaderRatio(); };
        comparison.Children.Add(readerSplitter);
        comparison.SizeChanged += (_, _) => LayoutComparison();
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

    private void ToggleCommands()
    {
        commandsCollapsed = !commandsCollapsed;
        // Move focus out of the rail before hiding it.
        toggleCommands.Focus();
        commandArea.Visibility = commandsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        main.ColumnDefinitions[0].Width = new(commandsCollapsed ? 0 : 264);
        main.ColumnDefinitions[1].Width = new(commandsCollapsed ? 0 : 18);
        var label = language.Get(commandsCollapsed ? "AiPanel_ShowCommands" : "AiPanel_HideCommands");
        toggleCommands.Content = label; AutomationProperties.SetName(toggleCommands, label);
        if (!commandsCollapsed) input.Focus();
    }

    private void LayoutComparison()
    {
        var sideBySide = comparison.ActualWidth >= 620;
        var hasResult = Session.Current != null;
        if (readerSideBySide == sideBySide && readerHasResult == hasResult) return;
        readerSideBySide = sideBySide; readerHasResult = hasResult;
        comparison.ColumnDefinitions.Clear(); comparison.RowDefinitions.Clear();
        var ratio = sideBySide ? preferences.Settings.ReaderHorizontalRatio : hasResult ? preferences.Settings.ReaderVerticalRatio : 0.68;
        if (sideBySide)
        {
            comparison.ColumnDefinitions.Add(new() { Width = new(ratio, GridUnitType.Star), MinWidth = 180 });
            comparison.ColumnDefinitions.Add(new() { Width = new(12) });
            comparison.ColumnDefinitions.Add(new() { Width = new(1 - ratio, GridUnitType.Star), MinWidth = 180 });
        }
        else
        {
            comparison.RowDefinitions.Add(new() { Height = new(ratio, GridUnitType.Star), MinHeight = 100 });
            comparison.RowDefinitions.Add(new() { Height = new(12) });
            comparison.RowDefinitions.Add(new() { Height = new(1 - ratio, GridUnitType.Star), MinHeight = hasResult ? 150 : 80 });
        }
        Grid.SetRow(resultCard, sideBySide ? 0 : 2); Grid.SetColumn(resultCard, sideBySide ? 2 : 0);
        Grid.SetRow(readerSplitter, sideBySide ? 0 : 1); Grid.SetColumn(readerSplitter, sideBySide ? 1 : 0);
        readerSplitter.ResizeDirection = sideBySide ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        readerSplitter.HorizontalAlignment = sideBySide ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        readerSplitter.VerticalAlignment = sideBySide ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        readerSplitter.Width = sideBySide ? 12 : double.NaN; readerSplitter.Height = sideBySide ? double.NaN : 12;
        readerSplitter.Cursor = sideBySide ? Cursors.SizeWE : Cursors.SizeNS;
        var line = new FrameworkElementFactory(typeof(Border)); line.SetValue(Border.BackgroundProperty, ActionUi.Line);
        line.SetValue(FrameworkElement.WidthProperty, sideBySide ? 1d : double.NaN);
        line.SetValue(FrameworkElement.HeightProperty, sideBySide ? double.NaN : 1d);
        line.SetValue(FrameworkElement.HorizontalAlignmentProperty, sideBySide ? HorizontalAlignment.Center : HorizontalAlignment.Stretch);
        line.SetValue(FrameworkElement.VerticalAlignmentProperty, sideBySide ? VerticalAlignment.Stretch : VerticalAlignment.Center);
        readerSplitter.Template = new ControlTemplate(typeof(GridSplitter)) { VisualTree = line };
    }

    private void SaveReaderRatio()
    {
        var horizontal = readerSideBySide == true;
        var first = horizontal ? comparison.ColumnDefinitions[0].ActualWidth : comparison.RowDefinitions[0].ActualHeight;
        var last = horizontal ? comparison.ColumnDefinitions[2].ActualWidth : comparison.RowDefinitions[2].ActualHeight;
        if (first + last <= 0 || !horizontal && Session.Current == null) return;
        var ratio = Math.Clamp(first / (first + last), 0.2, 0.8);
        var settings = preferences.Settings;
        if (!preferences.TrySave(horizontal ? settings with { ReaderHorizontalRatio = ratio } : settings with { ReaderVerticalRatio = ratio }, out var error))
            hint.Text = error == null ? "" : UiText.Current(error, language.Culture);
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
        basis.Text = UiText.Join(language.Get("AiPanel_RewriteSource"), BasisName(Session.BasisId)); basis.ToolTip = basis.Text;
        restart.Visibility = Session.BasisId == null ? Visibility.Collapsed : Visibility.Visible;
        var profile = preferences.SelectedProfile;
        modelLabel.Text = language.Get("AiPanel_ServiceSettings");
        AutomationProperties.SetName(model, language.Get("AiPanelWindow_039")); model.ToolTip = UiNames.Provider(profile, language) + " · " + profile.Model;
        resume.Visibility = Session.Current != null && Session.BasisId != Session.Current.Id ? Visibility.Visible : Visibility.Collapsed;
        resume.Content = language.Get("AiPanel_UseCurrent");
        restart.IsEnabled = resume.IsEnabled = !Session.Busy;
        stop.Visibility = Session.Busy ? Visibility.Visible : Visibility.Collapsed;
        progress.Visibility = Session.Busy ? Visibility.Visible : Visibility.Hidden;
        resultHeading.Text = language.Get(Session.Busy ? "AiPanelWindow_043" : "AiPanel_CurrentResult");
        emptyResult.Text = language.Get(Session.Busy ? "AiPanel_WaitingResult" : "AiPanel_EmptyResult");
        resultMetadata.Visibility = Session.Current == null ? Visibility.Hidden : Visibility.Visible;
        difference.Visibility = Session.Current == null ? Visibility.Collapsed : Visibility.Visible;
        difference.IsEnabled = Session.Current != null && Session.Original.Length <= 12000 && Session.Current.Text.Length <= 12000;
        if (!difference.IsEnabled) showingDifferences = false;
        difference.Content = language.Get(showingDifferences ? "AiPanel_CleanReading" : "AiPanel_ShowDifferences");
        AutomationProperties.SetName(difference, difference.Content.ToString());
        difference.ToolTip = language.Get(difference.IsEnabled ? "AiPanel_DifferenceHelp" : "AiPanel_DifferenceLimit");
        originalMetadata.Text = language.Get(showingDifferences ? "AiPanel_RemovedLegend" : "AiPanel_OriginalMetadata");
        previous.IsEnabled = !Session.Busy && Session.Index > 0; next.IsEnabled = !Session.Busy && Session.Index + 1 < Session.Versions.Count;
        previous.Visibility = next.Visibility = Session.Versions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (Session.Current is { } current)
        {
            version.Text = language.Get("AiPanelWindow_045",(current.Id)); execution.Text = UiText.Join("· ", instructionNames.GetValueOrDefault(current.Id, language.Get("AiPanelWindow_046")));
            if (renderedText != current.Text)
            {
                renderedText = current.Text;
                if (showingDifferences)
                {
                    var comparisonText = TextComparison.Create(Session.Original, current.Text);
                    originalContent.Content = AiMarkdownView.Comparison(comparisonText.Original, true);
                    resultContent.Content = AiMarkdownView.Comparison(comparisonText.Result, false);
                    execution.Text = language.Get("AiPanel_AddedLegend");
                    execution.ToolTip = language.Get("AiPanel_AddedLegend");
                }
                else { originalContent.Content = original; resultContent.Content = language.Run(() => AiMarkdownView.Create(current.Text)); execution.ToolTip = execution.Text; }
            }
            if (showingDifferences) execution.Text = language.Get("AiPanel_AddedLegend");
        }
        copy.Content = language.Get("AiPanelWindow_048"); copy.IsEnabled = Session.Current != null;
        var writable = targetValid && CanApplyTarget() && apply != null;
        replace.Visibility = Session.Current != null && writable ? Visibility.Visible : Visibility.Collapsed; replace.IsEnabled = !Session.Busy && !applying;
        var primaryCopy = Session.Current != null && !writable;
        copy.Background = copy.BorderBrush = primaryCopy ? ActionUi.Accent : Brushes.White;
        if (!primaryCopy) copy.BorderBrush = ActionUi.Line;
        copy.Foreground = primaryCopy ? Brushes.White : ActionUi.Ink;
        copy.Resources["ControlHover"] = primaryCopy ? ActionUi.Brush("#2E58BE") : ActionUi.Brush("#EEF1F6");
        copy.Resources["ControlPressed"] = primaryCopy ? ActionUi.Brush("#24489F") : ActionUi.Brush("#E2E7F0");
        status.Text = !targetValid
            ? Session.Current == null ? language.Get("AiPanelWindow_049") : language.Get("AiPanelWindow_050")
            : !CanApplyTarget()
                ? Session.Current == null ? language.Get("AiPanelWindow_051") : language.Get("AiPanelWindow_052")
                : Session.Current == null ? language.Get("AiPanelWindow_053") : language.Get("AiPanelWindow_054");
        RenderCommands();
        LayoutComparison();
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
            versionPromptIds[Session.Current.Id] = promptId;
            foreach (var id in versionPromptIds.Keys.Where(id => !Session.Versions.Any(v => v.Id == id)).ToArray()) versionPromptIds.Remove(id);
            showingDifferences = false; renderedText = null;
            foreach (var id in instructionNames.Keys.Where(id => !Session.Versions.Any(v => v.Id == id)).ToArray()) instructionNames.Remove(id);
            hint.Text = "";
            if (promptId != null)
            {
                var settings = preferences.Settings;
                if (!preferences.TrySave(settings with { RecentIds = new[] { promptId }.Concat(settings.RecentIds).Distinct().Take(10).ToArray() }, out _))
                    hint.Text = language.Get("AiPanelWindow_061");
            }
            Refresh();
            if (!commandsCollapsed && interaction == interactionRevision && Session.Draft.Length == 0 && HasForeground) input.Focus();
        }
        catch (OperationCanceledException) { if (!closed && ReferenceEquals(request, cancellation)) hint.Text = language.Get("AiPanelWindow_062"); }
        catch (Exception e) when (e is InvalidOperationException or System.IO.IOException or HttpRequestException or ObjectDisposedException)
        { if (!closed && ReferenceEquals(request, cancellation) && !cancellation.IsCancellationRequested) hint.Text = language.Get("AiPanelWindow_063"); }
        finally { if (ReferenceEquals(request, cancellation)) { request = null; runningPromptId = null; Session.Cancel(); if (!closed) Refresh(); } }
    }
    private string BasisName(long? id) => id == null ? language.Get("Ai_Original") : language.Get("Ai_Version", id);
    private string ErrorMessage(string code) => UiText.Join(language.Run(() => AiErrorMessages.Describe(code)), language.Get("AiPanelWindow_064"));
    private void Stop() { request?.Cancel(); Session.Cancel(); if (!closed) Refresh(); }
    internal void InvalidateTarget() { targetValid = false; Stop(); if (!closed) { hint.Text = ""; Refresh(); } }
    internal async Task ApplyAsync()
    {
        if (closed || applying || Session.Busy || !targetValid || apply == null || Session.Current == null
            || !CanApplyTarget()) return;
        applying = true; Refresh();
        try { if (await apply(Session.Current.Text)) Close(); else { targetValid = false; hint.Text = ""; } }
        finally { applying = false; Refresh(); }
    }
    private bool CanApplyTarget() => selection != null
        ? SessionPolicy.CanWrite(selection, selection.Target)
        : localCanApply?.Invoke() == true;
    private void Copy() => Copy(originalText: false);
    private void Copy(bool originalText)
    {
        var text = originalText ? Session.Original : Session.Current?.Text;
        if (text == null) return;
        try { Clipboard.SetText(text); hint.Text = language.Get("AiPanelWindow_067"); } catch (ExternalException) { hint.Text = language.Get("AiPanelWindow_068"); }
    }
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
            var selected = Keyboard.FocusedElement is TextBox box && box.SelectionLength > 0
                || originalContent.Content is FlowDocumentScrollViewer originalViewer && originalViewer.IsKeyboardFocusWithin && !originalViewer.Selection.IsEmpty
                || resultContent.Content is FlowDocumentScrollViewer viewer && viewer.IsKeyboardFocusWithin && !viewer.Selection.IsEmpty;
            if (!selected) { e.Handled = true; Copy(originalContent.IsKeyboardFocusWithin); }
        }
    }
}
