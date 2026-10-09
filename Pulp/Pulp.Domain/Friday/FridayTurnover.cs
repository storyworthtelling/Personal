namespace Pulp.Domain.Friday;

/// <summary>
/// D110: the stack turns over at Friday 6 am in the household's own zone. Worked in local time and
/// converted back, so the week across a clock change is a week of six-am Fridays, not 168 hours.
/// </summary>
public static class FridayTurnover
{
    public static readonly TimeOnly At = new(6, 0);

    public const DayOfWeek Day = DayOfWeek.Friday;

    /// <summary>The turnover that began the week <paramref name="now"/> is in: the latest Friday 6 am at or before it.</summary>
    public static DateTimeOffset Current(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var daysSince = ((int)local.DayOfWeek - (int)Day + 7) % 7;
        var friday = DateOnly.FromDateTime(local).AddDays(-daysSince);
        if (local < friday.ToDateTime(At))
        {
            friday = friday.AddDays(-7);
        }

        return Local(friday, zone);
    }

    /// <summary>The next turnover after <paramref name="now"/>: when this week's stack is replaced.</summary>
    public static DateTimeOffset Next(DateTimeOffset now, TimeZoneInfo zone)
    {
        var current = Current(now, zone);
        var friday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(current, zone).DateTime);
        return Local(friday.AddDays(7), zone);
    }

    private static DateTimeOffset Local(DateOnly friday, TimeZoneInfo zone)
    {
        var local = friday.ToDateTime(At, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
