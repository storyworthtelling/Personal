using Constitution.Domain.Calendar;
using Constitution.Domain.Household;
using Constitution.Domain.Settings;

namespace Constitution.Tests.HouseholdSettings;

/// <summary>Invented household: Sam, Lee, Robin, Juniper; example.com.</summary>
internal static class HouseholdData
{
    public static RosterView Roster() => RosterView.From(Tests.HouseholdFile.RosterData.Household());

    public static HouseholdChoices Choices() => new()
    {
        Bedtime = new TimeOnly(19, 30),
        DinnerStart = new TimeOnly(17, 45),
        DinnerLength = TimeSpan.FromMinutes(45),
        ZoneId = "America/Chicago",
        FamilyCalendarId = "family@example.com",
        ChildrenCalendarIds = ["school@example.com"],
        DeliversHere = "Sam",
        AdultCalendars = new Dictionary<string, string> { ["Sam"] = "sam@example.com", ["Lee"] = "lee@example.com" },
    };

    public static PlanningSetup Setup() => new([
        new HouseholdAdult("Sam", "sam@example.com", true, ["sam@example.com"], null, true),
        new HouseholdAdult("Lee", null, null),
    ]);
}
