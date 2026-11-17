using System.Runtime.InteropServices;
using System.Windows.Interop;
using SnapBar.Core;

namespace SnapBar.Windows;

public record InputEvent(string Kind, int X = 0, int Y = 0, TextAction? Action = null,
    SelectionModifiers Modifiers = SelectionModifiers.None);

public sealed class InputMonitor : IDisposable
{
    private readonly HwndSource source;
    private readonly Native.HookProc mouseProc, keyboardProc;
    private nint mouseHook, keyboardHook;
    private Native.Point down, lastUp;
    private long lastUpTime;
    private int clickCount;
    private bool pressed;
    public event Action<InputEvent>? Input;
    public event Action<HotkeySettings>? HotkeyRecorded;
    public Func<uint, bool>? KeyFilter { get; set; }
    private readonly HashSet<uint> filteredKeys = [];
    private readonly HashSet<uint> heldModifiers = [];
    private readonly GlobalHotkey hotkey, searchHotkey, copyHotkey, ocrHotkey, lookupHotkey, aiHotkey, customHotkey;
    public bool HotkeyRegistered => hotkey.Registered;
    public nint OwnerWindow => source.Handle;
    public bool HotkeySuppressed
    {
        get => hotkey.Suppressed;
        set => hotkey.Suppressed = searchHotkey.Suppressed = copyHotkey.Suppressed = ocrHotkey.Suppressed
            = lookupHotkey.Suppressed = aiHotkey.Suppressed = customHotkey.Suppressed = value;
    }
    public bool TryChangeHotkey(HotkeySettings candidate, Action persist) => hotkey.TryChange(candidate, persist);
    public bool TryChangeActionHotkey(TextAction action, HotkeySettings? candidate, Action persist) =>
        GetActionHotkey(action).TryChange(candidate, persist);
    public bool IsActionHotkeyRegistered(TextAction action) => GetActionHotkey(action).Registered;

