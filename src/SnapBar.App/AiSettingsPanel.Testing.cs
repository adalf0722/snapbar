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
        testService = ActionUi.Button(Text("AiSettingsPanel_Testing_000"), async () => await RunTestAsync());
        AutomationProperties.SetAutomationId(testService, "AiTestService"); buttons.Children.Add(testService);
        cancelServiceTest = ActionUi.Button(Text("AiSettingsPanel_Testing_001"), () => testController.Cancel());
        AutomationProperties.SetAutomationId(cancelServiceTest, "AiCancelServiceTest"); buttons.Children.Add(cancelServiceTest);
        testStatus = ActionUi.Text("", 12, ActionUi.Muted);
        testStatus.Margin = new(6, 8, 0, 8);
        AutomationProperties.SetAutomationId(testStatus, "AiServiceTestStatus"); buttons.Children.Add(testStatus);
        fields.Children.Add(buttons);
        var cost = ActionUi.Text(Text("AiSettingsPanel_Testing_002"), 12, ActionUi.Muted);
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
        { ResetTest(); testStatus.Text = ValidationMessages.Describe(validation, language); return; }
        string effectiveKey;
        try { effectiveKey = EffectiveTestKey(profile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException or ArgumentException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        { ResetTest(); testStatus.Text = Text("AiSettingsPanel_Testing_003"); return; }
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
            "testing" => Text("AiSettingsPanel_Testing_004"), "success" => Text("AiSettingsPanel_Testing_005"),
            "untested" => Text("AiSettingsPanel_Testing_006"),
            _ => UiText.Join(Text("AiSettingsPanel_Testing_007"), UiText.Current(AiErrorMessages.Describe(testController.Status), language?.Culture))
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
