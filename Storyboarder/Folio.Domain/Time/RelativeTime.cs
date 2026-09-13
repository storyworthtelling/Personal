using System.Globalization;

namespace Folio.Domain.Time;

/// <summary>
/// Relative list timestamps per DESIGN.md §11: the shortest honest phrase,
/// falling back to absolute dates once "ago" stops being meaningful.
/// </summary>
public static class RelativeTime
{
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
