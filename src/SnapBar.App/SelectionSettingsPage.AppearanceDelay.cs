using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed partial class SelectionSettingsPage
{
    internal Action? CancelAppearanceDelayEdit { get; private set; }

    internal void ConfigureAppearanceDelay(int initial, Func<int, bool> save)
    {
        var saved = ToolbarAppearanceDelay.Normalize(initial);
        var summary = Description(UiText.Get("SelectionSettings_DelayValue", saved));
        Identify(summary, "ToolbarAppearanceDelaySummary", UiText.Get("AppearanceDelay_Title"));
        summary.VerticalAlignment = VerticalAlignment.Center;
        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(summary, Dock.Right); header.Children.Add(summary);
        header.Children.Add(Description(UiText.Get("AppearanceDelay_Title")));
        var details = Details("AppearanceDelay_Title", "ToolbarAppearanceDelayDetails", new StackPanel());
        details.Header = header;
        details.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var slider = new Slider
        {
            Minimum = 0, Maximum = ToolbarAppearanceDelay.MaximumMilliseconds, SmallChange = 1,
            LargeChange = 10, TickFrequency = 10, IsMoveToPointEnabled = true, Width = 152,
            VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 4, 12, 4)
        };
        var input = new TextBox { Width = 66, VerticalContentAlignment = VerticalAlignment.Center, Margin = new(0, 4, 6, 4) };
        var unit = Description(UiText.Get("AppearanceDelay_Milliseconds"));
        unit.VerticalAlignment = VerticalAlignment.Center;
        unit.Margin = new(0, 0, 12, 0);
        var status = Description("");
        status.Visibility = Visibility.Collapsed;
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var updating = false;
        var pointerEditing = false;
        var pointerChanged = false;
        var draft = false;

        void ShowStatus(string? key)
        {
            status.Text = key == null ? "" : UiText.Get(key);
            status.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
            if (key != null) UIElementAutomationPeer.FromElement(status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        void Display(int value, bool updateSlider = true)
        {
            updating = true;
            try
            {
                if (updateSlider) slider.Value = value;
                input.Text = value.ToString(CultureInfo.InvariantCulture);
                draft = false;
            }
            finally { updating = false; }
        }

        void Commit(int value)
        {
            debounce.Stop();
            var success = value == saved || save(value);
            if (success) saved = value;
            Display(saved);
            ShowStatus(success ? null : "AppearanceDelay_SaveFailed");
            summary.Text = UiText.Get("SelectionSettings_DelayValue", saved);
        }

        void CommitInput(bool losingFocus)
        {
            if (!draft) return;
            if (int.TryParse(input.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                && value <= ToolbarAppearanceDelay.MaximumMilliseconds)
            {
                Commit(value);
                return;
            }
            if (losingFocus) Display(saved);
            ShowStatus("AppearanceDelay_Invalid");
        }

        int SliderValue() => pointerEditing
            ? 10 * (int)Math.Round(slider.Value / 10, MidpointRounding.AwayFromZero)
            : (int)Math.Round(slider.Value, MidpointRounding.AwayFromZero);

        void EndPointerEdit()
        {
            if (!pointerEditing) return;
            var value = SliderValue();
            pointerEditing = false;
            slider.IsSnapToTickEnabled = false;
            if (Mouse.Captured == slider) slider.ReleaseMouseCapture();
            if (pointerChanged || slider.Value != saved) Commit(value);
        }

        var reset = ActionUi.Button(UiText.Get("AppearanceDelay_Reset"), () => Commit(ToolbarAppearanceDelay.DefaultMilliseconds));
        var controls = new WrapPanel { Margin = new(0, 6, 0, 0) };
        var number = new StackPanel { Orientation = Orientation.Horizontal };
        number.Children.Add(input); number.Children.Add(unit);
        controls.Children.Add(slider); controls.Children.Add(number); controls.Children.Add(reset);
        var body = new StackPanel();
        body.Children.Add(Description(UiText.Get("AppearanceDelay_Help")));
        body.Children.Add(controls); body.Children.Add(status);
        details.Content = body;
        toolbarRows.Children.Add(SettingRow(details));
        Identify(slider, "ToolbarAppearanceDelaySlider", UiText.Get("AppearanceDelay_Title"));
        Identify(input, "ToolbarAppearanceDelayInput", UiText.Get("AppearanceDelay_Input"));
        Identify(reset, "ToolbarAppearanceDelayReset", UiText.Get("AppearanceDelay_Reset"));
        Identify(status, "ToolbarAppearanceDelayStatus", UiText.Get("AppearanceDelay_Status"));
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Display(saved);

        input.GotKeyboardFocus += (_, _) => { if (debounce.IsEnabled) Commit(SliderValue()); };
        input.TextChanged += (_, _) => { if (!updating) draft = true; };
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) CommitInput(losingFocus: false);
            else if (e.Key == Key.Escape) { Display(saved); ShowStatus(null); }
            else return;
            e.Handled = true;
        };
        input.PreviewLostKeyboardFocus += (_, e) =>
        {
            if (!input.IsEnabled || !IsVisible || UiText.IsRefreshing)
            { Display(saved); return; }
            if (e.NewFocus == slider || e.NewFocus == reset || pointerEditing)
            { Display(saved, updateSlider: false); return; }
            CommitInput(losingFocus: true);
        };
        slider.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            debounce.Stop();
            pointerEditing = true;
            // WPF may have already moved the track before this preview handler.
            pointerChanged = slider.Value != saved;
            slider.IsSnapToTickEnabled = true;
            Display(pointerChanged ? SliderValue() : saved);
            slider.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (!pointerEditing) return;
                if (Mouse.LeftButton == MouseButtonState.Released)
                {
                    EndPointerEdit();
                    return;
                }
                // A Thumb drag captures during normal input routing. Track clicks
                // do not, so capture only after that routing has completed.
                if (Mouse.Captured == null && !Mouse.Capture(slider, CaptureMode.SubTree))
                    EndPointerEdit();
            });
        }), handledEventsToo: true);
        slider.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left) EndPointerEdit();
        }), handledEventsToo: true);
        slider.LostMouseCapture += (_, _) => EndPointerEdit();
        slider.ValueChanged += (_, _) =>
        {
            if (updating || !controls.IsEnabled) return;
            Display(SliderValue(), updateSlider: false);
            if (pointerEditing) { pointerChanged = true; return; }
            debounce.Stop();
            debounce.Start();
        };
        debounce.Tick += (_, _) => Commit(SliderValue());
        void CancelEdit()
        {
            debounce.Stop();
            pointerEditing = false;
            slider.IsSnapToTickEnabled = false;
            if (Mouse.Captured == slider) slider.ReleaseMouseCapture();
            if (slider.Template?.FindName("PART_Track", slider) is Track { Thumb: { IsDragging: true } thumb })
                thumb.CancelDrag();
            Display(saved);
            ShowStatus(null);
        }

        void RefreshEnabled()
        {
            var enabled = AutomaticToggle.IsChecked == true;
            if (!enabled) CancelEdit();
            controls.IsEnabled = enabled;
            if (!enabled) details.IsExpanded = false;
        }
        AutomaticToggle.Checked += (_, _) => RefreshEnabled();
        AutomaticToggle.Unchecked += (_, _) => RefreshEnabled();
        CancelAppearanceDelayEdit = CancelEdit;
        details.Collapsed += (_, _) => CancelEdit();
        Unloaded += (_, _) => CancelEdit();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelEdit(); };
        RefreshEnabled();
    }
}
