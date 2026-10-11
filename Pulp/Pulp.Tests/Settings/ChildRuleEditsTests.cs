using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;
using Pulp.Domain.Settings;

namespace Pulp.Tests.Settings;

/// <summary>
/// The Household page's edits to a child's rules (D12, D127–D129, D255). Each returns a new value and
/// keeps the parents' picks, which live in their own records and must not be dropped by an edit here.
/// </summary>
public class ChildRuleEditsTests
{
    private static readonly ViewerId Pip = new("pip-example");
    private static readonly TitleId Lantern = new("lantern-film-example");
    private static readonly ServiceId Streamly = new("streamly-example");
    private static readonly ParentPick Pick = new(new TitleId("orchard-show-example"), new ViewerId("robin-example"), new DateTimeOffset(2026, 4, 3, 20, 0, 0, TimeSpan.Zero));

    private static ChildRules Rules => new ChildRules(Pip, AgeBand.SevenToNine) { ParentPicks = [Pick] };

    /// <summary>A child added without a ceiling chosen yet starts at the strictest band, never the loosest.</summary>
    [Fact]
    public void ANewChildStartsAtTheYoungestBand()
    {
        var rules = ChildRuleEdits.NewChild(Pip);

        Assert.Equal(AgeBand.UnderSeven, rules.Ceiling);
        Assert.Empty(rules.Allowed);
        Assert.Empty(rules.BlockedTitles);
        Assert.Empty(rules.BlockedServices);
    }

    [Fact]
    public void TheCeilingChangesAndThePicksStay()
    {
        var rules = ChildRuleEdits.Ceiling(Rules, AgeBand.TenToTwelve);

        Assert.Equal(AgeBand.TenToTwelve, rules.Ceiling);
        Assert.Equal([Pick], rules.ParentPicks);
    }

    /// <summary>The latest choice is the parent's intent: allowing a blocked title unblocks it, and blocking an allowed one disallows it.</summary>
    [Fact]
    public void AllowAndBlockAreEachOthersOpposite()
    {
        var blocked = ChildRuleEdits.Block(Rules, Lantern);
        Assert.Contains(Lantern, blocked.BlockedTitles);
        Assert.DoesNotContain(Lantern, blocked.Allowed);

        var allowed = ChildRuleEdits.Allow(blocked, Lantern);
        Assert.Contains(Lantern, allowed.Allowed);
        Assert.DoesNotContain(Lantern, allowed.BlockedTitles);
        Assert.Equal([Pick], allowed.ParentPicks);
    }

    [Fact]
    public void AnAllowedTitleShowsAboveTheCeilingAndABlockedOneNever()
    {
        var facts = new TitleFacts(Lantern, TitleKind.Film, ContentRating.Parse("PG-13"));

        Assert.False(ChildFilter.Decide(Rules, facts).Visible);
        Assert.True(ChildFilter.Decide(ChildRuleEdits.Allow(Rules, Lantern), facts).Visible);
        Assert.False(ChildFilter.Decide(ChildRuleEdits.Block(ChildRuleEdits.Ceiling(Rules, AgeBand.Seventeen), Lantern), facts).Visible);
    }

    [Fact]
    public void UnallowAndUnblockTakeOnlyTheirOwnEntry()
    {
        var other = new TitleId("kestrel-film-example");
        var rules = ChildRuleEdits.Block(ChildRuleEdits.Allow(Rules, Lantern), other);

        Assert.DoesNotContain(Lantern, ChildRuleEdits.Unallow(rules, Lantern).Allowed);
        Assert.Contains(other, ChildRuleEdits.Unallow(rules, Lantern).BlockedTitles);
        Assert.DoesNotContain(other, ChildRuleEdits.Unblock(rules, other).BlockedTitles);
        Assert.Contains(Lantern, ChildRuleEdits.Unblock(rules, other).Allowed);
    }

    [Fact]
    public void AServiceIsBlockedAndUnblockedWhole()
    {
        var blocked = ChildRuleEdits.BlockService(Rules, Streamly);
        Assert.Contains(Streamly, blocked.BlockedServices);
        Assert.Empty(ChildRuleEdits.UnblockService(blocked, Streamly).BlockedServices);
    }

    [Fact]
    public void AFranchiseBlockCanBeLifted()
    {
        var series = new FranchiseId("lantern-series-example");
        var rules = Rules with { BlockedFranchises = new HashSet<FranchiseId> { series } };

        Assert.Empty(ChildRuleEdits.UnblockFranchise(rules, series).BlockedFranchises);
    }

    /// <summary>The dog-ear's undo of an unblocked series puts the block back, and nothing else.</summary>
    [Fact]
    public void AFranchiseBlockCanBePutBack()
    {
        var series = new FranchiseId("lantern-series-example");

        var rules = ChildRuleEdits.BlockFranchise(Rules, series);

        Assert.Equal([series], rules.BlockedFranchises);
        Assert.Equal([Pick], rules.ParentPicks);
        Assert.Empty(rules.BlockedTitles);
    }

    [Theory]
    [InlineData(AgeBand.UnderSeven, "Under 7")]
    [InlineData(AgeBand.SevenToNine, "7 to 9")]
    [InlineData(AgeBand.TenToTwelve, "10 to 12")]
    [InlineData(AgeBand.ThirteenToSixteen, "13 to 16")]
    [InlineData(AgeBand.Seventeen, "17")]
    public void EachBandIsNamedByItsAges(AgeBand band, string expected) => Assert.Equal(expected, ChildRuleEdits.BandLabel(band));

    /// <summary>The detail is read from <see cref="AgeBands"/> itself, so it cannot promise a rating the filter refuses (D281).</summary>
    [Theory]
    [InlineData(AgeBand.UnderSeven, "Up to G and TV-G")]
    [InlineData(AgeBand.SevenToNine, "Up to G and TV-Y7")]
    [InlineData(AgeBand.TenToTwelve, "Up to PG and TV-PG")]
    [InlineData(AgeBand.ThirteenToSixteen, "Up to PG-13 and TV-14")]
    [InlineData(AgeBand.Seventeen, "Up to R and TV-MA")]
    public void EachBandSaysTheHighestRatingsItAdmits(AgeBand band, string expected) =>
        Assert.Equal(expected, ChildRuleEdits.BandDetail(band));
}
