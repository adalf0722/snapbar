namespace SnapBar.Core;

// A ticket is valid only for the exact card, editor revision and selection that created it.
public sealed record LocalActionTicket
{
    private LocalActionTicket(Guid cardId, long revision, string text, int selectionStart, int selectionLength, bool useSelection)
    {
        CardId = cardId; Revision = revision; Text = text; SelectionStart = selectionStart; SelectionLength = selectionLength;
        Input = useSelection ? (selectionLength == 0 ? text : text.Substring(selectionStart, selectionLength)) : "";
    }

    public Guid CardId { get; }
    public long Revision { get; }
    public string Text { get; }
    public int SelectionStart { get; }
    public int SelectionLength { get; }
    public string Input { get; }

    public static LocalActionTicket Create(Guid cardId, long revision, string text, int selectionStart, int selectionLength, bool useSelection)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (selectionStart < 0 || selectionLength < 0 || selectionStart > text.Length || selectionLength > text.Length - selectionStart)
            throw new ArgumentOutOfRangeException(nameof(selectionStart));
        return new(cardId, revision, text, selectionStart, selectionLength, useSelection);
    }

    public bool IsCurrent(Guid cardId, long revision, string text, int selectionStart, int selectionLength) =>
        CardId == cardId && Revision == revision && Text == text && SelectionStart == selectionStart && SelectionLength == selectionLength;

    public bool TryReplace(Guid cardId, long revision, string text, int selectionStart, int selectionLength, string replacement, out string updated)
    {
        updated = text;
        if (!IsCurrent(cardId, revision, text, selectionStart, selectionLength)) return false;
        updated = SelectionLength == 0 ? replacement : text.Remove(SelectionStart, SelectionLength).Insert(SelectionStart, replacement);
        return true;
    }
}
