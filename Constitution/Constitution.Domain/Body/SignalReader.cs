namespace Constitution.Domain.Body;

/// <summary>How one signal's turn in a read ended.</summary>
public enum SignalReadStatus
{
    /// <summary>The platform has not given this install the signal, so nothing was asked of it.</summary>
    NotGranted,

    /// <summary>It was read and what came back was stored.</summary>
    Read,

    /// <summary>This platform cannot supply it.</summary>
    Unsupported,

    /// <summary>The local date is earlier than the install day (a clock set back): nothing is asked, nothing earlier than install is ever read.</summary>
    BeforeInstall,

    /// <summary>The platform refused the read.</summary>
    Denied,

    /// <summary>The read failed for another reason.</summary>
    Failed,
}

/// <summary>One signal's status and how many daily readings it stored.</summary>
public sealed record SignalReadEntry(BodySignal Signal, SignalReadStatus Status, int Readings);

/// <summary>What a read pass did, signal by signal. Counts and shapes only.</summary>
public sealed class SignalReadReport
{
    public SignalReadReport(IEnumerable<SignalReadEntry> entries)
    {
        Entries = entries.ToList();
    }

    public IReadOnlyList<SignalReadEntry> Entries { get; }

    public SignalReadStatus StatusOf(BodySignal signal) => Entries.First(entry => entry.Signal == signal).Status;

    public int Count(SignalReadStatus status) => Entries.Count(entry => entry.Status == status);

    public int ReadingsStored => Entries.Sum(entry => entry.Readings);
}

/// <summary>
/// Reads the signals Health Connect supplies into the Body store (C-7.1.2). Pure orchestration over
/// <see cref="IHealthRecordSource"/>, so it runs, and is tested, with no phone.
///
/// <para><b>From install day, never before.</b> The first pass records today as the install day. Every pass
/// after reads from the earlier of the last day it stored and <see cref="TrailingDays"/> back from today
/// (late-syncing data, a watch that uploads at nine, would otherwise stay undercounted for good), but never
/// before install day, and anything a source returns dated earlier than install day is dropped as well. A
/// clock set back behind install day reads nothing at all. No history permission is asked for, so there is
/// nothing earlier to read; this is the second lock on the same door.</para>
///
/// <para><b>The bed is not a retry loop.</b> The household login is shared, so a pass asks the bed about at
/// most <see cref="BedNightsPerPass"/> nights, the latest ones, stops at the first night it cannot answer, and
/// a bed that throws costs the watch's readings nothing. A person with a side chosen and no Health Connect
/// sleep grant still gets a sleep line, from the bed alone.</para>
///
/// <para><b>Granted only.</b> A signal the platform has not given this install is not asked for at all:
/// asking would either fail or, worse, look like a prompt the owner never saw. The permission request
/// belongs to the chart that opens (C-7.3.5), not to a reader.</para>
/// </summary>
public sealed class SignalReader
{
    /// <summary>Days a pass reads again behind today, so data that arrives late (a watch syncing at nine) is not undercounted for good.</summary>
    public const int TrailingDays = 3;

    /// <summary>The most nights a pass asks the bed about: the household login is shared, so a pass is not allowed to be a storm.</summary>
    public const int BedNightsPerPass = 14;

    private readonly IBodyStore store;
    private readonly IHealthRecordSource source;
    private readonly Func<DateOnly> today;
    private readonly IBodyLog log;
    private readonly ISleepGapFiller? sleepFiller;

