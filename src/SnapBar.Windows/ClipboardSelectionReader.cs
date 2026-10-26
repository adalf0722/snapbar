using System.Runtime.InteropServices;
using System.Text;
using SnapBar.Core;

namespace SnapBar.Windows;

public sealed record ClipboardReadAttempt(Func<bool> SendCopy, bool? Editable);
public sealed record ClipboardReadPlan(string Source, Func<TargetIdentity, bool> IsTarget,
    Func<bool> IsReady, IReadOnlyList<ClipboardReadAttempt> Attempts, string NoResponseStatus);

// All calls run on the owning UI thread. Clipboard locks never span an await.
public sealed class ClipboardSelectionReader(nint ownerWindow)
{
    private bool busy;
    public bool IsBusy => busy;
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardOwner();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);

    public async Task<ProbeResponse> CaptureAsync(TargetIdentity target, Func<bool> isCurrent,
        ClipboardReadPlan plan, uint? expectedSequence = null)
    {
        if (busy) return new("busy");
        if (!plan.IsTarget(target)) return new("target-changed");
        if (!plan.IsReady()) return new("compatibility-setup-required");
        if (!isCurrent() || Native.GetIdentity(Native.GetForegroundWindow()) != target) return new("target-changed");
        if (Native.AnyModifierDown()) return new("modifier-held");
        busy = true;
        using var backup = new ClipboardBackup();
        try
        {
            uint before;
            uint? retrySequence = null;
            for (var backupAttempt = 0; ; backupAttempt++)
            {
                bool opened = false;
                for (var attempt = 0; attempt < 8 && !opened; attempt++)
                {
                    if (!isCurrent() || Native.GetIdentity(Native.GetForegroundWindow()) != target) return new("target-changed");
                    opened = OpenClipboard(ownerWindow);
                    if (!opened) await Task.Delay(15);
                }
                if (!opened) return new("clipboard-busy");
                try
                {
                    if (!isCurrent() || Native.GetIdentity(Native.GetForegroundWindow()) != target) return new("target-changed");
                    var expected = retrySequence ?? expectedSequence;
                    if (expected is { } sequence && Native.GetClipboardSequenceNumber() != sequence)
                        return new("clipboard-changed");
                    var status = backup.Capture();
                    // Rendering may legitimately advance the sequence while we hold the lock.
                    before = Native.GetClipboardSequenceNumber();
                    if (status == "ok") break;
                    if (status != "clipboard-format-unavailable" || backupAttempt >= 2) return new(status);
                    retrySequence = before;
                }
                finally { CloseClipboard(); }
                // Let the source's message loop finish preparing its data. Keep no lock
                // across this wait, and reject new clipboard contents before retrying.
                await Task.Delay(40 * (backupAttempt + 1));
            }

            foreach (var step in plan.Attempts)
            {
                if (!isCurrent() || Native.GetIdentity(Native.GetForegroundWindow()) != target || !plan.IsTarget(target))
                    return new("target-changed");
                if (!plan.IsReady()) return new("compatibility-setup-required");
                if (Native.GetClipboardSequenceNumber() != before) return new("clipboard-changed");
                if (!step.SendCopy()) return new("input-blocked");

                // One transaction per gesture; a second condition is tried only if no copy arrived.
                // Once the target/input changes, stop probing. Any late clipboard write has
                // uncertain ownership and must not be overwritten with the old backup.
                for (var attempt = 0; attempt < 32; attempt++)
                {
                    await Task.Delay(25);
                    if (!isCurrent()) return new("clipboard-input-changed");
                    if (Native.GetIdentity(Native.GetForegroundWindow()) != target) return new("target-changed");
                    var observed = Native.GetClipboardSequenceNumber();
                    if (observed == before) continue;
                    // Require a stable sequence and recheck it while holding the clipboard lock.
                    await Task.Delay(35);
                    if (!isCurrent()) return new("clipboard-input-changed");
                    if (Native.GetIdentity(Native.GetForegroundWindow()) != target) return new("target-changed");
                    if (!OpenClipboard(ownerWindow)) continue;
                    try
                    {
                        var owner = GetClipboardOwner();
                        Native.GetWindowThreadProcessId(owner, out var pid);
                        var decision = ClipboardCapturePolicy.Evaluate(true,
                            Native.GetClipboardSequenceNumber() == observed, owner != 0, (int)pid, target.ProcessId,
                            Native.GetIdentity(Native.GetForegroundWindow()) == target, isCurrent());
                        if (decision != "ok") return new(decision);
                        var bytes = ReadBytes(13, (32768 + 1) * 2);
                        var text = bytes == null ? null : Encoding.Unicode.GetString(bytes).Split('\0')[0];
                        if (!backup.Restore()) return new("clipboard-restore-failed");
                        var restoredSequence = Native.GetClipboardSequenceNumber();
                        if (!isCurrent() || Native.GetIdentity(Native.GetForegroundWindow()) != target) return new("target-changed");
                        if (text == null || text.Length > 32768) return new("too-large");
                        if (text.Length == 0) return new("no-selection");
                        return new("ok", new(Guid.NewGuid().ToString("N"), target, text, null, step.Editable, 1,
                            text.Length, plan.Source), ClipboardSequence: restoredSequence);
                    }
                    finally { CloseClipboard(); }
                }
            }
            return new(plan.NoResponseStatus);
        }
        finally
        {
            busy = false;
        }
    }

    private static byte[]? ReadBytes(uint format, int limit)
    {
        var memory = GetClipboardData(format);
        if (memory == 0) return null;
        var size = GlobalSize(memory);
        if (size == 0 || size > (nuint)Math.Max(0, limit)) return null;
        var pointer = GlobalLock(memory);
        if (pointer == 0) return null;
        try { var bytes = new byte[(int)size]; Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes; }
        finally { GlobalUnlock(memory); }
    }

}
