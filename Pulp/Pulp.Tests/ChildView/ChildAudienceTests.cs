using Pulp.Domain.Availability;
using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.Ratings;
using Pulp.Domain.Services;
using static Pulp.Tests.ChildView.ChildViewFixtures;

namespace Pulp.Tests.ChildView;

/// <summary>
/// Who a child's view is for, as every area reads it: what it may show (D12, D175, D280 — the
/// strictest child, read as an intersection), whose names a hand-off carries (G13, D23), which
/// child's age the voice is written for (D233), and which home Play asks for (D210, D282).
/// </summary>
public class ChildAudienceTests
{
    private static readonly ServiceId Streamly = new("streamly-example");
    private static readonly ServiceId Kestrel = new("kestrel-example");

    private static readonly ServiceCatalogue Catalogue = new(
    [
        new StreamingService(Streamly, "Streamly", ["com.example.streamly"], "streamly", []),
        new StreamingService(Kestrel, "Kestrel TV", ["com.example.kestrel"], "kestrel", []),
    ]);

    [Fact]
    public void TogetherOnlyWhatEveryChildMaySeeIsShown()
    {
        var audience = ChildAudience.Of([RobinRules(AgeBand.SevenToNine), SamRules(AgeBand.UnderSeven)]);

        Assert.True(audience.Shows(Rated(Biscuit, "TV-Y")));
        Assert.False(audience.Shows(Rated(Bugs, "TV-Y7")));
    }

    [Fact]
    public void OneChildsAllowListNeverOpensATitleToAnother()
    {
        var robin = RobinRules() with { Allowed = new HashSet<TitleId> { Grim } };
        var audience = ChildAudience.Of([robin, SamRules()]);

        Assert.False(audience.Shows(Rated(Grim, "PG-13")));
    }

    [Fact]
    public void ATitleWithNoFactsIsHidden() =>
        Assert.False(ChildAudience.Of([RobinRules()]).Shows(null));

    [Fact]
    public void VisibleKeepsTheShownTitlesInTheirOrder()
    {
        var audience = ChildAudience.Of([RobinRules()]);
        var facts = new Dictionary<TitleId, TitleFacts>
        {
            [Biscuit] = Rated(Biscuit, "TV-Y"),
            [Grim] = Rated(Grim, "TV-MA"),
            [Tidewater] = Rated(Tidewater, "TV-Y7"),
        };

        var shown = audience.Visible(new[] { Grim, Tidewater, Cloudberry, Biscuit }, id => facts.GetValueOrDefault(id));

        Assert.Equal(new[] { Tidewater, Biscuit }, shown);
    }

    [Fact]
    public void TheHandOffCarriesTheChildrenAndNobodyElse() =>
        Assert.Equal(new[] { Robin, Sam }, ChildAudience.Of([RobinRules(), SamRules()]).Watching);

    [Fact]
    public void TheVoiceIsWrittenForTheYoungestBandWatching() =>
        Assert.Equal(Sam, ChildAudience.Of([RobinRules(AgeBand.TenToTwelve), SamRules(AgeBand.UnderSeven)]).ReadingLevel.Child);

    [Fact]
    public void AnAudienceNeedsAChild() =>
        Assert.Throws<ArgumentException>(() => ChildAudience.Of([]));

    [Fact]
    public void PlayAsksForTheBestHomeTheChildMayStartOn()
    {
        var audience = ChildAudience.Of([RobinRules()]);
        var title = Rated(Biscuit, "TV-Y") with { Homes = [Streamly, Kestrel] };
        var homes = new HomeChoice(new Home(Streamly, VideoQuality.Uhd, false), [new Home(Kestrel, VideoQuality.Hd, false)]);

        var home = audience.HomeFor(homes, title, Catalogue, new AttestedServices([Kestrel]));

        Assert.Equal(Kestrel, home!.Service);
    }

    [Fact]
    public void WithNoHomeTheChildMayStartOnPlayAsksForTheBestSoTheGateCanSayWhy()
    {
        var audience = ChildAudience.Of([RobinRules()]);
        var title = Rated(Biscuit, "TV-Y") with { Homes = [Streamly, Kestrel] };
        var homes = new HomeChoice(new Home(Streamly, VideoQuality.Uhd, false), [new Home(Kestrel, VideoQuality.Hd, false)]);

        var home = audience.HomeFor(homes, title, Catalogue, new AttestedServices([]));

        Assert.Equal(Streamly, home!.Service);
    }

    [Fact]
    public void WithNoHomeAtAllThereIsNothingToPlay()
    {
        var home = ChildAudience.Of([RobinRules()]).HomeFor(
            new HomeChoice(null, []), Rated(Biscuit, "TV-Y"), Catalogue, new AttestedServices([]));

        Assert.Null(home);
    }

    [Fact]
    public void AMoodSearchForOneChildIsThatChildsAndForSeveralIsEveryones()
    {
        Assert.Equal(new[] { Robin }, ChildAudience.Of([RobinRules()]).Mood.Children.Select(child => child.Child));
        Assert.Equal(new[] { Robin, Sam }, ChildAudience.Of([RobinRules(), SamRules()]).Mood.Children.Select(child => child.Child));
        Assert.True(ChildAudience.Of([RobinRules()]).Mood.IsFiltered);
    }

    [Fact]
    public void AnUnratedTitleStaysHiddenFromAChildsView() =>
        Assert.False(ChildAudience.Of([RobinRules(AgeBand.Seventeen)]).Shows(new TitleFacts(Grim, TitleKind.Film, ContentRating.Unrated)));
}
