using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.Design;
using Storyboarder.Domain.Imaging;
using Storyworthtelling.Paper;

namespace Storyboarder.Infrastructure.Imaging;

// Renders the revision report to raster pages (AnalysisDocumentRenderer), then reuses the existing
// image-based PDF assembler to wrap those pages into a single analysis PDF — no PDF-text or font
// machinery, and the same rendering path the comic volumes already ship on-device.
public sealed class AnalysisPdfComposer : IAnalysisPdfComposer
{
    private readonly IVolumePdfComposer pdfComposer;
    private readonly AnalysisDocumentRenderer renderer;

    public AnalysisPdfComposer(IVolumePdfComposer pdfComposer, AnalysisDocumentRenderer renderer)
    {
        this.pdfComposer = pdfComposer;
        this.renderer = renderer;
    }

    public byte[] Compose(BookAnalysisResult analysis, string title, IReadOnlyList<SceneEvidence> scenes, DraftReview review)
    {
        var pages = renderer.Render(analysis, title, scenes, review);
        return pdfComposer.Compose(pages, DesignTokens.Pdf.PrintDpi);
    }
}
