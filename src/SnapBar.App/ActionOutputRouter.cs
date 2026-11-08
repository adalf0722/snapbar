using SnapBar.Core;

namespace SnapBar.App;

// The source owns authorization and deferred Apply. Every callback is invoked only while current.
internal sealed record ActionOutputHost(
    Func<bool> IsCurrent,
    Action Completed,
    Action<string> Copy,
    Func<string, bool> Replace,
    Action<string, bool> ShowResult,
    Action ShowDiagnostic);

internal static class ActionOutputRouter
{
    // Returns whether the source should retain the result session.
    public static bool Route(ActionOutput output, string text, bool failure, bool hasError, ActionOutputHost host)
    {
        if (!host.IsCurrent()) return false;
        if (failure) { host.ShowDiagnostic(); return true; }
        var keep = false;
        switch (output)
        {
            case ActionOutput.None: host.Completed(); break;
            case ActionOutput.Clipboard: host.Copy(text); break;
            case ActionOutput.ReplaceSelection:
                if (!host.Replace(text) && host.IsCurrent()) host.ShowResult(text, false);
                break;
            case ActionOutput.Hud:
            case ActionOutput.Window:
            case ActionOutput.Markdown:
                keep = output != ActionOutput.Hud || hasError;
                host.ShowResult(text, true);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(output));
        }
        if (hasError && output is ActionOutput.None or ActionOutput.Clipboard or ActionOutput.ReplaceSelection && host.IsCurrent())
        { keep = true; host.ShowDiagnostic(); }
        return keep;
    }
}
