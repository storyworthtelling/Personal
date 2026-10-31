using System.Globalization;
using Constitution.Domain.Body;
using Constitution.Infrastructure.Backup;
using Constitution.Infrastructure.Vault;
using Storyworthtelling.Voice;

namespace Constitution.Tests.Backup;

// The fakes the Body backup tests stand on. Nothing here is Drive, a phone or a real reading: the
// vault envelope and its crypto are the real ones, run over an in-memory file, so only the network
// and the file-backed store are replaced. Every reading is invented.

/// <summary>
/// One account's Body file in memory. It counts what is done to it, can be told to fail, can change
/// the world under a call, and can store something other than what it was sent (a bad upload).
/// </summary>
internal sealed class BackupDrive : IDriveTextFile
{
    public string? Content { get; set; }

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    /// <summary>Runs after a read has taken its answer and before it returns: the world changing mid-call.</summary>
    public Action? AfterRead { get; set; }

    /// <summary>Given the 1-based write number and what was sent, returns what ends up stored.</summary>
    public Func<int, string, string>? OnWrite { get; set; }

    /// <summary>When set, every read waits for it after being counted.</summary>
    public TaskCompletionSource? HoldReads { get; set; }

    /// <summary>When true, a read fails from the moment a write has landed: the upload went, the check did not.</summary>
    public bool FailReadsAfterAWrite { get; set; }

    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;
        if (HoldReads is { } hold)
        {
            await hold.Task;
        }

        if (ReadFailure is not null)
        {
            throw ReadFailure;
        }

        if (FailReadsAfterAWrite && Writes > 0)
        {
            throw new HttpRequestException("the read after the write did not come back");
        }

        var content = Content;
        AfterRead?.Invoke();
        return content;
    }

    public Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WriteFailure is not null)
        {
            return Task.FromException(WriteFailure);
        }

        Writes++;
        Content = OnWrite is null ? content : OnWrite(Writes, content);
        return Task.CompletedTask;
    }
}

/// <summary>
/// An <see cref="IBodyStore"/> that lives in memory, standing in for the file-backed store. It
/// follows the interface's documented rules: one reading per signal per day with the later one
/// winning, and an install day that is recorded once and never moved.
/// </summary>
internal sealed class InMemoryBodyStore : IBodyStore
{
    private readonly Dictionary<(BodySignal Signal, DateOnly Day), double> readings = [];

    public bool IsEmpty => readings.Count == 0;

    public DateOnly? InstallDay { get; private set; }

    public int UpsertCalls { get; private set; }

    public int SetInstallDayCalls { get; private set; }

    public Exception? UpsertFailure { get; set; }

    public IReadOnlyList<BodyReading> All() =>
        readings
            .Select(pair => new BodyReading(pair.Key.Signal, pair.Key.Day, pair.Value))
            .OrderBy(reading => reading.Day)
            .ThenBy(reading => reading.Signal)
            .ToList();

    public void Upsert(IEnumerable<BodyReading> incoming)
    {
        UpsertCalls++;
        if (UpsertFailure is not null)
        {
            throw UpsertFailure;
        }

        foreach (var reading in incoming)
        {
            readings[(reading.Signal, reading.Day)] = reading.Value;
        }
    }

    public void SetInstallDay(DateOnly day)
    {
        SetInstallDayCalls++;
        InstallDay ??= day;
    }

    public int MergeCalls { get; private set; }

    public bool MergeRestored(DateOnly? installDay, IEnumerable<BodyReading> incoming)
    {
        MergeCalls++;
        if (UpsertFailure is not null)
        {
            throw UpsertFailure;
        }

        var changed = false;
        foreach (var reading in incoming)
        {
            if (readings.TryAdd((reading.Signal, reading.Day), reading.Value))
            {
                changed = true;
            }
        }

        if (installDay is { } day && (InstallDay is null || day < InstallDay))
        {
            InstallDay = day;
            changed = true;
        }

        return changed;
    }
}

internal sealed class RecordingLog : IConstitutionVaultLog
{
    public List<string> Lines { get; } = [];

    public void Outcome(string phrase) => Lines.Add(phrase);
}

/// <summary>The invented values every backup test uses. None resembles a real reading.</summary>
internal static class BackupInvented
{
    public const string Passphrase = "example-body-passphrase-one";
    public const string OtherPassphrase = "example-body-passphrase-two";

    public static readonly DateOnly InstallDay = new(2026, 11, 1);

    public static readonly DateOnly FirstDay = new(2026, 11, 2);

    /// <summary>A day's value for one signal, distinct for every (signal, day) so a mix-up shows.</summary>
    public static double ValueOf(BodySignal signal, int dayOffset) => 100 + ((int)signal * 10) + dayOffset + 0.25;

    /// <summary>Three days of every one of the nine signals.</summary>
    public static List<BodyReading> EverySignal(int days = 3) =>
        BodySignals.All
            .SelectMany(signal => Enumerable.Range(0, days)
                .Select(offset => new BodyReading(signal, FirstDay.AddDays(offset), ValueOf(signal, offset))))
            .ToList();

    public static BodyReading One(BodySignal signal, DateOnly day, double value) => new(signal, day, value);

    /// <summary>How a value reads in text, so a sweep can look for it.</summary>
    public static string Text(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Every invented value as text, for the sweeps that search output for any of them.</summary>
    public static IEnumerable<string> EveryValueText() =>
        EverySignal().Select(reading => Text(reading.Value)).Distinct();
}

/// <summary>One install: its store, its log and the backup over an account's Drive file.</summary>
internal sealed class BackupRig
{
    public BackupRig(BackupDrive? drive = null)
    {
        Drive = drive ?? new BackupDrive();
        Backup = new BodyBackup(Drive, Log);
    }

    public BackupDrive Drive { get; }

    public InMemoryBodyStore Store { get; } = new();

    public RecordingLog Log { get; } = new();

    public BodyBackup Backup { get; }

    /// <summary>Gives the store its history, as an install that has been reading for a while.</summary>
    public BackupRig WithHistory(IEnumerable<BodyReading>? readings = null, DateOnly? installDay = null)
    {
        Store.SetInstallDay(installDay ?? BackupInvented.InstallDay);
        Store.Upsert(readings ?? BackupInvented.EverySignal());
        return this;
    }

    public Task<BodyBackupResult> BackUp(string? passphrase = BackupInvented.Passphrase) =>
        Backup.BackupAsync(passphrase, Store, default);

    public Task<BodyRestoreResult> Restore(string? passphrase = BackupInvented.Passphrase) =>
        Backup.RestoreAsync(passphrase, Store, default);
}
