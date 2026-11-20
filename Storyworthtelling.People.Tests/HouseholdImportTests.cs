using Storyworthtelling.Household;

namespace Storyworthtelling.People.Tests;

/// <summary>
/// An app's own people join Foreword's household once, on the owner's phone: matched by name and linked, or added with the app's id kept,
/// so the app's records about them keep pointing at the right person.
/// </summary>
public class HouseholdImportTests
{
    private readonly HouseholdPeopleEditor owner = new(canEdit: true);

    private PeopleEdit Import(HouseholdDocument document, string app, params ImportedPerson[] people) =>
        owner.Import(document, app, people, Household.Next, Household.Writer);

    [Fact]
    public void ASameNamedPersonIsLinkedNotDuplicated()
    {
        var edit = Import(Household.WithFour(), PeopleApps.Pulp, new ImportedPerson("viewer-7", "sam", false, null, true, null));

        var people = HouseholdPeople.From(edit.Document);
        Assert.Equal(4, people.People.Count);
        Assert.Equal("sam", people.FindByLink(PeopleApps.Pulp, "viewer-7")!.Id);
    }

    [Fact]
    public void ANewPersonKeepsTheAppsId()
    {
        var edit = Import(HouseholdDocument.Empty, PeopleApps.Constitution,
            new ImportedPerson("p-41", "Sam", false, null, true, "sam@example.com"),
            new ImportedPerson("p-42", "Robin", true, new DateOnly(2016, 4, 9), false, null));

        var people = HouseholdPeople.From(edit.Document);
        Assert.Equal(["p-41", "p-42"], people.People.Select(person => person.Id).Order());
        Assert.Equal("p-42", people.FindByLink(PeopleApps.Constitution, "p-42")!.Id);
        Assert.Equal(new DateOnly(2016, 4, 9), people.Find("p-42")!.Birthdate);
        Assert.True(people.Find("p-41")!.OnPickup);
    }

    [Fact]
    public void AnIdAlreadyTakenGetsTheAppsName()
    {
        var edit = Import(Household.WithFour(), PeopleApps.Pulp, new ImportedPerson("sam", "Alex", false, null, false, null));

        Assert.Equal("Alex", HouseholdPeople.From(edit.Document).FindByLink(PeopleApps.Pulp, "sam")!.Name);
        Assert.Equal("sam-pulp", HouseholdPeople.From(edit.Document).FindByLink(PeopleApps.Pulp, "sam")!.Id);
    }

    [Fact]
    public void ImportFillsBlanksButNeverOverwritesForeword()
    {
        var edit = Import(Household.WithFour(), PeopleApps.Constitution,
            new ImportedPerson("c-1", "Juniper", true, new DateOnly(2019, 1, 2), false, null),
            new ImportedPerson("c-2", "Sam", false, null, false, "other@example.com"));

        var people = HouseholdPeople.From(edit.Document);
        Assert.Equal(new DateOnly(2019, 1, 2), people.Find("juniper")!.Birthdate);
        Assert.True(people.Find("sam")!.OnPickup);
        Assert.Equal("sam@example.com", people.Find("sam")!.GoogleAddress);
    }

    [Fact]
    public void ImportingTwiceChangesNothing()
    {
        var first = Import(HouseholdDocument.Empty, PeopleApps.Pulp, new ImportedPerson("viewer-1", "Lee", false, null, false, null));

        var second = Import(first.Document, PeopleApps.Pulp, new ImportedPerson("viewer-1", "Lee (renamed in Pulp)", false, null, false, null));

        Assert.Same(first.Document, second.Document);
    }

    [Fact]
    public void ThePartnersPhoneImportsNothing()
    {
        var partner = new HouseholdPeopleEditor(canEdit: false);

        var edit = partner.Import(HouseholdDocument.Empty, PeopleApps.Pulp, [new ImportedPerson("viewer-1", "Lee", false, null, false, null)], Household.Next, "phone-partner");

        Assert.False(edit.Accepted);
        Assert.Empty(HouseholdPeople.From(edit.Document).People);
    }

    [Fact]
    public void TheRhythmFillsOnlyWhatIsUnset()
    {
        var document = owner.SetRhythm(HouseholdDocument.Empty, new HouseholdRhythm { Bedtime = new TimeOnly(20, 0) }, Household.Next(), Household.Writer).Document;

        document = owner.FillRhythm(document, new HouseholdRhythm { Bedtime = new TimeOnly(19, 0), DinnerStart = new TimeOnly(18, 0) }, Household.Next(), Household.Writer).Document;

        var rhythm = HouseholdPeople.From(document).Rhythm;
        Assert.Equal(new TimeOnly(20, 0), rhythm.Bedtime);
        Assert.Equal(new TimeOnly(18, 0), rhythm.DinnerStart);
    }
}
