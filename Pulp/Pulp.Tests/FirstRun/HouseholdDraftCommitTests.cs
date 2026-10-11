using Pulp.Domain.Core;
using Pulp.Domain.FirstRun;
using Pulp.Domain.People;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// Turning the first-run draft into household records at save time (D142). The draft's ids were minted
/// from the people typed so far; if Drive adopted a roster while the owner typed, those ids could clash
/// with people already there, and writing one would merge a new person into an old one's records. So the
/// ids are minted again against the roster as it stands when saving, a person already there by name is
/// not added twice, and every person added gets a name record so the screens show a name, not an id.
/// Invented names.
/// </summary>
public class HouseholdDraftCommitTests
{
    private static HouseholdDraft Draft(params (string Name, bool Child)[] people) =>
        people.Aggregate(HouseholdDraft.Empty, (draft, person) => draft.Add(person.Name, person.Child).Draft);

    [Fact]
    public void EachPersonIsWrittenWithTheirNameAsTyped()
    {
        var commit = Draft(("Rowan Vale", false), ("Pip", true)).Commit([]);

        Assert.Equal(["rowan-vale", "pip"], commit.Viewers.Select(viewer => viewer.Id.Value));
        Assert.Equal(["Rowan Vale", "Pip"], commit.Names.Select(name => name.Name));
        Assert.Equal(commit.Viewers.Select(viewer => viewer.Id), commit.Names.Select(name => name.Viewer));
        Assert.True(commit.Viewers[1].IsChild);
    }

    /// <summary>The bug: first run checked only the people typed so far, so an adopted "rowan" was written over.</summary>
    [Fact]
    public void AnIdTakenByAnAdoptedPersonIsMintedAgain()
    {
        var commit = Draft(("Pip", true)).Commit([new ViewerId("pip")], new Dictionary<ViewerId, string> { [new ViewerId("pip")] = "Pippin" });

        Assert.Equal("pip-2", Assert.Single(commit.Viewers).Id.Value);
    }

    [Fact]
    public void ThePeopleInOneDraftNeverShareAnIdEvenWhenTheRosterIsEmpty()
    {
        var commit = Draft(("Rowan", false), ("Ada", false)).Commit([]);

        Assert.Equal(2, commit.Viewers.Select(viewer => viewer.Id).Distinct().Count());
    }

    [Fact]
    public void ASomeoneAlreadyThereByNameIsNotAddedTwice()
    {
        var commit = Draft(("Rowan", false), ("Pip", true)).Commit([new ViewerId("rowan")]);

        Assert.Equal(["pip"], commit.Viewers.Select(viewer => viewer.Id.Value));
        Assert.Equal(["Rowan"], commit.AlreadyThere);
    }

    [Fact]
    public void ANameRecordedForAnAdoptedPersonCountsAsTheirName()
    {
        var commit = Draft(("Robbie", false)).Commit([new ViewerId("robin")], namesNow: new Dictionary<ViewerId, string> { [new ViewerId("robin")] = "Robbie" });

        Assert.Empty(commit.Viewers);
        Assert.Equal(["Robbie"], commit.AlreadyThere);
    }

    [Fact]
    public void TheDraftSurvivesBeingSavedAndRestored()
    {
        var draft = Draft(("Rowan Vale", false), ("Pip", true));

        var back = HouseholdDraftCodec.Decode(HouseholdDraftCodec.Encode(draft));

        Assert.Equal(draft.People, back.People);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbled")]
    public void AnythingUnreadableIsAnEmptyDraft(string? text) =>
        Assert.Empty(HouseholdDraftCodec.Decode(text).People);

    [Fact]
    public void TheSentenceForSomeoneAlreadyThereNamesThem()
    {
        Assert.Equal("Rowan is already in the household.", FirstRunText.AlreadyThere(["Rowan"]));
        Assert.Equal("Rowan and Pip are already in the household.", FirstRunText.AlreadyThere(["Rowan", "Pip"]));
        Assert.Equal("Rowan, Pip and Ada are already in the household.", FirstRunText.AlreadyThere(["Rowan", "Pip", "Ada"]));
    }
}