    private GlobalHotkey GetActionHotkey(TextAction action) => action switch
    {
        TextAction.Search => searchHotkey,
        TextAction.Copy => copyHotkey,
        TextAction.Ocr => ocrHotkey,
        TextAction.Lookup => lookupHotkey,
        TextAction.Ai => aiHotkey,
        TextAction.Custom => customHotkey,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public InputMonitor(HwndSource source, HotkeySettings? initialHotkey = null,
        HotkeySettings? initialSearchHotkey = null, HotkeySettings? initialCopyHotkey = null,
        HotkeySettings? initialOcrHotkey = null, HotkeySettings? initialLookupHotkey = null,
        HotkeySettings? initialAiHotkey = null, HotkeySettings? initialCustomHotkey = null)
    {
        this.source = source;
        foreach (var key in new uint[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
            if ((Native.GetAsyncKeyState((int)key) & 0x8000) != 0) heldModifiers.Add(key);
        source.AddHook(WindowMessage);
        hotkey = new(source.Handle, initialHotkey ?? HotkeySettings.Default);
        searchHotkey = new(source.Handle, initialSearchHotkey, firstId: 3);
        copyHotkey = new(source.Handle, initialCopyHotkey, firstId: 5);
        ocrHotkey = new(source.Handle, initialOcrHotkey, firstId: 9);
        lookupHotkey = new(source.Handle, initialLookupHotkey, firstId: 11);
        aiHotkey = new(source.Handle, initialAiHotkey, firstId: 13);
        customHotkey = new(source.Handle, initialCustomHotkey, firstId: 15);
        mouseProc = MouseCallback; keyboardProc = KeyboardCallback;
        mouseHook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
        keyboardHook = Native.SetWindowsHookEx(13, keyboardProc, Native.GetModuleHandle(null), 0);
        if (mouseHook == 0 || keyboardHook == 0)
        {
            Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Native.WM_HOTKEY)
        {
            if (hotkey.OwnsMessage(wParam)) Dispatch(hotkey, null);
            else if (searchHotkey.OwnsMessage(wParam)) Dispatch(searchHotkey, TextAction.Search);
            else if (copyHotkey.OwnsMessage(wParam)) Dispatch(copyHotkey, TextAction.Copy);
            else if (ocrHotkey.OwnsMessage(wParam)) Dispatch(ocrHotkey, TextAction.Ocr);
            else if (lookupHotkey.OwnsMessage(wParam)) Dispatch(lookupHotkey, TextAction.Lookup);
            else if (aiHotkey.OwnsMessage(wParam)) Dispatch(aiHotkey, TextAction.Ai);
            else if (customHotkey.OwnsMessage(wParam)) Dispatch(customHotkey, TextAction.Custom);
            else return 0;
            handled = true;
        }
        return 0;

        void Dispatch(GlobalHotkey binding, TextAction? action)
        {
            if (!binding.Matches(wParam, lParam)) return;
            if (binding.Suppressed) HotkeyRecorded?.Invoke(binding.Current!);
            else Input?.Invoke(new("hotkey", Action: action));
        }
    }

    private nint MouseCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<Native.MouseHookData>(lParam);
            if (data.ExtraInfo != (nuint)Native.InputMarker)
            {
                switch ((int)wParam)
                {
                    case 0x201:
                        down = data.Point; pressed = true;
                        Input?.Invoke(new("click", down.X, down.Y)); break;
                    case 0x202 when pressed:
                        pressed = false;
                        var now = Environment.TickCount64;
                        var same = now - lastUpTime <= Native.GetDoubleClickTime()
                            && Math.Abs(data.Point.X - lastUp.X) <= Native.GetSystemMetrics(36)
                            && Math.Abs(data.Point.Y - lastUp.Y) <= Native.GetSystemMetrics(37);
                        clickCount = same ? clickCount + 1 : 1;
                        lastUp = data.Point; lastUpTime = now;
                        var drag = Math.Abs(data.Point.X - down.X) >= Native.GetSystemMetrics(68)
                            || Math.Abs(data.Point.Y - down.Y) >= Native.GetSystemMetrics(69);
                        if (drag || clickCount >= 2) Input?.Invoke(new("selection", data.Point.X, data.Point.Y,
                            Modifiers: ReadModifiers()));
                        break;
                    case 0x204: case 0x207: Input?.Invoke(new("click", data.Point.X, data.Point.Y)); break;
                    case 0x20A: case 0x20E: Input?.Invoke(new("scroll", data.Point.X, data.Point.Y)); break;
                }
            }
        }
        return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private nint KeyboardCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && ((int)wParam is 0x100 or 0x104 or 0x101 or 0x105))
        {
            var data = Marshal.PtrToStructure<Native.KeyboardHookData>(lParam);
            if (data.ExtraInfo != (nuint)Native.InputMarker)
            {
                var down = (int)wParam is 0x100 or 0x104;
                // Let the source observe balanced modifier transitions, so asynchronous
                // state is already correct when the following menu key is inspected.
                if (data.Key is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5)
                {
                    // Typematic modifier-down events are still the same held gesture.
                    // Only a fresh press invalidates a pending selection; always forward
                    // balanced transitions to the source application.
                    if (!down) heldModifiers.Remove(data.Key);
                    else if (heldModifiers.Add(data.Key) && KeyFilter?.Invoke(data.Key) != true)
                        Input?.Invoke(new("key"));
                    return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
                }
                if (!down && filteredKeys.Remove(data.Key)) return 1;
                if (down && KeyFilter?.Invoke(data.Key) == true) { filteredKeys.Add(data.Key); return 1; }
                if (down) Input?.Invoke(new("key"));
            }
        }
        return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    public static SelectionModifiers ReadModifiers()
    {
        bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        return (Down(0x11) ? SelectionModifiers.Control : 0)
            | (Down(0x10) ? SelectionModifiers.Shift : 0)
            | (Down(0x12) ? SelectionModifiers.Alt : 0)
            | (Down(0x5B) || Down(0x5C) ? SelectionModifiers.Windows : 0);
    }

    public void Dispose()
    {
        if (mouseHook != 0) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = 0; }
        if (keyboardHook != 0) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = 0; }
        hotkey.Dispose();
        searchHotkey.Dispose();
        copyHotkey.Dispose();
        ocrHotkey.Dispose();
        lookupHotkey.Dispose();
        aiHotkey.Dispose();
        customHotkey.Dispose();
        source.RemoveHook(WindowMessage);
    }
}
