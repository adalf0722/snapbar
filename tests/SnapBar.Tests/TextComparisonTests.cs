using SnapBar.Core;

internal static class TextComparisonTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        foreach (var (source, result) in new[] { ("", ""), ("原文", ""), ("", "新增"), ("相同", "相同"),
            ("這是舊文字，保留結尾。", "這是新文字，保留結尾。"), ("a\r\nb\t c", "a\nb  c"), ("👩‍💻é原文", "👩‍💻é改寫"),
            (new string('甲', 2000), new string('乙', 2000)) })
            check("text comparison preserves exact original and result " + source.Length + "/" + result.Length, () =>
            {
                var diff = TextComparison.Create(source, result);
                return string.Concat(diff.Original.Select(s => s.Text)) == source && string.Concat(diff.Result.Select(s => s.Text)) == result
                    && string.Concat(diff.Original.Where(s => !s.Changed).Select(s => s.Text)) == string.Concat(diff.Result.Where(s => !s.Changed).Select(s => s.Text));
            });
        check("comparison identifies separate Chinese replacements without marking the intervening text", () =>
        {
            var diff = TextComparison.Create("甲舊乙舊丙", "甲新乙新丙");
            return diff.Original.Count(s => s.Changed) == 2 && diff.Result.Count(s => s.Changed) == 2
                && string.Concat(diff.Result.Where(s => !s.Changed).Select(s => s.Text)) == "甲乙丙";
        });
        check("reader ratios normalize unsafe values without changing other preferences", () =>
        {
            var settings = AiPolicy.Normalize(new() { ReaderHorizontalRatio = double.NaN, ReaderVerticalRatio = 100 });
            return settings.ReaderHorizontalRatio == 0.5 && settings.ReaderVerticalRatio == 0.8 && settings.PinnedIds.Length == 3;
        });
    }
}
