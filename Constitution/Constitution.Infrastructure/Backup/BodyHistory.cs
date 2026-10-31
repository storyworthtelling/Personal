using Constitution.Domain.Body;

namespace Constitution.Infrastructure.Backup;

/// <summary>
/// A person's Body history as a value: the install day and every reading, one per signal per day, in
/// a fixed order. It is what the backup copies out of a store, merges with what Drive already holds,
/// and writes; it is the only Body type outside the Domain, and it lives in this folder because the
/// private backup is the only place Body is allowed to go (D98, <c>BodyStaysHomeTests</c>).
///
/// <para>Immutable and normalised on the way in, so two histories that say the same thing compare as
/// the same whatever order their readings arrived in, which is what lets a backup that would write
/// nothing new notice that and write nothing.</para>
/// </summary>
public sealed class BodyHistory
{
    private static readonly Dictionary<BodySignal, int> SignalRank = BodySignals.All
        .Select((signal, index) => (signal, index))
        .ToDictionary(pair => pair.signal, pair => pair.index);

    private BodyHistory(DateOnly? installDay, IReadOnlyList<BodyReading> readings)
    {
        InstallDay = installDay;
        Readings = readings;
    }

    /// <summary>No readings and no install day.</summary>
    public static BodyHistory Empty { get; } = new(null, []);

    /// <summary>The day Body started reading, or null when it is not known.</summary>
    public DateOnly? InstallDay { get; }

    /// <summary>Every reading, signals in <see cref="BodySignals.All"/> order and each signal's days oldest first.</summary>
    public IReadOnlyList<BodyReading> Readings { get; }

    /// <summary>True when there is not one reading.</summary>
    public bool IsEmpty => Readings.Count == 0;

    /// <summary>
    /// Builds a history. Of two readings of one signal on one day the later in <paramref name="readings"/>
    /// wins, as in a store. A reading that is not a finite number is left out: JSON cannot hold one, and a
    /// value that cannot be charted is not one worth wedging every backup over.
    /// </summary>
    public static BodyHistory Create(DateOnly? installDay, IEnumerable<BodyReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var byKey = new Dictionary<(BodySignal, DateOnly), BodyReading>();
        foreach (var reading in readings)
        {
            if (double.IsFinite(reading.Value))
            {
                byKey[(reading.Signal, reading.Day)] = reading;
            }
        }

        var ordered = byKey.Values
            .OrderBy(reading => SignalRank.GetValueOrDefault(reading.Signal, int.MaxValue))
            .ThenBy(reading => reading.Day)
            .ToList();
        return new BodyHistory(installDay, ordered);
    }

    /// <summary>A snapshot of everything a store holds.</summary>
    public static BodyHistory From(IBodyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return Create(store.InstallDay, store.All());
    }

    /// <summary>
    /// Every reading from both histories, with <paramref name="newer"/>'s winning on a day both hold, and
    /// the earlier of the two install days. Neither side is ever dropped from the other: merging is how
    /// a smaller history can be added to a larger backup without shrinking it.
    /// </summary>
    public BodyHistory MergedWith(BodyHistory newer)
    {
        ArgumentNullException.ThrowIfNull(newer);

        DateOnly? day = (InstallDay, newer.InstallDay) switch
        {
            (null, var other) => other,
            (var own, null) => own,
            (var own, var other) => own <= other ? own : other,
        };
        return Create(day, Readings.Concat(newer.Readings));
    }

    /// <summary>Whether the two say exactly the same thing: the same install day and the same readings.</summary>
    public bool HasSameContentAs(BodyHistory other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return InstallDay == other.InstallDay && Readings.SequenceEqual(other.Readings);
    }
}
