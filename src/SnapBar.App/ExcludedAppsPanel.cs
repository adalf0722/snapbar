using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class ExcludedAppsPanel : ContentControl
{
    private readonly ListBox applications;
    private readonly TextBlock selectedPath;
    private readonly Button remove;
    private readonly TextBlock heading;
    private readonly FrameworkElement emptyState;
    public event Action<ExcludedApplication>? AddRequested;
    public event Action<ExcludedApplication>? RemoveRequested;
    public event Action<string>? ValidationFailed;

    public ExcludedAppsPanel()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "ExcludedAppsPanel");
        var panel = new DockPanel(); var introduction = new StackPanel();
        heading = ActionUi.Heading(UiText.Get("ExcludedAppsPanel_000"), 16); introduction.Children.Add(heading);
        introduction.Children.Add(new TextBlock { Text = UiText.Get("ExcludedAppsPanel_001"),
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = ActionUi.Muted, Margin = new(0, 4, 0, 8) });
        DockPanel.SetDock(introduction, Dock.Top); panel.Children.Add(introduction);
        applications = new ListBox { BorderBrush = ActionUi.Line, BorderThickness = new(1), DisplayMemberPath = nameof(ExcludedApplication.DisplayName) };
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(ExcludedApplication.DisplayName))));
        itemStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(ExcludedApplication.ExecutablePath))));
        applications.ItemContainerStyle = itemStyle;
        AutomationProperties.SetAutomationId(applications, "ExcludedAppsList");
        var footer = new StackPanel();
        selectedPath = new TextBlock { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12,
            Foreground = ActionUi.Muted, Margin = new(0, 6, 0, 8) }; footer.Children.Add(selectedPath);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var add = ActionUi.Button(UiText.Get("ExcludedAppsPanel_002"), () => { });
        AutomationProperties.SetAutomationId(add, "AddExcludedApp");
        add.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = UiText.Get("ExcludedAppsPanel_003"), Filter = UiText.Get("ExcludedAppsPanel_004"),
                CheckFileExists = true, Multiselect = false };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            var executablePath = AppExclusionPolicy.NormalizePath(picker.FileName);
            if (executablePath == null)
            { ValidationFailed?.Invoke(UiText.Get("ExcludedAppsPanel_005")); return; }
            var name = Path.GetFileNameWithoutExtension(executablePath);
            try
            {
                var description = FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
            AddRequested?.Invoke(AppExclusionPolicy.Create(executablePath, name));
        };
        remove = ActionUi.Button(UiText.Get("ExcludedAppsPanel_006"), () => { }); remove.IsEnabled = false;
        AutomationProperties.SetAutomationId(remove, "RemoveExcludedApp");
        remove.Click += (_, _) =>
        {
            if (applications.SelectedItem is ExcludedApplication app) RemoveRequested?.Invoke(app);
        };
        applications.SelectionChanged += (_, _) =>
        {
            var selected = applications.SelectedItem as ExcludedApplication;
            selectedPath.Text = selected?.ExecutablePath ?? UiText.Get("ExcludedAppsPanel_007");
            selectedPath.ToolTip = selectedPath.Text;
            remove.IsEnabled = selected != null;
        };
        buttons.Children.Add(add); buttons.Children.Add(remove);
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var listHost = new Grid(); listHost.Children.Add(applications);
        var emptyText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(16) };
        emptyText.Children.Add(ActionUi.Heading(UiText.Get("ExcludedAppsPanel_008"), 14));
        var emptyHelp = ActionUi.Text(UiText.Get("ExcludedAppsPanel_009"), 12, ActionUi.Muted); emptyHelp.Margin = new(0, 6, 0, 0); emptyText.Children.Add(emptyHelp);
        emptyState = emptyText; emptyText.IsHitTestVisible = false; AutomationProperties.SetAutomationId(emptyText, "ExcludedAppsEmptyState");
        listHost.Children.Add(emptyText); panel.Children.Add(listHost); Content = panel;
    }

    public void ShowApplications(IReadOnlyList<ExcludedApplication> entries)
    {
        heading.Text = UiText.Get("ExcludedAppsPanel_010",(entries.Count));
        applications.ItemsSource = entries;
        emptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        applications.SelectedIndex = entries.Count > 0 ? 0 : -1;
        selectedPath.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (entries.Count == 0) selectedPath.Text = "";
    }
}
