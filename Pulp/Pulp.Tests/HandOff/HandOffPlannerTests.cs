using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;
using Pulp.Domain.Services;
using static Pulp.Tests.HandOff.HandOffFixtures;

namespace Pulp.Tests.HandOff;

/// <summary>
/// G13 at the door the screens use. <see cref="HandOffPlanner"/> is what the Android executor asks,
/// and the executor builds its <c>Intent</c> from the plan's intent alone; so if a child's request to
/// an unattested service comes back with no intent here, nothing on the phone can send one. Adults go
/// straight through; any child — alone, with an adult, or with other children — goes through
/// <see cref="ChildHandOff"/>, whoever else is on the sofa.
/// </summary>
public class HandOffPlannerTests
{
    [Fact]
    public void AnAdultIsHandedOffWithTheServicesOwnPackage()
    {
        var plan = Planner().Plan(Request(Sample, Sam), At);

        Assert.Equal(HandOffAudience.Adult, plan.Audience);
        Assert.NotNull(plan.Intent);
        Assert.Equal("com.example.sample", plan.Intent.Package);
        Assert.Equal(StartRefusal.None, plan.Verdict.Refusal);
    }

    [Fact]
    public void AChildToAnUnattestedServiceGetsTheRefusalAndNoIntent()
    {
        var plan = Planner(Flix.Id).Plan(Request(Sample, Wren), At);

        Assert.Equal(HandOffAudience.Child, plan.Audience);
        Assert.Equal(StartRefusal.ServiceNotAttested, plan.Verdict.Refusal);
        Assert.Null(plan.Intent);
        Assert.Null(plan.Pending);
    }

    /// <summary>The planner's whole G13 sweep: every public service, unattested, every viewer mix with a child.</summary>
    [Fact]
    public void NoChildMixReachesAnUnattestedPublicServiceThroughThePlanner()
    {
        var services = ServiceCatalogue.Public.All.Where(service => !service.KidsOnly).ToList();
        Assert.NotEmpty(services);
        var planner = new Pulp.Domain.HandOffs.HandOffPlanner(Roster, [WrenRules, MossRules], ServiceCatalogue.Public, new AttestedServices([]));
        ViewerId[][] mixes = [[Wren], [Sam, Wren], [Wren, Moss], [Sam, Wren, Moss]];
        var checkedCount = 0;

        foreach (var service in services)
        foreach (var mix in mixes)
        {
            var facts = new TitleFacts(Lantern, TitleKind.Show, ContentRating.Parse("TV-Y")) { Homes = [service.Id] };
            var link = new HandOffLink(Lantern, service.Id, new Uri("https://watch.example.com/title/100"), null);

            var plan = planner.Plan(new HandOffRequest(mix, facts, link, []), At);

            Assert.Null(plan.Intent);
            Assert.Null(plan.Pending);
            Assert.Equal(StartRefusal.ServiceNotAttested, plan.Verdict.Refusal);
            checkedCount++;
        }

        Assert.Equal(services.Count * mixes.Length, checkedCount);
    }

    [Fact]
    public void AnAdultOnTheSofaDoesNotLiftTheChildGate()
    {
        var plan = Planner(Flix.Id).Plan(Request(Sample, Sam, Wren), At);

        Assert.Equal(HandOffAudience.Child, plan.Audience);
        Assert.Null(plan.Intent);
    }

    [Fact]
    public void ChildrenTogetherAreJudgedByTheStrictest()
    {
        var older = new TitleFacts(Lantern, TitleKind.Film, ContentRating.Parse("PG-13")) { Homes = [Flix.Id] };
        var request = new HandOffRequest([Moss, Wren], older, Link(Flix), []);

        var plan = Planner(Flix.Id).Plan(request, At);

        Assert.Equal(HandOffAudience.ChildrenTogether, plan.Audience);
        Assert.Equal(StartRefusal.Hidden, plan.Verdict.Refusal);
        Assert.Equal(Wren, plan.Verdict.RefusedFor);
        Assert.Null(plan.Intent);
    }

    [Fact]
    public void AChildToAnAttestedServiceIsHandedOff()
    {
        var plan = Planner(Flix.Id).Plan(Request(Flix, Wren), At);

        Assert.NotNull(plan.Intent);
        Assert.Equal("com.example.flix", plan.Intent.Package);
    }

    [Fact]
    public void AStartedPlanCarriesAPendingHandedOffEntry()
    {
        var plan = Planner().Plan(Request(Sample, Sam), At);

        Assert.NotNull(plan.Pending);
        Assert.Equal(At, plan.Pending.Evidence.At);
        Assert.Equal(Lantern, plan.Pending.Title);
        Assert.Equal(Sample.Id, plan.Pending.Service);
        Assert.Equal("com.example.sample", plan.Pending.Package);
        Assert.Equal([Sam], plan.Pending.Watching);
        Assert.Equal(["Lantern Cove"], plan.Pending.SessionNames);
    }

    [Fact]
    public void AnAdultToAServiceTheCatalogueDoesNotKnowIsRefused()
    {
        var stray = new ServiceId("stray-example");
        var link = new HandOffLink(Lantern, stray, new Uri("https://watch.example.com/title/100"), null);

        var plan = Planner().Plan(new HandOffRequest([Sam], Facts(Flix), link, []), At);

        Assert.Equal(StartRefusal.UnknownService, plan.Verdict.Refusal);
        Assert.Null(plan.Intent);
        Assert.Null(plan.Pending);
    }

    [Fact]
    public void AViewerNotOnTheRosterIsRefusedNotTakenForAnAdult() =>
        Assert.Throws<ArgumentException>(() =>
            Planner().Plan(Request(Sample, new ViewerId("guest-example")), At));

    [Fact]
    public void ALinkForAnotherTitleIsAProgrammingError() =>
        Assert.Throws<ArgumentException>(() => Planner().Plan(
            new HandOffRequest(
                [Sam],
                Facts(Flix),
                new HandOffLink(new TitleId("title-other"), Flix.Id, new Uri("https://watch.example.com/title/2"), null),
                []),
            At));

    [Fact]
    public void NobodyWatchingIsRefused() =>
        Assert.Throws<ArgumentException>(() => new HandOffRequest([], Facts(Flix), Link(Flix), []));

    [Fact]
    public void AChildOnTheRosterWithNoRulesIsRefusedAtConstruction() =>
        Assert.Throws<ArgumentException>(() =>
            new Pulp.Domain.HandOffs.HandOffPlanner(Roster, [WrenRules], TestCatalogue, new AttestedServices([])));

    [Fact]
    public void RulesForAnAdultAreRefusedAtConstruction() =>
        Assert.Throws<ArgumentException>(() => new Pulp.Domain.HandOffs.HandOffPlanner(
            Roster, [WrenRules, MossRules, new ChildRules(Sam, AgeBand.SevenToNine)], TestCatalogue, new AttestedServices([])));
}
