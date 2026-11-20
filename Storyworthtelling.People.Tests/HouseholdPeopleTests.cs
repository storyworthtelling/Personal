using Storyworthtelling.Household;

namespace Storyworthtelling.People.Tests;

public class HouseholdPeopleTests
{
    private readonly HouseholdPeopleEditor owner = new(canEdit: true);

    [Fact]
    public void APersonRoundTripsThroughTheDocument()
    {
        var document = owner.Put(HouseholdDocument.Empty, Household.Robin(), Household.Next(), Household.Writer).Document;

        var robin = Assert.Single(HouseholdPeople.From(document).People);
        Assert.Equal(Household.Robin(), robin);
    }

    [Fact]
    public void TheRosterSplitsAdultsChildrenAndPickup()
    {
        var people = HouseholdPeople.From(Household.WithFour());

        Assert.Equal(["Lee", "Sam"], people.Adults.Select(person => person.Name).Order());
        Assert.Equal(["Juniper", "Robin"], people.Children.Select(person => person.Name).Order());
        Assert.Equal(["Sam"], people.PickupAdults.Select(person => person.Name));
        Assert.Equal(["robin-cal@example.com"], people.ChildrenCalendarIds);
    }

    [Fact]
    public void ANameIsFoundWithoutCase()
    {
        Assert.Equal("lee", HouseholdPeople.From(Household.WithFour()).FindByName(" LEE ")!.Id);
    }

    [Fact]
    public void AChildIsNeverOnPickup()
    {
        Assert.Throws<ArgumentException>(() => Household.Robin() with { OnPickup = true }).ToString();
        Assert.Throws<ArgumentException>(() => owner.Put(HouseholdDocument.Empty, Household.Robin() with { OnPickup = true }, Household.Next(), Household.Writer));
    }

    [Fact]
    public void AnAddressHasOneAt()
    {
        Assert.Throws<ArgumentException>(() => owner.Put(HouseholdDocument.Empty, Household.Sam() with { GoogleAddress = "sam at example.com" }, Household.Next(), Household.Writer));
    }

    [Fact]
    public void APrintedPersonNamesTheIdOnly()
    {
        var text = Household.Robin().ToString();

        Assert.DoesNotContain("Robin", text);
        Assert.DoesNotContain("2016", text);
    }

    [Fact]
    public void ThePartnersEditIsRefusedAndTheDocumentUnchanged()
    {
        var partner = new HouseholdPeopleEditor(canEdit: false);
        var before = Household.WithFour();

        var edit = partner.Put(before, Household.Sam() with { OnPickup = false }, Household.Next(), "phone-partner");

        Assert.False(edit.Accepted);
        Assert.Same(before, edit.Document);
        Assert.Contains("Foreword", edit.Refusal);
    }

    [Fact]
    public void ChangingPickupKeepsEveryOtherField()
    {
        var document = owner.Put(Household.WithFour(), Household.Sam() with { OnPickup = false }, Household.Next(), Household.Writer).Document;

        var sam = HouseholdPeople.From(document).Find("sam")!;
        Assert.False(sam.OnPickup);
        Assert.Equal("sam@example.com", sam.GoogleAddress);
    }

    [Fact]
    public void AnEditKeepsLinksAndFieldsANewerBuildWrote()
    {
        var document = Household.WithFour();
        var key = new RecordKey(PeopleKinds.Person, "sam");
        var newer = document.Find(key)!.Edit(new Dictionary<string, string> { ["nickname"] = "S" }, 2, Household.Next(), "phone-newer");
        document = document.With(newer);
        document = owner.Link(document, "sam", PeopleApps.Pulp, "viewer-1", Household.Next(), Household.Writer).Document;

        document = owner.Put(document, Household.Sam() with { CalendarId = "sam-work@example.com" }, Household.Next(), Household.Writer).Document;

        Assert.Equal("S", document.Find(key)!.Fields["nickname"]);
        Assert.Equal("viewer-1", HouseholdPeople.From(document).Find("sam")!.Links[PeopleApps.Pulp]);
        Assert.Equal("sam-work@example.com", HouseholdPeople.From(document).Find("sam")!.CalendarId);
    }

    [Fact]
    public void RemovingAPersonLeavesATombstone()
    {
        var document = owner.Remove(Household.WithFour(), "juniper", Household.Next(), Household.Writer).Document;

        Assert.Null(HouseholdPeople.From(document).Find("juniper"));
        Assert.True(document.Find(new RecordKey(PeopleKinds.Person, "juniper"))!.IsTombstone);
    }

    [Fact]
    public void AnUnreadablePersonIsReportedNotDropped()
    {
        var bad = HouseholdRecord.Write(new RecordKey(PeopleKinds.Person, "ghost"), 1, new Dictionary<string, string> { ["child"] = "maybe" }, Household.Next(), "phone-newer");

        var people = HouseholdPeople.From(Household.WithFour().With(bad));

        Assert.Equal(4, people.People.Count);
        Assert.Single(people.Unreadable);
    }

    [Fact]
    public void TheRhythmRoundTrips()
    {
        var rhythm = new HouseholdRhythm
        {
            FamilyCalendarId = "family@example.com",
            DinnerStart = new TimeOnly(17, 45),
            DinnerLength = TimeSpan.FromMinutes(45),
            Bedtime = new TimeOnly(19, 30),
            ZoneId = "America/Chicago",
        };

        var document = owner.SetRhythm(HouseholdDocument.Empty, rhythm, Household.Next(), Household.Writer).Document;

        Assert.Equal(rhythm, HouseholdPeople.From(document).Rhythm);
    }

    [Fact]
    public void NoRhythmIsNothingChosenNotADefault()
    {
        var rhythm = HouseholdPeople.From(HouseholdDocument.Empty).Rhythm;

        Assert.Null(rhythm.FamilyCalendarId);
        Assert.False(rhythm.NoFamilyCalendar);
        Assert.Null(rhythm.DinnerStart);
        Assert.Null(rhythm.Bedtime);
    }

    [Fact]
    public void TheWireNamesAreFrozen()
    {
        Assert.Equal("storyworthtelling-household.json", PeopleFile.FileName);
        Assert.Equal("person", PeopleKinds.Person);
        Assert.Equal("household-rhythm", PeopleKinds.Rhythm);
        Assert.Equal("constitution", PeopleApps.Constitution);
        Assert.Equal("pulp", PeopleApps.Pulp);
    }
}
