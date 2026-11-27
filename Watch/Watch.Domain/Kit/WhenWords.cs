using System.Globalization;

namespace Watch.Domain.Kit;

/// <summary>
/// The watch's relative times (WD §5, A37; P14 B4), in the shortest words the corner of a row holds, as the mockups
/// draw them (People waiting's "2 min", "1 h", Mail's "Yesterday"): "just now", "2 min", "3 h", "Yesterday", the day
/// of the week, then the date. Read in the watch's own zone, so "Yesterday" turns at the wrist's midnight. Not the
/// phones' list words ("2m ago", <c>Storyworthtelling.Time</c>): the watch shares no copy with Paper &amp; Ink (A33).
/// </summary>
public static class WhenWords
{
    public static string Relative(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            // A phone whose clock runs ahead of the watch's sends a moment that has not come yet: it is now.
            return "just now";
        }
        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} min";
        }

        var then = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        var today = TimeZoneInfo.ConvertTime(now, zone).DateTime.Date;
        if (then.Date == today)
        {
            return $"{(int)elapsed.TotalHours} h";
        }
        if (then.Date == today.AddDays(-1))
        {
            return "Yesterday";
        }
        if (then.Date > today.AddDays(-7))
        {
            return then.ToString("dddd", CultureInfo.InvariantCulture);
        }
        return then.Year == today.Year
            ? then.ToString("MMM d", CultureInfo.InvariantCulture)
            : then.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
