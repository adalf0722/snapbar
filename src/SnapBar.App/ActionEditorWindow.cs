using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class ActionEditorWindow : Window
{
    private readonly UiLanguage language = UiText.Capture();
    private readonly string id;
    private readonly CustomActionDefinition? originalAction;
    private readonly string? initialDisplayName;
    private readonly TextBox name = ActionUi.Input(max: 32), content = ActionUi.Input(), sample = ActionUi.Input(),
        runtime = ActionUi.Input(), timeout = ActionUi.Input("10", 3), preview = ActionUi.Input();
    private readonly ComboBox kind = new(), output = new();
    private string selectedIcon = "globe";
    private readonly AiIconPresenter iconPreview = new() { Width = 22, Height = 22 };
    private Button iconButton = null!;
    private readonly StackPanel scriptOptions = new(), sampleSection = new();
    private readonly CheckBox useSelection = new() { Content = UiText.Get("ActionEditorWindow_000"), IsChecked = true, Margin = new(0, 0, 0, 14),
        ToolTip = UiText.Get("ActionEditorWindow_001") };
    private readonly ActionDiagnosticView diagnostics = new() { Visibility = Visibility.Collapsed };
    private readonly ActionTestComparisonView comparison = new();
    private readonly CheckBox showWhitespace = new()
    {
        Content = UiText.Get("ActionEditorWindow_002"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        Margin = new(12, 0, 0, 0), Visibility = Visibility.Collapsed,
        ToolTip = UiText.Get("ActionEditorWindow_003")
    };
    private string? rawTestOutput;
    private readonly TextBlock error = ActionUi.Text("", 12, Brushes.Firebrick), destination = ActionUi.Text("", 12, ActionUi.Muted),
        help = ActionUi.Text("", 11, ActionUi.Muted), testStatus = ActionUi.Text("", 11, ActionUi.Muted);
    private readonly Button test, stop, variable;
    private readonly TextBlock outputHelp = ActionUi.Text("", 12, ActionUi.Muted);
    private readonly TextBlock resultHeading = ActionUi.Heading(UiText.Get("ActionEditorWindow_004"), 14), contentHeading = ActionUi.Heading(UiText.Get("ActionEditorWindow_005"), 14);
    private readonly Dictionary<CustomActionKind, (string Name, string Content, string Sample, bool UseSelection)> drafts = new();
    private CustomActionKind currentKind;
    private CancellationTokenSource? testing;
    private bool ready, iconChosen;
    private int previewRevision;
    public CustomActionDefinition? SavedAction { get; private set; }
    private static string[] OutputNames => [UiText.Get("ActionEditorWindow_006"), UiText.Get("ActionEditorWindow_007"), UiText.Get("ActionEditorWindow_008"), UiText.Get("ActionEditorWindow_009"), UiText.Get("ActionEditorWindow_010"), UiText.Get("ActionEditorWindow_011")];

    public ActionEditorWindow(CustomActionDefinition? existing = null)
    {
        originalAction = existing;
        initialDisplayName = existing == null ? null : UiNames.Action(existing, language);
        ActionUi.Setup(this, existing == null ? language.Get("ActionEditorWindow_012") : language.Get("ActionEditorWindow_013"), 1080, 850);
        MinWidth = 880; MinHeight = 640; WindowWorkArea.Attach(this); id = existing?.Id ?? "custom-" + Guid.NewGuid().ToString("N");
        iconChosen = existing != null;
        selectedIcon = existing?.Icon ?? "globe"; iconPreview.IconId = selectedIcon;
        var outer = new DockPanel { Margin = new(28) };
        var heading = new StackPanel { Margin = new(0, 0, 0, 22) };
        heading.Children.Add(ActionUi.Heading(existing == null ? language.Get("ActionEditorWindow_014") : language.Get("ActionEditorWindow_015"), 24));
        var subtitle = ActionUi.Text(language.Get("ActionEditorWindow_016"), 13, ActionUi.Muted); subtitle.Margin = new(0, 5, 0, 0); heading.Children.Add(subtitle);
        DockPanel.SetDock(heading, Dock.Top); outer.Children.Add(heading);
        var footer = new DockPanel { Margin = new(0, 18, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ActionUi.Button(language.Get("ActionEditorWindow_017"), Close)); buttons.Children.Add(ActionUi.Button(language.Get("ActionEditorWindow_018"), Save, primary: true));
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        var footerText = new StackPanel { Margin = new(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        footerText.Children.Add(ActionUi.Text(language.Get("ActionEditorWindow_019"), 12, ActionUi.Muted)); footerText.Children.Add(error); footer.Children.Add(footerText);
        DockPanel.SetDock(footer, Dock.Bottom); outer.Children.Add(footer);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(1.1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(20) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var form = new StackPanel(); var formHeading = ActionUi.Heading(language.Get("ActionEditorWindow_020")); formHeading.Margin = new(0, 0, 0, 12); form.Children.Add(formHeading);
        kind.ItemsSource = new[] { language.Get("ActionEditorWindow_021"), "PowerShell", "JavaScript" }; kind.SelectedIndex = (int)(existing?.Kind ?? CustomActionKind.Url);
        AutomationProperties.SetName(kind, language.Get("ActionEditorWindow_022")); Field(form, language.Get("ActionEditorWindow_023"), kind);
        var identity = new Grid(); identity.ColumnDefinitions.Add(new() { Width = new(44) });
        identity.ColumnDefinitions.Add(new() { Width = new(12) }); identity.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        iconButton = ActionUi.Button("", ChooseIcon); iconButton.Content = iconPreview;
        iconButton.Width = iconButton.Height = 44; iconButton.Padding = new(0);
        iconButton.ToolTip = language.Get("ActionEditorWindow_024");
        AutomationProperties.SetName(iconButton, language.Get("ActionEditorWindow_025"));
        AutomationProperties.SetAutomationId(iconButton, "CustomActionChooseIcon");
        AutomationProperties.SetAutomationId(iconPreview, "CustomActionIconPreview");
        name.Height = 44;
        var iconField = new StackPanel(); Field(iconField, language.Get("ActionEditorWindow_026"), iconButton); identity.Children.Add(iconField);
        var nameField = new StackPanel(); Field(nameField, language.Get("ActionEditorWindow_027"), name); Grid.SetColumn(nameField, 2); identity.Children.Add(nameField); form.Children.Add(identity);
        iconButton.Margin = name.Margin = new(0, 0, 0, 3);
        AutomationProperties.SetName(name, language.Get("ActionEditorWindow_028"));
        content.AcceptsReturn = true; content.AcceptsTab = true; content.Height = 120; content.TextWrapping = TextWrapping.Wrap;
        content.VerticalContentAlignment = VerticalAlignment.Top;
        content.FontFamily = new("Consolas, Microsoft JhengHei UI"); content.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var inputHeader = new DockPanel { Margin = new(0, 0, 0, 8) }; variable = ActionUi.Button(language.Get("ActionEditorWindow_029"), InsertVariable, "plus");
        variable.Margin = new(0); DockPanel.SetDock(variable, Dock.Right); inputHeader.Children.Add(variable); inputHeader.Children.Add(contentHeading);
        var resize = new Thumb { Style = (Style)FindResource("EditorResizeGrip"),
            ToolTip = language.Get("ActionEditorWindow_030") };
        AutomationProperties.SetName(resize, language.Get("ActionEditorWindow_031")); AutomationProperties.SetAutomationId(resize, "ResizeActionContent");
        resize.DragDelta += (_, e) => content.Height = Math.Clamp(content.Height + e.VerticalChange, 96, 480);
        resize.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down or Key.Home)) return;
            content.Height = e.Key == Key.Home ? 120 : Math.Clamp(content.Height + (e.Key == Key.Up ? -16 : 16), 96, 480);
            e.Handled = true;
        };
        form.Children.Add(inputHeader); form.Children.Add(content); form.Children.Add(resize);
        help.Margin = new(0, 0, 0, 10); form.Children.Add(help);
        useSelection.Margin = new(0, 0, 0, 8);
        AutomationProperties.SetAutomationId(useSelection, "UseSelectionInput"); scriptOptions.Children.Add(useSelection);
        output.ItemsSource = OutputNames; output.SelectedIndex = (int)(existing?.Output ?? ActionOutput.Window); output.Padding = new(7); Field(scriptOptions, language.Get("ActionEditorWindow_032"), output);
        AutomationProperties.SetName(output, language.Get("ActionEditorWindow_033"));
        outputHelp.Margin = new(0, -4, 0, 8); AutomationProperties.SetAutomationId(outputHelp, "ActionOutputHelp"); scriptOptions.Children.Add(outputHelp);
        var advanced = new StackPanel(); var runtimeField = new StackPanel();
        Field(runtimeField, language.Get("ActionEditorWindow_034"), runtime); advanced.Children.Add(runtimeField);
        Field(advanced, language.Get("ActionEditorWindow_035"), timeout);
        AutomationProperties.SetName(runtime, language.Get("ActionEditorWindow_036")); AutomationProperties.SetName(timeout, language.Get("ActionEditorWindow_037"));
        var advancedExpander = new Expander { Header = language.Get("ActionEditorWindow_038"), Content = advanced };
        AutomationProperties.SetAutomationId(advancedExpander, "AdvancedActionSettings");
        advancedExpander.Expanded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (advancedExpander.IsExpanded && runtimeField.IsVisible) runtimeField.BringIntoView();
        });
        scriptOptions.Children.Add(advancedExpander); form.Children.Add(scriptOptions);
        var left = ActionUi.Card(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); grid.Children.Add(left);
        var right = new StackPanel(); right.Children.Add(ActionUi.Heading(language.Get("ActionEditorWindow_039")));
        var sampleLabel = ActionUi.Text(language.Get("ActionEditorWindow_040"), 13, ActionUi.Muted); sampleLabel.Margin = new(0, 20, 0, 8); sampleSection.Children.Add(sampleLabel);
        sample.AcceptsReturn = true; sample.TextWrapping = TextWrapping.Wrap; sample.Height = 96; sample.VerticalContentAlignment = VerticalAlignment.Top;
        sample.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; sampleSection.Children.Add(sample); right.Children.Add(sampleSection); AutomationProperties.SetName(sample, language.Get("ActionEditorWindow_041"));
        var resultHeader = new DockPanel { Margin = new(0, 24, 0, 6) };
        DockPanel.SetDock(showWhitespace, Dock.Right); resultHeader.Children.Add(showWhitespace); resultHeader.Children.Add(resultHeading); right.Children.Add(resultHeader);
        AutomationProperties.SetAutomationId(showWhitespace, "ShowResultWhitespace");
        AutomationProperties.SetName(showWhitespace, language.Get("ActionEditorWindow_042"));
        showWhitespace.Checked += (_, _) => RefreshResultDisplay(); showWhitespace.Unchecked += (_, _) => RefreshResultDisplay();
        destination.Margin = new(0, 0, 0, 10); right.Children.Add(destination);
        preview.IsReadOnly = true; preview.AcceptsReturn = true; preview.TextWrapping = TextWrapping.Wrap; preview.Height = 200;
        preview.VerticalContentAlignment = VerticalAlignment.Top; preview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        preview.FontFamily = new("Consolas, Microsoft JhengHei UI"); preview.Background = ActionUi.Canvas; right.Children.Add(preview); AutomationProperties.SetName(preview, language.Get("ActionEditorWindow_043"));
        comparison.Margin = new(0, 10, 0, 0); right.Children.Add(comparison);
        right.Children.Add(diagnostics);
        test = ActionUi.Button(language.Get("ActionEditorWindow_044"), () => _ = TestAsync(), "terminal"); test.Margin = new(0, 16, 0, 8);
        test.Background = ActionUi.Pale; test.BorderBrush = ActionUi.Brush("#B2C5EF");
        test.Resources["ControlHover"] = ActionUi.Brush("#DDE7FD");
        test.Resources["ControlPressed"] = ActionUi.Brush("#CCDDFB");
        AutomationProperties.SetAutomationId(test, "TestCustomAction"); right.Children.Add(test);
        stop = ActionUi.Button(language.Get("ActionEditorWindow_045"), () => testing?.Cancel()); stop.Margin = new(0, 0, 0, 8); stop.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(stop, "StopCustomActionTest"); right.Children.Add(stop); right.Children.Add(testStatus);
        var note = ActionUi.Text(language.Get("ActionEditorWindow_046"), 11, ActionUi.Muted); note.Margin = new(0, 24, 0, 0); right.Children.Add(note);
        var rightCard = ActionUi.Card(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Grid.SetColumn(rightCard, 2); grid.Children.Add(rightCard); outer.Children.Add(grid); Content = outer;
        currentKind = (CustomActionKind)kind.SelectedIndex;
        LoadKind(currentKind, existing); ready = true;
        kind.SelectionChanged += (_, _) => { if (!ready) return; testing?.Cancel(); drafts[currentKind] = (name.Text, content.Text, sample.Text, useSelection.IsChecked == true); currentKind = (CustomActionKind)kind.SelectedIndex; LoadKind(currentKind); };
        foreach (var input in new[] { name, content, sample, runtime, timeout }) input.TextChanged += (_, _) => { if (ready) UpdatePreview(); };
        output.SelectionChanged += (_, _) => { if (ready) UpdatePreview(); };
        useSelection.Checked += (_, _) => { if (ready) UpdatePreview(); };
        useSelection.Unchecked += (_, _) => { if (ready) UpdatePreview(); };
        Closed += (_, _) => testing?.Cancel();
        AutomationProperties.SetAutomationId(name, "CustomActionName"); AutomationProperties.SetAutomationId(content, "CustomActionContent");
        AutomationProperties.SetAutomationId(sample, "CustomActionSample"); AutomationProperties.SetAutomationId(kind, "CustomActionKind");
    }

    private void ChooseIcon()
    {
        var picker = new AiIconPickerWindow(selectedIcon) { Owner = this };
        picker.ShowDialog();
        if (picker.SelectedId is { } chosen)
        {
            selectedIcon = chosen; iconChosen = true; iconPreview.IconId = chosen;
        }
        iconButton.Focus();
    }

    private static void Field(Panel panel, string label, UIElement input)
    { var text = ActionUi.Text(label, 12); text.Margin = new(0, 0, 0, 6); panel.Children.Add(text); if (input is FrameworkElement e) e.Margin = new(0, 0, 0, 10); panel.Children.Add(input); }

    private void LoadKind(CustomActionKind type, CustomActionDefinition? existing = null)
    {
        var defaults = type switch
        {
            CustomActionKind.Url => (language.Get("ActionEditorWindow_047"), "https://translate.google.com/?sl=auto&tl=zh-TW&text=***&op=translate", "Make something wonderful.", true),
            CustomActionKind.PowerShell => (language.Get("ActionEditorWindow_048"), "($env:POPCLIP_TEXT -replace '\\s+', ' ').Trim()", "  Make   something    wonderful.  ", true),
            _ => (language.Get("ActionEditorWindow_049"), "const text = process.env.POPCLIP_TEXT;\nconsole.log(JSON.stringify(JSON.parse(text), null, 2));", "{\"product\":\"SnapBar\",\"focus\":\"next action\"}", true)
        };
        var draft = drafts.GetValueOrDefault(type, defaults); name.Text = existing != null ? initialDisplayName! : draft.Item1; content.Text = existing?.Content ?? draft.Item2; sample.Text = draft.Item3;
        useSelection.IsChecked = existing?.UseSelection ?? draft.Item4;
        if (!iconChosen)
        {
            selectedIcon = type == CustomActionKind.Url ? "globe" : "terminal";
            iconPreview.IconId = selectedIcon;
        }
        if (existing != null) { runtime.Text = existing.InterpreterPath ?? ""; timeout.Text = existing.TimeoutSeconds.ToString(); }
        scriptOptions.Visibility = type == CustomActionKind.Url ? Visibility.Collapsed : Visibility.Visible;
        UpdatePreview();
    }

    private CustomActionDefinition? ReadAction()
    {
        if (currentKind != CustomActionKind.Url && (!int.TryParse(timeout.Text, out var n) || n is < 1 or > 300)) { error.Text = language.Get("ActionEditorWindow_050"); return null; }
        var action = new CustomActionDefinition(id, UiNames.KeepOriginalName(name.Text.Trim(), initialDisplayName ?? "", originalAction?.Name), currentKind, selectedIcon, content.Text,
            (ActionOutput)Math.Max(0, output.SelectedIndex), currentKind == CustomActionKind.Url ? 10 : int.Parse(timeout.Text), string.IsNullOrWhiteSpace(runtime.Text) ? null : runtime.Text.Trim(),
            UseSelection: currentKind == CustomActionKind.Url || useSelection.IsChecked == true);
        error.Text = ValidationMessages.Describe(CustomActionPolicy.Validate(action), language); return error.Text.Length == 0 ? action : null;
    }
    private void UpdatePreview()
    {
        previewRevision++; ResetResultDisplay(); diagnostics.Clear(); comparison.Clear(); preview.Visibility = Visibility.Visible;
        var urlMode = currentKind == CustomActionKind.Url;
        var usesInput = urlMode || useSelection.IsChecked == true;
        sampleSection.Visibility = usesInput ? Visibility.Visible : Visibility.Collapsed;
        variable.IsEnabled = usesInput;
        outputHelp.Text = language.Run(() => OutputDescription((ActionOutput)Math.Max(0, output.SelectedIndex)));
        help.Text = urlMode ? language.Get("ActionEditorWindow_051") : !usesInput ? language.Get("ActionEditorWindow_052")
            : currentKind == CustomActionKind.PowerShell ? language.Get("ActionEditorWindow_053") : language.Get("ActionEditorWindow_054");
        var testLabel = urlMode ? language.Get("ActionEditorWindow_055") : language.Get("ActionEditorWindow_056");
        ((StackPanel)test.Content).Children.OfType<TextBlock>().Single().Text = testLabel;
        AutomationProperties.SetName(test, testLabel);
        resultHeading.Text = urlMode ? language.Get("ActionEditorWindow_057") : language.Get("ActionEditorWindow_058"); contentHeading.Text = urlMode ? language.Get("ActionEditorWindow_059") : language.Get("ActionEditorWindow_060");
        preview.Height = urlMode ? 144 : 80;
        if (currentKind == CustomActionKind.Url)
        { var url = CustomActionPolicy.ResolveUrl(sample.Text, content.Text); destination.Text = url.Success ? UiText.Join(language.Get("ActionEditorWindow_061"), url.Uri!.Host) : language.Get("ActionEditorWindow_062"); preview.Text = url.Success ? url.Uri!.AbsoluteUri : language.Get("ActionEditorWindow_063"); }
        else { destination.Text = $"{(currentKind == CustomActionKind.JavaScript ? "Node.js" : "PowerShell")} · {(usesInput ? language.Get("ActionEditorWindow_079") : language.Get("ActionEditorWindow_080"))}";
            preview.Text = usesInput ? language.Get("ActionEditorWindow_064") : language.Get("ActionEditorWindow_065"); }
        testStatus.Text = "";
    }
    private void InsertVariable()
    {
        if (currentKind != CustomActionKind.Url && useSelection.IsChecked != true) return;
        var variable = currentKind switch { CustomActionKind.Url => "***", CustomActionKind.PowerShell => "$env:POPCLIP_TEXT", _ => "process.env.POPCLIP_TEXT" };
        var start = content.SelectionStart; content.SelectedText = variable; content.Select(start + variable.Length, 0); content.Focus();
    }
    private void Save()
    { if (testing != null) { error.Text = language.Get("ActionEditorWindow_066"); return; } var action = ReadAction(); if (action == null) return; SavedAction = action; DialogResult = true; }
    private void ResetResultDisplay()
    {
        rawTestOutput = null; showWhitespace.IsChecked = false; showWhitespace.Visibility = Visibility.Collapsed;
        preview.TextWrapping = TextWrapping.Wrap; preview.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private void RefreshResultDisplay()
    {
        if (rawTestOutput == null) return;
        var symbols = showWhitespace.IsChecked == true;
        preview.Text = symbols ? ActionTestComparisonView.VisibleWhitespace(rawTestOutput) : rawTestOutput;
        preview.TextWrapping = symbols ? TextWrapping.NoWrap : TextWrapping.Wrap;
        preview.HorizontalScrollBarVisibility = symbols ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        preview.ScrollToHome();
    }

    private async Task TestAsync()
    {
        if (testing != null) return; var action = ReadAction(); if (action == null) return;
        if (currentKind == CustomActionKind.Url) { UpdatePreview(); testStatus.Text = language.Get("ActionEditorWindow_067"); return; }
        var testInput = action.UseSelection ? sample.Text : ""; var testRevision = previewRevision;
        using var cancellation = new CancellationTokenSource(); testing = cancellation; test.IsEnabled = false; kind.IsEnabled = false; stop.Visibility = Visibility.Visible;
        ResetResultDisplay(); diagnostics.Clear(); comparison.Clear(); preview.Text = ""; preview.Height = 80; preview.Visibility = Visibility.Visible;
        testStatus.Text = language.Get("ActionEditorWindow_068");
        try
        {
            var result = await CustomActionRunner.RunAsync(action, testInput, cancellation.Token);
            if (!IsVisible) return;
            if (testRevision != previewRevision)
            { UpdatePreview(); testStatus.Text = language.Get("ActionEditorWindow_069"); return; }
            var diagnostic = language.Run(() => ScriptDiagnostic.Create(action, result));
            preview.Text = diagnostic.IsFailure ? "" : result.Output;
            preview.Height = Math.Clamp((result.Output.Count(c => c == '\n') + 1) * 20 + 20, 64, 144);
            preview.Visibility = diagnostic.IsFailure ? Visibility.Collapsed : Visibility.Visible;
            if (!diagnostic.IsFailure)
            {
                rawTestOutput = result.Output; showWhitespace.Visibility = Visibility.Visible;
                RefreshResultDisplay();
            }
            if (!diagnostic.IsFailure && action.UseSelection) comparison.Show(testInput, result.Output);
            if (diagnostic.IsFailure || !string.IsNullOrWhiteSpace(result.Error)) diagnostics.Show(diagnostic);
            testStatus.Text = !diagnostic.IsFailure
                ? string.IsNullOrWhiteSpace(result.Error) ? language.Get("ActionEditorWindow_070") : language.Get("ActionEditorWindow_071")
                : language.Get("ActionEditorWindow_072");
        }
        finally { testing = null; test.IsEnabled = true; kind.IsEnabled = true; stop.Visibility = Visibility.Collapsed; }
    }
    internal static string ScriptStatus(string status) => ScriptDiagnostic.StatusText(status);
    internal static string OutputDescription(ActionOutput value) => value switch
    {
        ActionOutput.None => UiText.Get("ActionEditorWindow_073"),
        ActionOutput.Clipboard => UiText.Get("ActionEditorWindow_074"),
        ActionOutput.ReplaceSelection => UiText.Get("ActionEditorWindow_075"),
        ActionOutput.Hud => UiText.Get("ActionEditorWindow_076"),
        ActionOutput.Window => UiText.Get("ActionEditorWindow_077"),
        ActionOutput.Markdown => UiText.Get("ActionEditorWindow_078"),
        _ => ""
    };
}
