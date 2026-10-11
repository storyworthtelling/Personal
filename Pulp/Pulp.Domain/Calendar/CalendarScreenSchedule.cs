namespace Pulp.Domain.Calendar;

/// <summary>
/// When the daily changes pass (the nightly calendar write rides it, D149) runs: first at the next 03:00 in
/// the household's own zone, then every day. The head hands the delay to WorkManager, which keeps one row
/// under the frozen <see cref="PulpIdentity.DailyChangesWorkName"/>.
/// </summary>
public static class CalendarScreenSchedule
{
    public static readonly TimeOnly RunAt = new(3, 0);

    public static readonly TimeSpan Repeat = TimeSpan.FromHours(24);

    public static TimeSpan DelayUntilNextRun(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var next = local.Date + RunAt.ToTimeSpan();
        if (next <= local.DateTime)
        {
            next = next.AddDays(1);
        }

        if (zone.IsInvalidTime(next))
        {
            next = next.AddHours(1);
        }

        return new DateTimeOffset(next, zone.GetUtcOffset(next)) - now;
    }
}
