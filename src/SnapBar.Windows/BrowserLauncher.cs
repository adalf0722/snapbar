using System.Diagnostics;
using SnapBar.Core;

namespace SnapBar.Windows;

public static class BrowserLauncher
{
    public static void Open(Uri destination)
    {
        if (!destination.IsAbsoluteUri || !WebActionPolicy.OpenLink(destination.AbsoluteUri).Success)
            throw new ArgumentException("Only complete HTTP or HTTPS addresses may be launched");
        // No command interpreter or user-supplied argument string is involved.
        using var process = Process.Start(new ProcessStartInfo(destination.AbsoluteUri) { UseShellExecute = true });
    }
}
