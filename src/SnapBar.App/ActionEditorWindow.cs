using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class ActionEditorWindow : Window
{
    private readonly string id;
    private readonly TextBox name = ActionUi.Input(max: 32), content = ActionUi.Input(), sample = ActionUi.Input(),
        runtime = ActionUi.Input(), timeout = ActionUi.Input("10", 3), preview = ActionUi.Input();
    private readonly ComboBox kind = new(), output = new(), icon = new();
    private readonly StackPanel scriptOptions = new();
    private readonly TextBlock error = ActionUi.Text("", 12, Brushes.Firebrick), destination = ActionUi.Text("", 12, ActionUi.Muted),
        help = ActionUi.Text("", 11, ActionUi.Muted), testStatus = ActionUi.Text("", 11, ActionUi.Muted);
    private readonly Button test;
    private readonly Dictionary<CustomActionKind, (string Name, string Content, string Sample)> drafts = new();
    private CustomActionKind currentKind;
    private CancellationTokenSource? testing;
    private bool ready;
    public CustomActionDefinition? SavedAction { get; private set; }
    private static readonly string[] OutputNames = ["不處理輸出", "複製到剪貼簿", "替換原文", "短暫提示", "顯示結果視窗", "顯示 Markdown 視窗"];

    public ActionEditorWindow(CustomActionDefinition? existing = null)
    {
        ActionUi.Setup(this, existing == null ? "新增自訂動作 · SnapBar" : "編輯自訂動作 · SnapBar", 1020, 790);
        MinWidth = 800; MinHeight = 580; id = existing?.Id ?? "custom-" + Guid.NewGuid().ToString("N");
        var outer = new DockPanel { Margin = new(28) };
        var heading = new StackPanel { Margin = new(0, 0, 0, 22) };
        heading.Children.Add(ActionUi.Text("MAKE IT YOURS", 10, ActionUi.Muted));
        heading.Children.Add(ActionUi.Text("把重複的工作，變成一個動作。", 25));
        DockPanel.SetDock(heading, Dock.Top); outer.Children.Add(heading);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(1.1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(20) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var form = new StackPanel();
        kind.ItemsSource = new[] { "網址", "PowerShell", "JavaScript" }; kind.SelectedIndex = (int)(existing?.Kind ?? CustomActionKind.Url);
        kind.Padding = new(9); kind.Margin = new(0, 0, 0, 18); form.Children.Add(kind);
        Field(form, "給它一個好記的名字", name);
        var iconNames = new[] { "複製", "剪下", "貼上", "搜尋", "連結", "資料夾", "網頁", "翻譯", "程式碼", "終端機", "星芒", "文件", "清理文字" };
        for (var i = 0; i < ActionUi.Icons.Length; i++)
        {
            var option = new StackPanel { Orientation = Orientation.Horizontal };
            option.Children.Add(ActionUi.Icon(ActionUi.Icons[i], ActionUi.Muted, 16));
            var label = ActionUi.Text(iconNames[i], 12); label.Margin = new(9, 0, 0, 0); option.Children.Add(label);
            var item = new ComboBoxItem { Tag = ActionUi.Icons[i], Content = option }; icon.Items.Add(item);
            if (ActionUi.Icons[i] == (existing?.Icon ?? "globe")) icon.SelectedItem = item;
        }
        icon.Padding = new(6); Field(form, "圖示", icon);
        content.AcceptsReturn = true; content.AcceptsTab = true; content.Height = 124; content.TextWrapping = TextWrapping.Wrap;
        content.FontFamily = new("Consolas, Microsoft JhengHei UI"); content.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var inputHeader = new DockPanel(); var variable = ActionUi.Button("插入選取文字", InsertVariable, "plus"); DockPanel.SetDock(variable, Dock.Right); inputHeader.Children.Add(variable); inputHeader.Children.Add(ActionUi.Text("網址或腳本內容", 12));
        form.Children.Add(inputHeader); form.Children.Add(content); help.Margin = new(0, 6, 0, 18); form.Children.Add(help);
        output.ItemsSource = OutputNames; output.SelectedIndex = (int)(existing?.Output ?? ActionOutput.Window); output.Padding = new(7); Field(scriptOptions, "完成後，如何處理結果？", output);
        var clipboardHint = ActionUi.Text("替換原文會先將結果放入剪貼簿，再驗證來源並送出貼上。", 10, ActionUi.Muted); clipboardHint.Margin = new(0, -8, 0, 15); scriptOptions.Children.Add(clipboardHint);
        var advanced = new StackPanel(); Field(advanced, "執行環境完整路徑（留空使用預設）", runtime); Field(advanced, "逾時秒數 · 1–300", timeout);
        scriptOptions.Children.Add(new Expander { Header = "進階設定", Content = advanced }); form.Children.Add(scriptOptions);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        buttons.Children.Add(ActionUi.Button("取消", Close)); buttons.Children.Add(ActionUi.Button("儲存動作", Save, primary: true)); form.Children.Add(buttons); form.Children.Add(error);
        var left = ActionUi.Card(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); grid.Children.Add(left);
        var right = new StackPanel(); right.Children.Add(ActionUi.Text("儲存之前，先看一眼。", 15));
        var sampleLabel = ActionUi.Text("拿這段範例文字試試", 11, ActionUi.Muted); sampleLabel.Margin = new(0, 22, 0, 8); right.Children.Add(sampleLabel);
        sample.AcceptsReturn = true; sample.TextWrapping = TextWrapping.Wrap; sample.Height = 90; sample.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; right.Children.Add(sample);
        destination.Margin = new(0, 18, 0, 10); right.Children.Add(destination);
        preview.IsReadOnly = true; preview.AcceptsReturn = true; preview.TextWrapping = TextWrapping.Wrap; preview.Height = 220; preview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; preview.FontFamily = new("Consolas, Microsoft JhengHei UI"); right.Children.Add(preview);
        test = ActionUi.Button("預覽／測試", () => _ = TestAsync(), "terminal"); test.Margin = new(0, 15, 0, 8); right.Children.Add(test);
        right.Children.Add(ActionUi.Button("取消測試", () => testing?.Cancel())); right.Children.Add(testStatus);
        var note = ActionUi.Text("儲存不會執行動作。網址只預覽；腳本在點擊測試後會以你的使用者權限實際執行。", 11, ActionUi.Muted); note.Margin = new(0, 24, 0, 0); right.Children.Add(note);
        var rightCard = ActionUi.Card(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); rightCard.Background = ActionUi.Brush("#EEF0E8"); Grid.SetColumn(rightCard, 2); grid.Children.Add(rightCard); outer.Children.Add(grid); Content = outer;
        currentKind = (CustomActionKind)kind.SelectedIndex;
        LoadKind(currentKind, existing); ready = true;
        kind.SelectionChanged += (_, _) => { if (!ready) return; testing?.Cancel(); drafts[currentKind] = (name.Text, content.Text, sample.Text); currentKind = (CustomActionKind)kind.SelectedIndex; LoadKind(currentKind); };
        foreach (var input in new[] { name, content, sample, runtime, timeout }) input.TextChanged += (_, _) => { if (ready && testing == null) UpdatePreview(); };
        output.SelectionChanged += (_, _) => { if (ready) UpdatePreview(); };
        Closed += (_, _) => testing?.Cancel();
        AutomationProperties.SetAutomationId(name, "CustomActionName"); AutomationProperties.SetAutomationId(content, "CustomActionContent");
        AutomationProperties.SetAutomationId(sample, "CustomActionSample"); AutomationProperties.SetAutomationId(kind, "CustomActionKind");
    }

    private static void Field(Panel panel, string label, UIElement input)
    { var text = ActionUi.Text(label, 12); text.Margin = new(0, 0, 0, 6); panel.Children.Add(text); if (input is FrameworkElement e) e.Margin = new(0, 0, 0, 16); panel.Children.Add(input); }

    private void LoadKind(CustomActionKind type, CustomActionDefinition? existing = null)
    {
        var defaults = type switch
        {
            CustomActionKind.Url => ("翻譯成繁體中文", "https://translate.google.com/?sl=auto&tl=zh-TW&text=***&op=translate", "Make something wonderful."),
            CustomActionKind.PowerShell => ("清理多餘空白", "($env:POPCLIP_TEXT -replace '\\s+', ' ').Trim()", "  Make   something    wonderful.  "),
            _ => ("格式化 JSON", "const text = process.env.POPCLIP_TEXT;\nconsole.log(JSON.stringify(JSON.parse(text), null, 2));", "{\"product\":\"SnapBar\",\"focus\":\"next action\"}")
        };
        var draft = drafts.GetValueOrDefault(type, defaults); name.Text = existing?.Name ?? draft.Item1; content.Text = existing?.Content ?? draft.Item2; sample.Text = draft.Item3;
        if (existing != null) { runtime.Text = existing.InterpreterPath ?? ""; timeout.Text = existing.TimeoutSeconds.ToString(); }
        scriptOptions.Visibility = type == CustomActionKind.Url ? Visibility.Collapsed : Visibility.Visible;
        help.Text = type switch { CustomActionKind.Url => "*** 會換成完整編碼的選取文字；儲存不會開啟網址。", CustomActionKind.PowerShell => "從 $env:POPCLIP_TEXT 讀取選取文字，不拼入指令。", _ => "從 process.env.POPCLIP_TEXT 讀取選取文字。需要 Node.js。" };
        UpdatePreview();
    }

    private CustomActionDefinition? ReadAction()
    {
        if (currentKind != CustomActionKind.Url && (!int.TryParse(timeout.Text, out var n) || n is < 1 or > 300)) { error.Text = "逾時秒數須為 1–300 的整數。"; return null; }
        var action = new CustomActionDefinition(id, name.Text.Trim(), currentKind, (icon.SelectedItem as ComboBoxItem)?.Tag as string ?? "spark", content.Text,
            (ActionOutput)Math.Max(0, output.SelectedIndex), currentKind == CustomActionKind.Url ? 10 : int.Parse(timeout.Text), string.IsNullOrWhiteSpace(runtime.Text) ? null : runtime.Text.Trim());
        error.Text = CustomActionPolicy.Validate(action) ?? ""; return error.Text.Length == 0 ? action : null;
    }
    private void UpdatePreview()
    {
        if (currentKind == CustomActionKind.Url)
        { var url = CustomActionPolicy.ResolveUrl(sample.Text, content.Text); destination.Text = url.Success ? "目的地 · " + url.Uri!.Host : "等待有效的 HTTP／HTTPS 網址"; preview.Text = url.Success ? url.Uri!.AbsoluteUri : "請在路徑、參數值或片段中放入一個 ***。"; }
        else { destination.Text = $"{(currentKind == CustomActionKind.JavaScript ? "Node.js" : "PowerShell")} · {sample.Text.Length} 字輸入"; preview.Text = "按「預覽／測試」以範例文字實際執行腳本。"; }
        testStatus.Text = "";
    }
    private void InsertVariable()
    {
        var variable = currentKind switch { CustomActionKind.Url => "***", CustomActionKind.PowerShell => "$env:POPCLIP_TEXT", _ => "process.env.POPCLIP_TEXT" };
        var start = content.SelectionStart; content.SelectedText = variable; content.Select(start + variable.Length, 0); content.Focus();
    }
    private void Save()
    { if (testing != null) { error.Text = "請等待或取消測試後再儲存。"; return; } var action = ReadAction(); if (action == null) return; SavedAction = action; DialogResult = true; }
    private async Task TestAsync()
    {
        if (testing != null) return; var action = ReadAction(); if (action == null) return;
        if (currentKind == CustomActionKind.Url) { UpdatePreview(); testStatus.Text = "網址已預覽；未開啟瀏覽器。"; return; }
        using var cancellation = new CancellationTokenSource(); testing = cancellation; test.IsEnabled = false; kind.IsEnabled = false;
        testStatus.Text = "正在執行範例…";
        try
        {
            var result = await CustomActionRunner.RunAsync(action, sample.Text, cancellation.Token);
            if (!IsVisible) return;
            preview.Text = result.Success ? result.Output : result.Error.Length > 0 ? result.Error : ScriptStatus(result.Status);
            testStatus.Text = result.Success ? "範例已完成；未複製或套用至其他 App。" : ScriptStatus(result.Status);
        }
        finally { testing = null; test.IsEnabled = true; kind.IsEnabled = true; }
    }
    internal static string ScriptStatus(string status) => status switch
    { "cancelled" => "已取消執行。", "script-timeout" => "執行逾時，已終止程序。", "runtime-unavailable" => "找不到執行環境，請安裝或指定完整的 .exe 路徑。", "script-output-too-large" => "輸出超過大小限制，已停止。", "invalid-action" => "動作設定無效。", _ => "腳本執行失敗，請檢查內容與執行環境。" };
}
