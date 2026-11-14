using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Controls;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class OnboardingTest
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Children<T>(child)) yield return descendant;
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Children<T>(root).Single(c => AutomationProperties.GetAutomationId(c) == id);

    internal static void Ui(Action<bool, string> check, bool snapshots = true)
    {
        var culture = UiText.Culture.Name;
        var messages = new List<OnboardingMessage>();
        var window = new OnboardingPracticeWindow(message => { messages.Add(message); return Task.CompletedTask; });
        try
        {
            window.Receive(new(1, "configure", Language: "en", Shortcut: "Ctrl + Alt + V", Automatic: false, HotkeyRegistered: true));
            window.UpdateLayout();
            check(Find<TextBox>(window, "OnboardingSample").SelectedText.Length == 0
                && !Find<CheckBox>(window, "OnboardingAutomatic").IsVisible
                && !Children<Button>(window).Any(b => AutomationProperties.GetAutomationId(b) is "OnboardingStart" or "OnboardingContinue"),
                "onboarding opens directly with unselected practice text and no start or continue gate");
            if (snapshots) UiAppearanceTest.Snapshot(window, "onboarding-en.png");
            window.Receive(new(1, "configure", Language: "zh-Hant", Shortcut: "Ctrl + Alt + B", Automatic: false, HotkeyRegistered: true));
            var keycaps = Find<WrapPanel>(window, "OnboardingShortcutKeys");
            check(AutomationProperties.GetName(keycaps) == "Ctrl + Alt + B"
                && Children<TextBlock>(keycaps).Any(t => t.Text == "B"),
                "onboarding keycaps display the actual saved shortcut with a complete accessible name");
            window.UpdateLayout();
            if (snapshots) UiAppearanceTest.Snapshot(window, "onboarding-zh.png");
            window.Receive(new(1, "configure", Language: "en", Shortcut: "Ctrl + Alt + V", Automatic: false, HotkeyRegistered: true));
            window.Receive(new(1, "search-failed", Error: "browser-unavailable"));
            check(!window.Completed && Find<TextBlock>(window, "OnboardingProgress").Text.Contains("browser"),
                "failed browser launch does not complete onboarding");
            window.Receive(new(1, "search-launched"));
            window.UpdateLayout();
            check(window.Completed && Find<CheckBox>(window, "OnboardingAutomatic").IsVisible
                && !Find<Button>(window, "OnboardingSkip").IsVisible,
                "real search completion expands preferences in the same window");
            var language = Find<ComboBox>(window, "OnboardingLanguage"); language.SelectedIndex = 2;
            var request = messages.Last();
            window.Receive(new(1, "preference-result", Language: "zh-Hant", Automatic: true, RequestId: request.RequestId + 1));
            check(!language.IsEnabled && Find<TextBox>(window, "OnboardingSample").Text.StartsWith("SnapBar keeps"),
                "onboarding ignores preference responses from another request");
            window.Receive(new(1, "preference-result", Language: "en", Automatic: false,
                RequestId: request.RequestId, Error: "save-failed"));
            check(Find<ComboBox>(window, "OnboardingLanguage").SelectedIndex == 1
                && !Find<CheckBox>(window, "OnboardingAutomatic").IsChecked!.Value,
                "failed onboarding preference restores the actual saved settings");
            window.Receive(new(1, "configure", Language: "zh-Hant", Shortcut: "Ctrl + Alt + B", Automatic: false, HotkeyRegistered: false));
            window.Width = 580; window.Height = 470; window.UpdateLayout();
            check(Find<TextBox>(window, "OnboardingSample").Text.Contains("文字操作")
                && Find<Button>(window, "OnboardingChangeShortcut").IsVisible,
                "completed Chinese onboarding still offers shortcut conflict recovery");
            var finish = Find<Button>(window, "OnboardingFinish");
            var finishBounds = finish.TransformToAncestor(window).TransformBounds(new Rect(finish.RenderSize));
            check(finish.IsVisible && finishBounds.Bottom <= window.ActualHeight && finishBounds.Top >= 0,
                "onboarding primary action remains in view when the content must scroll");
            if (snapshots) UiAppearanceTest.Snapshot(window, "onboarding-small-conflict.png");
            window.Receive(new(1, "configure", Language: "zh-Hant", Shortcut: "Ctrl + Alt + V", Automatic: false, HotkeyRegistered: true));
            window.Width = 660; window.Height = 590; window.UpdateLayout();
            if (snapshots) UiAppearanceTest.Snapshot(window, "onboarding-complete-zh.png");
        }
        finally { window.CloseFromParent(); UiText.SetLanguage(culture); }
    }

    internal static async Task Runtime(Action<string, bool> check, string parentDirectory)
    {
        var directory = Path.Combine(parentDirectory, "onboarding"); Directory.CreateDirectory(directory);
        MainWindow? host = null;
        try
        {
            foreach (var invalid in new[] {
                "{\"version\":2,\"kind\":\"skip\"}",
                "{\"version\":1,\"kind\":\"skip\",\"window\":42}",
                "{\"version\":1,\"kind\":\"skip\",\"kind\":\"finish\"}",
                "{\"version\":1,\"kind\":\"set-language\",\"requestId\":1,\"language\":\"unknown\"}",
                "{\"version\":1,\"kind\":\"close\"}" })
            {
                var rejected = false;
                try { OnboardingProtocol.Parse(Encoding.UTF8.GetBytes(invalid), fromChild: true); }
                catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException) { rejected = true; }
                check("onboarding rejects invalid command " + invalid, rejected);
            }
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 4097))))
            using (var channel = new OnboardingChannel(stream, true))
            {
                var rejected = false;
                try { await channel.ReadAsync(default); } catch (InvalidDataException) { rejected = true; }
                check("onboarding reader rejects overlong messages before waiting for newline", rejected);
            }
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"version\":1,\"kind\":\"skip\"}")))
            using (var channel = new OnboardingChannel(stream, true))
            {
                var rejected = false;
                try { await channel.ReadAsync(default); } catch (InvalidDataException) { rejected = true; }
                check("onboarding rejects truncated frames without executing the command", rejected);
            }
            var cancelled = new OnboardingPracticeSession();
            var connecting = cancelled.StartAsync(TestPaths.AppExecutable);
            await cancelled.StopAsync();
            try { await connecting; } catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
            check("cancelling a pending practice handshake ends the session", !cancelled.Connected);

            // No controller or real settings file is needed to validate practice process isolation.
            var session = new OnboardingPracticeSession();
            try
            {
                await session.StartAsync(TestPaths.AppExecutable);
                await session.SendAsync(new(1, "configure", Language: "en", Shortcut: "Ctrl + Alt + U", Automatic: false, HotkeyRegistered: true));
                var target = session.Target!;
                check("practice handshake identifies a distinct process and native window", target.ProcessId != Environment.ProcessId
                    && Native.GetIdentity((nint)target.Window) == target);
                using var owner = new HwndSource(new HwndSourceParameters("Onboarding selection tests") { Width = 1, Height = 1, WindowStyle = 0 });
                var launches = 0; bool browserFails = false;
                using var controller = new SelectionController(owner, TestPaths.ProbeExecutable,
                    _ => { if (browserFails) throw new IOException("fixture browser failure"); launches++; }, initialHotkey: new(3, 0x55));
                var results = new List<OnboardingSearchResult>();
                controller.OnboardingSearchFinished += _ => throw new InvalidOperationException("observer fixture failure");
                controller.OnboardingSearchFinished += results.Add;
                var root = AutomationElement.FromHandle((nint)target.Window);
                AutomationElement? sample = null;
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (sample == null && DateTime.UtcNow < deadline)
                {
                    sample = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "OnboardingSample"));
                    if (sample == null) await Task.Delay(25);
                }
                if (sample == null) throw new InvalidOperationException("Practice text not available");
                async Task Select()
                {
                    sample = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "OnboardingSample"))
                        ?? throw new InvalidOperationException("Current practice text not available");
                    session.Activate(); sample.SetFocus();
                    ((TextPattern)sample.GetCurrentPattern(TextPattern.Pattern)).DocumentRange.Select();
                    var wait = DateTime.UtcNow.AddSeconds(5);
                    while (Native.GetIdentity(Native.GetForegroundWindow()) != target && DateTime.UtcNow < wait) await Task.Delay(25);
                    if (Native.GetIdentity(Native.GetForegroundWindow()) != target) throw new InvalidOperationException("Practice must own foreground");
                    await controller.CaptureAsync(false, 0, 0);
                    if (controller.ActiveSelection?.Target != target) throw new InvalidOperationException("Practice selection not captured");
                }
                await Select();
                check("practice text uses the real native UIA pipeline and readonly safety", controller.ActiveSelection is { Source: "uia", Editable: false });
                var popup = Application.Current.Windows.OfType<PopupWindow>().Single(p => p.IsVisible);
                popup.FindActionButton(TextAction.Search).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                deadline = DateTime.UtcNow.AddSeconds(5);
                while (results.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(25);
                check("actual toolbar search reports original source after browser launch and isolates observers",
                    launches == 1 && results is [{ Launched: true }] && results[0].Target == target);
                await session.SendAsync(new(1, "search-launched"));
                deadline = DateTime.UtcNow.AddSeconds(5);
                while (root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "OnboardingFinish")) is not { Current.IsOffscreen: false })
                {
                    if (DateTime.UtcNow > deadline) throw new InvalidOperationException("Completed practice layout not ready");
                    await Task.Delay(25);
                }
                browserFails = true;
                await Select(); await controller.ExecuteAsync(TextAction.Search, direct: true);
                check("browser exception emits failure rather than successful onboarding", results.Count == 2 && !results[1].Launched && launches == 1);
            }
            finally { await session.StopAsync(); }
            check("practice shutdown releases its native window", session.Target != null && Native.GetIdentity((nint)session.Target.Window) == null);

            var path = Path.Combine(directory, "settings.json");
            host = new MainWindow(new SettingsStore(path), enableFirstRun: true);
            host.InitializeBackground();
            await Task.Delay(150);
            check("background startup does not present or persist first-use practice", !host.Onboarding.Active
                && !File.Exists(Path.Combine(directory, "onboarding.json")));
            host.ShowSettings();
            var hostDeadline = DateTime.UtcNow.AddSeconds(7);
            while (host.Onboarding.Target == null && DateTime.UtcNow < hostDeadline) await Task.Delay(25);
            check("manual activation after background startup offers first-use practice", host.Onboarding.Target != null);
            var practiceTarget = host.Onboarding.Target ?? throw new InvalidOperationException("First-run practice missing");
            await host.Onboarding.SearchFinishedAsync(new("wrong-source", practiceTarget with { Window = practiceTarget.Window + 1 }, true));
            check("coordinator refuses success from another window", !host.Onboarding.Completed);
            await host.Onboarding.SearchFinishedAsync(new("wrong-start", practiceTarget with { ProcessStarted = practiceTarget.ProcessStarted + 1 }, true));
            check("coordinator refuses reused process identity", !host.Onboarding.Completed);
            await host.Onboarding.SearchFinishedAsync(new("fixture-success", practiceTarget, true));
            check("matching success persists completion without rewriting unrelated preferences", host.Onboarding.Completed
                && new OnboardingStore(Path.Combine(directory, "onboarding.json")).Load().State?.Status == OnboardingStatus.Completed
                && !File.Exists(path));
            check("onboarding language choice saves through the host", host.ApplyOnboardingPreference(new(1, "set-language", Language: "en", RequestId: 1)));
            var before = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check("onboarding automatic preference save failure preserves active state", !host.ApplyOnboardingPreference(new(1, "set-automatic", RequestId: 2, Automatic: true))
                    && !host.AutomaticDetectionEnabled);
            check("failed onboarding save preserves settings file", before.SequenceEqual(File.ReadAllBytes(path)));
            Find<ComboBox>(host, "ShortcutTarget").SelectedIndex = 1;
            host.OnboardingShowSettings(true);
            check("onboarding shortcut recovery always records the toolbar binding", host.GeneralPage.IsRecording
                && Find<ComboBox>(host, "ShortcutTarget").SelectedIndex == 0);
            host.GeneralPage.CancelRecording();
            async Task InvokeChild(string id, TargetIdentity identity)
            {
                var root = AutomationElement.FromHandle((nint)identity.Window);
                var limit = DateTime.UtcNow.AddSeconds(5);
                AutomationElement? button = null;
                while (button == null && DateTime.UtcNow < limit)
                {
                    button = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
                    if (button == null) await Task.Delay(25);
                }
                if (button == null) throw new InvalidOperationException("Practice button missing: " + id);
                ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                limit = DateTime.UtcNow.AddSeconds(5);
                while (Native.GetIdentity((nint)identity.Window) != null && DateTime.UtcNow < limit) await Task.Delay(25);
            }
            await InvokeChild("OnboardingFinish", practiceTarget);
            var visibilityDeadline = DateTime.UtcNow.AddSeconds(2);
            while (host.IsVisible && DateTime.UtcNow < visibilityDeadline) await Task.Delay(25);
            check("practice finish closes the child and keeps completed progress", !host.Onboarding.Active
                && Native.GetIdentity((nint)practiceTarget.Window) == null
                && new OnboardingStore(Path.Combine(directory, "onboarding.json")).Load().State?.Status == OnboardingStatus.Completed);
            check("start using SnapBar returns to tray without opening settings", !host.IsVisible);
            host.OpenOnboarding();
            hostDeadline = DateTime.UtcNow.AddSeconds(7);
            while (host.Onboarding.Target == null && DateTime.UtcNow < hostDeadline) await Task.Delay(25);
            var replayTarget = host.Onboarding.Target ?? throw new InvalidOperationException("Replay did not open");
            await host.Onboarding.SearchFinishedAsync(new("late-old-session", practiceTarget, true));
            check("replaying practice rejects the previous session's late search", !host.Onboarding.Completed && replayTarget != practiceTarget);
            await InvokeChild("OnboardingSkip", replayTarget);
            visibilityDeadline = DateTime.UtcNow.AddSeconds(2);
            while (!host.IsVisible && DateTime.UtcNow < visibilityDeadline) await Task.Delay(25);
            check("skipping a replay never downgrades completed progress", !host.Onboarding.Active
                && new OnboardingStore(Path.Combine(directory, "onboarding.json")).Load().State?.Status == OnboardingStatus.Completed);
            check("maybe later closes practice and reveals the existing settings window", host.IsVisible
                && Native.GetIdentity((nint)replayTarget.Window) == null);
            await host.Onboarding.StopAsync(); host.PrepareSessionEnd(); host.Close(); host = null;
            host = new MainWindow(new SettingsStore(path), enableFirstRun: true); host.Show(); await Task.Delay(150);
            check("completed or existing users reopen settings without another automatic tutorial", !host.Onboarding.Active && host.IsVisible);
        }
        finally
        {
            if (host != null) { await host.Onboarding.StopAsync(); host.PrepareSessionEnd(); host.Close(); }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
