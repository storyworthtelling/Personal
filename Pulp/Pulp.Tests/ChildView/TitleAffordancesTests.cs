using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Title;
using static Pulp.Tests.ChildView.ChildViewFixtures;

namespace Pulp.Tests.ChildView;

/// <summary>
/// What a title's page offers, by who is watching (mockup 06, D161, D23, G13). An adult's page has
/// everything. A child's page has the rating chip, Play and the child's own star, and a title the
/// child's filter hides gets one quiet sentence in place of the page. "Open there instead", Add to…
/// (an adult's playlists), the trailer (it opens another app), the why-this line (an adult's taste) and
/// marking episodes (an adult's record) are not a child's. Invented names.
/// </summary>
public class TitleAffordancesTests
{
    [Fact]
    public void AnAdultsPageOffersEverything()
    {
        var page = TitleAffordances.For(null, null);

        Assert.True(page.Visible);
        Assert.Null(page.HiddenSentence);
        Assert.True(page.OpenThereInstead);
        Assert.True(page.AddTo);
        Assert.True(page.Trailer);
        Assert.True(page.WhyThis);
        Assert.True(page.MarkEpisodes);
        Assert.False(page.Star);
    }

    [Fact]
    public void AChildsPageOffersOnlyPlayAndTheirStar()
    {
        var audience = ChildAudience.Of([RobinRules()]);

        var page = TitleAffordances.For(audience, Rated(Biscuit, "TV-Y"));

        Assert.True(page.Visible);
        Assert.True(page.Star);
        Assert.False(page.OpenThereInstead);
        Assert.False(page.AddTo);
        Assert.False(page.Trailer);
        Assert.False(page.WhyThis);
        Assert.False(page.MarkEpisodes);
    }

    [Fact]
    public void ATitleTheChildCannotSeeShowsAQuietSentenceNotThePage()
    {
        var audience = ChildAudience.Of([RobinRules(AgeBand.UnderSeven)]);

        var page = TitleAffordances.For(audience, Rated(Grim, "TV-MA"));

        Assert.False(page.Visible);
        Assert.Equal(ChildViewText.TitleNotHere, page.HiddenSentence);
        Assert.False(page.Star);
    }

    [Fact]
    public void ATitleWithNoFactsIsHiddenFromAChildAsUnratedIs()
    {
        var page = TitleAffordances.For(ChildAudience.Of([RobinRules()]), null);

        Assert.False(page.Visible);
    }

    [Fact]
    public void AChildsViewWithNoResolvedChildrenOffersNothing()
    {
        var page = TitleAffordances.Unresolved;

        Assert.False(page.Visible);
        Assert.False(page.Star);
        Assert.False(page.AddTo);
        Assert.False(page.OpenThereInstead);
    }
}
