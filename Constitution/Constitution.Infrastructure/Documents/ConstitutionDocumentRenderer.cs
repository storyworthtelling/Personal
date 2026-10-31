using Constitution.Domain.Documents;
using Storyworthtelling.Paper;

namespace Constitution.Infrastructure.Documents;

/// <summary>
/// Renders laid-out pages to one vector PDF at the tablet's page size. <b>One render serves both
/// tablets</b>: nothing here takes a device profile, so the colour Paper Pro and the greyscale rM2
/// receive the same bytes. That is only possible because nothing carries meaning by colour; the
/// single coloured stroke is the header rule, and the grey panel simply shows it as a darker line.
/// </summary>
public static class ConstitutionDocumentRenderer
{
    public static byte[] Render(IReadOnlyList<InkPageLayout> pages)
    {
        if (pages.Count == 0)
        {
            throw new ArgumentException("A document needs at least one page to render.", nameof(pages));
        }

        using var sink = new VectorPdfSink();
        using var ink = new DocumentInk();

        foreach (var page in pages)
        {
            var canvas = ConstitutionPageSink.Begin(sink);
            InkPagePainter.Paint(canvas, page, ink);
            sink.EndPage();
        }

        return sink.Close();
    }
}
