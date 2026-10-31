namespace Constitution.Domain.Documents;

/// <summary>
/// When the Saturday digest is due, on the household's clock: from four on Saturday morning (before anyone wakes, like the day's other pages), and on Sunday
/// as a catch-up for a Saturday the phone could not reach the tablet. On any other day it is not due, so a week that was missed twice is let go rather than sent
/// late and out of date. The answer names the Saturday the digest is for, which is what its name and its once-per-week stamp are keyed by.
/// </summary>
public static class DigestSchedule
{
    public const int FirstHour = 4;

    public static DateOnly? DueSaturday(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        switch (day.DayOfWeek)
        {
            case DayOfWeek.Saturday:
                return local.Hour >= FirstHour ? day : null;
            case DayOfWeek.Sunday:
                return day.AddDays(-1);
            default:
                return null;
        }
    }
}
