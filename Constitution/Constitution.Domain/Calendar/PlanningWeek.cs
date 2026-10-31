namespace Constitution.Domain.Calendar;

/// <summary>
/// The household's planning week, and the only place that says where it begins. It runs <b>Sunday through
/// Saturday</b> everywhere the household plans: the workouts, the dinners, the groceries and the planning lease. Nothing
/// else in the app decides which day opens a week, counts days since a remembered Monday, or divides a day number by seven.
///
/// <para><b>A week has a number.</b> <see cref="IndexOf(DateOnly)"/> counts whole weeks from the calendar's first, and
/// is what a placement key carries as its week (<c>PlacementKey</c>), what the planner turns its exercises over by, and what
/// the lease names a week by. The calendar's first day, 0001-01-01, is a Monday, so week zero's Sunday does not exist;
/// week one opens on 0001-01-07, and every date a household can have lies far beyond it. The number is of the week, not
/// of a day, so every day of a week gives the same one.</para>
///
/// <para><b>The zone is the household's.</b> The helpers that take an instant read the day on the household's clock
/// (<see cref="ZoneClock"/>), so two phones in different zones name the same week, and a week is an hour longer or shorter
/// across a clock change (<see cref="Opens"/> and <see cref="Closes"/>); the days themselves are dates and do not move.</para>
/// </summary>
public static class PlanningWeek
{
    /// <summary>The day a week begins on. Changing it renumbers every week, so it is a decision, not a setting.</summary>
    public const DayOfWeek FirstDay = DayOfWeek.Sunday;

    /// <summary>Days in a week.</summary>
    public const int DaysInWeek = 7;

    // Days from the start of the week the calendar's first day sits in to that first day: 1 when the first day is a
    // Monday and weeks start on Sunday. The offset that makes (day number + offset) / 7 whole weeks.
    private static readonly int EpochOffset = Position(DateOnly.MinValue);

    /// <summary>The first day of the week the date falls in: the Sunday on or before it.</summary>
    public static DateOnly StartOf(DateOnly day) => day.AddDays(-Position(day));

    /// <summary>Whether the date opens a week.</summary>
    public static bool IsStart(DateOnly day) => day.DayOfWeek == FirstDay;

    /// <summary>The last day of the week the date falls in; for a week's first day, the Saturday that follows it.</summary>
    public static DateOnly EndOf(DateOnly start) => StartOf(start).AddDays(DaysInWeek - 1);

    /// <summary>How many days into its week the date is: 0 for Sunday, 6 for Saturday.</summary>
    public static int Position(DateOnly day) => Position(day.DayOfWeek);

    /// <summary>Where a weekday sits in the week: 0 for Sunday, 6 for Saturday.</summary>
    public static int Position(DayOfWeek day) => ((int)day - (int)FirstDay + DaysInWeek) % DaysInWeek;

    /// <summary>The number of the week the date falls in. Every day of a week gives the same number; the next week's is one more.</summary>
    public static int IndexOf(DateOnly day) => (day.DayNumber + EpochOffset) / DaysInWeek;

    /// <summary>The first day of the numbered week.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The number is negative, or is week zero, whose first day would fall before the calendar begins.</exception>
    public static DateOnly StartOfIndex(int index)
    {
        var dayNumber = (long)index * DaysInWeek - EpochOffset;
        if (index < 0 || dayNumber < DateOnly.MinValue.DayNumber || dayNumber > DateOnly.MaxValue.DayNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "That week does not begin on a day the calendar has.");
        }

        return DateOnly.FromDayNumber((int)dayNumber);
    }

    /// <summary>The seven days of the week that begins on <paramref name="start"/>, the first day first.</summary>
    /// <exception cref="ArgumentException">The date is not the first day of a week.</exception>
    public static IReadOnlyList<DateOnly> Days(DateOnly start)
    {
        RequireStart(start, nameof(start));
        return Enumerable.Range(0, DaysInWeek).Select(start.AddDays).ToArray();
    }

    /// <summary>Refuses a date that does not open a week, in words that name the day it was given.</summary>
    /// <exception cref="ArgumentException">The date is not a Sunday.</exception>
    public static void RequireStart(DateOnly day, string name)
    {
        if (!IsStart(day))
        {
            throw new ArgumentException(
                $"A week begins on a {FirstDay}, and {day:yyyy-MM-dd} is a {day.DayOfWeek}. A key names a session by its weekday within the week, so a week begun on another day would number the same days differently from one run to the next.",
                name);
        }
    }

    /// <summary>The first day of the week the instant falls in, on the household's clock.</summary>
    public static DateOnly StartOf(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return StartOf(ZoneClock.DayOf(instant, zone));
    }

    /// <summary>The number of the week the instant falls in, on the household's clock.</summary>
    public static int IndexOf(DateTimeOffset instant, TimeZoneInfo zone) => IndexOf(StartOf(instant, zone));

    /// <summary>The first moment of the week that begins on <paramref name="start"/>, on the household's clock.</summary>
    /// <exception cref="ArgumentException">The date is not the first day of a week.</exception>
    public static DateTimeOffset Opens(DateOnly start, TimeZoneInfo zone)
    {
        RequireStart(start, nameof(start));
        return ZoneClock.StartOfDay(start, zone);
    }

    /// <summary>The moment the week ends and the next opens (a week is an hour longer or shorter across a clock change).</summary>
    /// <exception cref="ArgumentException">The date is not the first day of a week.</exception>
    public static DateTimeOffset Closes(DateOnly start, TimeZoneInfo zone)
    {
        RequireStart(start, nameof(start));
        return ZoneClock.StartOfDay(start.AddDays(DaysInWeek), zone);
    }
}
