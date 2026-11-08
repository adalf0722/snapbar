using SnapBar.App;
using SnapBar.Core;

internal static class ActionOutputRouterTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        foreach (var (mode, expected) in new[] {
            (ActionOutput.None, "completed"), (ActionOutput.Clipboard, "copy:output"),
            (ActionOutput.ReplaceSelection, "replace:output"), (ActionOutput.Hud, "show:output:True"),
            (ActionOutput.Window, "show:output:True"), (ActionOutput.Markdown, "show:output:True") })
        {
            check("shared output routes " + mode, () =>
            {
                var calls = new List<string>();
                var host = Host(calls, () => true);
                var keep = ActionOutputRouter.Route(mode, "output", false, false, host);
                return calls.SequenceEqual([expected]) && keep == (mode is ActionOutput.Window or ActionOutput.Markdown);
            });
            check("stale output has no effect " + mode, () =>
            {
                var calls = new List<string>();
                return !ActionOutputRouter.Route(mode, "output", false, true, Host(calls, () => false)) && calls.Count == 0;
            });
        }
        check("failed replace preserves output and stderr diagnostic", () =>
        {
            var calls = new List<string>();
            var host = Host(calls, () => true) with { Replace = text => { calls.Add("replace:" + text); return false; } };
            return ActionOutputRouter.Route(ActionOutput.ReplaceSelection, "output", false, true, host)
                && calls.SequenceEqual(["replace:output", "show:output:False", "diagnostic"]);
        });
        check("script failure shows diagnostic only", () =>
        {
            var calls = new List<string>();
            return ActionOutputRouter.Route(ActionOutput.Clipboard, "output", true, true, Host(calls, () => true))
                && calls.SequenceEqual(["diagnostic"]);
        });
    }

    private static ActionOutputHost Host(List<string> calls, Func<bool> current) => new(current,
        () => calls.Add("completed"), text => calls.Add("copy:" + text),
        text => { calls.Add("replace:" + text); return true; },
        (text, apply) => calls.Add($"show:{text}:{apply}"), () => calls.Add("diagnostic"));
}
