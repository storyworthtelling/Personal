using Constitution.Domain.Calendar;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Settings;
using Storyworthtelling.Household;
using Storyworthtelling.People;

namespace Constitution.Tests.ForewordHousehold;

/// <summary>
/// Foreword owns the household; Constitution keeps its people as a mirror of it, under the ids its own records (taste notes, meal
/// answers, ratings) already use, and builds the week's setup from it.
/// </summary>
public class HouseholdMirrorTests
{
    private static long wall = 10_000;

    private static HlcTimestamp Next() => new(++wall, 0);

    private static HouseholdPeople Suite(HouseholdRhythm? rhythm, params HouseholdPerson[] people)
    {
        var editor = new HouseholdPeopleEditor(canEdit: true);
        var document = HouseholdDocument.Empty;
        foreach (var person in people)
        {
            document = editor.Put(document, person, Next(), "phone-owner").Document;
        }

        if (rhythm is not null)
        {
            document = editor.SetRhythm(document, rhythm, Next(), "phone-owner").Document;
        }

        return HouseholdPeople.From(document);
    }

    private static HouseholdPerson Sam => new("sam", "Sam", false, null, true, "sam@example.com", "sam@example.com");

    private static HouseholdPerson Lee => new("p-1a2b", "Lee", false, null, false, null, "lee@example.com")
    {
        Links = new Dictionary<string, string> { [PeopleApps.Constitution] = "lee-7" },
    };

    private static HouseholdPerson Robin => new("robin", "Robin", true, new DateOnly(2016, 4, 9), false, null, "robin-cal@example.com");

    private static HouseholdDocument Mirror(HouseholdDocument document, HouseholdPeople suite) =>
        HouseholdMirror.Apply(document, suite, Next, "phone-owner");

    [Fact]
    public void EveryoneInTheHouseholdIsOnTheRoster()
    {
        var roster = RosterView.From(Mirror(HouseholdDocument.Empty, Suite(null, Sam, Lee, Robin)));

        Assert.Equal(["Lee", "Robin", "Sam"], roster.Entries.Select(entry => entry.Name).Order());
        Assert.Equal(["Sam"], roster.PickupAdults.Select(entry => entry.Name));
        Assert.Equal(["Robin"], roster.Children.Select(entry => entry.Name));
    }

    [Fact]
    public void ALinkedPersonKeepsConstitutionsOwnId()
    {
        var roster = RosterView.From(Mirror(HouseholdDocument.Empty, Suite(null, Sam, Lee)));

        Assert.Equal(new PersonId("lee-7"), roster.Entries.Single(entry => entry.Name == "Lee").Id);
    }

    [Fact]
    public void ADuplicateCleanedUpInForewordKeepsTheRecordsOfTheOneConstitutionKnew()
    {
        // Constitution knew Lee as "lee-7"; Foreword removes that copy and keeps one it never linked to Constitution.
        var document = Mirror(HouseholdDocument.Empty, Suite(null, Sam, Lee));
        var kept = new HouseholdPerson("p-9f0e", "Lee", false, null, false, null, "lee@example.com");

        document = Mirror(document, Suite(null, Sam, kept));

        Assert.Equal(new PersonId("lee-7"), RosterView.From(document).Entries.Single(entry => entry.Name == "Lee").Id);
    }

    [Fact]
    public void AnIdConstitutionCannotHoldIsMadeOneItCan()
    {
        var odd = new HouseholdPerson("Juniper Ann", "Juniper", true, null, false, null, null);

        Assert.Equal(new PersonId("juniper-ann"), HouseholdMirror.IdFor(odd));
    }

    [Fact]
    public void TheBirthdateComesAcrossOnTheChild()
    {
        var document = Mirror(HouseholdDocument.Empty, Suite(null, Sam, Robin));

        var robin = HouseholdRecords.Read(document, RosterMaps.Person).Items.Single(person => person.Name == "Robin");
        Assert.Equal(new Birthdate(new DateOnly(2016, 4, 9)), robin.Birthdate);
    }

    [Fact]
    public void PickupChangedInForewordReachesTheRoster()
    {
        var document = Mirror(HouseholdDocument.Empty, Suite(null, Sam, Lee));

        document = Mirror(document, Suite(null, Sam with { OnPickup = false }, Lee with { OnPickup = true }));

        Assert.Equal(["Lee"], RosterView.From(document).PickupAdults.Select(entry => entry.Name));
    }

    [Fact]
    public void AnUnchangedHouseholdWritesNothing()
    {
        var suite = Suite(null, Sam, Lee, Robin);
        var document = Mirror(HouseholdDocument.Empty, suite);

        Assert.Same(document, Mirror(document, suite));
    }

    [Fact]
    public void SomeoneWhoLeftTheHouseholdLeavesTheRosterWithTheirRecords()
    {
        var document = Mirror(HouseholdDocument.Empty, Suite(null, Sam, Robin));

        document = Mirror(document, Suite(null, Sam));

        Assert.Equal(["Sam"], RosterView.From(document).Entries.Select(entry => entry.Name));
    }

    [Fact]
    public void AnEmptyHouseholdLeavesTheRosterAlone()
    {
        var document = Mirror(HouseholdDocument.Empty, Suite(null, Sam));

        Assert.Same(document, Mirror(document, HouseholdPeople.Empty));
    }

    [Fact]
    public void TheWeeksChoicesComeFromTheHouseholdAndThisPhoneKeepsWhoItDeliversFor()
    {
        var rhythm = new HouseholdRhythm
        {
            FamilyCalendarId = "family@example.com",
            DinnerStart = new TimeOnly(17, 45),
            DinnerLength = TimeSpan.FromMinutes(45),
            Bedtime = new TimeOnly(19, 30),
            ZoneId = "America/Chicago",
        };

        var choices = HouseholdMirror.Choices(Suite(rhythm, Sam, Lee, Robin), new HouseholdChoices { DeliversHere = "Lee", FamilyCalendarId = "old@example.com" });

        Assert.Equal("Lee", choices.DeliversHere);
        Assert.Equal("family@example.com", choices.FamilyCalendarId);
        Assert.Equal(["robin-cal@example.com"], choices.ChildrenCalendarIds);
        Assert.False(choices.NoChildren);
        Assert.Equal("sam@example.com", choices.AdultCalendars["Sam"]);
        Assert.Equal(new TimeOnly(19, 30), choices.Bedtime);
        Assert.Equal(TimeSpan.FromMinutes(45), choices.DinnerLength);
    }

    [Fact]
    public void AHouseholdWithNoChildrenSaysSo()
    {
        Assert.True(HouseholdMirror.Choices(Suite(null, Sam), new HouseholdChoices()).NoChildren);
    }
}
