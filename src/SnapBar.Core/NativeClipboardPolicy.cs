namespace SnapBar.Core;

// Capability-based eligibility for applications without a dedicated adapter.
public static class NativeClipboardPolicy
{
    public static string Check(bool ownedFocusAndPoint, bool sameNativeControl, bool passwordStyle,
        int? pointRole, int? pointState, int? focusRole, int? focusState)
    {
        const int protectedState = 0x20000000, focusedState = 0x4;
        const int unavailableOrHidden = 0x1 | 0x8000 | 0x10000;
        if (passwordStyle || (pointState.GetValueOrDefault() & protectedState) != 0
            || (focusState.GetValueOrDefault() & protectedState) != 0) return "password";
        if (!ownedFocusAndPoint || !sameNativeControl || pointRole is not (15 or 42)
            || focusRole is not (15 or 42) || pointState == null || focusState == null)
            return "clipboard-unavailable";
        if ((pointState.Value & unavailableOrHidden) != 0 || (focusState.Value & unavailableOrHidden) != 0
            || (pointState.Value & focusedState) == 0 || (focusState.Value & focusedState) == 0)
            return "clipboard-unavailable";
        return "ok";
    }
}
