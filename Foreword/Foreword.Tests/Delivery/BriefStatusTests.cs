using Xunit;
using Foreword.Domain.Delivery;

namespace Foreword.Tests.Delivery;

/// <summary>
/// What the delivery line says about the brief (audit W11, report 12 §28): plain words, a count that agrees with its
/// noun, "crossword" for the crossword, and never an exception's message, an API or a PDF.
/// </summary>
public class BriefStatusTests
{
    [Fact]
    public void AFullBriefCountsItsStories()
    {
        Assert.Equal("6 stories and the crossword", BriefStatus.Composed(6, fromSummary: 0));
        Assert.Equal("1 story and the crossword", BriefStatus.Composed(1, fromSummary: 0));
    }

    [Fact]
    public void StoriesPrintedFromTheirSummaryAreCounted() =>
        Assert.Equal("6 stories and the crossword, 2 from their summary only", BriefStatus.Composed(6, fromSummary: 2));

    [Theory]
    [InlineData(BriefShortfall.NewsUnreachable, "the crossword only — the news couldn’t be fetched")]
    [InlineData(BriefShortfall.NoStories, "the crossword only — the news had no stories")]
    [InlineData(BriefShortfall.NoStoryFetched, "the crossword only — no story could be fetched")]
    [InlineData(BriefShortfall.LayoutFailed, "the crossword only — the stories couldn’t be laid out")]
    public void AShortBriefSaysWhyInPlainWords(BriefShortfall why, string expected) =>
        Assert.Equal(expected, BriefStatus.CrosswordOnly(why));
}
