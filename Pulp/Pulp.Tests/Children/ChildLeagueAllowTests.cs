using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using Pulp.Domain.Sports;
using static Pulp.Tests.HandOff.HandOffFixtures;

namespace Pulp.Tests.Children;

/// <summary>
/// A parent's league allowance (D317) is the parent's explicit allow for every game in that league (D326): the
/// game has no rating and invents none, so for a child it sits in the allow tier of the filter, below a block and
/// above the unrated rule. The same facts go through the one planner (G8), so Play and the gate cannot disagree.
/// </summary>
public class ChildLeagueAllowTests
{
    private static readonly LeagueId Harbor = new("harbor-league");
    private static readonly LeagueId Ridge = new("ridge-league");
    private static readonly ServiceId Peacock = new("peacock");
    private static readonly GameId Game = new("g-601");

    private static TitleFacts GameFacts(LeagueId? league, params ServiceId[] homes) =>
        new(new TitleId(Game.Value), TitleKind.Show, Pulp.Domain.Ratings.ContentRating.Unrated) { Homes = homes, League = league };

    private static ChildRules WithLeagues(ChildRules rules, params LeagueId[] leagues) =>
        rules with { AllowedLeagues = leagues.ToHashSet() };

    [Fact]
    public void AnUnratedGameInAnAllowedLeagueIsShownAsAllowListed()
    {
        var verdict = ChildFilter.Decide(WithLeagues(WrenRules, Harbor), GameFacts(Harbor, Peacock));

        Assert.True(verdict.Visible);
        Assert.Equal(FilterReason.AllowListed, verdict.Reason);
    }

    [Fact]
    public void AnUnratedGameInALeagueNotAllowedStaysHidden()
    {
        var verdict = ChildFilter.Decide(WithLeagues(WrenRules, Ridge), GameFacts(Harbor, Peacock));

        Assert.False(verdict.Visible);
        Assert.Equal(FilterReason.Unrated, verdict.Reason);
    }

    [Fact]
    public void AGameWithNoLeagueIsNeverHelpedByAnAllowance()
    {
        Assert.Equal(FilterReason.Unrated, ChildFilter.Decide(WithLeagues(WrenRules, Harbor), GameFacts(null, Peacock)).Reason);
    }

    [Fact]
    public void ABlockedServiceStillBeatsTheLeagueAllowance()
    {
        var rules = WithLeagues(WrenRules, Harbor) with { BlockedServices = new HashSet<ServiceId> { Peacock } };

        var verdict = ChildFilter.Decide(rules, GameFacts(Harbor, Peacock));

        Assert.False(verdict.Visible);
        Assert.Equal(FilterReason.Blocked, verdict.Reason);
    }

    [Fact]
    public void ABlockedGameStillBeatsTheLeagueAllowance()
    {
        var rules = WithLeagues(WrenRules, Harbor) with { BlockedTitles = new HashSet<TitleId> { new(Game.Value) } };

        Assert.Equal(FilterReason.Blocked, ChildFilter.Decide(rules, GameFacts(Harbor, Peacock)).Reason);
    }

    [Fact]
    public void AGroupSeesTheGameOnlyIfEveryChildHasTheLeagueAllowed()
    {
        var both = new[] { WithLeagues(WrenRules, Harbor), WithLeagues(MossRules, Harbor) };
        var one = new[] { WithLeagues(WrenRules, Harbor), MossRules };

        Assert.True(ChildFilter.DecideTogether(both, GameFacts(Harbor, Peacock)).Visible);
        var refused = ChildFilter.DecideTogether(one, GameFacts(Harbor, Peacock));
        Assert.False(refused.Visible);
        Assert.Equal(Moss, refused.HiddenFor);
    }

    [Fact]
    public void StartingNeedsAnAttestedOrKidsOnlyServiceAsEver()
    {
        var rules = new[] { WithLeagues(WrenRules, Harbor) };
        var facts = GameFacts(Harbor, Peacock);

        Assert.Equal(
            StartRefusal.ServiceNotAttested,
            ChildFilter.CanStart(rules, facts, Peacock, ServiceCatalogue.Public, new AttestedServices([])).Refusal);
        Assert.True(ChildFilter.CanStart(rules, facts, Peacock, ServiceCatalogue.Public, new AttestedServices([Peacock])).MayStart);
    }

    private static HandOffPlanner PlannerWith(ChildRules wren, ChildRules moss, params ServiceId[] attested) =>
        new(Roster, [wren, moss], ServiceCatalogue.Public, new AttestedServices(attested));

    private static HandOffRequest RequestFor(LeagueId? league, params ViewerId[] watching)
    {
        var service = ServiceCatalogue.Public.ById(Peacock)!;
        var shape = GamePlay.Request(watching[0], Game, service, BroadcasterTable.HomeLink(service)!, "Foxes at Gulls", league);
        return new HandOffRequest(watching, shape.Title, shape.Link, shape.SessionNames);
    }

    [Fact]
    public void ThePlannerLetsAChildStartAnAllowedLeaguesGameOnAnAttestedService()
    {
        var planner = PlannerWith(WithLeagues(WrenRules, Harbor), MossRules, Peacock);

        var plan = planner.Plan(RequestFor(Harbor, Wren), At);

        Assert.NotNull(plan.Intent);
        Assert.Equal(Peacock, plan.Pending!.Service);
    }

    [Fact]
    public void ThePlannerRefusesAnUnattestedServiceOrAnUnallowedLeagueOrAGroupWithOneUnallowedChild()
    {
        var allowedWren = WithLeagues(WrenRules, Harbor);

        Assert.Null(PlannerWith(allowedWren, MossRules).Plan(RequestFor(Harbor, Wren), At).Intent);
        Assert.Null(PlannerWith(allowedWren, MossRules, Peacock).Plan(RequestFor(Ridge, Wren), At).Intent);
        Assert.Null(PlannerWith(allowedWren, MossRules, Peacock).Plan(RequestFor(null, Wren), At).Intent);
        Assert.Null(PlannerWith(allowedWren, MossRules, Peacock).Plan(RequestFor(Harbor, Wren, Moss), At).Intent);
        Assert.NotNull(PlannerWith(allowedWren, WithLeagues(MossRules, Harbor), Peacock).Plan(RequestFor(Harbor, Wren, Moss), At).Intent);
    }

    [Fact]
    public void AllowedLeaguesAreDerivedFromAllowancesAndNeverStoredOnTheChildRulesRecord()
    {
        var map = Pulp.Domain.Household.Records.HouseholdMaps.ChildRules;
        var fields = map.FieldsOf(WithLeagues(WrenRules, Harbor));

        Assert.DoesNotContain(fields.Keys, key => key.Contains("league", StringComparison.OrdinalIgnoreCase));
    }
}
