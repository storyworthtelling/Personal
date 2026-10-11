using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.FirstRun;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// First run wrote a child's viewer and name but not their rules, so the child had no rules record and the
/// hand-off planner refused to be built for the whole household ("No child rules for 'pip'"): Play went
/// dark for everyone and the child's view said the filter was not set. A child is never on the roster
/// without a rules record, whatever the draft held. Invented names.
/// </summary>
public class DraftWriteTests
{
    private static readonly HlcTimestamp Stamp = new(1000, 0);

    private static HouseholdDraft Draft(params (string Name, bool Child)[] people) =>
        people.Aggregate(HouseholdDraft.Empty, (draft, person) => draft.Add(person.Name, person.Child).Draft);

    [Fact]
    public void EachChildCommittedCarriesTheStrictestBandAndNoAdultDoes()
    {
        var commit = Draft(("Rowan", false), ("Pip", true), ("Wren", true)).Commit([]);

        Assert.Equal(["pip", "wren"], commit.ChildRules.Select(rules => rules.Child.Value));
        Assert.All(commit.ChildRules, rules => Assert.Equal(AgeBand.UnderSeven, rules.Ceiling));
    }

    [Fact]
    public void AChildAlreadyThereGetsNoSecondRulesRecord()
    {
        var commit = Draft(("Pip", true)).Commit([new ViewerId("pip")]);

        Assert.Empty(commit.ChildRules);
    }

    /// <summary>Over many drafts and rosters: every child on the resulting roster has a rules record.</summary>
    [Fact]
    public void ForAnyCommittedDraftEveryChildOnTheRosterHasRules()
    {
        var random = new Random(12);
        var pool = new[] { "Rowan", "Pip", "Wren", "Ada", "Moss", "Juniper", "Sage", "Fern" };
        var checkedDrafts = 0;
        for (var round = 0; round < 200; round++)
        {
            var draft = HouseholdDraft.Empty;
            foreach (var name in pool.OrderBy(_ => random.Next()).Take(random.Next(1, pool.Length)))
            {
                draft = draft.Add(name, random.Next(2) == 0).Draft;
            }

            if (draft.SaveProblem is not null)
            {
                continue;
            }

            var written = HouseholdDraftWrite.Apply(HouseholdDocument.Empty, draft, Stamp, "install-a");
            var view = HouseholdView.Of(written.Document);
            var children = view.Roster!.Viewers.Where(viewer => viewer.IsChild).Select(viewer => viewer.Id).ToList();

            Assert.All(children, child => Assert.Contains(view.ChildRules, rules => rules.Child == child));
            checkedDrafts++;
        }

        Assert.True(checkedDrafts > 50, "The property ran over too few drafts to mean anything.");
    }

    [Fact]
    public void ApplyingAfterAnAdoptedRosterKeepsTheirRulesAndAddsOnlyTheNewChildren()
    {
        var first = HouseholdDraftWrite.Apply(HouseholdDocument.Empty, Draft(("Rowan", false), ("Pip", true)), Stamp, "install-a").Document;
        var second = HouseholdDraftWrite.Apply(first, Draft(("Pip", true), ("Wren", true)), new HlcTimestamp(2000, 0), "install-a");

        var view = HouseholdView.Of(second.Document);
        Assert.Equal(["Pip"], second.Commit.AlreadyThere);
        Assert.Equal(["pip", "wren"], view.ChildRules.Select(rules => rules.Child.Value).Order());
    }

    [Fact]
    public void TheAddedViewersAndNamesAreWrittenToo()
    {
        var written = HouseholdDraftWrite.Apply(HouseholdDocument.Empty, Draft(("Rowan Vale", false), ("Pip", true)), Stamp, "install-a");
        var view = HouseholdView.Of(written.Document);

        Assert.Equal(["pip", "rowan-vale"], view.Roster!.Viewers.Select(viewer => viewer.Id.Value).Order());
        Assert.Equal("Rowan Vale", view.NameOf(new ViewerId("rowan-vale")));
    }
}
