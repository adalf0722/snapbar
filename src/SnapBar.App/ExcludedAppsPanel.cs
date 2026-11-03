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
        heading = ActionUi.Heading("排除 App（0）", 16); introduction.Children.Add(heading);
        introduction.Children.Add(new TextBlock { Text = "在這些 App 不會自動彈出；手動快捷鍵仍可使用。",
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
        var add = ActionUi.Button("加入 App…", () => { });
        AutomationProperties.SetAutomationId(add, "AddExcludedApp");
        add.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "選擇要排除的 App", Filter = "應用程式 (*.exe)|*.exe",
                CheckFileExists = true, Multiselect = false };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            var executablePath = AppExclusionPolicy.NormalizePath(picker.FileName);
            if (executablePath == null)
            { ValidationFailed?.Invoke("請選擇有效的 .exe 程式檔案，排除清單未變更。"); return; }
            var name = Path.GetFileNameWithoutExtension(executablePath);
            try
            {
                var description = FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
            AddRequested?.Invoke(AppExclusionPolicy.Create(executablePath, name));
        };
        remove = ActionUi.Button("移除選取項目", () => { }); remove.IsEnabled = false;
        AutomationProperties.SetAutomationId(remove, "RemoveExcludedApp");
        remove.Click += (_, _) =>
        {
            if (applications.SelectedItem is ExcludedApplication app) RemoveRequested?.Invoke(app);
        };
        applications.SelectionChanged += (_, _) =>
        {
            var selected = applications.SelectedItem as ExcludedApplication;
            selectedPath.Text = selected?.ExecutablePath ?? "尚未排除任何 App。";
            selectedPath.ToolTip = selectedPath.Text;
            remove.IsEnabled = selected != null;
        };
        buttons.Children.Add(add); buttons.Children.Add(remove);
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var listHost = new Grid(); listHost.Children.Add(applications);
        var emptyText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(16) };
        emptyText.Children.Add(ActionUi.Heading("尚未排除任何 App", 14));
        var emptyHelp = ActionUi.Text("按「加入 App…」選擇不需要自動顯示動作列的程式。", 12, ActionUi.Muted); emptyHelp.Margin = new(0, 6, 0, 0); emptyText.Children.Add(emptyHelp);
        emptyState = emptyText; emptyText.IsHitTestVisible = false; AutomationProperties.SetAutomationId(emptyText, "ExcludedAppsEmptyState");
        listHost.Children.Add(emptyText); panel.Children.Add(listHost); Content = panel;
    }

    public void ShowApplications(IReadOnlyList<ExcludedApplication> entries)
    {
        heading.Text = $"排除 App（{entries.Count}）";
        applications.ItemsSource = entries;
        emptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        applications.SelectedIndex = entries.Count > 0 ? 0 : -1;
        selectedPath.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (entries.Count == 0) selectedPath.Text = "";
    }
}
