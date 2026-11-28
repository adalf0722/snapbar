using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SnapBar.App;

internal static class AiClipboardTest
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);

    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
            foreach (var child in Children<T>(VisualTreeHelper.GetChild(item, i))) yield return child;
    }

    internal static async Task<bool> CopyExactAsync(Window panel, string buttonId, string expected)
    {
        var button = Children<Button>(panel).Single(b => AutomationProperties.GetAutomationId(b) == buttonId);
        var deadline = Stopwatch.StartNew();
        var attempts = 0;
        do
        {
            if (!button.IsEnabled) return false;
            attempts++;
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            // Retry the actual UI action only when the application reports busy.
            // A successful copy with incorrect content must never be retried.
            var labels = Children<TextBlock>(panel).Select(t => t.Text).ToArray();
            if (!labels.Contains(UiText.Get("AiPanelWindow_068")))
            {
                if (!labels.Contains(UiText.Get("AiPanelWindow_067")))
                { Console.WriteLine($"COPY DIAGNOSTIC button={buttonId}; completion-status-missing=true"); return false; }
                return await ReadExactAsync(expected, buttonId);
            }
            await Task.Delay(15);
        } while (deadline.ElapsedMilliseconds < 300);
        Console.WriteLine($"COPY DIAGNOSTIC button={buttonId}; write-busy=true; attempts={attempts}");
        return false;
    }

    private static async Task<bool> ReadExactAsync(string expected, string buttonId)
    {
        // WPF GetText may return an empty string when OLE cannot obtain the data
        // object. Only retry lock contention, never an available wrong value.
        var deadline = Stopwatch.StartNew();
        do
        {
            if (OpenClipboard(0))
            {
                try
                {
                    var memory = GetClipboardData(13);
                    var pointer = memory == 0 ? 0 : GlobalLock(memory);
                    if (pointer == 0)
                    { Console.WriteLine($"COPY DIAGNOSTIC button={buttonId}; unicode-data-unavailable=true"); return false; }
                    try
                    {
                        var actual = Marshal.PtrToStringUni(pointer) ?? "";
                        if (actual == expected) return true;
                        var difference = 0;
                        while (difference < Math.Min(actual.Length, expected.Length) && actual[difference] == expected[difference]) difference++;
                        Console.WriteLine($"COPY DIAGNOSTIC button={buttonId}; expected-length={expected.Length}; actual-length={actual.Length}; first-difference={difference}");
                        return false;
                    }
                    finally { GlobalUnlock(memory); }
                }
                finally { CloseClipboard(); }
            }
            await Task.Delay(15);
        } while (deadline.ElapsedMilliseconds < 300);
        Console.WriteLine($"COPY DIAGNOSTIC button={buttonId}; read-busy=true");
        return false;
    }
}
