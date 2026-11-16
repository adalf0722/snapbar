namespace SnapBar.Core;

public record ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public record TargetIdentity(long Window, int ProcessId, long ProcessStarted);
public record SelectionSnapshot(
    string Id, TargetIdentity Target, string Text, ScreenRect? Bounds,
    bool? Editable, int RangeCount, int CharacterCount, string Source = "uia",
    string? WriteToken = null, CaretIdentity? WriteCaret = null);
public sealed record CaretIdentity(long Window, ScreenRect ClientBounds);
public record ProbeRequest(string Operation, long Window = 0, int X = 0, int Y = 0,
    string? SessionId = null, bool UsePointer = false, bool PrepareProvider = false,
    string? Path = null, TargetIdentity? Target = null, int LeaseSeconds = 0, bool Diagnostics = false);
// Metadata only: never add document values, control names, ranges or exception messages.
public sealed record SelectionProbeDiagnostics(string Stage = "start", int? FocusType = null,
    int? PointerType = null, int Candidates = 0, int EmptySelections = 0,
    bool SupportsSelection = false, bool? PointerInsideFocus = null,
    int FocusCandidates = 0, int? EmptyType = null, string? NativeSource = null, bool? ClipboardWritable = null);
public record ClipboardCaptureTarget(string Id, TargetIdentity Target, string? WriteToken = null,
    CaretIdentity? WriteCaret = null);
public record ProbeResponse(string Status, SelectionSnapshot? Selection = null, double ElapsedMs = 0,
    uint? ClipboardSequence = null, ClipboardCaptureTarget? ClipboardTarget = null,
    SelectionProbeDiagnostics? Diagnostics = null)
{
    public bool Success => Status == "ok";
}

public enum TextAction { Copy, Cut, Paste, Search, OpenLink, RevealPath, Ai, Ocr = 8, Lookup, Custom }
