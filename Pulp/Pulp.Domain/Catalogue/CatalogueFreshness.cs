using System.Globalization;

namespace Pulp.Domain.Catalogue;

/// <summary>When a cached title was fetched, and whether the daily changes feed has since touched it.</summary>
public sealed record CacheStamp(DateTimeOffset FetchedAt, bool Invalidated);

/// <summary>
/// Whether a cached title should be asked about again (D204: weekly per tracked title, a daily
/// changes pass, a fresh look on open — inside the availability source's 1,000 lookups a month).
/// The screens always paint the cached copy first; this only says whether a fetch should follow.
/// The clock is an argument.
/// </summary>
public static class CatalogueFreshness
{
    /// <summary>A title on someone's Continue Watching or a playlist is looked at again once a week.</summary>
    public static readonly TimeSpan TrackedRefresh = TimeSpan.FromDays(7);

    /// <summary>Opening a title page looks again at most once a day, so paging back and forth spends nothing.</summary>
    public static readonly TimeSpan OpenThrottle = TimeSpan.FromHours(24);

    public static bool NeedsFetch(CacheStamp? cached, bool tracked, bool opened, DateTimeOffset now)
    {
        if (cached is null || cached.Invalidated)
        {
            return true;
        }

        // A stamp from the future means the phone's clock stepped back; trusting it would keep the
        // copy fresh until real time caught up, so it is treated as stale.
        if (cached.FetchedAt > now)
        {
            return true;
        }

        var age = now - cached.FetchedAt;
        return (opened && age >= OpenThrottle) || (tracked && age >= TrackedRefresh);
    }
}

/// <summary>One month's lookup count for one source.</summary>
public sealed record MonthTally(string Month, int Count);

/// <summary>A counted lookup: the month's tally now, and the month that just closed, if this lookup opened a new one.</summary>
public sealed record MonthCount(MonthTally Tally, MonthTally? Closed);

/// <summary>
/// The month's lookup count, for Diagnostics only (D261) — never a notice. Counted per UTC month
/// (the provider's allowance is monthly), and only milestones are worth a line in the log, since a
/// line per lookup would evict the failure history the log exists to keep.
/// </summary>
public static class LookupMonth
{
    /// <summary>The availability source's free allowance (finding §1.3).</summary>
    public const int Allowance = 1000;

    /// <summary>A line at the month's first lookup and every this-many after.</summary>
    public const int LineEvery = 100;

    public static string Of(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static MonthCount Count(MonthTally? current, DateTimeOffset at)
    {
        var month = Of(at);
        if (current is null)
        {
            return new MonthCount(new MonthTally(month, 1), null);
        }

        return current.Month == month
            ? new MonthCount(current with { Count = current.Count + 1 }, null)
            : new MonthCount(new MonthTally(month, 1), current);
    }

    public static bool IsMilestone(int count) => count == 1 || (count > 0 && count % LineEvery == 0);
}
