using System.IO;
using SnapBar.Core;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    private bool SaveAppearanceDelay(int milliseconds)
    {
        if (exitRequested) return false;
        var value = ToolbarAppearanceDelay.Normalize(milliseconds);
        if (preferences.ToolbarAppearanceDelayMilliseconds == value) return true;
        var updated = preferences with { ToolbarAppearanceDelayMilliseconds = value };
        try { settingsStore.Save(updated); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return false; }
        preferences = updated;
        controller?.ConfigureAppearanceDelay(value);
        return true;
    }
}
