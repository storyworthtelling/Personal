using Foreword.Domain.Brief;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Nyt;

/// <summary>
/// Which stories reach the brief, in what order, and with what said about them.
///
/// <para>Three things the pipeline did not do. It printed the same story twice when two sections
/// carried it; it printed them in whatever order two API calls happened to return; and when a full
/// article failed to fetch it printed the abstract <b>silently</b>, so a two-sentence story looked
/// like a short story rather than a failed download. That last one is bug B1.</para>
/// </summary>
public class BriefSelectionTests
{
    private static TopStory Story(string title, string section = "World", string? url = null) => new()
    {
        Title = title,
        Byline = "By Someone",
        Section = section,
        Url = url ?? $"https://example.com/{title.ToLowerInvariant().Replace(' ', '-')}",
        Abstract = "An abstract.",
    };

    // ---- dedupe ----

    /// <summary>
    /// The same story in two sections is one story. Top Stories returns a piece under Home and again
    /// under its own section, so the brief printed it twice — two full pages of identical text, and
    /// the owner counting eight stories and finding seven.
    /// </summary>
    [Fact]
    public void AStoryInTwoSectionsIsPrintedOnce()
    {
        var chosen = BriefSelection.Choose(
        [
            Story("The river towns are rebuilding", "Home"),
            Story("The river towns are rebuilding", "World"),
        ]);

        Assert.Single(chosen);
    }

    /// <summary>
    /// Matched on the URL, which is the story's identity, rather than on the headline — the same
    /// piece is re-titled between the home page and its section often enough that a title match
    /// would let the duplicate through.
    /// </summary>
    [Fact]
    public void TheSameUrlUnderTwoHeadlinesIsStillOneStory()
    {
        var chosen = BriefSelection.Choose(
        [
            Story("The river towns are rebuilding", "Home", "https://nyt.com/a"),
            Story("Rebuilding, but not where they were", "World", "https://nyt.com/a"),
        ]);

        Assert.Single(chosen);
    }

    /// <summary>
    /// And the one kept is the one in a real section. "Home" is a placement, not a subject, and a
    /// folio reading "Home · 1 of 8" tells the reader nothing.
    /// </summary>
    [Fact]
    public void TheCopyWithARealSectionIsTheOneKept()
    {
        var chosen = BriefSelection.Choose(
        [
            Story("The river towns are rebuilding", "Home", "https://nyt.com/a"),
            Story("The river towns are rebuilding", "World", "https://nyt.com/a"),
        ]);

        Assert.Equal("World", chosen.Single().Section);
    }

    /// <summary>Two genuinely different stories are two stories, however alike their headlines.</summary>
    [Fact]
    public void DifferentStoriesAreBothKept()
    {
        Assert.Equal(2, BriefSelection.Choose(
            [Story("A", "World"), Story("B", "World")]).Count);
    }

    // ---- order ----

    /// <summary>
    /// News before opinion, and within that the order the desk put them in. The brief was printed in
    /// whatever order two API calls happened to return, so an opinion column could open the paper —
    /// which is a different paper.
    /// </summary>
    [Fact]
    public void OpinionComesAfterTheNews()
    {
        var chosen = BriefSelection.Choose(
        [
            Story("A column", "Opinion"),
            Story("A report", "World"),
            Story("Another column", "Opinion"),
            Story("Another report", "Business"),
        ]);

        Assert.Equal(["A report", "Another report", "A column", "Another column"],
            chosen.Select(story => story.Title));
    }

    /// <summary>
    /// The desk's own order is kept inside each half. It is an editorial judgement made by people
    /// who do this for a living, and re-sorting it alphabetically or by section would throw that
    /// away for nothing.
    /// </summary>
    [Fact]
    public void TheDesksOrderIsKeptWithinEachHalf()
    {
        var chosen = BriefSelection.Choose(
            [Story("Third", "World"), Story("First", "Business"), Story("Second", "Science")]);

        Assert.Equal(["Third", "First", "Second"], chosen.Select(story => story.Title));
    }

    // ---- bug B1: the honest failure ----

    /// <summary>
    /// A story whose full text did not retrieve prints its abstract AND says so. Today the
    /// degradation is silent: a two-sentence page looks like a short story rather than a failed
    /// download, and the owner has no way to tell the difference — which is the whole of B1.
    /// </summary>
    [Fact]
    public void AnArticleThatFellBackToItsAbstractSaysSo()
    {
        var article = BriefSelection.FromAbstract(Story("Chip makers", "Business"));

        Assert.True(article.AbstractOnly);
        Assert.Equal("Chip makers", article.Title);
    }

    [Fact]
    public void AFullArticleDoesNotClaimToBeAnAbstract()
    {
        var full = new ArticleContent
        {
            Title = "The river towns", Byline = "By Someone", Section = "World",
            Body = [new TextBlock { Text = "A paragraph." }],
        };

        Assert.False(full.AbstractOnly);
    }

    /// <summary>
    /// A story with no abstract either is dropped rather than printed as a headline over nothing. A
    /// page carrying a title and white space is not a story; it is a fault printed at full size.
    /// </summary>
    [Fact]
    public void AStoryWithNoAbstractIsNotPrintedAtAll()
    {
        Assert.Null(BriefSelection.FromAbstract(Story("Chip makers") with { Abstract = "" }));
    }
}
