using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class AiCommandTrial : Expander, IDisposable
{
    private readonly UiLanguage language;
    private readonly AiPreferences preferences;
    private readonly TextBox instruction, sample, output;
    private readonly TextBlock status, service;
    private readonly Button run, stop;
    private readonly IAiTextClient client;
    private readonly bool ownsClient;
    private CancellationTokenSource? request;
    private int revision;
    private bool disposed;

    internal AiCommandTrial(TextBox instruction, AiPreferences preferences, UiLanguage language, IAiTextClient? client = null)
    {
        this.instruction = instruction; this.preferences = preferences; this.language = language;
        this.client = client ?? new AiHttpClient(); ownsClient = client == null;
        Header = language.Get("AiTry_Title"); Margin = new(0, 12, 0, 0);
        AutomationProperties.SetAutomationId(this, "AiCommandTrial");
        var body = new StackPanel();
        body.Children.Add(ActionUi.Text(language.Get("AiTry_Help"), 12, ActionUi.Muted));
        service = ActionUi.Text("", 12, ActionUi.Muted); service.Margin = new(0, 6, 0, 6); body.Children.Add(service);
        sample = ActionUi.Input(language.Get("AiTry_Default"), 8192); sample.AcceptsReturn = true;
        sample.TextWrapping = TextWrapping.Wrap; sample.Height = 80; sample.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        sample.VerticalContentAlignment = VerticalAlignment.Top;
        AutomationProperties.SetAutomationId(sample, "AiTrialSample"); AutomationProperties.SetName(sample, language.Get("AiTry_Sample")); body.Children.Add(sample);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 8) };
        run = ActionUi.Button(language.Get("AiTry_Run"), () => _ = RunAsync());
        stop = ActionUi.Button(language.Get("AiTry_Stop"), Cancel); stop.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(run, "AiTrialRun"); AutomationProperties.SetAutomationId(stop, "AiTrialStop");
        tools.Children.Add(run); tools.Children.Add(stop); body.Children.Add(tools);
        status = ActionUi.Text("", 12, ActionUi.Muted); SettingsFeedback.Live(status); body.Children.Add(status);
        output = ActionUi.Input(); output.IsReadOnly = true; output.AcceptsReturn = true; output.TextWrapping = TextWrapping.Wrap;
        output.Height = 120; output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; output.Visibility = Visibility.Collapsed;
        output.VerticalContentAlignment = VerticalAlignment.Top;
        AutomationProperties.SetAutomationId(output, "AiTrialOutput"); AutomationProperties.SetName(output, language.Get("AiTry_Title")); body.Children.Add(output);
        Content = body;
        sample.TextChanged += DraftChanged; instruction.TextChanged += DraftChanged;
        preferences.Changed += ServiceChanged; ServiceChanged();
    }

    private void DraftChanged(object sender, TextChangedEventArgs e) => Invalidate();
    private void ServiceChanged()
    {
        var profile = preferences.SelectedProfile;
        service.Text = UiNames.Provider(profile, language) + (string.IsNullOrWhiteSpace(profile.Model) ? "" : " · " + profile.Model);
        Invalidate();
    }
    private void Invalidate()
    {
        revision++;
        if (request != null || output.Visibility == Visibility.Visible) status.Text = language.Get("AiTry_Stale");
        CancelRequest(); output.Clear(); output.Visibility = Visibility.Collapsed;
    }
    private void CancelRequest()
    {
        var previous = request; request = null; previous?.Cancel();
        run.IsEnabled = !disposed; stop.Visibility = Visibility.Collapsed;
    }
    private void Cancel() { revision++; CancelRequest(); status.Text = language.Get("AiTry_Cancelled"); }

    internal async Task RunAsync()
    {
        if (disposed || request != null) return;
        output.Clear(); output.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(sample.Text) || string.IsNullOrWhiteSpace(instruction.Text)) { status.Text = language.Get("AiTry_Ready"); return; }
        var profile = preferences.SelectedProfile;
        if (AiPolicy.ValidateProfile(profile) != null) { status.Text = language.Run(() => AiErrorMessages.Describe("settings-required")); return; }
        var key = preferences.Secrets.Get(profile) ?? "";
        if (profile.Protocol == AiProtocol.Responses && key.Length == 0) { status.Text = language.Run(() => AiErrorMessages.Describe("key-required")); return; }
        using var cancellation = new CancellationTokenSource(); request = cancellation; var current = revision;
        run.IsEnabled = false; stop.Visibility = Visibility.Visible; status.Text = language.Get("AiTry_Busy");
        try
        {
            var result = await client.ExecuteAsync(profile, key, sample.Text, instruction.Text, cancellation.Token);
            if (disposed || cancellation.IsCancellationRequested || current != revision) return;
            if (!result.Success) { status.Text = language.Run(() => AiErrorMessages.Describe(result.Status)); return; }
            output.Text = result.Text; output.Visibility = Visibility.Visible; status.Text = "";
        }
        catch (OperationCanceledException) { if (!disposed && current == revision) status.Text = language.Get("AiTry_Cancelled"); }
        catch (Exception e) when (e is IOException or HttpRequestException or InvalidOperationException)
        { if (!disposed && current == revision) status.Text = language.Run(() => AiErrorMessages.Describe("network")); }
        finally
        {
            if (ReferenceEquals(request, cancellation)) { request = null; run.IsEnabled = !disposed; stop.Visibility = Visibility.Collapsed; }
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; revision++; CancelRequest();
        instruction.TextChanged -= DraftChanged; sample.TextChanged -= DraftChanged; preferences.Changed -= ServiceChanged;
        if (ownsClient && client is IDisposable disposable) disposable.Dispose();
    }
}
