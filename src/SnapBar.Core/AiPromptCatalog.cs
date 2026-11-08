namespace SnapBar.Core;

public sealed record DeletedAiPrompt(AiPrompt Prompt, int Index, int PinnedIndex);

public static class AiPromptCatalog
{
    public static IReadOnlyList<AiPrompt> Search(AiSettings settings, string query, string? category = null)
    {
        settings = AiPolicy.Normalize(settings);
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return settings.Prompts.Where(prompt => (category == null || prompt.Category == category)
            && words.All(word => (prompt.Name + "\n" + prompt.Instruction + "\n" + prompt.Keywords)
                .Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public static AiSettings Delete(AiSettings settings, string id, out DeletedAiPrompt deletion)
    {
        settings = AiPolicy.Normalize(settings);
        var index = Array.FindIndex(settings.Prompts, prompt => prompt.Id == id);
        if (index < 0) throw new ProductValidationException("ai-prompt-not-found");
        deletion = new(settings.Prompts[index], index, Array.IndexOf(settings.PinnedIds, id));
        return AiPolicy.Normalize(settings with { Prompts = settings.Prompts.Where(prompt => prompt.Id != id).ToArray() });
    }

    public static AiSettings Restore(AiSettings settings, DeletedAiPrompt deletion)
    {
        settings = AiPolicy.Normalize(settings);
        ArgumentNullException.ThrowIfNull(deletion);
        ArgumentNullException.ThrowIfNull(deletion.Prompt);
        if (settings.Prompts.Any(prompt => prompt.Id == deletion.Prompt.Id))
            throw new ProductValidationException("ai-prompt-duplicate");
        if (settings.Prompts.Length >= 128)
            throw new ProductValidationException("ai-prompt-limit", 128);
        var prompts = settings.Prompts.ToList();
        prompts.Insert(Math.Clamp(deletion.Index, 0, prompts.Count), deletion.Prompt);
        var pins = settings.PinnedIds.ToList();
        if (deletion.PinnedIndex >= 0)
            pins.Insert(Math.Min(deletion.PinnedIndex, pins.Count), deletion.Prompt.Id);
        return AiPolicy.Normalize(settings with { Prompts = prompts.ToArray(), PinnedIds = pins.ToArray() });
    }
}
