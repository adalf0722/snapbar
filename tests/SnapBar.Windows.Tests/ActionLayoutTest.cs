using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;

internal static class ActionLayoutTest
{
    private static IEnumerable<T> Children<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }

    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var passed = 0; var result = 1;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS " + name); }
        app.Startup += (_, _) =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Layout.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "settings.json");
            ActionSettingsWindow? window = null; ActionSettingsWindow? reloadedWindow = null; PopupWindow? popup = null;
            try
            {
                var store = new SettingsStore(path); var loaded = store.Load().Settings;
                var fifth = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "第五個", CustomActionKind.Url, "globe", "https://example.com/?q=***");
                var tenth = fifth with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "第十個" };
                var current = ActionCatalog.Normalize(new() { CustomActions = [fifth, tenth] });
                window = new ActionSettingsWindow(current, value => { current = value; store.Save(loaded with { Actions = value }); return true; });
                window.Show(); window.UpdateLayout();
                string[] Preview(Window view) => Children<StackPanel>(view).Select(AutomationProperties.GetAutomationId)
                    .Where(id => id.StartsWith("ActionBarPreview_", StringComparison.Ordinal)).Select(id => id[17..]).ToArray();
                string? BarTab() => Children<RadioButton>(window).Single(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_Bar").Content as string;
                void Place(string name, ActionPlacement placement)
                {
                    var id = ActionCatalog.GetItems(current).Single(a => a.Name == name).Id;
                    var currentPlacement = current.Layout.Single(e => e.Id == id).Placement;
                    Children<RadioButton>(window).Single(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_" + currentPlacement).IsChecked = true;
                    Children<ComboBox>(window).Single(c => AutomationProperties.GetName(c) == name + "顯示位置").SelectedIndex = (int)placement;
                    Children<RadioButton>(window).Single(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_" + placement).IsChecked = true;
                    window.UpdateLayout();
                }
                Check(Preview(window).SequenceEqual(new[] { "copy", "cut", "paste", "search" }) && BarTab() == "動作列 · 4／9",
                    "settings preview defaults to four pinned actions and shows remaining capacity");
                Place(fifth.Name, ActionPlacement.Bar);
                Check(Preview(window).SequenceEqual(new[] { "copy", "cut", "paste", "search", fifth.Id }), "fifth action appends and preview matches visible configured order");
                Place("開啟連結", ActionPlacement.Bar);
                Check(Preview(window).SequenceEqual(new[] { "copy", "cut", "paste", "search", fifth.Id, "link" }), "sixth conditional action remains visible at end of configuration preview");
                var linkRow = Children<Grid>(window).Single(g => Children<ComboBox>(g).Count(c => AutomationProperties.GetName(c) == "開啟連結顯示位置") == 1
                    && Children<Button>(g).Count() == 2);
                Children<Button>(linkRow).Single(b => (string?)b.ToolTip == "上移").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                Check(Preview(window).SequenceEqual(new[] { "copy", "cut", "paste", "search", "link", fifth.Id }), "sixth action arrow reorder updates complete preview and saved order together");
                Place("開啟連結", ActionPlacement.More); Place("檔案位置", ActionPlacement.Bar);
                var expected = new[] { "copy", "cut", "paste", "search", fifth.Id, "path" };
                Check(Preview(window).SequenceEqual(expected), "conditional path occupies sixth preview slot without filtering");
                var additional = new[] { "AI 改寫", "搜尋 Gmail", "取得公網 IP" };
                foreach (var name in additional) Place(name, ActionPlacement.Bar);
                expected = [.. expected, .. additional.Select(name => ActionCatalog.GetItems(current).Single(a => a.Name == name).Id)];
                Check(Preview(window).SequenceEqual(expected) && BarTab() == "動作列 · 9／9", "settings preview and tab show all nine pins");
                Place(tenth.Name, ActionPlacement.Bar);
                Check(Preview(window).SequenceEqual(expected) && current.Layout.Single(e => e.Id == tenth.Id).Placement == ActionPlacement.More
                    && Children<TextBlock>(window).Any(t => t.Text.Contains("最多可釘選 9 個動作")),
                    "tenth pin is refused with a message and preserves nine-item preview");
                var restarted = new SettingsStore(path).Load();
                Check(restarted.Warning == null && ActionCatalog.GetPinned(restarted.Settings.Actions).Select(a => a.Id).SequenceEqual(expected), "all nine positions survive disk roundtrip");
                reloadedWindow = new ActionSettingsWindow(restarted.Settings.Actions, _ => true); reloadedWindow.Show(); reloadedWindow.UpdateLayout();
                Check(Preview(reloadedWindow).SequenceEqual(expected), "reopened native settings preview preserves nine-item order");
                var snapshot = new SelectionSnapshot("layout", new(0, 0, 0), "normal text", new(100, 100, 20, 20), true, 1, 11);
                popup = new PopupWindow();
                void OpenMore() => Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string[] VisiblePins() => Children<Button>(popup).Where(b => b.Visibility == Visibility.Visible && b.Tag is ActionItem)
                    .Select(b => ((ActionItem)b.Tag).Id).ToArray();
                Button PinButton(string name) => Children<Button>(popup).Single(b => AutomationProperties.GetName(b) == "釘選" + name);
                var iconsOnly = current with { ShowLabels = false };
                popup.Present(snapshot, 100, 100, actionSettings: iconsOnly); popup.UpdateLayout();
                Check(VisiblePins().SequenceEqual(expected.Where(id => id != "path")), "actual toolbar filters path for plain text while retaining configured relative order");
                var eight = ActionCatalog.SetPlacement(iconsOnly, expected[^1], ActionPlacement.More);
                popup.Present(snapshot with { Text = @"D:\Documents\example.txt" }, 100, 100, actionSettings: eight); OpenMore(); popup.UpdateLayout();
                var ninthPin = PinButton(additional[^1]);
                Check(ninthPin.IsEnabled, "More menu permits adding the ninth pin");
                ninthPin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); popup.UpdateLayout();
                Check(VisiblePins().SequenceEqual(expected) && !PinButton(tenth.Name).IsEnabled,
                    "icon-only toolbar shows nine applicable actions and disables a tenth pin");
                var menuSettings = ActionCatalog.SetPlacement(new(), "link", ActionPlacement.Hidden);
                Button MenuPath() => Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreAction_path");
                popup.Present(snapshot, 100, 100, actionSettings: menuSettings); OpenMore(); popup.UpdateLayout();
                Check(MenuPath().IsVisible && !MenuPath().IsEnabled && Children<TextBlock>(popup).Any(t => AutomationProperties.GetAutomationId(t) == "MoreActionReason_path"),
                    "nonpath selection shows configured path menu item disabled with visible reason");
                var dispatched = 0; popup.ActionRequested += action => { if (action == TextAction.RevealPath) dispatched++; };
                popup.HandleMenuKey(0x28); popup.HandleMenuKey(0x0D);
                Check(dispatched == 0, "Enter cannot execute an unavailable path action");
                popup.Present(snapshot with { Text = @"D:\Documents\example.txt" }, 100, 100, actionSettings: menuSettings); OpenMore(); popup.UpdateLayout();
                Check(MenuPath().IsVisible && MenuPath().IsEnabled && popup.FindActionButton(TextAction.RevealPath).Visibility == Visibility.Collapsed,
                    "valid path stays in configured More menu and becomes enabled without promotion");
                MenuPath().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(dispatched == 1, "enabled path menu item dispatches through the existing action event");

                void DeleteCustom(string id)
                {
                    var placement = current.Layout.Single(e => e.Id == id).Placement;
                    Children<RadioButton>(window).Single(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_" + placement).IsChecked = true;
                    window.UpdateLayout();
                    var row = Children<Grid>(window).Single(g => AutomationProperties.GetAutomationId(g) == "ActionRow_" + id);
                    var more = Children<Button>(row).Single(b => AutomationProperties.GetName(b) == "⋯");
                    more.ContextMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "刪除動作")
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    window.UpdateLayout();
                }
                Button RestoreButton() => Children<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == "RestoreDeletedAction");
                var import = Children<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == "ImportActions");
                Check(import.IsVisible && AutomationProperties.GetName(import) == "匯入…" && import.ContextMenu == null
                    && !Children<Button>(window).Any(b => AutomationProperties.GetName(b) == "動作選項") && !RestoreButton().IsVisible,
                    "import is directly available without a global options menu and restore stays hidden before deletion");
                DeleteCustom(fifth.Id);
                Check(RestoreButton().IsVisible && Children<TextBlock>(window).Any(t => t.Text == $"已刪除「{fifth.Name}」")
                    && !current.CustomActions.Any(a => a.Id == fifth.Id), "deletion exposes named inline restore next to the removed action notice");
                UiAppearanceTest.Snapshot(window, "actions-deleted.png");
                Children<Expander>(window).Single(e => AutomationProperties.GetAutomationId(e) == "ActionSettingsAppearance").IsExpanded = true;
                window.UpdateLayout();
                Children<ComboBox>(window).Single(c => AutomationProperties.GetName(c) == "動作列主題").SelectedIndex = 1;
                Children<CheckBox>(window).Single(c => (string)c.Content == "圖示與文字").IsChecked = false;
                Children<CheckBox>(window).Single(c => (string)c.Content == "圖示與文字").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                var afterDelete = current;
                RestoreButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                Check(!RestoreButton().IsVisible && current.CustomActions.Contains(fifth) && current.Theme == "Light" && !current.ShowLabels
                    && current.Layout.Where(e => e.Id != fifth.Id).SequenceEqual(afterDelete.Layout) && Preview(window).SequenceEqual(expected),
                    "inline restore returns the original slot without rolling back later theme labels or other layout entries");
                Check(new SettingsStore(path).Load().Settings.Actions.CustomActions.Contains(fifth), "restored action is persisted to disk");
                DeleteCustom(fifth.Id); Place(tenth.Name, ActionPlacement.Bar);
                var filled = Preview(window);
                RestoreButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                Check(current.Layout.Single(e => e.Id == fifth.Id).Placement == ActionPlacement.More && Preview(window).SequenceEqual(filled)
                    && Children<TextBlock>(window).Any(t => t.Text.Contains("動作列已滿，已放入更多選單")),
                    "restore falls back to More with an explanation when the bar has filled since deletion");
                DeleteCustom(fifth.Id); DeleteCustom(tenth.Id);
                RestoreButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                Check(!current.CustomActions.Any(a => a.Id == fifth.Id) && current.CustomActions.Contains(tenth), "only the latest deleted action is restored");
                DeleteCustom(tenth.Id); window.Close();
                window = new ActionSettingsWindow(current, value => { current = value; return false; }); window.Show(); window.UpdateLayout();
                Check(!RestoreButton().IsVisible, "closing settings clears the temporary restore offer");
                // A failed disk save still applies settings for this session; keep its warning visible.
                var retry = fifth with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "儲存失敗測試" };
                window.Close(); current = ActionCatalog.Normalize(current with { CustomActions = [.. current.CustomActions, retry] });
                window = new ActionSettingsWindow(current, value => { current = value; return false; }); window.Show(); window.UpdateLayout();
                DeleteCustom(retry.Id);
                Check(RestoreButton().IsVisible && Children<TextBlock>(window).Any(t => t.Text.Contains("無法保存")), "deletion notice preserves a failed save warning");
                RestoreButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                Check(!RestoreButton().IsVisible && current.CustomActions.Contains(retry) && Children<TextBlock>(window).Any(t => t.Text.Contains("無法保存")),
                    "restore applies for the session while retaining a failed save warning");
                CheckChosenIcons(Check, snapshot);
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error.Message); }
            finally
            {
                popup?.Close(); reloadedWindow?.Close(); window?.Close();
                if (File.Exists(path)) File.Delete(path); Directory.Delete(directory, false);
                Console.WriteLine($"{passed} passed, {(result == 0 ? 0 : 1)} failed"); app.Shutdown();
            }
        };
        app.Run(); return result;
    }

    private static void CheckChosenIcons(Action<bool, string> check, SelectionSnapshot snapshot)
    {
        var icons = new[] { "legacy:globe", "translate_20_regular", "future_icon_20_regular" };
        var actions = icons.Select((icon, i) => new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"),
            "Icon " + i, CustomActionKind.JavaScript, icon, "return selection;", ActionOutput.ReplaceSelection)).ToArray();
        var popup = new PopupWindow();
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            foreach (var labels in new[] { true, false })
            foreach (var placement in new[] { ActionPlacement.Bar, ActionPlacement.More })
            {
                var settings = ActionCatalog.Normalize(new ActionSettings
                {
                    CustomActions = actions, Theme = theme, ShowLabels = labels,
                    Layout = [new("copy", ActionPlacement.Bar), new("search", ActionPlacement.Bar),
                        new("cut", ActionPlacement.More), new("paste", ActionPlacement.More),
                        .. actions.Select(a => new ActionLayoutEntry(a.Id, placement))]
                });
                var foreground = theme == "Dark" ? Brushes.White : ActionUi.Ink;
                bool Color(Brush actual, Brush expected) => actual.ToString() == expected.ToString();
                void AssertIcon(DependencyObject surface, string icon, Brush color, string name)
                {
                    var presenter = Children<AiIconPresenter>(surface).Single();
                    check(presenter.IconId == icon && presenter.Child != null && Color(presenter.Foreground, color), name);
                    if (icon == icons[2])
                        check(presenter.Child is Viewbox { Child: System.Windows.Shapes.Path { Data: not null } }, name + " unknown identifier renders fallback");
                    if (icon == icons[1])
                        check(presenter.Child is TextBlock { Text.Length: > 0 } glyph && Color(glyph.Foreground, color), name + " Fluent glyph is tinted");
                }
                var label = $"{theme} labels={labels} {placement}";
                var settingsWindow = new ActionSettingsWindow(settings, _ => true);
                try
                {
                    settingsWindow.Show(); settingsWindow.UpdateLayout();
                    Children<RadioButton>(settingsWindow).Single(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_" + placement).IsChecked = true;
                    settingsWindow.UpdateLayout();
                    foreach (var action in actions)
                    {
                        var row = Children<Grid>(settingsWindow).Single(g => AutomationProperties.GetAutomationId(g) == "ActionRow_" + action.Id);
                        AssertIcon(row, action.Icon, ActionUi.Accent, label + " settings list " + action.Icon);
                        if (placement != ActionPlacement.Bar) continue;
                        var preview = Children<StackPanel>(settingsWindow).Single(p => AutomationProperties.GetAutomationId(p) == "ActionBarPreview_" + action.Id);
                        AssertIcon(preview, action.Icon, foreground, label + " preview " + action.Icon);
                        check(Children<TextBlock>(preview).Any(t => t.Text == action.Name) == labels, label + " preview labels");
                    }
                    if (labels) UiAppearanceTest.Snapshot(settingsWindow, $"action-icons-settings-{theme}-{placement}.png");
                }
                finally { settingsWindow.Close(); }
                popup.Present(snapshot, 100, 100, actionSettings: settings);
                Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                popup.UpdateLayout();
                foreach (var action in actions)
                {
                    var id = placement == ActionPlacement.Bar ? "CustomAction_" : "MoreAction_";
                    var button = Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == id + action.Id);
                    AssertIcon(button, action.Icon, foreground, label + " popup " + action.Icon);
                    check(button.IsVisible && button.IsEnabled && !button.Focusable && !button.IsTabStop, label + " popup action availability and focus");
                    var content = (StackPanel)button.Content;
                    check(content.Children.OfType<TextBlock>().Any(t => t.Text == action.Name) == (labels || placement == ActionPlacement.More)
                        && ((FrameworkElement)content.Children[0]).Margin.Right == (labels || placement == ActionPlacement.More ? 7 : 0), label + " popup labels and icon margin");
                }
                foreach (var action in new[] { TextAction.Copy, TextAction.Search })
                {
                    var button = popup.FindActionButton(action);
                    check(((StackPanel)button.Content).Children[0] is Viewbox { Child: System.Windows.Shapes.Path path }
                        && Color(path.Stroke, foreground), label + " builtin " + action + " retains tinted path");
                }
                if (labels) UiAppearanceTest.Snapshot(popup, $"action-icons-popup-{theme}-{placement}.png");
                var disabledSettings = settings with { Layout = settings.Layout.Select(e => actions.Any(a => a.Id == e.Id)
                    ? e with { Placement = ActionPlacement.More } : e).ToArray() };
                popup.Present(snapshot with { Text = "" }, 100, 100, actionSettings: disabledSettings);
                Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                popup.UpdateLayout();
                foreach (var action in actions)
                {
                    var button = Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreAction_" + action.Id);
                    AssertIcon(button, action.Icon, foreground, label + " disabled popup " + action.Icon);
                    check(!button.IsEnabled, label + " unavailable action remains disabled");
                }
                popup.Dismiss();
            }
        }
        finally { popup.Close(); }
    }
}
