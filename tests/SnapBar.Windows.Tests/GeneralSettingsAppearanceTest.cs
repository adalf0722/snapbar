using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class GeneralSettingsAppearanceTest
{
    private static IEnumerable<T> Controls<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var match in Controls<T>(child)) yield return match;
    }

    internal static void Run(Action<bool, string> check)
    {
        foreach (var language in new[] { "zh-Hant", "en" })
        {
            UiText.WithCulture(System.Globalization.CultureInfo.GetCultureInfo(language), () =>
            {
                var saved = new AutoCopySettings(true, AutoCopyMode.RequireControl, false);
                var page = new SelectionSettingsPage(true, () => { });
                page.ConfigureAppearanceDelay(120, _ => true);
                page.ConfigureAutoCopy(saved, value => { saved = value; return true; });
                var shell = new Grid { Margin = new(28, 72, 28, 44) };
                shell.Children.Add(page);
                var window = new Window { Content = shell, ShowInTaskbar = false };
                ActionUi.Setup(window, "General settings preview", 760, 720);
                try
                {
                    window.Show(); window.UpdateLayout();
                    var scroll = Controls<ScrollViewer>(page).Single();
                    var details = Controls<Expander>(page).ToArray();
                    var mode = Controls<ComboBox>(page).Single(c => AutomationProperties.GetAutomationId(c) == "AutoCopyMode");
                    var enabled = Controls<CheckBox>(page).Single(c => AutomationProperties.GetAutomationId(c) == "AutoCopyEnabled");
                    check(details.Length == 3 && details.All(e => !e.IsExpanded), language + " instructions and advanced options start collapsed");
                    UiAppearanceTest.Snapshot(window, "selection-settings-" + language + ".png");
                    check(scroll.ScrollableHeight < 1 && mode.IsVisible, language + $" enabled selection settings fit the standard content area (extent={scroll.ExtentHeight}, viewport={scroll.ViewportHeight})");
                    foreach (var detail in details) detail.IsExpanded = true;
                    window.UpdateLayout();
                    check(Controls<CheckBox>(page).Single(c => AutomationProperties.GetAutomationId(c) == "AutoCopyFeedback").IsVisible,
                        language + " advanced feedback preference is available on demand");
                    UiAppearanceTest.Snapshot(window, "selection-settings-expanded-" + language + ".png");
                    enabled.IsChecked = false; enabled.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    window.UpdateLayout();
                    check(!mode.IsVisible && !saved.Enabled && !saved.ShowFeedback && saved.Mode == AutoCopyMode.RequireControl,
                        language + " disabling hides related controls and preserves preferences");
                    enabled.IsChecked = true; enabled.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    window.UpdateLayout();
                    check(mode.IsVisible && details.Where(e => AutomationProperties.GetAutomationId(e) != "ToolbarAppearanceDelayDetails").All(e => !e.IsExpanded) && !saved.ShowFeedback,
                        language + " re-enabling restores preferences with details collapsed");
                    window.Height = 480;
                    foreach (var detail in details) detail.IsExpanded = true;
                    window.UpdateLayout(); scroll.ScrollToBottom(); window.UpdateLayout();
                    check(scroll.ScrollableHeight > 0 && scroll.VerticalOffset > 0,
                        language + " small windows retain scrolling for expanded content");
                    UiAppearanceTest.Snapshot(window, "selection-settings-small-" + language + ".png");
                }
                finally { window.Close(); }
                return true;
            });
        }
    }
}
