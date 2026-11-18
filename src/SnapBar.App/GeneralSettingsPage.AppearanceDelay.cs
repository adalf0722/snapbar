using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed partial class GeneralSettingsPage
{
    internal Action? CancelAppearanceDelayEdit { get; private set; }

    internal void ConfigureAppearanceDelay(int initial, Func<int, bool> save)
    {
        var saved = ToolbarAppearanceDelay.Normalize(initial);
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
            if (pointerChanged || slider.Value != saved) Commit(value);
        }

        var reset = ActionUi.Button(UiText.Get("AppearanceDelay_Reset"), () => Commit(ToolbarAppearanceDelay.DefaultMilliseconds));
        var controls = new WrapPanel { Margin = new(0, 6, 0, 0) };
        var number = new StackPanel { Orientation = Orientation.Horizontal };
        number.Children.Add(input); number.Children.Add(unit);
        controls.Children.Add(slider); controls.Children.Add(number); controls.Children.Add(reset);
        var body = new StackPanel();
        body.Children.Add(ActionUi.Heading(UiText.Get("AppearanceDelay_Title"), 16));
        body.Children.Add(Description(UiText.Get("AppearanceDelay_Help")));
        body.Children.Add(controls); body.Children.Add(status);
        selectionRows.Children.Insert(1, Divider());
        selectionRows.Children.Insert(2, SettingRow(body));
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
            pointerChanged = false;
            slider.IsSnapToTickEnabled = true;
            Display((int)slider.Value, updateSlider: false);
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
            Display(saved);
            ShowStatus(null);
        }

        void RefreshEnabled()
        {
            var enabled = AutomaticToggle.IsChecked == true;
            if (!enabled) CancelEdit();
            controls.IsEnabled = enabled;
        }
        AutomaticToggle.Checked += (_, _) => RefreshEnabled();
        AutomaticToggle.Unchecked += (_, _) => RefreshEnabled();
        CancelAppearanceDelayEdit = CancelEdit;
        Unloaded += (_, _) => CancelEdit();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelEdit(); };
        RefreshEnabled();
    }
}
