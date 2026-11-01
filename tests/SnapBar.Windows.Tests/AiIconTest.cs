using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.App;
using SnapBar.Core;

internal static class AiIconTest
{
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        var seen = new HashSet<DependencyObject>(); return Walk(item);
        IEnumerable<T> Walk(DependencyObject node)
        {
            if (!seen.Add(node)) yield break;
            if (node is T match) yield return match;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) foreach (var found in Walk(child)) yield return found;
            if (node is Visual) for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var found in Walk(VisualTreeHelper.GetChild(node, i))) yield return found;
        }
    }
    private static T Find<T>(Window window, string id) where T : DependencyObject => Children<T>(window).Single(i => AutomationProperties.GetAutomationId(i) == id);
    private static void Click(Window window, string id) => Find<Button>(window, id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    private static async Task Layout(Window window) { window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    private static void Press(Window window, UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    public static async Task RunAsync(string directory, Action<bool, string> check, Action<Window, string> snapshot)
    {
        var managedBefore = GC.GetTotalMemory(true); var timer = Stopwatch.StartNew(); var catalog = AiIconCatalog.Browse; timer.Stop();
        var managedCatalog = GC.GetTotalMemory(true) - managedBefore;
        Console.WriteLine($"ICON METRIC catalog-first-use-ms={timer.Elapsed.TotalMilliseconds:F2}; variants={AiIconCatalog.VariantCount}; shapes-and-legacy={catalog.Count}; managed-catalog-delta-bytes={managedCatalog}");
        check(AiIconCatalog.VariantCount > 10000 && catalog.Count > 2000 && catalog.Select(i => i.Id).Distinct().Count() == catalog.Count && catalog.Count < AiIconCatalog.VariantCount,
            "AI Fluent catalog bundles full Regular names and deduplicates sizes for browsing");
        check(AiIconCatalog.TryResolve("  IC_FLUENT_TRANSLATE_20_REGULAR.SVG ", out var translate) && translate.Id == "translate_20_regular"
            && AiIconCatalog.TryResolve("translate", out var bare) && bare == translate,
            "AI Fluent name entry accepts official names filenames prefixes and bare aliases");
        check(AiIconCatalog.TryResolve("translate_24_regular", out var larger) && larger.Codepoint != translate.Codepoint
            && !AiIconCatalog.TryResolve("translate_20_filled", out _) && !AiIconCatalog.TryResolve("unknown-icon-fixture", out _),
            "AI Fluent explicit sizes resolve while unavailable styles and unknown names are rejected");
        var glyphs = new GlyphTypeface(new Uri(AiIconCatalog.FontResource));
        using var mapStream = Application.GetResourceStream(new Uri("pack://application:,,,/SnapBar.App;component/Assets/FluentIcons/catalog.json"))!.Stream;
        var rawMap = JsonSerializer.Deserialize<Dictionary<string, int>>(mapStream)!;
        check(rawMap.Values.All(point => glyphs.CharacterToGlyphMap.TryGetValue(point, out var value) && value != 0),
            "AI every named Fluent size variant exists in the bundled font without system installation");
        check(new Typeface(AiIconPresenter.FluentFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var resolvedFont)
            && resolvedFont.FontUri.AbsoluteUri.Contains("SnapBar.App", StringComparison.OrdinalIgnoreCase),
            "AI icon presenter resolves the bundled font rather than a system fallback");
        var supplementary = catalog.First(i => i.Codepoint > 0xffff); var presenter = new AiIconPresenter { IconId = supplementary.Id };
        check(presenter.Child is TextBlock text && text.Text.Length == 2 && char.ConvertToUtf32(text.Text, 0) == supplementary.Codepoint,
            "AI supplementary-plane Fluent glyphs preserve their full Unicode codepoint");
        check(AiIconCatalog.Search("翻譯", "程式").Any(i => i.Id == translate.Id) && AiIconCatalog.Search("摘要", "全部圖示").Any(i => i.Id == "text_bullet_list_20_regular")
            && AiIconCatalog.Search("", "程式").All(i => i.Category == "程式"),
            "AI Fluent Chinese search crosses categories while browsing filters remain effective");
        var defaults = new AiSettings().Prompts;
        check(defaults.Select(AiIconCatalog.ForPrompt).Distinct().Count() == 5 && defaults.All(p => AiIconCatalog.TryResolve(AiIconCatalog.ForPrompt(p), out _)),
            "AI five untouched factory commands receive distinct available Fluent icons");
        var improve = defaults.Single(p => p.Id == "improve");
        check(AiIconCatalog.ForPrompt(improve with { Icon = "legacy:spark" }) == "legacy:spark"
            && AiIconCatalog.ForPrompt(improve with { Instruction = "User custom instruction" }) == "spark"
            && AiIconCatalog.ForPrompt(improve with { Name = "User name" }) == "spark"
            && AiIconCatalog.Stored("translate")?.Id == "legacy:translate" && AiIconCatalog.Stored("legacy-icon") == null,
            "AI Fluent defaults preserve changed factory commands explicit legacy choices and unknown IDs");
        timer.Restart(); for (var i = 0; i < 25; i++) _ = AiIconCatalog.Search(i % 2 == 0 ? "翻譯" : "document", "全部圖示"); timer.Stop();
        Console.WriteLine($"ICON METRIC warm-search-average-ms={timer.Elapsed.TotalMilliseconds / 25:F2}");

        AiIconPickerWindow? picker = null; AiPromptEditorWindow? editor = null;
        try
        {
            timer.Restart(); picker = new(translate.Id); picker.Show(); picker.Activate(); await Layout(picker); timer.Stop();
            Console.WriteLine($"ICON METRIC picker-first-layout-ms={timer.Elapsed.TotalMilliseconds:F2}"); snapshot(picker, "icon-picker-common.png");
            Find<ComboBox>(picker, "AiIconCategory").SelectedItem = "全部圖示"; await Layout(picker);
            var realized = Enumerable.Range(0, picker.IconGrid.Items.Count).Count(n => picker.IconGrid.ItemContainerGenerator.ContainerFromIndex(n) != null);
            Console.WriteLine($"ICON METRIC rows={picker.IconGrid.Items.Count}; realized-rows={realized}; visible-presenters={Children<AiIconPresenter>(picker).Count(p => p.IsVisible)}");
            check(picker.IconGrid.Items.Count > 300 && realized > 0 && realized < 30 && Children<AiIconPresenter>(picker).Count(p => p.IsVisible) < 180,
                "AI full icon grid virtualizes rows and avoids constructing the complete catalog UI"); snapshot(picker, "icon-picker-all.png");
            picker.SearchInput.Focus(); Press(picker, picker.SearchInput, Key.Down);
            var first = Keyboard.FocusedElement as Button;
            check(first?.Tag is string, "AI icon picker Down enters the first visible icon");
            Press(picker, first!, Key.Right); var second = Keyboard.FocusedElement as Button;
            check(second?.Tag is string && second != first, "AI icon picker Right moves to another icon in the row");
            Press(picker, second!, Key.Down); check(Keyboard.FocusedElement is Button lower && lower != second,
                "AI icon picker Down navigates by visual grid row");
            for (var i = 0; i < 12; i++) Press(picker, (UIElement)Keyboard.FocusedElement, Key.Down);
            await Layout(picker);
            check(Keyboard.FocusedElement is Button { Tag: string } && Children<ScrollViewer>(picker.IconGrid).First().VerticalOffset > 0,
                "AI icon picker keyboard navigation realizes and focuses rows beyond the initial viewport");
            picker.SearchInput.Text = "翻譯"; await Layout(picker); snapshot(picker, "icon-picker-search.png");
            check(Children<Button>(picker).Any(b => (string?)b.Tag == translate.Id && b.Command?.CanExecute(b.CommandParameter) == true),
                "AI icon picker Chinese results expose working selection commands");
            picker.Width = 580; picker.Height = 440; await Layout(picker); var content = (FrameworkElement)VisualTreeHelper.GetChild(picker, 0);
            var cancel = Find<Button>(picker, "AiIconCancel"); var bottom = cancel.TransformToAncestor(content).TransformBounds(new Rect(cancel.RenderSize));
            check(bottom.Bottom <= content.ActualHeight + 1 && picker.IconGrid.ActualHeight > 100, "AI small icon picker retains its grid and cancel action"); snapshot(picker, "icon-picker-small.png");
            picker.SearchInput.Text = "ic_fluent_translate_20_regular"; picker.SearchInput.Focus();
            var composition = new TextComposition(InputManager.Current, picker.SearchInput, "測");
            picker.SearchInput.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
            Press(picker, picker.SearchInput, Key.Enter);
            check(picker.IsVisible && picker.SelectedId == null, "AI icon picker IME confirmation does not apply a symbol");
            picker.SearchInput.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
            Press(picker, picker.SearchInput, Key.Enter);
            check(!picker.IsVisible && picker.SelectedId == translate.Id, "AI icon picker Enter applies an exact pasted official name"); picker = null;
            picker = new(translate.Id); picker.Show(); await Layout(picker); Press(picker, picker.SearchInput, Key.Escape);
            check(picker.SelectedId == null && !picker.IsVisible, "AI icon picker cancellation returns no changed selection"); picker = null;

            AiPrompt? saved = null; var calls = 0; var existing = new AiPrompt("icon-fixture", "Icon fixture", "Disposable fixture instruction", "自訂", Icon: "legacy-icon");
            editor = new(existing, p => { saved = p; calls++; return null; }); editor.Show(); await Layout(editor);
            Find<Expander>(editor, "AiPromptAdvanced").IsExpanded = true; var name = Find<TextBox>(editor, "AiPromptIcon"); name.Text = "invalid-symbol-fixture";
            Click(editor, "AiPromptSave");
            check(calls == 0 && editor.IsVisible && Find<TextBlock>(editor, "AiPromptIconHint").Text.Contains("找不到"),
                "AI invalid icon name blocks saving and keeps the instruction draft");
            name.Text = "translate"; await Layout(editor);
            check(Find<AiIconPresenter>(editor, "AiPromptIconPreview").IconId == translate.Id && Find<TextBlock>(editor, "AiPromptError").Text.Length == 0,
                "AI valid icon name edits update the live preview and clear stale validation errors");
            editor.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                var owned = editor.OwnedWindows.Cast<Window>().OfType<AiIconPickerWindow>().Single();
                owned.ChooseIcon.Execute("text_grammar_checkmark_20_regular");
            });
            Click(editor, "AiPromptChooseIcon");
            check(name.Text == "text_grammar_checkmark_20_regular", "AI editor picker selection updates its icon field without changing the instruction");
            Find<TextBlock>(editor, "AiPromptIconHint").BringIntoView(); await Layout(editor); snapshot(editor, "prompt-editor-fluent.png"); Click(editor, "AiPromptSave"); editor = null;
            var prefs = new AiPreferences(Path.Combine(directory, "icon-persistence")); var before = prefs.Settings;
            check(saved?.Icon == "text_grammar_checkmark_20_regular" && calls == 1 && prefs.TrySave(before with { Prompts = before.Prompts.Append(saved).ToArray() }, out _)
                && new AiPreferences(Path.Combine(directory, "icon-persistence")).Settings.Prompts.Single(p => p.Id == existing.Id).Icon == saved.Icon
                && prefs.Settings.Profiles.SequenceEqual(before.Profiles), "AI selected Fluent names persist and reload without changing service profiles");
        }
        finally { picker?.Close(); editor?.Close(); }
    }
}
