using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Tests.Services;

/// <summary>
/// The broadcaster-name to service table (D310): what a game's channel text, as the ESPN and MLB adapters
/// expose it, resolves to among the services the household ticked.
/// </summary>
public class BroadcasterTableTests
{
    private static readonly ServiceId Peacock = new("peacock");
    private static readonly ServiceId Prime = new("prime-video");
    private static readonly ServiceId Paramount = new("paramount-plus");

    private static BroadcasterTable Table() => BroadcasterTable.Public;

    [Theory]
    [InlineData("Peacock", "peacock")]
    [InlineData("  peacock ", "peacock")]
    [InlineData("Prime Video", "prime-video")]
    [InlineData("Amazon Prime Video", "prime-video")]
    [InlineData("Netflix", "netflix")]
    [InlineData("Paramount+", "paramount-plus")]
    [InlineData("CBS", "paramount-plus")]
    [InlineData("ESPN+", "disney-plus")]
    [InlineData("HBO Max", "hbo-max")]
    [InlineData("TNT", "hbo-max")]
    public void ANamedBroadcasterResolvesToItsServiceWhenTicked(string channel, string expected)
    {
        var resolution = Table().Resolve(channel, [new ServiceId(expected)]);

        Assert.Equal(BroadcastKind.Ticked, resolution.Kind);
        Assert.Equal(new ServiceId(expected), resolution.Service);
    }

    [Fact]
    public void AKnownBroadcasterOnAnUntickedServiceNamesTheServiceToTick()
    {
        var resolution = Table().Resolve("Peacock", [Prime]);

        Assert.Equal(BroadcastKind.NotTicked, resolution.Kind);
        Assert.Equal(Peacock, resolution.Service);
    }

    [Theory]
    [InlineData("Gridline TV · channel 7")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnUnknownOrMissingChannelResolvesToNothing(string? channel)
    {
        var resolution = Table().Resolve(channel, [Peacock, Prime]);

        Assert.Equal(BroadcastKind.Unknown, resolution.Kind);
        Assert.Null(resolution.Service);
    }

    [Fact]
    public void ASeveralNameChannelPrefersTheFirstTickedService()
    {
        // ESPN joins a broadcast's names with a middle dot; MLB gives one name. Either way, the household's
        // own services decide among them, in the order the channel text names them.
        var both = "CBS · Prime Video";

        Assert.Equal(Prime, Table().Resolve(both, [Prime]).Service);
        Assert.Equal(Paramount, Table().Resolve(both, [Paramount, Prime]).Service);
        var none = Table().Resolve(both, []);
        Assert.Equal(BroadcastKind.NotTicked, none.Kind);
        Assert.Equal(Paramount, none.Service);
    }

    [Fact]
    public void ANameIsMatchedWholeNotAsAFragment()
    {
        Assert.Equal(BroadcastKind.Unknown, Table().Resolve("Maximum Sports Network", [new ServiceId("hbo-max")]).Kind);
        Assert.Equal(BroadcastKind.Unknown, Table().Resolve("Peacockery", [Peacock]).Kind);
    }

    [Fact]
    public void EveryServiceTheTableNamesIsInTheCatalogueAndEveryNameIsClaimedOnce()
    {
        Assert.NotEmpty(Table().Entries);
        Assert.All(Table().Entries, entry => Assert.NotNull(ServiceCatalogue.Public.ById(entry.Service)));
        var names = Table().Entries.Select(entry => entry.Name.ToLowerInvariant()).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void ATableThatClaimsANameTwiceOrNamesAnUnknownServiceIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new BroadcasterTable(ServiceCatalogue.Public,
            [new BroadcasterEntry("Peacock", Peacock), new BroadcasterEntry("peacock", Prime)]));
        Assert.Throws<ArgumentException>(() => new BroadcasterTable(ServiceCatalogue.Public,
            [new BroadcasterEntry("Gridline Plus", new ServiceId("not-in-the-catalogue"))]));
    }

    [Fact]
    public void AboutPrintsOneLinePerServiceListingItsNames()
    {
        var lines = Table().AboutLines();

        Assert.Contains(lines, line => line.StartsWith("NBC, Peacock", StringComparison.Ordinal) && line.EndsWith("open in Peacock", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("CBS", StringComparison.Ordinal) && line.EndsWith("open in Paramount+", StringComparison.Ordinal));
        Assert.Equal(Table().Entries.Select(entry => entry.Service).Distinct().Count(), lines.Count);
    }

    [Fact]
    public void AServiceHomeLinkIsHttpsOnTheServicesOwnDomain()
    {
        foreach (var service in ServiceCatalogue.Public.All)
        {
            var home = BroadcasterTable.HomeLink(service);

            Assert.NotNull(home);
            Assert.Equal(Uri.UriSchemeHttps, home!.Scheme);
            Assert.Contains(service.LinkDomains, domain => home.Host == domain || home.Host.EndsWith("." + domain, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AServiceWithNoLinkDomainHasNoHomeLink()
    {
        var bare = new StreamingService(new ServiceId("bare"), "Bare", ["com.example.bare"], "bare", ["Bare"]);

        Assert.Null(BroadcasterTable.HomeLink(bare));
    }

    // D340: the channels these leagues are named with on ESPN's feed. Only a name whose service the catalogue holds is mapped.
    [Theory]
    [InlineData("CBS · Paramount+", "paramount-plus")]
    [InlineData("NBC · Peacock", "peacock")]
    [InlineData("Prime Video", "prime-video")]
    [InlineData("Apple TV", "apple-tv")]
    [InlineData("Apple TV+", "apple-tv")]
    public void TheChannelsTheNewLeaguesAreNamedWithResolve(string channel, string service)
    {
        Assert.Equal(new ServiceId(service), Table().Resolve(channel, [new ServiceId(service)]).Service);
    }

    [Theory]
    [InlineData("MLS Season Pass")]
    [InlineData("ION")]
    [InlineData("NWSL+")]
    [InlineData("Victory+")]
    [InlineData("FOX Deportes")]
    public void ChannelsWhoseAppIsNotInTheCatalogueStayUnmappedSoPlayShowsTheHonestCard(string channel)
    {
        Assert.Equal(BroadcastKind.Unknown, Table().Resolve(channel, [Peacock, Prime, Paramount]).Kind);
    }

    // D351: Apple TV is in the catalogue, so its two feed spellings open its app; MLS Season Pass is a product
    // name, not a channel the feed was seen to use, and stays unmapped until a feed is seen naming it.
    [Fact]
    public void AppleTvNamedAmongSeveralChannelsOpensWhenTicked()
    {
        var apple = new ServiceId("apple-tv");

        Assert.Equal(BroadcastKind.Ticked, Table().Resolve("Apple TV · MLS Season Pass", [apple]).Kind);
        Assert.Equal(BroadcastKind.NotTicked, Table().Resolve("Apple TV", [Peacock]).Kind);
    }

    [Fact]
    public void ADomainThatIsAlreadyASubdomainIsNotGivenAWwwPrefix()
    {
        var apple = ServiceCatalogue.Public.ById(new ServiceId("apple-tv"))!;

        Assert.Equal(new Uri("https://tv.apple.com/"), BroadcasterTable.HomeLink(apple));
        Assert.Equal(new Uri("https://www.peacocktv.com/"), BroadcasterTable.HomeLink(ServiceCatalogue.Public.ById(Peacock)!));
    }
}
