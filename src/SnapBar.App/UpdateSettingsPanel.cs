using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class UpdateSettingsPanel : StackPanel, IDisposable
{
    private readonly UpdateCoordinator updates;
    private readonly TextBlock status, details, timestamp, retry;
    private readonly CheckBox automatic;
    private readonly Button check, download;
    private UpdateRelease? selected;
    private bool refreshing;
    private DateTimeOffset? observedFetch;
    internal UpdateRelease? DisplayedRelease => selected ?? updates.State.Latest;
    internal event Action? RevealRequested;

    internal UpdateSettingsPanel(UpdateCoordinator updates, bool showHeading = true)
    {
        this.updates = updates;
        observedFetch = updates.State.CandidatesFetchedAtUtc;
        Margin = new(0, 0, 0, 16);
        if (showHeading) Children.Add(ActionUi.Heading(UiText.Get("Updates_Heading"), 16));
        status = ActionUi.Text("", 13, ActionUi.Muted); Children.Add(status);
        AutomationProperties.SetAutomationId(status, "UpdateStatus");
        automatic = new CheckBox { Content = UiText.Get("Updates_Automatic"), Margin = new(0, 8, 0, 4) };
        AutomationProperties.SetAutomationId(automatic, "AutomaticUpdateChecks");
        automatic.Checked += (_, _) => { if (!refreshing) updates.SetEnabled(true); };
        automatic.Unchecked += (_, _) => { if (!refreshing) updates.SetEnabled(false); };
        Children.Add(automatic);
        Children.Add(ActionUi.Text(UiText.Get("Updates_Privacy"), 12, ActionUi.Muted));
        var buttons = new WrapPanel { Margin = new(0, 8, 0, 4) };
        check = ActionUi.Button(UiText.Get("Updates_Check"), () => { selected = null; _ = updates.CheckAsync(); Refresh(); });
        AutomationProperties.SetAutomationId(check, "CheckUpdates"); buttons.Children.Add(check);
        download = ActionUi.Button(UiText.Get("Updates_Download"), OpenRelease);
        download.Margin = new(8, 0, 0, 0); AutomationProperties.SetAutomationId(download, "OpenUpdateRelease");
        buttons.Children.Add(download); Children.Add(buttons);
        retry = ActionUi.Text("", 12, ActionUi.Muted); Children.Add(retry);
        details = ActionUi.Text("", 13, ActionUi.Muted); Children.Add(details);
        timestamp = ActionUi.Text("", 12, ActionUi.Muted); timestamp.Margin = new(0, 6, 0, 0); Children.Add(timestamp);
        Children.Add(ActionUi.Text(UiText.Get("Updates_Install"), 12, ActionUi.Muted));
        updates.Changed += OnChanged; UiText.Changed += Refresh;
        Refresh();
    }

    internal void ShowRelease(UpdateRelease? release) { selected = release; Refresh(); RevealRequested?.Invoke(); }
    private void OnChanged()
    {
        // Successful refresh can withdraw a previously cached target.
        if (observedFetch != updates.State.CandidatesFetchedAtUtc)
        {
            observedFetch = updates.State.CandidatesFetchedAtUtc;
            if (selected?.Version != updates.State.Latest?.Version && selected?.Version != updates.State.Notification?.Version)
                selected = null;
        }
        Refresh();
    }
    private void Refresh()
    {
        refreshing = true;
        try
        {
            var candidate = DisplayedRelease;
            var chinese = UiText.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            var text = chinese ? candidate?.Metadata?.Zh : candidate?.Metadata?.En;
            status.Text = UiText.Get(!updates.StorageAvailable && updates.Supported ? "Updates_StorageUnavailable" : updates.Status);
            automatic.IsEnabled = updates.Supported && updates.StorageAvailable;
            automatic.IsChecked = updates.State.Enabled && updates.StorageAvailable;
            check.IsEnabled = updates.CanCheck;
            automatic.Content = UiText.Get("Updates_Automatic"); check.Content = UiText.Get("Updates_Check"); download.Content = UiText.Get("Updates_Download");
            retry.Text = updates.NextManualUtc is { } next && !updates.CanCheck && !updates.Checking
                ? UiText.Get("Updates_Retry", next.ToLocalTime().ToString("g", UiText.Culture)) : "";
            download.Visibility = candidate == null ? Visibility.Collapsed : Visibility.Visible;
            details.Text = candidate == null ? "" : UiText.Join(candidate.Version, " · ", UiText.Get("VersionChannel_" + ProductVersion.Parse(candidate.Version).Channel), "\n",
                text == null ? UiText.Get("Updates_FullNotes") : string.Join("\n", text.Summaries.Select(item => "• " + item).Concat(text.Notices)));
            timestamp.Text = updates.State.LastSuccessUtc is { } time
                ? UiText.Get("Updates_LastSuccess", time.ToLocalTime().ToString("g", UiText.Culture)) : "";
        }
        finally { refreshing = false; }
    }

    private void OpenRelease()
    {
        if (DisplayedRelease is not { } candidate) return;
        try { Process.Start(new ProcessStartInfo(GitHubUpdateSource.ReleaseUri(candidate.Version).AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { status.Text = UiText.Get("Updates_OpenFailed"); }
    }

    public void Dispose() { updates.Changed -= OnChanged; UiText.Changed -= Refresh; RevealRequested = null; }
}
