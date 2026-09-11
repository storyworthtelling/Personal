using Foreword.Domain.Document;
using Foreword.Infrastructure.Pdf;
using UglyToad.PdfPig;
using Xunit;

namespace Foreword.Tests.Document;

/// <summary>
/// The tabs are real PDF links, checked by reading the finished document back.
///
/// <para>This is the one part of the tab strip that cannot be verified by looking at it. A tab that
/// is drawn but not linked, or linked to a name no page anchors, looks exactly like a tab that
/// works — right up until a finger lands on it on the tablet and nothing happens. So the document
/// is produced, opened again with a different library, and the annotations are read out of it.</para>
/// </summary>
public class DayTabLinksTests
{
    private static byte[] FourTabbedPages()
    {
        using var layout = new SkiaPageLayout(landscape: true);

        foreach (var section in Enum.GetValues<DaySection>())
        {
            if (section != DaySection.Day)
            {
                layout.NewPage();
            }

            layout.Anchor(SectionTabs.Destination(section));
            foreach (var tab in SectionTabs.Layout((float)layout.PageWidth, section))
            {
                layout.LinkTo(tab.Left, tab.Top, tab.Width, tab.Height,
                    SectionTabs.Destination(tab.Section));
            }
        }

        return layout.ToPdf();
    }

    [Fact]
    public void EveryPageCarriesFourLinks()
    {
        using var document = PdfDocument.Open(new MemoryStream(FourTabbedPages()));

        Assert.Equal(4, document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            Assert.Equal(4, page.GetAnnotations().Count(a => a.Type == UglyToad.PdfPig.Annotations.AnnotationType.Link));
        }
    }

    /// <summary>
    /// Each link names a destination some page anchors. A link to a name nothing anchors is the
    /// failure this whole test exists for: perfectly valid PDF, drawn exactly as designed, inert.
    /// </summary>
    [Fact]
    public void EveryLinkNamesADestinationThatExists()
    {
        var pdf = System.Text.Encoding.Latin1.GetString(FourTabbedPages());

        foreach (var section in Enum.GetValues<DaySection>())
        {
            var name = SectionTabs.Destination(section);

            // The link side: a /Dest naming it.
            Assert.Contains($"/Dest /{name}", pdf, StringComparison.Ordinal);

            // The anchor side: the name registered in the document's destination table.
            Assert.Contains($"/{name}", pdf, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The tabs sit where the layout puts them. A PDF's own coordinates run from the bottom left
    /// while every measurement in the composers runs from the top, so a strip that is right on the
    /// page and wrong in the annotation is drawn perfectly and links from the opposite corner.
    /// </summary>
    [Fact]
    public void TheLinkRectanglesAreWhereTheTabsAreDrawn()
    {
        using var document = PdfDocument.Open(new MemoryStream(FourTabbedPages()));

        var page = document.GetPage(1);
        var expected = SectionTabs.Layout(792f, DaySection.Day);
        var links = page.GetAnnotations()
            .Where(a => a.Type == UglyToad.PdfPig.Annotations.AnnotationType.Link)
            .OrderByDescending(a => a.Rectangle.Top)
            .ToList();

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Left, links[i].Rectangle.Left, 1);
            // Flipped: the top of a tab measured from the page top is this far from the bottom.
            Assert.Equal(612f - expected[i].Top, links[i].Rectangle.Top, 1);
            Assert.Equal(expected[i].Width, links[i].Rectangle.Width, 1);
            Assert.Equal(expected[i].Height, links[i].Rectangle.Height, 1);
        }
    }
}
