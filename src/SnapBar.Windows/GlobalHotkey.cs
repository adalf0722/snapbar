using SnapBar.Core;

namespace SnapBar.Windows;

/// <summary>Owns one active registration. Persistence failure releases only the proposed replacement.</summary>
public sealed class GlobalHotkey : IDisposable
{
    private readonly nint window;
    private readonly int firstId;
    private int activeId;
    private bool disposed;
    public bool Registered { get; private set; }
    public bool Suppressed { get; set; }
    public HotkeySettings? Current { get; private set; }

    public GlobalHotkey(nint window, HotkeySettings? initial, int firstId = 1)
    {
        if (initial is { IsValid: false }) throw new ArgumentException("Invalid hotkey", nameof(initial));
        this.window = window; this.firstId = activeId = firstId; Current = initial;
        Registered = initial != null && Register(activeId, initial);
    }

    private bool Register(int id, HotkeySettings value) => Native.RegisterHotKey(window, id, 0x4000u | (uint)value.Modifiers, (uint)value.Key);

    public bool TryChange(HotkeySettings? candidate, Action persist)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (candidate is { IsValid: false }) return false;
        if (candidate == null)
        {
            persist();
            if (Registered) Native.UnregisterHotKey(window, activeId);
            Registered = false; Current = null;
            return true;
        }
        if (Registered && candidate == Current) { persist(); return true; }
        var proposedId = activeId == firstId ? firstId + 1 : firstId;
        if (!Register(proposedId, candidate)) return false;
        try { persist(); }
        catch { Native.UnregisterHotKey(window, proposedId); throw; }
        var previousId = activeId; var previousRegistered = Registered;
        activeId = proposedId; Current = candidate; Registered = true;
        if (previousRegistered) Native.UnregisterHotKey(window, previousId);
        return true;
    }

    public bool OwnsMessage(nint id) => id == firstId || id == firstId + 1;
    public bool Matches(nint id, nint combination) => !disposed && Registered && id == activeId && Current is { } current
        && ((long)combination & 0xFFFF) == current.Modifiers && (((long)combination >> 16) & 0xFFFF) == current.Key;
    public bool ShouldTrigger(nint id, nint combination) => !Suppressed && Matches(id, combination);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (Registered) Native.UnregisterHotKey(window, activeId);
        Registered = false;
    }
}
