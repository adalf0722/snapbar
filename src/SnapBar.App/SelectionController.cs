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
    public bool ClipboardReadPending => clipboardReader.IsBusy;
    public event Action<string>? Status;
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
        Func<CustomActionDefinition, string, CancellationToken, Task<ScriptExecutionResult>>? runCustom = null)
    {
        this.launchWeb = launchWeb ?? BrowserLauncher.Open;
        this.createPathSession = createPathSession ?? (() => new PathRevealSession(probeExecutable));
        this.runCustom = runCustom ?? CustomActionRunner.RunAsync;
        dispatcher = source.Dispatcher;
        probe = new(probeExecutable);
        clipboardReader = new(source.Handle);
        monitor = new(source);
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
            if (selection != null && Native.GetForegroundWindow() != (nint)selection.Target.Window && !popup.HasSearchForeground) Invalidate();
        }, dispatcher);
    }

    private void OnInput(InputEvent input)
    {
        if (disposed || stopping) return;
        if (input.Kind == "key" && popup.HasSearchForeground) return;
        if ((input.Kind is "click" or "selection" or "scroll")
            && (popup.Contains(input.X, input.Y) || resultWindow?.Contains(input.X, input.Y) == true)) return;
        if (input.Kind is "key" or "click" or "scroll") { Invalidate(); return; }
        if (input.Kind == "hotkey" || (input.Kind == "selection" && Automatic))
        {
            if (executing) return;
            _ = CaptureAsync(input.Kind == "selection", input.X, input.Y);
        }
    }

    internal async Task CaptureAsync(bool pointer, int x, int y)
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
                () => { readinessTicket = ticket; Status?.Invoke("正在等候 App 的選字介面就緒…"); });
            var result = native.Response;
            if (SelectionRoutingPolicy.UseClipboardFallback(result, SublimeClipboardEnabled, sublime))
                result = await clipboardReader.CaptureAsync(target,
                    () => generation.IsCurrent(ticket) && !disposed && SublimeClipboardEnabled,
                    SublimeSelectionAdapter.ReadPlan());
            if (!generation.IsCurrent(ticket) || disposed || Native.GetIdentity(Native.GetForegroundWindow()) != target) return;
            if (!result.Success || result.Selection == null)
            { Report(sublime && !SublimeClipboardEnabled && result.Status == "unsupported" ? "sublime-disabled" : result.Status); return; }
            selection = result.Selection;
            popup.Present(selection, x, y, searchUrlTemplate, actions);
            Status?.Invoke(selection.Source == "sublime-clipboard"
                ? $"Sublime 相容模式 · {selection.CharacterCount} 字 · {(selection.Editable == true ? "可編輯" : "不提供寫入")} · 剪貼簿已還原 · {watch.Elapsed.TotalMilliseconds:F0} ms"
                : $"已取得 {selection.CharacterCount} 字 · {(selection.Editable == true ? "可編輯" : selection.Editable == false ? "唯讀" : "編輯狀態未知")} · {watch.Elapsed.TotalMilliseconds:F0} ms（UIA {native.ProbeElapsedMs:F0} ms{(native.Attempts > 1 ? $"／{native.Attempts} 次查詢" : "")}）");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Report("window-error"); }
        finally { if (readinessTicket == ticket) readinessTicket = null; }
    }

    internal async Task ExecuteAsync(TextAction action)
    {
        if (executing || selection == null || disposed || stopping) return;
        if (!popup.TryRestoreSourceFocus()) { Invalidate(); Report("target-changed"); return; }
        executing = true;
        var current = selection;
        var ticket = generation.Next();
        popup.Dismiss();
        try
        {
            if (!Enum.IsDefined(action)) { Report("invalid-action"); return; }
            var compatibility = current.Source == "sublime-clipboard";
            var writes = action is TextAction.Cut or TextAction.Paste;
            if (compatibility && !SublimeClipboardEnabled) { Report("sublime-disabled"); return; }
            if (writes && !SessionPolicy.CanWrite(current, current.Target)) { Report("not-editable"); return; }
            var actionSequence = Native.GetClipboardSequenceNumber();
            Task<ProbeResponse> Validate() => compatibility
                ? clipboardReader.CaptureAsync(current.Target,
                    () => generation.IsCurrent(ticket) && !disposed && SublimeClipboardEnabled,
                    SublimeSelectionAdapter.ReadPlan(requireEditable: writes),
                    expectedSequence: action == TextAction.Paste ? actionSequence : null)
                : probe.RequestAsync(new("validate", SessionId: current.Id));
            var validation = await Validate();
            if (!generation.IsCurrent(ticket) || disposed) return;
            var identity = Native.GetIdentity(Native.GetForegroundWindow());
            if (!validation.Success || validation.Selection == null || identity != current.Target)
            { Report(validation.Success ? "target-changed" : validation.Status); return; }
            if (compatibility && validation.Selection.Text != current.Text) { Report("selection-changed"); return; }
            if (action == TextAction.Copy)
            {
                Clipboard.SetText(current.Text, TextDataFormat.UnicodeText);
                Status?.Invoke($"已複製 {current.CharacterCount} 字");
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
                if (result == "ok") { Status?.Invoke("已交由檔案總管顯示項目。"); RememberBuiltIn(action); }
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
                    Status?.Invoke(action == TextAction.Search ? "已交由預設瀏覽器搜尋。" : "已交由預設瀏覽器開啟連結。");
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
                Status?.Invoke(action == TextAction.Cut ? "已送出剪下指令（請確認原 App 結果）" : "已送出貼上指令（請確認原 App 結果）");
                RememberBuiltIn(action);
            }
        }
        catch (ExternalException) { Report("clipboard-busy"); }
        finally { pathAction = null; executing = false; Invalidate(); }
    }

    private void Report(string status) => Status?.Invoke(status switch
    {
        "no-selection" => "未取得選取文字；請選取後再試。",
        "unsupported" => "目前未取得此控制項的選字介面；可能尚未就緒，請稍候重新選字。",
        "password" => "密碼欄位不提供選字動作。",
        "timeout" => "選字讀取逾時，已停止此次讀取；請重新選字。",
        "target-changed" or "focus-changed" or "selection-changed" or "expired" => "目標或選取已變更，已取消動作。",
        "not-editable" => "無法確認可編輯，已取消寫入。",
        "invalid-action" => "不支援此動作，已取消。",
        "invalid-link" => "選取內容不是完整的 HTTP／HTTPS 網址。",
        "invalid-search-template" => "搜尋網址設定無效，請至設定修正或恢復預設。",
        "web-address-too-long" => "搜尋或連結網址過長，請縮短選取內容後再試。",
        "browser-unavailable" => "無法開啟預設瀏覽器，請檢查 Windows 的預設瀏覽器設定。",
        "invalid-path" => "選取內容不是支援的完整檔案路徑。",
        "path-unavailable" => "找不到項目或無法存取，未開啟檔案總管。",
        "path-not-prepared" or "explorer-unavailable" => "無法在檔案總管顯示項目，請稍後再試。",
        "path-timeout" => "路徑查詢逾時，已停止此次操作。",
        "cancelled" => "操作已取消。",
        "modifier-held" => "請先放開 Ctrl、Alt、Shift 等修飾鍵。",
        "input-blocked" => "輸入指令未完整送出，請確認目標權限。",
        "clipboard-no-text" => "P0 貼上目前只接受含文字的剪貼簿。",
        "clipboard-busy" => "剪貼簿正被其他程式使用，請稍後再試。",
        "sublime-disabled" => "Sublime 不提供 UIA 選字；請勾選「Sublime 剪貼簿相容模式」。",
        "compatibility-setup-required" => "相容快捷鍵設定不存在或已變更；請重新勾選相容模式。",
        "sublime-no-selection" => "Sublime 未回傳單段選取；請選取文字，確認設定已載入且快捷鍵無衝突。",
        "clipboard-format-unsupported" => "剪貼簿含尚未支援備份的特殊格式，未觸發複製。",
        "clipboard-format-unavailable" => "目前剪貼簿無法完整備份，自動重試後仍未就緒；已保留原內容，未讀取選字。",
        "clipboard-backup-failed" => "剪貼簿備份失敗，未觸發複製。",
        "clipboard-backup-too-large" => "剪貼簿備份超過 64 MB，未觸發複製。",
        "clipboard-changed" => "剪貼簿在讀取期間再次變動，已取消並保留目前內容。",
        "clipboard-other-owner" => "剪貼簿已由其他程序接手，已取消並保留新內容。",
        "clipboard-input-changed" => "讀取期間收到新的操作，已取消此次選字。",
        "clipboard-restore-failed" => "剪貼簿還原失敗；請確認目前剪貼簿內容。",
        "too-large" => "選取超過原型的 32,768 字元／16 段限制。",
        "busy" => "上一筆讀取尚未完成，請重新選字。",
        _ => $"操作未完成（{status}），未寫入原 App。"
    });

    private void Invalidate()
    {
        customAction?.Cancel();
        resultWindow?.InvalidateTarget();
        pathAction?.Cancel();
        generation.Next(); delay?.Cancel(); delay?.Dispose(); delay = null;
        selection = null; popup.Dismiss();
        if (readinessTicket != null)
        { readinessTicket = null; Status?.Invoke("選字等待已取消，請重新選字。"); }
    }

    public void BeginShutdown()
    {
        stopping = true;
        Invalidate();
        resultWindow?.Close();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Invalidate(); resultWindow?.Close(); foregroundTimer.Stop(); monitor.Dispose(); probe.Dispose(); popup.Close();
    }
}
