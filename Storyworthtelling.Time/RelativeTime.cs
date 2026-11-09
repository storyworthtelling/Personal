using System.Globalization;

namespace Storyworthtelling.Time;

/// <summary>
/// Relative list timestamps per DESIGN.md §11: the shortest honest phrase,
/// falling back to absolute dates once "ago" stops being meaningful.
/// </summary>
public static class RelativeTime
{
    /// <summary>An instant, read in the viewer's zone: what a phone holding UTC instants shows.</summary>
    public static string Format(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return Format(TimeZoneInfo.ConvertTime(at, zone).DateTime, TimeZoneInfo.ConvertTime(now, zone).DateTime);
    }

    public static string Format(DateTime timestampLocal, DateTime nowLocal)
    {
        var elapsed = nowLocal - timestampLocal;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }
        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }
        if (timestampLocal.Date == nowLocal.Date)
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }
        if (timestampLocal.Date == nowLocal.Date.AddDays(-1))
        {
            return "yesterday";
        }
        if (elapsed < TimeSpan.FromDays(7))
        {
            return timestampLocal.ToString("dddd", CultureInfo.InvariantCulture);
        }
        return timestampLocal.Year == nowLocal.Year
            ? timestampLocal.ToString("MMM d", CultureInfo.InvariantCulture)
            : timestampLocal.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
