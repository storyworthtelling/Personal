namespace Constitution.Domain.Calendar;

/// <summary>Which end of a span a wall-clock time is.</summary>
public enum ClockEdge
{
    /// <summary>The time opens a span.</summary>
    Start,

    /// <summary>The time closes a span.</summary>
    End,
}

/// <summary>
/// Turns "6 pm on that date, in that zone" into an instant, and an instant back into the local day.
/// Everything in the busy-time model that is a time of day rather than an instant (work hours,
/// dinner, the bedtime routine, the start of a local day) goes through here, so the two awkward
/// nights of the year are settled in one place.
///
/// <list type="bullet">
///   <item><b>A time that does not exist</b> (spring forward: 2:30 am when the clock jumps from 2 to
///   3) resolves to the moment the clock jumps, for either edge.</item>
///   <item><b>A time that happens twice</b> (fall back: 1:30 am, once on daylight time and once on
///   standard) resolves to the <i>earlier</i> occurrence when it opens a span and the <i>later</i>
///   when it closes one. A span is therefore never made shorter than its wall clock says; the cost is
///   that one over a repeated hour covers both passes of it, which is the safe side.</item>
/// </list>
///
/// <para>Every instant that comes back carries the zone's own offset at that instant, so reading its
/// <see cref="DateTimeOffset.Hour"/> or <see cref="DateTimeOffset.DayOfWeek"/> gives the local clock.</para>
/// </summary>
public static class ZoneClock
{
    /// <summary>The instant at which the zone's clock reads <paramref name="time"/> on <paramref name="day"/>.</summary>
    public static DateTimeOffset At(DateOnly day, TimeOnly time, TimeZoneInfo zone, ClockEdge edge)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            // Inside a gap. Step to the first minute the clock really shows; that is the jump. Gap
            // boundaries sit on whole minutes, so a probe floored to the minute lands exactly on it.
            var probe = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified);
            do
            {
                probe = probe.AddMinutes(1);
            }
            while (zone.IsInvalidTime(probe));

            local = probe;
        }

        var offset = zone.IsAmbiguousTime(local)
            ? PickOffset(zone.GetAmbiguousTimeOffsets(local), edge)
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    /// <summary>
    /// The first real moment of the zone's day. Usually midnight; the hour after it on the rare zone
    /// whose clock skips midnight.
    /// </summary>
    public static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone) =>
        At(day, TimeOnly.MinValue, zone, ClockEdge.Start);

    /// <summary>
    /// The two ends of a window that starts at <paramref name="start"/> on <paramref name="day"/> and
    /// is <paramref name="length"/> long on the wall clock. A window that ends exactly at midnight
    /// ends at the next day's first moment. Its end is resolved on its own as an end, not by adding
    /// the length to the start's instant, so a clock change inside the window does not shorten it.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Window(DateOnly day, TimeOnly start, TimeSpan length, TimeZoneInfo zone)
    {
        var end = start.ToTimeSpan() + length;
        if (end > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "A window runs within one day; this one would pass midnight.");
        }

        var closes = end == TimeSpan.FromDays(1)
            ? At(day.AddDays(1), TimeOnly.MinValue, zone, ClockEdge.End)
            : At(day, TimeOnly.FromTimeSpan(end), zone, ClockEdge.End);
        return (At(day, start, zone, ClockEdge.Start), closes);
    }

    /// <summary>The local day the instant falls on in <paramref name="zone"/>; not the UTC day.</summary>
    public static DateOnly DayOf(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(InZone(instant, zone).DateTime);

    /// <summary>The same instant, expressed with the zone's own offset.</summary>
    public static DateTimeOffset InZone(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return TimeZoneInfo.ConvertTime(instant, zone);
    }

    // A larger offset is an earlier instant for the same wall-clock reading.
    private static TimeSpan PickOffset(TimeSpan[] offsets, ClockEdge edge) =>
        edge == ClockEdge.Start ? offsets.Max() : offsets.Min();
}
