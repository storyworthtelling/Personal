using Constitution.Domain.Calendar;

namespace Constitution.Domain.Household.Lease;

/// <summary>
/// The lease's week: Sunday to Sunday on the <b>household's</b> clock, not the phone's and not UTC, so both
/// installs name the same week even from different zones and across a daylight-saving change. It is the household's
/// planning week (<see cref="PlanningWeek"/>), the same one the weekly workout run, the dinners and the groceries use;
/// this class only widens the week number to the lease's <c>long</c> and keeps the lease's own vocabulary.
///
/// <para>A week's <b>index</b> is <see cref="PlanningWeek.IndexOf(DateOnly)"/>, whole weeks from the calendar's
/// first, so consecutive weeks are consecutive integers and the index is a stable, zone-free thing to put in a key.</para>
/// </summary>
public static class LeaseWeek
{
    /// <summary>The Sunday the week begins on, as the household's own calendar reads it.</summary>
    public static DateOnly FirstDayOf(DateTimeOffset instant, TimeZoneInfo householdZone) =>
        PlanningWeek.StartOf(instant, householdZone);

    /// <summary>The week's index: whole weeks since the calendar's first.</summary>
    public static long IndexOf(DateTimeOffset instant, TimeZoneInfo householdZone) =>
        PlanningWeek.IndexOf(instant, householdZone);

    /// <summary>The Sunday a week index names.</summary>
    public static DateOnly FirstDayOfIndex(long weekIndex) =>
        PlanningWeek.StartOfIndex(checked((int)weekIndex));

    /// <summary>The first moment of the week on the household's clock (a week is an hour longer or shorter across a clock change).</summary>
    public static DateTimeOffset StartOf(long weekIndex, TimeZoneInfo householdZone) =>
        PlanningWeek.Opens(FirstDayOfIndex(weekIndex), householdZone);
}
