using Index.Domain.Household;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Infrastructure.Snowball;
using Microsoft.Extensions.Logging;

namespace Index.Tests.Support;

/// <summary>A scripted history source: each read takes the next scripted result, or returns the synthetic
/// history once the script runs out. Counts reads and remembers the passphrases it was handed.</summary>
public sealed class FakeSnowballHistorySource : ISnowballHistorySource
{
    private readonly Queue<Func<SnowballHistoryReadResult>> script = new();

    public int Reads { get; private set; }

    public List<string?> Passphrases { get; } = [];

    public FakeSnowballHistorySource Then(SnowballHistoryReadResult result)
    {
        script.Enqueue(() => result);
        return this;
    }

    public FakeSnowballHistorySource ThenThrow(Exception exception)
    {
        script.Enqueue(() => throw exception);
        return this;
    }

    public Task<SnowballHistoryReadResult> ReadAsync(string? passphrase, CancellationToken cancellationToken)
    {
        Reads++;
        Passphrases.Add(passphrase);
        var next = script.Count > 0 ? script.Dequeue() : () => SyntheticLoaded();
        return Task.FromResult(next());
    }

    public static SnowballHistoryReadResult SyntheticLoaded()
    {
        var parsed = SnowballHistoryCodec.Parse(SyntheticSnowballHistory.Json);
        return SnowballHistoryReadResult.Loaded(parsed.Data!);
    }
}

public sealed class FakeImportMemory : ISnowballHistoryImportMemory
{
    public bool Imported { get; private set; }

    public int MarkCalls { get; private set; }

    public void MarkImported()
    {
        Imported = true;
        MarkCalls++;
    }
}

/// <summary>Collects every message a logger is given, so a test can prove none of them carries a secret.</summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception.Message)));
    }
}

/// <summary>Builds a <see cref="SnowballService"/> wired to a fake history source, the way the app wires
/// the real one.</summary>
public sealed class HistoryImportHarness
{
    public const string Passphrase = "synthetic-passphrase-for-tests";

    public HistoryImportHarness(TestDatabase db, DateTimeOffset now, InstallRole role = InstallRole.Owner, string? passphrase = Passphrase)
    {
        Clock = new FakeClock(now);
        Source = new FakeSnowballHistorySource();
        Memory = new FakeImportMemory();
        Logger = new RecordingLogger<SnowballService>();
        CurrentRole = role;
        CurrentPassphrase = passphrase;
        Backoff = new SnowballHistoryBackoff();
        Service = new SnowballService(
            db, Clock,
            new SnowballHistoryImport(Source, () => CurrentPassphrase, () => CurrentRole, Memory, Backoff),
            Logger);
    }

    /// <summary>An import over the synthetic history for an Owner install holding the test passphrase — for
    /// tests that need loan history in the database but are not about the import itself.</summary>
    public static SnowballHistoryImport NewImport() =>
        new(new FakeSnowballHistorySource(), () => Passphrase, () => InstallRole.Owner, new FakeImportMemory());

    public FakeClock Clock { get; }

    public FakeSnowballHistorySource Source { get; }

    public FakeImportMemory Memory { get; }

    public RecordingLogger<SnowballService> Logger { get; }

    public SnowballHistoryBackoff Backoff { get; }

    public SnowballService Service { get; }

    public InstallRole CurrentRole { get; set; }

    public string? CurrentPassphrase { get; set; }
}
