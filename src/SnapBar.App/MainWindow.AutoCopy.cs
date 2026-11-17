using System.IO;
using SnapBar.Core;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    private bool SaveAutoCopy(AutoCopySettings value)
    {
        if (exitRequested) return false;
        var updated = preferences with { AutoCopy = AutoCopySettings.Normalize(value) };
        try { settingsStore.Save(updated); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return false; }
        preferences = updated;
        controller?.ConfigureAutoCopy(updated.AutoCopy);
        return true;
    }
}
