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

internal sealed class ActionSettingsWindow : Window
{
    private ActionSettings settings;
    private readonly Func<ActionSettings, bool> save;
    private readonly StackPanel groups = new(), preview = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock status = ActionUi.Text("", 11, ActionUi.Muted);
    private string? dragging;
    private ActionSettings? undo;
    private readonly TextBlock previewHeading = ActionUi.Text("", 11, ActionUi.Muted);

    public ActionSettingsWindow(ActionSettings initial, Func<ActionSettings, bool> save)
    {
        this.save = save; settings = ActionCatalog.Normalize(initial);
        ActionUi.Setup(this, "我的動作 · SnapBar", 860, 800); MinWidth = 680;
        var outer = new DockPanel { Margin = new(28) }; var heading = new StackPanel { Margin = new(0, 0, 0, 22) };
        heading.Children.Add(ActionUi.Text("A PLACE FOR EVERY ACTION", 10, ActionUi.Muted)); heading.Children.Add(ActionUi.Text("你的動作列，你來決定。", 27));
        heading.Children.Add(ActionUi.Text("留在手邊、收進更多，或暫時隱藏。", 12, ActionUi.Muted)); DockPanel.SetDock(heading, Dock.Top); outer.Children.Add(heading);
        var top = new StackPanel(); var tools = new DockPanel { Margin = new(0, 0, 0, 18) };
        var add = ActionUi.Button("新增動作", () => Edit(null), "plus", true); DockPanel.SetDock(add, Dock.Right); tools.Children.Add(add);
        var import = ActionUi.Button("匯入…", Import); DockPanel.SetDock(import, Dock.Right); tools.Children.Add(import);
        var undoButton = ActionUi.Button("復原刪除", () => { if (undo != null) { var previous = undo; undo = null; Commit(previous); } }); DockPanel.SetDock(undoButton, Dock.Right); tools.Children.Add(undoButton);
        tools.Children.Add(ActionUi.Text("我的動作", 18)); top.Children.Add(tools);
        var appearance = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 14) };
        var theme = new ComboBox { ItemsSource = new[] { "深色", "淺色" }, SelectedIndex = settings.Theme == "Dark" ? 0 : 1, Padding = new(7), Width = 100, Margin = new(0, 0, 12, 0) };
        theme.SelectionChanged += (_, _) => Commit(settings with { Theme = theme.SelectedIndex == 0 ? "Dark" : "Light" }); appearance.Children.Add(theme);
        var labels = new CheckBox { Content = "圖示與文字", IsChecked = settings.ShowLabels, VerticalAlignment = VerticalAlignment.Center };
        labels.Click += (_, _) => Commit(settings with { ShowLabels = labels.IsChecked == true }); appearance.Children.Add(labels); top.Children.Add(appearance);
        top.Children.Add(previewHeading);
        var previewBox = new ScrollViewer { Content = preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(0, 8, 0, 18) };
        top.Children.Add(previewBox); DockPanel.SetDock(top, Dock.Top); outer.Children.Add(top);
        var previewHelp = ActionUi.Text("完整顯示釘選順序；實際動作列依選取情境顯示，寬度不足時將尾端收進「⋯」。", 11, ActionUi.Muted);
        previewHelp.Margin = new(0, -8, 0, 14); top.Children.Add(previewHelp);
        status.Margin = new(0, 10, 0, 0); DockPanel.SetDock(status, Dock.Bottom); outer.Children.Add(status);
        outer.Children.Add(new ScrollViewer { Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = outer; Render();
    }

    private void Commit(ActionSettings value)
    {
        try { settings = ActionCatalog.Normalize(value); var saved = save(settings); status.Text = saved ? "已保存；新增動作預設放在更多選單。" : "本次已生效，但無法保存；請檢查檔案權限或設定大小（64 KiB 上限）。"; Render(); }
        catch (ArgumentException) { status.Text = "動作列最多六個動作；請先移出一個再加入。"; Render(); }
    }

    private void Render()
    {
        preview.Children.Clear(); var dark = settings.Theme == "Dark"; preview.Background = dark ? ActionUi.Brush("#252B2A") : Brushes.White;
        var pinned = ActionCatalog.GetPinned(settings);
        previewHeading.Text = $"動作列即時預覽 · 已釘選 {pinned.Count}/6";
        foreach (var item in pinned)
        {
            var element = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(12, 10, 3, 10) };
            element.Tag = item.Id; AutomationProperties.SetAutomationId(element, "ActionBarPreview_" + item.Id);
            element.ToolTip = item.BuiltIn switch
            { TextAction.OpenLink => "僅選取完整網址時顯示", TextAction.RevealPath => "僅選取完整路徑時顯示",
                TextAction.Cut or TextAction.Paste => "僅可編輯來源時顯示", _ => item.Name };
            element.Children.Add(ActionUi.Icon(item.Icon, dark ? Brushes.White : ActionUi.Ink, 17));
            if (settings.ShowLabels) { var label = ActionUi.Text(item.Name, 12, dark ? Brushes.White : ActionUi.Ink); label.Margin = new(6, 0, 0, 0); element.Children.Add(label); } preview.Children.Add(element);
        }
        var dots = ActionUi.Text("⋯", 18, dark ? Brushes.White : ActionUi.Ink); dots.Margin = new(12, 5, 12, 5); preview.Children.Add(dots);
        groups.Children.Clear(); var all = ActionCatalog.GetItems(settings).ToDictionary(a => a.Id);
        foreach (var placement in Enum.GetValues<ActionPlacement>())
        {
            var list = settings.Layout.Where(e => e.Placement == placement).ToArray();
            var title = ActionUi.Text((placement switch { ActionPlacement.Bar => "在動作列", ActionPlacement.More => "在更多選單", _ => "已隱藏" }) + " · " + list.Length, 12, ActionUi.Muted);
            title.Margin = new(0, 12, 0, 8); groups.Children.Add(title);
            foreach (var entry in list)
            {
                var item = all[entry.Id]; var row = new Grid { Margin = new(0, 0, 0, 1), Background = Brushes.White, MinHeight = 58 };
                row.ColumnDefinitions.Add(new() { Width = new(36) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(125) });
                var image = ActionUi.Icon(item.Icon, item.Custom != null ? ActionUi.Accent : ActionUi.Muted); image.Margin = new(8); row.Children.Add(image);
                var name = new StackPanel { Margin = new(6, 9, 8, 9) }; name.Children.Add(ActionUi.Text(item.Name, 13)); name.Children.Add(ActionUi.Text(item.Custom?.Kind.ToString() ?? "內建動作", 10, ActionUi.Muted)); Grid.SetColumn(name, 1); row.Children.Add(name);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (placement == ActionPlacement.Bar)
                {
                    var up = ActionUi.Button("", () => Commit(ActionCatalog.Move(settings, item.Id, -1)), "up"); up.ToolTip = "上移"; up.Padding = new(6); buttons.Children.Add(up);
                    var down = ActionUi.Button("", () => Commit(ActionCatalog.Move(settings, item.Id, 1)), "down"); down.ToolTip = "下移"; down.Padding = new(6); buttons.Children.Add(down);
                    name.ToolTip = "拖曳此名稱可排序"; name.Cursor = Cursors.SizeNS;
                    name.PreviewMouseLeftButtonDown += (_, _) => dragging = item.Id;
                    name.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && dragging == item.Id) { DragDrop.DoDragDrop(name, item.Id, DragDropEffects.Move); dragging = null; } };
                    row.AllowDrop = true; row.Drop += (_, e) =>
                    {
                        if (e.Data.GetData(typeof(string)) is not string source) return;
                        var pinned = settings.Layout.Where(a => a.Placement == ActionPlacement.Bar).ToList();
                        var from = pinned.FindIndex(a => a.Id == source); var to = pinned.FindIndex(a => a.Id == item.Id);
                        if (from >= 0 && to >= 0) Commit(ActionCatalog.Move(settings, source, to - from));
                    };
                }
                if (item.Custom is { } custom)
                {
                    var edit = ActionUi.Button("編輯", () => Edit(custom)); edit.Padding = new(7); buttons.Children.Add(edit);
                    var extra = ActionUi.Button("⋯", () => { }); extra.Padding = new(8); extra.ContextMenu = ActionMenu(custom); extra.Click += (_, _) => { extra.ContextMenu.PlacementTarget = extra; extra.ContextMenu.IsOpen = true; }; buttons.Children.Add(extra);
                }
                Grid.SetColumn(buttons, 2); row.Children.Add(buttons);
                var combo = new ComboBox { ItemsSource = new[] { "動作列", "更多選單", "隱藏" }, SelectedIndex = (int)placement, VerticalAlignment = VerticalAlignment.Center, Margin = new(6), Padding = new(5) };
                AutomationProperties.SetName(combo, item.Name + "顯示位置"); combo.SelectionChanged += (_, _) => { try { Commit(ActionCatalog.SetPlacement(settings, item.Id, (ActionPlacement)combo.SelectedIndex)); } catch (ArgumentException) { status.Text = "動作列已滿六個，請先移出一個。"; Render(); } };
                Grid.SetColumn(combo, 3); row.Children.Add(combo); groups.Children.Add(row);
            }
        }
    }

    private ContextMenu ActionMenu(CustomActionDefinition action)
    {
        var menu = new ContextMenu();
        void Add(string label, Action handler) { var item = new MenuItem { Header = label }; item.Click += (_, _) => handler(); menu.Items.Add(item); }
        Add("複製為新動作", () => Edit(action with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = action.Name.Length > 28 ? action.Name[..28] + " 副本" : action.Name + " 副本" }));
        Add("匯出…", () => Export(action)); Add("刪除動作", () =>
        {
            undo = settings;
            Commit(settings with { CustomActions = settings.CustomActions.Where(a => a.Id != action.Id).ToArray(), Layout = settings.Layout.Where(a => a.Id != action.Id).ToArray(), RecentIds = settings.RecentIds.Where(a => a != action.Id).ToArray() });
            status.Text = "動作已刪除；可按「復原刪除」還原。";
        }); return menu;
    }

    private void Edit(CustomActionDefinition? existing)
    {
        var dialog = new ActionEditorWindow(existing) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SavedAction is { } action)
        {
            var customs = settings.CustomActions.Where(a => a.Id != action.Id).Append(action).ToArray();
            if (customs.Length > 32) { status.Text = "最多 32 個自訂動作，請先移除不需要的項目。"; return; }
            Commit(settings with { CustomActions = customs });
        }
    }

    private void Import()
    {
        var picker = new OpenFileDialog { Filter = "SnapBar 動作|*.snapbaraction|JSON|*.json", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            using var stream = new FileStream(picker.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 256 * 1024) { status.Text = "動作檔案超過 256 KiB。"; return; }
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            var action = CustomActionPolicy.Import(reader.ReadToEnd());
            var review = new Window { Owner = this }; ActionUi.Setup(review, "匯入前預覽", 700, 660);
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
        if (picker.ShowDialog(this) != true) return;
        try { File.WriteAllText(picker.FileName, CustomActionPolicy.Export(action), new UTF8Encoding(false)); status.Text = "已匯出動作；檔案包含腳本或網址範本，分享前請確認內容。"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "無法匯出，請檢查位置與檔案權限。"; }
    }
}
