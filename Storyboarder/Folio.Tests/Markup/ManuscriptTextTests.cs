using Folio.Domain.Markup;
using Xunit;

namespace Folio.Tests.Markup;

/// <summary>
/// The bug this guards: a real .docx paragraph opened with a literal Word tab — the author's own
/// indent, extracted faithfully by <c>DocxTextExtractor</c> as <c>'\t'</c>, which is correct — and
/// the marked-manuscript renderer drew that character straight into the page. The embedded prose
/// font has no glyph for a tab, so Skia drew its .notdef box, and PdfPig read it back as a bare
/// NUL: every new paragraph opened with a small black square. Found on the first report generated
/// from a real device.
/// </summary>
public class ManuscriptTextTests
{
    [Fact]
    public void ATab_BecomesASingleSpace_SoNoUnprintableGlyphReachesThePage()
    {
        var printed = ManuscriptText.ForPrint("\tSoooo, I began.");

        Assert.Equal(" Soooo, I began.", printed);
    }

    /// <summary>
    /// The one property every caller actually depends on: <see cref="MarkSource"/> indexes a mark's
    /// span against this same text, and a note anchored two paragraphs down must still land on the
    /// right line. A substitution that changed length would shift everything after it.
    /// </summary>
    [Fact]
    public void PreservesLength_SoOffsetsComputedAgainstTheOriginalManuscriptStillLand()
    {
        var original = "One.\n\n\tTwo starts here.\n\n\tThree, with a \tmid-line tab.";

        var printed = ManuscriptText.ForPrint(original);

        Assert.Equal(original.Length, printed.Length);
    }

    [Fact]
    public void OnlyTouchesTabs_LeavingOrdinaryProseAndParagraphBreaksAlone()
    {
        var printed = ManuscriptText.ForPrint("No tabs here.\n\nSecond paragraph.");

        Assert.Equal("No tabs here.\n\nSecond paragraph.", printed);
    }

    [Fact]
    public void EmptyManuscript_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ManuscriptText.ForPrint(string.Empty));
    }
}
