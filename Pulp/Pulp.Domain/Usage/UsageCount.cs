using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Domain.Usage;

/// <summary>A calendar month, the unit Index's usage line counts in (D269).</summary>
public readonly record struct UsageMonth
{
    public UsageMonth(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    /// <summary>The month on the phone's own clock at that moment — the offset it was recorded with, not UTC.</summary>
    public static UsageMonth Of(DateTimeOffset at) => new(at.Year, at.Month);

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}

/// <summary>One time a service was opened through Pulp — the service and the moment, nothing else.</summary>
public sealed record UsageHandOff(ServiceId Service, DateTimeOffset At)
{
    /// <summary>The usage entry for a hand-off that started; the title and viewers stay behind.</summary>
    public static UsageHandOff From(PendingHandOff handOff)
    {
        ArgumentNullException.ThrowIfNull(handOff);
        return new UsageHandOff(handOff.Service, handOff.Evidence.At);
    }
}

/// <summary>Time a session showed being watched on a service, where one was seen (finding §2.3).</summary>
public sealed record UsageMinutes
{
    public UsageMinutes(ServiceId service, DateTimeOffset at, TimeSpan watched)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(watched, TimeSpan.Zero);
        Service = service;
        At = at;
        Watched = watched;
    }

    public ServiceId Service { get; }

    public DateTimeOffset At { get; }

    public TimeSpan Watched { get; }
}

/// <summary>
/// One row of what Index receives (D90, D269–D271): a service, a month, how many times it was opened
/// through Pulp, whole minutes watched where any session told us (null where none did — unknown is not
/// zero), and the last day it was opened that month, for "Not opened in Pulp since …" (D270).
/// </summary>
public sealed record ServiceMonthUsage(ServiceId Service, UsageMonth Month, int HandOffs, int? Minutes, DateOnly? LastOpened);

/// <summary>
/// The usage count (D90, D269–D271): hand-offs and minutes folded per service per calendar month.
/// Shape only — nothing here can carry a title or a viewer (guarded), so what the household watched
/// never crosses to another app. Rows come back by month, then service id, so a publish is stable.
/// </summary>
public static class UsageCount
{
    public static IReadOnlyList<ServiceMonthUsage> Tally(IEnumerable<UsageHandOff> handOffs, IEnumerable<UsageMinutes> minutes)
    {
        ArgumentNullException.ThrowIfNull(handOffs);
        ArgumentNullException.ThrowIfNull(minutes);

        var opened = handOffs.ToList();
        var watched = minutes.ToList();
        var keys = opened.Select(entry => (entry.Service, Month: UsageMonth.Of(entry.At)))
            .Concat(watched.Select(entry => (entry.Service, Month: UsageMonth.Of(entry.At))))
            .Distinct()
            .OrderBy(key => key.Month.Year)
            .ThenBy(key => key.Month.Month)
            .ThenBy(key => key.Service.Value, StringComparer.OrdinalIgnoreCase);

        return keys.Select(key =>
        {
            var inMonth = opened.Where(entry => entry.Service.Equals(key.Service) && UsageMonth.Of(entry.At) == key.Month).ToList();
            var seen = watched.Where(entry => entry.Service.Equals(key.Service) && UsageMonth.Of(entry.At) == key.Month).ToList();
            int? total = seen.Count == 0
                ? null
                : (int)TimeSpan.FromTicks(seen.Sum(entry => entry.Watched.Ticks)).TotalMinutes;
            DateOnly? last = inMonth.Count == 0 ? null : DateOnly.FromDateTime(inMonth.Max(entry => entry.At).DateTime);
            return new ServiceMonthUsage(key.Service, key.Month, inMonth.Count, total, last);
        }).ToList();
    }
}

/// <summary>
/// The usage entries for every hand-off the household has recorded. A hand-off is kept as each watching
/// viewer's own sitting, so one for two children is two sittings with the same service and instant and
/// is still one opening of the service.
/// </summary>
public static class UsageHandOffs
{
    public static IReadOnlyList<UsageHandOff> From(IEnumerable<WatchedSitting> sittings)
    {
        ArgumentNullException.ThrowIfNull(sittings);
        return sittings
            .Where(sitting => sitting.Sitting.Evidence == WatchSource.HandOff && sitting.Sitting.Service is not null)
            .Select(sitting => new UsageHandOff(sitting.Sitting.Service!.Value, sitting.Sitting.At))
            .Distinct()
            .ToList();
    }
}
