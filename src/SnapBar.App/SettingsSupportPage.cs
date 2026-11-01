using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace SnapBar.App;

internal sealed class SettingsSupportPage : ContentControl
{
    internal TextBlock Status { get; }
    internal ListBox Events { get; }
    internal SettingsSupportPage(Action exit)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var body = new DockPanel(); var exitButton = ActionUi.Button("結束 SnapBar", exit);
        exitButton.HorizontalAlignment = HorizontalAlignment.Left; exitButton.Margin = new(0, 12, 0, 0); AutomationProperties.SetAutomationId(exitButton, "ExitSnapBar");
        DockPanel.SetDock(exitButton, Dock.Bottom); body.Children.Add(exitButton);
        var content = new DockPanel(); var heading = new StackPanel(); heading.Children.Add(ActionUi.Heading("SnapBar", 22));
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知版本";
        var versionLabel = ActionUi.Text("版本 " + version.Split('+')[0], 14, ActionUi.Muted); versionLabel.ToolTip = version; heading.Children.Add(versionLabel);
        var lifetime = ActionUi.Text("關閉視窗後會繼續在背景待命。使用系統匣圖示可返回設定；「結束 SnapBar」才會完整結束。", 13, ActionUi.Muted); lifetime.Margin = new(0, 16, 0, 16); heading.Children.Add(lifetime);
        DockPanel.SetDock(heading, Dock.Top); content.Children.Add(heading);
        var diagnostics = new DockPanel();
        var fixture = ActionUi.Button("開啟測試視窗", () =>
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add("--fixture"); Process.Start(start);
        }, "document"); fixture.HorizontalAlignment = HorizontalAlignment.Left; fixture.Margin = new(0, 8, 0, 0);
        DockPanel.SetDock(fixture, Dock.Bottom); diagnostics.Children.Add(fixture);
        Status = ActionUi.Text("正在初始化…", 12, ActionUi.Muted); AutomationProperties.SetAutomationId(Status, "LatestDiagnosticStatus"); DockPanel.SetDock(Status, Dock.Top); diagnostics.Children.Add(Status);
        var build = ActionUi.Text("完整版本：" + version, 12, ActionUi.Muted); build.Margin = new(0, 8, 0, 0);
        AutomationProperties.SetAutomationId(build, "SettingsBuildVersion"); DockPanel.SetDock(build, Dock.Top); diagnostics.Children.Add(build);
        Events = new ListBox { BorderThickness = new(0), Background = ActionUi.Canvas, FontSize = 12, Foreground = ActionUi.Muted, Margin = new(0, 8, 0, 0), MaxHeight = 240 };
        diagnostics.Children.Add(Events);
        var advanced = new Expander { Header = "診斷與測試", Content = diagnostics, VerticalContentAlignment = VerticalAlignment.Stretch };
        content.Children.Add(advanced); body.Children.Add(content); Content = body;
        SizeChanged += (_, _) => Events.MaxHeight = Math.Clamp(ActualHeight - heading.ActualHeight - exitButton.DesiredSize.Height - 110, 20, 240);
    }
}
