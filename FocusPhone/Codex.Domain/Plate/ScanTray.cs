namespace Codex.Domain.Plate;

/// <summary>The three doors into a scan, picked before the scanner opens.</summary>
public enum ScanDestination
{
    /// <summary>Google Drive.</summary>
    Drive,

    /// <summary>Post, as an attachment on a message the owner is about to write.</summary>
    Post,

    /// <summary>The paired reMarkable tablet.</summary>
    Remarkable,
}

/// <summary>
/// A scan in progress: the door it came through, and the pages gathered so far.
///
/// <para><b>Why Plate assembles the document itself.</b> ML Kit's document scanner caps a
/// single run at <see cref="PagesPerScannerRun"/> pages and the owner asked to scan "much
/// more if needed". So Plate stops taking the scanner's finished PDF and takes the per-page
/// images instead — which the shipped head already requests (<c>ResultFormatJpeg</c>
/// alongside <c>ResultFormatPdf</c>) and then discards — gathers them across as many runs as
/// it takes, and composes the document with the shared
/// <c>Storyworthtelling.Paper.PdfVolumeComposer</c>.</para>
///
/// <para>A page is an opaque reference: a content URI on the phone, a plain string in the
/// tests. The Domain never opens one, so it does not need to know what it is. Nothing here
/// touches the filesystem, which is what lets the phase's least-provable head have its
/// page arithmetic checked in the suite.</para>
///
/// <para>Immutable on purpose. The head holds the tray in a field and reassigns it; a tray
/// that mutated in place would let a caller who forgot to reassign appear to work right up
/// until an undo, when the pages it thought it had kept would be gone.</para>
/// </summary>
public sealed record ScanTray
{
    /// <summary>
    /// What one trip through Google's scanner can return. This is Google's limit, not ours —
    /// it is the reason the tray exists, and the number the head asks the scanner for.
    /// </summary>
    public const int PagesPerScannerRun = 50;

    private ScanTray(ScanDestination destination, IReadOnlyList<string> pages)
    {
        Destination = destination;
        Pages = pages;
    }

    /// <summary>Opens an empty tray at the door the owner picked.</summary>
    public static ScanTray For(ScanDestination destination) => new(destination, []);

    /// <summary>Where this document is going, decided before the first page was scanned.</summary>
    public ScanDestination Destination { get; }

    /// <summary>The pages, in the order they were scanned.</summary>
    public IReadOnlyList<string> Pages { get; }

    public int Count => Pages.Count;

    public bool HasPages => Pages.Count > 0;

    /// <summary>
    /// Adds a scanner run's pages after the ones already gathered. A cancelled run hands
    /// back nothing — or null, which ML Kit's nullable page list makes the ordinary way to
    /// see it — and changes nothing.
    /// </summary>
    public ScanTray Add(IEnumerable<string>? pageReferences) =>
        pageReferences is null ? this : new ScanTray(Destination, [.. Pages, .. pageReferences]);

    /// <summary>
    /// Drops one page. An index that is no longer there is a no-op rather than a throw: two
    /// taps on the same page is the ordinary way to produce one, and losing the whole tray
    /// to an exception would be far worse than doing nothing.
    /// </summary>
    public ScanTray RemoveAt(int index)
    {
        if (index < 0 || index >= Pages.Count)
        {
            return this;
        }

        var kept = new List<string>(Pages);
        kept.RemoveAt(index);
        return new ScanTray(Destination, kept);
    }
}
