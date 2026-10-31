using Constitution.Domain.Body;
using Constitution.Domain.Documents;
using Constitution.Domain.Workouts;

namespace Constitution.Tests.DigestPages;

/// <summary>Invented weeks for the digest tests: Sam's, with the week ending on Saturday 14 March 2026.</summary>
internal static class DigestFixture
{
    public static readonly DateOnly Saturday = new(2026, 3, 14);

    public static readonly DateOnly WeekStart = new(2026, 3, 8);

    public static DigestWorkoutWeek Week() => new(
        WeekStart,
        [
            new DigestSession(new DateOnly(2026, 3, 9), "Strength", 30, SessionOutcome.Done, "right"),
            new DigestSession(new DateOnly(2026, 3, 10), "Walk", 30, SessionOutcome.Done, "easy"),
            new DigestSession(new DateOnly(2026, 3, 12), "Mobility", 20, SessionOutcome.Skipped, null),
            new DigestSession(new DateOnly(2026, 3, 13), "Strength", 45, SessionOutcome.Done, "hard"),
        ],
        ["Wednesday's walk moved to Thursday for the forecast."]);

    /// <summary>One reading a day for <paramref name="days"/> days ending the day before the Saturday, each <paramref name="value"/>.</summary>
    public static List<BodyReading> Flat(BodySignal signal, double value, int days = 100)
    {
        var readings = new List<BodyReading>();
        for (var back = 1; back <= days; back++)
        {
            readings.Add(new BodyReading(signal, Saturday.AddDays(-back), value));
        }

        return readings;
    }

    public static DigestMealsWeek Meals() => new(
        "Lentil soup", "Braised short ribs", "Fine, a bit rich", "Lee will have it again with a salad",
        ["Five dinners cooked at home", "Two takeout nights", "One new recipe tried"]);

    public static DigestNextWeek Next() => new(
        [new DigestPlannedItem(new DateOnly(2026, 3, 16), "Strength")],
        [new DigestPlannedItem(new DateOnly(2026, 3, 16), "Sheet-pan chicken")]);

    public static SaturdayDigestModel Model(string person = "Sam", IEnumerable<BodyReading>? readings = null) => new(
        person, Saturday, Week(), (readings ?? Flat(BodySignal.Sleep, 7.0)).ToList(), Meals(), Next());
}
