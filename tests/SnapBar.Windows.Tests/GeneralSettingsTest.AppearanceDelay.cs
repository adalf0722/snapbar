using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static partial class GeneralSettingsTest
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);

    private static async Task TestAppearanceDelay(MainWindow window, string path, Action<string, bool> check)
    {
        var page = window.GeneralPage;
        var slider = Find<Slider>(page, "ToolbarAppearanceDelaySlider");
        var input = Find<TextBox>(page, "ToolbarAppearanceDelayInput");
        var reset = Find<Button>(page, "ToolbarAppearanceDelayReset");
        var status = Find<TextBlock>(page, "ToolbarAppearanceDelayStatus");
        int ReadSaved() => new SettingsStore(path).Load().Settings.ToolbarAppearanceDelayMilliseconds;
        static void Press(FrameworkElement element, Key key)
        {
            element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element),
                Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        static void LoseFocus(FrameworkElement element, IInputElement target) =>
            element.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, element, target)
                { RoutedEvent = Keyboard.PreviewLostKeyboardFocusEvent });

        check("appearance delay defaults to 120 with disabled controls", input.Text == "120" && slider.Value == 120
            && !slider.IsEnabled && !input.IsEnabled && !reset.IsEnabled);
        window.ToggleAutomatic();
        check("automatic appearance enables the delay controls", slider.IsEnabled && input.IsEnabled && reset.IsEnabled);
        input.Text = "173"; Press(input, Key.Enter);
        check("enter stores an exact millisecond and synchronizes the slider", ReadSaved() == 173 && slider.Value == 173);
        foreach (var invalid in new[] { "", "-1", "3.5", "2001", "NaN" })
        {
            input.Text = invalid; Press(input, Key.Enter);
            check($"invalid delay draft '{invalid}' keeps the saved value", input.Text == invalid && ReadSaved() == 173
                && slider.Value == 173 && status.Visibility == Visibility.Visible);
        }
        LoseFocus(input, page.AutomaticToggle);
        check("invalid focus loss restores the saved value and leaves the error visible", input.Text == "173"
            && status.Visibility == Visibility.Visible);
        input.Text = "875"; Press(input, Key.Escape);
        check("Escape discards the draft and clears the error", input.Text == "173" && status.Visibility == Visibility.Collapsed && ReadSaved() == 173);
        input.Text = "174"; LoseFocus(input, page.AutomaticToggle);
        check("valid focus loss persists exact milliseconds", ReadSaved() == 174 && slider.Value == 174);
        input.Text = "bad"; LoseFocus(input, slider);
        check("slider focus discards abandoned input without validating it", input.Text == "174" && status.Visibility == Visibility.Collapsed);
        Slider.IncreaseSmall.Execute(null, slider);
        check("slider keyboard step adds one millisecond and defers saving", input.Text == "175" && ReadSaved() == 174);
        await Task.Delay(400);
        check("debounced slider change persists", ReadSaved() == 175);
        await TestDelayTrackRelease(window, slider, input, reset, ReadSaved, check);
        Click(page, "ToolbarAppearanceDelayReset");
        check("reset restores the default", input.Text == "120" && slider.Value == 120 && ReadSaved() == 120);
        input.Text = "173"; Press(input, Key.Enter);

        using (var blocked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            input.Text = "182"; Press(input, Key.Enter);
            check("failed settings write rolls both controls back", input.Text == "173" && slider.Value == 173
                && status.Visibility == Visibility.Visible && ReadSaved() == 173);
        }
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var savedTimestamp = File.GetLastWriteTimeUtc(path);
        input.Text = " 173 "; Press(input, Key.Enter);
        check("retrying the existing value clears an error without a write", status.Visibility == Visibility.Collapsed
            && File.GetLastWriteTimeUtc(path) == savedTimestamp);
        window.ToggleAutomatic();
        check("disabling automatic appearance retains the saved delay", !input.IsEnabled && input.Text == "173" && ReadSaved() == 173);
        window.ToggleAutomatic();
        Slider.IncreaseSmall.Execute(null, slider);
        window.NavigateTo(SettingsCategory.About);
        await Task.Delay(400);
        window.NavigateTo(SettingsCategory.General);
        check("leaving settings cancels a pending slider save", input.Text == "173" && ReadSaved() == 173);

        var width = window.Width; var height = window.Height;
        var languageSelector = Find<ComboBox>(page, "InterfaceLanguageSelector");
        window.UpdateLayout();
        UiAppearanceTest.Snapshot(window, "settings-appearance-delay.png");
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        try
        {
            foreach (var (index, language, suffix, name) in new[]
            {
                (2, "en", "en", "Toolbar appearance delay"),
                (1, "zh-Hant", "zh-hant", "動作列出現延遲")
            })
            {
                languageSelector.SelectedIndex = index;
                check("appearance delay language changes to " + language,
                    new SettingsStore(path).Load().Settings.InterfaceLanguage == language);
                window.UpdateLayout();
                check("appearance delay fits and is accessible in " + language,
                    AutomationProperties.GetName(slider) == name && input.Text == "173" && slider.IsEnabled
                    && slider.ActualWidth > 0 && input.ActualWidth > 0 && reset.ActualWidth > 0
                    && ScrollWidthFits(page, reset));
                UiAppearanceTest.Snapshot(window, "settings-appearance-delay-" + suffix + ".png");
            }
        }
        finally { window.Width = width; window.Height = height; }
    }

    private static async Task TestDelayTrackRelease(Window window, Slider slider, TextBox input, Button reset,
        Func<int> readSaved, Action<string, bool> check)
    {
        window.UpdateLayout();
        var windowHandle = new WindowInteropHelper(window).Handle;
        var foregroundTimeout = DateTime.UtcNow.AddSeconds(15);
        Console.WriteLine("WAITING: activate the disposable SnapBar settings fixture for the slider gesture");
        while (Native.GetForegroundWindow() != windowHandle && DateTime.UtcNow < foregroundTimeout)
        {
            window.Activate();
            await Task.Delay(40);
        }
        if (Native.GetForegroundWindow() != windowHandle)
            throw new InvalidOperationException("The slider gesture requires the disposable settings window to own foreground");

        var track = (Track)slider.Template.FindName("PART_Track", slider);
        var inside = track.PointToScreen(new Point(track.ActualWidth * 0.7, track.ActualHeight / 2));
        var outside = reset.PointToScreen(new Point(reset.ActualWidth / 2, reset.ActualHeight / 2));
        var saved = readSaved();
        Native.GetCursorPos(out var original);
        try
        {
            // Queue actual desktop mouse messages to exercise WPF capture and hit testing.
            await Task.Run(() =>
            {
                if (!SetCursorPos((int)inside.X, (int)inside.Y)) throw new InvalidOperationException("Cannot move to slider track");
                mouse_event(2, 0, 0, 0, 0);
            });
            await Task.Delay(400);
            var preview = (int)slider.Value;
            check("track click previews a 10 ms tick without saving while held",
                preview != saved && preview % 10 == 0 && input.Text == preview.ToString()
                && readSaved() == saved);
            await Task.Run(() =>
            {
                if (!SetCursorPos((int)outside.X, (int)outside.Y)) throw new InvalidOperationException("Cannot move outside slider");
                mouse_event(4, 0, 0, 0, 0);
            });
            await Task.Delay(100);
            check("physical release outside slider saves the previewed value",
                slider.Value == preview && readSaved() == preview);
            Slider.IncreaseSmall.Execute(null, slider);
            await Task.Delay(400);
            check("keyboard change still saves after the outside release", readSaved() == preview + 1);
        }
        finally
        {
            await Task.Run(() =>
            {
                mouse_event(4, 0, 0, 0, 0);
                SetCursorPos(original.X, original.Y);
            });
        }
    }

    private static bool ScrollWidthFits(GeneralSettingsPage page, FrameworkElement element)
    {
        var bounds = element.TransformToAncestor(page).TransformBounds(new Rect(element.RenderSize));
        return bounds.Left >= 0 && bounds.Right <= page.ActualWidth;
    }
}
