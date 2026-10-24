using Constitution.Domain.Calendar;

namespace Constitution.Domain.Household.Lease;

/// <summary>
/// The lease's week: Monday to Monday on the <b>household's</b> clock, not the phone's and not UTC, so both
/// installs name the same week even from different zones and across a daylight-saving change. The week is
/// the same one the weekly workout run uses (<c>WorkoutWeekJoin.MondayOf</c> lives in Infrastructure, which
/// Domain cannot see, so the three lines are repeated here and a test holds the two equal).
///
/// <para>A week's <b>index</b> is whole weeks since the calendar's first Monday (0001-01-01, day number 0),
/// so consecutive weeks are consecutive integers and the index is a stable, zone-free thing to put in a key.</para>
/// </summary>
public static class LeaseWeek
{
    private const int DaysInAWeek = 7;

    /// <summary>The Monday the week begins on, as the household's own calendar reads it.</summary>
    public static DateOnly MondayOf(DateTimeOffset instant, TimeZoneInfo householdZone)
    {
        ArgumentNullException.ThrowIfNull(householdZone);
        var day = ZoneClock.DayOf(instant, householdZone);
        var sinceMonday = ((int)day.DayOfWeek + 6) % DaysInAWeek;
        return day.AddDays(-sinceMonday);
    }

    /// <summary>The week's index: whole weeks since the first Monday of the calendar.</summary>
    public static long IndexOf(DateTimeOffset instant, TimeZoneInfo householdZone) =>
        MondayOf(instant, householdZone).DayNumber / DaysInAWeek;

    /// <summary>The Monday a week index names.</summary>
    public static DateOnly MondayOfIndex(long weekIndex) =>
        DateOnly.FromDayNumber(checked((int)(weekIndex * DaysInAWeek)));

    /// <summary>The first moment of the week on the household's clock (a week is an hour longer or shorter across a clock change).</summary>
    public static DateTimeOffset StartOf(long weekIndex, TimeZoneInfo householdZone) =>
        ZoneClock.StartOfDay(MondayOfIndex(weekIndex), householdZone);
}
