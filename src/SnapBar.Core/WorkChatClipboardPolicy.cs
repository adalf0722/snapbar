namespace SnapBar.Core;

public static class WorkChatClipboardPolicy
{
    // MSAA STATE_SYSTEM_PROTECTED and roles; never infer write permission from these.
    private const int Protected = 0x20000000;

    public static string Check(bool knownApplication, bool ownedNativeFocusAndPoint, bool mainChatWindow,
        bool passwordStyle, int? pointRole, int? pointState, int? focusRole, int? focusState)
    {
        if (passwordStyle || (pointState.GetValueOrDefault() & Protected) != 0
            || (focusState.GetValueOrDefault() & Protected) != 0) return "password";
        if (!knownApplication || !ownedNativeFocusAndPoint || !mainChatWindow) return "clipboard-unavailable";
        const int unavailableOrHidden = 0x1 | 0x8000 | 0x10000;
        if ((pointState.GetValueOrDefault() & unavailableOrHidden) != 0
            || (focusState.GetValueOrDefault() & unavailableOrHidden) != 0) return "clipboard-unavailable";
        // WeCom's custom main window can omit MSAA roles entirely. A known non-text
        // role still denies copying; missing roles are scoped to this native profile.
        static bool TextOrOpaque(int? role) => role is null or 0 or 9 or 10 or 15 or 42;
        return TextOrOpaque(pointRole) && TextOrOpaque(focusRole) ? "ok" : "clipboard-unavailable";
    }
}
