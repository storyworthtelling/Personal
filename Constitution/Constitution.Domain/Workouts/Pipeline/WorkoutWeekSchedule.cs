using Constitution.Domain.Calendar;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// When the weekly workout run wakes each day: a quarter to seven on the household's own clock, after Foreword's delivery
/// (half past six) so the calendar it reads is the morning's, and before the read-back (a quarter past seven) so a page the run
/// sends is on the tablet by the time that worker looks. The run is idempotent, so the exact minute matters only for that order.
///
/// <para>The time is a wall-clock time and is worked out in the household's zone (<see cref="ZoneClock"/>), not by adding a day to an
/// offset: on the two nights a year the clocks change, the wait is an hour shorter or longer than a day's worth of hours, and a
/// fixed offset would wake the run an hour off the clock the owner reads.</para>
/// </summary>
public static class WorkoutWeekSchedule
{
    public const int Hour = 6;

    public const int Minute = 45;

    /// <summary>
    /// How long from <paramref name="now"/> until the next <see cref="Hour"/>:<see cref="Minute"/> on <paramref name="zone"/>'s clock.
    /// Exactly on the time waits for the next day's.
    /// </summary>
    public static TimeSpan DelayUntilNext(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var time = new TimeOnly(Hour, Minute);
        var today = ZoneClock.DayOf(now, zone);
        var target = ZoneClock.At(today, time, zone, ClockEdge.Start);
        if (target <= now)
        {
            target = ZoneClock.At(today.AddDays(1), time, zone, ClockEdge.Start);
        }

        return target - now;
    }
}
