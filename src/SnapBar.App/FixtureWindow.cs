using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapBar.App;

public sealed class FixtureWindow : Window
{
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    public const string SearchFixtureText = "SnapBar 中文 & + # ? 😀";
    public const string LinkFixtureText = "https://example.com/snapbar?first=1&second=two#part";
    public const string LookupFixtureText = "take off";
    public FixtureWindow(bool delayedUia = false, bool deepUia = false, string? webActionMode = null, string? pathFixture = null)
    {
        if (webActionMode == "lookup"
            && int.TryParse(Environment.GetEnvironmentVariable("SNAPBAR_LOOKUP_TEST_RUNNER_PID"), out var runnerPid) && runnerPid > 0)
        {
            Activated += (_, _) => AllowSetForegroundWindow(runnerPid);
            // Automation input can consume the one-time foreground grant after activation.
            // Renew it only while this disposable fixture actually owns the foreground.
            var foregroundGrant = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            foregroundGrant.Tick += (_, _) =>
            {
                if (SnapBar.Windows.Native.GetForegroundWindow() == new System.Windows.Interop.WindowInteropHelper(this).Handle)
                    AllowSetForegroundWindow(runnerPid);
            };
            Loaded += (_, _) => foregroundGrant.Start();
            Closed += (_, _) => foregroundGrant.Stop();
        }
        Title = delayedUia ? "SnapBar P0 — Delayed UIA Fixture"
            : deepUia ? "SnapBar P0 — Deep UIA Fixture" : "SnapBar P0 — Test Fixture";
        Width = 720; Height = 530;
        Background = new SolidColorBrush(Color.FromRgb(246, 248, 251));
        var stack = new StackPanel { Margin = new Thickness(30) };
        stack.Children.Add(new TextBlock { Text = "獨立程序測試視窗", FontSize = 25, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = "在下方選字，再按已設定的快捷鍵，或回設定啟用自動偵測。", Margin = new(0, 10, 0, 20) });
        stack.Children.Add(new TextBlock { Text = "可編輯文字" });
        TextBox edit = delayedUia ? new DelayedTextBox() : new TextBox();
        edit.Text = "SnapBar Windows — 測試選取文字。\nSelect this sentence to test Copy, Cut and Paste.\n第二行保留原文，確認只取代選取範圍。";
        if (webActionMode != null)
        {
            edit.Text = webActionMode switch { "link" => LinkFixtureText, "lookup" => LookupFixtureText, _ => SearchFixtureText };
            edit.IsReadOnly = true;
        }
        edit.AcceptsReturn = true; edit.Height = 130; edit.Padding = new(12);
        if (pathFixture != null) { edit.Text = pathFixture; edit.IsReadOnly = true; }
        edit.FontSize = 18; edit.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetAutomationId(edit, "EditableSample");
        UIElement editableContent = edit;
        if (deepUia)
            for (int depth = 0; depth < 32; depth++)
                editableContent = new GroupBox { Content = editableContent, Padding = new(0), BorderThickness = new(0) };
        stack.Children.Add(editableContent);
        stack.Children.Add(new TextBlock { Text = "唯讀文字（可複製或搜尋）", Margin = new(0, 18, 0, 4) });
        var read = new TextBox { Text = "Read-only sample — 不能剪下或貼上。", IsReadOnly = true, FontSize = 18, Padding = new(12) };
        AutomationProperties.SetAutomationId(read, "ReadOnlySample"); stack.Children.Add(read);
        stack.Children.Add(new TextBlock { Text = "密碼欄位（不應讀取）", Margin = new(0, 18, 0, 4) });
        var password = new PasswordBox { Password = "fixture-only", Padding = new(12) };
        AutomationProperties.SetAutomationId(password, "PasswordSample"); stack.Children.Add(password);
        Content = stack;
        Loaded += (_, _) => { edit.Focus(); if (webActionMode != null || pathFixture != null) edit.SelectAll(); else edit.Select(0, 7); };
    }
}
