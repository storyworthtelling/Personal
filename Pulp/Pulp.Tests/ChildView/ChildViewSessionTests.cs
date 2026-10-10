using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.Shell;
using static Pulp.Tests.ChildView.ChildViewFixtures;

namespace Pulp.Tests.ChildView;

/// <summary>
/// The child's view as a mode of the shell (D213, D179, D231, D175): who is watching, which areas
/// show, where a request for another area lands, and what "Back to me" restores. Invented household.
/// </summary>
public class ChildViewSessionTests
{
    [Fact]
    public void AChildSeesThreeTabsWatchingPlaylistsAndDiscover()
    {
        var session = new ChildViewSession([Robin], PulpArea.Watching);

        Assert.Equal([PulpArea.Watching, PulpArea.Playlists, PulpArea.Discover], session.Areas);
    }

    [Theory]
    [InlineData(PulpArea.Sports)]
    [InlineData(PulpArea.Calendar)]
    [InlineData(PulpArea.Settings)]
    public void AnAreaAChildDoesNotSeeIsNotReachableByRequest(PulpArea requested)
    {
        var session = new ChildViewSession([Robin], PulpArea.Watching);

        Assert.False(session.Shows(requested));
        Assert.Equal(PulpArea.Watching, session.Route(requested));
    }

    [Theory]
    [InlineData(PulpArea.Watching)]
    [InlineData(PulpArea.Playlists)]
    [InlineData(PulpArea.Discover)]
    public void AnAreaTheChildSeesIsReachedAsAsked(PulpArea requested) =>
        Assert.Equal(requested, new ChildViewSession([Robin], PulpArea.Watching).Route(requested));

    [Fact]
    public void WithNoChildViewEveryAreaShows()
    {
        Assert.Equal(PulpAreas.All.Select(spec => spec.Area), ChildViewAreas.For(null));
        Assert.Equal([PulpArea.Watching, PulpArea.Playlists, PulpArea.Discover], ChildViewAreas.For(new ChildViewSession([Sam], PulpArea.Watching)));
    }

    [Fact]
    public void BackToMeRestoresTheAreaTheAdultLeftFrom()
    {
        var session = new ChildViewSession([Robin], PulpArea.Calendar);

        Assert.Equal(PulpArea.Calendar, session.BackToMe());
    }

    [Fact]
    public void ASessionNeedsAChild() =>
        Assert.Throws<ArgumentException>(() => new ChildViewSession([], PulpArea.Watching));

    [Fact]
    public void AChildPickedTwiceIsOneChild() =>
        Assert.Equal([Robin], new ChildViewSession([Robin, new ViewerId("robin")], PulpArea.Watching).Children);

    [Fact]
    public void TheLineUnderTheHeaderNamesOneChild() =>
        Assert.Equal("Watching as Robin", ChildViewText.WatchingAs(["Robin"]));

    [Fact]
    public void TheLineUnderTheHeaderNamesTwoChildrenTogether() =>
        Assert.Equal("Watching as Robin and Sam", ChildViewText.WatchingAs(["Robin", "Sam"]));

    [Fact]
    public void TheLineUnderTheHeaderNamesThreeChildrenAsAList() =>
        Assert.Equal("Watching as Robin, Sam and Ada", ChildViewText.WatchingAs(["Robin", "Sam", "Ada"]));

    [Fact]
    public void TheSessionsLabelReadsItsChildrenByName() =>
        Assert.Equal("Watching as Robin and Sam", new ChildViewSession([Robin, Sam], PulpArea.Watching).Label(id => id.Value));

    [Fact]
    public void ASessionResolvesToTheChildrensRules()
    {
        var session = new ChildViewSession([Robin, Sam], PulpArea.Watching);

        var resolved = session.Resolve(Roster(), [RobinRules(), SamRules()]);

        Assert.Null(resolved.Problem);
        Assert.Equal([Robin, Sam], resolved.Audience!.Watching);
    }

    [Fact]
    public void AnAdultCannotBeWatchedAsAChild()
    {
        var resolved = new ChildViewSession([Parent], PulpArea.Watching).Resolve(Roster(), [RobinRules()]);

        Assert.Null(resolved.Audience);
        Assert.NotNull(resolved.Problem);
    }

    [Fact]
    public void AChildWithNoRulesShowsNothingRatherThanEverything()
    {
        var resolved = new ChildViewSession([Robin, Sam], PulpArea.Watching).Resolve(Roster(), [RobinRules()]);

        Assert.Null(resolved.Audience);
        Assert.Contains("Sam", resolved.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoRosterThereIsNobodyToWatchAs()
    {
        var resolved = new ChildViewSession([Robin], PulpArea.Watching).Resolve(null, [RobinRules()]);

        Assert.Null(resolved.Audience);
        Assert.NotNull(resolved.Problem);
    }

    [Fact]
    public void TheSessionIsKeptAndReadBack()
    {
        var session = new ChildViewSession([Robin, Sam], PulpArea.Watching);

        var read = ChildViewSessionCodec.Decode(ChildViewSessionCodec.Encode(session));

        Assert.NotNull(read);
        Assert.Equal(session.Children, read!.Children);
        Assert.Equal(PulpArea.Watching, read.ReturnTo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nowhere\nRobin")]
    [InlineData("watching")]
    [InlineData("watching\n\n  ")]
    public void AKeptSessionThatCannotBeReadIsNoSession(string? text) =>
        Assert.Null(ChildViewSessionCodec.Decode(text));

    [Fact]
    public void OneChildIsPickedWithoutAsking()
    {
        var choices = ChildPickChoices.For([Robin], id => id.Value);

        Assert.Equal([new[] { Robin }], choices.Select(choice => choice.Children.ToArray()));
    }

    [Fact]
    public void SeveralChildrenAreOfferedOneByOneAndTogether()
    {
        var choices = ChildPickChoices.For([Robin, Sam], id => id.Value);

        Assert.Equal(["Robin", "Sam", "Robin and Sam together"], choices.Select(choice => choice.Label));
        Assert.Equal([Robin, Sam], choices[^1].Children);
    }

    [Fact]
    public void WithNoChildrenThereIsNothingToPick() =>
        Assert.Empty(ChildPickChoices.For([], id => id.Value));

    [Fact]
    public void TheChildrenToPickFromAreTheRostersChildren() =>
        Assert.Equal([Robin, Sam], ChildPickChoices.ChildrenOf(Roster()));
}
