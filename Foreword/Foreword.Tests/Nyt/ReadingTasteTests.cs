using Foreword.Domain.Brief;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Nyt;

/// <summary>
/// The reading taste profile (D29). <b>It ranks, never excludes.</b>
///
/// <para>That is the whole decision. A profile that filtered would quietly narrow the brief to what
/// the owner already agrees with, and they asked for eight stories "one of which you will disagree
/// with" — a paper that only ever confirmed them would be a worse paper, and the owner would have no
/// way to see it happening. So taste moves a story up or down the order and nothing is ever dropped
/// for it.</para>
/// </summary>
public class ReadingTasteTests
{
    private static TopStory Story(string title, string section) => new()
    {
        Title = title, Byline = "By Someone", Section = section,
        Url = $"https://nyt.com/{title}", Abstract = "An abstract.",
    };

    private static readonly ReadingTaste Taste = new()
    {
        Sections = new Dictionary<string, int> { ["Science"] = 3, ["Sports"] = -2 },
    };

    /// <summary>
    /// A favoured section rises. The order is the desk's judgement adjusted by the owner's, not
    /// replaced by it.
    /// </summary>
    [Fact]
    public void AFavouredSectionRises()
    {
        var ranked = ReadingTaste.Rank(
            [Story("A", "Business"), Story("B", "Science")], Taste);

        Assert.Equal(["B", "A"], ranked.Select(story => story.Title));
    }

    [Fact]
    public void ADislikedSectionSinks()
    {
        var ranked = ReadingTaste.Rank(
            [Story("A", "Sports"), Story("B", "Business")], Taste);

        Assert.Equal(["B", "A"], ranked.Select(story => story.Title));
    }

    /// <summary>
    /// <b>Nothing is ever dropped.</b> The disliked story is last, and it is still there — which is
    /// the difference between a paper with a view and a paper with a filter.
    /// </summary>
    [Fact]
    public void NothingIsEverExcluded()
    {
        var stories = new[] { Story("A", "Sports"), Story("B", "Science"), Story("C", "Business") };

        var ranked = ReadingTaste.Rank(stories, Taste);

        Assert.Equal(stories.Length, ranked.Count);
        Assert.Contains(ranked, story => story.Title == "A");
    }

    /// <summary>
    /// Even a section the owner has vetoed hard. A weight is a weight, however large — if it could
    /// reach zero the profile would be a filter with extra steps.
    /// </summary>
    [Fact]
    public void EvenAHeavilyVetoedSectionStillPrints()
    {
        var hard = new ReadingTaste { Sections = new Dictionary<string, int> { ["Sports"] = -1000 } };

        Assert.Contains(ReadingTaste.Rank([Story("A", "Sports")], hard), story => story.Title == "A");
    }

    /// <summary>
    /// Ties keep the desk's order. Within equal taste there is nothing to add, and re-sorting
    /// equals is how a stable list becomes a different list every morning for no reason.
    /// </summary>
    [Fact]
    public void EqualTasteKeepsTheDesksOrder()
    {
        var ranked = ReadingTaste.Rank(
            [Story("First", "Business"), Story("Second", "World")], Taste);

        Assert.Equal(["First", "Second"], ranked.Select(story => story.Title));
    }

    /// <summary>
    /// An empty profile changes nothing. On the first morning there is no taste yet, and a brief
    /// that reordered itself on no evidence would be reordering itself at random.
    /// </summary>
    [Fact]
    public void AnEmptyProfileChangesNothing()
    {
        var stories = new[] { Story("A", "Sports"), Story("B", "Science") };

        Assert.Equal(
            stories.Select(story => story.Title),
            ReadingTaste.Rank(stories, ReadingTaste.Empty).Select(story => story.Title));
    }

    /// <summary>
    /// Opinion still comes after the news. Taste ranks within what the section order already
    /// decided — a favoured column must not open the paper, because that is a different paper.
    /// </summary>
    [Fact]
    public void TasteRanksWithinTheSectionOrderRatherThanAcrossIt()
    {
        var loved = new ReadingTaste { Sections = new Dictionary<string, int> { ["Opinion"] = 10 } };

        var chosen = BriefSelection.Choose([Story("A column", "Opinion"), Story("A report", "World")]);
        var ranked = ReadingTaste.Rank(chosen, loved);

        Assert.Equal("A report", ranked[0].Title);
    }

    /// <summary>
    /// Sections are matched the way they arrive — the API's capitalisation is not something the
    /// owner sees or controls, so a profile written as "science" must still match "Science".
    /// </summary>
    [Fact]
    public void SectionMatchingIgnoresCase()
    {
        var lower = new ReadingTaste { Sections = new Dictionary<string, int> { ["science"] = 3 } };

        var ranked = ReadingTaste.Rank([Story("A", "Business"), Story("B", "Science")], lower);

        Assert.Equal("B", ranked[0].Title);
    }
}
