using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapBar.App;

internal sealed class AiIconPickerWindow : Window
{
    internal sealed record IconRow(AiIconInfo[] Icons);
    private sealed class SelectCommand(Action<string> select) : ICommand
    {
        public bool CanExecute(object? parameter) => parameter is string;
        public void Execute(object? parameter) { if (parameter is string id) select(id); }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private readonly TextBox query;
    private readonly ComboBox category;
    private readonly ListBox grid;
    private readonly TextBlock count;
    private AiIconInfo[] filtered = [];
    private bool composing;
    public ICommand ChooseIcon { get; }
    internal string? SelectedId { get; private set; }
    internal ListBox IconGrid => grid;
    internal TextBox SearchInput => query;

    internal AiIconPickerWindow(string current)
    {
        ActionUi.Setup(this, "選擇指令圖示", 710, 600); MinWidth = 580; MinHeight = 440;
        ChooseIcon = new SelectCommand(id => { SelectedId = id; Close(); });
        var root = new DockPanel { Margin = new(20) };
        var header = new StackPanel(); var title = new DockPanel();
        var currentIcon = new AiIconPresenter { IconId = current, Width = 24, Height = 24, Margin = new(10, 0, 0, 0) };
        DockPanel.SetDock(currentIcon, Dock.Right); title.Children.Add(currentIcon); title.Children.Add(ActionUi.Heading("選擇圖示", 22)); header.Children.Add(title);
        var description = ActionUi.Text("搜尋中文、英文名稱，或貼上完整圖示名稱。", 12, ActionUi.Muted); description.Margin = new(0, 6, 0, 12); header.Children.Add(description);
        var search = new DockPanel(); category = new ComboBox { Width = 108, Margin = new(10, 0, 0, 0), ItemsSource = new[] { "常用", "全部圖示", "寫作", "翻譯", "閱讀", "程式", "溝通", "工作", "影像", "其他", "原有圖示" }, SelectedIndex = 0 };
        AutomationProperties.SetAutomationId(category, "AiIconCategory"); DockPanel.SetDock(category, Dock.Right); search.Children.Add(category);
        query = ActionUi.Input("", 128); AutomationProperties.SetAutomationId(query, "AiIconSearch"); AutomationProperties.SetName(query, "搜尋圖示或輸入完整名稱"); search.Children.Add(query); header.Children.Add(search);
        header.Margin = new(0, 0, 0, 12); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new(0, 12, 0, 0) }; var cancel = ActionUi.Button("取消", Close);
        AutomationProperties.SetAutomationId(cancel, "AiIconCancel"); cancel.Margin = new(0); DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel);
        count = ActionUi.Text("", 12, ActionUi.Muted); footer.Children.Add(count); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        grid = new ListBox { Background = Brushes.Transparent, BorderThickness = new(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = (DataTemplate)FindResource("AiIconGridRow"), Focusable = false };
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); itemStyle.Setters.Add(new Setter(FocusableProperty, false));
        grid.ItemContainerStyle = itemStyle;
        VirtualizingPanel.SetIsVirtualizing(grid, true); VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid, true); ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
        AutomationProperties.SetAutomationId(grid, "AiIconGrid"); root.Children.Add(grid); Content = root;
        query.TextChanged += (_, _) => Filter(); category.SelectionChanged += (_, _) => Filter();
        TextCompositionManager.AddPreviewTextInputStartHandler(query, (_, _) => composing = true);
        query.PreviewTextInput += (_, _) => composing = false;
        query.PreviewKeyUp += (_, e) => { if (e.Key is Key.Enter or Key.Escape or Key.ImeProcessed) composing = false; };
        query.PreviewKeyDown += (_, e) => { if (e.Key == Key.Down && !e.IsRepeat && !composing && !IsComposing(e)) { FocusIcon(0); e.Handled = true; } };
        PreviewKeyDown += Navigate;
        Loaded += (_, _) => query.Focus(); Filter();
    }
    private static bool IsComposing(KeyEventArgs e) => e.Key == Key.ImeProcessed || Keyboard.Modifiers != ModifierKeys.None;
    private void Filter()
    {
        filtered = AiIconCatalog.Search(query.Text, (string?)category.SelectedItem ?? "全部圖示").ToArray();
        grid.ItemsSource = filtered.Chunk(6).Select(items => new IconRow(items)).ToArray();
        count.Text = filtered.Length == 0 ? "沒有符合的圖示，請試其他名稱。" : $"{filtered.Length} 個圖示 · 方向鍵選擇 · Enter 套用";
    }
    private void Navigate(object sender, KeyEventArgs e)
    {
        if (composing || e.Key == Key.ImeProcessed) return;
        if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
        if (IsComposing(e)) return;
        if (e.Key == Key.Enter && query.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            if (AiIconCatalog.TryResolve(query.Text, out var exact)) ChooseIcon.Execute(exact.Id); else FocusIcon(0);
            return;
        }
        if (Keyboard.FocusedElement is not Button { Tag: string id } || e.Key is not (Key.Up or Key.Down or Key.Left or Key.Right)) return;
        var index = Array.FindIndex(filtered, i => i.Id == id); if (index < 0) return;
        e.Handled = true;
        if (e.Key == Key.Up && index < 6) { query.Focus(); return; }
        var target = e.Key switch
        {
            Key.Left when index % 6 > 0 => index - 1,
            Key.Right when index % 6 < 5 && index + 1 < filtered.Length => index + 1,
            Key.Up => index - 6,
            Key.Down when index / 6 < (filtered.Length - 1) / 6 => Math.Min(index + 6, filtered.Length - 1),
            _ => index
        };
        FocusIcon(target);
    }
    private void FocusIcon(int index)
    {
        if (index < 0 || index >= filtered.Length) return;
        var row = grid.Items[index / 6]; grid.ScrollIntoView(row); grid.UpdateLayout();
        var container = grid.ItemContainerGenerator.ContainerFromItem(row);
        if (container != null) FindButton(container, filtered[index].Id)?.Focus();
    }
    private static Button? FindButton(DependencyObject node, string id)
    {
        if (node is Button { Tag: string value } button && value == id) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindButton(VisualTreeHelper.GetChild(node, i), id) is { } found) return found;
        return null;
    }
}
