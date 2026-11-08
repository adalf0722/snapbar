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
    private readonly Dispatcher dispatcher;
    private readonly InputMonitor monitor;
    private readonly ProbeClient probe;
    private readonly Action<Uri> launchWeb;
    private readonly Func<IPathRevealSession> createPathSession;
    private readonly ClipboardSelectionReader clipboardReader;
    private readonly PopupWindow popup = new();
    private readonly GenerationGate generation = new();
    private readonly NativeSelectionCapture nativeCapture = new();
    private readonly DispatcherTimer foregroundTimer;
    private SelectionSnapshot? selection;
    private CancellationTokenSource? delay;
    private CancellationTokenSource? pathAction;
    private bool executing, disposed, stopping;
    private bool automatic;
    private bool sublimeClipboardEnabled;
    private ExcludedApplication[] excludedApplications = [];
    private string searchUrlTemplate = WebActionPolicy.DefaultSearchTemplate;
    private long? readinessTicket;
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
    public void SetHotkeyRecording(bool recording) { monitor.HotkeySuppressed = recording; Invalidate(); }
    public bool ClipboardReadPending => clipboardReader.IsBusy;
    public event Action<string>? Status;
    internal event Action? OcrRequested;
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
        HotkeySettings? initialOcrHotkey = null)
    {
        this.launchWeb = launchWeb ?? BrowserLauncher.Open;
        this.createPathSession = createPathSession ?? (() => new PathRevealSession(probeExecutable));
        this.runCustom = runCustom ?? CustomActionRunner.RunAsync;
        dispatcher = source.Dispatcher;
        probe = new(probeExecutable);
        clipboardReader = new(source.Handle);
        monitor = new(source, initialHotkey, initialSearchHotkey, initialCopyHotkey, initialOcrHotkey);
        monitor.HotkeyRecorded += value => HotkeyRecorded?.Invoke(value);
        // Low-level hooks run on this installing UI thread. Invalidate immediately so a queued
        // click/key cannot leave an old selection valid during an async action continuation.
        monitor.Input += OnInput;
        popup.ActionRequested += action => _ = ExecuteAsync(action);
        popup.CustomActionRequested += id => _ = ExecuteCustomAsync(id);
        popup.SettingsChanged += value => { actions = value; ActionsChanged?.Invoke(actions); };
        popup.ManageRequested += () => { Invalidate(); ManageActionsRequested?.Invoke(); };
        popup.SessionAbandoned += Invalidate;
        monitor.KeyFilter = key => selection != null && !popup.HasSearchForeground && Native.GetIdentity(Native.GetForegroundWindow()) == selection.Target && popup.HandleMenuKey(key);
        foregroundTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        {
            if (selection == null) return;
            if (Native.GetForegroundWindow() != (nint)selection.Target.Window && !popup.HasSearchForeground && aiWindow?.HasCaptureForeground != true)
                Invalidate();
            else if (popup.ShouldDismissForPointer())
                Invalidate();
        }, dispatcher);
    }

    private void OnInput(InputEvent input)
    {
        if (disposed || stopping) return;
        if (input.Kind == "key" && (popup.HasSearchForeground || aiWindow?.HasCaptureForeground == true)) return;
        if (input.Kind == "hotkey" && aiWindow?.HasCaptureForeground == true) return;
        if (input.Action == TextAction.Ocr) { OcrRequested?.Invoke(); return; }
        if ((input.Kind is "click" or "selection" or "scroll")
            && (popup.Contains(input.X, input.Y) || resultWindow?.Contains(input.X, input.Y) == true || aiWindow?.ContainsCaptureSurface(input.X, input.Y) == true)) return;
        if (input.Kind is "key" or "click" or "scroll") { Invalidate(); return; }
        if (input.Kind == "hotkey" || (input.Kind == "selection" && Automatic))
        {
            if (executing) return;
            _ = CaptureAsync(input.Kind == "selection", input.X, input.Y, input.Action);
        }
    }

    internal async Task CaptureAsync(bool pointer, int x, int y, TextAction? directAction = null)
    {
        if (disposed || stopping) return;
        Invalidate();
        var ticket = generation.Next();
        delay = new CancellationTokenSource();
        var token = delay.Token;
        var window = Native.GetForegroundWindow();
        var target = Native.GetIdentity(window);
        if (target == null || target.ProcessId == Environment.ProcessId) return;
        if (pointer && (!Automatic || !AppExclusionPolicy.AllowsAutomaticCapture(excludedApplications,
            excludedApplications.Length == 0 ? null : Native.GetExecutablePath(target)))) return;
        var watch = Stopwatch.StartNew();
        try
        {
            await Task.Delay(pointer ? 120 : 40, token);
            // A hotkey can arrive before the user releases Ctrl/Alt. Never inject through held modifiers.
            if (!pointer)
            {
                for (var n = 0; Native.AnyModifierDown() && n < 20; n++) await Task.Delay(25, token);
                Native.GetCursorPos(out var position); x = position.X; y = position.Y;
            }
            if (!generation.IsCurrent(ticket) || Native.GetIdentity(Native.GetForegroundWindow()) != target) return;
            var sublime = SublimeCompatibility.IsTarget(target);
            bool Current() => generation.IsCurrent(ticket) && !disposed
                && Native.GetIdentity(Native.GetForegroundWindow()) == target;
            int nativeRequests = 0;
            var native = await nativeCapture.CaptureAsync(
                remaining => probe.RequestAsync(new("capture", (long)window, x, y, UsePointer: pointer,
                    PrepareProvider: !sublime && nativeRequests++ == 0), remaining),
                Current, retryUnsupported: !sublime, token,
                () => { readinessTicket = ticket; Status?.Invoke(UiText.Get("SelectionController_000")); });
            var result = native.Response;
            if (SelectionRoutingPolicy.UseClipboardFallback(result, SublimeClipboardEnabled, sublime))
                result = await clipboardReader.CaptureAsync(target,
                    () => generation.IsCurrent(ticket) && !disposed && SublimeClipboardEnabled,
                    SublimeSelectionAdapter.ReadPlan());
            else if (SelectionRoutingPolicy.UseClipboardFallback(result, ClipboardCompatibilityEnabled,
                result.ClipboardTarget?.Target == target && ClipboardSelectionAdapter.IsTarget(target)))
                result = await CaptureClipboardAsync(result.ClipboardTarget!, ticket);
            if (!generation.IsCurrent(ticket) || disposed || Native.GetIdentity(Native.GetForegroundWindow()) != target) return;
            if (!result.Success || result.Selection == null)
            { Report(sublime && !SublimeClipboardEnabled && result.Status == "unsupported" ? "sublime-disabled" : result.Status); return; }
            selection = result.Selection;
            if (directAction is { } action)
            {
                await ExecuteAsync(action, direct: true);
                return;
            }
            popup.Present(selection, x, y, searchUrlTemplate, actions);
            Status?.Invoke(selection.Source == "sublime-clipboard"
                ? UiText.Get("SelectionController_001",(selection.CharacterCount),((selection.Editable == true ? UiText.Get("SelectionController_047") : UiText.Get("SelectionController_048"))),(watch.Elapsed.TotalMilliseconds).ToString("F0", System.Globalization.CultureInfo.InvariantCulture))                : selection.Source == ClipboardSelectionAdapter.Source
                    ? UiText.Get("SelectionController_002",(selection.CharacterCount),(watch.Elapsed.TotalMilliseconds).ToString("F0", System.Globalization.CultureInfo.InvariantCulture))                    : UiText.Get("SelectionController_003",(selection.CharacterCount),((selection.Editable == true ? UiText.Get("SelectionController_049") : selection.Editable == false ? UiText.Get("SelectionController_050") : UiText.Get("SelectionController_051"))),(watch.Elapsed.TotalMilliseconds).ToString("F0", System.Globalization.CultureInfo.InvariantCulture),(native.ProbeElapsedMs).ToString("F0", System.Globalization.CultureInfo.InvariantCulture),((native.Attempts > 1 ? UiText.Get("Selection_Attempts", native.Attempts) : ""))));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Report("window-error"); }
        finally { if (readinessTicket == ticket) readinessTicket = null; }
    }

    internal async Task ExecuteAsync(TextAction action, bool direct = false)
    {
        if (action == TextAction.Ai) { await OpenAiAsync(); return; }
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
            if (!Enum.IsDefined(action) || action == TextAction.Ocr) { Report("invalid-action"); return; }
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
                : genericCompatibility ? CaptureClipboardAsync(new(current.Id, current.Target), ticket)
                    : probe.RequestAsync(new("validate", SessionId: current.Id));
            var validation = await Validate();
            if (!generation.IsCurrent(ticket) || disposed) return;
            var identity = Native.GetIdentity(Native.GetForegroundWindow());
            if (!validation.Success || validation.Selection == null || identity != current.Target)
            { Report(validation.Success ? "target-changed" : validation.Status); return; }
            if ((compatibility || genericCompatibility) && validation.Selection.Text != current.Text) { Report("selection-changed"); return; }
            if (action == TextAction.Copy)
            {
                Clipboard.SetText(current.Text, TextDataFormat.UnicodeText);
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
                    Status?.Invoke(action == TextAction.Search ? UiText.Get("SelectionController_006") : UiText.Get("SelectionController_007"));
                    RememberBuiltIn(action);
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or ArgumentException)
                { Report("browser-unavailable"); }
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

    private void Invalidate()
    {
        aiWindow?.InvalidateTarget();
        customAction?.Cancel();
        if (lookupResult)
        {
            if (resultWindow is { IsLookupPinned: false }) resultWindow.Close();
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
        aiWindow?.Close();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Invalidate(); resultWindow?.Close(); aiWindow?.Close(); foregroundTimer.Stop(); monitor.Dispose(); probe.Dispose(); popup.Close();
    }
}
