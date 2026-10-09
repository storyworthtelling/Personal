using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;

namespace Pulp.Tests.Children;

/// <summary>
/// G9 — the children's filter order and the unrated rule. Block beats allow beats ceiling (D12,
/// D129); a parent's favorite allow-lists (D255); unrated is hidden unless allow-listed (D128). Each
/// case here is one the wrong order gets wrong, silently: the title simply appears on a child's page.
/// </summary>
public class ChildFilterTests
{
    private static readonly TitleId Lantern = new("title-lantern-cove");

    private static ParentPick PickOf(TitleId title) =>
        new(title, new ViewerId("parent-example-a"), new DateTimeOffset(2026, 3, 1, 20, 0, 0, TimeSpan.Zero));
    private static readonly FranchiseId Harbour = new("franchise-harbour-tales");
    private static readonly ServiceId Flix = new("example-flix");
    private static readonly ServiceId Sample = new("sample-stream");

    private static ChildRules Child(AgeBand ceiling = AgeBand.SevenToNine) => new(new ViewerId("child-wren"), ceiling);

    private static TitleFacts Title(string? rating, params ServiceId[] homes) =>
        new(Lantern, TitleKind.Show, ContentRating.Parse(rating)) { Homes = homes, Franchises = [Harbour] };

    [Fact]
    public void ATitleUnderTheCeilingIsShown()
    {
        var verdict = ChildFilter.Decide(Child(), Title("TV-Y7"));

        Assert.True(verdict.Visible);
        Assert.Equal(FilterReason.WithinCeiling, verdict.Reason);
    }

    [Fact]
    public void ATitleAboveTheCeilingIsHidden()
    {
        var verdict = ChildFilter.Decide(Child(), Title("PG-13"));

        Assert.False(verdict.Visible);
        Assert.Equal(FilterReason.AboveCeiling, verdict.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Not Rated")]
    [InlineData("N/A")]
    public void AnUnratedTitleIsHiddenEvenFromTheOldestBand(string? rating)
    {
        var verdict = ChildFilter.Decide(Child(AgeBand.Seventeen), Title(rating));

        Assert.False(verdict.Visible);
        Assert.Equal(FilterReason.Unrated, verdict.Reason);
    }

    [Fact]
    public void AnUnratedTitleIsShownOnceAllowListed()
    {
        var verdict = ChildFilter.Decide(Child() with { Allowed = new HashSet<TitleId> { Lantern } }, Title(null));

        Assert.True(verdict.Visible);
        Assert.Equal(FilterReason.AllowListed, verdict.Reason);
    }

    [Fact]
    public void AllowBeatsCeiling()
    {
        var verdict = ChildFilter.Decide(Child() with { Allowed = new HashSet<TitleId> { Lantern } }, Title("PG-13"));

        Assert.True(verdict.Visible);
        Assert.Equal(FilterReason.AllowListed, verdict.Reason);
    }

    [Fact]
    public void AParentsFavoriteAboveTheCeilingAllowListsIt()
    {
        var verdict = ChildFilter.Decide(Child() with { ParentPicks = [PickOf(Lantern)] }, Title("PG-13"));

        Assert.True(verdict.Visible);
        Assert.Equal(FilterReason.ParentFavorite, verdict.Reason);
    }

    [Fact]
    public void BlockBeatsAllowAndBeatsAParentsFavorite()
    {
        var child = Child() with
        {
            Allowed = new HashSet<TitleId> { Lantern },
            ParentPicks = [PickOf(Lantern)],
            BlockedTitles = new HashSet<TitleId> { Lantern },
        };

        var verdict = ChildFilter.Decide(child, Title("TV-Y"));

        Assert.False(verdict.Visible);
        Assert.Equal(FilterReason.Blocked, verdict.Reason);
    }

    [Fact]
    public void BlockBeatsCeilingForATitleWellUnderIt()
    {
        var verdict = ChildFilter.Decide(Child(AgeBand.Seventeen) with { BlockedTitles = new HashSet<TitleId> { Lantern } }, Title("G"));

        Assert.Equal(FilterReason.Blocked, verdict.Reason);
    }

    [Fact]
    public void AFranchiseBlockHidesEveryTitleInIt()
    {
        var child = Child() with
        {
            Allowed = new HashSet<TitleId> { Lantern },
            BlockedFranchises = new HashSet<FranchiseId> { new("FRANCHISE-HARBOUR-TALES") },
        };

        Assert.Equal(FilterReason.Blocked, ChildFilter.Decide(child, Title("TV-Y")).Reason);
    }

    [Fact]
    public void AServiceBlockHidesATitleOnlyThatServiceCarries()
    {
        var child = Child() with { Allowed = new HashSet<TitleId> { Lantern }, BlockedServices = new HashSet<ServiceId> { Flix } };

        Assert.Equal(FilterReason.Blocked, ChildFilter.Decide(child, Title("TV-Y", Flix)).Reason);
    }

    [Fact]
    public void AServiceBlockLeavesATitleAnotherServiceAlsoCarries()
    {
        var child = Child() with { BlockedServices = new HashSet<ServiceId> { Flix } };

        Assert.True(ChildFilter.Decide(child, Title("TV-Y", Flix, Sample)).Visible);
    }

    [Fact]
    public void AnAllowListIsPerTitleAndAllowsNothingElse()
    {
        var other = new TitleFacts(new TitleId("title-other"), TitleKind.Film, ContentRating.Parse("PG-13"));

        var verdict = ChildFilter.Decide(Child() with { Allowed = new HashSet<TitleId> { Lantern } }, other);

        Assert.False(verdict.Visible);
    }

    /// <summary>
    /// The whole order at once, over every combination of the four inputs: whatever else is true, a
    /// block hides; otherwise an allow shows; otherwise unrated hides; otherwise the ceiling decides.
    /// </summary>
    [Fact]
    public void TheOrderHoldsOverEveryCombination()
    {
        var cases = 0;
        foreach (var blocked in new[] { false, true })
        foreach (var allowed in new[] { false, true })
        foreach (var favorite in new[] { false, true })
        foreach (var rating in new string?[] { null, "TV-Y", "PG-13", "NC-17" })
        {
            var child = Child() with
            {
                BlockedTitles = blocked ? new HashSet<TitleId> { Lantern } : new HashSet<TitleId>(),
                Allowed = allowed ? new HashSet<TitleId> { Lantern } : new HashSet<TitleId>(),
                ParentPicks = favorite ? [PickOf(Lantern)] : [],
            };

            var verdict = ChildFilter.Decide(child, Title(rating));
            var expected = blocked ? false
                : allowed || favorite ? true
                : rating is null ? false
                : rating == "TV-Y";

            Assert.True(expected == verdict.Visible, $"blocked={blocked} allowed={allowed} favorite={favorite} rating={rating ?? "unrated"}");
            cases++;
        }

        Assert.Equal(32, cases);
    }
}
