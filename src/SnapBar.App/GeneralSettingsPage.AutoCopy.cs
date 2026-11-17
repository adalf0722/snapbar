using System.Windows;
using System.Windows.Controls;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed partial class GeneralSettingsPage
{
    internal void ConfigureAutoCopy(AutoCopySettings initial, Func<AutoCopySettings, bool> save)
    {
        var value = AutoCopySettings.Normalize(initial);
        var enabled = new CheckBox { Style = (Style)FindResource("Switch") };
        var mode = new ComboBox { MinWidth = 230, HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = new[] { UiText.Get("AutoCopy_ModeControl"), UiText.Get("AutoCopy_ModeSelection") } };
        var feedback = new CheckBox { Content = UiText.Get("AutoCopy_ShowFeedback"), Margin = new(0, 12, 0, 0) };
        var help = Description("");
        var error = Description(UiText.Get("AutoCopy_SaveFailed")); error.Visibility = Visibility.Collapsed;
        Identify(enabled, "AutoCopyEnabled", UiText.Get("AutoCopy_Title"));
        Identify(mode, "AutoCopyMode", UiText.Get("AutoCopy_ModeLabel"));
        Identify(feedback, "AutoCopyFeedback", UiText.Get("AutoCopy_ShowFeedback"));
        Identify(error, "AutoCopySaveError", UiText.Get("AutoCopy_SaveFailed"));
        var title = new DockPanel(); DockPanel.SetDock(enabled, Dock.Right); title.Children.Add(enabled);
        title.Children.Add(ActionUi.Heading(UiText.Get("AutoCopy_Title"), 16));
        var body = new StackPanel(); body.Children.Add(title);
        body.Children.Add(Description(UiText.Get("AutoCopy_Description")));
        body.Children.Add(mode); body.Children.Add(help); body.Children.Add(feedback); body.Children.Add(error);
        AddCard((StackPanel)scroll.Content, body);
        var updating = false;
        void Refresh()
        {
            updating = true;
            try
            {
                enabled.IsChecked = value.Enabled; mode.SelectedIndex = (int)value.Mode;
                feedback.IsChecked = value.ShowFeedback;
                mode.IsEnabled = feedback.IsEnabled = value.Enabled;
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
}
