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
                var seventh = fifth with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "第七個" };
                var current = ActionCatalog.Normalize(new() { CustomActions = [fifth, seventh] });
                window = new ActionSettingsWindow(current, value => { current = value; store.Save(loaded with { Actions = value }); return true; });
                window.Show(); window.UpdateLayout();
                string[] Preview(Window view) => Children<StackPanel>(view).Select(AutomationProperties.GetAutomationId)
                    .Where(id => id.StartsWith("ActionBarPreview_", StringComparison.Ordinal)).Select(id => id[17..]).ToArray();
                void Place(string name, ActionPlacement placement)
                {
                    var id = ActionCatalog.GetItems(current).Single(a => a.Name == name).Id;
                    var currentPlacement = current.Layout.Single(e => e.Id == id).Placement;
                    Children<RadioButton>(window).Single(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_" + currentPlacement).IsChecked = true;
                    Children<ComboBox>(window).Single(c => AutomationProperties.GetName(c) == name + "顯示位置").SelectedIndex = (int)placement;
                    Children<RadioButton>(window).Single(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_" + placement).IsChecked = true;
                    window.UpdateLayout();
                }
                Check(Preview(window).SequenceEqual(new[] { "copy", "cut", "paste", "search" }), "settings preview defaults to four pinned actions");
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
                Place(seventh.Name, ActionPlacement.Bar);
                Check(Preview(window).SequenceEqual(expected) && current.Layout.Single(e => e.Id == seventh.Id).Placement == ActionPlacement.More,
                    "seventh pin is refused without corrupting six-item preview");
                var restarted = new SettingsStore(path).Load();
                Check(restarted.Warning == null && ActionCatalog.GetPinned(restarted.Settings.Actions).Select(a => a.Id).SequenceEqual(expected), "all six positions survive disk roundtrip");
                reloadedWindow = new ActionSettingsWindow(restarted.Settings.Actions, _ => true); reloadedWindow.Show(); reloadedWindow.UpdateLayout();
                Check(Preview(reloadedWindow).SequenceEqual(expected), "reopened native settings preview preserves six-item order");
                var snapshot = new SelectionSnapshot("layout", new(0, 0, 0), "normal text", new(100, 100, 20, 20), true, 1, 11);
                popup = new PopupWindow(); popup.Present(snapshot, 100, 100, actionSettings: current); popup.UpdateLayout();
                var visible = Children<Button>(popup).Where(b => b.Visibility == Visibility.Visible && b.Tag is ActionItem).Select(b => ((ActionItem)b.Tag).Id).ToArray();
                Check(visible.SequenceEqual(new[] { "copy", "cut", "paste", "search", fifth.Id }), "actual toolbar filters path for plain text while retaining configured relative order");
                var menuSettings = ActionCatalog.SetPlacement(new(), "link", ActionPlacement.Hidden);
                void OpenMore() => Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
}
