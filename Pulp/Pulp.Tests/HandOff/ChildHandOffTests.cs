using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Ratings;
using Pulp.Domain.Services;

namespace Pulp.Tests.HandOff;

/// <summary>
/// G13 — a child can never reach an unattested service (D210, D44). Pulp cannot see a service's
/// profiles; the owner attests that every adult profile there is PIN-locked, and only then does a
/// child's hand-off go through. Everywhere else the refusal comes before any intent is built, so no
/// later step can send it by mistake. A kids-only app needs no attestation: it is the child's profile.
/// </summary>
public class ChildHandOffTests
{
    private static readonly StreamingService Flix = new(
        new ServiceId("example-flix"), "Exampleflix", ["com.example.flix"], "exampleflix", ["Exampleflix"]);

    private static readonly StreamingService Sample = new(
        new ServiceId("sample-stream"), "Samplestream", ["com.example.sample"], "samplestream", ["Samplestream"]);

    private static readonly StreamingService Tots = new(
        new ServiceId("example-tots"), "Exampletots", ["com.example.tots"], "exampletots", ["Exampletots"]) { KidsOnly = true };

    private static readonly TitleId Lantern = new("title-lantern-cove");

    private static readonly ChildRules Wren = new(new ViewerId("child-wren"), AgeBand.SevenToNine);

    private static TitleFacts Facts(params StreamingService[] homes) =>
        new(Lantern, TitleKind.Show, ContentRating.Parse("TV-Y")) { Homes = homes.Select(home => home.Id).ToList() };

    private static HandOffLink Link(StreamingService service) =>
        new(Lantern, service.Id, new Uri("https://watch.example.com/title/100"), null);

    [Fact]
    public void AChildsHandOffToAnAttestedServiceIsBuilt()
    {
        var result = ChildHandOff.For([Wren], Facts(Flix), Link(Flix), Flix, new AttestedServices([Flix.Id]));

        Assert.True(result.Verdict.MayStart);
        Assert.NotNull(result.Intent);
        Assert.Equal("com.example.flix", result.Intent.Package);
    }

    [Fact]
    public void AChildsHandOffToAnUnattestedServiceIsRefusedWithNoIntent()
    {
        var result = ChildHandOff.For([Wren], Facts(Flix, Sample), Link(Sample), Sample, new AttestedServices([Flix.Id]));

        Assert.False(result.Verdict.MayStart);
        Assert.Equal(StartRefusal.ServiceNotAttested, result.Verdict.Refusal);
        Assert.Null(result.Intent);
    }

    /// <summary>Every service in the public table, unattested, for every band: never an intent.</summary>
    [Fact]
    public void NoPublicServiceIsReachableByAChildUntilAttested()
    {
        var services = ServiceCatalogue.Public.All.Where(service => !service.KidsOnly).ToList();
        Assert.NotEmpty(services);
        var none = new AttestedServices([]);
        var checkedCount = 0;

        foreach (var service in services)
        foreach (var band in Enum.GetValues<AgeBand>())
        {
            var child = new ChildRules(new ViewerId("child-wren"), band) { Allowed = new HashSet<TitleId> { Lantern } };
            var result = ChildHandOff.For([child], Facts(service), Link(service), service, none);

            Assert.Null(result.Intent);
            Assert.Equal(StartRefusal.ServiceNotAttested, result.Verdict.Refusal);
            checkedCount++;
        }

        Assert.True(checkedCount > 0);
    }

    [Fact]
    public void AnAllowListDoesNotStandInForAttestation()
    {
        var child = Wren with { Allowed = new HashSet<TitleId> { Lantern }, ParentFavorites = new HashSet<TitleId> { Lantern } };

        var result = ChildHandOff.For([child], Facts(Sample), Link(Sample), Sample, new AttestedServices([]));

        Assert.Null(result.Intent);
    }

    [Fact]
    public void OneUnattestedChildInAGroupRefusesTheWholeGroup()
    {
        // Attestation is per service, not per child; a group with any child is held to it.
        var older = new ChildRules(new ViewerId("child-older"), AgeBand.ThirteenToSixteen);

        var result = ChildHandOff.For([older, Wren], Facts(Sample), Link(Sample), Sample, new AttestedServices([Flix.Id]));

        Assert.Null(result.Intent);
    }

    [Fact]
    public void AKidsOnlyAppNeedsNoAttestation()
    {
        var result = ChildHandOff.For([Wren], Facts(Tots), Link(Tots), Tots, new AttestedServices([]));

        Assert.True(result.Verdict.MayStart);
        Assert.NotNull(result.Intent);
    }

    [Fact]
    public void AHiddenTitleIsRefusedEvenOnAnAttestedService()
    {
        var facts = new TitleFacts(Lantern, TitleKind.Film, ContentRating.Parse("R")) { Homes = [Flix.Id] };

        var result = ChildHandOff.For([Wren], facts, Link(Flix), Flix, new AttestedServices([Flix.Id]));

        Assert.Equal(StartRefusal.Hidden, result.Verdict.Refusal);
        Assert.Null(result.Intent);
    }

    [Fact]
    public void ABlockedServiceIsRefusedEvenWhenAttestedAndAnotherHomeIsOpen()
    {
        var child = Wren with { BlockedServices = new HashSet<ServiceId> { Flix.Id } };

        var result = ChildHandOff.For([child], Facts(Flix, Sample), Link(Flix), Flix, new AttestedServices([Flix.Id, Sample.Id]));

        Assert.Equal(StartRefusal.ServiceBlocked, result.Verdict.Refusal);
        Assert.Null(result.Intent);
    }

    [Fact]
    public void AServiceThatIsNotOneOfTheTitlesHomesIsRefused()
    {
        var result = ChildHandOff.For([Wren], Facts(Flix), Link(Sample), Sample, new AttestedServices([Flix.Id, Sample.Id]));

        Assert.Equal(StartRefusal.NotAHome, result.Verdict.Refusal);
        Assert.Null(result.Intent);
    }

    [Fact]
    public void ALinkForAnotherTitleIsAProgrammingErrorNotARefusal() =>
        Assert.Throws<ArgumentException>(() => ChildHandOff.For(
            [Wren],
            Facts(Flix),
            new HandOffLink(new TitleId("title-other"), Flix.Id, new Uri("https://watch.example.com/title/2"), null),
            Flix,
            new AttestedServices([Flix.Id])));

    [Fact]
    public void AnEmptyGroupIsRefusedRatherThanTreatedAsAnAdult() =>
        Assert.Throws<ArgumentException>(() =>
            ChildHandOff.For([], Facts(Flix), Link(Flix), Flix, new AttestedServices([])));

    [Theory]
    [InlineData(false, false, ServiceBadge.AdultsOnly)]
    [InlineData(true, false, ServiceBadge.None)]
    [InlineData(false, true, ServiceBadge.None)]
    public void AnUnlockedServiceCarriesTheAdultsOnlyBadge(bool attested, bool kidsOnly, ServiceBadge badge)
    {
        var service = Sample with { KidsOnly = kidsOnly };
        var attestations = new AttestedServices(attested ? [Sample.Id] : []);

        Assert.Equal(badge, ChildFilter.BadgeFor(service, attestations));
    }
}
