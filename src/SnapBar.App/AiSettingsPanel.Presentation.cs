using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed partial class AiSettingsPanel
{
    private void InitializePresentation(Action<Uri> launch, IAiTextClient? testClient)
    {
        var body = new Grid();
        body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var footer = new StackPanel { Margin = new(0, 12, 0, 0) };
        hint = ActionUi.Text("", 12, Brushes.Firebrick);
        AutomationProperties.SetAutomationId(hint, "AiSettingsError");
        var errorStyle = new Style(typeof(TextBlock));
        var emptyError = new Trigger { Property = TextBlock.TextProperty, Value = "" };
        emptyError.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        errorStyle.Triggers.Add(emptyError); hint.Style = errorStyle;
        footer.Children.Add(hint);
        var footerRow = new DockPanel();
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        discard = ActionUi.Button(Text("AiSettingsPanel_Presentation_000"), Discard);
        AutomationProperties.SetAutomationId(discard, "DiscardAiSettings"); buttons.Children.Add(discard);
        save = ActionUi.Button(Text("AiSettingsPanel_Presentation_001"), () => TrySave(), primary: true);
        save.Margin = new(0);
        AutomationProperties.SetAutomationId(save, "SaveAiSettings"); buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Right); footerRow.Children.Add(buttons);
        state = ActionUi.Text("", 12, ActionUi.Muted);
        AutomationProperties.SetAutomationId(state, "AiDirtyStatus"); footerRow.Children.Add(state);
        footer.Children.Add(footerRow);
        var footerBorder = new Border { BorderBrush = ActionUi.Line, BorderThickness = new(0, 1, 0, 0),
            Margin = new(0, 16, 0, 0), Child = footer };
        KeyboardNavigation.SetTabIndex(footerBorder, 1);
        KeyboardNavigation.SetTabNavigation(footerBorder, KeyboardNavigationMode.Local);
        Grid.SetRow(footerBorder, 1);

        var content = new StackPanel { Margin = new(0, 0, 8, 0) };
        var introduction = ActionUi.Text(Text("AiSettingsPanel_Presentation_002"), 13, ActionUi.Muted);
        introduction.Margin = new(0, 0, 0, 14); content.Children.Add(introduction);
        var fields = new StackPanel();
        fields.Children.Add(FieldHeader(Text("AiSettingsPanel_Presentation_003")));
        profiles = new ComboBox { ItemsSource = drafts.Select(p => UiNames.Provider(p, language)).ToArray(), MinHeight = 40 };
        AutomationProperties.SetAutomationId(profiles, "AiProvider"); AutomationProperties.SetName(profiles, Text("AiSettingsPanel_Presentation_004")); fields.Children.Add(profiles);

        keyLink = ProviderLink(Text("AiSettingsPanel_Presentation_005"), "AiKeyLink", false, launch);
        fields.Children.Add(FieldHeader("API Key", keyLink, spaced: true));
        key = new PasswordBox { MaxLength = 8192, Padding = new(12, 9, 12, 9), MinHeight = 40 };
        AutomationProperties.SetAutomationId(key, "AiApiKey"); AutomationProperties.SetName(key, Text("AiSettingsPanel_Presentation_006"));
        key.ToolTip = Text("AiSettingsPanel_Presentation_007");
        var keyEditor = new Grid(); keyEditor.Children.Add(key);
        keyPlaceholder = ActionUi.Text(Text("AiSettingsPanel_Presentation_008"), 13, ActionUi.Muted);
        keyPlaceholder.Margin = new(12, 0, 12, 0); keyPlaceholder.IsHitTestVisible = false;
        keyEditor.Children.Add(keyPlaceholder); fields.Children.Add(keyEditor);
        var keyRow = new DockPanel { Margin = new(0, 6, 0, 0) };
        replaceKey = TextLink(Text("AiSettingsPanel_Presentation_009"), "AiReplaceKey", () =>
        {
            editingKeyScope = Scope(CurrentDraft()); ShowKeyStatus(); key.Focus();
        });
        DockPanel.SetDock(replaceKey, Dock.Right); keyRow.Children.Add(replaceKey);
        keyStatus = ActionUi.Text("", 13, ActionUi.Muted);
        AutomationProperties.SetAutomationId(keyStatus, "AiKeyStatus"); keyRow.Children.Add(keyStatus); fields.Children.Add(keyRow);
        keyHelp = ActionUi.Text("", 12, ActionUi.Muted); keyHelp.Margin = new(0, 6, 0, 0); fields.Children.Add(keyHelp);

        modelsLink = ProviderLink(Text("AiSettingsPanel_Presentation_010"), "AiModelsLink", true, launch);
        fields.Children.Add(FieldHeader(Text("AiSettingsPanel_Presentation_011"), modelsLink, spaced: true));
        model = new ComboBox { IsEditable = true, MinHeight = 40, SelectedValuePath = "Id" };
        TextSearch.SetTextPath(model, "Id");
        var item = new FrameworkElementFactory(typeof(TextBlock));
        item.SetBinding(TextBlock.TextProperty, new MultiBinding { StringFormat = "{0}  ·  {1}", Bindings = { new Binding("Name"), new Binding("Id") } });
        model.ItemTemplate = new DataTemplate { VisualTree = item };
        model.Loaded += (_, _) =>
        {
            model.ApplyTemplate();
            if (model.Template.FindName("PART_EditableTextBox", model) is TextBox editor) editor.MaxLength = 128;
        };
        AutomationProperties.SetAutomationId(model, "AiModel"); AutomationProperties.SetName(model, Text("AiSettingsPanel_Presentation_012")); fields.Children.Add(model);
        var modelHelp = ActionUi.Text(Text("AiSettingsPanel_Presentation_013"), 12, ActionUi.Muted);
        modelHelp.Margin = new(0, 4, 0, 0); fields.Children.Add(modelHelp);
        customEndpointNotice = ActionUi.Text(Text("AiSettingsPanel_Presentation_014"), 12, ActionUi.Muted);
        customEndpointNotice.Margin = new(0, 8, 0, 0);
        AutomationProperties.SetAutomationId(customEndpointNotice, "AiCustomEndpointNotice"); fields.Children.Add(customEndpointNotice);

        advanced = new Expander { Header = Text("AiSettingsPanel_Presentation_015"), Margin = new(0, 12, 0, 0),
            Style = (Style)FindResource("AiSettingsExpander") };
        AutomationProperties.SetAutomationId(advanced, "AiAdvancedSettings");
        var advancedFields = new StackPanel { Margin = new(0, 12, 0, 0) };
        advancedFields.Children.Add(FieldHeader(Text("AiSettingsPanel_Presentation_016")));
        endpoint = ActionUi.Input("", 2048); endpoint.MinHeight = 40;
        AutomationProperties.SetAutomationId(endpoint, "AiEndpoint"); AutomationProperties.SetName(endpoint, Text("AiSettingsPanel_Presentation_017")); advancedFields.Children.Add(endpoint);
        protocolHint = ActionUi.Text("", 12, ActionUi.Muted); protocolHint.Margin = new(0, 6, 0, 0); advancedFields.Children.Add(protocolHint);
        removeKey = new CheckBox { Content = ActionUi.Text(Text("AiSettingsPanel_Presentation_018"), 13), Margin = new(0, 12, 0, 0) };
        AutomationProperties.SetAutomationId(removeKey, "AiRemoveKey");
        AutomationProperties.SetName(removeKey, Text("AiSettingsPanel_Presentation_019")); advancedFields.Children.Add(removeKey);
        advanced.Content = advancedFields; fields.Children.Add(advanced);
        content.Children.Add(ActionUi.Card(fields, new(24, 16, 24, 16)));
        InitializeTesting(content, testClient);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        KeyboardNavigation.SetTabIndex(scroll, 0);
        KeyboardNavigation.SetTabNavigation(scroll, KeyboardNavigationMode.Local);
        body.Children.Add(scroll);
        body.Children.Add(footerBorder);
        Content = body;
    }

    private static DockPanel FieldHeader(string label, Button? link = null, bool spaced = false)
    {
        var row = new DockPanel { Margin = new(0, spaced ? 16 : 0, 0, 8) };
        if (link != null) { DockPanel.SetDock(link, Dock.Right); row.Children.Add(link); }
        var title = ActionUi.Heading(label, 14); title.Margin = new(0, 0, 16, 0); row.Children.Add(title);
        return row;
    }

    private Button TextLink(string title, string id, Action click)
    {
        var button = new Button { Content = title, Style = (Style)FindResource("AiSettingsLink") };
        button.Click += (_, _) => click();
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, title);
        return button;
    }

    private Button ProviderLink(string title, string id, bool models, Action<Uri> launch) => TextLink(title, id, () =>
    {
        var definition = AiProviderCatalog.Find(drafts[selected].Id);
        var url = models ? definition?.ModelsUrl : definition?.KeyUrl;
        if (url == null) return;
        try { launch(new Uri(url)); }
        catch { hint.Text = Text("AiSettingsPanel_Presentation_020"); }
    });

    private void ShowKeyStatus()
    {
        if (selected < 0) return;
        var profile = CurrentDraft();
        var removing = removeKey.IsChecked == true;
        var pending = key.Password.Length > 0;
        var stored = false;
        var unavailable = false;
        if (!pending && !removing)
        {
            try { stored = preferences.Secrets.Get(profile) != null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException or ArgumentException
                or InvalidOperationException or System.ComponentModel.Win32Exception)
            { unavailable = true; }
        }
        key.Visibility = !removing && (!stored || editingKeyScope == Scope(profile)) ? Visibility.Visible : Visibility.Collapsed;
        keyPlaceholder.Visibility = key.Visibility == Visibility.Visible && !pending ? Visibility.Visible : Visibility.Collapsed;
        replaceKey.Visibility = stored && key.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        keyStatus.Text = removing ? Text("AiSettingsPanel_Presentation_021") : pending ? Text("AiSettingsPanel_Presentation_022")
            : unavailable ? Text("AiSettingsPanel_Presentation_023") : stored ? Text("AiSettingsPanel_Presentation_024") : Text("AiSettingsPanel_Presentation_025");
        keyStatus.Visibility = removing || pending || unavailable || stored ? Visibility.Visible : Visibility.Collapsed;
        keyStatus.Foreground = removing || pending ? ActionUi.Ink : ActionUi.Muted;
        keyHelp.Text = removing ? Text("AiSettingsPanel_Presentation_026")
            : stored ? Text("AiSettingsPanel_Presentation_027")
            : profile.Id == "compatible" ? Text("AiSettingsPanel_Presentation_028") : Text("AiSettingsPanel_Presentation_029");
    }
}
