using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class AiPromptEditorWindow : Window
{
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
        var initialIcon = existing == null ? "spark" : AiIconCatalog.ForPrompt(existing);
        var icon = ActionUi.Input(initialIcon, 128); icon.FontSize = 12;
        AutomationProperties.SetAutomationId(icon, "AiPromptIcon"); AutomationProperties.SetName(icon, "圖示名稱");
        icon.ToolTip = "可輸入 translate 或 translate_20_regular，也可按選擇圖示。";
        var preview = new AiIconPresenter { IconId = initialIcon, Width = 24, Height = 24, Margin = new(0, 0, 10, 0) };
        AutomationProperties.SetAutomationId(preview, "AiPromptIconPreview");
        var iconHint = ActionUi.Text("", 12, ActionUi.Muted); iconHint.Margin = new(0, 5, 0, 0);
        AutomationProperties.SetAutomationId(iconHint, "AiPromptIconHint");
        var choose = ActionUi.Button("選擇圖示", () =>
        {
            var picker = new AiIconPickerWindow(icon.Text) { Owner = this }; picker.ShowDialog();
            if (picker.SelectedId is { } selected) icon.Text = selected;
        }); choose.Margin = new(8, 0, 0, 0); AutomationProperties.SetAutomationId(choose, "AiPromptChooseIcon");
        var iconLine = new DockPanel(); DockPanel.SetDock(preview, Dock.Left); iconLine.Children.Add(preview);
        DockPanel.SetDock(choose, Dock.Right); iconLine.Children.Add(choose); iconLine.Children.Add(icon);
        var iconFields = new StackPanel(); iconFields.Children.Add(iconLine); iconFields.Children.Add(iconHint);
        var iconValidationError = false;
        void PreviewIcon()
        {
            var info = icon.Text == initialIcon ? AiIconCatalog.Stored(initialIcon) : AiIconCatalog.TryResolve(icon.Text, out var found) ? found : null;
            preview.IconId = info?.Id ?? "spark";
            iconHint.Text = info?.Label ?? (icon.Text == initialIcon ? "保留目前圖示；可選擇新的圖示。" : "找不到圖示，請檢查名稱或從圖庫選擇。");
            iconHint.Foreground = info != null || icon.Text == initialIcon ? ActionUi.Muted : Brushes.Firebrick;
            if (iconValidationError && (info != null || icon.Text == initialIcon)) { hint.Text = ""; iconValidationError = false; }
        }
        icon.TextChanged += (_, _) => PreviewIcon(); PreviewIcon();
        var cancel = ActionUi.Button("取消", Close); AutomationProperties.SetAutomationId(cancel, "AiPromptCancel"); buttons.Children.Add(cancel);
        var saveButton = ActionUi.Button("儲存指令", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { hint.Text = "請填寫指令名稱。"; name.Focus(); return; }
            if (string.IsNullOrWhiteSpace(instruction.Text)) { hint.Text = "請填寫指令內容。"; instruction.Focus(); return; }
            var iconId = initialIcon;
            if (icon.Text != initialIcon)
            {
                if (!AiIconCatalog.TryResolve(icon.Text, out var selected)) { iconValidationError = true; hint.Text = "找不到這個圖示，請檢查名稱或選擇圖示。"; icon.Focus(); return; }
                iconId = selected.Id;
            }
            var prompt = new AiPrompt(existing?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), instruction.Text,
                category.Text.Trim(), keywords.Text.Trim(), iconId);
            if (save(prompt) is { } error) { hint.Text = error; return; } Close();
        }, primary: true);
        AutomationProperties.SetAutomationId(saveButton, "AiPromptSave"); buttons.Children.Add(saveButton);
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        static void Field(Panel fields, string label, FrameworkElement input)
        {
            var text = ActionUi.Text(label, 12, ActionUi.Muted); text.Margin = new(0, 12, 0, 5);
            fields.Children.Add(text); fields.Children.Add(input);
        }
        var primary = new StackPanel(); var nameLabel = ActionUi.Text("名稱", 12, ActionUi.Muted); nameLabel.Margin = new(0, 0, 0, 5);
        primary.Children.Add(nameLabel); primary.Children.Add(name);
        var instructionLabel = ActionUi.Text("指令內容", 12, ActionUi.Muted); instructionLabel.Margin = new(0, 14, 0, 0); primary.Children.Add(instructionLabel);
        var help = ActionUi.Text("直接描述處理方式，選取文字會自動帶入。", 12, ActionUi.Muted); help.Margin = new(0, 6, 0, 8); primary.Children.Add(help);
        var optional = new StackPanel(); Field(optional, "分類（選填）", category); Field(optional, "搜尋關鍵字（選填）", keywords); Field(optional, "圖示", iconFields);
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
