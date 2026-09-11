using Foreword.Domain.Models;
using Foreword.Infrastructure.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;
using Xunit;

namespace Foreword.Tests.Pdf;

/// <summary>
/// The brief's article pages (B2, B2a).
///
/// <para>One story per page, full measure, no columns — at reMarkable size a two-column measure runs
/// to about thirty characters, well under the sixty-odd a reader wants. The folio names the section
/// and the position in it, so the reader always knows how much of the news is left.</para>
/// </summary>
public class ArticlePageTests
{
    private static ArticleContent Article(
        string title, string section = "World", bool abstractOnly = false, int paragraphs = 3) => new()
    {
        Title = title,
        Byline = "By Amara Whitfield · The New York Times",
        Section = section,
        AbstractOnly = abstractOnly,
        Body =
        [
            .. Enumerable.Range(0, paragraphs).Select(i => new TextBlock
            {
                Text = $"Paragraph {i}. " + string.Join(' ',
                    Enumerable.Repeat("The county bought out ninety-one properties along the east bank.", 3)),
            }),
        ],
    };

    private static PdfDocument Compose(params ArticleContent[] articles) =>
        PdfDocument.Open(new MemoryStream(
            new PdfComposer(NullLogger<PdfComposer>.Instance).ComposeNews(articles)));

    /// <summary>
    /// One story per page. Two articles that ran together down one page meant the reader met a new
    /// headline halfway down a column of somebody else's story.
    /// </summary>
    [Fact]
    public void EachStoryStartsItsOwnPage()
    {
        using var document = Compose(Article("The river towns"), Article("Chip makers", "Business"));

        Assert.Equal(2, document.NumberOfPages);
        Assert.Contains("The river towns", document.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("Chip makers", document.GetPage(2).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Chip makers", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSectionAndBylineAreOnThePage()
    {
        using var document = Compose(Article("The river towns"));
        var page = document.GetPage(1).Text;

        // Case-insensitively: the kicker is set in caps (B2's own styling), and which case it is
        // set in is presentation rather than what the page says.
        Assert.Contains("World", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Amara Whitfield", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Bug B1.</b> A story whose full text did not retrieve prints its abstract with one plain
    /// line under the headline. Today that degradation is silent, so a two-sentence page looks like
    /// a short story rather than a failed download — and the reader has no way to tell which.
    /// </summary>
    [Fact]
    public void AnAbstractOnlyStorySaysSoOnThePage()
    {
        using var document = Compose(Article("Chip makers", "Business", abstractOnly: true));

        Assert.Contains("the full article did not fetch", document.GetPage(1).Text,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And a story that fetched says nothing at all — the line is a failure notice.</summary>
    [Fact]
    public void AFullStorySaysNothingAboutFetching()
    {
        using var document = Compose(Article("The river towns"));

        Assert.DoesNotContain("did not fetch", document.GetPage(1).Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The folio names the section and the position in it, so the reader always knows how much of
    /// the news is left — which is the question a stack of single-story pages otherwise raises.
    /// </summary>
    [Fact]
    public void TheFolioNamesThePositionInTheSection()
    {
        using var document = Compose(
            Article("One"), Article("Two"), Article("Three", "Opinion"));

        Assert.Contains("The news · 1 of 2", document.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("The news · 2 of 2", document.GetPage(2).Text, StringComparison.Ordinal);
        Assert.Contains("Opinion · 1 of 1", document.GetPage(3).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A long story runs on rather than being cut. The folio keeps naming the same story, because a
    /// page two of a story is not a second story.
    /// </summary>
    [Fact]
    public void ALongStoryRunsOnAndTheFolioDoesNotAdvance()
    {
        using var document = Compose(Article("The river towns", paragraphs: 60));

        Assert.True(document.NumberOfPages > 1, "A sixty-paragraph story fitted on one page.");
        Assert.All(document.GetPages(),
            page => Assert.Contains("The news · 1 of 1", page.Text, StringComparison.Ordinal));
    }

    /// <summary>
    /// The headline appears once, on the story's first page. Repeating it on a continuation reads as
    /// a new story; the folio is what carries the identity across the fold.
    /// </summary>
    [Fact]
    public void TheHeadlineIsNotRepeatedOnAContinuation()
    {
        using var document = Compose(Article("The river towns", paragraphs: 60));

        Assert.Contains("The river towns", document.GetPage(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("The river towns", document.GetPage(2).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoStoriesMakesNoPages()
    {
        Assert.Empty(new PdfComposer(NullLogger<PdfComposer>.Instance).ComposeNews([]));
    }
}
