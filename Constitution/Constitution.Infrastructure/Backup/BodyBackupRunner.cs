using Constitution.Domain.Body;
using Constitution.Infrastructure.Vault;

namespace Constitution.Infrastructure.Backup;

/// <summary>How a runner call ended. Carries no value and no passphrase.</summary>
public enum BodyBackupRunStatus
{
    /// <summary>No vault passphrase has been entered, so Drive was not touched.</summary>
    NoPassphrase,

    /// <summary>No Google account is signed in on this install, so Drive was not touched.</summary>
    NotSignedIn,

    /// <summary>A restore ran; see <see cref="BodyBackupRunResult.Restore"/> for how it went.</summary>
    RestoreRan,

    /// <summary>A backup ran; see <see cref="BodyBackupRunResult.Backup"/> for how it went.</summary>
    BackupRan,

    /// <summary>Nothing is due: the quiet window has not passed, or nothing has changed since the last backup. Drive was not touched.</summary>
    NotDue,

    /// <summary>The sync that was reported did not read anything, so there is nothing to back up because of it.</summary>
    SyncDidNotRun,

    /// <summary>This process already settled its launch restore (restored, found the history here, or found no backup), so it did not ask again.</summary>
    AlreadyRestored,

    /// <summary>The run itself failed (not a backup outcome); <see cref="BodyBackupRunResult.Detail"/> names the failure's type.</summary>
    Failed,
}

/// <summary>When a backup after a sync is written.</summary>
public enum BodyBackupTiming
{
    /// <summary>Debounced: once the readings have been quiet for the debouncer's window. For the app, which stays open.</summary>
    WhenDue,

    /// <summary>At once, whatever is pending: for the overnight worker, which is gone when the run ends and has no later turn.</summary>
    Now,
}

/// <summary>
/// What a runner call did. It carries a status, the backup's own typed results and, when the run itself
/// failed, the failure's type name. It has nowhere to put a reading, a signal, a date or a passphrase.
/// </summary>
/// <param name="Status">How the call ended.</param>
/// <param name="Restore">How a restore went, when one ran.</param>
/// <param name="Backup">How a backup went, when one ran.</param>
/// <param name="Detail">When the run itself failed: the failure's type name, and nothing else.</param>
public sealed record BodyBackupRunResult(
    BodyBackupRunStatus Status,
    BodyRestoreResult? Restore = null,
    BodyBackupResult? Backup = null,
    string? Detail = null);

/// <summary>
/// A store's way of taking a restored history in one piece: the install day and every reading, but only
/// when the store holds no readings. Returns false, changing nothing, when it does. (The file-backed
/// store's <c>ReplaceAll</c> is one.)
/// </summary>
public delegate bool BodyReplaceAll(DateOnly? installDay, IEnumerable<BodyReading> readings);

/// <summary>
/// Runs <see cref="BodyBackup"/> the way the app needs it run, and nothing more: no backup logic is here.
/// It reads the passphrase and the sign-in when each call is made (never earlier, never kept), skips
/// quietly when either is missing, and never throws to its caller: every ending is a
/// <see cref="BodyBackupRunResult"/>, except the caller leaving, which propagates as cancellation.
///
/// <list type="bullet">
/// <item><b>Restore at launch</b> runs when the install has no readings, and once its answer is settled
/// (restored, history already here, no backup) it is not asked again by this process. A wrong
/// passphrase, an unreachable Drive and a failure are not settled and are tried at the next launch. A
/// store takes the history through <see cref="BodyReplaceAll"/>, because a fresh install records a
/// stand-in install day on first open that the backup's own must replace.</item>
/// <item><b>After a sync</b> that read something, a changed history is noted for the debouncer and the
/// backup is written when it is due (<see cref="BodyBackupTiming.WhenDue"/>) or at once
/// (<see cref="BodyBackupTiming.Now"/>). A change is noted even when there is no passphrase or sign-in
/// yet, so the backup follows once they arrive.</item>
/// </list>
///
/// <para>The seams are constructor arguments so the orchestration reads, and is tested, on its own; the
/// Android head wires them to the shared passphrase store, its Google account and Drive. Each call runs
/// on a thread-pool thread.</para>
///
/// <para>The log gets outcome phrases and a failure's type name, never a value or a message. The same
/// instance is meant to live for the process, so that its debouncer, its settled restore and its one
/// <see cref="BodyBackup"/> (whose calls are serialised) are shared by the app and the worker.</para>
/// </summary>
public sealed class BodyBackupRunner
{
    private readonly Func<string?> passphrase;
    private readonly Func<bool> signedIn;
    private readonly Func<BodyBackup> backupFactory;
    private readonly Func<DateTimeOffset> clock;
    private readonly BodyBackupDebouncer debouncer;
    private readonly IConstitutionVaultLog log;

