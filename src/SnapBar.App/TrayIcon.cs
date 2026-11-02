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
        menu.Items.Add("開啟設定", null, (_, _) => showSettings());
        toggle = new Forms.ToolStripMenuItem("啟用自動偵測", null, (_, _) => toggleAutomatic());
        menu.Items.Add(toggle);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("結束 SnapBar", null, (_, _) => exit());
        icon = new Forms.NotifyIcon { Icon = ManualIcon(), Text = "SnapBar · 僅使用快捷鍵", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) showSettings(); };
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Update(bool automatic, bool enabled)
    {
        toggle.Text = automatic ? "暫停自動偵測" : "啟用自動偵測";
        toggle.Enabled = enabled;
        this.automatic = automatic;
        icon.Icon = automatic ? automaticIcon : ManualIcon();
        icon.Text = automatic ? "SnapBar · 自動偵測已啟用" : "SnapBar · 僅使用快捷鍵";
    }

    public void NotifyHidden() => icon.ShowBalloonTip(4000, "SnapBar 在背景待命",
        "按系統匣圖示可開啟設定；右鍵選單可結束 SnapBar。", Forms.ToolTipIcon.Info);

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
