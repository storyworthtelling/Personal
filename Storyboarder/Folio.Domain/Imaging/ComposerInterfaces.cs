using Folio.Domain.Model;

namespace Folio.Domain.Imaging;

// Persists and reads generated images/PDFs on disk. Paths are deterministic so retries overwrite
// cleanly and stored paths stay valid across restarts.
public interface IImageStore
{
    // Writes bytes at a path relative to the configured output root and returns the absolute path.
    string Save(string relativePath, byte[] content);
    byte[] Read(string absolutePath);
    bool Exists(string absolutePath);

    // Recursively removes a file or directory under the output root (e.g. a whole novel's output
    // subtree). A no-op if nothing is there, so deleting an already-clean project can't throw.
    void Delete(string relativePath);
}

// One scene's supporting evidence in the report: the wordless image (null when the model couldn't render
// one), cited as "Ch. N · Scene M", with the scene's purpose and verdict from the craft read.
public sealed record SceneEvidence(
    string Citation, string Purpose, string Verdict, byte[]? Image, byte[]? PreviousImage = null)
{
    /// <summary>
    /// Whether this block prints a before and an after. It needs both halves: a scene whose own
    /// render failed must not print its predecessor alone, or the reader takes the old picture for
    /// this draft's.
    /// </summary>
    public bool ShowsPair => Image is not null && PreviousImage is not null;
}

// One finding as it appears in the report, tagged with the cross-draft status the reconciliation gave it, so
// the report can mark each [new] / [carried] / [regressed] on a later draft.
public sealed record ReportFindingView(FindingCategory Category, string Chapter, string Detail, FindingStatus Status);

// The cross-draft context the report renders: which draft this is, this draft's findings with their status,
// and the findings resolved since the previous draft. On a first draft the tags would be noise, so it is
// FirstDraft (draft 1, empty lists) and the report renders without status marks or a "since last draft"
// summary.
public sealed record DraftReview(
    int DraftNumber,
    IReadOnlyList<ReportFindingView> Findings,
    IReadOnlyList<ReportFindingView> Resolved)
{
    public static DraftReview FirstDraft { get; } = new(1, Array.Empty<ReportFindingView>(), Array.Empty<ReportFindingView>());
}

/// <summary>
/// What the report's cover page prints, which is more than a title: the draft it is reading, how long
/// that draft runs, when it was read, the book's own jacket, and — on a sequel — where it sits in its
/// series.
///
/// <para>The jacket is carried here rather than fetched by the renderer because it is also what sets
/// the owner's mark's colourway (F37), and a page that reached out for an image mid-render could
/// print a mark in colours no jacket on the page actually has.</para>
/// </summary>
public sealed record ReportCover(
    string Title,
    int DraftNumber,
    int WordCount,
    DateTimeOffset ReadOn,
    byte[]? Jacket,
    string? SeriesLine);

// Renders the consolidated revision report — the star product — into a single vector PDF, placing
// each scene's wordless evidence image beside its notes. On a later draft, the review carries the cross-draft
// finding status so the report marks what changed since the previous draft.
public interface IAnalysisPdfComposer
{
    /// <summary>
    /// <paramref name="manuscript"/> is the draft's own text, which the report marks up (Release G).
    /// Optional, so a report can still be composed for a draft whose source is not to hand.
    /// <paramref name="chapters"/> says where each chapter sits in that text, which is what lets a
    /// finding that cites only a chapter be written on the opening of it; without them such a finding
    /// is printed among the whole-book notes instead.
    /// </summary>
    byte[] Compose(
        Folio.Domain.Analysis.BookAnalysisResult analysis,
        ReportCover cover,
        IReadOnlyList<SceneEvidence> scenes,
        DraftReview review,
        string? manuscript = null,
        IReadOnlyList<Folio.Domain.Markup.ChapterSpan>? chapters = null);
}
