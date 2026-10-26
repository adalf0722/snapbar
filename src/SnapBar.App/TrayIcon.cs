using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace SnapBar.App;

internal sealed class TrayIcon : IDisposable
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.ToolStripMenuItem toggle;
    private readonly Drawing.Icon automaticIcon = CreateIcon(true), manualIcon = CreateIcon(false);

    public TrayIcon(Action showSettings, Action toggleAutomatic, Action exit)
    {
        menu.Items.Add("開啟設定", null, (_, _) => showSettings());
        toggle = new Forms.ToolStripMenuItem("啟用自動偵測", null, (_, _) => toggleAutomatic());
        menu.Items.Add(toggle);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("結束 SnapBar", null, (_, _) => exit());
        icon = new Forms.NotifyIcon { Icon = manualIcon, Text = "SnapBar · 僅使用快捷鍵", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) showSettings(); };
    }

    public void Update(bool automatic, bool enabled)
    {
        toggle.Text = automatic ? "暫停自動偵測" : "啟用自動偵測";
        toggle.Enabled = enabled;
        icon.Icon = automatic ? automaticIcon : manualIcon;
        icon.Text = automatic ? "SnapBar · 自動偵測已啟用" : "SnapBar · 僅使用快捷鍵";
    }

    public void NotifyHidden() => icon.ShowBalloonTip(4000, "SnapBar 在背景待命",
        "按系統匣圖示可開啟設定；右鍵選單可結束 SnapBar。", Forms.ToolTipIcon.Info);

    private static Drawing.Icon CreateIcon(bool automatic)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var background = new Drawing.SolidBrush(automatic ? Drawing.Color.FromArgb(25, 104, 190) : Drawing.Color.FromArgb(64, 73, 88));
        graphics.FillEllipse(background, 1, 1, 30, 30);
        using var pen = new Drawing.Pen(Drawing.Color.White, 2.5f);
        graphics.DrawLines(pen, [new(11, 9), new(7, 9), new(7, 23), new(11, 23)]);
        graphics.DrawLines(pen, [new(21, 9), new(25, 9), new(25, 23), new(21, 23)]);
        graphics.DrawLine(pen, 13, 13, 20, 13);
        graphics.DrawLine(pen, 13, 19, 20, 19);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Drawing.Icon.FromHandle(handle); return (Drawing.Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }

    public void Dispose()
    {
        icon.Visible = false; icon.Dispose(); menu.Dispose(); automaticIcon.Dispose(); manualIcon.Dispose();
    }
}
