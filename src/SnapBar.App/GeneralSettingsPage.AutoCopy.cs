using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed partial class GeneralSettingsPage
{
    internal void ConfigureAutoCopy(AutoCopySettings initial, Func<AutoCopySettings, bool> save)
    {
        var value = AutoCopySettings.Normalize(initial);
        var enabled = new CheckBox { Style = (Style)FindResource("Switch") };
        var mode = new ComboBox { MinWidth = 230, HorizontalAlignment = HorizontalAlignment.Right,
            ItemsSource = new[] { UiText.Get("AutoCopy_ModeControl"), UiText.Get("AutoCopy_ModeSelection") } };
        var feedback = new CheckBox { Content = UiText.Get("AutoCopy_ShowFeedback"), Margin = new(4, 8, 0, 8) };
        var help = Description("");
        var error = Description(UiText.Get("AutoCopy_SaveFailed")); error.Visibility = Visibility.Collapsed;
        Identify(enabled, "AutoCopyEnabled", UiText.Get("AutoCopy_Title"));
        Identify(mode, "AutoCopyMode", UiText.Get("AutoCopy_ModeLabel"));
        Identify(feedback, "AutoCopyFeedback", UiText.Get("AutoCopy_ShowFeedback"));
        Identify(error, "AutoCopySaveError", UiText.Get("AutoCopy_SaveFailed"));
        enabled.VerticalAlignment = VerticalAlignment.Center;
        var title = new DockPanel(); DockPanel.SetDock(enabled, Dock.Right); title.Children.Add(enabled);
        var caption = new StackPanel(); caption.Children.Add(ActionUi.Heading(UiText.Get("AutoCopy_Title"), 16));
        caption.Children.Add(Description(UiText.Get("General_AutoCopySummary"))); title.Children.Add(caption);
        var body = new StackPanel(); body.Children.Add(title);
        var options = new StackPanel { Margin = new(0, 10, 0, 0) };
        var modeRow = new DockPanel { Margin = new(0, 0, 0, 8) };
        DockPanel.SetDock(mode, Dock.Right); modeRow.Children.Add(mode);
        var modeLabel = Description(UiText.Get("General_Trigger"));
        AutomationProperties.SetLabeledBy(mode, modeLabel); modeRow.Children.Add(modeLabel);
        options.Children.Add(modeRow);
        help.Margin = new(4, 8, 4, 10);
        var instructions = Details("General_OperationHelp", "AutoCopyInstructions", help);
        var advanced = Details("General_AdvancedOptions", "AutoCopyAdvanced", feedback);
        options.Children.Add(instructions); options.Children.Add(advanced);
        body.Children.Add(options); body.Children.Add(error);
        selectionRows.Children.Add(Divider()); selectionRows.Children.Add(SettingRow(body));
        var updating = false;
        void Refresh()
        {
            updating = true;
            try
            {
                enabled.IsChecked = value.Enabled; mode.SelectedIndex = (int)value.Mode;
                feedback.IsChecked = value.ShowFeedback;
                mode.IsEnabled = feedback.IsEnabled = value.Enabled;
                options.Visibility = value.Enabled ? Visibility.Visible : Visibility.Collapsed;
                if (!value.Enabled) { instructions.IsExpanded = false; advanced.IsExpanded = false; }
                help.Text = UiText.Get(value.Mode == AutoCopyMode.RequireControl ? "AutoCopy_HelpControl" : "AutoCopy_HelpSelection");
            }
            finally { updating = false; }
        }
        void Change()
        {
            if (updating || UiText.IsRefreshing) return;
            var proposed = new AutoCopySettings(enabled.IsChecked == true,
                mode.SelectedIndex == 1 ? AutoCopyMode.OnSelection : AutoCopyMode.RequireControl, feedback.IsChecked == true);
            if (save(proposed)) { value = proposed; error.Visibility = Visibility.Collapsed; }
            else error.Visibility = Visibility.Visible;
            Refresh();
        }
        Refresh();
        enabled.Click += (_, _) => Change(); feedback.Click += (_, _) => Change();
        mode.SelectionChanged += (_, _) => Change();
    }

    private Expander Details(string titleKey, string id, UIElement content)
    {
        var details = new Expander { Header = UiText.Get(titleKey), Content = content,
            Style = (Style)FindResource("AiSettingsExpander"), FontSize = 13 };
        Identify(details, id, UiText.Get(titleKey));
        return details;
    }
}
