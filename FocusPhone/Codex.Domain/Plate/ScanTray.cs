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
/// One page in the tray: what it is, and when it was scanned.
///
/// <para>The reference is opaque — a content URI on the phone, a plain string in the tests.
/// The Domain never opens one, so it does not need to know what it is. The time is here
/// because the tray screen lists pages as "Page 2 · scanned 4:39 PM", which is the only way
/// to tell two near-identical sheets apart when a long scan goes wrong.</para>
/// </summary>
public readonly record struct ScanPage(string Reference, DateTime ScannedAt);

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

    private ScanTray(ScanDestination destination, IReadOnlyList<ScanPage> pages)
    {
        Destination = destination;
        Pages = pages;
    }

    /// <summary>Opens an empty tray at the door the owner picked.</summary>
    public static ScanTray For(ScanDestination destination) => new(destination, []);

    /// <summary>Where this document is going, decided before the first page was scanned.</summary>
    public ScanDestination Destination { get; }

    /// <summary>The pages, in the order they will be bound into the document.</summary>
    public IReadOnlyList<ScanPage> Pages { get; }

    public int Count => Pages.Count;

    public bool HasPages => Pages.Count > 0;

    /// <summary>
    /// Adds a scanner run's pages after the ones already gathered. A cancelled run hands
    /// back nothing — or null, which ML Kit's nullable page list makes the ordinary way to
    /// see it — and changes nothing.
    /// </summary>
    public ScanTray Add(IEnumerable<string>? pageReferences, DateTime? scannedAt = null)
    {
        if (pageReferences is null)
        {
            return this;
        }

        var when = scannedAt ?? DateTime.Now;
        return new ScanTray(Destination, [.. Pages, .. pageReferences.Select(r => new ScanPage(r, when))]);
    }

    /// <summary>
    /// Moves one page to another position, carrying its scan time with it. Scanning a stack
    /// out of order is ordinary, and a single misfed sheet must not cost the whole document.
    /// A move that goes nowhere — the first page up, the last page down, an index that is no
    /// longer there — leaves the tray alone rather than throwing away a scan in progress.
    /// </summary>
    public ScanTray Move(int from, int to)
    {
        if (from == to || !InRange(from) || !InRange(to))
        {
            return this;
        }

        var moved = new List<ScanPage>(Pages);
        var page = moved[from];
        moved.RemoveAt(from);
        moved.Insert(to, page);
        return new ScanTray(Destination, moved);
    }

    private bool InRange(int index) => index >= 0 && index < Pages.Count;

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

        var kept = new List<ScanPage>(Pages);
        kept.RemoveAt(index);
        return new ScanTray(Destination, kept);
    }
}