    /// <param name="store">Where readings go.</param>
    /// <param name="source">Health Connect.</param>
    /// <param name="today">The local date, read each pass.</param>
    /// <param name="log">Outcome phrases.</param>
    /// <param name="sleepFiller">The bed, when a side is chosen; null reads the watch alone.</param>
    public SignalReader(IBodyStore store, IHealthRecordSource source, Func<DateOnly> today, IBodyLog log, ISleepGapFiller? sleepFiller)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(today);
        ArgumentNullException.ThrowIfNull(log);
        this.store = store;
        this.source = source;
        this.today = today;
        this.log = log;
        this.sleepFiller = sleepFiller;
    }

    /// <summary>Runs one pass over the seven signals.</summary>
    public async Task<SignalReadReport> ReadAsync(CancellationToken cancellationToken)
    {
        var day = today();
        if (store.InstallDay is null)
        {
            store.SetInstallDay(day);
            log.Outcome("body install day recorded");
        }

        var install = store.InstallDay ?? day;
        var lastStored = store.All()
            .GroupBy(reading => reading.Signal)
            .ToDictionary(group => group.Key, group => group.Max(reading => reading.Day));

        var entries = new List<SignalReadEntry>();
        foreach (var signal in BodySignals.HealthConnect)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (day < install)
            {
                entries.Add(new SignalReadEntry(signal, SignalReadStatus.BeforeInstall, 0));
                continue;
            }

            var from = FromFor(lastStored.TryGetValue(signal, out var last) ? last : null, install, day);
            if (!source.IsGranted(signal))
            {
                // A bed-only sleeper: the watch is not read, the bed is the whole of the line.
                entries.Add(signal == BodySignal.Sleep && sleepFiller is not null
                    ? await ReadBedAloneAsync(from, day, cancellationToken).ConfigureAwait(false)
                    : new SignalReadEntry(signal, SignalReadStatus.NotGranted, 0));
                continue;
            }

            entries.Add(await ReadOneAsync(signal, from, day, cancellationToken).ConfigureAwait(false));
        }

        var report = new SignalReadReport(entries);
        log.Outcome($"body read: {report.Count(SignalReadStatus.NotGranted)} not granted, {report.Count(SignalReadStatus.Read)} read, "
            + $"{report.Count(SignalReadStatus.Unsupported) + report.Count(SignalReadStatus.Denied) + report.Count(SignalReadStatus.Failed)} not read, "
            + $"{report.ReadingsStored} readings stored");
        return report;
    }

    // Install day when nothing is stored; otherwise the earlier of the last stored day and the trailing window, never before install day and never after today.
    private static DateOnly FromFor(DateOnly? lastStored, DateOnly install, DateOnly today)
    {
        if (lastStored is null)
        {
            return install;
        }

        var trailing = today.AddDays(-TrailingDays);
        var from = lastStored.Value < trailing ? lastStored.Value : trailing;
        return from < install ? install : from;
    }

    private async Task<SignalReadEntry> ReadBedAloneAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var readings = await BedNightsAsync([], from, to, cancellationToken).ConfigureAwait(false);
        if (readings.Count > 0)
        {
            store.Upsert(readings);
        }

        log.Outcome($"body read sleep: bed only, {readings.Count} {(readings.Count == 1 ? "reading" : "readings")}");
        return new SignalReadEntry(BodySignal.Sleep, SignalReadStatus.Read, readings.Count);
    }

    private async Task<SignalReadEntry> ReadOneAsync(
        BodySignal signal, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var read = await source.ReadAsync(signal, from, to, cancellationToken).ConfigureAwait(false);
        var id = BodySignals.Id(signal);
        if (read.Outcome != HealthReadOutcome.Ok)
        {
            log.Outcome($"body read {id}: {read.Outcome.ToString().ToLowerInvariant()}");
            return new SignalReadEntry(signal, StatusOf(read.Outcome), 0);
        }

        // The second lock on the same door. From is never before install day, and a pass keeps only the days it
        // asked for: a record that reaches back before the range (a night that began the evening before) comes
        // back as a partial reading of a day already stored whole, and must not replace it.
        var readings = DailyReadings.From(signal, read.Samples)
            .Where(reading => reading.Day >= from && reading.Day <= to)
            .ToList();

        if (signal == BodySignal.Sleep && sleepFiller is not null)
        {
            readings.AddRange(await BedNightsAsync(readings, from, to, cancellationToken).ConfigureAwait(false));
        }

        if (readings.Count > 0)
        {
            store.Upsert(readings);
        }

        log.Outcome($"body read {id}: ok, {readings.Count} {(readings.Count == 1 ? "reading" : "readings")}");
        return new SignalReadEntry(signal, SignalReadStatus.Read, readings.Count);
    }

    // The bed is asked about the latest nights of the range, newest first, at most BedNightsPerPass of them, and only where the
    // watch left a gap. The first night it cannot answer ends its turn: asking again would be one more failed
    // sign-in against a login the whole household shares.
    private async Task<List<BodyReading>> BedNightsAsync(
        IReadOnlyList<BodyReading> watch, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var filled = new List<BodyReading>();
        var watchByDay = watch.ToDictionary(reading => reading.Day, reading => reading.Value);
        var first = to.AddDays(1 - BedNightsPerPass);
        for (var night = to; night >= (first > from ? first : from); night = night.AddDays(-1))
        {
            if (sleepFiller!.GaveUp)
            {
                break;
            }

            double? watchHours = watchByDay.TryGetValue(night, out var hours) ? hours : null;
            double? fromBed;
            try
            {
                fromBed = await sleepFiller.BedHoursAsync(night, watchHours, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // The type only: the bed's message can echo what was asked. The watch's readings are kept either way.
                log.Outcome($"body bed sleep: failed ({error.GetType().Name})");
                break;
            }

            if (fromBed is { } bedHours && bedHours > 0)
            {
                filled.Add(new BodyReading(BodySignal.Sleep, night, bedHours));
            }
        }

        return filled;
    }

    private static SignalReadStatus StatusOf(HealthReadOutcome outcome) => outcome switch
    {
        HealthReadOutcome.Ok => SignalReadStatus.Read,
        HealthReadOutcome.Unsupported => SignalReadStatus.Unsupported,
        HealthReadOutcome.Denied => SignalReadStatus.Denied,
        _ => SignalReadStatus.Failed,
    };
}
