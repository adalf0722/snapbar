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
    private readonly string id;
    private readonly TextBox name = ActionUi.Input(max: 32), content = ActionUi.Input(), sample = ActionUi.Input(),
        runtime = ActionUi.Input(), timeout = ActionUi.Input("10", 3), preview = ActionUi.Input();
    private readonly ComboBox kind = new(), output = new();
    private string selectedIcon = "globe";
    private readonly AiIconPresenter iconPreview = new() { Width = 22, Height = 22 };
    private Button iconButton = null!;
    private readonly StackPanel scriptOptions = new(), sampleSection = new();
    private readonly CheckBox useSelection = new() { Content = "使用選取文字", IsChecked = true, Margin = new(0, 0, 0, 14),
        ToolTip = "關閉時以空字串執行腳本；仍從選字工具列啟動並驗證來源。" };
    private readonly ActionDiagnosticView diagnostics = new() { Visibility = Visibility.Collapsed };
    private readonly ActionTestComparisonView comparison = new();
    private readonly CheckBox showWhitespace = new()
    {
        Content = "顯示空白符號", FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        Margin = new(12, 0, 0, 0), Visibility = Visibility.Collapsed,
        ToolTip = "· 空格   ⇥ Tab   ␍ CR   ↵ 換行\n只切換顯示方式，不修改實際輸出。"
    };
    private string? rawTestOutput;
    private readonly TextBlock error = ActionUi.Text("", 12, Brushes.Firebrick), destination = ActionUi.Text("", 12, ActionUi.Muted),
        help = ActionUi.Text("", 11, ActionUi.Muted), testStatus = ActionUi.Text("", 11, ActionUi.Muted);
    private readonly Button test, stop, variable;
    private readonly TextBlock outputHelp = ActionUi.Text("", 12, ActionUi.Muted);
    private readonly TextBlock resultHeading = ActionUi.Heading("執行結果", 14), contentHeading = ActionUi.Heading("腳本內容", 14);
    private readonly Dictionary<CustomActionKind, (string Name, string Content, string Sample, bool UseSelection)> drafts = new();
    private CustomActionKind currentKind;
    private CancellationTokenSource? testing;
    private bool ready, iconChosen;
    private int previewRevision;
    public CustomActionDefinition? SavedAction { get; private set; }
    private static readonly string[] OutputNames = ["不處理輸出", "複製到剪貼簿", "替換原文", "短暫提示", "顯示結果視窗", "顯示 Markdown 視窗"];

    public ActionEditorWindow(CustomActionDefinition? existing = null)
    {
        ActionUi.Setup(this, existing == null ? "新增自訂動作 · SnapBar" : "編輯自訂動作 · SnapBar", 1080, 850);
        MinWidth = 880; MinHeight = 640; WindowWorkArea.Attach(this); id = existing?.Id ?? "custom-" + Guid.NewGuid().ToString("N");
        iconChosen = existing != null;
        selectedIcon = existing?.Icon ?? "globe"; iconPreview.IconId = selectedIcon;
        var outer = new DockPanel { Margin = new(28) };
        var heading = new StackPanel { Margin = new(0, 0, 0, 22) };
        heading.Children.Add(ActionUi.Heading(existing == null ? "新增動作" : "編輯動作", 24));
        var subtitle = ActionUi.Text("設定一次，選字後就能使用。", 13, ActionUi.Muted); subtitle.Margin = new(0, 5, 0, 0); heading.Children.Add(subtitle);
        DockPanel.SetDock(heading, Dock.Top); outer.Children.Add(heading);
        var footer = new DockPanel { Margin = new(0, 18, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ActionUi.Button("取消", Close)); buttons.Children.Add(ActionUi.Button("儲存動作", Save, primary: true));
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        var footerText = new StackPanel { Margin = new(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        footerText.Children.Add(ActionUi.Text("儲存不會執行動作。", 12, ActionUi.Muted)); footerText.Children.Add(error); footer.Children.Add(footerText);
        DockPanel.SetDock(footer, Dock.Bottom); outer.Children.Add(footer);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(1.1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(20) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var form = new StackPanel(); var formHeading = ActionUi.Heading("動作設定"); formHeading.Margin = new(0, 0, 0, 12); form.Children.Add(formHeading);
        kind.ItemsSource = new[] { "網址", "PowerShell", "JavaScript" }; kind.SelectedIndex = (int)(existing?.Kind ?? CustomActionKind.Url);
        AutomationProperties.SetName(kind, "動作類型"); Field(form, "動作類型", kind);
        var identity = new Grid(); identity.ColumnDefinitions.Add(new() { Width = new(44) });
        identity.ColumnDefinitions.Add(new() { Width = new(12) }); identity.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        iconButton = ActionUi.Button("", ChooseIcon); iconButton.Content = iconPreview;
        iconButton.Width = iconButton.Height = 44; iconButton.Padding = new(0);
        iconButton.ToolTip = "更換圖示";
        AutomationProperties.SetName(iconButton, "更換圖示");
        AutomationProperties.SetAutomationId(iconButton, "CustomActionChooseIcon");
        AutomationProperties.SetAutomationId(iconPreview, "CustomActionIconPreview");
        name.Height = 44;
        var iconField = new StackPanel(); Field(iconField, "圖示", iconButton); identity.Children.Add(iconField);
        var nameField = new StackPanel(); Field(nameField, "名稱", name); Grid.SetColumn(nameField, 2); identity.Children.Add(nameField); form.Children.Add(identity);
        iconButton.Margin = name.Margin = new(0, 0, 0, 3);
        AutomationProperties.SetName(name, "動作名稱");
        content.AcceptsReturn = true; content.AcceptsTab = true; content.Height = 120; content.TextWrapping = TextWrapping.Wrap;
        content.VerticalContentAlignment = VerticalAlignment.Top;
        content.FontFamily = new("Consolas, Microsoft JhengHei UI"); content.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var inputHeader = new DockPanel { Margin = new(0, 0, 0, 8) }; variable = ActionUi.Button("插入選取文字", InsertVariable, "plus");
        variable.Margin = new(0); DockPanel.SetDock(variable, Dock.Right); inputHeader.Children.Add(variable); inputHeader.Children.Add(contentHeading);
        var resize = new Thumb { Style = (Style)FindResource("EditorResizeGrip"),
            ToolTip = "拖曳調整高度；上下鍵調整，Home 還原。" };
        AutomationProperties.SetName(resize, "調整內容編輯區高度"); AutomationProperties.SetAutomationId(resize, "ResizeActionContent");
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
        output.ItemsSource = OutputNames; output.SelectedIndex = (int)(existing?.Output ?? ActionOutput.Window); output.Padding = new(7); Field(scriptOptions, "完成後，如何處理結果？", output);
        AutomationProperties.SetName(output, "結果處理方式");
        outputHelp.Margin = new(0, -4, 0, 8); AutomationProperties.SetAutomationId(outputHelp, "ActionOutputHelp"); scriptOptions.Children.Add(outputHelp);
        var advanced = new StackPanel(); var runtimeField = new StackPanel();
        Field(runtimeField, "執行環境完整路徑（留空使用預設）", runtime); advanced.Children.Add(runtimeField);
        Field(advanced, "逾時秒數 · 1–300", timeout);
        AutomationProperties.SetName(runtime, "執行環境完整路徑"); AutomationProperties.SetName(timeout, "逾時秒數");
        var advancedExpander = new Expander { Header = "進階設定", Content = advanced };
        AutomationProperties.SetAutomationId(advancedExpander, "AdvancedActionSettings");
        advancedExpander.Expanded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (advancedExpander.IsExpanded && runtimeField.IsVisible) runtimeField.BringIntoView();
        });
        scriptOptions.Children.Add(advancedExpander); form.Children.Add(scriptOptions);
        var left = ActionUi.Card(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); grid.Children.Add(left);
        var right = new StackPanel(); right.Children.Add(ActionUi.Heading("測試"));
        var sampleLabel = ActionUi.Text("範例輸入", 13, ActionUi.Muted); sampleLabel.Margin = new(0, 20, 0, 8); sampleSection.Children.Add(sampleLabel);
        sample.AcceptsReturn = true; sample.TextWrapping = TextWrapping.Wrap; sample.Height = 96; sample.VerticalContentAlignment = VerticalAlignment.Top;
        sample.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; sampleSection.Children.Add(sample); right.Children.Add(sampleSection); AutomationProperties.SetName(sample, "範例輸入");
        var resultHeader = new DockPanel { Margin = new(0, 24, 0, 6) };
        DockPanel.SetDock(showWhitespace, Dock.Right); resultHeader.Children.Add(showWhitespace); resultHeader.Children.Add(resultHeading); right.Children.Add(resultHeader);
        AutomationProperties.SetAutomationId(showWhitespace, "ShowResultWhitespace");
        AutomationProperties.SetName(showWhitespace, "顯示空白符號");
        showWhitespace.Checked += (_, _) => RefreshResultDisplay(); showWhitespace.Unchecked += (_, _) => RefreshResultDisplay();
        destination.Margin = new(0, 0, 0, 10); right.Children.Add(destination);
        preview.IsReadOnly = true; preview.AcceptsReturn = true; preview.TextWrapping = TextWrapping.Wrap; preview.Height = 200;
        preview.VerticalContentAlignment = VerticalAlignment.Top; preview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        preview.FontFamily = new("Consolas, Microsoft JhengHei UI"); preview.Background = ActionUi.Canvas; right.Children.Add(preview); AutomationProperties.SetName(preview, "執行結果或網址預覽");
        comparison.Margin = new(0, 10, 0, 0); right.Children.Add(comparison);
        right.Children.Add(diagnostics);
        test = ActionUi.Button("執行測試", () => _ = TestAsync(), "terminal"); test.Margin = new(0, 16, 0, 8);
        test.Background = ActionUi.Pale; test.BorderBrush = ActionUi.Brush("#B2C5EF");
        test.Resources["ControlHover"] = ActionUi.Brush("#DDE7FD");
        test.Resources["ControlPressed"] = ActionUi.Brush("#CCDDFB");
        AutomationProperties.SetAutomationId(test, "TestCustomAction"); right.Children.Add(test);
        stop = ActionUi.Button("停止測試", () => testing?.Cancel()); stop.Margin = new(0, 0, 0, 8); stop.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(stop, "StopCustomActionTest"); right.Children.Add(stop); right.Children.Add(testStatus);
        var note = ActionUi.Text("儲存不會執行動作。網址只預覽；腳本在點擊測試後會以你的使用者權限實際執行。", 11, ActionUi.Muted); note.Margin = new(0, 24, 0, 0); right.Children.Add(note);
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
            CustomActionKind.Url => ("翻譯成繁體中文", "https://translate.google.com/?sl=auto&tl=zh-TW&text=***&op=translate", "Make something wonderful.", true),
            CustomActionKind.PowerShell => ("清理多餘空白", "($env:POPCLIP_TEXT -replace '\\s+', ' ').Trim()", "  Make   something    wonderful.  ", true),
            _ => ("格式化 JSON", "const text = process.env.POPCLIP_TEXT;\nconsole.log(JSON.stringify(JSON.parse(text), null, 2));", "{\"product\":\"SnapBar\",\"focus\":\"next action\"}", true)
        };
        var draft = drafts.GetValueOrDefault(type, defaults); name.Text = existing?.Name ?? draft.Item1; content.Text = existing?.Content ?? draft.Item2; sample.Text = draft.Item3;
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
        if (currentKind != CustomActionKind.Url && (!int.TryParse(timeout.Text, out var n) || n is < 1 or > 300)) { error.Text = "逾時秒數須為 1–300 的整數。"; return null; }
        var action = new CustomActionDefinition(id, name.Text.Trim(), currentKind, selectedIcon, content.Text,
            (ActionOutput)Math.Max(0, output.SelectedIndex), currentKind == CustomActionKind.Url ? 10 : int.Parse(timeout.Text), string.IsNullOrWhiteSpace(runtime.Text) ? null : runtime.Text.Trim(),
            UseSelection: currentKind == CustomActionKind.Url || useSelection.IsChecked == true);
        error.Text = CustomActionPolicy.Validate(action) ?? ""; return error.Text.Length == 0 ? action : null;
    }
    private void UpdatePreview()
    {
        previewRevision++; ResetResultDisplay(); diagnostics.Clear(); comparison.Clear(); preview.Visibility = Visibility.Visible;
        var urlMode = currentKind == CustomActionKind.Url;
        var usesInput = urlMode || useSelection.IsChecked == true;
        sampleSection.Visibility = usesInput ? Visibility.Visible : Visibility.Collapsed;
        variable.IsEnabled = usesInput;
        outputHelp.Text = OutputDescription((ActionOutput)Math.Max(0, output.SelectedIndex));
        help.Text = urlMode ? "*** 會換成完整編碼的選取文字；儲存不會開啟網址。" : !usesInput ? "不傳入選取文字；POPCLIP_TEXT 會是空字串。"
            : currentKind == CustomActionKind.PowerShell ? "從 $env:POPCLIP_TEXT 讀取選取文字，不拼入指令。" : "從 process.env.POPCLIP_TEXT 讀取選取文字。需要 Node.js。";
        var testLabel = urlMode ? "預覽網址" : "執行測試";
        ((StackPanel)test.Content).Children.OfType<TextBlock>().Single().Text = testLabel;
        AutomationProperties.SetName(test, testLabel);
        resultHeading.Text = urlMode ? "網址預覽" : "執行結果"; contentHeading.Text = urlMode ? "網址範本" : "腳本內容";
        preview.Height = urlMode ? 144 : 80;
        if (currentKind == CustomActionKind.Url)
        { var url = CustomActionPolicy.ResolveUrl(sample.Text, content.Text); destination.Text = url.Success ? "目的地 · " + url.Uri!.Host : "等待有效的 HTTP／HTTPS 網址"; preview.Text = url.Success ? url.Uri!.AbsoluteUri : "請在路徑、參數值或片段中放入一個 ***。"; }
        else { destination.Text = $"{(currentKind == CustomActionKind.JavaScript ? "Node.js" : "PowerShell")} · {(usesInput ? "範例測試" : "不使用選取文字")}";
            preview.Text = usesInput ? "尚未測試\n執行後可查看原始結果與輸入／輸出比較。" : "尚未測試\n執行後可在此查看結果。"; }
        testStatus.Text = "";
    }
    private void InsertVariable()
    {
        if (currentKind != CustomActionKind.Url && useSelection.IsChecked != true) return;
        var variable = currentKind switch { CustomActionKind.Url => "***", CustomActionKind.PowerShell => "$env:POPCLIP_TEXT", _ => "process.env.POPCLIP_TEXT" };
        var start = content.SelectionStart; content.SelectedText = variable; content.Select(start + variable.Length, 0); content.Focus();
    }
    private void Save()
    { if (testing != null) { error.Text = "請等待或取消測試後再儲存。"; return; } var action = ReadAction(); if (action == null) return; SavedAction = action; DialogResult = true; }
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
        if (currentKind == CustomActionKind.Url) { UpdatePreview(); testStatus.Text = "網址已預覽；未開啟瀏覽器。"; return; }
        var testInput = action.UseSelection ? sample.Text : ""; var testRevision = previewRevision;
        using var cancellation = new CancellationTokenSource(); testing = cancellation; test.IsEnabled = false; kind.IsEnabled = false; stop.Visibility = Visibility.Visible;
        ResetResultDisplay(); diagnostics.Clear(); comparison.Clear(); preview.Text = ""; preview.Height = 80; preview.Visibility = Visibility.Visible;
        testStatus.Text = "正在執行範例…";
        try
        {
            var result = await CustomActionRunner.RunAsync(action, testInput, cancellation.Token);
            if (!IsVisible) return;
            if (testRevision != previewRevision)
            { UpdatePreview(); testStatus.Text = "設定或範例已變更，請重新測試。"; return; }
            var diagnostic = ScriptDiagnostic.Create(action, result);
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
                ? string.IsNullOrWhiteSpace(result.Error) ? "範例已完成；未複製或套用至其他 App。" : "程序已結束；請查看診斷訊息。未複製或套用至其他 App。"
                : "範例未完成；請查看上方原因與詳細資訊。";
        }
        finally { testing = null; test.IsEnabled = true; kind.IsEnabled = true; stop.Visibility = Visibility.Collapsed; }
    }
    internal static string ScriptStatus(string status) => ScriptDiagnostic.StatusText(status);
    internal static string OutputDescription(ActionOutput value) => value switch
    {
        ActionOutput.None => "完成後不顯示或複製輸出，不修改原文。",
        ActionOutput.Clipboard => "將結果複製到剪貼簿，不修改原文。",
        ActionOutput.ReplaceSelection => "重新驗證原選取後，以結果取代；結果也會放入剪貼簿。",
        ActionOutput.Hud => "短暫顯示結果，不修改原文或剪貼簿。",
        ActionOutput.Window => "在文字結果視窗中顯示，可自行複製或套用。",
        ActionOutput.Markdown => "以 Markdown 排版顯示結果，可自行複製或套用。",
        _ => ""
    };
}
