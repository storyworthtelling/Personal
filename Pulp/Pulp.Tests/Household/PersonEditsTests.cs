using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using Pulp.Domain.Services;
using Pulp.Domain.Sports;
using Pulp.Domain.Settings;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Household;

/// <summary>
/// A person is three records — the viewer, their name and (for a child) their rules — written one at a
/// time, and a sync pass can land between any two. The receiving phone builds its hand-off planner from
/// whatever it then holds, and that refuses a child with no rules and rules for someone who is not a
/// child. These tests enumerate every state a write order passes through and ask the planner itself
/// whether it would be built, rather than restating its rule.
/// </summary>
public class PersonEditsTests
{
    private static readonly HouseholdViewer AveryAdult = new(Avery, false);
    private static readonly HouseholdViewer PipChild = new(Pip, true);
    private static readonly ChildRules PipRules = ChildRuleEdits.NewChild(Pip);

    private static HouseholdDocument Start(params (HouseholdViewer Viewer, ChildRules? Rules)[] people)
    {
        var document = HouseholdDocument.Empty;
        foreach (var (viewer, rules) in people)
        {
            document = HouseholdRecords.Put(document, HouseholdMaps.Viewer, viewer, Next(), PhoneA);
            document = HouseholdRecords.Put(document, HouseholdMaps.ViewerName, new ViewerNameEntry(viewer.Id, viewer.Id.Value), Next(), PhoneA);
            if (rules is not null)
            {
                document = HouseholdRecords.Put(document, HouseholdMaps.ChildRules, rules, Next(), PhoneA);
            }
        }

        return document;
    }

