using System.Text.RegularExpressions;
using Constitution.Domain.Body;
using Constitution.Infrastructure.Backup;
using Constitution.Tests.Body;

namespace Constitution.Tests.Backup;

/// <summary>
/// The orchestration that puts the Body backup into the app (C-7.4.1 wiring): when to restore, when to
/// back up, and what to say when it cannot. Run over the real <see cref="BodyBackup"/> and the real
/// vault envelope, with an in-memory Drive file standing in for the network. Every reading is invented.
/// </summary>
public class BodyBackupRunnerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>One install wired to the runner through its seams, with every call counted.</summary>
    private sealed class Rig
    {
        public Rig(BackupDrive? drive = null)
        {
            Install = new BackupRig(drive);
            Runner = new BodyBackupRunner(
                () =>
                {
                    PassphraseReads++;
                    return Passphrase;
                },
                () => SignedIn,
                () =>
                {
                    FactoryCalls++;
                    return Factory?.Invoke() ?? Install.Backup;
                },
                () => Now,
                Debouncer,
                Install.Log);
        }

        public BackupRig Install { get; }

        public BodyBackupRunner Runner { get; }

        public BodyBackupDebouncer Debouncer { get; } = new();

        public string? Passphrase { get; set; } = BackupInvented.Passphrase;

        public bool SignedIn { get; set; } = true;

        public DateTimeOffset Now { get; set; } = Start;

        public Func<BodyBackup>? Factory { get; set; }

        public int PassphraseReads { get; private set; }

        public int FactoryCalls { get; private set; }

        public int Refreshes { get; private set; }

        public void Refresh() => Refreshes++;

        public BackupDrive Drive => Install.Drive;

        public InMemoryBodyStore Store => Install.Store;

        public Task<BodyBackupRunResult> Restore(CancellationToken cancellationToken = default) =>
            Runner.RestoreAtLaunchAsync(Store, Refresh, cancellationToken);

        public Task<BodyBackupRunResult> AfterSync(BodySyncResult? sync = null, BodyBackupTiming timing = BodyBackupTiming.WhenDue) =>
            Runner.AfterSyncAsync(sync ?? GoodSync(), Store, timing, default);
    }

    private static BodySyncResult GoodSync() =>
        new(BodySyncTrigger.AppOpen, true, BodySyncGap.None,
            new SignalReadReport([new SignalReadEntry(BodySignal.Weight, SignalReadStatus.Read, 2)]));

    /// <summary>A backup already sitting in Drive, written by an earlier install of the same account.</summary>
    private static async Task<BackupDrive> DriveHoldingABackup(string passphrase = BackupInvented.Passphrase)
    {
        var earlier = new BackupRig().WithHistory();
        var written = await earlier.BackUp(passphrase);
        Assert.Equal(BodyBackupOutcome.BackedUp, written.Outcome);
        return earlier.Drive;
    }

    // ---- no passphrase, no sign-in: nothing leaves ----

    [Fact]
    public async Task NoPassphraseMakesNoDriveCallOnRestore()
    {
        var rig = new Rig { Passphrase = null };

        var result = await rig.Restore();

        Assert.Equal(BodyBackupRunStatus.NoPassphrase, result.Status);
        Assert.Equal(0, rig.FactoryCalls);
        Assert.Equal(0, rig.Drive.Reads);
        Assert.Equal(0, rig.Drive.Writes);
        Assert.Equal(0, rig.Refreshes);
    }

    [Fact]
    public async Task AnEmptyPassphraseIsNoPassphrase()
    {
        var rig = new Rig { Passphrase = string.Empty };

        Assert.Equal(BodyBackupRunStatus.NoPassphrase, (await rig.Restore()).Status);
        Assert.Equal(0, rig.Drive.Reads);
    }

    [Fact]
    public async Task NotSignedInMakesNoDriveCallOnRestore()
    {
        var rig = new Rig { SignedIn = false };

        var result = await rig.Restore();

        Assert.Equal(BodyBackupRunStatus.NotSignedIn, result.Status);
        Assert.Equal(0, rig.FactoryCalls);
        Assert.Equal(0, rig.Drive.Reads);
        Assert.Equal(0, rig.Drive.Writes);
    }

    [Fact]
    public async Task NoPassphraseMakesNoDriveCallAfterASync()
    {
        var rig = new Rig { Passphrase = null };
        rig.Install.WithHistory();
        rig.Now = Start.AddHours(1);

        var result = await rig.AfterSync(timing: BodyBackupTiming.Now);

        Assert.Equal(BodyBackupRunStatus.NoPassphrase, result.Status);
        Assert.Equal(0, rig.FactoryCalls);
        Assert.Equal(0, rig.Drive.Reads);
        Assert.Equal(0, rig.Drive.Writes);
    }

    [Fact]
    public async Task NotSignedInMakesNoDriveCallAfterASync()
    {
        var rig = new Rig { SignedIn = false };
        rig.Install.WithHistory();

        var result = await rig.AfterSync(timing: BodyBackupTiming.Now);

        Assert.Equal(BodyBackupRunStatus.NotSignedIn, result.Status);
        Assert.Equal(0, rig.Drive.Reads);
        Assert.Equal(0, rig.Drive.Writes);
    }

    [Fact]
    public async Task AChangeStaysPendingUntilAPassphraseArrives()
    {
        var rig = new Rig { Passphrase = null };
        rig.Install.WithHistory();

        await rig.AfterSync();
        Assert.True(rig.Debouncer.HasPending);

        // The owner types the passphrase; the next sync, after the quiet window, writes the backup.
        rig.Passphrase = BackupInvented.Passphrase;
        rig.Now = Start.AddMinutes(3);
        var result = await rig.AfterSync();

        Assert.Equal(BodyBackupRunStatus.BackupRan, result.Status);
        Assert.Equal(BodyBackupOutcome.BackedUp, result.Backup!.Outcome);
    }

    [Fact]
    public async Task ThePassphraseIsReadOnEveryCallAndNeverKept()
    {
        var rig = new Rig { Passphrase = null };

        await rig.Restore();
        var readsAfterFirst = rig.PassphraseReads;
        rig.Passphrase = BackupInvented.Passphrase;
        var second = await rig.Restore();

        Assert.True(readsAfterFirst >= 1);
        Assert.True(rig.PassphraseReads > readsAfterFirst);
        Assert.Equal(BodyBackupRunStatus.RestoreRan, second.Status);
    }

    // ---- restore at launch ----

    [Fact]
    public async Task RestoreFillsAnEmptyStoreAndRefreshesOnce()
    {
        var rig = new Rig(await DriveHoldingABackup());
        var writesBefore = rig.Drive.Writes;

        var result = await rig.Restore();

        Assert.Equal(BodyBackupRunStatus.RestoreRan, result.Status);
        Assert.Equal(BodyRestoreOutcome.Restored, result.Restore!.Outcome);
        Assert.Equal(BackupInvented.EverySignal().Count, rig.Store.All().Count);
        Assert.Equal(1, rig.Refreshes);
        Assert.Equal(writesBefore, rig.Drive.Writes);
    }

    [Fact]
    public async Task RestoreMergesIntoAStoreThatHasAlreadyRead()
    {
        // Was RestoreLeavesAStoreWithHistoryAlone (see BodyBackupTests.RestoreMergesWithoutOverwritingLocalDays for why).
        var rig = new Rig(await DriveHoldingABackup());
        rig.Install.WithHistory([BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay, 1.5)]);
        var writesBefore = rig.Drive.Writes;

        var result = await rig.Restore();

        Assert.Equal(BodyBackupRunStatus.RestoreRan, result.Status);
        Assert.Equal(BodyRestoreOutcome.Restored, result.Restore!.Outcome);
        Assert.Equal(BackupInvented.EverySignal().Count, rig.Store.All().Count);
        Assert.Equal(1.5, rig.Store.All().Single(reading => reading.Signal == BodySignal.Weight && reading.Day == BackupInvented.FirstDay).Value);
        Assert.Equal(1, rig.Refreshes);
        Assert.Equal(writesBefore, rig.Drive.Writes);
    }

    [Fact]
    public async Task RestoreWithAnIdenticalHistoryAddsNothingAndIsSettled()
    {
        var rig = new Rig(await DriveHoldingABackup());
        rig.Install.WithHistory();
        var readsBefore = rig.Drive.Reads;

        var first = await rig.Restore();
        var second = await rig.Restore();

        Assert.Equal(BodyRestoreOutcome.NothingNew, first.Restore!.Outcome);
        Assert.Equal(readsBefore + 1, rig.Drive.Reads);
        Assert.Equal(BodyBackupRunStatus.AlreadyRestored, second.Status);
        Assert.Equal(0, rig.Refreshes);
    }

    [Fact]
    public async Task RestoreTakesTheBackupsInstallDayOverAStandIn()
    {
        var path = Path.Combine(Path.GetTempPath(), "constitution-runner-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var rig = new Rig(await DriveHoldingABackup());
            var store = new Constitution.Infrastructure.Body.JsonBodyStore(path);

            // A fresh install records today as its install day on first open, before any restore.
            store.SetInstallDay(new DateOnly(2026, 11, 9));
            Assert.True(store.IsEmpty);

            var result = await rig.Runner.RestoreAtLaunchAsync(store, rig.Refresh, default);

            Assert.Equal(BodyRestoreOutcome.Restored, result.Restore!.Outcome);
            Assert.Equal(BackupInvented.InstallDay, store.InstallDay);
            Assert.Equal(BackupInvented.EverySignal().Count, store.All().Count);
            Assert.Equal(1, rig.Refreshes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ARestoreIntoARealStoreThatHasReadKeepsTheEarlierInstallDayAndLocalDays()
    {
        // The reader's first pass records today as the install day with no readings, then stores some: the store is
        // dated and not empty by the time the passphrase is typed. The real file-backed store, through the runner.
        var path = Path.Combine(Path.GetTempPath(), "constitution-runner-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var rig = new Rig(await DriveHoldingABackup());
            var store = new Constitution.Infrastructure.Body.JsonBodyStore(path);
            store.SetInstallDay(new DateOnly(2026, 11, 9));
            store.Upsert([BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay, 2.5)]);

            var result = await rig.Runner.RestoreAtLaunchAsync(store, rig.Refresh, default);

            Assert.Equal(BodyRestoreOutcome.Restored, result.Restore!.Outcome);
            Assert.Equal(BackupInvented.InstallDay, store.InstallDay);
            Assert.Equal(2.5, store.All().Single(reading => reading.Signal == BodySignal.Weight && reading.Day == BackupInvented.FirstDay).Value);
            Assert.Equal(BackupInvented.EverySignal().Count, store.All().Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ALaterBackupInstallDayNeverMovesAnEarlierOneForward()
    {
        var path = Path.Combine(Path.GetTempPath(), "constitution-runner-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var rig = new Rig(await DriveHoldingABackup());
            var store = new Constitution.Infrastructure.Body.JsonBodyStore(path);
            var earlier = BackupInvented.InstallDay.AddDays(-20);
            store.SetInstallDay(earlier);

            await rig.Runner.RestoreAtLaunchAsync(store, rig.Refresh, default);

            Assert.Equal(earlier, store.InstallDay);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ARestoreThatAddsNothingDoesNotRefresh()
    {
        var rig = new Rig(await DriveHoldingABackup());
        rig.Install.WithHistory();

        var result = await rig.Restore();

        Assert.Equal(BodyBackupRunStatus.RestoreRan, result.Status);
        Assert.Equal(BodyRestoreOutcome.NothingNew, result.Restore!.Outcome);
        Assert.Equal(0, rig.Refreshes);
    }

    [Fact]
    public async Task RestoreRunsOnceOnceItIsSettled()
    {
        var rig = new Rig(await DriveHoldingABackup());
        await rig.Restore();
        var readsAfterFirst = rig.Drive.Reads;

        var again = await rig.Restore();

        Assert.Equal(BodyBackupRunStatus.AlreadyRestored, again.Status);
        Assert.Equal(readsAfterFirst, rig.Drive.Reads);
        Assert.Equal(1, rig.Refreshes);
    }

    [Fact]
    public async Task NoBackupYetIsSettledToo()
    {
        var rig = new Rig();

        var first = await rig.Restore();
        var second = await rig.Restore();

        Assert.Equal(BodyRestoreOutcome.NoBackup, first.Restore!.Outcome);
        Assert.Equal(BodyBackupRunStatus.AlreadyRestored, second.Status);
        Assert.Equal(0, rig.Refreshes);
    }

    [Fact]
    public async Task AnUnreachableDriveIsTriedAgainNextLaunch()
    {
        var drive = await DriveHoldingABackup();
        drive.ReadFailure = new HttpRequestException("no network");
        var rig = new Rig(drive);

        var first = await rig.Restore();
        drive.ReadFailure = null;
        var second = await rig.Restore();

        Assert.Equal(BodyRestoreOutcome.Unreachable, first.Restore!.Outcome);
        Assert.Equal(BodyRestoreOutcome.Restored, second.Restore!.Outcome);
        Assert.Equal(1, rig.Refreshes);
    }

    [Fact]
    public async Task AWrongPassphraseIsTypedAndTriedAgainWithTheRightOne()
    {
        var rig = new Rig(await DriveHoldingABackup(BackupInvented.OtherPassphrase));

        var first = await rig.Restore();
        Assert.Equal(BodyRestoreOutcome.WrongPassphrase, first.Restore!.Outcome);
        Assert.True(first.Restore.NeedsAttention);
        Assert.Equal(0, rig.Refreshes);

        rig.Passphrase = BackupInvented.OtherPassphrase;
        var second = await rig.Restore();

        Assert.Equal(BodyRestoreOutcome.Restored, second.Restore!.Outcome);
        Assert.Equal(1, rig.Refreshes);
    }

    // ---- after a sync ----

    [Fact]
    public async Task ASyncIsBackedUpOnlyOnceTheQuietWindowHasPassed()
    {
        var rig = new Rig();
        rig.Install.WithHistory();

        var early = await rig.AfterSync();
        Assert.Equal(BodyBackupRunStatus.NotDue, early.Status);
        Assert.Equal(0, rig.Drive.Reads);
        Assert.Equal(0, rig.Drive.Writes);

        rig.Now = Start + BodyBackupDebouncer.QuietWindow - TimeSpan.FromSeconds(1);
        Assert.Equal(BodyBackupRunStatus.NotDue, (await rig.AfterSync()).Status);

        rig.Now = Start + BodyBackupDebouncer.QuietWindow + TimeSpan.FromSeconds(1);
        var due = await rig.AfterSync();

        Assert.Equal(BodyBackupRunStatus.BackupRan, due.Status);
        Assert.Equal(BodyBackupOutcome.BackedUp, due.Backup!.Outcome);
        Assert.Equal(1, rig.Drive.Writes);
    }

    [Fact]
    public async Task ABurstOfSyncsIsOneWrite()
    {
        var rig = new Rig();
        rig.Install.WithHistory();

        await rig.AfterSync();
        rig.Now = Start.AddMinutes(3);
        await rig.AfterSync();
        await rig.AfterSync();
        await rig.AfterSync();

        Assert.Equal(1, rig.Drive.Writes);
    }

    [Fact]
    public async Task ASyncThatChangedNothingIsNotBackedUpAgain()
    {
        var rig = new Rig();
        rig.Install.WithHistory();
        await rig.AfterSync();
        rig.Now = Start.AddMinutes(3);
        await rig.AfterSync();
        var readsAfterBackup = rig.Drive.Reads;

        rig.Now = Start.AddHours(2);
        var again = await rig.AfterSync();

        Assert.Equal(BodyBackupRunStatus.NotDue, again.Status);
        Assert.Equal(readsAfterBackup, rig.Drive.Reads);
    }

    [Fact]
    public async Task ANewReadingAfterABackupMakesTheNextOneDue()
    {
        var rig = new Rig();
        rig.Install.WithHistory();
        await rig.AfterSync();
        rig.Now = Start.AddMinutes(3);
        await rig.AfterSync();

        rig.Store.Upsert([BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay.AddDays(10), 77.5)]);
        rig.Now = Start.AddHours(2);
        await rig.AfterSync();
        rig.Now = Start.AddHours(2).AddMinutes(3);
        var result = await rig.AfterSync();

        Assert.Equal(BodyBackupOutcome.BackedUp, result.Backup!.Outcome);
        Assert.Equal(2, rig.Drive.Writes);
    }

    [Fact]
    public async Task AFailedSyncMakesNoBackup()
    {
        var rig = new Rig();
        rig.Install.WithHistory();
        var notRun = new BodySyncResult(BodySyncTrigger.Worker, false, BodySyncGap.BackgroundNotGranted, null);
        var refused = new BodySyncResult(BodySyncTrigger.AppOpen, true, BodySyncGap.None,
            new SignalReadReport([new SignalReadEntry(BodySignal.Weight, SignalReadStatus.Denied, 0)]));
        rig.Now = Start.AddHours(1);

        Assert.Equal(BodyBackupRunStatus.SyncDidNotRun, (await rig.AfterSync(notRun, BodyBackupTiming.Now)).Status);
        Assert.Equal(BodyBackupRunStatus.SyncDidNotRun, (await rig.AfterSync(refused, BodyBackupTiming.Now)).Status);
        Assert.Equal(0, rig.FactoryCalls);
        Assert.Equal(0, rig.Drive.Reads);
        Assert.False(rig.Debouncer.HasPending);
    }

    [Fact]
    public async Task TheWorkerBacksUpAtOnce()
    {
        var rig = new Rig();
        rig.Install.WithHistory();

        var result = await rig.AfterSync(timing: BodyBackupTiming.Now);

        Assert.Equal(BodyBackupRunStatus.BackupRan, result.Status);
        Assert.Equal(BodyBackupOutcome.BackedUp, result.Backup!.Outcome);
        Assert.Equal(1, rig.Drive.Writes);
    }

    [Fact]
    public async Task AnUnreadableFileIsRefusedAndTyped()
    {
        var rig = new Rig(await DriveHoldingABackup(BackupInvented.OtherPassphrase));
        rig.Install.WithHistory();
        var before = rig.Drive.Content;
        var writesBefore = rig.Drive.Writes;

        var result = await rig.AfterSync(timing: BodyBackupTiming.Now);

        Assert.Equal(BodyBackupRunStatus.BackupRan, result.Status);
        Assert.Equal(BodyBackupOutcome.RefusedUnreadable, result.Backup!.Outcome);
        Assert.True(result.Backup.NeedsAttention);
        Assert.Equal(writesBefore, rig.Drive.Writes);
        Assert.Equal(before, rig.Drive.Content);
    }

    [Fact]
    public async Task AnUnreachableDriveKeepsTheChangePending()
    {
        var rig = new Rig();
        rig.Install.WithHistory();
        rig.Drive.ReadFailure = new HttpRequestException("no network");

        var result = await rig.AfterSync(timing: BodyBackupTiming.Now);

        Assert.Equal(BodyBackupOutcome.Unreachable, result.Backup!.Outcome);
        Assert.True(rig.Debouncer.HasPending);
    }

    // ---- never throws to the screen ----

    [Fact]
    public async Task AFailureIsTypedByItsTypeNameOnly()
    {
        const string marker = "marker-text-that-must-not-travel";
        var rig = new Rig { Factory = () => throw new InvalidOperationException(marker) };

        var restore = await rig.Restore();
        rig.Install.WithHistory();
        var backup = await rig.AfterSync(timing: BodyBackupTiming.Now);

        Assert.Equal(BodyBackupRunStatus.Failed, restore.Status);
        Assert.Equal(nameof(InvalidOperationException), restore.Detail);
        Assert.Equal(BodyBackupRunStatus.Failed, backup.Status);
        Assert.Equal(nameof(InvalidOperationException), backup.Detail);
        Assert.DoesNotContain(rig.Install.Log.Lines, line => line.Contains(marker, StringComparison.Ordinal));
        Assert.Contains(rig.Install.Log.Lines, line => line.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStoreThatThrowsDuringRestoreIsAFailureNotACrash()
    {
        var rig = new Rig(await DriveHoldingABackup());
        rig.Store.UpsertFailure = new IOException("disk gone");

        var result = await rig.Restore();

        Assert.Equal(BodyBackupRunStatus.Failed, result.Status);
        Assert.Equal(nameof(IOException), result.Detail);
        Assert.Equal(0, rig.Refreshes);
    }

    [Fact]
    public async Task AFailedRestoreIsTriedAgain()
    {
        var rig = new Rig(await DriveHoldingABackup());
        rig.Store.UpsertFailure = new IOException("disk gone");
        await rig.Restore();
        rig.Store.UpsertFailure = null;

        var again = await rig.Restore();

        Assert.Equal(BodyRestoreOutcome.Restored, again.Restore!.Outcome);
    }

    [Fact]
    public async Task ARefreshThatThrowsIsAFailureNotACrash()
    {
        var rig = new Rig(await DriveHoldingABackup());

        var result = await rig.Runner.RestoreAtLaunchAsync(
            rig.Store, () => throw new ObjectDisposedException("page"), default);

        Assert.Equal(BodyBackupRunStatus.Failed, result.Status);
        Assert.Equal(nameof(ObjectDisposedException), result.Detail);
    }

    [Fact]
    public async Task CancellationPropagatesFromRestore()
    {
        var rig = new Rig(await DriveHoldingABackup());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Restore(source.Token));
        Assert.Equal(0, rig.Store.All().Count);
    }

    [Fact]
    public async Task CancellationPropagatesFromABackup()
    {
        var rig = new Rig();
        rig.Install.WithHistory();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            rig.Runner.AfterSyncAsync(GoodSync(), rig.Store, BodyBackupTiming.Now, source.Token));
    }

    [Fact]
    public async Task CancellationInsideTheBackupIsNotTurnedIntoAResult()
    {
        var rig = new Rig();
        rig.Install.WithHistory();
        rig.Factory = () => throw new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.AfterSync(timing: BodyBackupTiming.Now));
    }

    // ---- what the trail and the results may carry ----

    [Fact]
    public async Task NeitherTheTrailNorAResultCarriesAPassphraseOrAReading()
    {
        var rig = new Rig(await DriveHoldingABackup());
        rig.Install.WithHistory([BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay, 61.8125)]);
        var results = new List<BodyBackupRunResult>
        {
            await rig.Restore(),
            await rig.AfterSync(timing: BodyBackupTiming.Now),
        };
        rig.Passphrase = null;
        results.Add(await rig.Restore());

        var everything = string.Join("\n", rig.Install.Log.Lines) + "\n" + string.Join("\n", results.Select(result => result.ToString()));

        Assert.DoesNotContain(BackupInvented.Passphrase, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(BackupInvented.OtherPassphrase, everything, StringComparison.Ordinal);
        Assert.DoesNotContain("61.8125", everything, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRunnerHasNoWayToHoldAReading()
    {
        var holdsBody = typeof(BodyBackupRunResult).GetProperties()
            .Select(property => property.PropertyType)
            .Where(type => type.Namespace == "Constitution.Domain.Body")
            .ToList();

        Assert.Empty(holdsBody);
    }

    // ---- the flush when the app is paused (review N1c) ----

    [Fact]
    public void ThePauseOfTheActivityFlushesWithoutWaiting()
    {
        var activity = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "ConstitutionActivity.cs"));
        var wiring = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "Services", "BodyBackups.cs"));

        var pause = Settings.SettingsWiringSourceTests.BodyOf(activity, "OnPause");
        Assert.NotNull(pause);
        Assert.Contains("BodyBackups.FlushOnPause(this)", pause, StringComparison.Ordinal);

        var flush = Settings.SettingsWiringSourceTests.BodyOf(wiring, "FlushOnPause");
        Assert.NotNull(flush);
        Assert.Contains(".FlushAsync(", flush, StringComparison.Ordinal);
        Assert.Contains("Task.Run(", flush, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", flush, StringComparison.Ordinal);
        Assert.DoesNotContain("GetResult()", flush, StringComparison.Ordinal);
        Assert.DoesNotContain(".Result", flush, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APauseFlushWritesWhatIsPendingAtOnce()
    {
        var rig = new Rig();
        rig.Install.WithHistory();
        await rig.AfterSync(); // noted, not yet due
        Assert.True(rig.Debouncer.HasPending);
        Assert.Equal(0, rig.Drive.Writes);

        var result = await rig.Runner.FlushAsync(rig.Store, default);

        Assert.Equal(BodyBackupRunStatus.BackupRan, result.Status);
        Assert.Equal(BodyBackupOutcome.BackedUp, result.Backup!.Outcome);
        Assert.Equal(1, rig.Drive.Writes);
        Assert.False(rig.Debouncer.HasPending);
    }

    [Fact]
    public async Task APauseWithNothingPendingTouchesNothing()
    {
        var rig = new Rig();
        rig.Install.WithHistory();

        var result = await rig.Runner.FlushAsync(rig.Store, default);

        Assert.Equal(BodyBackupRunStatus.NotDue, result.Status);
        Assert.Equal(0, rig.Drive.Reads);
        Assert.Equal(0, rig.Drive.Writes);
    }

    [Fact]
    public async Task APauseWithNoPassphraseOrSignInMakesNoDriveCall()
    {
        var noPass = new Rig { Passphrase = null };
        noPass.Install.WithHistory();
        await noPass.AfterSync();
        var signedOut = new Rig { SignedIn = false };
        signedOut.Install.WithHistory();
        await signedOut.AfterSync();

        Assert.Equal(BodyBackupRunStatus.NoPassphrase, (await noPass.Runner.FlushAsync(noPass.Store, default)).Status);
        Assert.Equal(BodyBackupRunStatus.NotSignedIn, (await signedOut.Runner.FlushAsync(signedOut.Store, default)).Status);
        Assert.Equal(0, noPass.Drive.Reads + signedOut.Drive.Reads);
        Assert.True(noPass.Debouncer.HasPending);
    }

    [Fact]
    public async Task APauseFlushThatCannotReachDriveKeepsTheChangePending()
    {
        var rig = new Rig();
        rig.Install.WithHistory();
        await rig.AfterSync();
        rig.Drive.ReadFailure = new HttpRequestException("no network");

        var result = await rig.Runner.FlushAsync(rig.Store, default);

        Assert.Equal(BodyBackupOutcome.Unreachable, result.Backup!.Outcome);
        Assert.True(rig.Debouncer.HasPending);
    }

    [Fact]
    public async Task APauseFlushFailureIsATypeNotACrash()
    {
        var rig = new Rig { Factory = () => throw new InvalidOperationException("marker-text-that-must-not-travel") };
        rig.Install.WithHistory();
        await rig.AfterSync();

        var result = await rig.Runner.FlushAsync(rig.Store, default);

        Assert.Equal(BodyBackupRunStatus.Failed, result.Status);
        Assert.Equal(nameof(InvalidOperationException), result.Detail);
    }
}

/// <summary>
/// Who may build the Body backup's Drive file. Only the backup folder in Infrastructure and the one device
/// wiring file in the head know the file's name or construct the backup over a <c>DriveTextFile</c>; a
/// second place that did would be a second way for Body to leave the phone that
/// <c>BodyStaysHomeTests</c> was not watching. A lexer and a few regexes, as that guard is.
/// </summary>
public class BodyBackupWiringGuardTests
{
    private const string WiringFile = "Constitution.Android/Services/BodyBackups.cs";
    private const string BackupFolder = "Constitution.Infrastructure/Backup/";
    private const string CodecFile = "Constitution.Infrastructure/Backup/BodyBackup.cs";

    private static readonly Regex Construction = new(@"\bnew\s+BodyBackup\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex FileNameConstant = new(@"\bBodyBackup\s*\.\s*FileName\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex DriveFileConstruction = new(@"\bnew\s+DriveTextFile\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private const string FileNameLiteral = "storyworthtelling-constitution-body";

    /// <summary>The ways a source file (relative to Constitution/) steps outside the wiring; empty when it does not.</summary>
    internal static IReadOnlyList<string> Violations(string relativePath, string source)
    {
        var code = HeadScan.Sanitise(source);
        var inBackup = relativePath.StartsWith(BackupFolder, StringComparison.Ordinal);
        var isWiring = relativePath == WiringFile;
        var found = new List<string>();

        if (Construction.IsMatch(code) && !isWiring)
        {
            found.Add("constructs a BodyBackup outside the device wiring");
        }

        if (FileNameConstant.IsMatch(code) && !inBackup && !isWiring)
        {
            found.Add("names the Body file's constant outside the backup");
        }

        if (source.Contains(FileNameLiteral, StringComparison.Ordinal) && relativePath != CodecFile)
        {
            found.Add("spells the Body file's name");
        }

        if (DriveFileConstruction.IsMatch(code) && FileNameConstant.IsMatch(code) && !isWiring)
        {
            found.Add("builds a Drive file for the Body backup outside the device wiring");
        }

        return found;
    }

    private static IEnumerable<(string Path, string Text)> Production()
    {
        return ConstitutionSource.Files(".cs")
            .Where(file => !ConstitutionSource.Relative(file).StartsWith("Constitution.Tests/", StringComparison.Ordinal))
            .Select(file => (ConstitutionSource.Relative(file), File.ReadAllText(file)));
    }

    [Fact]
    public void OnlyTheBackupAndTheWiringBuildTheBodyFile()
    {
        var offenders = Production()
            .SelectMany(file => Violations(file.Path, file.Text).Select(kind => file.Path + ": " + kind))
            .ToList();

        Assert.True(offenders.Count == 0, "Body backup construction outside its two homes:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheWiringDoesBuildTheBackupOverADriveFileOfTheBodyName()
    {
        var path = Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "Services", "BodyBackups.cs");
        Assert.True(File.Exists(path), "Services/BodyBackups.cs does not exist.");
        var code = HeadScan.Sanitise(File.ReadAllText(path));

        Assert.Matches(Construction, code);
        Assert.Matches(DriveFileConstruction, code);
        Assert.Matches(FileNameConstant, code);
    }

    [Fact]
    public void TheScanReadsTheRealTree()
    {
        var files = Production().ToList();

        Assert.True(files.Count >= 100, $"Only {files.Count} production files were read; the guard is not seeing the tree.");
        Assert.Contains(files, file => file.Path == CodecFile);
    }

    [Theory]
    [InlineData("Constitution.Android/Body/BodyServices.cs", "var b = new BodyBackup(file, log);", "constructs a BodyBackup outside the device wiring")]
    [InlineData("Constitution.Android/Settings/Foo.cs", "var n = BodyBackup.FileName;", "names the Body file's constant outside the backup")]
    [InlineData("Constitution.Infrastructure/Vault/Foo.cs", "var n = \"storyworthtelling-constitution-body.json\";", "spells the Body file's name")]
    [InlineData("Constitution.Android/Services/Other.cs", "var f = new DriveTextFile(d, BodyBackup.FileName);", "builds a Drive file for the Body backup outside the device wiring")]
    public void APlantedBreachIsNamed(string path, string source, string kind)
    {
        Assert.Contains(kind, Violations(path, source));
    }

    [Theory]
    [InlineData("Constitution.Android/Services/BodyBackups.cs", "var b = new BodyBackup(new DriveTextFile(f, BodyBackup.FileName), log);")]
    [InlineData("Constitution.Infrastructure/Backup/BodyBackupRunner.cs", "var n = BodyBackup.FileName;")]
    [InlineData("Constitution.Android/Services/ConstitutionVaults.cs", "var s = new DriveTextFile(f, DriveSecretsVaultStore.FileName);")]
    [InlineData("Constitution.Android/Body/BodyServices.cs", "// new BodyBackup(file, log) is only in a comment\nvar t = \"new BodyBackup(\";")]
    public void TheHomesAndOrdinaryCodeAreLeftAlone(string path, string source)
    {
        Assert.Empty(Violations(path, source));
    }
}
