using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapBar.App;
using SnapBar.Core;

internal static class UiAppearanceTest
{
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
            foreach (var descendant in Children<T>(child)) yield return descendant;
    }

    internal static void Snapshot(Window window, string file)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        var width = content.ActualWidth; var height = content.ActualHeight;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new(0, 0, width, height));
        }
        bitmap.Render(visual); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/ui-polish");
        using var stream = File.Create(Path.Combine("artifacts/ui-polish", file)); encoder.Save(stream);
    }

    public static int Run(bool preview = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var passed = 0; var exit = 1;
        void Check(bool ok, string name)
        { if (!ok) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS " + name); }
        app.Startup += async (_, _) =>
        {
            MainWindow? main = null; ActionSettingsWindow? actions = null; ActionEditorWindow? editor = null; PopupWindow? popup = null;
            var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Appearance." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                main = new MainWindow(new SettingsStore(Path.Combine(directory, "settings.json"))); main.Show(); main.UpdateLayout();
                Check(!main.AutomaticDetectionEnabled && Children<Expander>(main).Where(e => e.IsVisible).All(e => !e.IsExpanded), "settings opens with diagnostics collapsed and automatic detection opt-in intact");
                Check(main.ClipboardCompatibilityEnabled && Children<CheckBox>(main).Single(c => AutomationProperties.GetAutomationId(c) == "ClipboardCompatibilityToggle").IsChecked == true,
                    "compatibility is enabled by default and separated from optional Sublime setup");
                Check(!Children<TextBlock>(main).Single(t => AutomationProperties.GetAutomationId(t) == "LatestDiagnosticStatus").IsVisible,
                    "routine capture status stays inside collapsed diagnostics");
                main.SetSearchUrlTemplate("file:///invalid"); main.UpdateLayout();
                var notice = Children<TextBlock>(main).Single(t => AutomationProperties.GetAutomationId(t) == "SettingsNotice");
                Check(notice.IsVisible && notice.Text.Contains("無效"), "invalid settings remain visible without opening diagnostics");
                notice.Visibility = Visibility.Collapsed;
                Snapshot(main, "settings.png");
                var searchPanel = Children<SearchSettingsPanel>(main).Single(); searchPanel.Reveal(); main.UpdateLayout();
                var provider = Children<ComboBox>(searchPanel).Single(c => AutomationProperties.GetAutomationId(c) == "SearchProvider");
                var template = Children<TextBox>(searchPanel).Single(t => AutomationProperties.GetAutomationId(t) == "SearchUrlTemplate");
                Check(provider.SelectedIndex == 0 && !template.IsVisible, "default search shows provider choice and hides custom URL details");
                Snapshot(main, "search-provider.png"); provider.SelectedIndex = 1;
                Check(!File.Exists(Path.Combine(directory, "settings.json")), "changing search provider waits for explicit save");
                Children<Button>(searchPanel).Single(b => AutomationProperties.GetAutomationId(b) == "SaveSearchTemplate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(new SettingsStore(Path.Combine(directory, "settings.json")).Load().Settings.SearchUrlTemplate == SearchSettingsPanel.BingTemplate,
                    "Bing choice saves the corresponding valid query template");
                provider.SelectedIndex = 2; template.Text = "https://example.com/search?q={query}"; provider.SelectedIndex = 0; provider.SelectedIndex = 2;
                Check(template.Text == "https://example.com/search?q={query}", "switching provider preserves unsaved custom URL draft");
                Snapshot(main, "search-custom.png");
                Children<Button>(searchPanel).Single(b => AutomationProperties.GetAutomationId(b) == "SaveSearchTemplate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(new SettingsStore(Path.Combine(directory, "settings.json")).Load().Settings.SearchUrlTemplate == template.Text, "custom query template saves without normalization or replacement");
                Children<Button>(searchPanel).Single(b => AutomationProperties.GetAutomationId(b) == "ResetSearchTemplate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(provider.SelectedIndex == 0 && !template.IsVisible, "reset returns to Google provider and hides custom fields");
                notice.Visibility = Visibility.Collapsed;
                await SettingsNavigationTest.RunAsync(main, directory, Check);
                main.NavigateTo(SettingsCategory.General);
                main.Width = main.MinWidth; main.Height = main.MinHeight; main.UpdateLayout(); Snapshot(main, "settings-small.png");
                Check(main.Content is not ScrollViewer && Children<ScrollViewer>(main).Where(s => s.IsVisible).All(s => s.ScrollableHeight < 1),
                    "compact general settings retain fixed navigation without vertical overflow");
                main.Width = 980; main.Height = 720; main.UpdateLayout();

                var script = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "清理多餘空白", CustomActionKind.PowerShell, "terminal", "($env:POPCLIP_TEXT -replace '\\s+', ' ').Trim()");
                var counter = script with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "統計字數", Content = "$env:POPCLIP_TEXT.Length" };
                var settings = ActionCatalog.Normalize(new() { CustomActions = [script, counter] });
                settings = ActionCatalog.SetPlacement(ActionCatalog.SetPlacement(settings, "link", ActionPlacement.Bar), "path", ActionPlacement.Bar);
                var saveCalls = 0;
                actions = new ActionSettingsWindow(settings, _ => { saveCalls++; return true; }); actions.Show(); actions.UpdateLayout(); Snapshot(actions, "actions.png");
                void SelectPlacement(ActionPlacement placement)
                { Children<RadioButton>(actions).Single(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_" + placement).IsChecked = true; actions.UpdateLayout(); }
                SelectPlacement(ActionPlacement.More);
                Check(saveCalls == 0 && Children<ComboBox>(actions).Any(c => AutomationProperties.GetName(c) == script.Name + "顯示位置")
                    && !Children<ComboBox>(actions).Any(c => AutomationProperties.GetName(c) == "複製顯示位置"), "More tab directly exposes custom actions without saving or showing pinned rows");
                Snapshot(actions, "actions-more.png");
                Check(Children<RadioButton>(actions).Single(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_Hidden").Visibility == Visibility.Collapsed,
                    "empty hidden tab is omitted");
                Children<ComboBox>(actions).Single(c => AutomationProperties.GetName(c) == script.Name + "顯示位置").SelectedIndex = (int)ActionPlacement.Hidden;
                SelectPlacement(ActionPlacement.Hidden);
                Check(Children<RadioButton>(actions).Any(t => AutomationProperties.GetAutomationId(t) == "ActionPlacementTab_Hidden" && t.IsVisible && ((string)t.Content).Contains("1")),
                    "hidden tab appears with an accurate count when an action is placed there");
                Children<ComboBox>(actions).Single(c => AutomationProperties.GetName(c) == script.Name + "顯示位置").SelectedIndex = (int)ActionPlacement.More;
                SelectPlacement(ActionPlacement.Bar);
                Check(!Children<TextBlock>(actions).Any(t => t.Text == "內建動作" || t.Text == "動作列 · 6")
                    && Children<Border>(actions).Count(b => AutomationProperties.GetAutomationId(b).StartsWith("ReorderAction_")) == 6,
                    "pinned list exposes six drag grips without duplicate headings or builtin subtitles");
                var theme = Children<ComboBox>(actions).Single(c => AutomationProperties.GetName(c) == "動作列主題");
                theme.SelectedIndex = 1; actions.UpdateLayout(); Snapshot(actions, "actions-light.png");
                actions.Width = actions.MinWidth; actions.Height = 620; actions.UpdateLayout(); Snapshot(actions, "actions-small.png");
                Check(Children<ComboBox>(actions).Where(c => AutomationProperties.GetName(c).EndsWith("顯示位置")).All(c => c.ActualWidth >= 100), "compact action rows retain usable placement controls");
                actions.Width = 900; actions.Height = 820; actions.UpdateLayout();

                editor = new ActionEditorWindow(script); editor.Show(); editor.UpdateLayout();
                var chooseIcon = Children<Button>(editor).Single(b => AutomationProperties.GetAutomationId(b) == "CustomActionChooseIcon");
                Check(chooseIcon.Focusable && chooseIcon.Focus() && chooseIcon.IsKeyboardFocused
                    && Children<AiIconPresenter>(editor).Any(p => AutomationProperties.GetAutomationId(p) == "CustomActionIconPreview"),
                    "custom icon button exposes a keyboard focusable preview entry");
                var test = Children<Button>(editor).Single(b => AutomationProperties.GetAutomationId(b) == "TestCustomAction");
                var stop = Children<Button>(editor).Single(b => AutomationProperties.GetAutomationId(b) == "StopCustomActionTest");
                Check(AutomationProperties.GetName(test) == "執行測試" && stop.Visibility == Visibility.Collapsed, "script editor names real execution and hides idle stop control");
                var output = Children<ComboBox>(editor).Single(c => AutomationProperties.GetName(c) == "結果處理方式");
                var outputHelp = Children<TextBlock>(editor).Single(t => AutomationProperties.GetAutomationId(t) == "ActionOutputHelp");
                var descriptions = new[] { "不顯示", "複製到剪貼簿", "重新驗證原選取", "短暫顯示", "文字結果視窗", "Markdown" };
                for (var i = 0; i < descriptions.Length; i++)
                { output.SelectedIndex = i; Check(outputHelp.Text.Contains(descriptions[i]), "output help follows mode " + i); }
                output.SelectedIndex = (int)ActionOutput.Window;
                var advanced = Children<Expander>(editor).Single(e => AutomationProperties.GetAutomationId(e) == "AdvancedActionSettings");
                var runtime = Children<TextBox>(editor).Single(t => AutomationProperties.GetName(t) == "執行環境完整路徑");
                var formScroll = Children<ScrollViewer>(editor).Single(s => s.Content is StackPanel panel && Children<TextBox>(panel).Contains(runtime));
                editor.UpdateLayout();
                var advancedBounds = advanced.TransformToAncestor(formScroll).TransformBounds(new Rect(advanced.RenderSize));
                Check(formScroll.VerticalOffset == 0 && advancedBounds.Bottom <= formScroll.ViewportHeight + 1,
                    "default editor exposes basic settings and advanced entry without scrolling");
                Snapshot(editor, "editor.png");
                var code = Children<TextBox>(editor).Single(t => AutomationProperties.GetAutomationId(t) == "CustomActionContent");
                var resize = Children<Thumb>(editor).Single(t => AutomationProperties.GetAutomationId(t) == "ResizeActionContent");
                var originalCode = code.Text; code.Select(5, 3);
                resize.RaiseEvent(new DragDeltaEventArgs(0, 1000) { RoutedEvent = Thumb.DragDeltaEvent }); editor.UpdateLayout();
                Check(code.ActualHeight == 480 && code.Text == originalCode && code.SelectedText == originalCode.Substring(5, 3),
                    "expanding editor clamps height while preserving script and selection");
                resize.RaiseEvent(new DragDeltaEventArgs(0, -1000) { RoutedEvent = Thumb.DragDeltaEvent }); editor.UpdateLayout();
                Check(code.ActualHeight == 96, "shrinking editor retains a usable minimum content area");
                void ResizeKey(Key key) => resize.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
                ResizeKey(Key.Down); editor.UpdateLayout(); Check(code.ActualHeight == 112, "resize grip supports keyboard height adjustment");
                ResizeKey(Key.Home); editor.UpdateLayout(); Check(code.ActualHeight == 120, "Home restores compact editor height");
                formScroll.ScrollToTop();
                editor.Width = 1024; editor.Height = 800; editor.UpdateLayout();
                advancedBounds = advanced.TransformToAncestor(formScroll).TransformBounds(new Rect(advanced.RenderSize));
                Check(advancedBounds.Bottom <= formScroll.ViewportHeight + 1, "basic editor settings remain visible at 1024 by 800");
                Snapshot(editor, "editor-compact.png");
                editor.Width = editor.MinWidth; editor.Height = editor.MinHeight; editor.UpdateLayout(); Snapshot(editor, "editor-small.png");
                var save = Children<Button>(editor).Single(b => AutomationProperties.GetName(b) == "儲存動作");
                var point = save.TransformToAncestor((FrameworkElement)editor.Content).Transform(new Point(0, 0));
                Check(point.Y + save.ActualHeight <= ((FrameworkElement)editor.Content).ActualHeight + 1, "save remains visible at minimum editor size");
                advanced.IsExpanded = true;
                await editor.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                editor.UpdateLayout();
                var runtimeBounds = runtime.TransformToAncestor(formScroll).TransformBounds(new Rect(runtime.RenderSize));
                Check(formScroll.VerticalOffset > 0 && runtimeBounds.Top >= -1 && runtimeBounds.Bottom <= formScroll.ActualHeight + 1,
                    "expanding advanced settings scrolls first input fully into the minimum-size viewport");
                Snapshot(editor, "editor-advanced-small.png"); advanced.IsExpanded = false; formScroll.ScrollToTop();
                editor.Width = 1080; editor.Height = 850;
                code.Text = "Start-Sleep -Seconds 30"; test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!test.IsEnabled && stop.Visibility == Visibility.Visible, "running test exposes stop and disables duplicate execution");
                stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!test.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(20);
                Check(test.IsEnabled && stop.Visibility == Visibility.Collapsed, "cancelled test returns to idle controls");
                var sample = Children<TextBox>(editor).Single(t => AutomationProperties.GetAutomationId(t) == "CustomActionSample");
                var result = Children<TextBox>(editor).Single(t => AutomationProperties.GetName(t) == "執行結果或網址預覽");
                var comparison = Children<ActionTestComparisonView>(editor).Single();
                async Task RunTest()
                {
                    test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var limit = DateTime.UtcNow.AddSeconds(10);
                    while (!test.IsEnabled && DateTime.UtcNow < limit) await Task.Delay(20);
                    if (!test.IsEnabled) throw new InvalidOperationException("comparison test did not complete");
                    editor.UpdateLayout();
                }
                sample.Text = "  中文\t😀\r\n字  "; code.Text = "[Console]::Write(($env:POPCLIP_TEXT -replace '\\s+', ' ').Trim())";
                await RunTest();
                Check(result.Text == "中文 😀 字" && sample.Text == "  中文\t😀\r\n字  ", "comparison preserves raw Unicode input and output");
                Check(comparison.Visibility == Visibility.Visible && Children<TextBlock>(comparison).Any(t => t.Text.Contains("內容已變更")),
                    "successful result reports an actual input/output change");
                Check(Children<TextBox>(comparison).Single(t => AutomationProperties.GetAutomationId(t) == "TestInputWhitespace").Text == "··中文⇥😀␍↵\n字··",
                    "whitespace representation distinguishes spaces tabs CRLF without altering source");
                var symbols = Children<CheckBox>(editor).Single(c => AutomationProperties.GetAutomationId(c) == "ShowResultWhitespace");
                var compareExpander = Children<Expander>(comparison).Single();
                Check(symbols.IsVisible && symbols.IsChecked != true && !compareExpander.IsExpanded
                    && !Children<TextBlock>(comparison).Single(t => AutomationProperties.GetAutomationId(t) == "TestComparisonSummary").IsVisible,
                    "result opens raw with compact whitespace toggle and comparison summary inside collapsed details");
                symbols.IsChecked = true; editor.UpdateLayout();
                Check(result.Text == "中文·😀·字" && sample.Text == "  中文\t😀\r\n字  " && test.IsEnabled
                    && result.TextWrapping == TextWrapping.NoWrap && result.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto,
                    "whitespace switch changes only the result display without rerunning or changing input");
                symbols.IsChecked = false;
                Check(result.Text == "中文 😀 字", "disabling whitespace restores the exact original result");
                Snapshot(editor, "editor-result-compact.png");
                Children<Expander>(comparison).Single().IsExpanded = true; Snapshot(editor, "editor-comparison.png");
                code.Text = "[Console]::Write($env:POPCLIP_TEXT)";
                Check(comparison.Visibility == Visibility.Collapsed && Children<TextBox>(comparison).All(t => t.Text.Length == 0)
                    && symbols.Visibility == Visibility.Collapsed && symbols.IsChecked != true, "editing clears comparison and resets result whitespace display");
                await RunTest();
                Check(result.Text == sample.Text && Children<TextBlock>(comparison).Any(t => t.Text.Contains("內容相同")), "unchanged result is labeled without invented changes");
                symbols.IsChecked = true; editor.UpdateLayout();
                Check(result.Text == "··中文⇥😀␍↵\n字··", "result whitespace view preserves visible CRLF and Unicode");
                symbols.IsChecked = false;
                Check(result.Text == sample.Text, "raw result restoration preserves exact CRLF and tabs");
                Check(ActionTestComparisonView.VisibleWhitespace("a\rb\nc\r\nd") == "a␍\nb↵\nc␍↵\nd",
                    "CR LF and CRLF remain distinct while preserving actual line boundaries");
                sample.Text = new string('x', 160) + " wonderful"; await RunTest();
                compareExpander.IsExpanded = true; symbols.IsChecked = true; editor.UpdateLayout();
                Check(Children<TextBox>(comparison).All(t => t.TextWrapping == TextWrapping.NoWrap
                    && t.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && t.LineCount == 1)
                    && result.LineCount == 1, "long comparison and symbol-mode lines never introduce artificial wrapping");
                Snapshot(editor, "editor-comparison-long-line.png");
                editor.Width = editor.MinWidth; editor.Height = editor.MinHeight; editor.UpdateLayout();
                Check(Children<TextBox>(comparison).All(t => t.LineCount == 1), "comparison retains original lines at minimum editor size");
                comparison.BringIntoView();
                await editor.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                Snapshot(editor, "editor-comparison-small.png");
                editor.Width = 1080; editor.Height = 850;
                code.Text = "[Console]::Write('')"; await RunTest();
                Check(result.Text == "" && Children<TextBlock>(comparison).Any(t => t.Text.Contains("沒有文字輸出")), "successful empty output is explicit and stays empty");
                code.Text = "throw 'preview failure'"; await RunTest();
                Check(symbols.Visibility == Visibility.Collapsed && comparison.Visibility == Visibility.Collapsed, "failed result hides whitespace controls and comparison");
                code.Text = "Start-Sleep -Milliseconds 300; [Console]::Write('obsolete')";
                test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); sample.Text = "new sample";
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (!test.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(20);
                Check(test.IsEnabled && comparison.Visibility == Visibility.Collapsed && result.Text != "obsolete"
                    && Children<TextBlock>(editor).Any(t => t.Text.Contains("請重新測試")), "editing during execution never presents old result as a new comparison");
                var inputOption = Children<CheckBox>(editor).Single(c => AutomationProperties.GetAutomationId(c) == "UseSelectionInput");
                inputOption.IsChecked = false; code.Text = "[Console]::Write('[' + $env:POPCLIP_TEXT + ']')"; output.SelectedIndex = (int)ActionOutput.Hud;
                await RunTest();
                Check(!sample.IsVisible && comparison.Visibility == Visibility.Collapsed && result.Text == "[]"
                    && !Children<Button>(editor).Single(b => AutomationProperties.GetName(b) == "插入選取文字").IsEnabled,
                    "disabled selection hides sample and comparison and executes with empty input");
                symbols.IsChecked = true;
                Check(symbols.IsVisible && result.Text == "[]" && comparison.Visibility == Visibility.Collapsed, "selection-independent output still supports whitespace inspection without input comparison");
                Check(outputHelp.Text.Contains("短暫顯示") && !outputHelp.Text.Contains("取代"), "HUD description never implies replacement");
                Snapshot(editor, "editor-no-selection.png"); inputOption.IsChecked = true; editor.UpdateLayout();
                Check(sample.IsVisible && sample.Text == "new sample", "reenabling selection restores the sample draft");
                editor.Close(); editor = new ActionEditorWindow(); editor.Show(); editor.UpdateLayout(); Snapshot(editor, "editor-url.png");
                Check(Children<CheckBox>(editor).Single(c => AutomationProperties.GetAutomationId(c) == "ShowResultWhitespace").Visibility == Visibility.Collapsed,
                    "URL preview does not expose script-result whitespace control");
                Check(Children<Button>(editor).Any(b => AutomationProperties.GetName(b) == "預覽網址"), "URL editor labels preview without suggesting execution");
                var kind = Children<ComboBox>(editor).Single(c => AutomationProperties.GetAutomationId(c) == "CustomActionKind");
                AiIconPresenter IconPreview(Window window) => Children<AiIconPresenter>(window).Single(p => AutomationProperties.GetAutomationId(p) == "CustomActionIconPreview");
                void OpenPicker(Action<AiIconPickerWindow> operate)
                {
                    editor.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                    {
                        var picker = editor.OwnedWindows.Cast<Window>().OfType<AiIconPickerWindow>().Single();
                        Check(picker.Owner == editor && System.Windows.Interop.ComponentDispatcher.IsThreadModal, "custom icon picker is owned and modal");
                        Snapshot(picker, "editor-custom-icon-picker.png");
                        operate(picker);
                        Check(!picker.IsVisible, "custom icon picker closes after selection or dismissal");
                    });
                    Children<Button>(editor).Single(b => AutomationProperties.GetAutomationId(b) == "CustomActionChooseIcon").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(Children<Button>(editor).Single(b => AutomationProperties.GetAutomationId(b) == "CustomActionChooseIcon").IsKeyboardFocused,
                        "custom icon picker restores keyboard focus to its entry button");
                }
                Check(IconPreview(editor).IconId == "globe", "new URL action defaults to globe icon");
                kind.SelectedIndex = 1;
                Check(IconPreview(editor).IconId == "terminal", "new script defaults to terminal icon");
                kind.SelectedIndex = 2; Check(IconPreview(editor).IconId == "terminal", "new JavaScript action defaults to terminal icon");
                kind.SelectedIndex = 0; Check(IconPreview(editor).IconId == "globe", "unselected icon follows URL default again");
                var draftName = Children<TextBox>(editor).Single(t => AutomationProperties.GetAutomationId(t) == "CustomActionName");
                var draftContent = Children<TextBox>(editor).Single(t => AutomationProperties.GetAutomationId(t) == "CustomActionContent");
                var draftSample = Children<TextBox>(editor).Single(t => AutomationProperties.GetAutomationId(t) == "CustomActionSample");
                draftName.Text = "Icon fixture"; draftContent.Text = "https://example.com/***"; draftSample.Text = "Disposable sample"; editor.UpdateLayout();
                var beforePosition = draftContent.TranslatePoint(new Point(), editor); var beforeSize = new Size(editor.ActualWidth, editor.ActualHeight);
                OpenPicker(p => p.ChooseIcon.Execute("translate_20_regular")); editor.UpdateLayout();
                Check(IconPreview(editor).IconId == "translate_20_regular" && editor.SavedAction == null,
                    "custom picker updates the preview without saving");
                Check(draftName.Text == "Icon fixture" && draftContent.Text == "https://example.com/***" && draftSample.Text == "Disposable sample"
                    && draftContent.TranslatePoint(new Point(), editor) == beforePosition && new Size(editor.ActualWidth, editor.ActualHeight) == beforeSize,
                    "custom picker preserves drafts editor size and content position");
                Snapshot(editor, "editor-custom-icon.png");
                editor.Width = editor.MinWidth; editor.Height = editor.MinHeight; editor.UpdateLayout();
                Snapshot(editor, "editor-custom-icon-small.png");
                foreach (var dismiss in new[] { "close", "cancel", "escape" })
                {
                    OpenPicker(p =>
                    {
                        if (dismiss == "close") p.Close();
                        else if (dismiss == "cancel") Children<Button>(p).Single(b => AutomationProperties.GetAutomationId(b) == "AiIconCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        else p.SearchInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(p), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                        Check(p.SelectedId == null, "custom picker " + dismiss + " returns no selection");
                    });
                    Check(IconPreview(editor).IconId == "translate_20_regular" && editor.SavedAction == null, "custom picker " + dismiss + " preserves unsaved icon");
                }
                foreach (var index in new[] { 1, 2, 0 }) { kind.SelectedIndex = index; Check(IconPreview(editor).IconId == "translate_20_regular", "chosen icon survives action type " + index); }
                Check(draftName.Text == "Icon fixture" && draftContent.Text == "https://example.com/***" && draftSample.Text == "Disposable sample", "type switching preserves URL drafts after icon selection");
                editor.Close(); Check(editor.SavedAction == null, "closing editor discards selected icon without saving");
                foreach (var storedIcon in new[] { "globe", "unknown-valid-icon" })
                {
                    editor = new ActionEditorWindow(script with { Icon = storedIcon }); editor.Show(); editor.UpdateLayout();
                    var storedKind = Children<ComboBox>(editor).Single(c => AutomationProperties.GetAutomationId(c) == "CustomActionKind");
                    storedKind.SelectedIndex = 2; storedKind.SelectedIndex = 0;
                    Check(IconPreview(editor).IconId == storedIcon, "editing preserves stored icon " + storedIcon);
                    editor.Close();
                }
                editor = new ActionEditorWindow(script); editor.Show(); editor.UpdateLayout();
                var selectedDialog = new ActionEditorWindow(script) { Owner = editor };
                selectedDialog.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                {
                    selectedDialog.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                        selectedDialog.OwnedWindows.Cast<Window>().OfType<AiIconPickerWindow>().Single().ChooseIcon.Execute("translate_20_regular"));
                    Children<Button>(selectedDialog).Single(b => AutomationProperties.GetAutomationId(b) == "CustomActionChooseIcon").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Children<Button>(selectedDialog).Single(b => AutomationProperties.GetName(b) == "儲存動作").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                Check(selectedDialog.ShowDialog() == true && selectedDialog.SavedAction?.Icon == "translate_20_regular", "explicit Save returns the selected custom icon");
                var fixtureStore = new SettingsStore(Path.Combine(directory, "icon-persistence", "settings.json"));
                var fixtureSettings = fixtureStore.Load().Settings;
                var otherAction = script with { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "Untouched fixture", Icon = "legacy:globe" };
                fixtureStore.Save(fixtureSettings with { Actions = fixtureSettings.Actions with { CustomActions = [script, otherAction] } });
                var aiDirectory = Path.Combine(directory, "icon-persistence");
                var aiPreferences = new AiPreferences(aiDirectory);
                Check(aiPreferences.TrySave(aiPreferences.Settings, out _), "custom icon persistence creates disposable AI fixture");
                var aiBytes = File.ReadAllBytes(Path.Combine(aiDirectory, "ai.json"));
                var beforeCancel = File.ReadAllBytes(fixtureStore.FilePath);
                var cancelled = new ActionEditorWindow(script) { Owner = editor };
                cancelled.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                {
                    cancelled.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                        cancelled.OwnedWindows.Cast<Window>().OfType<AiIconPickerWindow>().Single().ChooseIcon.Execute("translate_20_regular"));
                    Children<Button>(cancelled).Single(b => AutomationProperties.GetAutomationId(b) == "CustomActionChooseIcon").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    cancelled.Close();
                });
                Check(cancelled.ShowDialog() != true && cancelled.SavedAction == null
                    && beforeCancel.SequenceEqual(File.ReadAllBytes(fixtureStore.FilePath)), "cancelled custom icon draft leaves persisted actions unchanged");
                var savedSettings = fixtureStore.Load().Settings;
                fixtureStore.Save(savedSettings with { Actions = savedSettings.Actions with { CustomActions = [selectedDialog.SavedAction!, otherAction] } });
                var reloadedActions = new SettingsStore(fixtureStore.FilePath).Load();
                var persistedAction = reloadedActions.Settings.Actions.CustomActions.Single(a => a.Id == script.Id);
                Check(reloadedActions.Warning == null && persistedAction.Icon == "translate_20_regular"
                    && CustomActionPolicy.Import(CustomActionPolicy.Export(persistedAction)).Icon == persistedAction.Icon
                    && reloadedActions.Settings.Actions.CustomActions.Single(a => a.Id == otherAction.Id) == otherAction
                    && aiBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(aiDirectory, "ai.json"))),
                    "saved custom icon survives production settings reload and export import without changing other actions or AI data");
                var savedDialog = new ActionEditorWindow(script with { UseSelection = false }) { Owner = editor };
                savedDialog.Loaded += (_, _) => Children<Button>(savedDialog).Single(b => AutomationProperties.GetName(b) == "儲存動作").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(savedDialog.ShowDialog() == true && savedDialog.SavedAction?.UseSelection == false,
                    "saving an existing action preserves explicit disabled selection");
                var invalidSettingsPath = Path.Combine(directory, "invalid.json");
                File.WriteAllText(invalidSettingsPath, "{bad", new System.Text.UTF8Encoding(false));
                var invalidSettingsWindow = new MainWindow(new SettingsStore(invalidSettingsPath)); invalidSettingsWindow.Show();
                Check(!invalidSettingsWindow.ClipboardCompatibilityEnabled
                    && Children<CheckBox>(invalidSettingsWindow).Single(c => AutomationProperties.GetAutomationId(c) == "ClipboardCompatibilityToggle").IsChecked == false,
                    "invalid settings disable compatibility instead of silently restoring an enabled default");
                invalidSettingsWindow.PrepareSessionEnd(); invalidSettingsWindow.Close();

                popup = new PopupWindow();
                var selection = new SelectionSnapshot("appearance", new(0, 0, 0), "Disposable fixture text", new(100, 100, 100, 20), true, 1, 23);
                foreach (var name in new[] { "Dark", "Light" })
                {
                    popup.Present(selection, 100, 100, actionSettings: settings with { Theme = name }); popup.UpdateLayout(); Snapshot(popup, "toolbar-" + name.ToLowerInvariant() + ".png");
                    Children<Button>(popup).Single(b => AutomationProperties.GetAutomationId(b) == "MoreActions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    popup.UpdateLayout(); Snapshot(popup, "more-" + name.ToLowerInvariant() + ".png");
                }
                popup.Close(); popup = null; exit = 0;
                if (preview)
                {
                    Console.WriteLine("PREVIEW: disposable settings and actions; close editor to finish.");
                    var closed = new TaskCompletionSource(); editor.Closed += (_, _) => closed.TrySetResult();
                    await closed.Task;
                }
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error); }
            finally
            {
                popup?.Close(); editor?.Close(); actions?.Close();
                main?.PrepareSessionEnd(); main?.Close();
                var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var cleanupPath = Path.GetFullPath(directory);
                if (Directory.Exists(cleanupPath) && cleanupPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(cleanupPath).StartsWith("SnapBar.Appearance.", StringComparison.Ordinal)
                    && new DirectoryInfo(cleanupPath).LinkTarget == null) Directory.Delete(cleanupPath, true);
                Console.WriteLine($"{passed} passed, {(exit == 0 ? 0 : 1)} failed"); app.Shutdown();
            }
        };
        app.Run(); return exit;
    }
}
