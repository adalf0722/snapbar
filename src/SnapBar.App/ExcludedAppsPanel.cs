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

internal sealed class ExcludedAppsPanel : Expander
{
    private readonly ListBox applications;
    private readonly TextBlock selectedPath;
    private readonly Button remove;
    public event Action<ExcludedApplication>? AddRequested;
    public event Action<ExcludedApplication>? RemoveRequested;
    public event Action<string>? ValidationFailed;

    public ExcludedAppsPanel()
    {
        SetResourceReference(StyleProperty, typeof(Expander));
        Header = "排除 App（0）";
        AutomationProperties.SetAutomationId(this, "ExcludedAppsPanel");
        var panel = new StackPanel { Margin = new(0, 10, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "在這些 App 不會自動彈出；Ctrl+Alt+V 仍可使用。",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = ActionUi.Muted, Margin = new(0, 0, 0, 8) });
        applications = new ListBox { Height = 100, BorderBrush = ActionUi.Line, BorderThickness = new(1), DisplayMemberPath = nameof(ExcludedApplication.DisplayName) };
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(ExcludedApplication.DisplayName))));
        itemStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(ExcludedApplication.ExecutablePath))));
        applications.ItemContainerStyle = itemStyle;
        AutomationProperties.SetAutomationId(applications, "ExcludedAppsList");
        panel.Children.Add(applications);
        selectedPath = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Foreground = ActionUi.Muted, Margin = new(0, 6, 0, 8) };
        panel.Children.Add(selectedPath);
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
            remove.IsEnabled = selected != null;
        };
        buttons.Children.Add(add); buttons.Children.Add(remove);
        panel.Children.Add(buttons); Content = panel;
    }

    public void ShowApplications(IReadOnlyList<ExcludedApplication> entries)
    {
        Header = $"排除 App（{entries.Count}）";
        applications.ItemsSource = entries;
        applications.SelectedIndex = entries.Count > 0 ? 0 : -1;
        if (entries.Count == 0) selectedPath.Text = "尚未排除任何 App。";
    }
}
