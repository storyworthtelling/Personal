using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Ratings;

namespace Pulp.Tests.Children;

/// <summary>
/// D175 — several children watching together: the strictest filter wins. Kept pure here although
/// the together-watching screen is the TV phase's. A title is shown to the group only when every
/// child in it would be shown it, so one child's allow-list never opens a title to a younger one.
/// </summary>
public class WatchingTogetherTests
{
    private static readonly TitleId Lantern = new("title-lantern-cove");

    private static readonly ChildRules Older = new(new ViewerId("child-older"), AgeBand.ThirteenToSixteen);
    private static readonly ChildRules Younger = new(new ViewerId("child-younger"), AgeBand.UnderSeven);

    private static TitleFacts Title(string? rating) => new(Lantern, TitleKind.Film, ContentRating.Parse(rating));

    [Fact]
    public void TheYoungestCeilingDecidesForTheGroup()
    {
        var verdict = ChildFilter.DecideTogether([Older, Younger], Title("PG"));

        Assert.False(verdict.Visible);
        Assert.Equal(FilterReason.AboveCeiling, verdict.Reason);
        Assert.Equal(Younger.Child, verdict.HiddenFor);
    }

    [Fact]
    public void OneChildsAllowListDoesNotOpenATitleToAnother()
    {
        var older = Older with { Allowed = new HashSet<TitleId> { Lantern } };

        Assert.False(ChildFilter.DecideTogether([older, Younger], Title("R")).Visible);
    }

    [Fact]
    public void OneChildsBlockHidesTheTitleFromTheGroupEvenUnderEveryCeiling()
    {
        var younger = Younger with { BlockedTitles = new HashSet<TitleId> { Lantern } };

        var verdict = ChildFilter.DecideTogether([Older, younger], Title("G"));

        Assert.False(verdict.Visible);
        Assert.Equal(FilterReason.Blocked, verdict.Reason);
    }

    [Fact]
    public void ATitleEveryChildMaySeeIsShown() =>
        Assert.True(ChildFilter.DecideTogether([Older, Younger], Title("G")).Visible);

    [Fact]
    public void TheGroupVerdictDoesNotDependOnWhoWasPickedFirst()
    {
        foreach (var rating in new string?[] { null, "G", "PG", "PG-13", "R" })
        {
            Assert.Equal(
                ChildFilter.DecideTogether([Older, Younger], Title(rating)).Visible,
                ChildFilter.DecideTogether([Younger, Older], Title(rating)).Visible);
        }
    }

    [Fact]
    public void AGroupWithNoChildIsNotAChildsFilter() =>
        Assert.Throws<ArgumentException>(() => ChildFilter.DecideTogether([], Title("G")));
}
