using System.Text;
using SnapBar.Core;

internal static class OnboardingTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Onboarding." + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "onboarding.json");
        try
        {
            check("onboarding only offers a readable fresh manual launch", () =>
            {
                var missing = new OnboardingLoadResult(OnboardingLoadKind.Missing);
                return OnboardingPolicy.ShouldOffer(false, false, true, missing)
                    && !OnboardingPolicy.ShouldOffer(true, false, true, missing)
                    && !OnboardingPolicy.ShouldOffer(false, true, true, missing)
                    && !OnboardingPolicy.ShouldOffer(false, false, false, missing)
                    && Enum.GetValues<OnboardingLoadKind>().Where(kind => kind != OnboardingLoadKind.Missing)
                        .All(kind => !OnboardingPolicy.ShouldOffer(false, false, true, new(kind)));
            });
            check("onboarding missing state does not create files", () =>
                new OnboardingStore(path).Load().Kind == OnboardingLoadKind.Missing && !File.Exists(path));
            check("onboarding persists progress without BOM and never downgrades completion", () =>
            {
                var store = new OnboardingStore(path);
                store.Save(new(1, OnboardingStatus.Presented));
                if (new OnboardingStore(path).Load().State?.Status != OnboardingStatus.Presented) return false;
                store.Save(new(1, OnboardingStatus.Skipped));
                if (store.Load().State?.Status != OnboardingStatus.Skipped) return false;
                store.Save(new(1, OnboardingStatus.Completed));
                var bytes = File.ReadAllBytes(path);
                store.Save(new(1, OnboardingStatus.Presented)); store.Save(new(1, OnboardingStatus.Skipped));
                return bytes.SequenceEqual(File.ReadAllBytes(path))
                    && !bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 });
            });
            foreach (var pair in new[] {
                ("{", OnboardingLoadKind.Invalid), ("{\"version\":2,\"status\":0}", OnboardingLoadKind.Unsupported),
                ("{\"version\":1}", OnboardingLoadKind.Invalid),
                ("{\"version\":1,\"status\":0,\"unknown\":true}", OnboardingLoadKind.Invalid),
                ("{\"version\":1,\"status\":99}", OnboardingLoadKind.Invalid),
                (new string('x', 4097), OnboardingLoadKind.Invalid) })
                check("onboarding preserves rejected state " + pair.Item2 + " " + pair.Item1.Length, () =>
                {
                    File.WriteAllText(path, pair.Item1, new UTF8Encoding(false));
                    var store = new OnboardingStore(path);
                    if (store.Load().Kind != pair.Item2) return false;
                    try { store.Save(new(1, OnboardingStatus.Completed)); return false; }
                    catch (InvalidOperationException) { return File.ReadAllText(path) == pair.Item1; }
                });
            check("onboarding locked state rejects saving and preserves original", () =>
            {
                File.WriteAllText(path, "{\"version\":1,\"status\":0}", new UTF8Encoding(false));
                var before = File.ReadAllBytes(path);
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var store = new OnboardingStore(path);
                    if (store.Load().Kind != OnboardingLoadKind.Unavailable) return false;
                    try { store.Save(new(1, OnboardingStatus.Completed)); return false; }
                    catch (InvalidOperationException) { }
                }
                return before.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(directory).Length == 1;
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
