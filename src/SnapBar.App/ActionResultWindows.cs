using System.Windows;

namespace SnapBar.App;

// Completed results outlive their source selection, but never retain permission to write it.
internal sealed class ActionResultWindows
{
    private readonly List<ActionResultWindow> windows = [];

    internal void Show(ActionResultWindow window)
    {
        InvalidateTargets();
        if (windows.LastOrDefault(w => w.IsVisible) is { } previous)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = previous.Left + 24;
            window.Top = previous.Top + 24;
        }
        windows.Add(window);
        window.Closed += (_, _) => windows.Remove(window);
        WindowWorkArea.Attach(window);
        window.Show();
    }

    internal bool Contains(int x, int y) => windows.Any(window => window.Contains(x, y));

    internal void InvalidateTargets()
    {
        foreach (var window in windows) window.InvalidateTarget();
    }

    internal void CloseAll()
    {
        foreach (var window in windows.ToArray()) window.Close();
    }
}