    /// <summary>True when the receiving phone could build its planner from the document.</summary>
    private static bool PlannerBuilds(HouseholdDocument document)
    {
        var view = HouseholdView.Of(document);
        if (view.Roster is null)
        {
            return true; // no roster: the head builds no planner at all
        }

        try
        {
            _ = new HandOffPlanner(view.Roster, view.ChildRules, ServiceCatalogue.Public, new AttestedServices([]));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IReadOnlyList<HouseholdDocument> StatesAfterEachStep(HouseholdDocument start, PersonEdit edit)
    {
        var states = new List<HouseholdDocument>();
        var document = start;
        foreach (var step in edit.Steps)
        {
            document = PersonEdits.Apply(document, step, Next(), PhoneA);
            states.Add(document);
        }

        return states;
    }

    [Fact]
    public void EveryEditStartsFromAndEndsAtAStateThePlannerAccepts()
    {
        var adultOnly = Start((AveryAdult, null));
        var withChild = Start((AveryAdult, null), (PipChild, PipRules));
        var cases = new (HouseholdDocument Start, PersonEdit Edit)[]
        {
            (adultOnly, PersonEdits.Add(PipChild, new ViewerNameEntry(Pip, "Pip"), PipRules)),
            (adultOnly, PersonEdits.Add(new HouseholdViewer(Robin, false), new ViewerNameEntry(Robin, "Robin"), null)),
            (withChild, PersonEdits.Remove(PipChild, new ViewerNameEntry(Pip, Pip.Value), PipRules)),
            (withChild, PersonEdits.MakeAdult(PipChild, PipRules)),
            (adultOnly, PersonEdits.MakeChild(AveryAdult, ChildRuleEdits.NewChild(Avery))),
        };

        foreach (var (start, edit) in cases)
        {
            Assert.True(PlannerBuilds(start));
            Assert.True(PlannerBuilds(PersonEdits.ApplyAll(start, edit, Next(), PhoneA)));
        }
    }

    [Fact]
    public void AddingOrRemovingAnAdultNeverPassesThroughAStateThePlannerRefuses()
    {
        var adultOnly = Start((AveryAdult, null));
        var robin = new HouseholdViewer(Robin, false);
        var added = PersonEdits.Add(robin, new ViewerNameEntry(Robin, "Robin"), null);
        var removed = PersonEdits.Remove(robin, new ViewerNameEntry(Robin, "Robin"), null);

        var states = StatesAfterEachStep(adultOnly, added).ToList();
        states.AddRange(StatesAfterEachStep(PersonEdits.ApplyAll(adultOnly, added, Next(), PhoneA), removed));

        Assert.NotEmpty(states);
        Assert.All(states, state => Assert.True(PlannerBuilds(state)));
    }

    [Fact]
    public void ARemovalDropsTheRulesBeforeTheViewerAndTheViewerLast()
    {
        var edit = PersonEdits.Remove(PipChild, new ViewerNameEntry(Pip, "Pip"), PipRules);

        Assert.Equal(PersonRecord.Rules, edit.Steps[0].Record);
        Assert.Equal(PersonRecord.Viewer, edit.Steps[^1].Record);
        Assert.All(edit.Steps, step => Assert.Equal(PersonWriteKind.Remove, step.Kind));
    }

    [Fact]
    public void AnAdditionWritesTheViewerBeforeTheRules()
    {
        var edit = PersonEdits.Add(PipChild, new ViewerNameEntry(Pip, "Pip"), PipRules);

        Assert.Equal(PersonRecord.Viewer, edit.Steps[0].Record);
        Assert.Equal(PersonRecord.Rules, edit.Steps[^1].Record);
    }

    [Fact]
    public void ChangingAChildsFlagWritesTheOrderThatNeverLeavesRulesForAnAdultLast()
    {
        var toAdult = PersonEdits.MakeAdult(PipChild, PipRules);
        var toChild = PersonEdits.MakeChild(AveryAdult, ChildRuleEdits.NewChild(Avery));

        Assert.Equal([PersonRecord.Rules, PersonRecord.Viewer], toAdult.Steps.Select(step => step.Record));
        Assert.Equal([PersonRecord.Viewer, PersonRecord.Rules], toChild.Steps.Select(step => step.Record));
    }

    /// <summary>
    /// The reason a child's change is written as ONE edit where the head can: with the viewer and the
    /// rules as two writes, no order leaves out the state the planner refuses. Both orders are checked,
    /// so a future change of order cannot claim to have fixed it.
    /// </summary>
    [Fact]
    public void NoOrderOfTwoSeparateWritesAvoidsARefusedStateForAChildsChange()
    {
        var withChild = Start((AveryAdult, null), (PipChild, PipRules));
        var viewerFirst = PersonEdits.Apply(withChild, new PersonStep(PersonWriteKind.Put, PersonRecord.Viewer, PipChild with { IsChild = false }, null, null), Next(), PhoneA);
        var rulesFirst = PersonEdits.Apply(withChild, new PersonStep(PersonWriteKind.Remove, PersonRecord.Rules, null, null, PipRules), Next(), PhoneA);

        Assert.False(PlannerBuilds(viewerFirst));
        Assert.False(PlannerBuilds(rulesFirst));
    }

    [Fact]
    public void TheSingleEditThatAppliesAllStepsAtOnceNeverExposesAnIntermediateState()
    {
        var withChild = Start((AveryAdult, null), (PipChild, PipRules));
        var edit = PersonEdits.MakeAdult(PipChild, PipRules);

        var whole = PersonEdits.ApplyAll(withChild, edit, Next(), PhoneA);

        Assert.True(PlannerBuilds(whole));
        Assert.Empty(HouseholdView.Of(whole).ChildRules);
        Assert.False(HouseholdView.Of(whole).Roster!.IsChild(Pip));
    }

    private static readonly ChildLeagueAllowance PipHarbor = new(Pip, new LeagueId("harbor-league"), Avery, DateTimeOffset.UnixEpoch);
    private static readonly ChildLeagueAllowance PipRiver = new(Pip, new LeagueId("river-league"), Avery, DateTimeOffset.UnixEpoch);

    private static HouseholdDocument StartWithLeagues()
    {
        var document = Start((AveryAdult, null), (PipChild, PipRules));
        document = HouseholdRecords.Put(document, HouseholdMaps.ChildLeague, PipHarbor, Next(), PhoneA);
        return HouseholdRecords.Put(document, HouseholdMaps.ChildLeague, PipRiver, Next(), PhoneA);
    }

    [Fact]
    public void ARemovedChildsLeaguesDoNotReturnToANewChildWithTheSameId()
    {
        var removed = PersonEdits.ApplyAll(
            StartWithLeagues(), PersonEdits.Remove(PipChild, new ViewerNameEntry(Pip, "Pip"), PipRules, [PipHarbor, PipRiver]), Next(), PhoneA);
        var readded = PersonEdits.ApplyAll(
            removed, PersonEdits.Add(PipChild, new ViewerNameEntry(Pip, "Pip"), ChildRuleEdits.NewChild(Pip)), Next(), PhoneA);

        var view = HouseholdView.Of(readded);

        Assert.Empty(view.ChildLeagueAllowances);
        Assert.Empty(Assert.Single(view.ChildRules).AllowedLeagues);
    }

    [Fact]
    public void AChildMadeAdultAndThenChildAgainDoesNotKeepTheLeagues()
    {
        var adult = PersonEdits.ApplyAll(StartWithLeagues(), PersonEdits.MakeAdult(PipChild, PipRules, [PipHarbor, PipRiver]), Next(), PhoneA);
        var again = PersonEdits.ApplyAll(adult, PersonEdits.MakeChild(new HouseholdViewer(Pip, false), ChildRuleEdits.NewChild(Pip)), Next(), PhoneA);

        Assert.Empty(HouseholdView.Of(adult).ChildLeagueAllowances);
        Assert.Empty(HouseholdView.Of(again).ChildLeagueAllowances);
        Assert.True(PlannerBuilds(adult));
        Assert.True(PlannerBuilds(again));
    }

    [Fact]
    public void UndoingARemovalOrAnAdultChangePutsTheLeaguesBackWithThePerson()
    {
        var start = StartWithLeagues();
        var removed = PersonEdits.ApplyAll(start, PersonEdits.Remove(PipChild, new ViewerNameEntry(Pip, "Pip"), PipRules, [PipHarbor, PipRiver]), Next(), PhoneA);
        var undone = PersonEdits.ApplyAll(removed, PersonEdits.Add(PipChild, new ViewerNameEntry(Pip, "Pip"), PipRules, [PipHarbor, PipRiver]), Next(), PhoneA);
        var adult = PersonEdits.ApplyAll(start, PersonEdits.MakeAdult(PipChild, PipRules, [PipHarbor, PipRiver]), Next(), PhoneA);
        var back = PersonEdits.ApplyAll(adult, PersonEdits.MakeChild(PipChild, PipRules, [PipHarbor, PipRiver]), Next(), PhoneA);

        foreach (var restored in new[] { undone, back })
        {
            var leagues = HouseholdView.Of(restored).LeaguesAllowedFor(Pip);
            Assert.Equal(2, leagues.Count);
            Assert.Contains(PipHarbor.League, leagues);
            Assert.True(PlannerBuilds(restored));
        }
    }

    [Fact]
    public void ALeagueRecordOfSomeoneNotCurrentlyAChildIsNotReadAsAllowed()
    {
        // A record left by an older build: the child is gone but the allowance was never tombstoned.
        var document = StartWithLeagues();
        document = PersonEdits.ApplyAll(document, PersonEdits.Remove(PipChild, new ViewerNameEntry(Pip, "Pip"), PipRules), Next(), PhoneA);
        document = PersonEdits.ApplyAll(document, PersonEdits.Add(PipChild, new ViewerNameEntry(Pip, "Pip"), ChildRuleEdits.NewChild(Pip)), Next(), PhoneA);
        var viewerOnly = HouseholdRecords.Put(
            HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Viewer, AveryAdult, Next(), PhoneA),
            HouseholdMaps.ChildLeague, new ChildLeagueAllowance(Avery, new LeagueId("harbor-league"), Avery, DateTimeOffset.UnixEpoch), Next(), PhoneA);

        Assert.Empty(HouseholdView.Of(viewerOnly).ChildLeagueAllowances);
        Assert.Empty(HouseholdView.Of(viewerOnly).LeaguesAllowedFor(Avery));
    }

    [Fact]
    public void AnAdultsGoogleAddressIsReadOnlyWhileTheyAreCurrentlyAnAdult()
    {
        var document = HouseholdRecords.Put(Start((AveryAdult, null), (PipChild, PipRules)), HouseholdMaps.AdultAccount, new AdultAccountEntry(Pip, "pip@example.com"), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.AdultAccount, new AdultAccountEntry(Avery, "avery@example.com"), Next(), PhoneA);

        Assert.Equal(["avery@example.com"], HouseholdView.Of(document).AdultGoogleAccounts);
    }

    [Fact]
    public void RenamingWritesOnlyTheName()
    {
        var edit = PersonEdits.Rename(Pip, "Pippin");

        var step = Assert.Single(edit.Steps);
        Assert.Equal(PersonRecord.Name, step.Record);
    }
}
