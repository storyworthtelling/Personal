using Folio.Domain.Analysis;
using Folio.Domain.Imaging;

namespace Folio.Infrastructure.Imaging;

// Renders the revision report straight to a vector PDF. There is no image composer in the path any
// more: the pages used to be rasterised to PNGs and wrapped, which is what made the text unselectable
// and the type soft on e-ink.
public sealed class AnalysisPdfComposer : IAnalysisPdfComposer
{
    private readonly AnalysisDocumentRenderer renderer;

    public AnalysisPdfComposer(AnalysisDocumentRenderer renderer)
    {
        this.renderer = renderer;
    }

    public byte[] Compose(
        BookAnalysisResult analysis, ReportCover cover, IReadOnlyList<SceneEvidence> scenes,
        DraftReview review, string? manuscript = null, IReadOnlyList<Folio.Domain.Markup.ChapterSpan>? chapters = null) =>
        renderer.Render(analysis, cover, scenes, review, manuscript, chapters);
}
