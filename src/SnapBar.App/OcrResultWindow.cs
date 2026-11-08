using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SnapBar.Core;
using SnapBar.Windows;
using SnapBar.Windows.Ocr;

namespace SnapBar.App;

internal sealed partial class OcrResultWindow : Window
{
    internal static TimeSpan ProgressDelay { get; set; } = TimeSpan.FromMilliseconds(300);
    internal static TimeSpan CopyConfirmationDuration { get; set; } = TimeSpan.FromSeconds(1.5);
    private readonly UiLanguage language = UiText.Capture();
    private readonly TextBlock status;
    private readonly TextBox editor;
    private readonly Button copy, more, reselect, cancel;
    private readonly ProgressBar progress;
    private readonly DispatcherTimer progressTimer, copyTimer;
    private readonly ScreenRect? region;
    private bool copied, completed;
    internal event Action? Reselect;
    internal bool IsProcessing => !completed;

    internal OcrResultWindow(string searchTemplate, ScreenRect? region, OcrActionDependencies? actionDependencies = null)
    {
        this.region = region;
        InitializeActions(actionDependencies ?? OcrActionDependencies.Default(searchTemplate));
        ActionUi.Setup(this, language.Get("MainWindow_Ocr_000"), 420, 180);
        MinWidth = 320; MinHeight = 120; Topmost = true; ResizeMode = ResizeMode.NoResize;
        var body = new DockPanel { Margin = new(18) }; Content = body;
        status = ActionUi.Text(language.Get("Ocr_Processing"), 13, ActionUi.Muted);
        status.Margin = new(0, 0, 0, 10);
        AutomationProperties.SetAutomationId(status, "OcrStatus");
        DockPanel.SetDock(status, Dock.Top); body.Children.Add(status);
        progress = new ProgressBar { Height = 4, IsIndeterminate = SystemParameters.ClientAreaAnimation,
            Minimum = 0, Maximum = 1, Value = 0, Visibility = Visibility.Collapsed, Margin = new(0, 0, 0, 10) };
        AutomationProperties.SetName(progress, language.Get("Ocr_Processing"));
        DockPanel.SetDock(progress, Dock.Top); body.Children.Add(progress);
        var buttons = new WrapPanel { Margin = new(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);
        editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(8), Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(editor, "OcrEditor");
        AutomationProperties.SetName(editor, language.Get("Ocr_Editor"));
        KeyboardNavigation.SetTabIndex(editor, 0);
        body.Children.Add(editor);
        copy = AddButton("Ocr_Copy", "OcrCopy", () =>
        {
            if (string.IsNullOrWhiteSpace(SelectedText())) return;
            try
            {
                Clipboard.SetText(SelectedText()); status.Text = language.Get("MainWindow_Ocr_001"); copied = true; UpdateActions();
                copyTimer!.Stop(); copyTimer.Start();
            }
            catch (ExternalException) { copied = false; status.Text = language.Get("MainWindow_Ocr_009"); UpdateActions(); }
        }, 1);
        reselect = AddButton("Ocr_Reselect", "OcrReselect", () => Reselect?.Invoke(), 2);
        more = AddButton("Ocr_More", "OcrMore", OpenActions, 3, "more");
        cancel = AddButton("Ocr_Cancel", "OcrCancel", Close, 4);
        copy.Visibility = reselect.Visibility = more.Visibility = Visibility.Collapsed;
        editor.TextChanged += (_, _) => EditorChanged();
        editor.SelectionChanged += (_, _) => EditorChanged();
        progressTimer = new DispatcherTimer { Interval = ProgressDelay };
        progressTimer.Tick += (_, _) => { progressTimer.Stop(); if (!completed) progress.Visibility = Visibility.Visible; };
        copyTimer = new DispatcherTimer { Interval = CopyConfirmationDuration };
        copyTimer.Tick += (_, _) => { copyTimer.Stop(); copied = false; UpdateActions(); };
        Loaded += (_, _) => { PlaceNear(); if (!completed) progressTimer.Start(); else editor.Focus(); };
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook((nint hwnd, int message, nint w, nint l, ref bool handled) =>
            {
                if (message is 0x02E0 or 0x007E or 0x001A) Dispatcher.BeginInvoke(PlaceNear);
                return 0;
            });
        };
        Closed += (_, _) => { DisposeActions(); progressTimer.Stop(); copyTimer.Stop(); editor.Clear(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (!CloseActions()) Close(); } };

        Button AddButton(string key, string id, Action action, int tab, string? icon = null)
        {
            var button = ActionUi.Button(language.Get(key), action, icon);
            AutomationProperties.SetAutomationId(button, id); KeyboardNavigation.SetTabIndex(button, tab);
            buttons.Children.Add(button); return button;
        }
    }
    private string SelectedText() => editor.SelectionLength > 0 ? editor.SelectedText : editor.Text;
    private void EditorChanged()
    {
        ActionEditorChanged();
        copied = false; copyTimer?.Stop(); UpdateActions();
    }
    private static void SetLabel(Button button, string label)
    {
        ((StackPanel)button.Content).Children.OfType<TextBlock>().Last().Text = label;
        AutomationProperties.SetName(button, label);
    }
    private void UpdateActions()
    {
        if (copy == null || more == null) return;
        copy.IsEnabled = !string.IsNullOrWhiteSpace(SelectedText());
        SetLabel(copy, language.Get(copied ? "Ocr_Copied" : editor.SelectionLength > 0 ? "Ocr_CopySelected" : "Ocr_Copy"));
    }
    internal void SetResult(OcrResult result)
    {
        completed = true; progressTimer.Stop(); progress.Visibility = Visibility.Collapsed;
        cancel.Visibility = Visibility.Collapsed; reselect.Visibility = Visibility.Visible;
        status.Text = language.Get(result.Status switch
        {
            "ok" => "MainWindow_Ocr_001", "empty" => "Ocr_Empty", "runtime" => "MainWindow_Ocr_003",
            "models" => "MainWindow_Ocr_004", "region" => "MainWindow_Ocr_005", "timeout" => "Ocr_Timeout",
            "capture" => "Ocr_CaptureError", "worker" => "Ocr_WorkerError", "protocol" => "Ocr_ProtocolError",
            _ => "MainWindow_Ocr_006"
        });
        if (result.Status == "ok" && result.Text != null)
        {
            MinHeight = 220; Height = 300; ResizeMode = ResizeMode.CanResize;
            editor.Text = result.Text; editor.Visibility = Visibility.Visible;
            copy.Visibility = more.Visibility = Visibility.Visible;
            UpdateActions(); editor.Focus();
        }
        WindowWorkArea.Attach(this);
        if (IsLoaded) PlaceNear();
    }
    private void PlaceNear()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var anchor = region;
        if (anchor == null) { Native.GetCursorPos(out var point); anchor = new(point.X, point.Y, 1, 1); }
        // Move onto the target monitor first, then query that window's effective DPI.
        Native.SetWindowPos(handle, 0, (int)anchor.X, (int)anchor.Y, 0, 0, 0x15);
        for (var pass = 0; pass < 2; pass++)
        {
            var scale = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
            var work = Native.WorkArea((int)anchor.X, (int)anchor.Y);
            MinWidth = Math.Min(320, work.Width / scale);
            MinHeight = Math.Min(editor.Visibility == Visibility.Visible ? 220 : 120, work.Height / scale);
            var width = Math.Min(Width * scale, work.Width); var height = Math.Min(Height * scale, work.Height);
            var gap = 12 * scale;
            var x = anchor.Right + gap; var y = anchor.Bottom + gap;
            if (y + height > work.Bottom) y = anchor.Y - gap - height;
            x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - width));
            y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - height));
            Native.SetWindowPos(handle, 0, (int)x, (int)y, (int)width, (int)height, 0x14);
        }
    }
}
