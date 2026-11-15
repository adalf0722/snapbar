using System.Text.Json;
using SnapBar.Core;

internal static class ProductVersionTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "version-cases.json")));
        foreach (var item in cases.RootElement.EnumerateArray())
        {
            var version = item.GetProperty("version").GetString()!;
            var channel = item.GetProperty("channel").GetString();
            check("product version classifies " + JsonSerializer.Serialize(version), () =>
            {
                try
                {
                    var parsed = ProductVersion.Parse(version);
                    return channel != null && parsed.Version == version && parsed.Channel == channel
                        && parsed.IsPublic == (channel is "Stable" or "Preview");
                }
                catch (FormatException) { return channel == null; }
            });
        }
    }
}
