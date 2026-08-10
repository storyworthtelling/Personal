using Storyboarder.Domain.Model;

namespace Storyboarder.Domain.Imaging;

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

// Assembles ordered page images into a single PDF. dpi is the physical resolution each full-page
// raster is declared at, so the PDF prints at the intended size rather than PdfSharp's default 96 DPI.
public interface IVolumePdfComposer
{
    byte[] Compose(IReadOnlyList<byte[]> pageImages, int dpi);
}

// One scene's supporting evidence in the report: the wordless image (null when the model couldn't render
// one), cited as "Ch. N · Scene M", with the scene's purpose and verdict from the craft read.
public sealed record SceneEvidence(string Citation, string Purpose, string Verdict, byte[]? Image);

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

// Renders the consolidated revision report — the star product — into a single image-and-text PDF, placing
// each scene's wordless evidence image beside its notes. On a later draft, the review carries the cross-draft
// finding status so the report marks what changed since the previous draft.
public interface IAnalysisPdfComposer
{
    byte[] Compose(
        Storyboarder.Domain.Analysis.BookAnalysisResult analysis,
        string title,
        IReadOnlyList<SceneEvidence> scenes,
        DraftReview review);
}
