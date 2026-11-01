using System.IO;
using SnapBar.App;
using SnapBar.Core;

internal static class AiManagementTest
{
    public static void Run(string directory, Action<bool, string> check)
    {
        var root = Path.Combine(directory, "management");
        Directory.CreateDirectory(root);
        var preferences = new AiPreferences(root);
        var changed = 0; var undoChanged = 0;
        preferences.Changed += () => changed++;
        preferences.UndoChanged += () => undoChanged++;
        check(preferences.DeletePrompt("improve", out _) && preferences.LastDeletion?.Prompt.Id == "improve"
            && !new AiPreferences(root).Settings.Prompts.Any(p => p.Id == "improve") && changed == 1 && undoChanged == 1,
            "AI shared delete persists once and announces recoverable deletion");
        var additions = new AiPrompt("management-new", "Disposable command", "Return the input.", "");
        check(preferences.TrySavePrompt(additions, true, out _) && preferences.TogglePin("summary", out _)
            && preferences.TryRestoreDeleted(out _) && preferences.LastDeletion == null
            && preferences.Settings.Prompts[1].Id == "improve" && preferences.Settings.Prompts.Last().Id == additions.Id
            && preferences.Settings.PinnedIds.Contains("summary") && undoChanged == 2,
            "AI shared restore merges new commands and pin changes without losing them");
        check(!preferences.TrySavePrompt(additions, true, out _) && preferences.Settings.Prompts.Count(p => p.Id == additions.Id) == 1,
            "AI shared create rejects duplicate command identity");
        check(preferences.DeletePrompt("summary", out _) && !preferences.TrySavePrompt(new("summary", "Old draft", "instruction", ""), false, out _)
            && preferences.LastDeletion?.Prompt.Id == "summary",
            "AI editor cannot resurrect a command deleted while its draft was open");

        var originalUndo = preferences.LastDeletion;
        var current = preferences.Settings;
        var path = Path.Combine(root, "ai.json");
        File.Delete(path); Directory.CreateDirectory(path);
        check(!preferences.DeletePrompt("grammar", out _) && preferences.Settings == current
            && preferences.LastDeletion == originalUndo && undoChanged == 3,
            "AI failed delete keeps current commands and the earlier undo record");
        check(!preferences.TryRestoreDeleted(out _) && preferences.Settings == current
            && preferences.LastDeletion == originalUndo && undoChanged == 3,
            "AI failed restore retains deleted state and retryable undo");
        Directory.Delete(path);
        check(preferences.TryRestoreDeleted(out _) && new AiPreferences(root).Settings.Prompts.Any(p => p.Id == "summary"),
            "AI restore succeeds after persistence becomes available again");
        check(preferences.DeletePrompt("grammar", out _) && preferences.DeletePrompt("shorten", out _)
            && preferences.TryRestoreDeleted(out _) && !preferences.Settings.Prompts.Any(p => p.Id == "grammar")
            && preferences.Settings.Prompts.Any(p => p.Id == "shorten"),
            "AI only the most recent successful deletion can be restored");
        check(preferences.DeletePrompt("translate-en", out _) && new AiPreferences(root).LastDeletion == null,
            "AI undo is process memory and is not persisted as a recycle bin");
    }
}
