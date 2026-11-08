using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace SnapBar.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.ToolStripMenuItem toggle;
    private readonly Drawing.Icon automaticIcon = LoadIcon("snapbar");
    private readonly Drawing.Icon lightIcon = LoadIcon("snapbar-tray-light"), darkIcon = LoadIcon("snapbar-tray-dark");
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private bool automatic, disposed;

    public TrayIcon(Action showSettings, Action toggleAutomatic, Action exit)
    {
        menu.Items.Add(UiText.Get("TrayIcon_000"), null, (_, _) => showSettings());
        toggle = new Forms.ToolStripMenuItem(UiText.Get("TrayIcon_001"), null, (_, _) => toggleAutomatic());
        menu.Items.Add(toggle);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(UiText.Get("TrayIcon_002"), null, (_, _) => exit());
        icon = new Forms.NotifyIcon { Icon = ManualIcon(), Text = UiText.Get("TrayIcon_003"), ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) showSettings(); };
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Update(bool automatic, bool enabled)
    {
        toggle.Text = automatic ? UiText.Get("TrayIcon_004") : UiText.Get("TrayIcon_005");
        toggle.Enabled = enabled;
        this.automatic = automatic;
        icon.Icon = automatic ? automaticIcon : ManualIcon();
        icon.Text = automatic ? UiText.Get("TrayIcon_006") : UiText.Get("TrayIcon_007");
    }

    internal void RefreshLanguage()
    {
        foreach (Forms.ToolStripItem item in menu.Items) item.Text = UiText.Current(item.Text ?? "");
        Update(automatic, toggle.Enabled);
    }

    internal void AddOcr(Action capture)
    {
        var item = new Forms.ToolStripMenuItem(UiText.Get("TrayIcon_008"), null, (_, _) => dispatcher.BeginInvoke(capture));
        menu.Items.Insert(2, item);
    }

    public void NotifyHidden() => icon.ShowBalloonTip(4000, UiText.Get("TrayIcon_009"),
        UiText.Get("TrayIcon_010"), Forms.ToolTipIcon.Info);

    private static Drawing.Icon LoadIcon(string name)
    {
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/SnapBar.App;component/Assets/Branding/{name}.ico"))
            ?? throw new InvalidOperationException($"Missing application icon: {name}");
        using var stream = resource.Stream;
        using var source = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        return (Drawing.Icon)source.Clone();
    }

    private Drawing.Icon ManualIcon()
    {
        if (Forms.SystemInformation.HighContrast)
            return Drawing.SystemColors.Window.GetBrightness() > 0.5f ? darkIcon : lightIcon;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0 ? darkIcon : lightIcon;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { return lightIcon; }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(() =>
        {
            if (!disposed) icon.Icon = automatic ? automaticIcon : ManualIcon();
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        icon.Visible = false; icon.Dispose(); menu.Dispose(); automaticIcon.Dispose(); lightIcon.Dispose(); darkIcon.Dispose();
    }
}
