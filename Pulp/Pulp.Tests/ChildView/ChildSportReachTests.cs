using System.Text.RegularExpressions;
using Pulp.Domain.ChildView;
using Pulp.Domain.Shell;

namespace Pulp.Tests.ChildView;

/// <summary>
/// D231 as amended by D325, held structurally: a child's tab set stays three, and only the content of Watching
/// grew. The head is not compiled in this suite, so the Android half is read by content.
/// </summary>
public class ChildSportReachTests
{
    private static string Text(string relative) => File.ReadAllText(Path.Combine(PulpSource.Root(), relative));

    [Fact]
    public void TheChildsTabSetIsStillExactlyThree()
    {
        var areas = ChildViewAreas.For(new ChildViewSession([ChildViewFixtures.Robin], PulpArea.Sports));

        Assert.Equal([PulpArea.Watching, PulpArea.Playlists, PulpArea.Discover], areas.OrderBy(area => Array.IndexOf(
            new[] { PulpArea.Watching, PulpArea.Playlists, PulpArea.Discover }, area)));
        Assert.Equal(3, areas.Count);
    }

    [Fact]
    public void ARequestForSportsInAChildsViewLandsOnWatching()
    {
        var session = new ChildViewSession([ChildViewFixtures.Robin], PulpArea.Sports);

        Assert.Equal(PulpArea.Watching, session.Route(PulpArea.Sports));
    }

    [Fact]
    public void TheChildsWatchingPageIsWhereTheSportSectionIsDrawn()
    {
        var page = Text("Pulp.Android/Shell/ChildWatchingPage.cs");

        Assert.Contains("ChildSportAsync", page, StringComparison.Ordinal);
        Assert.Contains("ChildSportSectionView", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSportSectionViewReadsNoFeedAndOpensNoAdultSportsScreen()
    {
        var view = Text("Pulp.Android/Shell/ChildSportSectionView.cs");

        Assert.DoesNotMatch(
            new Regex(@"\b(LeagueFeed|FollowedFeedsAsync|SportsScreen|SportsArea|SportsPage|OpenLeague|Standings|TapSpoilersTag|HighlightsOffer|PlayHighlights)\b"),
            view);
    }

    [Fact]
    public void TheSportSectionViewHandsOffOnlyAsTheChildrenWatchingThroughTheOneDoor()
    {
        var view = Text("Pulp.Android/Shell/ChildSportSectionView.cs");

        Assert.Contains("new HandOffRequest(audience.Watching", view, StringComparison.Ordinal);
        Assert.Contains("executor.Start(", view, StringComparison.Ordinal);
        Assert.Contains("game.Play is not { Opens: true }", view, StringComparison.Ordinal);
        // D326: the request carries the game's league, so the planner reads the same parent allowance the section did.
        Assert.Matches(@"GamePlay\.Request\([^;]*game\.Title, league\)", view);
    }

    [Fact]
    public void TheServicesDoorToChildSportReadsNothingWhenNothingIsAllowed()
    {
        var services = Text("Pulp.Android/App/PulpServices.cs");

        Assert.Matches(@"ChildSports\.AllowedForAll\(", services);
        Assert.Matches(@"allowed\.Count == 0 \|\| follows\.Count == 0", services);
    }
}
