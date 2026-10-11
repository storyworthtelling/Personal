using System.Globalization;

namespace Pulp.Domain.Library;

/// <summary>
/// The day headings on Recently watched (mockup 05, D226), in DESIGN.md §11's relative-time rule:
/// "Tonight" (or "Today" for a day watched only before the evening), "Yesterday", the weekday within
/// the week ("Tuesday"), then the date ("Sep 27"), with the year only when it is not this one. Read in
/// the viewer's zone, as <see cref="RecentlyWatched"/> groups the days; the clock is an argument.
/// Pulp's Domain has no <c>RelativeTime</c> of its own, and a heading is a day, not an instant, so this
/// is its own small rule rather than a copy of the list-timestamp one.
/// </summary>
public static class RecentDayLabel
{
    /// <summary>A day whose last sitting began at or after this hour is "Tonight".</summary>
    public static readonly TimeOnly EveningStarts = new(17, 0);

    /// <param name="day">The day the rows were last watched, in <paramref name="zone"/>.</param>
    /// <param name="newestThatDay">The latest sitting on that day, which says whether it was the evening.</param>
    public static string For(DateOnly day, DateTimeOffset newestThatDay, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

        // A sitting stamped ahead of this phone's clock is still today's, not a date in the future.
        if (day >= today)
        {
            var at = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(newestThatDay, zone).DateTime);
            return at >= EveningStarts ? "Tonight" : "Today";
        }

        var daysAgo = today.DayNumber - day.DayNumber;
        if (daysAgo == 1)
        {
            return "Yesterday";
        }

        if (daysAgo < 7)
        {
            return day.ToString("dddd", CultureInfo.InvariantCulture);
        }

        return day.Year == today.Year
            ? day.ToString("MMM d", CultureInfo.InvariantCulture)
            : day.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
