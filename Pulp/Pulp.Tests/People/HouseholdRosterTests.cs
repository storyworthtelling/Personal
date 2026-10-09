using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Tests.People;

/// <summary>
/// S3 (D23, D283): "is this viewer a child" has one answer, the household roster's. A viewer the
/// roster does not name is refused rather than silently taken for an adult, and a viewer cannot be
/// listed twice with two answers. Invented household.
/// </summary>
public class HouseholdRosterTests
{
    private static readonly ViewerId Sam = new("adult-example-a");
    private static readonly ViewerId Kit = new("child-example-a");

    private static readonly HouseholdRoster Roster = new([new HouseholdViewer(Sam, IsChild: false), new HouseholdViewer(Kit, IsChild: true)]);

    [Fact]
    public void TheRosterSaysWhoIsAChild()
    {
        Assert.False(Roster.IsChild(Sam));
        Assert.True(Roster.IsChild(Kit));
        Assert.Equal([Kit], Roster.Children);
    }

    [Fact]
    public void AViewerNotOnTheRosterIsRefusedNotTakenForAnAdult()
    {
        var error = Assert.Throws<ArgumentException>(() => Roster.IsChild(new ViewerId("guest-example")));

        Assert.Contains("guest-example", error.Message);
    }

    [Fact]
    public void AViewerListedTwiceIsRefused() =>
        Assert.Throws<ArgumentException>(() => new HouseholdRoster(
            [new HouseholdViewer(Kit, IsChild: true), new HouseholdViewer(new ViewerId("CHILD-EXAMPLE-A"), IsChild: false)]));

    [Fact]
    public void AnEmptyRosterIsRefused() =>
        Assert.Throws<ArgumentException>(() => new HouseholdRoster([]));
}
