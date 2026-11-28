using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;

internal static class ActionSettingsInteractionTest
{
    private static IEnumerable<T> Children<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }

    internal static void Run(Action<bool, string> check)
    {
        var current = ActionCatalog.Normalize(new()); var saves = 0;
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Drag.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path); var loaded = store.Load().Settings;
        var panel = new ActionSettingsPanel(current, value => { current = value; saves++; store.Save(loaded with { Actions = value }); return true; });
        var window = new Window { Content = panel, Width = 1000, Height = 850 };
        window.Show(); window.UpdateLayout();
        try
        {
            Grid Row(string id) => Children<Grid>(panel).Single(g => AutomationProperties.GetAutomationId(g) == "ActionRow_" + id);
            FrameworkElement Group(ActionPlacement placement) => (FrameworkElement)Children<TextBlock>(panel)
                .Single(t => AutomationProperties.GetAutomationId(t) == "ActionGroup_" + placement).Parent;
            string[] Pins() => current.Layout.Where(e => e.Placement == ActionPlacement.Bar).Select(e => e.Id).ToArray();
            IDataObject Payload(string id, ActionSettingsPanel? owner = null)
            {
                var type = typeof(ActionSettingsPanel).GetNestedType("ActionDragPayload", BindingFlags.NonPublic)!;
                return new DataObject("SnapBar.ActionSettings.Move", Activator.CreateInstance(type, owner ?? panel, id)!);
            }
            DragEventArgs Drag(FrameworkElement target, IDataObject data, RoutedEvent routedEvent, bool after = false)
            {
                window.UpdateLayout();
                var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
                var args = (DragEventArgs)constructor.Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move,
                    target, new Point(10, after ? target.ActualHeight - 1 : 1)]);
                args.RoutedEvent = routedEvent; target.RaiseEvent(args); window.UpdateLayout(); return args;
            }
            void Menu(string id, string label)
            {
                Row(id).ContextMenu.Items.OfType<MenuItem>().Single(i => (string)i.Header == label)
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                window.UpdateLayout();
            }

            check(Children<Border>(panel).Where(b => AutomationProperties.GetAutomationId(b).StartsWith("ReorderAction_"))
                .All(b => b.Cursor == GrabCursors.Open) && GrabCursors.Open != GrabCursors.Closed,
                "both groups load distinct grab and grabbing cursors");
            var data = Payload("link");
            var before = current;
            check(Drag(Row("copy"), data, DragDrop.DragOverEvent).Effects == DragDropEffects.Move
                && saves == 0 && current == before, "drag-over previews a cross-group move without saving");
            Drag(Row("copy"), data, DragDrop.DropEvent);
            check(Pins().SequenceEqual(new[] { "link", "copy", "cut", "paste", "search" }) && saves == 1,
                "dropping More action above a bar row inserts before it and saves once");
            check(new SettingsStore(path).Load().Settings.Actions.Layout.SequenceEqual(current.Layout),
                "dropped placement and insertion order survive settings reload");
            Drag(Row("search"), Payload("link"), DragDrop.DropEvent, after: true);
            check(Pins().SequenceEqual(new[] { "copy", "cut", "paste", "search", "link" }), "bar drop below row inserts after it");
            Drag(Group(ActionPlacement.More), Payload("copy"), DragDrop.DropEvent);
            check(current.Layout.Single(e => e.Id == "copy").Placement == ActionPlacement.More,
                "bar action drops on More group heading");
            Menu("copy", "隱藏動作"); Menu("copy", "移至動作列");
            check(!current.Layout.Single(e => e.Id == "copy").IsVisible && Pins()[^1] == "copy",
                "context menu moves hidden actions while preserving visibility");
            check(Children<ComboBox>(panel).Single(c => AutomationProperties.GetAutomationId(c) == "ActionPlacement_copy").IsKeyboardFocused,
                "context commands return keyboard focus to the moved action");
            Menu("copy", "顯示動作");
            var saved = saves;
            check(Drag(Row("cut"), new DataObject("copy"), DragDrop.DragOverEvent).Effects == DragDropEffects.None,
                "external text drags are rejected");
            var other = new ActionSettingsPanel(current, _ => true);
            check(Drag(Row("cut"), Payload("copy", other), DragDrop.DropEvent).Effects == DragDropEffects.None && saves == saved,
                "drag payload from a different settings panel cannot change settings");
            Drag(Row("cut"), Payload("copy"), DragDrop.DragOverEvent);
            Drag(Row("cut"), Payload("copy"), DragDrop.DragLeaveEvent);
            check(saves == saved && Row("cut").Children.OfType<Border>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "ActionDropMarker_cut").Visibility == Visibility.Collapsed,
                "leaving an uncommitted drop clears the marker without saving");

            var full = ActionCatalog.Normalize(new());
            foreach (var id in full.Layout.Where(e => e.Placement == ActionPlacement.More).Take(5).Select(e => e.Id).ToArray())
                full = ActionCatalog.SetPlacement(full, id, ActionPlacement.Bar);
            current = full; panel.ShowSettings(full); window.UpdateLayout();
            var extra = full.Layout.First(e => e.Placement == ActionPlacement.More).Id;
            saved = saves;
            check(Drag(Group(ActionPlacement.Bar), Payload(extra), DragDrop.DropEvent).Effects == DragDropEffects.None
                && saves == saved && current == full && Children<TextBlock>(panel).Any(t => t.Text.Contains("最多可釘選 9 個動作")),
                "full bar rejects a drop with a visible capacity reason and no partial save");
            Menu(extra, "移至動作列");
            check(current == full && saves == saved, "context menu enforces the same capacity limit");
            Menu(extra, "隱藏動作"); Menu(extra, "移至動作列");
            check(!current.Layout.Single(e => e.Id == extra).IsVisible && Pins().Contains(extra)
                && current.Layout.Count(e => e.Placement == ActionPlacement.Bar && e.IsVisible) == 9,
                "hidden action can move to a full bar without consuming visible capacity");

            foreach (var id in Pins()) panel.MoveAction(id, ActionPlacement.More);
            window.UpdateLayout();
            var emptyBar = Children<TextBlock>(panel).Single(t => AutomationProperties.GetAutomationId(t) == "ActionGroupEmpty_Bar");
            Drag(emptyBar, Payload("copy"), DragDrop.DropEvent);
            check(Pins().SequenceEqual(new[] { "copy" }), "empty bar accepts a dropped action");
            current = ActionCatalog.Normalize(current with { Layout = current.Layout.Select(e => e with
                { Placement = ActionPlacement.Bar, IsVisible = e.Id == "copy" }).ToArray() });
            panel.ShowSettings(current); window.UpdateLayout();
            var emptyMore = Children<TextBlock>(panel).Single(t => AutomationProperties.GetAutomationId(t) == "ActionGroupEmpty_More");
            Drag(emptyMore, Payload("copy"), DragDrop.DropEvent);
            check(current.Layout.Single(e => e.Id == "copy").Placement == ActionPlacement.More,
                "empty More group accepts a dropped action");
            current = ActionCatalog.Normalize(new()); panel.ShowSettings(current); window.UpdateLayout();
            UiAppearanceTest.Snapshot(window, "actions-drag-groups.png");
            var context = Row("copy").ContextMenu;
            context.PlacementTarget = Row("copy"); context.IsOpen = true; window.UpdateLayout();
            check(context.IsOpen && context.Items.OfType<MenuItem>().Count() == 2,
                "builtin context menu opens with only placement and visibility commands");
            UiAppearanceTest.Snapshot(window, "actions-context-menu.png");
            context.IsOpen = false;
            foreach (var custom in current.CustomActions)
            {
                var customRow = Row(custom.Id);
                var options = Children<Button>(customRow).Single(b => AutomationProperties.GetAutomationId(b) == "CustomActionOptions_" + custom.Id);
                foreach (var entry in new[] { (Target: (FrameworkElement)customRow, Menu: customRow.ContextMenu, Name: "right-click"),
                    (Target: (FrameworkElement)options, Menu: options.ContextMenu, Name: "options button") })
                {
                    try
                    {
                        entry.Menu.PlacementTarget = entry.Target;
                        entry.Menu.IsOpen = true;
                        entry.Menu.UpdateLayout();
                        panel.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        check(entry.Menu.IsOpen && entry.Menu.ActualHeight > 0
                            && entry.Menu.Items.OfType<Separator>().Count() == 1
                            && entry.Menu.Items.OfType<MenuItem>().Count() == 6,
                            "custom action " + entry.Name + " menu renders separator and all commands without crashing");
                    }
                    finally { entry.Menu.IsOpen = false; }
                }
            }
            window.Width = 760; window.Height = 620; window.UpdateLayout();
            UiAppearanceTest.Snapshot(window, "actions-drag-small.png");
            Children<TextBox>(panel).Single(t => AutomationProperties.GetAutomationId(t) == "ActionsSearch").Text = "複製";
            window.UpdateLayout();
            check(!Row("copy").AllowDrop && !Children<Border>(panel).Any(b => AutomationProperties.GetAutomationId(b).StartsWith("ReorderAction_")),
                "search disables drag handles and drop targets");
            Menu("copy", "移至更多");
            check(current.Layout.Single(e => e.Id == "copy").Placement == ActionPlacement.More,
                "context movement remains available while searching");
        }
        finally { window.Close(); if (File.Exists(path)) File.Delete(path); Directory.Delete(directory, false); }
    }
}
