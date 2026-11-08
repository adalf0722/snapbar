using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class ActionSettingsPanel : ContentControl
{
    private readonly UiLanguage? language;
    private string Text(string key, params object?[] values) => language?.Get(key, values) ?? UiText.Get(key, values);
    private ActionSettings settings;
    private readonly Func<ActionSettings, bool> save;
    private readonly Action? editSearch;
    private readonly Action? manageDictionary;
    private bool dictionaryInstalled;
    private Button? dictionaryEdit;
    private TextBlock? dictionaryStatus;
    private bool searchHasDraft;
    private Button? searchEdit;
    private TextBlock? searchDraftStatus;
    private readonly StackPanel groups = new(), preview = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock status = ActionUi.Text("", 11, ActionUi.Muted);
    private DeletedCustomAction? undo;
    private readonly Border deletionNotice = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock deletionText = ActionUi.Text("", 13);
    private readonly Border previewShell = new();
    private readonly Dictionary<ActionPlacement, RadioButton> tabs = new();
    private readonly ScrollViewer listScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private ActionPlacement selectedPlacement = ActionPlacement.Bar;

    public ActionSettingsPanel(ActionSettings initial, Func<ActionSettings, bool> save, Action? editSearch = null,
        Action? manageDictionary = null, bool dictionaryInstalled = false, UiLanguage? language = null)
    {
        this.language = language;
        this.save = save; this.editSearch = editSearch; settings = ActionCatalog.Normalize(initial);
        this.manageDictionary = manageDictionary;
        this.dictionaryInstalled = dictionaryInstalled;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        ActionUi.LoadStyles(this);
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "ActionSettingsPanel");
        var outer = new DockPanel { Margin = new(0) };
        var top = new StackPanel(); var tools = new DockPanel { Margin = new(0, 0, 0, 10) };
        var add = ActionUi.Button(Text("ActionSettingsPanel_000"), () => Edit(null), "plus", true); add.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(add, "AddAction");
        DockPanel.SetDock(add, Dock.Right); tools.Children.Add(add);
        var import = ActionUi.Button(Text("ActionSettingsPanel_001"), Import); import.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(import, "ImportActions");
        DockPanel.SetDock(import, Dock.Right); tools.Children.Add(import);
        var previewBox = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new(0) };
        AutomationProperties.SetAutomationId(previewBox, "ActionSettingsPreview");
        AutomationProperties.SetName(previewBox, Text("ActionSettingsPanel_002"));
        var previewHelp = Text("ActionSettingsPanel_003");
        previewBox.ToolTip = previewHelp; AutomationProperties.SetHelpText(previewBox, previewHelp);
        previewShell.Child = preview; previewShell.HorizontalAlignment = HorizontalAlignment.Center;
        previewShell.CornerRadius = new(11); previewShell.Padding = new(6); previewShell.BorderThickness = new(1);
        previewBox.Content = previewShell;
        var appearanceOptions = new WrapPanel { Margin = new(0, 8, 0, 4) };
        var theme = new ComboBox { ItemsSource = new[] { Text("ActionSettingsPanel_004"), Text("ActionSettingsPanel_005") }, SelectedIndex = settings.Theme == "Dark" ? 0 : 1, Width = 104, Margin = new(0, 0, 16, 0) };
        AutomationProperties.SetName(theme, Text("ActionSettingsPanel_006"));
        theme.SelectionChanged += (_, _) => { if (!UiText.IsRefreshing) Commit(settings with { Theme = theme.SelectedIndex == 0 ? "Dark" : "Light" }); }; appearanceOptions.Children.Add(theme);
        var labels = new CheckBox { Content = Text("ActionSettingsPanel_007"), IsChecked = settings.ShowLabels, VerticalAlignment = VerticalAlignment.Center };
        labels.Click += (_, _) => Commit(settings with { ShowLabels = labels.IsChecked == true }); appearanceOptions.Children.Add(labels);
        var appearance = new Expander { Header = Text("ActionSettingsPanel_008"), IsExpanded = false, Content = appearanceOptions, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(appearance, "ActionSettingsAppearance");
        tools.Children.Add(appearance); top.Children.Add(tools);
        var previewLine = new DockPanel(); var previewLabel = ActionUi.Text(Text("ActionSettingsPanel_009"), 12, ActionUi.Muted);
        previewLabel.Margin = new(0, 0, 12, 0); DockPanel.SetDock(previewLabel, Dock.Left); previewLine.Children.Add(previewLabel); previewLine.Children.Add(previewBox);
        var previewExpander = new Expander { Header = Text("ActionSettingsPanel_010"), IsExpanded = false };
        AutomationProperties.SetAutomationId(previewExpander, "ActionSettingsCompactPreview");
        var previewHost = new ContentControl { Content = previewLine, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var previewDetails = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        previewExpander.Content = previewDetails;
        previewExpander.Visibility = Visibility.Collapsed;
        previewHost.Margin = previewExpander.Margin = new(0, 0, 0, 10);
        top.Children.Add(previewHost);
        top.Children.Add(previewExpander);
        bool? wasCompact = null;
        appearance.Expanded += (_, _) => { if (wasCompact == true) previewExpander.IsExpanded = false; };
        previewExpander.Expanded += (_, _) => { if (wasCompact == true) appearance.IsExpanded = false; };
        SizeChanged += (_, _) =>
        {
            var compact = ActualHeight < 420;
            previewDetails.MaxHeight = Math.Max(60, ActualHeight - 260);
            if (compact != wasCompact)
            {
                previewHost.Content = null; previewDetails.Content = null;
                if (compact) { previewDetails.Content = previewLine; previewExpander.IsExpanded = false; appearance.IsExpanded = false; }
                else previewHost.Content = previewLine;
                previewHost.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                previewExpander.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
                previewLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                wasCompact = compact;
            }
        };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) };
        var placementLabel = ActionUi.Text(Text("ActionSettingsPanel_011"), 12, ActionUi.Muted); placementLabel.Margin = new(0, 0, 12, 0); navigation.Children.Add(placementLabel);
        foreach (var placement in Enum.GetValues<ActionPlacement>())
        {
            var tab = new RadioButton { GroupName = "ActionPlacement", Style = (Style)FindResource("ActionPlacementFilter"),
                IsChecked = placement == selectedPlacement, Margin = new(0, 0, 6, 0) };
            AutomationProperties.SetAutomationId(tab, "ActionPlacementTab_" + placement);
            tab.Checked += (_, _) =>
            {
                if (selectedPlacement == placement) return;
                selectedPlacement = placement; Render(); listScroll.ScrollToTop();
            };
            tabs.Add(placement, tab); navigation.Children.Add(tab);
        }
        top.Children.Add(navigation);
        DockPanel.SetDock(top, Dock.Top); outer.Children.Add(top);
        var footer = new StackPanel();
        var deletionContent = new DockPanel();
        var restore = ActionUi.Button(Text("ActionSettingsPanel_012"), RestoreDeleted);
        restore.Margin = new(12, 0, 0, 0); DockPanel.SetDock(restore, Dock.Right); deletionContent.Children.Add(restore);
        AutomationProperties.SetAutomationId(restore, "RestoreDeletedAction");
        AutomationProperties.SetAutomationId(deletionText, "DeletedActionNotice");
        deletionContent.Children.Add(deletionText);
        deletionNotice.Child = ActionUi.Card(deletionContent, new Thickness(12, 8, 12, 8));
        deletionNotice.Margin = new(0, 10, 0, 0); footer.Children.Add(deletionNotice);
        status.Margin = new(0, 10, 0, 0); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); outer.Children.Add(footer);
        listScroll.Content = groups; outer.Children.Add(listScroll); Content = outer; Render();
    }

    internal void ShowSettings(ActionSettings value) { settings = ActionCatalog.Normalize(value); Render(); }
    internal bool FocusSearchEditor() => searchEdit?.Focus() == true;
    internal void FocusDictionaryEditor()
    {
        var placement = settings.Layout.Single(entry => entry.Id == "lookup").Placement;
        tabs[placement].IsChecked = true;
        dictionaryEdit?.BringIntoView();
        dictionaryEdit?.Focus();
    }
    internal void ShowDictionaryStatus(bool installed)
    {
        dictionaryInstalled = installed;
        if (dictionaryStatus != null) dictionaryStatus.Text = installed ? Text("ActionSettingsPanel_013") : Text("ActionSettingsPanel_014");
    }
    internal void ShowSearchDraft(bool dirty)
    {
        searchHasDraft = dirty;
        if (searchDraftStatus != null) searchDraftStatus.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool Commit(ActionSettings value, string? successMessage = null)
    {
        try { settings = ActionCatalog.Normalize(value); var saved = save(settings); status.Text = saved ? successMessage ?? Text("ActionSettingsPanel_015") : Text("ActionSettingsPanel_016"); Render(); return true; }
        catch (ArgumentException error) { status.Text = ValidationMessages.Describe(error, language); Render(); return false; }
    }

    private void Render()
    {
        searchEdit = null; searchDraftStatus = null;
        dictionaryEdit = null; dictionaryStatus = null;
        deletionNotice.Visibility = undo == null ? Visibility.Collapsed : Visibility.Visible;
        deletionText.Text = undo == null ? "" : Text("ActionSettingsPanel_017",(UiNames.Action(undo.Action, language)));
        if (selectedPlacement == ActionPlacement.Hidden && !settings.Layout.Any(e => e.Placement == ActionPlacement.Hidden))
            selectedPlacement = ActionPlacement.More;
        foreach (var (placement, tab) in tabs)
        {
            var count = settings.Layout.Count(e => e.Placement == placement);
            tab.Content = placement switch
            {
                ActionPlacement.Bar => Text("ActionSettingsPanel_018",(count),(ActionCatalog.BarActionLimit)),
                ActionPlacement.More => Text("ActionSettingsPanel_019",(count)),
                _ => Text("ActionSettingsPanel_020",(count))            };
            tab.Visibility = placement == ActionPlacement.Hidden && count == 0 ? Visibility.Collapsed : Visibility.Visible;
            tab.IsChecked = selectedPlacement == placement;
        }
        preview.Children.Clear(); var dark = settings.Theme == "Dark";
        previewShell.Background = dark ? ActionUi.DarkSurface : Brushes.White;
        previewShell.BorderBrush = dark ? ActionUi.DarkLine : ActionUi.Line;
        var pinned = ActionCatalog.GetPinned(settings);
        foreach (var item in pinned)
        {
            var element = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(10, 0, 10, 0), Height = 36 };
            element.Tag = item.Id; AutomationProperties.SetAutomationId(element, "ActionBarPreview_" + item.Id);
            element.ToolTip = item.BuiltIn switch
            { TextAction.OpenLink => Text("ActionSettingsPanel_021"), TextAction.RevealPath => Text("ActionSettingsPanel_022"),
                TextAction.Cut or TextAction.Paste => Text("ActionSettingsPanel_023"), _ => UiNames.Action(item, language) };
            element.Children.Add(ActionUi.ItemIcon(item, dark ? Brushes.White : ActionUi.Ink, 17));
            if (settings.ShowLabels) { var label = ActionUi.Text(UiNames.Action(item, language), 13, dark ? Brushes.White : ActionUi.Ink); label.Margin = new(7, 0, 0, 0); element.Children.Add(label); } preview.Children.Add(element);
        }
        var dots = ActionUi.Text("⋯", 18, dark ? Brushes.White : ActionUi.Ink); dots.Margin = new(12, 5, 12, 5); preview.Children.Add(dots);
        groups.Children.Clear(); var all = ActionCatalog.GetItems(settings).ToDictionary(a => a.Id);
        foreach (var placement in new[] { selectedPlacement })
        {
            var list = settings.Layout.Where(e => e.Placement == placement).ToArray();
            if (placement == ActionPlacement.Hidden && list.Length == 0) continue;
            AutomationProperties.SetName(listScroll, placement switch { ActionPlacement.Bar => Text("ActionSettingsPanel_024"), ActionPlacement.More => Text("ActionSettingsPanel_025"), _ => Text("ActionSettingsPanel_026") });
            if (list.Length == 0)
            {
                var empty = ActionUi.Text(placement == ActionPlacement.More ? Text("ActionSettingsPanel_027") : Text("ActionSettingsPanel_028"), 13, ActionUi.Muted);
                empty.Margin = new(12, 18, 12, 18); groups.Children.Add(empty);
            }
            foreach (var entry in list)
            {
                var item = all[entry.Id]; var row = new Grid { MinHeight = 48 };
                AutomationProperties.SetAutomationId(row, "ActionRow_" + item.Id);
                row.ColumnDefinitions.Add(new() { Width = placement == ActionPlacement.Bar ? new(24) : new(0) });
                row.ColumnDefinitions.Add(new() { Width = new(36) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(125) });
                var image = ActionUi.ItemIcon(item, item.Custom != null ? ActionUi.Accent : ActionUi.Muted); image.Margin = new(8); Grid.SetColumn(image, 1); row.Children.Add(image);
                var name = new StackPanel { Margin = new(6, 7, 8, 7), VerticalAlignment = VerticalAlignment.Center }; name.Children.Add(ActionUi.Text(UiNames.Action(item, language), 14));
                if (item.Custom != null) name.Children.Add(ActionUi.Text(item.Custom.Kind.ToString(), 12, ActionUi.Muted));
                if (item.BuiltIn == TextAction.Lookup && manageDictionary != null)
                {
                    dictionaryStatus = ActionUi.Text("", 12, ActionUi.Muted);
                    AutomationProperties.SetAutomationId(dictionaryStatus, "DictionaryActionStatus");
                    ShowDictionaryStatus(dictionaryInstalled);
                    name.Children.Add(dictionaryStatus);
                }
                if (item.BuiltIn == TextAction.Search && editSearch != null)
                {
                    searchDraftStatus = ActionUi.Text(Text("ActionSettingsPanel_029"), 12, ActionUi.Muted);
                    AutomationProperties.SetAutomationId(searchDraftStatus, "SearchActionDraftStatus");
                    searchDraftStatus.Visibility = searchHasDraft ? Visibility.Visible : Visibility.Collapsed; name.Children.Add(searchDraftStatus);
                }
                Grid.SetColumn(name, 2); row.Children.Add(name);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (placement == ActionPlacement.Bar)
                {
                    var up = ActionUi.Button("", () => Commit(ActionCatalog.Move(settings, item.Id, -1)), "up"); up.ToolTip = Text("ActionSettingsPanel_030"); up.Padding = new(6); buttons.Children.Add(up);
                    var down = ActionUi.Button("", () => Commit(ActionCatalog.Move(settings, item.Id, 1)), "down"); down.ToolTip = Text("ActionSettingsPanel_031"); down.Padding = new(6); buttons.Children.Add(down);
                    AutomationProperties.SetName(up, UiText.Join(UiNames.Action(item, language), Text("ActionSettingsPanel_032"))); AutomationProperties.SetName(down, UiText.Join(UiNames.Action(item, language), Text("ActionSettingsPanel_033")));
                    up.MinWidth = down.MinWidth = 34;
                    up.IsEnabled = entry != list.First(); down.IsEnabled = entry != list.Last();
                    var grip = new Border { Child = ActionUi.Icon("grip", ActionUi.Muted, 16), Background = Brushes.Transparent,
                        Width = 24, Height = 36, VerticalAlignment = VerticalAlignment.Center };
                    AutomationProperties.SetName(grip, UiText.Join(UiNames.Action(item, language), Text("ActionSettingsPanel_034"))); AutomationProperties.SetAutomationId(grip, "ReorderAction_" + item.Id);
                    EnableReordering(grip, item.Id); EnableReordering(name, item.Id); row.Children.Add(grip);
                    row.AllowDrop = true; row.Drop += (_, e) =>
                    {
                        if (e.Data.GetData(typeof(string)) is not string source) return;
                        var pinned = settings.Layout.Where(a => a.Placement == ActionPlacement.Bar).ToList();
                        var from = pinned.FindIndex(a => a.Id == source); var to = pinned.FindIndex(a => a.Id == item.Id);
                        if (from >= 0 && to >= 0 && from != to) { Commit(ActionCatalog.Move(settings, source, to - from)); e.Effects = DragDropEffects.Move; e.Handled = true; }
                    };
                }
                if (item.BuiltIn == TextAction.Search && editSearch != null)
                {
                    searchEdit = ActionUi.Button(Text("ActionSettingsPanel_035"), editSearch); searchEdit.Padding = new(7); searchEdit.ToolTip = Text("ActionSettingsPanel_036");
                    AutomationProperties.SetName(searchEdit, Text("ActionSettingsPanel_037")); AutomationProperties.SetAutomationId(searchEdit, "EditSearchAction"); buttons.Children.Add(searchEdit);
                }
                if (item.BuiltIn == TextAction.Lookup && manageDictionary != null)
                {
                    dictionaryEdit = ActionUi.Button(Text("ActionSettingsPanel_038"), manageDictionary);
                    AutomationProperties.SetAutomationId(dictionaryEdit, "ManageLookupDictionary");
                    buttons.Children.Add(dictionaryEdit);
                }
                if (item.Custom is { } custom)
                {
                    var edit = ActionUi.Button(Text("ActionSettingsPanel_039"), () => Edit(custom)); edit.Padding = new(7); buttons.Children.Add(edit);
                    var extra = ActionUi.Button("⋯", () => { }); extra.Padding = new(8); extra.ContextMenu = ActionMenu(custom); extra.Click += (_, _) => { extra.ContextMenu.PlacementTarget = extra; extra.ContextMenu.IsOpen = true; }; buttons.Children.Add(extra);
                }
                Grid.SetColumn(buttons, 3); row.Children.Add(buttons);
                var combo = new ComboBox { ItemsSource = new[] { Text("ActionSettingsPanel_040"), Text("ActionSettingsPanel_041"), Text("ActionSettingsPanel_042") }, SelectedIndex = (int)placement, VerticalAlignment = VerticalAlignment.Center, Margin = new(6), Padding = new(5) };
                AutomationProperties.SetName(combo, UiText.Join(UiNames.Action(item, language), Text("ActionSettingsPanel_043")));
                combo.SelectionChanged += (_, _) =>
                {
                    if (UiText.IsRefreshing) return;
                    try { Commit(ActionCatalog.SetPlacement(settings, item.Id, (ActionPlacement)combo.SelectedIndex)); }
                    catch (ArgumentException error) { status.Text = ValidationMessages.Describe(error, language); Render(); }
                };
                Grid.SetColumn(combo, 4); row.Children.Add(combo);
                var rowCard = ActionUi.Card(row, new Thickness(8, 0, 6, 0)); rowCard.CornerRadius = new(8); rowCard.Margin = new(0, 0, 0, 3);
                groups.Children.Add(rowCard);
            }
        }
    }

    private void EnableReordering(FrameworkElement handle, string actionId)
    {
        Point? start = null;
        handle.Cursor = Cursors.Hand; handle.ToolTip = Text("ActionSettingsPanel_044");
        handle.PreviewMouseLeftButtonDown += (_, e) => { start = e.GetPosition(this); handle.CaptureMouse(); };
        handle.PreviewMouseLeftButtonUp += (_, _) => { start = null; if (handle.IsMouseCaptured) handle.ReleaseMouseCapture(); };
        handle.LostMouseCapture += (_, _) => start = null;
        handle.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) { start = null; if (handle.IsMouseCaptured) handle.ReleaseMouseCapture(); return; }
            if (start is not { } origin) return;
            var delta = e.GetPosition(this) - origin;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            if (handle.IsMouseCaptured) handle.ReleaseMouseCapture();
            DragDrop.DoDragDrop(handle, actionId, DragDropEffects.Move);
        };
    }

    private ContextMenu ActionMenu(CustomActionDefinition action)
    {
        var menu = new ContextMenu();
        void Add(string label, Action handler) { var item = new MenuItem { Header = label }; item.Click += (_, _) => handler(); menu.Items.Add(item); }
        Add(Text("ActionSettingsPanel_045"), () =>
        {
            var displayName = UiNames.Action(action);
            Edit(action with { Id = "custom-" + Guid.NewGuid().ToString("N"),
                Name = UiText.Join(displayName.Length > 28 ? displayName[..28] : displayName, UiText.Get("ActionSettingsPanel_047")) });
        });
        Add(Text("ActionSettingsPanel_048"), () => Export(action)); Add(Text("ActionSettingsPanel_049"), () =>
        {
            var updated = ActionCatalog.DeleteCustom(settings, action.Id, out var deleted);
            if (Commit(updated, Text("ActionSettingsPanel_050"))) { undo = deleted; Render(); }
        }); return menu;
    }

    private void RestoreDeleted()
    {
        if (undo is not { } deleted) return;
        try
        {
            var restored = ActionCatalog.RestoreCustom(settings, deleted);
            var placement = restored.Layout.Single(e => e.Id == deleted.Action.Id).Placement;
            var message = placement != deleted.Placement ? Text("ActionSettingsPanel_051") : Text("ActionSettingsPanel_052");
            if (Commit(restored, message))
            {
                undo = null; selectedPlacement = placement; Render();
            }
        }
        catch (ArgumentException error) { status.Text = ValidationMessages.Describe(error, language); }
    }

    private void Edit(CustomActionDefinition? existing)
    {
        var dialog = new ActionEditorWindow(existing) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.SavedAction is { } action)
        {
            var customs = settings.CustomActions.Where(a => a.Id != action.Id).Append(action).ToArray();
            selectedPlacement = settings.Layout.FirstOrDefault(e => e.Id == action.Id)?.Placement ?? ActionPlacement.More;
            Commit(settings with { CustomActions = customs });
        }
    }

    private void Import()
    {
        var picker = new OpenFileDialog { Filter = Text("ActionSettingsPanel_053"), CheckFileExists = true };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            using var stream = new FileStream(picker.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 256 * 1024) { status.Text = Text("ActionSettingsPanel_054"); return; }
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            var action = CustomActionPolicy.Import(reader.ReadToEnd());
            var review = new Window { Owner = Window.GetWindow(this) }; ActionUi.Setup(review, Text("ActionSettingsPanel_055"), 700, 660);
            var body = new DockPanel { Margin = new(24) }; var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            footer.Children.Add(ActionUi.Button(Text("ActionSettingsPanel_056"), review.Close)); footer.Children.Add(ActionUi.Button(Text("ActionSettingsPanel_057"), () => review.DialogResult = true, primary: true)); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
            var text = ActionUi.Input(CustomActionPolicy.Export(action)); text.IsReadOnly = true; text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var note = ActionUi.Text(Text("ActionSettingsPanel_058"), 12, ActionUi.Muted); note.Margin = new(0, 0, 0, 14); DockPanel.SetDock(note, Dock.Top); body.Children.Add(note); body.Children.Add(text); review.Content = body;
            if (review.ShowDialog() == true) Commit(settings with { CustomActions = settings.CustomActions.Append(action).ToArray() });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { status.Text = Text("ActionSettingsPanel_059"); }
    }
    private void Export(CustomActionDefinition action)
    {
        var picker = new SaveFileDialog { Filter = Text("ActionSettingsPanel_060"), DefaultExt = ".snapbaraction", FileName = "action.snapbaraction" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(picker.FileName, CustomActionPolicy.Export(action), new UTF8Encoding(false)); status.Text = Text("ActionSettingsPanel_061"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = Text("ActionSettingsPanel_062"); }
    }
}
