using Constitution.Domain.Documents;
using Storyworthtelling.Paper;

namespace Constitution.Infrastructure.Documents;

/// <summary>
/// Renders laid-out pages to one vector PDF at the tablet's page size, each in the shared tablet frame
/// (<see cref="ConstitutionSheet"/>). <b>One render serves both
/// tablets</b>: nothing here takes a device profile, so the colour Paper Pro and the greyscale rM2
/// receive the same bytes. That is only possible because nothing carries meaning by colour; the
/// single coloured stroke is the header rule, and the grey panel simply shows it as a darker line.
/// </summary>
public static class ConstitutionDocumentRenderer
{
    public static byte[] Render(IReadOnlyList<InkPageLayout> pages) => Render(pages, framed: true);

    /// <summary>
    /// The pages' own content with no frame: no ground, header, tabs or folio. What Constitution's content rules are
    /// checked against (ink only, registered rungs, the voice hand only for the owner's voice); the frame is the suite's
    /// and is checked where it is drawn.
    /// </summary>
    public static byte[] RenderContent(IReadOnlyList<InkPageLayout> pages) => Render(pages, framed: false);

    private static byte[] Render(IReadOnlyList<InkPageLayout> pages, bool framed)
    {
        if (pages.Count == 0)
        {
            throw new ArgumentException("A document needs at least one page to render.", nameof(pages));
        }

        var tabs = framed ? ConstitutionSheet.Tabs(pages) : [];
        using var sink = new VectorPdfSink();
        using var ink = new DocumentInk();

        for (var index = 0; index < pages.Count; index++)
        {
            var canvas = ConstitutionPageSink.Begin(sink);
            if (framed)
            {
                ConstitutionSheet.Draw(canvas, pages[index], tabs, index);
            }

            InkPagePainter.Paint(canvas, pages[index], ink);
            sink.EndPage();
        }

        return sink.Close();
    }
}
