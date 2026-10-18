using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

/// <summary>An in-memory Body store: enough of the seam for the reader and the sync to run against.</summary>
internal sealed class MemoryBodyStore : IBodyStore
{
    private readonly Dictionary<(BodySignal, DateOnly), double> readings = [];

    public bool IsEmpty => readings.Count == 0;

    public DateOnly? InstallDay { get; private set; }

    public int SetInstallDayCalls { get; private set; }

    public IReadOnlyList<BodyReading> All() =>
        readings.OrderBy(pair => pair.Key.Item2).ThenBy(pair => pair.Key.Item1)
            .Select(pair => new BodyReading(pair.Key.Item1, pair.Key.Item2, pair.Value)).ToList();

    public void Upsert(IEnumerable<BodyReading> toStore)
    {
        foreach (var reading in toStore)
        {
            readings[(reading.Signal, reading.Day)] = reading.Value;
        }
    }

    public void SetInstallDay(DateOnly day)
    {
        SetInstallDayCalls++;
        InstallDay ??= day;
    }
}

/// <summary>A Health Connect stand-in: which signals are granted, what each read returns, and what was asked of it.</summary>
internal sealed class FakeHealthSource : IHealthRecordSource
{
    private readonly Dictionary<BodySignal, HealthRead> answers = [];

    public HashSet<BodySignal> Granted { get; } = [];

    public bool BackgroundReadsGranted { get; set; }

    /// <summary>Every read asked for, in order: the signal and the days it was asked to cover.</summary>
    public List<(BodySignal Signal, DateOnly From, DateOnly To)> Asked { get; } = [];

    public FakeHealthSource Grant(BodySignal signal, params HealthSample[] samples)
    {
        Granted.Add(signal);
        answers[signal] = HealthRead.Ok(samples);
        return this;
    }

    public FakeHealthSource Answer(BodySignal signal, HealthRead read)
    {
        Granted.Add(signal);
        answers[signal] = read;
        return this;
    }

    public bool IsGranted(BodySignal signal) => Granted.Contains(signal);

    public Task<HealthRead> ReadAsync(BodySignal signal, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Asked.Add((signal, from, to));
        return Task.FromResult(answers.TryGetValue(signal, out var read) ? read : HealthRead.Ok([]));
    }
}

internal sealed class CapturingBodyLog : IBodyLog
{
    public List<string> Lines { get; } = [];

    public void Outcome(string phrase) => Lines.Add(phrase);
}

internal sealed class CapturingSyncStatus : IBodySyncStatus
{
    public List<BodySyncResult> Recorded { get; } = [];

    public void Record(BodySyncResult result) => Recorded.Add(result);
}

internal sealed class FakeSleepFiller : ISleepGapFiller
{
    public Dictionary<DateOnly, double> Nights { get; } = [];

    public List<(DateOnly Day, double? Watch)> Asked { get; } = [];

    public Task<double?> BedHoursAsync(DateOnly day, double? watchHours, CancellationToken cancellationToken)
    {
        Asked.Add((day, watchHours));
        return Task.FromResult<double?>(Nights.TryGetValue(day, out var hours) && !(watchHours > 0) ? hours : null);
    }
}

/// <summary>Invented samples: made-up numbers on a made-up week, nobody's real ones.</summary>
internal static class Samples
{
    public static readonly DateOnly Today = new(2026, 11, 17);

    public static HealthSample Of(DateOnly day, double amount, string origin = "example.watch") => new(day, amount, origin);
}
