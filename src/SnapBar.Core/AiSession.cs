namespace SnapBar.Core;

public sealed record AiVersion(long Id, string Text, string Instruction, string BasisLabel)
{
    public long? BasisId { get; init; }
}
public sealed record AiRequestTicket(long Id, string SourceText, string Instruction, long? BasisId, int HistoryIndex, long DraftRevision, long ViewRevision);

public sealed class AiSession
{
    private const int MaximumText = 262144;
    private readonly List<AiVersion> versions = [];
    private readonly IReadOnlyList<AiVersion> readOnlyVersions;
    private string draft = "";
    private long draftRevision, viewRevision, requestId, versionId;
    private AiRequestTicket? active;
    // An evicted basis remains a source snapshot until the user explicitly changes basis.
    private string? basisSnapshot;
    public AiSession(string original)
    {
        if (string.IsNullOrWhiteSpace(original) || original.Length > MaximumText) throw new ArgumentException("Invalid source text.");
        Original = original;
        readOnlyVersions = versions.AsReadOnly();
    }
    public string Original { get; }
    public string Draft
    {
        get => draft;
        set { ArgumentNullException.ThrowIfNull(value); if (draft != value) { draft = value; draftRevision++; } }
    }
    public IReadOnlyList<AiVersion> Versions => readOnlyVersions;
    public AiVersion? Current => Index >= 0 && Index < versions.Count ? versions[Index] : null;
    public int Index { get; private set; } = -1;
    public long? BasisId { get; private set; }
    public string BasisLabel => BasisId is { } id ? $"第 {id} 版" : "原文";
    public bool ViewingOriginal { get; private set; } = true;
    public bool HasNewResult { get; private set; }
    public bool Busy => active != null;

    public AiRequestTicket Begin(string instruction)
    {
        if (Busy) throw new InvalidOperationException("An AI request is already active.");
        if (string.IsNullOrWhiteSpace(instruction) || instruction.Length > 8192) throw new ArgumentException("Invalid instruction.");
        var source = BasisId == null ? Original : versions.Find(v => v.Id == BasisId)?.Text ?? basisSnapshot;
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaximumText) throw new ArgumentException("Invalid source text.");
        active = new(++requestId, source, instruction, BasisId, Index, draftRevision, viewRevision);
        return active;
    }

    public bool Complete(AiRequestTicket ticket, string text)
    {
        if (active == null || !ReferenceEquals(active, ticket)) return false;
        active = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumText) return false;
        var previousBasisText = BasisId == null ? null : versions.Find(v => v.Id == BasisId)?.Text ?? basisSnapshot;
        var branchIndex = ticket.BasisId == null ? ticket.HistoryIndex : versions.FindIndex(v => v.Id == ticket.BasisId);
        // A detached basis is older than the retained branch; preserve its snapshot, begin a new branch.
        if (branchIndex + 1 < versions.Count) versions.RemoveRange(branchIndex + 1, versions.Count - branchIndex - 1);
        var version = new AiVersion(++versionId, text, ticket.Instruction, ticket.BasisId is { } id ? $"第 {id} 版" : "原文") { BasisId = ticket.BasisId };
        versions.Add(version);
        if (versions.Count > 20) versions.RemoveRange(0, versions.Count - 20);
        Index = versions.Count - 1;
        if (draftRevision == ticket.DraftRevision)
        {
            Draft = "";
            SetBasis(version);
        }
        else basisSnapshot = previousBasisText;
        if (viewRevision == ticket.ViewRevision) { ViewingOriginal = false; HasNewResult = false; }
        else HasNewResult = true;
        return true;
    }

    public void Cancel() => active = null;
    public void ShowOriginal() { ViewingOriginal = true; viewRevision++; }
    public void ShowResult() { if (Current != null) ViewingOriginal = false; HasNewResult = false; viewRevision++; }
    public void MoveHistory(int delta)
    {
        if (Busy || delta == 0) return;
        var next = (long)Index + delta;
        if (next < 0 || next >= versions.Count) return;
        Index = (int)next;
        ViewingOriginal = false;
        HasNewResult = false;
        viewRevision++;
        if (Draft.Length == 0) SetBasis(Current);
    }
    public void FromOriginal() { SetBasis(null); ShowOriginal(); }
    public void ContinueCurrent() { if (Current != null) { SetBasis(Current); ShowResult(); } }
    private void SetBasis(AiVersion? version) { BasisId = version?.Id; basisSnapshot = version?.Text; }
}
