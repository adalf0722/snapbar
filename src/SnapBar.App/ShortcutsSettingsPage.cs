using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class ShortcutsSettingsPage : ContentControl
{
    internal sealed record ShortcutChoice(string Id, string Name);
    internal static readonly TextAction?[] Actions = [null, TextAction.Search, TextAction.Lookup, TextAction.Ocr, TextAction.Ai, TextAction.Custom];
    private readonly Func<TextAction?, HotkeySettings?> read;
    private readonly Func<TextAction?, HotkeySettings?, string?, ShortcutSaveError?> save;
    private readonly Func<TextAction, bool> registered;
    private readonly Func<TextAction, IReadOnlyList<ShortcutChoice>> choices;
    private readonly Func<TextAction, string?> readTarget;
    private readonly Action<TextAction> manage;
    private readonly List<(TextAction? Action, TextBlock Name, TextBlock Detail, StackPanel Keys, Button Edit)> rows = [];
    internal ShortcutEditorWindow? Editor { get; private set; }
    internal Action<bool>? EditingChanged { get; set; }

    internal ShortcutsSettingsPage(Func<TextAction?, HotkeySettings?> read,
        Func<TextAction?, HotkeySettings?, string?, ShortcutSaveError?> save, Func<TextAction, bool> registered,
        Func<TextAction, IReadOnlyList<ShortcutChoice>> choices, Func<TextAction, string?> readTarget, Action<TextAction> manage)
    {
        this.read = read; this.save = save; this.registered = registered;
        this.choices = choices; this.readTarget = readTarget; this.manage = manage;
        ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var panel = new StackPanel();
        var intro = ActionUi.Text(UiText.Get("Shortcuts_Intro"), 14, ActionUi.Muted);
        intro.Margin = new(0, 0, 0, 16); panel.Children.Add(intro);
        var list = new StackPanel();
        foreach (var action in Actions)
        {
            if (rows.Count > 0) list.Children.Add(new Border { Height = 1, Background = ActionUi.Line });
            var row = new Grid { MinHeight = 66, Margin = new(0, 0, 0, 0) };
            AutomationProperties.SetAutomationId(row, "ShortcutRow_" + Id(action));
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new(72) });
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 8, 16, 8) };
            var name = ActionUi.Text(ActionName(action), 15);
            var detail = ActionUi.Text("", 12, ActionUi.Muted);
            detail.Margin = new(0, 3, 0, 0); detail.MaxHeight = 36;
            title.Children.Add(name); title.Children.Add(detail); row.Children.Add(title);
            var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new(0, 0, 16, 0) };
            AutomationProperties.SetAutomationId(keys, "ShortcutKeys_" + Id(action));
            Grid.SetColumn(keys, 1); row.Children.Add(keys);
            var edit = ActionUi.Button("", () => Edit(action));
            edit.Style = (Style)FindResource("AiRowTool"); edit.Tag = "Pinned";
            edit.Foreground = ActionUi.Accent; edit.BorderBrush = Brushes.Transparent; edit.Background = Brushes.Transparent;
            edit.HorizontalAlignment = HorizontalAlignment.Right;
            AutomationProperties.SetAutomationId(edit, "EditShortcut_" + Id(action));
            Grid.SetColumn(edit, 2); row.Children.Add(edit);
            list.Children.Add(row); rows.Add((action, name, detail, keys, edit));
        }
        panel.Children.Add(ActionUi.Card(list, new Thickness(20, 4, 20, 4)));
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseEditor(); };
        Refresh();
    }

    internal static string Id(TextAction? action) => action?.ToString() ?? "Toolbar";
    internal static string ActionName(TextAction? action) => UiText.Get(action switch {
        TextAction.Search => "Shortcuts_Search", TextAction.Lookup => "GeneralSettings_ShortcutLookup",
        TextAction.Ocr => "GeneralSettingsPage_015", TextAction.Ai => "GeneralSettings_ShortcutPrompt",
        TextAction.Custom => "GeneralSettings_ShortcutCustomItem", _ => "GeneralSettingsPage_012"
    });

    internal void Refresh()
    {
        foreach (var row in rows)
        {
            var key = read(row.Action);
            row.Name.Text = ActionName(row.Action);
            var targetId = row.Action is TextAction.Ai or TextAction.Custom ? readTarget(row.Action.Value) : null;
            var target = targetId == null ? null : choices(row.Action!.Value).FirstOrDefault(choice => choice.Id == targetId);
            row.Detail.Text = target?.Name ?? (targetId != null ? UiText.Get("Shortcuts_TargetUnavailable") : "");
            row.Detail.Foreground = targetId != null && target == null ? Brushes.Firebrick : ActionUi.Muted;
            row.Detail.Visibility = row.Detail.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            row.Detail.ToolTip = row.Detail.Text;
            ShortcutEditorWindow.RenderKeys(row.Keys, key);
            row.Edit.Content = UiText.Get(key == null ? "Shortcuts_Set" : "Shortcuts_Change");
            AutomationProperties.SetName(row.Edit, UiText.Join(row.Edit.Content, " ", ActionName(row.Action)));
        }
    }

    internal void Edit(TextAction? action)
    {
        if (Editor is { } active)
        {
            if (active.Action == action) { active.Activate(); return; }
            active.Close();
        }
        var owner = Window.GetWindow(this);
        var editor = new ShortcutEditorWindow(action, read(action), readTarget, choices, save, manage,
            action is { } kind && read(action) != null && !registered(kind));
        editor.Owner = owner; Editor = editor;
        editor.EditingChanged = active => EditingChanged?.Invoke(active);
        editor.Closed += (_, _) =>
        {
            EditingChanged?.Invoke(false); Editor = null; Refresh();
            if (IsVisible) rows.First(row => row.Action == action).Edit.Focus();
        };
        editor.Show(); editor.Activate();
    }

    internal void Record(HotkeySettings key) => Editor?.Record(key);
    internal void CloseEditor() => Editor?.Close();
    internal void RefreshLanguage()
    {
        UiText.Refresh(this); Refresh(); Editor?.RefreshLanguage();
    }
}
