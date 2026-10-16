using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using Pulp.Domain.Sports;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Children;

/// <summary>
/// D59, D72, D231 and the follow-up ruling: a child sees sport only for a league a parent allowed,
/// never a held league's revealing games, never a score, never a peek. Nothing allowed means nothing.
/// </summary>
public class ChildSportsTests
{
    private static readonly LeagueId Harbor = new("harbor-league");
    private static readonly ViewerId Wren = new("wren-example");
    private static readonly LeagueId Ridge = new("ridge-league");
    private static readonly DateTimeOffset Now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);

    private static ChildLeagueAllowance Allow(ViewerId child, LeagueId league) => new(child, league, Avery, Evening);

    private static Team TeamOf(string id) => new(new TeamId(id), id + " team", "art");

    private static LeagueFeed Feed(LeagueId id, params Game[] games)
    {
        var gulls = TeamOf("gulls");
        var herons = TeamOf("herons");
        var league = new League(id, id.Value, "art", SeasonPhase.Postseason, new DateOnly(2026, 9, 1), null);
        return new LeagueFeed(league, [gulls, herons], games, [gulls, herons], [new Highlight("vid1", "They won it all")], null);
    }

    private static Game GameOf(string id, GameStage stage, DateTimeOffset at) =>
        new(new GameId(id), TeamOf("gulls"), TeamOf("herons"), at, stage, "Channel 5");

    [Fact]
    public void NothingAllowedMeansNothingShown()
    {
        var allowed = ChildSports.AllowedFor([], Pip);

        Assert.Empty(allowed);
        Assert.False(ChildSports.Shows(allowed, Harbor));
        Assert.Null(ChildSports.Present(Feed(Harbor), allowed, [], [], Now));
    }

    [Fact]
    public void AnAllowanceIsPerChildAndPerLeague()
    {
        var all = new[] { Allow(Pip, Harbor), Allow(Robin, Ridge) };

        Assert.Equal([Harbor], ChildSports.AllowedFor(all, Pip));
        Assert.Equal([Ridge], ChildSports.AllowedFor(all, Robin));
        Assert.Empty(ChildSports.AllowedFor(all, Avery));
    }

    [Fact]
    public void ChildrenTogetherSeeOnlyWhatEveryChildMaySee()
    {
        var all = new[] { Allow(Pip, Harbor), Allow(Pip, Ridge), Allow(Robin, Ridge) };

        Assert.Equal([Ridge], ChildSports.AllowedForAll(all, [Pip, Robin]));
        Assert.Empty(ChildSports.AllowedForAll(all, [Pip, Avery]));
        Assert.Empty(ChildSports.AllowedForAll(all, []));
    }

    [Fact]
    public void AnAllowedOpenLeagueShowsItsGamesButNeverHighlights()
    {
        var game = GameOf("g1", GameStage.RegularSeason, Now.AddDays(1));
        var sheet = ChildSports.Present(Feed(Harbor, game), new HashSet<LeagueId> { Harbor }, [new TeamId("gulls")], [], Now);

        var open = Assert.IsType<OpenLeague>(sheet);
        Assert.Empty(open.Highlights);
        Assert.False(open.IsPeek);
        Assert.Single(Assert.Single(open.Teams).Games);
    }

    [Fact]
    public void AHeldLeagueStaysHeldForAChildWithNoPeek()
    {
        var recorded = GameOf("g1", GameStage.RegularSeason, Now.AddHours(-5));
        var later = GameOf("g2", GameStage.Playoffs, Now.AddDays(2));
        var sheet = ChildSports.Present(
            Feed(Harbor, recorded, later), new HashSet<LeagueId> { Harbor }, [new TeamId("gulls")], [recorded.Id], Now);

        var held = Assert.IsType<HeldLeague>(sheet);
        Assert.DoesNotContain(held.Teams.SelectMany(team => team.Games), line => line.Id == later.Id);
    }

    [Fact]
    public void ALeagueNotAllowedIsNotPresentedEvenWhenHeldOrFollowed()
    {
        var sheet = ChildSports.Present(Feed(Ridge), new HashSet<LeagueId> { Harbor }, [new TeamId("gulls")], [], Now);

        Assert.Null(sheet);
    }

    [Fact]
    public void TheEditorListsEveryLeagueWithItsTickInTableOrder()
    {
        var rows = ChildSports.Rows(SportsSourceTable.Default, new HashSet<LeagueId> { new("nhl") });

        Assert.Equal(SportsSourceTable.Default.Leagues.Count, rows.Count);
        Assert.Equal(["mlb", "nfl", "nhl"], rows.Take(3).Select(row => row.League.Value));
        Assert.Equal([false, false, true], rows.Take(3).Select(row => row.Allowed));
    }

    /// <summary>A document with each of these on the roster as a child and holding live rules: the only people a league tick is read for.</summary>
    private static HouseholdDocument WithChildren(params ViewerId[] children)
    {
        var document = HouseholdDocument.Empty;
        foreach (var child in children)
        {
            document = HouseholdRecords.Put(document, HouseholdMaps.Viewer, new HouseholdViewer(child, true), Next(), PhoneA);
            document = HouseholdRecords.Put(document, HouseholdMaps.ChildRules, new ChildRules(child, AgeBand.SevenToNine), Next(), PhoneA);
        }

        return document;
    }

    [Fact]
    public void AnAllowanceRoundTripsAndRemovalLeavesATombstone()
    {
        var entry = Allow(Pip, Harbor);
        var document = HouseholdRecords.Put(WithChildren(Pip), HouseholdMaps.ChildLeague, entry, Next(), PhoneA);

        var view = HouseholdView.Of(document);
        Assert.Equal([Harbor], view.LeaguesAllowedFor(Pip));
        Assert.Empty(view.LeaguesAllowedFor(Robin));

        document = HouseholdRecords.Remove(document, HouseholdMaps.ChildLeague, entry, Next(), PhoneA);
        Assert.Empty(HouseholdView.Of(document).LeaguesAllowedFor(Pip));
    }

    [Fact]
    public void TwoSpellingsOfOneLeagueAreOneAllowanceAndTwoParentsMerge()
    {
        var document = HouseholdRecords.Put(WithChildren(Pip), HouseholdMaps.ChildLeague, Allow(Pip, Harbor), Next(), PhoneA);
        document = HouseholdRecords.Put(
            document, HouseholdMaps.ChildLeague, new ChildLeagueAllowance(Pip, new LeagueId("HARBOR-LEAGUE"), Robin, Evening), Next(), PhoneB);

        Assert.Single(HouseholdView.Of(document).ChildLeagueAllowances);
    }

    [Fact]
    public void TheHouseholdViewHandsEachChildOnlyTheLeaguesAllowedForThatChild()
    {
        var document = HouseholdRecords.Put(WithChildren(Pip, Wren), HouseholdMaps.ChildLeague, Allow(Pip, Harbor), Next(), PhoneA);

        var rules = HouseholdView.Of(document).ChildRules;

        Assert.Equal([Harbor], rules.Single(child => child.Child == Pip).AllowedLeagues);
        Assert.Empty(rules.Single(child => child.Child == Wren).AllowedLeagues);
    }

    [Fact]
    public void TheKindIsTheFrozenWord()
    {
        Assert.Equal("child-league", HouseholdKinds.ChildLeague);
        Assert.Equal("child-league", HouseholdMaps.ChildLeague.Kind);
    }
}
