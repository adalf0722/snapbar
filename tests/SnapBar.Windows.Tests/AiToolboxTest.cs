using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;

internal static class AiToolboxTest
{
    private sealed class Client : IAiTextClient
    {
        internal int Calls;
        internal TaskCompletionSource<AiTextResult>? Pending;
        public Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string key, string source, string instruction, CancellationToken token)
        { Calls++; return Pending?.Task ?? Task.FromResult(new AiTextResult(true, "Toolbox fixture result")); }
    }

    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        for (var n = 0; n < VisualTreeHelper.GetChildrenCount(item); n++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(item, n))) yield return child;
    }

    internal static async Task RunAsync(string directory, Action<bool, string> check, Action<Window, string> snapshot)
    {
        var preferences = new AiPreferences(Path.Combine(directory, "toolbox")); var settings = preferences.Settings;
        var profile = settings.Profiles.Single(p => p.Id == "compatible") with { Model = "fixture-model" };
        check(preferences.TrySave(settings with { SelectedProfileId = profile.Id, Profiles = settings.Profiles.Select(p => p.Id == profile.Id ? profile : p).ToArray() }, out _),
            "AI toolbox uses isolated fake service settings");
        var client = new Client();
        var source = new SelectionSnapshot("ui", new(0, 1, 1), "Disposable toolbox source.", new(100, 100, 50, 20), false, 1, 43);
        var panel = new AiPanelWindow(source, preferences, client: client);
        try
        {
            panel.Show(); panel.Activate(); await Task.Delay(100); panel.UpdateLayout();
            Button[] Commands() => Children<Button>(panel).Where(b => AutomationProperties.GetAutomationId(b).StartsWith("AiPrompt_", StringComparison.Ordinal)).ToArray();
            Button Button(string id) => Children<Button>(panel).Single(b => AutomationProperties.GetAutomationId(b) == id);
            void Press(UIElement target, Key key)
            {
                target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(panel), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                panel.UpdateLayout();
            }
            var commands = Commands();
            var commandArea = Children<Border>(panel).Single(b => AutomationProperties.GetAutomationId(b) == "AiCommandArea");
            var readerArea = Children<Border>(panel).Single(b => AutomationProperties.GetAutomationId(b) == "AiReaderArea");
            check(commandArea.BorderThickness == readerArea.BorderThickness && commandArea.CornerRadius == readerArea.CornerRadius
                && Math.Abs(commandArea.ActualHeight - readerArea.ActualHeight) < 1
                && readerArea.TranslatePoint(new Point(), panel).X - commandArea.TranslatePoint(new Point(commandArea.ActualWidth, 0), panel).X >= 17,
                "AI toolbox has equally tall matching command and reader surfaces separated by a gutter");
            check(Math.Abs(commands[0].TranslatePoint(new Point(), panel).Y - commands[1].TranslatePoint(new Point(), panel).Y) < 1
                && commands[1].TranslatePoint(new Point(), panel).X > commands[0].TranslatePoint(new Point(commands[0].ActualWidth, 0), panel).X
                && commands[2].TranslatePoint(new Point(), panel).Y >= commands[0].TranslatePoint(new Point(0, commands[0].ActualHeight), panel).Y,
                "AI toolbox commands form two distinct columns in visual row order");
            panel.CommandInput.Focus(); Press(panel.CommandInput, Key.Down);
            check(commands[0].IsKeyboardFocused, "AI toolbox Down from input focuses first command");
            Press(commands[0], Key.Right); check(commands[1].IsKeyboardFocused, "AI toolbox Right moves within the first row");
            Press(commands[1], Key.Right); check(commands[1].IsKeyboardFocused, "AI toolbox Right never wraps into the next row");
            Press(commands[1], Key.Down); check(commands[3].IsKeyboardFocused, "AI toolbox Down moves in the same column");
            Press(commands[3], Key.Down); check(commands[4].IsKeyboardFocused, "AI toolbox Down reaches the lone final-row command");
            Press(commands[4], Key.Down); check(commands[4].IsKeyboardFocused, "AI toolbox Down stops at the final row");
            Press(commands[4], Key.Up); check(commands[2].IsKeyboardFocused, "AI toolbox Up follows the final command column");
            Press(commands[2], Key.Left); check(commands[2].IsKeyboardFocused, "AI toolbox Left stops at the first column");
            Press(commands[2], Key.Up); Press(commands[0], Key.Up);
            check(panel.CommandInput.IsKeyboardFocused, "AI toolbox Up from the top row returns to input");
            commands[0].Focus(); var pin = Button("AiPin_improve"); pin.Focus();
            pin.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); panel.UpdateLayout();
            check(Button("AiPrompt_improve").IsKeyboardFocused, "AI toolbox pin reordering preserves focused prompt identity");
            panel.CommandInput.Text = "summary"; panel.CommandInput.CaretIndex = panel.CommandInput.Text.Length; panel.CommandInput.Focus(); panel.UpdateLayout();
            commands = Commands(); check(commands.Length == 1, "AI toolbox search rebuilds a single visible command");
            Press(panel.CommandInput, Key.Down); Press(commands[0], Key.Down); Press(commands[0], Key.Right);
            check(commands[0].IsKeyboardFocused, "AI toolbox single-command arrows remain on the existing cell");
            var options = Button("AiOptions_summary"); options.Focus(); Press(options, Key.Escape);
            check(panel.IsVisible && panel.CommandInput.IsKeyboardFocused && panel.CommandInput.Text == "summary",
                "AI toolbox Escape from management returns to input without clearing the draft or closing");
            client.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Press(panel.CommandInput, Key.Down); Press(commands[0], Key.Enter);
            check(client.Calls == 1 && panel.Session.Busy && Commands().All(b => !b.IsEnabled)
                && Children<TextBlock>(Button("AiPrompt_summary")).Any(t => t.Text == "處理中…"),
                "AI toolbox busy execution disables commands and identifies the running tile");
            client.Pending.SetResult(new(true, "Toolbox fixture result"));
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (panel.Session.Busy && DateTime.UtcNow < deadline) await Task.Delay(10);
            check(client.Calls == 1 && panel.Session.Current?.Text == "Toolbox fixture result",
                "AI toolbox Enter executes the focused saved command exactly once");
            panel.CommandInput.Text = "unmatched-fixture-query"; panel.CommandInput.Focus(); Press(panel.CommandInput, Key.Down);
            check(Commands().Length == 0 && panel.CommandInput.IsKeyboardFocused && client.Calls == 1,
                "AI toolbox empty search has no stale keyboard targets or execution");
            panel.CommandInput.Clear(); var category = (ComboBox)Children<Expander>(panel).Single().Content; category.SelectedItem = "閱讀"; panel.UpdateLayout();
            check(Commands().Length == 1 && AutomationProperties.GetAutomationId(Commands()[0]) == "AiPrompt_summary",
                "AI toolbox category filtering rebuilds spatial targets"); category.SelectedItem = "全部分類";

            var many = Enumerable.Range(0, 32).Select(n => new AiPrompt("tile-" + n,
                n == 0 ? "這是一個較長的自訂指令名稱用來確認兩行文字與完整提示" : $"指令 {n + 1}", "Fixture instruction " + n, "自訂")).ToArray();
            check(preferences.TrySave(preferences.Settings with { Prompts = many, PinnedIds = [], RecentIds = [] }, out _), "AI toolbox accepts a larger isolated command collection");
            panel.UpdateLayout(); commands = Commands();
            var longName = Children<TextBlock>(commands[0]).Single(t => t.Text == many[0].Name);
            check(longName.TextWrapping == TextWrapping.Wrap && longName.ActualHeight <= 36.5
                && longName.ActualWidth <= commands[0].ActualWidth && (string)commands[0].ToolTip == many[0].Name,
                "AI toolbox long names fit a bounded wrapped label with the full name available"); snapshot(panel, "toolbox-long-name.png");
            panel.Width = 680; panel.Height = 500; panel.UpdateLayout(); commands[0].Focus();
            for (var n = 0; n < 15; n++) Press((UIElement)Keyboard.FocusedElement, Key.Down);
            Press((UIElement)Keyboard.FocusedElement, Key.Right);
            await panel.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); panel.UpdateLayout();
            var scroll = Children<ScrollViewer>(panel).Single(s => AutomationProperties.GetAutomationId(s) == "AiCommandList");
            var last = commands[^1]; var lastTop = last.TranslatePoint(new Point(), scroll).Y;
            check(last.IsKeyboardFocused && scroll.VerticalOffset > 0 && lastTop >= 0 && lastTop + last.ActualHeight <= scroll.ActualHeight + 1,
                "AI toolbox keyboard navigation scrolls the last command completely into view");
            var copy = Button("AiCopy");
            check(copy.IsVisible && copy.TranslatePoint(new Point(0, copy.ActualHeight), panel).Y <= panel.ActualHeight - 8
                && panel.CommandInput.ActualWidth > 200 && readerArea.ActualHeight > 200,
                "AI toolbox minimum window keeps input reader and copy controls available"); snapshot(panel, "toolbox-many-small.png");
        }
        finally { panel.Close(); }
    }
}
