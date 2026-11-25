using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class SelectionController : IDisposable
{
    private const int PointerCaptureWaitMilliseconds = 120;
    private readonly Dispatcher dispatcher;
    private readonly InputMonitor monitor;
    private readonly ProbeClient probe;
    private readonly Action<Uri> launchWeb;
    private readonly Func<IPathRevealSession> createPathSession;
    private readonly ClipboardSelectionReader clipboardReader;
    private readonly PopupWindow popup = new();
    private readonly GenerationGate generation = new();
    private readonly SelectionCapturePipeline capturePipeline = new();
    private readonly DispatcherTimer foregroundTimer;
    private SelectionSnapshot? selection;
    private CancellationTokenSource? delay;
    private CancellationTokenSource? pathAction;
    private bool executing, disposed, stopping;
    private int updateCaptureCount;
    internal bool UpdateNotificationBusy => updateCaptureCount != 0 || executing || clipboardReader.IsBusy
        || pendingAppearanceTarget != null || autoCopyTarget != null || popup.IsVisible || aiWindow?.IsVisible == true;
    private bool automatic;
    private bool sublimeClipboardEnabled;
    private ExcludedApplication[] excludedApplications = [];
    private string searchUrlTemplate = WebActionPolicy.DefaultSearchTemplate;
    private long? readinessTicket;
    private readonly SelectionDiagnostics selectionDiagnostics = SelectionDiagnostics.Shared;
    public bool SublimeClipboardEnabled
    {
        get => sublimeClipboardEnabled;
        set { sublimeClipboardEnabled = value; Invalidate(); }
    }
    public bool Automatic
    {
        get => automatic;
        set { automatic = value; Invalidate(); }
    }
    public bool HotkeyRegistered => monitor.HotkeyRegistered;
    public event Action<HotkeySettings>? HotkeyRecorded;
    public bool TryChangeHotkey(HotkeySettings candidate, Action persist) => monitor.TryChangeHotkey(candidate, persist);
    internal bool TryChangeActionHotkey(TextAction action, HotkeySettings? candidate, Action persist) =>
        monitor.TryChangeActionHotkey(action, candidate, persist);
    internal bool IsActionHotkeyRegistered(TextAction action) => monitor.IsActionHotkeyRegistered(action);
    internal string? AiShortcutPromptId { get; set; }
    internal string? CustomShortcutActionId { get; set; }
    public void SetHotkeyRecording(bool recording) { monitor.HotkeySuppressed = recording; Invalidate(); }
    public bool ClipboardReadPending => clipboardReader.IsBusy;
    public event Action<string>? Status;
    internal event Action? OcrRequested;
    internal event Action<OnboardingSearchResult>? OnboardingSearchFinished;
    private void NotifyOnboardingSearch(SelectionSnapshot current, bool launched)
    {
        if (OnboardingSearchFinished == null) return;
        foreach (Action<OnboardingSearchResult> handler in OnboardingSearchFinished.GetInvocationList())
        {
            try { handler(new(current.Id, current.Target, launched)); }
            catch (Exception) { /* Optional observers must not change the action result. */ }
        }
    }
    internal void CancelSelection() => Invalidate();
    internal SelectionSnapshot? ActiveSelection => selection;
    public string SearchUrlTemplate
    {
        get => searchUrlTemplate;
        set
        {
            if (!WebActionPolicy.IsValidSearchTemplate(value)) throw new ArgumentException("Invalid search template");
            searchUrlTemplate = value; Invalidate();
        }
    }

    public void SetExcludedApplications(IReadOnlyList<ExcludedApplication> applications)
    {
        excludedApplications = AppExclusionPolicy.Normalize(applications);
        Invalidate();
    }

    public SelectionController(HwndSource source, string probeExecutable, Action<Uri>? launchWeb = null,
        Func<IPathRevealSession>? createPathSession = null,
        Func<CustomActionDefinition, string, CancellationToken, Task<ScriptExecutionResult>>? runCustom = null,
        HotkeySettings? initialHotkey = null, HotkeySettings? initialSearchHotkey = null, HotkeySettings? initialCopyHotkey = null,
        HotkeySettings? initialOcrHotkey = null, HotkeySettings? initialLookupHotkey = null,
        HotkeySettings? initialAiHotkey = null, HotkeySettings? initialCustomHotkey = null)
    {
        this.launchWeb = launchWeb ?? BrowserLauncher.Open;
        this.createPathSession = createPathSession ?? (() => new PathRevealSession(probeExecutable));
        this.runCustom = runCustom ?? CustomActionRunner.RunAsync;
        dispatcher = source.Dispatcher;
        probe = new(probeExecutable);
        clipboardReader = new(source.Handle);
        monitor = new(source, initialHotkey, initialSearchHotkey, initialCopyHotkey, initialOcrHotkey,
            initialLookupHotkey, initialAiHotkey, initialCustomHotkey);
        monitor.HotkeyRecorded += value => HotkeyRecorded?.Invoke(value);
        // Low-level hooks run on this installing UI thread. Invalidate immediately so a queued
        // click/key cannot leave an old selection valid during an async action continuation.
        monitor.Input += OnInput;
        popup.ActionRequested += action => _ = ExecuteAsync(action);
        popup.CustomActionRequested += id => _ = ExecuteCustomAsync(id);
        popup.SettingsChanged += value => { actions = value; ActionsChanged?.Invoke(actions); };
        popup.ManageRequested += () => { Invalidate(); ManageActionsRequested?.Invoke(); };
        popup.SessionAbandoned += Invalidate;
        monitor.KeyFilter = key => popup.IsVisible && selection != null && !popup.HasSearchForeground
            && Native.GetIdentity(Native.GetForegroundWindow()) == selection.Target && popup.HandleMenuKey(key);
        foregroundTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        {
            var waitingTarget = pendingAppearanceTarget ?? autoCopyTarget;
            if (waitingTarget != null && Native.GetIdentity(Native.GetForegroundWindow()) != waitingTarget)
                Invalidate(preserveReadonlyAction: true);
            if (selection == null)
            {
                if (lookupResult && resultWindow is { IsLookupPinned: false, HasForeground: false }) resultWindow.Close();
                return;
            }
            if (Native.GetForegroundWindow() != (nint)selection.Target.Window && !popup.HasSearchForeground && aiWindow?.HasCaptureForeground != true)
                Invalidate(preserveReadonlyAction: true);
            else if (popup.ShouldDismissForPointer())
                Invalidate(preserveReadonlyAction: true);
        }, dispatcher);
    }

    private void OnInput(InputEvent input)
    {
        if (disposed || stopping) return;
        if (selectionDiagnostics.Enabled && input.Kind is "selection" or "hotkey")
            selectionDiagnostics.RecordStatus(0, "input-" + input.Kind, input.Modifiers.ToString());
        if (input.Kind == "key" && (popup.HasSearchForeground || aiWindow?.HasCaptureForeground == true)) return;
        if (input.Kind == "hotkey" && aiWindow?.HasCaptureForeground == true) return;
        if (input.Action == TextAction.Ocr) { OcrRequested?.Invoke(); return; }
        if ((input.Kind is "click" or "selection" or "scroll")
            && (popup.Contains(input.X, input.Y) || resultWindow?.Contains(input.X, input.Y) == true
                || customResults.Contains(input.X, input.Y) || aiWindow?.ContainsCaptureSurface(input.X, input.Y) == true)) return;
        if (input.Kind is "key" or "click" or "scroll") { Invalidate(preserveReadonlyAction: true); return; }
        var copySelection = input.Kind == "selection" && autoCopy.ShouldCopy(input.Modifiers);
        if (input.Kind == "hotkey" || (input.Kind == "selection" && (Automatic || copySelection)))
        {
            if (executing) return;
            var targetId = input.Action == TextAction.Ai ? AiShortcutPromptId : CustomShortcutActionId;
            if (input.Action == TextAction.Ai && aiPreferences?.Settings.Prompts.Any(p => p.Id == targetId) != true
                || input.Action == TextAction.Custom && !actions.CustomActions.Any(a => a.Id == targetId
                    && !actions.Layout.Any(e => e.Id == a.Id && e.Placement == ActionPlacement.Hidden)))
            { Status?.Invoke(UiText.Get("GeneralSettings_ShortcutTargetMissing")); return; }
            if (input.Action == TextAction.Custom && actions.CustomActions.FirstOrDefault(a => a.Id == targetId)
                is { UseSelection: false, Kind: not CustomActionKind.Url, Output: not ActionOutput.ReplaceSelection })
            { _ = ExecuteStandaloneCustomAsync(targetId!); return; }
            // Keep process inspection, UIA startup and feedback window work outside the
            // low-level hook callback. A newer input must still cancel this queued capture.
            var queuedTicket = generation.Next();
            var sourceWindow = Native.GetForegroundWindow();
            var appearance = input.Kind == "selection" ? StartAppearanceDeadline() : (AppearanceDeadline?)null;
            dispatcher.BeginInvoke(() =>
            {
                if (generation.IsCurrent(queuedTicket) && Native.GetForegroundWindow() == sourceWindow)
                    _ = CaptureAsync(input.Kind == "selection", input.X, input.Y, input.Action, targetId, copySelection, appearance);
            });
        }
    }

    internal Task CaptureAsync(bool pointer, int x, int y, TextAction? directAction = null, string? directTargetId = null,
        bool autoCopyRequest = false) =>
        CaptureAsync(pointer, x, y, directAction, directTargetId, autoCopyRequest, pointer ? StartAppearanceDeadline() : null);

    private async Task CaptureAsync(bool pointer, int x, int y, TextAction? directAction, string? directTargetId,
        bool autoCopyRequest, AppearanceDeadline? appearance)
    {
        if (disposed || stopping) return;
        var directPrompt = directAction == TextAction.Ai && directTargetId != null
            ? aiPreferences?.Settings.Prompts.FirstOrDefault(p => p.Id == directTargetId) : null;
        var directCustom = directAction == TextAction.Custom
            ? actions.CustomActions.FirstOrDefault(a => a.Id == directTargetId) : null;
        Invalidate();
        var ticket = generation.Next();
        delay = new CancellationTokenSource();
        var token = delay.Token;
        var window = Native.GetForegroundWindow();
        var target = Native.GetIdentity(window);
        if (target == null || target.ProcessId == Environment.ProcessId) return;
        if (pointer && (!(Automatic || autoCopyRequest) || !AppExclusionPolicy.AllowsAutomaticCapture(excludedApplications,
            excludedApplications.Length == 0 ? null : Native.GetExecutablePath(target)))) return;
        var watch = Stopwatch.StartNew();
        updateCaptureCount++;
        var captureSequence = Native.GetClipboardSequenceNumber();
        if (autoCopyRequest) autoCopyTarget = target;
        if (pointer && Automatic && directAction == null) pendingAppearanceTarget = target;
        bool Current() => generation.IsCurrent(ticket) && !token.IsCancellationRequested && !disposed && !stopping
            && Native.GetIdentity(Native.GetForegroundWindow()) == target;
        try
        {
            if (autoCopyRequest)
            {
                selectionDiagnostics.Record(ticket, "auto-copy-wait", new("start"));
                var waiting = Stopwatch.StartNew();
                while (Native.AnyModifierDown() && waiting.ElapsedMilliseconds < AutoCopyModifierWaitMilliseconds)
                {
                    await Task.Delay(20, token);
                    if (!Current()) return;
                }
                if (Native.AnyModifierDown()) autoCopyRequest = false;
                selectionDiagnostics.Record(ticket, "auto-copy-wait", new(autoCopyRequest ? "released" : "modifier-held"));
                if (!autoCopyRequest && !Automatic) return;
            }
            await Task.Delay(pointer ? PointerCaptureWaitMilliseconds : 40, token);
            if (!Current()) return;
            // A hotkey can arrive before the user releases Ctrl/Alt. Never inject through held modifiers.
            if (!pointer)
            {
                for (var n = 0; Native.AnyModifierDown() && n < 20; n++)
                {
                    await Task.Delay(25, token);
                    if (!Current()) return;
                }
                Native.GetCursorPos(out var position); x = position.X; y = position.Y;
            }
            var sublime = SublimeCompatibility.IsTarget(target);
            int nativeRequests = 0;
            SublimeAppearanceSource? sublimeSource = null;
            ClipboardFallback? ResolveFallback(ProbeResponse response)
            {
                if (SelectionRoutingPolicy.UseClipboardFallback(response, SublimeClipboardEnabled, sublime))
                {
                    return new(target, () =>
                    {
                        if (pointer && Automatic && directAction == null && appearance?.Remaining > TimeSpan.Zero)
                            sublimeSource = new(NativeClipboardSourceReader.ReadFocusedWindow(target), NativeCaretReader.ReadIdentity(target));
                        return clipboardReader.CaptureAsync(target, () => Current() && SublimeClipboardEnabled,
                            SublimeSelectionAdapter.ReadPlan(), expectedSequence: autoCopyRequest ? captureSequence : null,
                            diagnostic: stage => selectionDiagnostics.Record(ticket, "sublime-capture-" + stage, new("ok")));
                    }, AutomaticBackoff: false);
                }
                if (SelectionRoutingPolicy.UseClipboardFallback(response, ClipboardCompatibilityEnabled,
                    response.ClipboardTarget?.Target == target && ClipboardSelectionAdapter.IsTarget(target)))
                    return new(target, () => CaptureClipboardAsync(response.ClipboardTarget!, ticket,
                        expectedSequence: autoCopyRequest ? captureSequence : null));
                return null;
            }
            var captured = await capturePipeline.CaptureAsync(target, pointer,
                async remaining =>
                {
                    var response = await probe.RequestAsync(new("capture", (long)window, x, y, UsePointer: pointer,
                        PrepareProvider: !sublime && nativeRequests++ == 0, Diagnostics: selectionDiagnostics.Enabled), remaining);
                    selectionDiagnostics.Record(ticket, "native", response);
                    return response;
                },
                Current, ResolveFallback, retryUnsupported: !sublime, token,
                () => { readinessTicket = ticket; Status?.Invoke(UiText.Get("SelectionController_000")); });
            var native = captured.Native;
            var result = captured.Response;
            if (captured.BackedOff)
            { selectionDiagnostics.Record(ticket, "automatic-clipboard-backoff", new("backoff")); return; }
            if (!Current())
            { selectionDiagnostics.RecordStatus(ticket, "capture-result", "stale"); return; }
            selectionDiagnostics.Record(ticket, "capture-result", result, watch.Elapsed.TotalMilliseconds);
            if (!result.Success || result.Selection == null)
            { Report(sublime && !SublimeClipboardEnabled && result.Status == "unsupported" ? "sublime-disabled" : result.Status); return; }
            var candidate = result.Selection;
            var autoCopied = autoCopyRequest && await CopySelectionAutomaticallyAsync(candidate, ticket,
                result.ClipboardSequence ?? captureSequence);
            if (!Current()) return;
            if (directAction is { } action)
            {
                if (directTargetId != null && (action == TextAction.Ai && (directPrompt == null
                    || aiPreferences?.Settings.Prompts.FirstOrDefault(p => p.Id == directTargetId) != directPrompt)
                    || action == TextAction.Custom && (directCustom == null
                        || actions.CustomActions.FirstOrDefault(a => a.Id == directTargetId) != directCustom)))
                { Status?.Invoke(UiText.Get("GeneralSettings_ShortcutTargetMissing")); return; }
                selection = candidate;
                if (action == TextAction.Ai) await OpenAiAsync(direct: true, promptId: directTargetId);
                else if (action == TextAction.Custom)
                {
                    if (directTargetId != null) await ExecuteCustomAsync(directTargetId, direct: true);
                }
                else await ExecuteAsync(action, direct: true);
                return;
            }
            if (pointer && !Automatic)
            {
                selection = candidate;
                if (autoCopied) ShowAutoCopyFeedback(candidate, x, y);
                return;
            }
            var remaining = appearance?.Remaining ?? TimeSpan.Zero;
            if (remaining > TimeSpan.Zero)
            {
                if (autoCopied) ShowAutoCopyFeedback(candidate, x, y);
                bool CanAppear() => Current() && Automatic;
                await Task.Delay(appearance.GetValueOrDefault().Remaining, token);
                if (!CanAppear()) return;
                var validated = await ValidateDelayedAppearanceAsync(candidate, CanAppear, sublimeSource);
                if (validated == null || !CanAppear()) return;
                candidate = validated;
            }
            pendingAppearanceTarget = null;
            selection = candidate;
            popup.Present(selection, x, y, searchUrlTemplate, actions, usePointer: pointer);
            selectionDiagnostics.RecordStatus(ticket, "popup-present", "ok", watch.Elapsed.TotalMilliseconds);
            if (autoCopied && remaining <= TimeSpan.Zero) ShowAutoCopyFeedback(selection, x, y);
            Status?.Invoke(selection.Source == "sublime-clipboard"
                ? UiText.Get("SelectionController_001",(selection.CharacterCount),((selection.Editable == true ? UiText.Get("SelectionController_047") : UiText.Get("SelectionController_048"))),(watch.Elapsed.TotalMilliseconds).ToString("F0", System.Globalization.CultureInfo.InvariantCulture))                : selection.Source == ClipboardSelectionAdapter.Source
                    ? UiText.Get(selection.Editable == true ? "SelectionController_ClipboardEditable" : "SelectionController_002", selection.CharacterCount, watch.Elapsed.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))
                    : UiText.Get("SelectionController_003", selection.CharacterCount, selection.Editable == true ? UiText.Get("SelectionController_049") : selection.Editable == false ? UiText.Get("SelectionController_050") : UiText.Get("SelectionController_051"), watch.Elapsed.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture), native.ProbeElapsedMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture), native.Attempts > 1 ? UiText.Get("Selection_Attempts", native.Attempts) : ""));
        }
        catch (OperationCanceledException) { selectionDiagnostics.RecordStatus(ticket, "capture-result", "cancelled"); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Report("window-error"); }
        finally
        {
            updateCaptureCount--;
            if (readinessTicket == ticket) readinessTicket = null;
            if (generation.IsCurrent(ticket)) pendingAppearanceTarget = null;
            if (generation.IsCurrent(ticket) && copyFeedback?.IsVisible != true) autoCopyTarget = null;
        }
    }

    internal async Task ExecuteAsync(TextAction action, bool direct = false)
    {
        if (action == TextAction.Ai) { await OpenAiAsync(direct); return; }
        if (action == TextAction.Lookup) { await LookupAsync(direct); return; }
        if (executing || selection == null || disposed || stopping) return;
        var sourceFocused = direct
            ? Native.GetIdentity(Native.GetForegroundWindow()) == selection.Target
            : popup.TryRestoreSourceFocus();
        if (!sourceFocused) { Invalidate(); Report("target-changed"); return; }
        executing = true;
        var current = selection;
        var ticket = generation.Next();
        popup.Dismiss();
        try
        {
            if (!Enum.IsDefined(action) || action is TextAction.Ocr or TextAction.Custom) { Report("invalid-action"); return; }
            var compatibility = current.Source == "sublime-clipboard";
            var genericCompatibility = current.Source == ClipboardSelectionAdapter.Source;
            var writes = action is TextAction.Cut or TextAction.Paste;
            if (compatibility && !SublimeClipboardEnabled) { Report("sublime-disabled"); return; }
            if (genericCompatibility && !ClipboardCompatibilityEnabled) { Report("compatibility-disabled"); return; }
            if (writes && !SessionPolicy.CanWrite(current, current.Target)) { Report("not-editable"); return; }
            var actionSequence = Native.GetClipboardSequenceNumber();
            Task<ProbeResponse> Validate() => compatibility
                ? clipboardReader.CaptureAsync(current.Target,
                    () => generation.IsCurrent(ticket) && !disposed && SublimeClipboardEnabled,
                    SublimeSelectionAdapter.ReadPlan(requireEditable: writes),
                    expectedSequence: action == TextAction.Paste ? actionSequence : null)
                : genericCompatibility ? CaptureClipboardAsync(new(current.Id, current.Target), ticket,
                    expectedSequence: action == TextAction.Paste ? actionSequence : null)
                    : probe.RequestAsync(new("validate", SessionId: current.Id));
            var validation = await Validate();
            if (!generation.IsCurrent(ticket) || disposed) return;
            var identity = Native.GetIdentity(Native.GetForegroundWindow());
            if (!validation.Success || validation.Selection == null || identity != current.Target)
            { Report(validation.Success ? "target-changed" : validation.Status); return; }
            if ((compatibility || genericCompatibility) && validation.Selection.Text != current.Text) { Report("selection-changed"); return; }
            if (action == TextAction.Copy)
            {
                if (!WriteSelectionClipboard(current, ticket, validation.ClipboardSequence ?? actionSequence))
                { Report("clipboard-changed"); return; }
                Status?.Invoke(UiText.Get("SelectionController_004",(current.CharacterCount)));
                RememberBuiltIn(action);
            }
            else if (action == TextAction.RevealPath)
            {
                var destination = PathActionPolicy.Resolve(current.Text);
                if (!destination.Success) { Report(destination.Status); return; }
                using var cancellation = new CancellationTokenSource();
                pathAction = cancellation;
                using var session = createPathSession();
                var prepared = await session.PrepareAsync(destination.Path!, cancellation.Token);
                if (!generation.IsCurrent(ticket) || disposed || cancellation.IsCancellationRequested) return;
                if (prepared != "ok") { Report(prepared); return; }
                // Lookup can take time: revalidate again before any Shell side effect.
                var finalValidation = await Validate();
                if (!generation.IsCurrent(ticket) || disposed || cancellation.IsCancellationRequested) return;
                if (!finalValidation.Success || finalValidation.Selection?.Text != current.Text
                    || Native.GetIdentity(Native.GetForegroundWindow()) != current.Target)
                { Report(finalValidation.Success ? "target-changed" : finalValidation.Status); return; }
                var result = await session.RevealAsync(current.Target, cancellation.Token);
                // Explorer may become foreground before its acknowledgement arrives.
                if (result == "ok") { Status?.Invoke(UiText.Get("SelectionController_005")); RememberBuiltIn(action); }
                else if (generation.IsCurrent(ticket)) Report(result);
            }
            else if (action is TextAction.Search or TextAction.OpenLink)
            {
                var destination = action == TextAction.Search ? WebActionPolicy.Search(current.Text, searchUrlTemplate)
                    : WebActionPolicy.OpenLink(current.Text);
                if (!destination.Success) { Report(destination.Status); return; }
                if (!generation.IsCurrent(ticket) || Native.GetIdentity(Native.GetForegroundWindow()) != current.Target)
                { Report("target-changed"); return; }
                try
                {
                    launchWeb(destination.Uri!);
                    if (action == TextAction.Search) NotifyOnboardingSearch(current, true);
                    Status?.Invoke(action == TextAction.Search ? UiText.Get("SelectionController_006") : UiText.Get("SelectionController_007"));
                    RememberBuiltIn(action);
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or ArgumentException)
                { if (action == TextAction.Search) NotifyOnboardingSearch(current, false); Report("browser-unavailable"); }
            }
            else
            {
                if (identity == null || !SessionPolicy.CanWrite(validation.Selection, identity)) { Report("not-editable"); return; }
                if (Native.AnyModifierDown()) { Report("modifier-held"); return; }
                if (action == TextAction.Paste && !Clipboard.ContainsText()) { Report("clipboard-no-text"); return; }
                if (action == TextAction.Paste && !ClipboardActionPolicy.IsUnchanged(
                    validation.ClipboardSequence ?? actionSequence, Native.GetClipboardSequenceNumber()))
                { Report("clipboard-changed"); return; }
                // Recheck immediately before injection; validation is intentionally not treated as an atomic lock.
                if (Native.GetIdentity(Native.GetForegroundWindow()) != current.Target) { Report("target-changed"); return; }
                if (!generation.IsCurrent(ticket)) { Report("selection-changed"); return; }
                if (!WriteSourceCurrent(validation.Selection)) { Report("focus-changed"); return; }
                var sent = compatibility ? SublimeSelectionAdapter.SendAction(action, current.Target)
                    : Native.SendControlKey(action == TextAction.Cut ? (ushort)0x58 : (ushort)0x56);
                if (!sent) { Report("input-blocked"); return; }
                Status?.Invoke(action == TextAction.Cut ? UiText.Get("SelectionController_008") : UiText.Get("SelectionController_009"));
                RememberBuiltIn(action);
            }
        }
        catch (ExternalException) { Report("clipboard-busy"); }
        finally { pathAction = null; executing = false; Invalidate(); }
    }

    private void Report(string status) => Status?.Invoke(status switch
    {
        "no-selection" => UiText.Get("SelectionController_010"),
        "unsupported" => UiText.Get("SelectionController_011"),
        "password" => UiText.Get("SelectionController_012"),
        "timeout" => UiText.Get("SelectionController_013"),
        "target-changed" or "focus-changed" or "selection-changed" or "expired" => UiText.Get("SelectionController_014"),
        "not-editable" => UiText.Get("SelectionController_015"),
        "invalid-action" => UiText.Get("SelectionController_016"),
        "invalid-link" => UiText.Get("SelectionController_017"),
        "invalid-search-template" => UiText.Get("SelectionController_018"),
        "web-address-too-long" => UiText.Get("SelectionController_019"),
        "browser-unavailable" => UiText.Get("SelectionController_020"),
        "invalid-path" => UiText.Get("SelectionController_021"),
        "path-unavailable" => UiText.Get("SelectionController_022"),
        "path-not-prepared" or "explorer-unavailable" => UiText.Get("SelectionController_023"),
        "path-timeout" => UiText.Get("SelectionController_024"),
        "cancelled" => UiText.Get("SelectionController_025"),
        "modifier-held" => UiText.Get("SelectionController_026"),
        "input-blocked" => UiText.Get("SelectionController_027"),
        "clipboard-no-text" => UiText.Get("SelectionController_028"),
        "clipboard-busy" => UiText.Get("SelectionController_029"),
        "sublime-disabled" => UiText.Get("SelectionController_030"),
        "compatibility-disabled" => UiText.Get("SelectionController_031"),
        "clipboard-unavailable" or "clipboard-selection-unverified" => UiText.Get("SelectionController_032"),
        "compatibility-setup-required" => UiText.Get("SelectionController_033"),
        "sublime-no-selection" => UiText.Get("SelectionController_034"),
        "clipboard-format-unsupported" => UiText.Get("SelectionController_035"),
        "clipboard-format-unavailable" => UiText.Get("SelectionController_036"),
        "clipboard-backup-failed" => UiText.Get("SelectionController_037"),
        "clipboard-backup-too-large" => UiText.Get("SelectionController_038"),
        "clipboard-changed" => UiText.Get("SelectionController_039"),
        "clipboard-other-owner" => UiText.Get("SelectionController_040"),
        "clipboard-input-changed" => UiText.Get("SelectionController_041"),
        "clipboard-restore-failed" => UiText.Get("SelectionController_042"),
        "too-large" => UiText.Get("SelectionController_043"),
        "busy" => UiText.Get("SelectionController_044"),
        _ => UiText.Get("SelectionController_045",(status))    });

    private void Invalidate() => Invalidate(preserveReadonlyAction: false);

    private void Invalidate(bool preserveReadonlyAction)
    {
        autoCopyTarget = null;
        pendingAppearanceTarget = null;
        copyFeedback?.HideFeedback();
        aiWindow?.InvalidateTarget();
        if (!preserveReadonlyAction || !customActionAllowsDetachedResult) customAction?.Cancel();
        customResults.InvalidateTargets();
        if (lookupResult)
        {
            if (resultWindow is { IsLookupPinned: false, HasForeground: false }) resultWindow.Close();
        }
        else resultWindow?.InvalidateTarget();
        pathAction?.Cancel();
        generation.Next(); delay?.Cancel(); delay?.Dispose(); delay = null;
        selection = null; popup.Dismiss();
        if (readinessTicket != null)
        { readinessTicket = null; Status?.Invoke(UiText.Get("SelectionController_046")); }
    }

    public void BeginShutdown()
    {
        stopping = true;
        Invalidate();
        resultWindow?.Close();
        customResults.CloseAll();
        aiWindow?.Close();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Invalidate(); copyFeedback?.Close(); resultWindow?.Close(); customResults.CloseAll(); aiWindow?.Close(); foregroundTimer.Stop(); monitor.Dispose(); probe.Dispose(); popup.Close();
    }
}
