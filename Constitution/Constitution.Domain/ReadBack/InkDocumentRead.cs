using Constitution.Domain.Ink;

namespace Constitution.Domain.ReadBack;

/// <summary>What the vision read said about pen ink on the pages as a whole, apart from any one box.</summary>
public enum PenStrokes
{
    /// <summary>The read did not say.</summary>
    Unknown,

    /// <summary>The read saw handwriting or pen ink somewhere on the pages.</summary>
    Seen,

    /// <summary>The read saw the pages entirely unmarked.</summary>
    None,
}

/// <summary>
/// One document as the vision read left it: a read (or a "not read") for each page, and its word on
/// whether any pen ink appears at all. A page the read said nothing about is not an empty page, it is a
/// page that was not read.
/// </summary>
public sealed class InkDocumentRead
{
    private readonly IReadOnlyDictionary<int, InkPageRead> pages;

    public InkDocumentRead(IReadOnlyDictionary<int, InkPageRead> pages, PenStrokes penStrokes)
    {
        this.pages = pages ?? throw new ArgumentNullException(nameof(pages));
        PenStrokes = penStrokes;
    }

    public PenStrokes PenStrokes { get; }

    /// <summary>The read for a page, or a "not read" when the read had nothing for it.</summary>
    public InkPageRead PageFor(int number) =>
        pages.TryGetValue(number, out var read) ? read : InkPageRead.NotRead($"The read had nothing for page {number}.");
}
