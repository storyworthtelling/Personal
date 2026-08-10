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

// Renders the consolidated revision report into a single, image-and-text PDF.
public interface IAnalysisPdfComposer
{
    byte[] Compose(Storyboarder.Domain.Analysis.BookAnalysisResult analysis, string title);
}
