using Constitution.Domain.Delivery;
using Constitution.Domain.Ink;

namespace Constitution.Domain.ReadBack;

/// <summary>The steps a document goes through in a read-back, in order. A failure names the step it happened in, and the page is kept.</summary>
public enum ReadBackStage
{
    Download,
    Read,
    Resolve,
    Persist,
    Ask,
    Trash,
}

/// <summary>What one page's ink came to, in the ids the page was painted with.</summary>
public sealed record PageResolutions(int Number, IReadOnlyList<InkResolution> Resolutions);

/// <summary>
/// The saved result of reading one document: every page's resolutions, kept before the page is trashed.
/// This is what later features read (the assessment baselines, the meal sheet, the injury check), and
/// where a question the owner has not answered yet waits to be put back on the Waiting page.
/// </summary>
public sealed record DocumentResult(
    string Name,
    DeliveryKind Kind,
    DateOnly Day,
    string Person,
    DateTimeOffset ReadAt,
    IReadOnlyList<PageResolutions> Pages)
{
    /// <summary>Every resolution on every page, in page order.</summary>
    public IReadOnlyList<InkResolution> All => Pages.SelectMany(page => page.Resolutions).ToList();

    /// <summary>The questions the owner still has to answer.</summary>
    public IReadOnlyList<InkResolution.NeedsAsking> Questions => All.OfType<InkResolution.NeedsAsking>().ToList();
}
