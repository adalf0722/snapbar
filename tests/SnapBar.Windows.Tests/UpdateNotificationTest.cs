using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class UpdateNotificationTest
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { var response = await send(request, token); response.RequestMessage ??= request; return response; }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static UpdateMetadata Metadata(string version, bool desktop = true) => new(1, version, new string('a', 40), desktop,
        new(["改善首次選字與複製的穩定性。"], ["請先結束舊版，再完整解壓縮。"]),
        new(["Improves the reliability of first-time selection and copying."], ["Quit the old version before extracting the new package."]));
    private static object Release(string version, bool desktop = true, bool draft = false, bool complete = true) => new
    {
        tag_name = "v" + version, draft, prerelease = version.Contains('-'),
        body = "<!-- snapbar-update-v1: " + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Metadata(version, desktop), JsonOptions)) + " -->",
        assets = new[] { new { name = "SnapBar-v" + version + "-win-x64.zip", state = "uploaded", size = 100 },
            new { name = "SnapBar-v" + version + "-win-x64.zip.sha256", state = complete ? "uploaded" : "new", size = 64 } }
    };
    private static HttpResponseMessage Response(object[] releases) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases)) };

    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Width = 760, Height = 600, ShowActivated = false, Title = "SnapBar update fixture" };
        int passed = 0, failed = 0;
        void Check(string name, bool condition)
        { if (condition) { passed++; Console.WriteLine("PASS " + name); } else { failed++; Console.WriteLine("FAIL " + name); } }
        window.Loaded += async (_, _) =>
        {
            var directory = Directory.CreateTempSubdirectory("SnapBar.Updates.");
            try
            {
                using (var client = new HttpClient(new Handler((request, _) =>
                {
                    Check("update request has fixed source and no authentication", request.RequestUri!.Host == "api.github.com"
                        && request.Headers.Authorization == null && request.Headers.UserAgent.ToString().Contains("SnapBar"));
                    return Task.FromResult(Response([Release("1.1.0"), Release("1.2.0", draft: true), Release("1.3.0", complete: false)]));
                })))
                using (var source = new GitHubUpdateSource(client))
                {
                    var result = await source.ReadAsync(default);
                    Check("source accepts only public complete packages with metadata", result.Count == 1 && result[0].Metadata?.DesktopChanged == true);
                }
                int pages = 0;
                using (var client = new HttpClient(new Handler((_, _) =>
                {
                    pages++;
                    var result = Response(Enumerable.Range((pages - 1) * 100, pages == 1 ? 100 : 1).Select(i => Release("1.0." + i)).ToArray());
                    if (pages == 1) result.Headers.Add("Link", "<https://api.github.com/repos/adalf0722/snapbar/releases?per_page=100&page=2>; rel=\"next\"");
                    return Task.FromResult(result);
                })))
                using (var source = new GitHubUpdateSource(client))
                    Check("source reads beyond the first 100 releases", (await source.ReadAsync(default)).Count == 101 && pages == 2);
                foreach (var link in new[] { "https://evil.example/releases?per_page=100&page=2", "https://api.github.com/repos/other/repo/releases?per_page=100&page=2" })
                {
                    var calls = 0;
                    using var client = new HttpClient(new Handler((_, _) =>
                    {
                        calls++; var result = Response([]); result.Headers.Add("Link", "<" + link + ">; rel=\"next\""); return Task.FromResult(result);
                    }));
                    using var source = new GitHubUpdateSource(client);
                    bool rejected = false; try { await source.ReadAsync(default); } catch (InvalidDataException) { rejected = true; }
                    Check("unsafe pagination is rejected before following it", rejected && calls == 1);
                }
                using (var client = new HttpClient(new Handler((_, _) =>
                {
                    var result = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    result.Headers.RetryAfter = new(TimeSpan.FromHours(2)); return Task.FromResult(result);
                })))
                using (var source = new GitHubUpdateSource(client))
                {
                    bool limited = false; try { await source.ReadAsync(default); }
                    catch (UpdateRateLimitException error) { limited = error.RetryAt > DateTimeOffset.UtcNow.AddMinutes(119); }
                    Check("rate limit preserves server retry time", limited);
                }
                foreach (var invalid in new object[][] {
                    [Release("1.1.0"), Release("1.1.0", complete: false)],
                    [new { tag_name = "v1.1.0", draft = false, prerelease = true, assets = Array.Empty<object>() }],
                    [new { tag_name = "v1.1.0", draft = false }]
                })
                {
                    using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(invalid))));
                    using var source = new GitHubUpdateSource(client);
                    bool rejected = false; try { await source.ReadAsync(default); }
                    catch (Exception error) when (error is InvalidDataException or KeyNotFoundException) { rejected = true; }
                    Check("duplicate, contradictory or malformed identities fail the whole scan", rejected);
                }
                using (var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(new string('x', 8 * 1024 * 1024 + 1)) }))))
                using (var source = new GitHubUpdateSource(client))
                {
                    bool rejected = false; try { await source.ReadAsync(default); } catch (InvalidDataException) { rejected = true; }
                    Check("oversized responses are rejected", rejected);
                }
                var pageCount = 0;
                using (var client = new HttpClient(new Handler((_, _) =>
                {
                    var result = Response([]); pageCount++;
                    result.Headers.Add("Link", $"<https://api.github.com/repos/adalf0722/snapbar/releases?per_page=100&page={pageCount + 1}>; rel=\"next\"");
                    return Task.FromResult(result);
                })))
                using (var source = new GitHubUpdateSource(client))
                {
                    bool rejected = false; try { await source.ReadAsync(default); } catch (InvalidDataException) { rejected = true; }
                    Check("page cap never returns a partial success", rejected && pageCount == 10);
                }
                using (var client = new HttpClient(new Handler(async (_, token) =>
                { await Task.Delay(Timeout.Infinite, token); return Response([]); })))
                using (var source = new GitHubUpdateSource(client))
                using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(20)))
                {
                    bool cancelled = false; try { await source.ReadAsync(cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
                    Check("network cancellation propagates", cancelled);
                }

                var clock = DateTimeOffset.UtcNow;
                var statePath = Path.Combine(directory.FullName, "updates.json");
                var store = new UpdateStateStore(statePath);
                int requests = 0, notifications = 0;
                bool busy = true;
                var release = new UpdateRelease("1.1.0", Metadata("1.1.0"));
                var traySettings = 0; var presentations = 0; UpdateRelease? clicked = null;
                using (var tray = new TrayIcon(() => traySettings++, () => { }, () => { }, (_, _, _) => presentations++))
                {
                    tray.UpdateClicked = value => clicked = value;
                    tray.NotifyHidden(); tray.HandleBalloonClick();
                    Check("hidden tray notification opens ordinary settings", traySettings == 1 && clicked == null);
                    tray.NotifyUpdate(release); tray.NotifyHidden(); tray.HandleBalloonClick();
                    Check("hidden notice cannot replace an active update click target", presentations == 2 && clicked?.Version == "1.1.0" && traySettings == 1);
                    tray.NotifyHidden(); tray.HandleBalloonClick();
                    Check("subsequent hidden tray notice resets its update target", traySettings == 2 && presentations == 3);
                }
                Task<IReadOnlyList<UpdateRelease>> Read(CancellationToken token)
                { requests++; return Task.FromResult<IReadOnlyList<UpdateRelease>>([release, new("1.2.0", Metadata("1.2.0", false))]); }
                using (var coordinator = new UpdateCoordinator("1.0.0", store, Read, () => busy, _ => notifications++, () => clock))
                {
                    await coordinator.TickAsync(); Check("startup check waits thirty seconds", requests == 0);
                    clock += TimeSpan.FromSeconds(30); await coordinator.TickAsync();
                    Check("busy operation defers notification while preserving latest package", requests == 1 && notifications == 0
                        && coordinator.State.Latest?.Version == "1.2.0" && coordinator.State.Notification?.Version == "1.1.0");
                    busy = false; await coordinator.TickAsync();
                    Check("idle tick notifies the exact desktop version once", notifications == 1 && coordinator.State.NotifiedVersions.Contains("1.1.0"));
                    await coordinator.TickAsync(); Check("automatic checks are throttled", requests == 1 && notifications == 1);
                    await coordinator.CheckAsync(); Check("manual cooldown prevents repeat requests", requests == 1);
                    clock += TimeSpan.FromSeconds(61); await coordinator.CheckAsync();
                    Check("manual check bypasses daily interval without notifying", requests == 2 && notifications == 1);
                    coordinator.SetEnabled(false); clock += TimeSpan.FromDays(2); await coordinator.TickAsync();
                    Check("disabled automatic checks stay off", requests == 2);
                    await coordinator.CheckAsync(); Check("manual checks remain available when automatic checks are off", requests == 3);
                    using (var disabledPanel = new UpdateSettingsPanel(coordinator))
                    {
                        var button = disabledPanel.Children.OfType<WrapPanel>().SelectMany(row => row.Children.OfType<Button>())
                            .Single(item => AutomationProperties.GetAutomationId(item) == "CheckUpdates");
                        Check("manual button respects cooldown while automatic checks are off", !button.IsEnabled);
                        clock += TimeSpan.FromSeconds(61); await coordinator.TickAsync();
                        Check("manual button recovers after cooldown while automatic checks are off", button.IsEnabled);
                    }
                    coordinator.SetEnabled(true);
                }
                using (var restarted = new UpdateCoordinator("1.0.0", new UpdateStateStore(statePath), Read, () => false, _ => notifications++, () => clock))
                {
                    clock += TimeSpan.FromSeconds(31); await restarted.TickAsync();
                    Check("restart preserves interval and notification dedupe", requests == 3 && notifications == 1);
                }
                using (var upgraded = new UpdateCoordinator("1.3.0", new UpdateStateStore(statePath), Read, () => false, _ => notifications++, () => clock))
                    Check("installed upgrade removes stale cached download", upgraded.State.Latest == null);
                using (var dev = new UpdateCoordinator("1.0.0-dev", new UpdateStateStore(statePath), Read, () => false, _ => notifications++, () => clock))
                { await dev.CheckAsync(); clock += TimeSpan.FromDays(1); await dev.TickAsync(); Check("development builds never query or advertise upgrades", requests == 3 && dev.State.Latest == null); }

                var late = new TaskCompletionSource<IReadOnlyList<UpdateRelease>>();
                using (var cancelled = new UpdateCoordinator("1.0.0", new UpdateStateStore(Path.Combine(directory.FullName, "late.json")),
                    _ => late.Task, () => false, _ => notifications++, () => clock))
                {
                    var work = cancelled.CheckAsync(); cancelled.SetEnabled(false); late.SetResult([release]); await work;
                    Check("late response cannot change state after disabling checks", cancelled.State.Latest == null && !cancelled.State.Enabled
                        && cancelled.Status != "Updates_Checking" && !cancelled.Checking);
                }
                var disposalLate = new TaskCompletionSource<IReadOnlyList<UpdateRelease>>();
                var disposed = new UpdateCoordinator("1.0.0", new UpdateStateStore(Path.Combine(directory.FullName, "disposed.json")),
                    _ => disposalLate.Task, () => false, _ => notifications++, () => clock);
                var disposalWork = disposed.CheckAsync(); disposed.Dispose(); disposalLate.SetResult([release]); await disposalWork;
                Check("late response cannot change state after disposal", disposed.State.Latest == null);

                var cacheStore = new UpdateStateStore(Path.Combine(directory.FullName, "cache.json")); cacheStore.Load();
                cacheStore.Save(new() { Latest = release, Notification = release, LastSuccessUtc = clock });
                using (var failedCheck = new UpdateCoordinator("1.0.0", cacheStore,
                    _ => throw new HttpRequestException(), () => true, _ => notifications++, () => clock))
                {
                    await failedCheck.CheckAsync();
                    Check("failed check retains cached result and honest timestamp", failedCheck.Status == "Updates_Failed"
                        && failedCheck.State.Latest?.Version == "1.1.0" && failedCheck.State.LastSuccessUtc == clock);
                }
                using (var withdrawn = new UpdateCoordinator("1.0.0", cacheStore,
                    _ => Task.FromResult<IReadOnlyList<UpdateRelease>>([]), () => false, _ => notifications++, () => clock.AddMinutes(2)))
                { await withdrawn.CheckAsync(); Check("successful check removes withdrawn releases", withdrawn.State.Latest == null && withdrawn.State.Notification == null); }

                var manualNotifications = 0;
                using (var manual = new UpdateCoordinator("1.0.0", new UpdateStateStore(Path.Combine(directory.FullName, "manual.json")),
                    Read, () => false, _ => manualNotifications++, () => clock))
                {
                    await manual.CheckAsync(); clock += TimeSpan.FromMinutes(2); await manual.TickAsync();
                    Check("fresh manual discovery is acknowledged without a later balloon", manual.State.Latest != null && manualNotifications == 0);
                }
                var failedPath = Path.Combine(directory.FullName, "save-failed.json");
                using (var saveFailed = new UpdateCoordinator("1.0.0", new UpdateStateStore(failedPath), _ =>
                {
                    File.WriteAllText(failedPath, "{}", new UTF8Encoding(false));
                    return Task.FromResult<IReadOnlyList<UpdateRelease>>([release]);
                }, () => false, _ => manualNotifications++, () => clock))
                {
                    clock += TimeSpan.FromSeconds(31); await saveFailed.TickAsync();
                    Check("save failure suppresses notification and preserves protected file", manualNotifications == 0
                        && !saveFailed.StorageAvailable && File.ReadAllText(failedPath) == "{}");
                }

                using var ui = new UpdateCoordinator("1.0.0", new UpdateStateStore(Path.Combine(directory.FullName, "ui.json")), Read, () => false, _ => { });
                await ui.CheckAsync();
                using var panel = new UpdateSettingsPanel(ui);
                window.Content = new ScrollViewer { Content = new Border { Padding = new(24), Background = Brushes.White, Child = panel } };
                var originalLanguage = UiText.Culture.Name;
                foreach (var language in new[] { "zh-Hant", "en" })
                {
                    UiText.SetLanguage(language); UiText.Refresh(panel);
                    panel.ShowRelease(release);
                    foreach (var width in new[] { 760, 460 })
                    {
                        window.Width = width; window.UpdateLayout();
                        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        image.Render(window);
                        var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "checks", "updates-preview"); Directory.CreateDirectory(output);
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                        using var file = File.Create(Path.Combine(output, language + "-" + width + ".png")); png.Save(file);
                    }
                    Check("update panel retains exact notification target in " + language, panel.DisplayedRelease?.Version == "1.1.0");
                }
                UiText.SetLanguage(originalLanguage);
                Console.WriteLine($"Updates: {passed} passed, {failed} failed.");
            }
            catch (Exception error) { failed++; Console.WriteLine("FAIL update fixture: " + error); }
            finally { directory.Delete(true); window.Close(); app.Shutdown(); }
        };
        window.Show(); app.Run(); return failed == 0 ? 0 : 1;
    }
}
