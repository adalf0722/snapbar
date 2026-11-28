using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;

internal static class AiComparisonTest
{
    private sealed class Client : IAiTextClient
    {
        public string Result = "我們將於週五推出新版。新版操作更簡單，也能保留你的工作進度。";
        public Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string key, string source, string instruction, CancellationToken token)
            => Task.FromResult(new AiTextResult(true, Result));
    }
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(item, i))) yield return child;
    }
    internal static async Task RunAsync(string directory, Action<bool, string> check, Action<Window, string> snapshot)
    {
        var path = Path.Combine(directory, "comparison"); var preferences = new AiPreferences(path);
        var profile = preferences.Settings.Profiles.Single(p => p.Id == "compatible") with { Model = "fixture-model" };
        check(preferences.TrySave(preferences.Settings with { SelectedProfileId = profile.Id,
            Profiles = preferences.Settings.Profiles.Select(p => p.Id == profile.Id ? profile : p).ToArray() }, out _), "AI comparison uses isolated fake service");
        const string source = "我們將在週五的時候推出新版。新版的操作將會更加簡單，而且可以保留你的工作進度。";
        var client = new Client();
        var panel = new AiPanelWindow(new SelectionSnapshot("ui", new(0, 1, 1), source, new(100, 100, 50, 20), false, 1, 43), preferences, client: client);
        try
        {
            panel.Show(); panel.Activate(); await Task.Delay(80); panel.UpdateLayout();
            Button Button(string id) => Children<Button>(panel).Single(b => AutomationProperties.GetAutomationId(b) == id);
            Border Pane(string id) => Children<Border>(panel).Single(b => AutomationProperties.GetAutomationId(b) == id);
            void Click(string id) { Button(id).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); panel.UpdateLayout(); }
            var sourcePane = Pane("AiOriginalPane"); var resultPane = Pane("AiResultPane");
            var initialHeight = sourcePane.ActualHeight;
            var commands = Children<Button>(panel).Where(b => AutomationProperties.GetAutomationId(b).StartsWith("AiPrompt_", StringComparison.Ordinal)).ToArray();
            var commandScroll = Children<ScrollViewer>(panel).Single(s => AutomationProperties.GetAutomationId(s) == "AiCommandList");
            check(commands.Length == 7 && commandScroll.ScrollableHeight < 1 && commands.All(b => b.ActualHeight <= 44),
                "AI standard compact list shows every default command without scrolling");
            snapshot(panel, "reading-ready.png");
            await panel.SubmitAsync("Improve writing", "improve"); panel.UpdateLayout();
            check(sourcePane.ActualHeight < initialHeight && resultPane.ActualHeight > sourcePane.ActualHeight,
                "AI first result changes from source-focused layout to comparison proportions");
            check(AutomationProperties.GetItemStatus(Button("AiPrompt_improve")).Length > 0
                && AutomationProperties.GetItemStatus(Button("AiPrompt_summary")).Length == 0, "AI result identifies its command independently of pins");
            panel.Width = 1100; panel.UpdateLayout();
            var sourceViewer = Children<FlowDocumentScrollViewer>(sourcePane).Single();
            var resultViewer = Children<FlowDocumentScrollViewer>(resultPane).Single();
            var sourceRect = sourceViewer.Document.ContentStart.GetNextInsertionPosition(LogicalDirection.Forward)!.GetCharacterRect(LogicalDirection.Forward);
            var resultRect = resultViewer.Document.ContentStart.GetNextInsertionPosition(LogicalDirection.Forward)!.GetCharacterRect(LogicalDirection.Forward);
            check(Math.Abs(sourceViewer.TranslatePoint(sourceRect.TopLeft, panel).Y - resultViewer.TranslatePoint(resultRect.TopLeft, panel).Y) < 1,
                "AI original and result first text lines align exactly"); snapshot(panel, "reading-polished-wide.png");
            Click("AiToggleDifferences");
            sourceViewer = Children<FlowDocumentScrollViewer>(sourcePane).Single(); resultViewer = Children<FlowDocumentScrollViewer>(resultPane).Single();
            var removed = sourceViewer.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).ToArray();
            var added = resultViewer.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).ToArray();
            check(removed.Any(r => r.TextDecorations.Contains(TextDecorations.Strikethrough[0]))
                && added.Any(r => r.TextDecorations.Contains(TextDecorations.Underline[0]))
                && string.Concat(removed.Select(r => r.Text)) == source && string.Concat(added.Select(r => r.Text)) == client.Result,
                "AI differences preserve full literal text and distinguish removal and insertion without relying on color");
            check(await AiClipboardTest.CopyExactAsync(panel, "AiCopy", client.Result), "AI difference mode copies the exact raw result");
            check(await AiClipboardTest.CopyExactAsync(panel, "AiCopyOriginal", source), "AI difference mode copies the exact original");
            snapshot(panel, "reading-differences.png");
            Click("AiToggleDifferences");
            var splitter = Children<GridSplitter>(panel).Single(); var grid = (Grid)splitter.Parent;
            grid.ColumnDefinitions[0].Width = new(0.6, GridUnitType.Star); grid.ColumnDefinitions[2].Width = new(0.4, GridUnitType.Star); panel.UpdateLayout();
            splitter.RaiseEvent(new DragCompletedEventArgs(10, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            check(Math.Abs(new AiPreferences(path).Settings.ReaderHorizontalRatio - 0.6) < 0.01,
                "AI horizontal divider changes persist to isolated preferences");
            panel.Width = 800; panel.UpdateLayout();
            grid.RowDefinitions[0].Height = new(0.45, GridUnitType.Star); grid.RowDefinitions[2].Height = new(0.55, GridUnitType.Star); panel.UpdateLayout();
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 10, false) { RoutedEvent = Thumb.DragCompletedEvent });
            var reloaded = new AiPreferences(path).Settings;
            check(Math.Abs(reloaded.ReaderVerticalRatio - 0.45) < 0.01 && Math.Abs(reloaded.ReaderHorizontalRatio - 0.6) < 0.01,
                "AI vertical divider persists independently from horizontal preferences");
            snapshot(panel, "reading-polished-stacked.png");
            client.Result = new string('長', 12001); await panel.SubmitAsync("Long result", "summary"); panel.UpdateLayout();
            check(!Button("AiToggleDifferences").IsEnabled && panel.Session.Current?.Text == client.Result,
                "AI long outputs keep complete content while bounding difference work");
            panel.InvalidateTarget(); panel.UpdateLayout();
            check(Children<TextBlock>(panel).Count(t => t.Text.Contains("原選取已失效")) == 1 && !Button("AiReplace").IsVisible,
                "AI invalidated selection has one status message and cannot replace text");
        }
        finally { panel.Close(); }
    }
}
