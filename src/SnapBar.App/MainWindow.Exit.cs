using System.Windows;
using System.Windows.Controls;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    internal bool TryResolveDraftsForExit()
    {
        var drafts = new ISettingsDraft[] { searchSettingsPanel }.Concat(aiPreferences.Drafts).Where(d => d.IsDirty).Distinct().ToArray();
        if (drafts.Length == 0) return true;
        var choice = ExitChoiceProvider?.Invoke(drafts.Select(d => d.DraftName).ToArray()) ?? ShowExitChoice(drafts);
        if (choice == ExitChoice.Cancel) return false;
        if (choice == ExitChoice.Discard)
        {
            foreach (var draft in drafts) draft.Discard();
            return true;
        }
        foreach (var draft in drafts)
        {
            if (draft.TrySave()) continue;
            draft.Reveal(); return false;
        }
        return true;
    }

    private ExitChoice ShowExitChoice(IReadOnlyList<ISettingsDraft> drafts)
    {
        var result = ExitChoice.Cancel;
        var dialog = new Window { Owner = IsVisible ? this : null, ShowInTaskbar = !IsVisible, Topmost = true };
        ActionUi.Setup(dialog, UiText.Get("MainWindow_Exit_000"), 580, 360); dialog.ResizeMode = ResizeMode.CanResize;
        var body = new DockPanel { Margin = new(24) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        void Add(string text, ExitChoice choice, bool primary = false) => buttons.Children.Add(ActionUi.Button(text, () => { result = choice; dialog.Close(); }, primary: primary));
        Add(UiText.Get("MainWindow_Exit_001"), ExitChoice.Cancel); Add(UiText.Get("MainWindow_Exit_002"), ExitChoice.Discard); Add(UiText.Get("MainWindow_Exit_003"), ExitChoice.Save, true);
        DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);
        var content = new StackPanel(); content.Children.Add(ActionUi.Heading(UiText.Get("MainWindow_Exit_004"), 22));
        var note = ActionUi.Text(UiText.Get("MainWindow_Exit_005"), 13, ActionUi.Muted); note.Margin = new(0, 12, 0, 10); content.Children.Add(note);
        foreach (var draft in drafts) content.Children.Add(ActionUi.Text("• " + draft.DraftName, 13));
        body.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); dialog.Content = body;
        WindowWorkArea.Attach(dialog); dialog.ShowDialog(); return result;
    }
}
