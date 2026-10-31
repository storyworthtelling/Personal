using Constitution.Domain.Calendar;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>Which week the deadline step has to decide, or why it has none. A closed pair.</summary>
public abstract record MealDeadlineChoice
{
    private MealDeadlineChoice()
    {
    }

    /// <summary>No week needs deciding. The sentence is fixed words.</summary>
    public sealed record Nothing(MealDeadlineNothingBecause Because, string Sentence) : MealDeadlineChoice;

    /// <summary>This kept week is to be decided; <paramref name="Existing"/> is what an earlier run recorded for it, or null when none did.</summary>
    public sealed record Decide(StoredMealWeek Target, MealDecisionEntry? Existing) : MealDeadlineChoice;
}

/// <summary>
/// Which kept week the deadline step decides: the newest whose deadline has passed and that the record of decided weeks does not hold, or an
/// already-decided week that still has judging or calendar removals to retry. One rule with two readers, the step itself and the worker's cheap
/// "is anything due" look (<see cref="MealPassPlan"/>), so the two can never disagree about whether a week is waiting. Pure.
/// </summary>
public static class MealDeadlineTarget
{
    public const string NoWeekSentence = "No planned week is kept, so there is no sheet to decide.";
    public const string BeforeSentence = "The sheet's deadline has not passed yet.";
    public const string DecidedSentence = "Every week whose deadline has passed is already decided.";

    public static MealDeadlineChoice Choose(MealWeekRecord kept, MealDecisionRecord decided, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(decided);
        ArgumentNullException.ThrowIfNull(zone);

        if (kept.Weeks.Count == 0)
        {
            return new MealDeadlineChoice.Nothing(MealDeadlineNothingBecause.NoWeekKept, NoWeekSentence);
        }

        var passed = kept.Weeks.Where(week => now >= MealDeadline.ForWeek(week.WeekStart, zone)).ToList();
        if (passed.Count == 0)
        {
            return new MealDeadlineChoice.Nothing(MealDeadlineNothingBecause.BeforeTheDeadline, BeforeSentence);
        }

        // The newest week that has passed is the one to decide. An older week left undecided is not decided late: its evenings are over.
        var newest = passed.MaxBy(week => week.WeekStart)!;
        var earlier = decided.For(newest.WeekStart);
        if (earlier is null)
        {
            return new MealDeadlineChoice.Decide(newest, null);
        }

        var today = ZoneClock.DayOf(now, zone);
        var again = passed
            .Where(week => decided.For(week.WeekStart) is { } entry && (entry.EvidencePending || entry.RemovalsDue(today)))
            .MaxBy(week => week.WeekStart);
        return again is null
            ? new MealDeadlineChoice.Nothing(MealDeadlineNothingBecause.AlreadyDecided, DecidedSentence)
            : new MealDeadlineChoice.Decide(again, decided.For(again.WeekStart));
    }
}
