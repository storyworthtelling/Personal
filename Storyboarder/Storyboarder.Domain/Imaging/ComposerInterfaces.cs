namespace Storyboarder.Domain.Imaging;

// Persists and reads generated images/PDFs on disk. Paths are deterministic so retries overwrite
// cleanly and stored paths stay valid across restarts.
public interface IImageStore
{
    // Writes bytes at a path relative to the configured output root and returns the absolute path.
    string Save(string relativePath, byte[] content);
    byte[] Read(string absolutePath);
    bool Exists(string absolutePath);
}

// The page furniture outside the panels: the folio band's page number and
// running volume title (e.g. "My Novel · Volume 2").
public sealed record PageChrome(int PageNumber, string VolumeTitle);

// Composites a page's panel images into a single page raster, typesetting dialogue/captions
// and drawing the folio band.
public interface IPageComposer
{
    byte[] Compose(IReadOnlyList<PanelArt> panels, PageChrome chrome);
}

// Assembles ordered page images into a single volume PDF.
public interface IVolumePdfComposer
{
    byte[] Compose(IReadOnlyList<byte[]> pageImages);
}

// Renders the consolidated book-analysis (revision report) into a single, text-heavy PDF.
public interface IAnalysisPdfComposer
{
    byte[] Compose(Storyboarder.Domain.Analysis.BookAnalysisResult analysis, string title);
}
