using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

// Presentation only: execution and ticket validation remain owned by the result card.
internal sealed class OcrActionMenu : ContextMenu
{
    private readonly UiLanguage language;
    private readonly OcrActionDependencies dependencies;
    private readonly Func<string> selectedText;
    private readonly Func<bool> hasSelection;
    private readonly Action<ActionItem> invoke;
    private readonly TextBlock source;
    private readonly StackPanel list = new();
    private readonly TextBlock placeholder;
    private readonly ScrollViewer scroll;
    internal TextBox Search { get; }
    internal IEnumerable<MenuItem> Rows => list.Children.OfType<MenuItem>();
    internal MenuItem Manage { get; }
    internal ScrollViewer ActionList => scroll;

    internal OcrActionMenu(Button target, UiLanguage language, OcrActionDependencies dependencies,
        Func<string> selectedText, Func<bool> hasSelection, Action<ActionItem> invoke, Action returnFocus)
    {
        this.language = language; this.dependencies = dependencies; this.selectedText = selectedText;
        this.hasSelection = hasSelection; this.invoke = invoke;
        PlacementTarget = Window.GetWindow(target); Placement = PlacementMode.Right;
        var handle = new WindowInteropHelper(Window.GetWindow(target)).Handle;
        var point = target.PointToScreen(new Point(0, 0));
        var work = Native.WorkArea((int)point.X, (int)point.Y);
        var scale = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
        var bounds = ClampSize(work.Width / scale, work.Height / scale);
        Width = bounds.Width; MaxHeight = bounds.Height;
        Background = Brushes.White; BorderBrush = ActionUi.Line; BorderThickness = new(1);
        FontFamily = ActionUi.Font; FontSize = 13;

        // ContextMenu's default item containers are selectable; use a content template instead.
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.White); border.SetValue(Border.BorderBrushProperty, ActionUi.Line);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.PaddingProperty, new Thickness(8));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Tag)) { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(presenter); Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
        var root = new DockPanel(); Tag = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var searchBorder = new Border { BorderBrush = ActionUi.Line, BorderThickness = new(1), CornerRadius = new(4), Padding = new(8, 3, 8, 3) };
        var searchGrid = new Grid(); searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); searchGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = ActionUi.Icon("search", ActionUi.Muted, 16); icon.Margin = new(0, 0, 8, 0); searchGrid.Children.Add(icon);
        var inputGrid = new Grid(); Grid.SetColumn(inputGrid, 1); searchGrid.Children.Add(inputGrid);
        Search = new TextBox { BorderThickness = new(0), Background = Brushes.Transparent, Padding = new(0, 4, 0, 4), FontSize = 13 };
        AutomationProperties.SetAutomationId(Search, "OcrActionSearch"); AutomationProperties.SetName(Search, language.Get("Ocr_ActionSearch"));
        placeholder = ActionUi.Text(language.Get("Ocr_ActionSearchPlaceholder"), 13, ActionUi.Muted); placeholder.IsHitTestVisible = false;
        AutomationProperties.SetAutomationId(placeholder, "OcrActionSearchPlaceholder");
        inputGrid.Children.Add(Search); inputGrid.Children.Add(placeholder); searchBorder.Child = searchGrid; header.Children.Add(searchBorder);
        Search.GotKeyboardFocus += (_, _) => searchBorder.BorderBrush = ActionUi.Accent;
        Search.LostKeyboardFocus += (_, _) => searchBorder.BorderBrush = ActionUi.Line;
        source = ActionUi.Text("", 12, ActionUi.Muted); source.Margin = new(4, 8, 4, 5);
        AutomationProperties.SetAutomationId(source, "OcrActionSource"); header.Children.Add(source);
        var footer = new Border { BorderBrush = ActionUi.Line, BorderThickness = new(0, 1, 0, 0), Padding = new(0, 4, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        Manage = new MenuItem { Header = language.Get("Ocr_ManageActions"), MinHeight = 36, Padding = new(8, 6, 8, 6) };
        AutomationProperties.SetAutomationId(Manage, "OcrManageActions"); Manage.Click += (_, _) => { IsOpen = false; dependencies.Manage(); };
        footer.Child = Manage;
        scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        root.Children.Add(scroll);
        Search.TextChanged += (_, _) => Render();
        PreviewKeyDown += OnKey;
        Closed += (_, _) => returnFocus();
        Render();
    }

    internal static Size ClampSize(double width, double height) => new(Math.Max(1, Math.Min(320, width)), Math.Max(1, Math.Min(420, height)));

    internal void RefreshSelection()
    {
        if (IsOpen) Render();
    }

    private void Render()
    {
        placeholder.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        source.Text = language.Get(hasSelection() ? "Ocr_ActionSourceSelection" : "Ocr_ActionSourceAll");
        list.Children.Clear();
        var settings = ActionCatalog.Normalize(dependencies.Settings());
        var query = Search.Text.Trim();
        var all = ActionCatalog.GetLocalActions(settings);
        var matches = ActionCatalog.GetLocalActions(settings, query).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var filtered = all.Where(item => matches.Contains(item.Id) || UiNames.Action(item, language).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (query.Length > 0)
        {
            if (filtered.Length == 0) AddLabel("Ocr_ActionNoResults", "OcrActionEmpty");
            else { AddLabel("Ocr_ActionResults", "OcrActionResults"); foreach (var item in filtered) AddRow(item); }
        }
        else
        {
            var recent = settings.RecentIds.ToHashSet(StringComparer.Ordinal);
            var first = filtered.Where(item => recent.Contains(item.Id)).ToArray();
            var other = filtered.Where(item => !recent.Contains(item.Id)).ToArray();
            if (first.Length > 0) { AddLabel("Ocr_ActionRecent", "OcrActionRecent"); foreach (var item in first) AddRow(item); }
            if (other.Length > 0) { AddLabel("Ocr_ActionOther", "OcrActionOther"); foreach (var item in other) AddRow(item); }
        }
    }

    private void AddLabel(string key, string id)
    {
        var label = ActionUi.Text(language.Get(key), 12, ActionUi.Muted); label.Margin = new(8, 7, 8, 4);
        AutomationProperties.SetAutomationId(label, id); list.Children.Add(label);
    }

    private void AddRow(ActionItem item)
    {
        var enabled = ActionCatalog.IsApplicableLocal(item, selectedText()) && (item.BuiltIn != TextAction.Ai || dependencies.Ai != null);
        var reason = language.Get(item.BuiltIn switch
        { TextAction.OpenLink => "Ocr_InvalidLink", TextAction.RevealPath => "Ocr_InvalidPath", TextAction.Lookup => "Ocr_InvalidLookup", _ => "Ocr_ActionUnavailable" });
        var shortReason = language.Get(item.BuiltIn switch
        { TextAction.OpenLink => "Ocr_ReasonLink", TextAction.RevealPath => "Ocr_ReasonPath", TextAction.Lookup => "Ocr_ReasonLookup", _ => "Ocr_ReasonUnavailable" });
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = enabled ? ActionUi.Ink : ActionUi.Muted, VerticalAlignment = VerticalAlignment.Center };
        label.Inlines.Add(new Run(UiNames.Action(item, language)));
        if (!enabled) label.Inlines.Add(new Run("  " + shortReason) { Foreground = ActionUi.Muted, FontSize = 12 });
        var row = new MenuItem { Header = label, IsEnabled = enabled, MinHeight = 36, Padding = new(8, 6, 8, 6),
            Icon = ActionUi.ItemIcon(item, enabled ? ActionUi.Ink : ActionUi.Muted, 16), ToolTip = enabled ? item.Custom?.Output == ActionOutput.ReplaceSelection ? language.Get("Ocr_Apply") : null : reason };
        if (!enabled) { ToolTipService.SetShowOnDisabled(row, true); AutomationProperties.SetHelpText(row, reason); }
        AutomationProperties.SetName(row, UiNames.Action(item, language)); AutomationProperties.SetAutomationId(row, "OcrAction_" + item.Id);
        row.Click += (_, _) => { IsOpen = false; invoke(item); }; row.GotKeyboardFocus += (_, _) => row.BringIntoView();
        list.Children.Add(row);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; IsOpen = false; return; }
        var enabled = Rows.Where(row => row.IsEnabled).ToArray();
        if (e.Key == Key.Enter)
        {
            var focused = Search.IsKeyboardFocusWithin ? enabled.FirstOrDefault() : enabled.FirstOrDefault(row => row.IsKeyboardFocusWithin);
            if (Search.IsKeyboardFocusWithin || focused != null) { e.Handled = true; focused?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
            return;
        }
        if (e.Key == Key.Tab)
        {
            var reverse = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (Search.IsKeyboardFocusWithin) { if (reverse) Manage.Focus(); else (enabled.FirstOrDefault() ?? Manage).Focus(); }
            else if (Manage.IsKeyboardFocusWithin) { if (reverse) (enabled.LastOrDefault() ?? (IInputElement)Search).Focus(); else Search.Focus(); }
            else if (reverse) Search.Focus(); else Manage.Focus();
            e.Handled = true; return;
        }
        if (e.Key is not (Key.Down or Key.Up)) return;
        var current = Array.FindIndex(enabled, row => row.IsKeyboardFocusWithin);
        var next = e.Key == Key.Down ? current + 1 : current < 0 ? enabled.Length - 1 : current - 1;
        if (next >= 0 && next < enabled.Length) { enabled[next].Focus(); enabled[next].BringIntoView(); }
        e.Handled = true;
    }
}
