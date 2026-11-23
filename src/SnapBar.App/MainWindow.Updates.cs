using System.IO;
using System.Reflection;
using System.Windows.Threading;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    private UpdateCoordinator updates = null!;
    private UpdateSettingsPanel updatePanel = null!;
    private GitHubUpdateSource? updateSource;
    private DispatcherTimer? updateTimer;

    private void InitializeUpdates()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "";
        updateSource = new();
        updates = new(version, new UpdateStateStore(Path.Combine(Path.GetDirectoryName(settingsStore.FilePath)!, "updates.json")),
            updateSource.ReadAsync, () => exitRequested || tray == null || tray.NotificationPending || offeringOnboarding || ocrSession != null
                || controller?.UpdateNotificationBusy == true,
            release => tray?.NotifyUpdate(release));
        updatePanel = new(updates, showHeading: false);
        updates.Changed += RefreshTrayUpdate;
    }

    private void StartUpdates()
    {
        RefreshTrayUpdate();
        updateTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            async (_, _) => await updates.TickAsync(), Dispatcher);
        updateTimer.Start();
    }

    internal void ShowUpdate(UpdateRelease? release)
    {
        if (exitRequested) return;
        ShowCategory(SettingsCategory.About); updatePanel.ShowRelease(release);
    }

    private void RefreshTrayUpdate() => tray?.SetUpdate(updates.State.Latest, () => ShowUpdate(null));

    private void StopUpdates()
    {
        updateTimer?.Stop(); updates.Changed -= RefreshTrayUpdate;
        updates.Dispose(); updatePanel.Dispose(); updateSource?.Dispose();
    }
}
