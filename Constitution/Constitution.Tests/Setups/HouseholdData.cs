using Constitution.Domain.Calendar;

namespace Constitution.Tests.Setups;

/// <summary>An invented household, written as the file the phone holds: Sam and Lee, Robin and Juniper, and every address at example.com.</summary>
internal static class HouseholdData
{
    public const string File =
        "{\"version\":1,\"zone\":\"UTC\"," +
        "\"adults\":[" +
        "{\"name\":\"Sam\",\"calendar\":\"sam@example.com\",\"pickup\":true,\"addresses\":[\"sam@example.com\"],\"otherNames\":[\"Dad\"],\"deliversHere\":true}," +
        "{\"name\":\"Lee\",\"calendar\":\"lee@example.com\",\"pickup\":false,\"addresses\":[\"lee@example.com\"]}]," +
        "\"familyCalendar\":\"family@example.com\",\"childrenCalendars\":[\"robin@example.com\",\"juniper@example.com\"]," +
        "\"dinner\":\"18:00\",\"dinnerMinutes\":45,\"bedtime\":\"20:00\"}";

    public static PlanningSetup Setup() => new(
    [
        new HouseholdAdult("Sam", "sam@example.com", true, ["sam@example.com"], ["Dad"], DeliversHere: true),
        new HouseholdAdult("Lee", "lee@example.com", false, ["lee@example.com"]),
    ])
    {
        FamilyCalendarId = "family@example.com",
        ChildrenCalendarIds = ["robin@example.com", "juniper@example.com"],
        ZoneId = "UTC",
        DinnerStart = new TimeOnly(18, 0),
        DinnerLength = TimeSpan.FromMinutes(45),
        Bedtime = new TimeOnly(20, 0),
    };
}
