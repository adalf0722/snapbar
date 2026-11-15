using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;

internal static class SettingsFeedbackTest
{
    private sealed class Client : IAiTextClient
    {
        internal TaskCompletionSource<AiTextResult> Pending = new();
        internal string? Source, Instruction;
        internal CancellationToken Token;
        public Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string key, string source, string instruction, CancellationToken token)
        { Source = source; Instruction = instruction; Token = token; return Pending.Task; }
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Children<T>(child)) yield return descendant;
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Children<T>(root).Single(item => AutomationProperties.GetAutomationId(item) == id);

    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var preferences = new AiPreferences(Path.Combine(directory, "feedback"));
        foreach (var culture in new[] { "en", "zh-Hant" })
        {
            var language = new UiLanguage(CultureInfo.GetCultureInfo(culture));
            var panel = new ActionSettingsPanel(new(), _ => true, editSearch: () => { }, manageDictionary: () => { }, language: language);
            var window = new Window(); ActionUi.Setup(window, "Feedback fixture", 760, 620); window.Content = panel;
            AiCommandsPanel? commands = null;
            try
            {
                window.Show(); window.UpdateLayout();
                var copyUp = Find<Button>(panel, "ActionMoveUp_copy"); var searchUp = Find<Button>(panel, "ActionMoveUp_search");
                var copyPlacement = Find<ComboBox>(panel, "ActionPlacement_copy"); var searchPlacement = Find<ComboBox>(panel, "ActionPlacement_search");
                check(Math.Abs(copyUp.TranslatePoint(new Point(), panel).X - searchUp.TranslatePoint(new Point(), panel).X) < 1
                    && Math.Abs(copyPlacement.TranslatePoint(new Point(), panel).X - searchPlacement.TranslatePoint(new Point(), panel).X) < 1,
                    culture + " search editing does not shift sorting or placement columns");
                var searchEdit = Find<Button>(panel, "EditSearchAction");
                check(!Children<TextBlock>(searchEdit).Any() && searchEdit.ToolTip is string,
                    culture + " search edit is an explained icon action");
                UiAppearanceTest.Snapshot(window, "feedback-actions-toolbar-" + culture + ".png");
                var row = Find<Grid>(panel, "ActionRow_cut");
                var move = Children<Button>(row).First(b => b.IsEnabled);
                move.Focus(); window.UpdateLayout();
                var preview = Find<StackPanel>(panel, "ActionBarPreview_cut");
                check(((Border)preview.Parent).Background == ActionUi.DarkHover,
                    culture + " keyboard focus identifies the corresponding toolbar preview");
                check(((string)preview.ToolTip).Contains(language.Get("ActionSettingsPanel_040")),
                    culture + " preview tooltip identifies its name and placement");
                move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                row = Find<Grid>(panel, "ActionRow_cut");
                check(row.IsKeyboardFocusWithin, culture + " reordering at the boundary retains focus on the same action");
                var combo = Children<ComboBox>(row).Single(); combo.SelectedIndex = (int)ActionPlacement.More; window.UpdateLayout();
                Find<RadioButton>(panel, "ActionPlacementTab_More").IsChecked = true; window.UpdateLayout();
                combo = Children<ComboBox>(Find<Grid>(panel, "ActionRow_cut")).Single();
                var label = new TextBlock { Text = combo.SelectedItem.ToString(), FontSize = combo.FontSize, FontFamily = combo.FontFamily };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                check(combo.ActualWidth >= label.DesiredSize.Width + 42, culture + " placement reserves room for the complete label and arrow");
                UiAppearanceTest.Snapshot(window, "feedback-actions-" + culture + ".png");
                commands = UiText.WithCulture(language.Culture, () => new AiCommandsPanel(preferences));
                window.Content = commands; window.UpdateLayout();
                check(Find<Button>(commands, "AiCommandIcon_grammar").ToolTip is string,
                    culture + " changeable command icons explain their action");
                check(Children<TextBlock>(commands).Any(t => t.Text == language.Get("PromptDescription_grammar")),
                    culture + " command purpose is localized");
                var commandRow = Find<Grid>(commands, "AiCommandRow_grammar");
                var text = commandRow.Children.OfType<StackPanel>().Single(p => Grid.GetColumn(p) == 1);
                check(text.Children.OfType<TextBlock>().Count() == 2 && text.Children.OfType<TextBlock>().First().FontWeight == FontWeights.SemiBold,
                    culture + " command card prioritizes name and purpose in two lines");
                var edit = Find<Button>(commands, "AiCommandEdit_grammar"); var remove = Find<Button>(commands, "AiCommandDelete_grammar");
                var pin = Find<System.Windows.Controls.Primitives.ToggleButton>(commands, "AiCommandPin_grammar");
                check(!Children<TextBlock>(edit).Any() && edit.ActualWidth == remove.ActualWidth && edit.ActualWidth == pin.ActualWidth
                    && edit.ActualHeight == pin.ActualHeight && edit.ToolTip is string && ((string)pin.ToolTip).Contains(culture == "en" ? "AI panel" : "AI 面板"),
                    culture + " command action icons have equal targets and explain the pin destination");
                UiAppearanceTest.Snapshot(window, "feedback-ai-" + culture + ".png");
                window.Width = 600; window.Height = 480; window.UpdateLayout();
                var delete = Find<Button>(commands, "AiCommandDelete_grammar");
                check(delete.IsVisible && delete.TranslatePoint(new Point(delete.ActualWidth, 0), window).X <= window.ActualWidth,
                    culture + " compact command rows retain direct delete controls");
                UiAppearanceTest.Snapshot(window, "feedback-ai-small-" + culture + ".png");
            }
            finally { commands?.Detach(); window.Close(); }
        }
        var list = new AiCommandsPanel(preferences);
        try
        {
            Find<Button>(list, "AiCommandDelete_grammar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(preferences.LastDeletion?.Prompt.Id == "grammar" && !list.VisiblePromptIds.Contains("grammar"), "direct command delete retains recoverable identity");
            list.RestoreDeletedCommand();
            check(list.VisiblePromptIds[0] == "grammar", "direct command delete can be undone in its original position");
        }
        finally { list.Detach(); }

        var editor = preferences.OpenEditor(null, preferences.Settings.Prompts[0]);
        try
        {
            editor.UpdateLayout(); var trialSection = Find<AiCommandTrial>(editor, "AiCommandTrial"); trialSection.IsExpanded = true;
            await editor.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); editor.UpdateLayout();
            var scroll = Find<ScrollViewer>(editor, "AiPromptForm");
            var run = Find<Button>(editor, "AiTrialRun");
            var position = run.TranslatePoint(new Point(), scroll);
            check(run.IsVisible && position.Y >= 0 && position.Y + run.ActualHeight <= scroll.ViewportHeight + 1,
                "expanded trial remains reachable within the command editor form");
            check(Find<Button>(editor, "AiPromptSave").IsVisible, "trial expansion retains the editor save control");
            UiAppearanceTest.Snapshot(editor, "feedback-ai-editor-trial.png");
        }
        finally { editor.Close(); }

        var profile = preferences.Settings.Profiles.Single(p => p.Id == "compatible") with { Model = "fixture" };
        preferences.TrySave(preferences.Settings with { SelectedProfileId = profile.Id, Profiles = preferences.Settings.Profiles.Select(p => p.Id == profile.Id ? profile : p).ToArray() }, out _);
        var saved = File.ReadAllBytes(Path.Combine(directory, "feedback", "ai.json"));
        var instruction = ActionUi.Input("Draft instruction fixture"); var client = new Client();
        var languageTrial = new UiLanguage(CultureInfo.GetCultureInfo("en"));
        using var trial = new AiCommandTrial(instruction, preferences, languageTrial, client) { IsExpanded = true };
        var host = new Window(); ActionUi.Setup(host, "Trial fixture", 600, 620); host.Content = trial;
        try
        {
            host.Show(); host.UpdateLayout();
            var sample = Find<TextBox>(trial, "AiTrialSample"); sample.Text = "Disposable sample fixture";
            var task = trial.RunAsync();
            check(client.Source == sample.Text && client.Instruction == instruction.Text && !Find<Button>(trial, "AiTrialRun").IsEnabled,
                "trial sends only explicit sample and unsaved instruction and blocks duplicate requests");
            client.Pending.SetResult(new(true, "Fixture result")); await task; host.UpdateLayout();
            check(Find<TextBox>(trial, "AiTrialOutput").Text == "Fixture result", "trial displays the controlled service result");
            UiAppearanceTest.Snapshot(host, "feedback-ai-trial.png");
            client.Pending = new(); task = trial.RunAsync(); instruction.Text = "Changed draft";
            check(client.Token.IsCancellationRequested, "editing the draft cancels the trial request");
            client.Pending.SetResult(new(true, "Stale result")); await task;
            check(Find<TextBox>(trial, "AiTrialOutput").Visibility == Visibility.Collapsed, "late results cannot overwrite the changed draft preview");
            client.Pending = new(); task = trial.RunAsync();
            Find<Button>(trial, "AiTrialStop").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(client.Token.IsCancellationRequested && Find<Button>(trial, "AiTrialRun").IsEnabled, "trial cancel restores the run control immediately");
            client.Pending.SetResult(new(true, "Cancelled result")); await task;
            check(Find<TextBox>(trial, "AiTrialOutput").Visibility == Visibility.Collapsed, "cancelled trial results stay hidden");
            client.Pending = new(); task = trial.RunAsync(); trial.Dispose();
            check(client.Token.IsCancellationRequested, "closing the trial cancels its request");
            client.Pending.SetResult(new(true, "Closed result")); await task;
            check(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "feedback", "ai.json"))), "trial never saves the command or changes stored preferences");
        }
        finally { host.Close(); }
    }
}
