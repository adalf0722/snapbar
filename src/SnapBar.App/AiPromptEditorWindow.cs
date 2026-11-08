using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class AiPromptEditorWindow : Window, ISettingsDraft
{
    private readonly UiLanguage language = UiText.Capture();
    private Func<bool> saveDraft = () => false;
    private Func<bool> dirty = () => false;
    public string DraftName => language.Get("AiPromptEditorWindow_000");
    public bool IsDirty => dirty();
    public bool TrySave() => saveDraft();
    public void Discard() => Close();
    public void Reveal() { Show(); Activate(); }
    public AiPromptEditorWindow(AiPrompt? existing, Func<AiPrompt, string?> save)
    {
        ActionUi.Setup(this, existing == null ? language.Get("AiPromptEditorWindow_001") : language.Get("AiPromptEditorWindow_002"), 570, 610); MinWidth = 460;
        var body = new DockPanel { Margin = new(24) };
        var title = ActionUi.Heading(existing == null ? language.Get("AiPromptEditorWindow_003") : language.Get("AiPromptEditorWindow_004"), 22); title.Margin = new(0, 0, 0, 18);
        DockPanel.SetDock(title, Dock.Top); body.Children.Add(title);
        var footer = new StackPanel { Margin = new(0, 16, 0, 0) };
        var draftStatus = ActionUi.Text("", 12, ActionUi.Muted); footer.Children.Add(draftStatus);
        AutomationProperties.SetAutomationId(draftStatus, "AiPromptDirtyStatus");
        var hint = ActionUi.Text("", 12, Brushes.Firebrick); footer.Children.Add(hint);
        AutomationProperties.SetAutomationId(hint, "AiPromptError");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var initialName = existing == null ? "" : UiNames.Prompt(existing, language);
        var initialCategory = existing == null ? language.Get("AiPromptEditorWindow_005") : UiNames.Category(existing, language);
        var name = ActionUi.Input(initialName, 64); var category = ActionUi.Input(initialCategory, 32);
        var keywords = ActionUi.Input(existing?.Keywords ?? "", 256); var instruction = ActionUi.Input(existing?.Instruction ?? "", 8192);
        instruction.AcceptsReturn = true; instruction.TextWrapping = TextWrapping.Wrap; instruction.MinHeight = 140;
        instruction.VerticalContentAlignment = VerticalAlignment.Top; instruction.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        AutomationProperties.SetAutomationId(name, "AiPromptName"); AutomationProperties.SetName(name, language.Get("AiPromptEditorWindow_006"));
        AutomationProperties.SetAutomationId(instruction, "AiPromptInstruction"); AutomationProperties.SetName(instruction, language.Get("AiPromptEditorWindow_007"));
        AutomationProperties.SetAutomationId(category, "AiPromptCategory"); AutomationProperties.SetName(category, language.Get("AiPromptEditorWindow_008"));
        AutomationProperties.SetAutomationId(keywords, "AiPromptKeywords"); AutomationProperties.SetName(keywords, language.Get("AiPromptEditorWindow_009"));
        var initialIcon = existing == null ? "spark" : AiIconCatalog.ForPrompt(existing);
        var icon = ActionUi.Input(initialIcon, 128); icon.FontSize = 12;
        AutomationProperties.SetAutomationId(icon, "AiPromptIcon"); AutomationProperties.SetName(icon, language.Get("AiPromptEditorWindow_010"));
        icon.ToolTip = language.Get("AiPromptEditorWindow_011");
        var preview = new AiIconPresenter { IconId = initialIcon, Width = 24, Height = 24, Margin = new(0, 0, 10, 0) };
        AutomationProperties.SetAutomationId(preview, "AiPromptIconPreview");
        var iconHint = ActionUi.Text("", 12, ActionUi.Muted); iconHint.Margin = new(0, 5, 0, 0);
        AutomationProperties.SetAutomationId(iconHint, "AiPromptIconHint");
        var choose = ActionUi.Button(language.Get("AiPromptEditorWindow_012"), () =>
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
            iconHint.Text = info != null ? UiText.Current(info.Label, language.Culture) : (icon.Text == initialIcon ? language.Get("AiPromptEditorWindow_013") : language.Get("AiPromptEditorWindow_014"));
            iconHint.Foreground = info != null || icon.Text == initialIcon ? ActionUi.Muted : Brushes.Firebrick;
            if (iconValidationError && (info != null || icon.Text == initialIcon)) { hint.Text = ""; iconValidationError = false; }
        }
        icon.TextChanged += (_, _) => PreviewIcon(); PreviewIcon();
        var cancel = ActionUi.Button(language.Get("AiPromptEditorWindow_015"), Close); AutomationProperties.SetAutomationId(cancel, "AiPromptCancel"); buttons.Children.Add(cancel);
        bool SaveDraft()
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { hint.Text = language.Get("AiPromptEditorWindow_016"); name.Focus(); return false; }
            if (string.IsNullOrWhiteSpace(instruction.Text)) { hint.Text = language.Get("AiPromptEditorWindow_017"); instruction.Focus(); return false; }
            var iconId = initialIcon;
            if (icon.Text != initialIcon)
            {
                if (!AiIconCatalog.TryResolve(icon.Text, out var selected)) { iconValidationError = true; hint.Text = language.Get("AiPromptEditorWindow_018"); icon.Focus(); return false; }
                iconId = selected.Id;
            }
            var prompt = new AiPrompt(existing?.Id ?? Guid.NewGuid().ToString("N"), UiNames.KeepOriginalName(name.Text.Trim(), initialName, existing?.Name), instruction.Text,
                UiNames.KeepOriginalName(category.Text.Trim(), initialCategory, existing?.Category), keywords.Text.Trim(), iconId);
            if (language.Run(() => save(prompt)) is { } error) { hint.Text = UiText.Current(error, language.Culture); return false; }
            dirty = () => false; Close(); return true;
        }
        saveDraft = SaveDraft;
        dirty = () => name.Text != initialName || instruction.Text != (existing?.Instruction ?? "")
            || category.Text != initialCategory || keywords.Text != (existing?.Keywords ?? "") || icon.Text != initialIcon;
        foreach (var field in new[] { name, instruction, category, keywords, icon })
            field.TextChanged += (_, _) => draftStatus.Text = IsDirty ? language.Get("AiPromptEditorWindow_020") : "";
        var saveButton = ActionUi.Button(language.Get("AiPromptEditorWindow_021"), () => TrySave(), primary: true);
        AutomationProperties.SetAutomationId(saveButton, "AiPromptSave"); buttons.Children.Add(saveButton);
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        static void Field(Panel fields, string label, FrameworkElement input)
        {
            var text = ActionUi.Text(label, 12, ActionUi.Muted); text.Margin = new(0, 12, 0, 5);
            fields.Children.Add(text); fields.Children.Add(input);
        }
        var primary = new StackPanel(); var nameLabel = ActionUi.Text(language.Get("AiPromptEditorWindow_022"), 12, ActionUi.Muted); nameLabel.Margin = new(0, 0, 0, 5);
        primary.Children.Add(nameLabel); primary.Children.Add(name);
        var instructionLabel = ActionUi.Text(language.Get("AiPromptEditorWindow_023"), 12, ActionUi.Muted); instructionLabel.Margin = new(0, 14, 0, 0); primary.Children.Add(instructionLabel);
        var help = ActionUi.Text(language.Get("AiPromptEditorWindow_024"), 12, ActionUi.Muted); help.Margin = new(0, 6, 0, 8); primary.Children.Add(help);
        var optional = new StackPanel(); Field(optional, language.Get("AiPromptEditorWindow_025"), category); Field(optional, language.Get("AiPromptEditorWindow_026"), keywords); Field(optional, language.Get("AiPromptEditorWindow_027"), iconFields);
        var advanced = new Expander { Header = language.Get("AiPromptEditorWindow_028"), Content = optional, Margin = new(0, 12, 0, 0), IsExpanded = false };
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
        Closed += (_, _) => dirty = () => false;
        WindowWorkArea.Attach(this);
    }
}
