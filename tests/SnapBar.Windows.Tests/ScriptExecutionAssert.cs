using SnapBar.Windows;

// Test fixtures only: never use this helper for production selection or clipboard data.
internal static class ScriptExecutionAssert
{
    public static void Result(ScriptExecutionResult actual, string status = "ok", int? exitCode = 0,
        string? output = null, string? error = null)
    {
        if (actual.Status != status || (exitCode.HasValue && actual.ExitCode != exitCode))
            throw new InvalidOperationException($"Expected status={status}, exit={exitCode?.ToString() ?? "any"}; {Describe(actual)}");
        if (output != null) Text(output, actual.Output, "stdout", actual);
        if (error != null) Text(error, actual.Error, "stderr", actual);
    }

    private static void Text(string expected, string actual, string stream, ScriptExecutionResult result)
    {
        if (expected == actual) return;
        var index = 0;
        while (index < Math.Min(expected.Length, actual.Length) && expected[index] == actual[index]) index++;
        throw new InvalidOperationException($"{Describe(result)}; {stream} expected length={expected.Length}, actual length={actual.Length}; "
            + $"first difference at UTF-16 index={index}: expected {Character(expected, index)}, actual {Character(actual, index)}");
    }

    private static string Character(string value, int index) => index == value.Length ? "<EOF>" : value[index] switch
    {
        '\r' => "<CR>", '\n' => "<LF>", '\t' => "<TAB>", _ => $"U+{(int)value[index]:X4}"
    };

    internal static string Describe(ScriptExecutionResult result) =>
        $"status={result.Status}, exit={result.ExitCode?.ToString() ?? "unavailable"}, stdout length={result.Output.Length}, stderr length={result.Error.Length}";
}
