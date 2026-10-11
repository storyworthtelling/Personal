using System.Globalization;

namespace Pulp.Domain.Calendar;

/// <summary>
/// The heading over a day on the Calendar page (mockup 8, DESIGN.md §11): "Tonight" for today when
/// something is on in the evening, "Today" otherwise, "Tomorrow", the weekday within the coming week, then
/// the date — with the year only when it is not this one. Read in the viewer's zone; the clock is an
/// argument. The forward-looking cousin of <c>RecentDayLabel</c>, which only looks back.
/// </summary>
public static class CalendarScreenDayLabel
{
    /// <summary>A day with a timed item at or after this hour is "Tonight" (the same hour <c>RecentDayLabel</c> uses).</summary>
    public static readonly TimeOnly EveningStarts = new(17, 0);

    /// <summary>The last day ahead named by its weekday; a week out is the date, since the weekday would be today's.</summary>
    private const int WeekdayDaysAhead = 6;

    public static string For(CalendarDay day, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(zone);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var ahead = day.Date.DayNumber - today.DayNumber;

        if (ahead <= 0)
        {
            return day.Items.Any(item => item.When.Time is { } time && time >= EveningStarts) ? "Tonight" : "Today";
        }

        if (ahead == 1)
        {
            return "Tomorrow";
        }

        if (ahead <= WeekdayDaysAhead)
        {
            return day.Date.ToString("dddd", CultureInfo.InvariantCulture);
        }

        return day.Date.Year == today.Year
            ? day.Date.ToString("MMM d", CultureInfo.InvariantCulture)
            : day.Date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
