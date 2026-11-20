using Xunit;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Storyworthtelling.Household;
using Storyworthtelling.People;

namespace Foreword.Tests.Calendar;

/// <summary>
/// The household (owned here, shared with every app) is who the columns are: one column per person on both phones, each phone keeping
/// its own order and routing words. A person renamed in the household is renamed in every rule; a person who left takes their column.
/// </summary>
public class HouseholdColumnsTests
{
    private static long wall = 5_000;

    private static HouseholdPeople People(params HouseholdPerson[] people) => HouseholdPeople.From(Document(people));

    private static HouseholdDocument Document(IEnumerable<HouseholdPerson> people, HouseholdRhythm? rhythm = null)
    {
        var editor = new HouseholdPeopleEditor(canEdit: true);
        var document = HouseholdDocument.Empty;
        foreach (var person in people)
        {
            document = editor.Put(document, person, new HlcTimestamp(++wall, 0), "phone-owner").Document;
        }

        if (rhythm is not null)
        {
            document = editor.SetRhythm(document, rhythm, new HlcTimestamp(++wall, 0), "phone-owner").Document;
        }

        return document;
    }

    private static HouseholdPerson Sam => new("sam", "Sam", false, null, true, "sam@example.com", "sam@example.com");

    private static HouseholdPerson Lee => new("lee", "Lee", false, null, false, null, null);

    private static HouseholdPerson Robin => new("robin", "Robin", true, null, false, null, "robin-cal@example.com");

    private static (CalendarMappingOptions Mapping, WorkScheduleOptions Work) Fresh() => (new CalendarMappingOptions(), new WorkScheduleOptions());

    [Fact]
    public void EveryPersonGetsAColumnWithTheirAccountAndAddress()
    {
        var (mapping, work) = Fresh();

        Assert.True(HouseholdColumns.Apply(mapping, work, People(Sam, Lee, Robin)));

        Assert.Equal(["Lee", "Robin", "Sam"], mapping.Layout().People.Select(column => column.Name).Order());
        Assert.True(mapping.Layout().HasAccount(new PersonColumn("Sam")));
        Assert.False(mapping.Layout().HasAccount(new PersonColumn("Lee")));
        Assert.Contains("sam@example.com", mapping.PersonAddresses[new PersonColumn("Sam")]);
        Assert.Equal("sam", mapping.Columns.Single(column => column.Name == "Sam").HouseholdId);
    }

    [Fact]
    public void ThisPhonesOrderIsKept()
    {
        var (mapping, work) = Fresh();
        HouseholdColumns.Apply(mapping, work, People(Sam, Lee, Robin));
        ColumnEdits.Move(mapping, new PersonColumn("Robin"), -2);
        var order = mapping.Layout().People.Select(column => column.Name).ToList();

        Assert.False(HouseholdColumns.Apply(mapping, work, People(Sam, Lee, Robin)));

        Assert.Equal(order, mapping.Layout().People.Select(column => column.Name).ToList());
    }

    [Fact]
    public void ARenameInTheHouseholdRenamesEveryRule()
    {
        var (mapping, work) = Fresh();
        HouseholdColumns.Apply(mapping, work, People(Sam, Lee));
        mapping.Keywords[new PersonColumn("Lee")] = ["swim"];

        HouseholdColumns.Apply(mapping, work, People(Sam, Lee with { Name = "Juniper" }));

        Assert.Equal(["swim"], mapping.Keywords[new PersonColumn("Juniper")]);
        Assert.DoesNotContain(mapping.Columns, column => column.Name == "Lee");
    }

    [Fact]
    public void APersonWhoLeftTakesTheirColumn()
    {
        var (mapping, work) = Fresh();
        HouseholdColumns.Apply(mapping, work, People(Sam, Lee));

        HouseholdColumns.Apply(mapping, work, People(Sam));

        Assert.Equal(["Sam"], mapping.Layout().People.Select(column => column.Name));
    }

    [Fact]
    public void AColumnTheHouseholdDoesNotHaveGoes()
    {
        var (mapping, work) = Fresh();
        ColumnEdits.Add(mapping, "Juniper", hasAccount: false);

        HouseholdColumns.Apply(mapping, work, People(Sam));

        Assert.Equal(["Sam"], mapping.Layout().People.Select(column => column.Name));
    }

    [Fact]
    public void ASameNamedColumnIsAdoptedWithItsRules()
    {
        var (mapping, work) = Fresh();
        ColumnEdits.Add(mapping, "Lee", hasAccount: false);
        mapping.Keywords[new PersonColumn("Lee")] = ["piano"];

        HouseholdColumns.Apply(mapping, work, People(Lee));

        Assert.Equal(["piano"], mapping.Keywords[new PersonColumn("Lee")]);
        Assert.Equal("lee", mapping.Columns.Single(column => column.Name == "Lee").HouseholdId);
    }

    [Fact]
    public void APersonsCalendarPrintsInTheirColumnUnlessThisPhoneAnsweredIt()
    {
        var (mapping, work) = Fresh();
        mapping.NotPrintedCalendars.Add("sam@example.com");

        HouseholdColumns.Apply(mapping, work, People(Sam, Robin));

        Assert.Equal(new PersonColumn("Robin"), mapping.CalendarColumns["robin-cal@example.com"]);
        Assert.False(mapping.CalendarColumns.ContainsKey("sam@example.com"));
    }

    [Fact]
    public void TheFamilyCalendarIsSplitByNameUnlessThisPhoneAnsweredIt()
    {
        var (mapping, work) = Fresh();

        HouseholdColumns.Apply(mapping, work, HouseholdPeople.From(Document([Sam], new HouseholdRhythm { FamilyCalendarId = "family@example.com" })));

        Assert.True(mapping.SplitCalendars.Contains("family@example.com") || mapping.FamilyCalendarId == "family@example.com");
    }

    [Fact]
    public void AnEmptyHouseholdChangesNothing()
    {
        var (mapping, work) = Fresh();
        ColumnEdits.Add(mapping, "Juniper", hasAccount: false);

        Assert.False(HouseholdColumns.Apply(mapping, work, HouseholdPeople.Empty));

        Assert.Contains(mapping.Columns, column => column.Name == "Juniper");
    }

    [Fact]
    public void ColumnsNotYetSharedAreOfferedForImport()
    {
        var (mapping, _) = Fresh();
        ColumnEdits.Add(mapping, "Sam", hasAccount: true);
        ColumnEdits.Add(mapping, "Robin", hasAccount: false);
        mapping.PersonAddresses[new PersonColumn("Sam")] = ["sam@example.com"];

        var offered = HouseholdColumns.Unshared(mapping);

        var sam = offered.Single(person => person.Name == "Sam");
        Assert.False(sam.IsChild);
        Assert.Equal("sam@example.com", sam.GoogleAddress);
        Assert.True(offered.Single(person => person.Name == "Robin").IsChild);
        Assert.DoesNotContain(offered, person => person.Name == "Me");
    }

    [Fact]
    public void AColumnsOwnCalendarIsOfferedToThePersonWithNone()
    {
        var (mapping, work) = Fresh();
        HouseholdColumns.Apply(mapping, work, People(Lee));
        mapping.CalendarColumns["lee-made@example.com"] = new PersonColumn("Lee");

        var calendars = HouseholdColumns.CalendarsToShare(mapping, People(Lee, Sam));

        Assert.Equal("lee-made@example.com", calendars["lee"]);
        Assert.False(calendars.ContainsKey("sam"));
    }
}
