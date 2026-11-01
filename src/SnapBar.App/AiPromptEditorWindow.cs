using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class AiPromptEditorWindow : Window
{
    private sealed record IconChoice(string Id, string Label, Geometry Geometry)
    {
        public override string ToString() => Label;
    }

    public AiPromptEditorWindow(AiPrompt? existing, Func<AiPrompt, string?> save)
    {
        ActionUi.Setup(this, existing == null ? "新增 AI 指令" : "編輯 AI 指令", 570, 610); MinWidth = 460;
        var body = new DockPanel { Margin = new(24) };
        var title = ActionUi.Heading(existing == null ? "新增指令" : "編輯指令", 22); title.Margin = new(0, 0, 0, 18);
        DockPanel.SetDock(title, Dock.Top); body.Children.Add(title);
        var footer = new StackPanel { Margin = new(0, 16, 0, 0) };
        var hint = ActionUi.Text("", 12, Brushes.Firebrick); footer.Children.Add(hint);
        AutomationProperties.SetAutomationId(hint, "AiPromptError");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var name = ActionUi.Input(existing?.Name ?? "", 64); var category = ActionUi.Input(existing?.Category ?? "自訂", 32);
        var keywords = ActionUi.Input(existing?.Keywords ?? "", 256); var instruction = ActionUi.Input(existing?.Instruction ?? "", 8192);
        instruction.AcceptsReturn = true; instruction.TextWrapping = TextWrapping.Wrap; instruction.MinHeight = 140;
        instruction.VerticalContentAlignment = VerticalAlignment.Top; instruction.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        AutomationProperties.SetAutomationId(name, "AiPromptName"); AutomationProperties.SetName(name, "指令名稱");
        AutomationProperties.SetAutomationId(instruction, "AiPromptInstruction"); AutomationProperties.SetName(instruction, "指令內容");
        AutomationProperties.SetAutomationId(category, "AiPromptCategory"); AutomationProperties.SetName(category, "分類（選填）");
        AutomationProperties.SetAutomationId(keywords, "AiPromptKeywords"); AutomationProperties.SetName(keywords, "搜尋關鍵字（選填）");
        var icon = new ComboBox { MinHeight = 36, SelectedValuePath = nameof(IconChoice.Id), ItemTemplate = (DataTemplate)FindResource("AiPromptIconOption") };
        AutomationProperties.SetAutomationId(icon, "AiPromptIcon"); AutomationProperties.SetName(icon, "指令圖示");
        var choices = new List<IconChoice>();
        void IconOption(string id, string label)
        {
            var preview = (Viewbox)ActionUi.Icon(id, ActionUi.Muted, 16);
            choices.Add(new(id, label, ((System.Windows.Shapes.Path)preview.Child).Data));
        }
        var iconNames = new[] { "複製", "剪下", "貼上", "搜尋", "連結", "資料夾", "網頁", "翻譯", "程式碼", "終端機", "星芒", "文件", "清理文字" };
        for (var i = 0; i < ActionUi.Icons.Length; i++) IconOption(ActionUi.Icons[i], iconNames[i]);
        if (existing is { } current && !ActionUi.Icons.Contains(current.Icon)) IconOption(current.Icon, "目前圖示");
        icon.ItemsSource = choices; icon.SelectedValue = existing?.Icon ?? "spark";
        var cancel = ActionUi.Button("取消", Close); AutomationProperties.SetAutomationId(cancel, "AiPromptCancel"); buttons.Children.Add(cancel);
        var saveButton = ActionUi.Button("儲存指令", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { hint.Text = "請填寫指令名稱。"; name.Focus(); return; }
            if (string.IsNullOrWhiteSpace(instruction.Text)) { hint.Text = "請填寫指令內容。"; instruction.Focus(); return; }
            var prompt = new AiPrompt(existing?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), instruction.Text,
                category.Text.Trim(), keywords.Text.Trim(), (string?)icon.SelectedValue ?? "spark");
            if (save(prompt) is { } error) { hint.Text = error; return; } Close();
        }, primary: true);
        AutomationProperties.SetAutomationId(saveButton, "AiPromptSave"); buttons.Children.Add(saveButton);
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        static void Field(Panel fields, string label, Control input)
        {
            var text = ActionUi.Text(label, 12, ActionUi.Muted); text.Margin = new(0, 12, 0, 5);
            fields.Children.Add(text); fields.Children.Add(input);
        }
        var primary = new StackPanel(); var nameLabel = ActionUi.Text("名稱", 12, ActionUi.Muted); nameLabel.Margin = new(0, 0, 0, 5);
        primary.Children.Add(nameLabel); primary.Children.Add(name);
        var instructionLabel = ActionUi.Text("指令內容", 12, ActionUi.Muted); instructionLabel.Margin = new(0, 14, 0, 0); primary.Children.Add(instructionLabel);
        var help = ActionUi.Text("直接描述處理方式，選取文字會自動帶入。", 12, ActionUi.Muted); help.Margin = new(0, 6, 0, 8); primary.Children.Add(help);
        var optional = new StackPanel(); Field(optional, "分類（選填）", category); Field(optional, "搜尋關鍵字（選填）", keywords); Field(optional, "圖示", icon);
        var advanced = new Expander { Header = "更多設定（選填）", Content = optional, Margin = new(0, 12, 0, 0), IsExpanded = false };
        AutomationProperties.SetAutomationId(advanced, "AiPromptAdvanced");
        var form = new Grid(); form.RowDefinitions.Add(new() { Height = GridLength.Auto });
        form.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); form.RowDefinitions.Add(new() { Height = GridLength.Auto });
        form.Children.Add(primary); Grid.SetRow(instruction, 1); form.Children.Add(instruction); Grid.SetRow(advanced, 2); form.Children.Add(advanced);
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetAutomationId(scroll, "AiPromptForm");
        form.SetBinding(HeightProperty, new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = scroll });
        void UpdateMinimum() => form.MinHeight = primary.DesiredSize.Height + advanced.DesiredSize.Height + instruction.MinHeight;
        primary.SizeChanged += (_, _) => UpdateMinimum(); advanced.SizeChanged += (_, _) => UpdateMinimum();
        advanced.Expanded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => category.BringIntoView());
        body.Children.Add(scroll); Content = body; Loaded += (_, _) => name.Focus();
    }
}
