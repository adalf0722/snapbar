using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed class SettingsSupportPage : ContentControl
{
    internal TextBlock Status { get; }
    internal ListBox Events { get; }
    internal SettingsSupportPage(Action exit, Action? getStarted = null)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var body = new DockPanel(); var exitButton = ActionUi.Button(UiText.Get("SettingsSupportPage_000"), exit);
        exitButton.HorizontalAlignment = HorizontalAlignment.Left; exitButton.Margin = new(0, 12, 0, 0); AutomationProperties.SetAutomationId(exitButton, "ExitSnapBar");
        DockPanel.SetDock(exitButton, Dock.Bottom); body.Children.Add(exitButton);
        var content = new DockPanel(); var heading = new StackPanel();
        var identity = new DockPanel { LastChildFill = true };
        var brandIcon = ActionUi.BrandIcon(48); brandIcon.Margin = new(0, 0, 14, 0);
        DockPanel.SetDock(brandIcon, Dock.Left); identity.Children.Add(brandIcon);
        var product = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        product.Children.Add(ActionUi.Heading("SnapBar", 22));
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? UiText.Get("SettingsSupportPage_001");
        ProductVersion? productIdentity = null;
        try { productIdentity = ProductVersion.Parse(version.Split('+')[0]); } catch (FormatException) { }
        var versionLabel = ActionUi.Text(UiText.Join(UiText.Get("SettingsSupportPage_002"), productIdentity?.Version ?? UiText.Get("SettingsSupportPage_001")), 14, ActionUi.Muted);
        AutomationProperties.SetAutomationId(versionLabel, "SettingsProductVersion");
        versionLabel.ToolTip = version; product.Children.Add(versionLabel);
        if (productIdentity != null)
        {
            var channel = ActionUi.Text(UiText.Get("VersionChannel_" + productIdentity.Channel), 12, ActionUi.Muted);
            AutomationProperties.SetAutomationId(channel, "SettingsVersionChannel"); product.Children.Add(channel);
        }
        identity.Children.Add(product); heading.Children.Add(identity);
        var lifetime = ActionUi.Text(UiText.Get("SettingsSupportPage_003"), 13, ActionUi.Muted); lifetime.Margin = new(0, 16, 0, 16); heading.Children.Add(lifetime);
        if (getStarted != null)
        {
            var tutorial = ActionUi.Button(UiText.Get("Onboarding_Entry"), getStarted);
            tutorial.HorizontalAlignment = HorizontalAlignment.Left; tutorial.Margin = new(0, 0, 0, 16);
            AutomationProperties.SetAutomationId(tutorial, "OpenOnboarding"); heading.Children.Add(tutorial);
        }
        DockPanel.SetDock(heading, Dock.Top); content.Children.Add(heading);
        var diagnostics = new DockPanel();
        var tools = new WrapPanel { Margin = new(0, 8, 0, 0) };
        var export = ActionUi.Button(UiText.Get("Support_Export"), PreviewReport);
        AutomationProperties.SetAutomationId(export, "ExportDiagnostics");
        export.Margin = new(0, 0, 8, 4);
        var record = ActionUi.Button(UiText.Get("Support_Record"), () => { });
        AutomationProperties.SetAutomationId(record, "RecordDiagnostics");
        record.Click += async (_, _) =>
        {
            var contentBeforeRecording = record.Content;
            record.IsEnabled = false;
            record.Content = UiText.Get("Support_Recording");
            await SelectionDiagnostics.Shared.RecordReproductionAsync(SupportSession.Local, TimeSpan.FromMinutes(2));
            record.Content = contentBeforeRecording;
            UiText.Refresh(record);
            record.IsEnabled = true;
        };
        tools.Children.Add(export);
        tools.Children.Add(record);
        var fixture = ActionUi.Button(UiText.Get("SettingsSupportPage_004"), () =>
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add("--fixture"); Process.Start(start);
        }, "document"); fixture.HorizontalAlignment = HorizontalAlignment.Left; fixture.Margin = new(0, 8, 0, 0);
        DockPanel.SetDock(fixture, Dock.Bottom); diagnostics.Children.Add(fixture);
        DockPanel.SetDock(tools, Dock.Bottom); diagnostics.Children.Add(tools);
        Status = ActionUi.Text(UiText.Get("SettingsSupportPage_005"), 12, ActionUi.Muted); AutomationProperties.SetAutomationId(Status, "LatestDiagnosticStatus"); DockPanel.SetDock(Status, Dock.Top); diagnostics.Children.Add(Status);
        var build = ActionUi.Text(UiText.Join(UiText.Get("SettingsSupportPage_006"), version), 12, ActionUi.Muted); build.Margin = new(0, 8, 0, 0);
        AutomationProperties.SetAutomationId(build, "SettingsBuildVersion"); DockPanel.SetDock(build, Dock.Top); diagnostics.Children.Add(build);
        if (SupportSession.Local.PreviousInterrupted)
        {
            var interrupted = ActionUi.Text(UiText.Get("Support_Interrupted"), 12, ActionUi.Muted);
            DockPanel.SetDock(interrupted, Dock.Top);
            diagnostics.Children.Add(interrupted);
        }
        Events = new ListBox { BorderThickness = new(0), Background = ActionUi.Canvas, FontSize = 12, Foreground = ActionUi.Muted, Margin = new(0, 8, 0, 0), MaxHeight = 240 };
        diagnostics.Children.Add(Events);
        var advanced = new Expander { Header = UiText.Get("SettingsSupportPage_007"), Content = diagnostics,
            IsExpanded = SupportSession.Local.PreviousInterrupted, VerticalContentAlignment = VerticalAlignment.Stretch };
        content.Children.Add(advanced); body.Children.Add(content); Content = body;
        SizeChanged += (_, _) => Events.MaxHeight = Math.Clamp(ActualHeight - heading.ActualHeight - exitButton.DesiredSize.Height - 110, 20, 240);
    }

    private void PreviewReport()
    {
        var report = SupportSession.Local.CreateReport(SelectionDiagnostics.Shared);
        var preview = new Window
        {
            Owner = Window.GetWindow(this),
            Title = UiText.Get("Support_Export"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 640,
            Height = 470,
            ShowInTaskbar = false
        };
        var body = new DockPanel { Margin = new(16) };
        var save = ActionUi.Button(UiText.Get("Support_Save"), () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "SnapBar-diagnostics.json", DefaultExt = ".json",
                Filter = "JSON (*.json)|*.json" };
            if (dialog.ShowDialog(preview) != true) return;
            try { File.WriteAllText(dialog.FileName, report); preview.Close(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { MessageBox.Show(preview, UiText.Get("Support_SaveFailed"), preview.Title); }
        });
        save.HorizontalAlignment = HorizontalAlignment.Right;
        save.Margin = new(0, 8, 0, 0);
        DockPanel.SetDock(save, Dock.Bottom);
        body.Children.Add(save);
        var explanation = ActionUi.Text(UiText.Get("Support_Preview"), 13, ActionUi.Muted);
        explanation.Margin = new(0, 0, 0, 8);
        DockPanel.SetDock(explanation, Dock.Top);
        body.Children.Add(explanation);
        body.Children.Add(new TextBox { Text = report, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        preview.Content = body;
        preview.ShowDialog();
    }
}
