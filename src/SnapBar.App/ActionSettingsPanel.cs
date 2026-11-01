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
    private ActionSettings settings;
    private readonly Func<ActionSettings, bool> save;
    private readonly Action? editSearch;
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

    public ActionSettingsPanel(ActionSettings initial, Func<ActionSettings, bool> save, Action? editSearch = null)
    {
        this.save = save; this.editSearch = editSearch; settings = ActionCatalog.Normalize(initial);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        ActionUi.LoadStyles(this);
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "ActionSettingsPanel");
        var outer = new DockPanel { Margin = new(0) };
        var top = new StackPanel(); var tools = new DockPanel { Margin = new(0, 0, 0, 10) };
        var add = ActionUi.Button("新增動作", () => Edit(null), "plus", true); add.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(add, "AddAction");
        DockPanel.SetDock(add, Dock.Right); tools.Children.Add(add);
        var import = ActionUi.Button("匯入…", Import); import.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(import, "ImportActions");
        DockPanel.SetDock(import, Dock.Right); tools.Children.Add(import);
        var previewBox = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new(0) };
        AutomationProperties.SetAutomationId(previewBox, "ActionSettingsPreview");
        AutomationProperties.SetName(previewBox, "動作列預覽");
        const string previewHelp = "完整顯示釘選順序；實際動作列依選取情境顯示，寬度不足時將尾端收進「⋯」。此預覽不執行動作。";
        previewBox.ToolTip = previewHelp; AutomationProperties.SetHelpText(previewBox, previewHelp);
        previewShell.Child = preview; previewShell.HorizontalAlignment = HorizontalAlignment.Center;
        previewShell.CornerRadius = new(11); previewShell.Padding = new(6); previewShell.BorderThickness = new(1);
        previewBox.Content = previewShell;
        var appearanceOptions = new WrapPanel { Margin = new(0, 8, 0, 4) };
        var theme = new ComboBox { ItemsSource = new[] { "深色", "淺色" }, SelectedIndex = settings.Theme == "Dark" ? 0 : 1, Width = 104, Margin = new(0, 0, 16, 0) };
        AutomationProperties.SetName(theme, "動作列主題");
        theme.SelectionChanged += (_, _) => Commit(settings with { Theme = theme.SelectedIndex == 0 ? "Dark" : "Light" }); appearanceOptions.Children.Add(theme);
        var labels = new CheckBox { Content = "圖示與文字", IsChecked = settings.ShowLabels, VerticalAlignment = VerticalAlignment.Center };
        labels.Click += (_, _) => Commit(settings with { ShowLabels = labels.IsChecked == true }); appearanceOptions.Children.Add(labels);
        var appearance = new Expander { Header = "外觀", IsExpanded = false, Content = appearanceOptions, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(appearance, "ActionSettingsAppearance");
        tools.Children.Add(appearance); top.Children.Add(tools);
        var previewLine = new DockPanel(); var previewLabel = ActionUi.Text("預覽", 12, ActionUi.Muted);
        previewLabel.Margin = new(0, 0, 12, 0); DockPanel.SetDock(previewLabel, Dock.Left); previewLine.Children.Add(previewLabel); previewLine.Children.Add(previewBox);
        var previewExpander = new Expander { Header = "預覽", IsExpanded = false };
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
        var placementLabel = ActionUi.Text("顯示位置", 12, ActionUi.Muted); placementLabel.Margin = new(0, 0, 12, 0); navigation.Children.Add(placementLabel);
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
        var restore = ActionUi.Button("復原", RestoreDeleted);
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
    internal void ShowSearchDraft(bool dirty)
    {
        searchHasDraft = dirty;
        if (searchDraftStatus != null) searchDraftStatus.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool Commit(ActionSettings value, string successMessage = "已保存；新增動作預設放在更多選單。")
    {
        try { settings = ActionCatalog.Normalize(value); var saved = save(settings); status.Text = saved ? successMessage : "本次已生效，但無法保存；請檢查檔案權限或設定大小（64 KiB 上限）。"; Render(); return true; }
        catch (ArgumentException error) { status.Text = error.Message; Render(); return false; }
    }

    private void Render()
    {
        searchEdit = null; searchDraftStatus = null;
        deletionNotice.Visibility = undo == null ? Visibility.Collapsed : Visibility.Visible;
        deletionText.Text = undo == null ? "" : $"已刪除「{undo.Action.Name}」";
        if (selectedPlacement == ActionPlacement.Hidden && !settings.Layout.Any(e => e.Placement == ActionPlacement.Hidden))
            selectedPlacement = ActionPlacement.More;
        foreach (var (placement, tab) in tabs)
        {
            var count = settings.Layout.Count(e => e.Placement == placement);
            tab.Content = (placement switch { ActionPlacement.Bar => "動作列", ActionPlacement.More => "更多", _ => "隱藏" }) + " · " + count;
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
            { TextAction.OpenLink => "僅選取完整網址時顯示", TextAction.RevealPath => "僅選取完整路徑時顯示",
                TextAction.Cut or TextAction.Paste => "僅可編輯來源時顯示", _ => item.Name };
            element.Children.Add(ActionUi.ItemIcon(item, dark ? Brushes.White : ActionUi.Ink, 17));
            if (settings.ShowLabels) { var label = ActionUi.Text(item.Name, 13, dark ? Brushes.White : ActionUi.Ink); label.Margin = new(7, 0, 0, 0); element.Children.Add(label); } preview.Children.Add(element);
        }
        var dots = ActionUi.Text("⋯", 18, dark ? Brushes.White : ActionUi.Ink); dots.Margin = new(12, 5, 12, 5); preview.Children.Add(dots);
        groups.Children.Clear(); var all = ActionCatalog.GetItems(settings).ToDictionary(a => a.Id);
        foreach (var placement in new[] { selectedPlacement })
        {
            var list = settings.Layout.Where(e => e.Placement == placement).ToArray();
            if (placement == ActionPlacement.Hidden && list.Length == 0) continue;
            AutomationProperties.SetName(listScroll, placement switch { ActionPlacement.Bar => "動作列中的動作", ActionPlacement.More => "更多選單中的動作", _ => "隱藏的動作" });
            if (list.Length == 0)
            {
                var empty = ActionUi.Text(placement == ActionPlacement.More ? "目前沒有更多動作。新增動作會出現在這裡。" : "目前沒有固定動作。請從「更多」選擇要放在動作列的項目。", 13, ActionUi.Muted);
                empty.Margin = new(12, 18, 12, 18); groups.Children.Add(empty);
            }
            foreach (var entry in list)
            {
                var item = all[entry.Id]; var row = new Grid { MinHeight = 48 };
                AutomationProperties.SetAutomationId(row, "ActionRow_" + item.Id);
                row.ColumnDefinitions.Add(new() { Width = placement == ActionPlacement.Bar ? new(24) : new(0) });
                row.ColumnDefinitions.Add(new() { Width = new(36) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(125) });
                var image = ActionUi.ItemIcon(item, item.Custom != null ? ActionUi.Accent : ActionUi.Muted); image.Margin = new(8); Grid.SetColumn(image, 1); row.Children.Add(image);
                var name = new StackPanel { Margin = new(6, 7, 8, 7), VerticalAlignment = VerticalAlignment.Center }; name.Children.Add(ActionUi.Text(item.Name, 14));
                if (item.Custom != null) name.Children.Add(ActionUi.Text(item.Custom.Kind.ToString(), 12, ActionUi.Muted));
                if (item.BuiltIn == TextAction.Search && editSearch != null)
                {
                    searchDraftStatus = ActionUi.Text("尚未儲存", 12, ActionUi.Muted);
                    AutomationProperties.SetAutomationId(searchDraftStatus, "SearchActionDraftStatus");
                    searchDraftStatus.Visibility = searchHasDraft ? Visibility.Visible : Visibility.Collapsed; name.Children.Add(searchDraftStatus);
                }
                Grid.SetColumn(name, 2); row.Children.Add(name);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (placement == ActionPlacement.Bar)
                {
                    var up = ActionUi.Button("", () => Commit(ActionCatalog.Move(settings, item.Id, -1)), "up"); up.ToolTip = "上移"; up.Padding = new(6); buttons.Children.Add(up);
                    var down = ActionUi.Button("", () => Commit(ActionCatalog.Move(settings, item.Id, 1)), "down"); down.ToolTip = "下移"; down.Padding = new(6); buttons.Children.Add(down);
                    AutomationProperties.SetName(up, item.Name + "上移"); AutomationProperties.SetName(down, item.Name + "下移");
                    up.MinWidth = down.MinWidth = 34;
                    up.IsEnabled = entry != list.First(); down.IsEnabled = entry != list.Last();
                    var grip = new Border { Child = ActionUi.Icon("grip", ActionUi.Muted, 16), Background = Brushes.Transparent,
                        Width = 24, Height = 36, VerticalAlignment = VerticalAlignment.Center };
                    AutomationProperties.SetName(grip, item.Name + "拖曳排序"); AutomationProperties.SetAutomationId(grip, "ReorderAction_" + item.Id);
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
                    searchEdit = ActionUi.Button("編輯", editSearch); searchEdit.Padding = new(7); searchEdit.ToolTip = "設定搜尋引擎與網址";
                    AutomationProperties.SetName(searchEdit, "編輯搜尋動作"); AutomationProperties.SetAutomationId(searchEdit, "EditSearchAction"); buttons.Children.Add(searchEdit);
                }
                if (item.Custom is { } custom)
                {
                    var edit = ActionUi.Button("編輯", () => Edit(custom)); edit.Padding = new(7); buttons.Children.Add(edit);
                    var extra = ActionUi.Button("⋯", () => { }); extra.Padding = new(8); extra.ContextMenu = ActionMenu(custom); extra.Click += (_, _) => { extra.ContextMenu.PlacementTarget = extra; extra.ContextMenu.IsOpen = true; }; buttons.Children.Add(extra);
                }
                Grid.SetColumn(buttons, 3); row.Children.Add(buttons);
                var combo = new ComboBox { ItemsSource = new[] { "動作列", "更多選單", "隱藏" }, SelectedIndex = (int)placement, VerticalAlignment = VerticalAlignment.Center, Margin = new(6), Padding = new(5) };
                AutomationProperties.SetName(combo, item.Name + "顯示位置"); combo.SelectionChanged += (_, _) => { try { Commit(ActionCatalog.SetPlacement(settings, item.Id, (ActionPlacement)combo.SelectedIndex)); } catch (ArgumentException) { status.Text = "動作列已滿六個，請先移出一個。"; Render(); } };
                Grid.SetColumn(combo, 4); row.Children.Add(combo);
                var rowCard = ActionUi.Card(row, new Thickness(8, 0, 6, 0)); rowCard.CornerRadius = new(8); rowCard.Margin = new(0, 0, 0, 3);
                groups.Children.Add(rowCard);
            }
        }
    }

    private void EnableReordering(FrameworkElement handle, string actionId)
    {
        Point? start = null;
        handle.Cursor = Cursors.Hand; handle.ToolTip = "拖曳排序；也可使用右側上下移按鈕。";
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
        Add("複製為新動作", () => Edit(action with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = action.Name.Length > 28 ? action.Name[..28] + " 副本" : action.Name + " 副本" }));
        Add("匯出…", () => Export(action)); Add("刪除動作", () =>
        {
            var updated = ActionCatalog.DeleteCustom(settings, action.Id, out var deleted);
            if (Commit(updated, "已保存。")) { undo = deleted; Render(); }
        }); return menu;
    }

    private void RestoreDeleted()
    {
        if (undo is not { } deleted) return;
        try
        {
            var restored = ActionCatalog.RestoreCustom(settings, deleted);
            var placement = restored.Layout.Single(e => e.Id == deleted.Action.Id).Placement;
            var message = placement != deleted.Placement ? "動作已復原；動作列已滿，已放入更多選單。" : "動作已復原。";
            if (Commit(restored, message))
            {
                undo = null; selectedPlacement = placement; Render();
            }
        }
        catch (ArgumentException error) { status.Text = error.Message; }
    }

    private void Edit(CustomActionDefinition? existing)
    {
        var dialog = new ActionEditorWindow(existing) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.SavedAction is { } action)
        {
            var customs = settings.CustomActions.Where(a => a.Id != action.Id).Append(action).ToArray();
            if (customs.Length > 32) { status.Text = "最多 32 個自訂動作，請先移除不需要的項目。"; return; }
            selectedPlacement = settings.Layout.FirstOrDefault(e => e.Id == action.Id)?.Placement ?? ActionPlacement.More;
            Commit(settings with { CustomActions = customs });
        }
    }

    private void Import()
    {
        var picker = new OpenFileDialog { Filter = "SnapBar 動作|*.snapbaraction|JSON|*.json", CheckFileExists = true };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            using var stream = new FileStream(picker.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 256 * 1024) { status.Text = "動作檔案超過 256 KiB。"; return; }
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            var action = CustomActionPolicy.Import(reader.ReadToEnd());
            var review = new Window { Owner = Window.GetWindow(this) }; ActionUi.Setup(review, "匯入前預覽", 700, 660);
            var body = new DockPanel { Margin = new(24) }; var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            footer.Children.Add(ActionUi.Button("取消", review.Close)); footer.Children.Add(ActionUi.Button("加入更多選單", () => review.DialogResult = true, primary: true)); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
            var text = ActionUi.Input(CustomActionPolicy.Export(action)); text.IsReadOnly = true; text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var note = ActionUi.Text("請確認名稱、類型、完整內容與結果處理方式。匯入不會執行；腳本之後會以你的使用者權限執行。", 12, ActionUi.Muted); note.Margin = new(0, 0, 0, 14); DockPanel.SetDock(note, Dock.Top); body.Children.Add(note); body.Children.Add(text); review.Content = body;
            if (review.ShowDialog() == true) Commit(settings with { CustomActions = settings.CustomActions.Append(action).ToArray() });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { status.Text = "無法匯入：檔案格式、動作數量、大小或權限不符合要求。"; }
    }
    private void Export(CustomActionDefinition action)
    {
        var picker = new SaveFileDialog { Filter = "SnapBar 動作|*.snapbaraction", DefaultExt = ".snapbaraction", FileName = "action.snapbaraction" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(picker.FileName, CustomActionPolicy.Export(action), new UTF8Encoding(false)); status.Text = "已匯出動作；檔案包含腳本或網址範本，分享前請確認內容。"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "無法匯出，請檢查位置與檔案權限。"; }
    }
}