    private readonly object gate = new();
    private BodyBackup? backup;
    private bool restoreSettled;
    private BodyHistory? lastNoted;

    /// <param name="passphrase">The vault passphrase, read live on each call and never kept.</param>
    /// <param name="signedIn">Whether a Google account is signed in on this install.</param>
    /// <param name="backupFactory">Builds the backup over this account's Drive file; called at most once, and not at all before a passphrase and a sign-in exist.</param>
    /// <param name="clock">The time, so the debouncer's sums are decided by the caller.</param>
    /// <param name="debouncer">When a backup is due; shared by every caller in the process.</param>
    /// <param name="log">Where outcome phrases go.</param>
    public BodyBackupRunner(
        Func<string?> passphrase,
        Func<bool> signedIn,
        Func<BodyBackup> backupFactory,
        Func<DateTimeOffset> clock,
        BodyBackupDebouncer debouncer,
        IConstitutionVaultLog log)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(signedIn);
        ArgumentNullException.ThrowIfNull(backupFactory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(debouncer);
        ArgumentNullException.ThrowIfNull(log);
        this.passphrase = passphrase;
        this.signedIn = signedIn;
        this.backupFactory = backupFactory;
        this.clock = clock;
        this.debouncer = debouncer;
        this.log = log;
    }

    /// <summary>
    /// Brings the backed-up history back onto an install that has none. Runs on a thread-pool thread.
    /// </summary>
    /// <param name="store">This install's Body store.</param>
    /// <param name="replaceAll">How the store takes a whole history in at once, only onto an empty store.</param>
    /// <param name="onRestored">Called once, after a restore that landed, so the Body page can draw what arrived. May be null.</param>
    /// <param name="cancellationToken">The caller leaving.</param>
    public Task<BodyBackupRunResult> RestoreAtLaunchAsync(
        IBodyStore store, BodyReplaceAll replaceAll, Action? onRestored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(replaceAll);
        return Task.Run(() => RunRestoreAsync(store, replaceAll, onRestored, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// What a finished sync calls: notes a changed history and backs it up when due. Runs on a thread-pool thread.
    /// </summary>
    /// <param name="sync">What the sync did; a sync that read nothing is left alone.</param>
    /// <param name="store">This install's Body store.</param>
    /// <param name="timing">Debounced for the app, at once for the worker.</param>
    /// <param name="cancellationToken">The caller leaving.</param>
    public Task<BodyBackupRunResult> AfterSyncAsync(
        BodySyncResult sync, IBodyStore store, BodyBackupTiming timing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(store);
        return Task.Run(() => RunAfterSyncAsync(sync, store, timing, cancellationToken), cancellationToken);
    }

    private async Task<BodyBackupRunResult> RunRestoreAsync(
        IBodyStore store, BodyReplaceAll replaceAll, Action? onRestored, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref restoreSettled))
        {
            return new BodyBackupRunResult(BodyBackupRunStatus.AlreadyRestored);
        }

        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
        {
            return new BodyBackupRunResult(BodyBackupRunStatus.NoPassphrase);
        }

        if (!signedIn())
        {
            log.Outcome("body restore skipped: not signed in");
            return new BodyBackupRunResult(BodyBackupRunStatus.NotSignedIn);
        }

        try
        {
            // An install with history is not even looked at, and Drive is not built for it.
            if (!store.IsEmpty)
            {
                Volatile.Write(ref restoreSettled, true);
                log.Outcome("body restore: skipped (history already here)");
                return new BodyBackupRunResult(
                    BodyBackupRunStatus.RestoreRan, Restore: new BodyRestoreResult(BodyRestoreOutcome.SkippedNotEmpty));
            }

            var target = new RestoreTarget(store, replaceAll);
            var result = await Backup().RestoreAsync(pass, target, cancellationToken);

            // The backup says it restored when it handed the history over; the store has the last word.
            if (result.Outcome == BodyRestoreOutcome.Restored && !target.Landed)
            {
                log.Outcome("body restore: skipped (history arrived first)");
                result = new BodyRestoreResult(BodyRestoreOutcome.SkippedNotEmpty);
            }

            if (result.Outcome is BodyRestoreOutcome.Restored or BodyRestoreOutcome.SkippedNotEmpty or BodyRestoreOutcome.NoBackup)
            {
                Volatile.Write(ref restoreSettled, true);
            }

            if (result.Outcome == BodyRestoreOutcome.Restored)
            {
                onRestored?.Invoke();
            }

            return new BodyBackupRunResult(BodyBackupRunStatus.RestoreRan, Restore: result);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Failed("restore", e);
        }
    }

    private async Task<BodyBackupRunResult> RunAfterSyncAsync(
        BodySyncResult sync, IBodyStore store, BodyBackupTiming timing, CancellationToken cancellationToken)
    {
        if (!sync.Ran || sync.Outcome is not (Domain.Diagnostics.DiagnosticsOutcome.Succeeded or Domain.Diagnostics.DiagnosticsOutcome.Partial))
        {
            return new BodyBackupRunResult(BodyBackupRunStatus.SyncDidNotRun);
        }

        try
        {
            // Noted before the passphrase and the sign-in are asked about: a change made while they are
            // missing is still a change, and is backed up at the first sync after they arrive.
            NoteChange(store, clock());

            var pass = passphrase();
            if (string.IsNullOrEmpty(pass))
            {
                return new BodyBackupRunResult(BodyBackupRunStatus.NoPassphrase);
            }

            if (!signedIn())
            {
                log.Outcome("body backup skipped: not signed in");
                return new BodyBackupRunResult(BodyBackupRunStatus.NotSignedIn);
            }

            var now = clock();
            var backupNow = Backup();
            var result = timing == BodyBackupTiming.Now
                ? await backupNow.FlushAsync(pass, store, debouncer, now, cancellationToken)
                : await backupNow.BackupWhenDueAsync(pass, store, debouncer, now, cancellationToken);
            return result is null
                ? new BodyBackupRunResult(BodyBackupRunStatus.NotDue)
                : new BodyBackupRunResult(BodyBackupRunStatus.BackupRan, Backup: result);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Failed("backup", e);
        }
    }

    // A sync that stored the same history as the last one noted says nothing new, and must not push the
    // quiet window out every time the app is opened.
    private void NoteChange(IBodyStore store, DateTimeOffset now)
    {
        var history = BodyHistory.From(store);
        lock (gate)
        {
            if (history.IsEmpty || (lastNoted is not null && history.HasSameContentAs(lastNoted)))
            {
                return;
            }

            lastNoted = history;
        }

        debouncer.Changed(now);
    }

    private BodyBackup Backup()
    {
        lock (gate)
        {
            return backup ??= backupFactory();
        }
    }

    // The type name only: a message from a Drive, crypto or storage failure is not ours to vouch for as
    // free of a value or a passphrase.
    private BodyBackupRunResult Failed(string verb, Exception e)
    {
        var type = e.GetType().Name;
        log.Outcome($"body {verb} failed: {type}");
        return new BodyBackupRunResult(BodyBackupRunStatus.Failed, Detail: type);
    }

    // What the backup restores into. It reads through to the real store, but takes the history in one
    // piece through the store's own replace, so the backup's install day replaces the stand-in a fresh
    // install recorded on first open, and records whether the store accepted it.
    private sealed class RestoreTarget : IBodyStore
    {
        private readonly IBodyStore inner;
        private readonly BodyReplaceAll replaceAll;
        private DateOnly? installDay;

        public RestoreTarget(IBodyStore inner, BodyReplaceAll replaceAll)
        {
            this.inner = inner;
            this.replaceAll = replaceAll;
        }

        public bool Landed { get; private set; }

        public bool IsEmpty => inner.IsEmpty;

        public DateOnly? InstallDay => inner.InstallDay;

        public IReadOnlyList<BodyReading> All() => inner.All();

        public void SetInstallDay(DateOnly day) => installDay = day;

        public void Upsert(IEnumerable<BodyReading> readings) => Landed = replaceAll(installDay, readings.ToList());
    }
}
