namespace Constitution.Domain.Calendar;

/// <summary>
/// Why a stretch of time is taken. The first three are <b>busy</b> (something on a calendar); the
/// next three are <b>never-placed</b> (a rule of the household that no calendar says). They are
/// flags because stretches that overlap or touch merge into one and keep every reason.
/// </summary>
[Flags]
public enum BusyReason
{
    None = 0,

    /// <summary>The person's own calendar.</summary>
    OwnCalendar = 1,

    /// <summary>The family calendar.</summary>
    FamilyCalendar = 2,

    /// <summary>A children's event this person is on the hook for.</summary>
    ChildrensEvent = 4,

    /// <summary>Work hours from Foreword's schedule.</summary>
    WorkHours = 8,

    /// <summary>The family dinner.</summary>
    FamilyDinner = 16,

    /// <summary>The children's bedtime routine.</summary>
    BedtimeRoutine = 32,
}

/// <summary>Which side of the busy / never-placed line a reason falls on.</summary>
public static class BusyReasons
{
    private const BusyReason BusyMask = BusyReason.OwnCalendar | BusyReason.FamilyCalendar | BusyReason.ChildrensEvent;
    private const BusyReason NeverPlacedMask = BusyReason.WorkHours | BusyReason.FamilyDinner | BusyReason.BedtimeRoutine;

    /// <summary>True when every reason is something on a calendar (and there is at least one).</summary>
    public static bool IsBusy(BusyReason reasons) => reasons != BusyReason.None && (reasons & ~BusyMask) == 0;

    /// <summary>True when every reason is a household rule (and there is at least one).</summary>
    public static bool IsNeverPlaced(BusyReason reasons) => reasons != BusyReason.None && (reasons & ~NeverPlacedMask) == 0;
}

/// <summary>
/// A half-open stretch of time <c>[Start, End)</c> that is taken, and why. Touching stretches do not
/// overlap: a workout may start the instant a meeting ends.
/// </summary>
public sealed record BusyInterval(DateTimeOffset Start, DateTimeOffset End, BusyReason Reasons)
{
    public TimeSpan Length => End - Start;

    /// <summary>True when <c>[start, end)</c> shares any time with this stretch.</summary>
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => start < End && Start < end;
}

/// <summary>A stretch of time <c>[Start, End)</c> in which nothing is taken.</summary>
public sealed record FreeGap(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Length => End - Start;
}

/// <summary>The one place intervals are merged, so "touching merges" is not decided twice.</summary>
internal static class Intervals
{
    /// <summary>Sorted, with any that overlap or touch merged into one carrying every reason. Empty ones are dropped.</summary>
    public static List<BusyInterval> Merge(IEnumerable<BusyInterval> intervals)
    {
        var merged = new List<BusyInterval>();
        foreach (var next in intervals.Where(interval => interval.End > interval.Start).OrderBy(interval => interval.Start).ThenBy(interval => interval.End))
        {
            if (merged.Count > 0 && next.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = new BusyInterval(last.Start, next.End > last.End ? next.End : last.End, last.Reasons | next.Reasons);
            }
            else
            {
                merged.Add(next);
            }
        }

        return merged;
    }

    /// <summary>The part of the interval inside <c>[from, to)</c>, or null if none of it is.</summary>
    public static BusyInterval? Clip(BusyInterval interval, DateTimeOffset from, DateTimeOffset to)
    {
        var start = interval.Start > from ? interval.Start : from;
        var end = interval.End < to ? interval.End : to;
        return end > start ? interval with { Start = start, End = end } : null;
    }
}
