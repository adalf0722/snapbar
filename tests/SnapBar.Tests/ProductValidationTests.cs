using System.Globalization;
using SnapBar.Core;

internal static class ProductValidationTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        var action = new CustomActionDefinition("custom-" + Guid.NewGuid().ToString("N"), "My action",
            CustomActionKind.Url, "search", "https://example.com/?q=***");
        check("action validation codes do not depend on UI culture", () =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                foreach (var culture in new[] { "en-US", "zh-TW" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                    if (CustomActionPolicy.Validate(action with { Name = "" }) != "action-name-invalid"
                        || CustomActionPolicy.Validate(action with { TimeoutSeconds = 301 }) != "action-timeout-invalid"
                        || CustomActionPolicy.Validate(action with { Content = "https://***.example.com/" }) != "action-template-invalid")
                        return false;
                }
                return CustomActionPolicy.Validate(action) == null;
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        });
        check("malformed imported actions expose a stable product error without source text", () =>
        {
            try { CustomActionPolicy.Import("{private source"); return false; }
            catch (ProductValidationException error)
            { return error.Code == "action-import-invalid" && error.Arguments.Count == 0 && !error.Message.Contains("private"); }
        });
        check("duplicate imported fields retain rejection with a stable code", () =>
        {
            try { CustomActionPolicy.Import("{\"version\":2,\"Version\":2}"); return false; }
            catch (ProductValidationException error) { return error.Code == "action-import-duplicate-field"; }
        });
        check("pin-limit errors carry their numeric limit independently of prose", () =>
        {
            var customs = Enumerable.Range(0, 10).Select(i => action with
                { Id = "custom-" + Guid.NewGuid().ToString("N"), Name = "Item " + i }).ToArray();
            try
            {
                ActionCatalog.Normalize(new() { CustomActions = customs,
                    Layout = customs.Select(a => new ActionLayoutEntry(a.Id, ActionPlacement.Bar)).ToArray() });
                return false;
            }
            catch (ProductValidationException error)
            { return error.Code == "action-bar-limit" && error.Arguments.Count == 1 && Equals(error.Arguments[0], 9); }
        });
        check("AI endpoint and model validation remain distinguishable without English matching", () =>
        {
            var profile = new AiProviderProfile("test", "My service", AiProtocol.ChatCompletions, "https://example.com/v1/chat/completions", "model");
            return AiPolicy.ValidateProfile(profile) == null
                && AiPolicy.ValidateProfile(profile with { Endpoint = "file:///private" }) == "ai-endpoint-invalid"
                && AiPolicy.ValidateProfile(profile with { Model = "" }) == "ai-model-invalid";
        });
        check("missing AI prompt errors remain catchable by existing argument handlers", () =>
        {
            try { AiPromptCatalog.Delete(new(), "missing", out _); return false; }
            catch (ArgumentException error) { return error is ProductValidationException { Code: "ai-prompt-not-found" }; }
        });
        check("dictionary extension rejection carries structured product information with the original exception type", () =>
        {
            try { new OfflineDictionary("unused").Import("private.txt"); return false; }
            catch (InvalidDataException error)
            {
                var detail = ProductError.FromException(error);
                return detail is { Code: "dictionary-ifo-required" } && detail.Arguments.Count == 0
                    && !error.Message.Contains("private");
            }
        });
        check("external exception prose is not treated as a product validation code", () =>
            ProductError.FromException(new InvalidDataException("dictionary-ifo-required")) == null
            && ProductError.FromException(new ArgumentException("action-name-invalid")) == null
            && ProductError.FromException(new ProductValidationException("action-bar-limit", 9)) is { Code: "action-bar-limit" });
        check("corrupt dictionary manifest exposes a product code and preserves the original file", () =>
        {
            var folder = Directory.CreateTempSubdirectory("snapbar-manifest-error-");
            try
            {
                var legacy = Path.Combine(folder.FullName, "dictionary.bin");
                var library = Directory.CreateDirectory(legacy + ".library");
                var manifest = Path.Combine(library.FullName, "library.json");
                File.WriteAllText(manifest, "{broken", new System.Text.UTF8Encoding(false));
                try { _ = new OfflineDictionaryLibrary(legacy).Dictionaries; return false; }
                catch (InvalidDataException error)
                { return ProductError.FromException(error) is { Code: "dictionary-manifest-invalid" } && File.ReadAllText(manifest) == "{broken"; }
            }
            finally { folder.Delete(true); }
        });
    }
}
