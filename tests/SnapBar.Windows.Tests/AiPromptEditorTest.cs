using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;

internal static class AiPromptEditorTest
{
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        var visited = new HashSet<DependencyObject>();
        return Walk(item);
        IEnumerable<T> Walk(DependencyObject node)
        {
            if (!visited.Add(node)) yield break;
            if (node is T match) yield return match;
            foreach (var logical in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                foreach (var child in Walk(logical)) yield return child;
            if (node is Visual)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    foreach (var child in Walk(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
    }
    private static T Find<T>(Window window, string id) where T : DependencyObject =>
        Children<T>(window).Single(item => AutomationProperties.GetAutomationId(item) == id);
    private static void Click(Window window, string id) => Find<Button>(window, id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    private static async Task Layout(Window window)
    {
        window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
    }
    public static async Task RunAsync(AiPreferences preferences, string directory, Action<bool, string> check, Action<Window, string> snapshot)
    {
        AiPromptEditorWindow? editor = null;
        try
        {
            AiPrompt? saved = null; var calls = 0;
            editor = new(null, prompt => { calls++; saved = prompt; return null; }); editor.Show(); await Layout(editor);
            var form = Find<ScrollViewer>(editor, "AiPromptForm"); var advanced = Find<Expander>(editor, "AiPromptAdvanced");
            var name = Find<TextBox>(editor, "AiPromptName"); var instruction = Find<TextBox>(editor, "AiPromptInstruction");
            check(!advanced.IsExpanded && !Find<TextBox>(editor, "AiPromptCategory").IsVisible && !Find<ComboBox>(editor, "AiPromptIcon").IsVisible,
                "AI new prompt shows only primary fields with optional settings collapsed");
            check(form.ScrollableHeight < 1 && instruction.ActualHeight >= 140
                && instruction.TranslatePoint(new Point(0, instruction.ActualHeight), form).Y <= form.ViewportHeight + 1
                && Find<Button>(editor, "AiPromptSave").IsVisible,
                "AI normal prompt editor fits name instruction and fixed save controls without scrolling"); snapshot(editor, "prompt-editor.png");
            var normalEditorHeight = instruction.ActualHeight; editor.Height = 740; await Layout(editor);
            check(instruction.ActualHeight > normalEditorHeight, "AI instruction editor grows with available window space"); editor.Height = 610; await Layout(editor);
            Click(editor, "AiPromptSave");
            check(calls == 0 && editor.IsVisible && Find<TextBlock>(editor, "AiPromptError").Text.Length > 0,
                "AI empty prompt name is rejected before persistence");
            name.Text = "測試指令"; Click(editor, "AiPromptSave");
            check(calls == 0 && editor.IsVisible, "AI empty instruction is rejected before persistence");
            instruction.Text = "  Keep original spacing.\nReturn only the rewrite.  "; Click(editor, "AiPromptSave");
            check(calls == 1 && saved is { Category: "自訂", Keywords: "", Icon: "spark" } && saved.Instruction == instruction.Text && !editor.IsVisible,
                "AI minimal prompt saves with optional defaults and exact instruction text"); editor = null;

            var existing = saved! with { Category = "已存分類", Keywords = "saved aliases", Icon = "legacy-icon" };
            editor = new(existing, prompt => { saved = prompt; return null; }); editor.Show(); await Layout(editor);
            Find<TextBox>(editor, "AiPromptName").Text = "改名後"; Click(editor, "AiPromptSave");
            check(saved is { Name: "改名後" } && saved.Id == existing.Id && saved.Category == existing.Category && saved.Keywords == existing.Keywords && saved.Icon == existing.Icon,
                "AI editing preserves collapsed category keywords and unknown existing icon"); editor = null;

            var before = preferences.Settings;
            editor = new(existing, prompt => preferences.TrySave(preferences.Settings with { Prompts = preferences.Settings.Prompts.Append(prompt).ToArray() }, out var error) ? null : error);
            editor.Show(); await Layout(editor); editor.Width = 460; editor.Height = 420; await Layout(editor);
            var save = Find<Button>(editor, "AiPromptSave");
            var content = (FrameworkElement)VisualTreeHelper.GetChild(editor, 0); var saveBounds = save.TransformToAncestor(content).TransformBounds(new Rect(save.RenderSize));
            check(saveBounds.Top >= 0 && saveBounds.Bottom <= content.ActualHeight + 1, "AI small prompt editor retains a fully visible fixed save row");
            advanced = Find<Expander>(editor, "AiPromptAdvanced"); advanced.IsExpanded = true; await Layout(editor);
            var category = Find<TextBox>(editor, "AiPromptCategory"); category.Text = "翻譯";
            var keywords = Find<TextBox>(editor, "AiPromptKeywords"); keywords.Text = "日文 japanese";
            var icons = Find<ComboBox>(editor, "AiPromptIcon");
            icons.SelectedValue = "translate"; await Layout(editor);
            check(Children<Viewbox>(icons).Any(v => v.IsVisible && v.ActualWidth > 0 && v.ActualHeight > 0)
                && Children<TextBlock>(icons).Any(t => t.IsVisible && t.ActualWidth > 0 && t.Text == "翻譯"),
                "AI selected icon shows a visible vector preview and readable name in the minimum window");
            icons.BringIntoView(); await Layout(editor); form = Find<ScrollViewer>(editor, "AiPromptForm");
            check(form.ScrollableHeight > 0 && icons.TranslatePoint(new Point(), form).Y >= 0 && icons.TranslatePoint(new Point(0, icons.ActualHeight), form).Y <= form.ViewportHeight + 1,
                "AI expanded optional settings remain reachable in the minimum window"); snapshot(editor, "prompt-editor-small-advanced.png");
            editor.Width = 570; editor.Height = 610; await Layout(editor); icons.BringIntoView(); await Layout(editor); snapshot(editor, "prompt-editor-advanced.png");
            Click(editor, "AiPromptSave"); editor = null;
            var reloaded = new AiPreferences(directory).Settings;
            check(reloaded.Profiles.SequenceEqual(before.Profiles) && reloaded.SelectedProfileId == before.SelectedProfileId
                && reloaded.Prompts.Single(p => p.Id == existing.Id) is { Category: "翻譯", Keywords: "日文 japanese", Icon: "translate" },
                "AI prompt saving retains configured provider profiles and selected service");

            editor = new(existing, _ => "儲存失敗測試"); editor.Show(); await Layout(editor); Click(editor, "AiPromptSave");
            check(editor.IsVisible && Find<TextBox>(editor, "AiPromptInstruction").Text == existing.Instruction && Find<TextBlock>(editor, "AiPromptError").Text == "儲存失敗測試",
                "AI prompt save failure keeps the editor and exact draft"); editor.Close(); editor = null;
            calls = 0; editor = new(null, _ => { calls++; return null; }); editor.Show(); await Layout(editor); Click(editor, "AiPromptCancel");
            check(calls == 0 && !editor.IsVisible, "AI prompt cancellation never calls persistence"); editor = null;
        }
        finally { editor?.Close(); }
    }
}
