using System.Windows;
using System.Windows.Controls;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class AiPromptEditorWindow : Window
{
    public AiPromptEditorWindow(AiPrompt? existing, Func<AiPrompt, string?> save)
    {
        ActionUi.Setup(this, existing == null ? "新增 AI 指令" : "編輯 AI 指令", 570, 610); MinWidth = 460;
        var body = new DockPanel { Margin = new(24) };
        var footer = new StackPanel(); var hint = ActionUi.Text("", 12, ActionUi.Muted); footer.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var name = ActionUi.Input(existing?.Name ?? "", 64); var category = ActionUi.Input(existing?.Category ?? "自訂", 32);
        var keywords = ActionUi.Input(existing?.Keywords ?? "", 256); var instruction = ActionUi.Input(existing?.Instruction ?? "", 8192);
        instruction.AcceptsReturn = true; instruction.TextWrapping = TextWrapping.Wrap; instruction.Height = 220;
        instruction.VerticalContentAlignment = VerticalAlignment.Top;
        instruction.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var icon = new ComboBox { ItemsSource = ActionUi.Icons, SelectedItem = existing?.Icon ?? "spark", MinHeight = 36 };
        buttons.Children.Add(ActionUi.Button("取消", Close));
        buttons.Children.Add(ActionUi.Button("儲存指令", () =>
        {
            var prompt = new AiPrompt(existing?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), instruction.Text, category.Text.Trim(), keywords.Text.Trim(), (string?)icon.SelectedItem ?? "spark");
            if (save(prompt) is { } error) { hint.Text = error; return; } Close();
        }, primary: true));
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var fields = new StackPanel(); fields.Children.Add(ActionUi.Heading(existing == null ? "新增指令" : "編輯指令", 22));
        void Field(string label, Control input) { var text = ActionUi.Text(label, 12, ActionUi.Muted); text.Margin = new(0, 12, 0, 5); fields.Children.Add(text); fields.Children.Add(input); }
        Field("名稱", name); Field("分類", category); Field("指令內容", instruction); Field("搜尋關鍵字", keywords); Field("圖示", icon);
        body.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = body;
    }
}
