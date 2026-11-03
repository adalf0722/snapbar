using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed partial class AiSettingsPanel
{
    private AiHttpClient? ownedTestClient;
    private AiServiceTestController testController = null!;
    private Button testService = null!, cancelServiceTest = null!;
    private TextBlock testStatus = null!;
    private bool testingDisposed;

    private void InitializeTesting(StackPanel fields, IAiTextClient? client)
    {
        testController = new(client ?? (ownedTestClient = new AiHttpClient()));
        var buttons = new WrapPanel { Margin = new(0, 16, 0, 0) };
        testService = ActionUi.Button("測試連線", async () => await RunTestAsync());
        AutomationProperties.SetAutomationId(testService, "AiTestService"); buttons.Children.Add(testService);
        cancelServiceTest = ActionUi.Button("取消測試", () => testController.Cancel());
        AutomationProperties.SetAutomationId(cancelServiceTest, "AiCancelServiceTest"); buttons.Children.Add(cancelServiceTest);
        testStatus = ActionUi.Text("", 12, ActionUi.Muted);
        testStatus.Margin = new(6, 8, 0, 8);
        AutomationProperties.SetAutomationId(testStatus, "AiServiceTestStatus"); buttons.Children.Add(testStatus);
        fields.Children.Add(buttons);
        var cost = ActionUi.Text("測試會傳送少量文字，可能產生費用。", 12, ActionUi.Muted);
        cost.Margin = new(0, 6, 0, 0); fields.Children.Add(cost);
        testController.Changed += RenderTestStatus;
        RenderTestStatus();
    }

    private string EffectiveTestKey(AiProviderProfile profile)
    {
        if (removeKey.IsChecked == true) return "";
        if (key.Password.Length > 0) return key.Password;
        if (keyChanges.TryGetValue(Scope(profile), out var pending)) return pending.Key ?? "";
        return preferences.Secrets.Get(profile) ?? "";
    }

    private async Task RunTestAsync()
    {
        if (testingDisposed || testController.Busy) return;
        var profile = CurrentDraft();
        if (AiPolicy.ValidateProfile(profile) is { } validation)
        { ResetTest(); testStatus.Text = validation; return; }
        string effectiveKey;
        try { effectiveKey = EffectiveTestKey(profile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException or ArgumentException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        { ResetTest(); testStatus.Text = "無法讀取金鑰，請重新輸入後測試。"; return; }
        await testController.StartAsync(profile, effectiveKey);
    }

    private void ResetTest() { if (!testingDisposed) testController.Reset(); }

    private void RenderTestStatus()
    {
        testService.IsEnabled = !testController.Busy;
        cancelServiceTest.IsEnabled = testController.Busy;
        cancelServiceTest.Visibility = testController.Busy ? Visibility.Visible : Visibility.Collapsed;
        testStatus.Foreground = testController.Status == "success" ? ActionUi.Brush("#237346")
            : testController.Status is "testing" or "untested" ? ActionUi.Muted : System.Windows.Media.Brushes.Firebrick;
        testStatus.Text = testController.Status switch
        {
            "testing" => "測試中…", "success" => "✓ 連線成功",
            "untested" => "尚未測試",
            _ => "測試失敗：" + AiErrorMessages.Describe(testController.Status)
        };
    }

    private void DisposeTesting()
    {
        if (testingDisposed) return;
        testController.Cancel();
        testController.Changed -= RenderTestStatus;
        testingDisposed = true;
        testController.Dispose(); ownedTestClient?.Dispose();
    }
}
