using Pulp.Domain.People;
using Pulp.Domain.FirstRun;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// The household as it is being set up at first run (D142): people added by name with a child
/// flag, refused with one plain sentence when the name will not do, and not saved until at least
/// one adult is on it — a phone belongs to an adult (D97), so a roster of children only would leave
/// "This phone is …" with nobody to choose. Invented names.
/// </summary>
public class HouseholdDraftTests
{
    [Fact]
    public void AnAddedPersonGetsAnIdFromTheirName()
    {
        var edit = HouseholdDraft.Empty.Add("Rowan", isChild: false);

        Assert.Null(edit.Problem);
        var person = Assert.Single(edit.Draft.People);
        Assert.Equal("rowan", person.Id.Value);
        Assert.False(person.IsChild);
    }

    [Fact]
    public void AChildKeepsTheirFlag()
    {
        var draft = HouseholdDraft.Empty.Add("Rowan", false).Draft.Add("Pip", isChild: true).Draft;

        Assert.True(draft.People.Single(person => person.Id.Value == "pip").IsChild);
    }

    [Fact]
    public void ARefusedNameLeavesTheDraftAsItWas()
    {
        var draft = HouseholdDraft.Empty.Add("Rowan", false).Draft;

        var edit = draft.Add("ROWAN", isChild: true);

        Assert.Equal(ViewerNaming.AlreadyThere("Rowan"), edit.Problem);
        Assert.Same(draft, edit.Draft);
    }

    [Fact]
    public void APersonCanBeTakenOffAgain()
    {
        var draft = HouseholdDraft.Empty.Add("Rowan", false).Draft.Add("Pip", true).Draft;

        var removed = draft.Remove(draft.People[1].Id);

        Assert.Equal(["rowan"], removed.People.Select(person => person.Id.Value));
    }

    [Fact]
    public void AnEmptyHouseholdIsNotSaved() =>
        Assert.Equal(HouseholdDraft.NobodyYet, HouseholdDraft.Empty.SaveProblem);

    [Fact]
    public void ChildrenAloneAreNotSaved() =>
        Assert.Equal(HouseholdDraft.NeedsAnAdult, HouseholdDraft.Empty.Add("Pip", isChild: true).Draft.SaveProblem);

    [Fact]
    public void AnAdultIsEnoughToSave() =>
        Assert.Null(HouseholdDraft.Empty.Add("Rowan", false).Draft.SaveProblem);
}
