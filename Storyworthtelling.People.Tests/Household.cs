using Storyworthtelling.Household;

namespace Storyworthtelling.People.Tests;

/// <summary>An invented household: Sam and Lee, the adults; Robin and Juniper, the children. Every address is example.com.</summary>
internal static class Household
{
    public const string Writer = "phone-owner";

    private static long wall = 1_000;

    public static HlcTimestamp Next() => new(++wall, 0);

    public static HouseholdPerson Sam() =>
        new("sam", "Sam", IsChild: false, Birthdate: null, OnPickup: true, GoogleAddress: "sam@example.com", CalendarId: "sam@example.com");

    public static HouseholdPerson Lee() =>
        new("lee", "Lee", IsChild: false, Birthdate: null, OnPickup: false, GoogleAddress: "lee@example.com", CalendarId: "lee@example.com");

    public static HouseholdPerson Robin() =>
        new("robin", "Robin", IsChild: true, Birthdate: new DateOnly(2016, 4, 9), OnPickup: false, GoogleAddress: null, CalendarId: "robin-cal@example.com");

    public static HouseholdDocument WithFour()
    {
        var editor = new HouseholdPeopleEditor(canEdit: true);
        var document = HouseholdDocument.Empty;
        foreach (var person in new[] { Sam(), Lee(), Robin(), new HouseholdPerson("juniper", "Juniper", true, null, false, null, null) })
        {
            document = editor.Put(document, person, Next(), Writer).Document;
        }

        return document;
    }
}
