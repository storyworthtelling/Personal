using System.Text.Json;
using Constitution.Domain.Body;
using Constitution.Infrastructure.Backup;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.Backup;

/// <summary>
/// C-7.4.1 — Body's history, encrypted into the person's own Drive and restored to a fresh install.
///
/// <para>The vault envelope, its crypto and the codec are the real ones, run over an in-memory Drive
/// file; only the network and the file-backed store are faked, and every reading is invented. Three
/// rules carry the story and each has a test that names it: a restore lands only on an install with
/// no history; nothing is ever written over a file that could not be read; and an install with less
/// history never shrinks or blanks the backup of an install with more.</para>
/// </summary>
public class BodyBackupTests
{
    private const string Passphrase = BackupInvented.Passphrase;
    private const string OtherPassphrase = BackupInvented.OtherPassphrase;

    private static readonly DateTimeOffset Noon = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static string Sealed(string plaintext, string passphrase = Passphrase) =>
        VaultEnvelope.Write(plaintext, passphrase);

    private static string Plaintext(BackupDrive drive, string passphrase = Passphrase)
    {
        var (status, plaintext) = VaultEnvelope.Read(drive.Content, passphrase);
        Assert.Equal(SecretsVaultStatus.Loaded, status);
        return plaintext!;
    }

    private static BodyHistory OnDrive(BackupDrive drive) => BodyBackupCodec.Read(Plaintext(drive)).History;

    // ---- the round trip ----

    [Fact]
    public async Task RoundTripsEverySignal()
    {
        var phone = new BackupRig().WithHistory();

        var saved = await phone.BackUp();
        var replacement = new BackupRig(phone.Drive);
        var restored = await replacement.Restore();

        Assert.Equal(BodyBackupOutcome.BackedUp, saved.Outcome);
        Assert.Equal(BodyRestoreOutcome.Restored, restored.Outcome);
        Assert.Equal(27, replacement.Store.All().Count);
        Assert.Equal(phone.Store.All(), replacement.Store.All());
        Assert.Equal(phone.Store.InstallDay, replacement.Store.InstallDay);
        foreach (var signal in BodySignals.All)
        {
            Assert.Equal(3, replacement.Store.All().Count(reading => reading.Signal == signal));
        }
    }

    // ---- restore ----

    [Fact]
    public async Task RestoresOnlyOntoAnEmptyInstall()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();

        // A fresh install takes the whole history back, and the install day with it.
        var fresh = new BackupRig(original.Drive);
        Assert.Equal(BodyRestoreOutcome.Restored, (await fresh.Restore()).Outcome);
        Assert.Equal(original.Store.All(), fresh.Store.All());
        Assert.Equal(BackupInvented.InstallDay, fresh.Store.InstallDay);

        // An install with any history at all is left exactly as it is, whatever the backup holds, and
        // the backup is not even opened.
        var busy = new BackupRig(original.Drive);
        busy.Store.Upsert([BackupInvented.One(BodySignal.Weight, new DateOnly(2026, 11, 10), 170.5)]);
        var before = busy.Store.All();
        var readsBefore = original.Drive.Reads;

        var skipped = await busy.Restore();

        Assert.Equal(BodyRestoreOutcome.SkippedNotEmpty, skipped.Outcome);
        Assert.Equal(before, busy.Store.All());
        Assert.Equal(0, busy.Store.SetInstallDayCalls);
        Assert.Equal(readsBefore, original.Drive.Reads);
    }

    [Fact]
    public async Task AReadingThatArrivesMidRestoreIsNotOverwritten()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();

        // The reader writes a first reading while the restore is still waiting on Drive.
        var fresh = new BackupRig(original.Drive);
        var arriving = BackupInvented.One(BodySignal.Steps, new DateOnly(2026, 11, 17), 1234);
        original.Drive.AfterRead = () => fresh.Store.Upsert([arriving]);

        var result = await fresh.Restore();

        Assert.Equal(BodyRestoreOutcome.SkippedNotEmpty, result.Outcome);
        Assert.Equal([arriving], fresh.Store.All());
    }

    [Fact]
    public async Task ARestoreNeverWritesToDrive()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();
        var writesBefore = original.Drive.Writes;
        var contentBefore = original.Drive.Content;

        await new BackupRig(original.Drive).Restore();

        Assert.Equal(writesBefore, original.Drive.Writes);
        Assert.Equal(contentBefore, original.Drive.Content);
    }

    [Fact]
    public async Task ANewInstallWithNoBackupStaysEmpty()
    {
        foreach (var content in new string?[] { null, string.Empty, "  \n " })
        {
            var rig = new BackupRig();
            rig.Drive.Content = content;

            var result = await rig.Restore();

            Assert.Equal(BodyRestoreOutcome.NoBackup, result.Outcome);
            Assert.True(rig.Store.IsEmpty);
        }
    }

    [Fact]
    public async Task AWrongPassphraseRestoresNothingAndSaysSo()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();

        var fresh = new BackupRig(original.Drive);
        var result = await fresh.Restore(OtherPassphrase);

        Assert.Equal(BodyRestoreOutcome.WrongPassphrase, result.Outcome);
        Assert.True(fresh.Store.IsEmpty);
        Assert.True(result.NeedsAttention);
    }

    [Fact]
    public async Task AFileThatIsNotABackupRestoresNothingAndSaysSo()
    {
        foreach (var content in new[] { "{ not an envelope", Sealed("{ not a backup"), Sealed("[1,2,3]"), MalformedNonce() })
        {
            var rig = new BackupRig();
            rig.Drive.Content = content;

            var result = await rig.Restore();

            Assert.Equal(BodyRestoreOutcome.Corrupt, result.Outcome);
            Assert.True(rig.Store.IsEmpty);
            Assert.True(result.NeedsAttention);
        }
    }

    [Fact]
    public async Task ABackupFromANewerBuildRestoresWhatThisBuildKnows()
    {
        var rig = new BackupRig();
        rig.Drive.Content = Sealed(
            """{"version":2,"installDay":"2026-11-01","readings":{"weight":[{"day":"2026-11-02","value":180.5}],"blood-pressure":[{"day":"2026-11-02","value":120}]}}""");

        var result = await rig.Restore();

        Assert.Equal(BodyRestoreOutcome.Restored, result.Outcome);
        Assert.Equal([BackupInvented.One(BodySignal.Weight, new DateOnly(2026, 11, 2), 180.5)], rig.Store.All());
    }

    [Fact]
    public async Task ABackupWithNothingInItRestoresNothing()
    {
        var rig = new BackupRig();
        rig.Drive.Content = Sealed("""{"version":1,"readings":{}}""");

        var result = await rig.Restore();

        Assert.Equal(BodyRestoreOutcome.NoBackup, result.Outcome);
        Assert.Equal(0, rig.Store.UpsertCalls);
    }

    [Fact]
    public async Task DriveBeingUnreachableIsAnOutcomeNotAnException()
    {
        var rig = new BackupRig();
        rig.Drive.ReadFailure = new HttpRequestException("no network at all");

        var result = await rig.Restore();

        Assert.Equal(BodyRestoreOutcome.Unreachable, result.Outcome);
        Assert.Contains("HttpRequestException", result.Detail);
        Assert.True(rig.Store.IsEmpty);
        Assert.False(result.NeedsAttention);
    }

    [Fact]
    public async Task WithoutAPassphraseNothingIsTouched()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();
        var fresh = new BackupRig(original.Drive);
        var readsBefore = original.Drive.Reads;

        foreach (var passphrase in new string?[] { null, string.Empty })
        {
            var result = await fresh.Restore(passphrase);

            Assert.Equal(BodyRestoreOutcome.NoPassphrase, result.Outcome);
        }

        Assert.Equal(readsBefore, original.Drive.Reads);
        Assert.True(fresh.Store.IsEmpty);
    }

    [Fact]
    public async Task AStoreThatFailsIsNotSwallowed()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();
        var fresh = new BackupRig(original.Drive);
        fresh.Store.UpsertFailure = new IOException("the disk is full");

        var error = await Assert.ThrowsAsync<IOException>(() => fresh.Restore());

        Assert.Equal("the disk is full", error.Message);
    }

    [Fact]
    public async Task ACancelledRestoreThrowsRatherThanReportingAnOutcome()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new BackupRig(original.Drive).Backup.RestoreAsync(Passphrase, new InMemoryBodyStore(), cancelled.Token));
    }

    // ---- backup: never over a file that cannot be read ----

    private static string MalformedNonce()
    {
        // A valid envelope whose nonce is the wrong length: the crypto refuses it with an argument
        // exception rather than the tag failure a wrong passphrase gives.
        using var document = JsonDocument.Parse(Sealed("{}"));
        var fields = document.RootElement.EnumerateObject()
            .ToDictionary(field => field.Name, field => field.Value.Clone());
        var edited = fields.ToDictionary(pair => pair.Key, pair => pair.Key == "Nonce" ? (object)"AAAA" : pair.Value);
        return JsonSerializer.Serialize(edited);
    }

    private static string FlipFirstCharOfCiphertext(string envelope)
    {
        var parsed = JsonSerializer.Deserialize<SecretsEnvelope>(envelope, VaultEnvelope.SerializerOptions)!;
        var first = parsed.Ciphertext[0] == 'A' ? 'B' : 'A';
        return JsonSerializer.Serialize(parsed with { Ciphertext = first + parsed.Ciphertext[1..] }, VaultEnvelope.SerializerOptions);
    }

    private static string BadBase64(string envelope)
    {
        var parsed = JsonSerializer.Deserialize<SecretsEnvelope>(envelope, VaultEnvelope.SerializerOptions)!;
        return JsonSerializer.Serialize(parsed with { Salt = "!!!not-base64!!!" }, VaultEnvelope.SerializerOptions);
    }

    public static IEnumerable<object[]> UnreadableFiles()
    {
        yield return ["a wrong passphrase", Sealed("""{"version":1,"readings":{}}""", OtherPassphrase)];
        yield return ["not an envelope", "{ not an envelope"];
        yield return ["a plain list", "[1,2,3]"];
        yield return ["a flipped byte", FlipFirstCharOfCiphertext(Sealed("""{"version":1,"readings":{}}"""))];
        yield return ["bad base64", BadBase64(Sealed("""{"version":1,"readings":{}}"""))];
        yield return ["a short nonce", MalformedNonce()];
        yield return ["garbled plaintext", Sealed("{ not a backup")];
        yield return ["the wrong shape", Sealed("[1,2,3]")];
        yield return ["no version", Sealed("""{"readings":{}}""")];
    }

    [Theory]
    [MemberData(nameof(UnreadableFiles))]
    public async Task NeverOverwritesAnUndecryptableFile(string why, string content)
    {
        var rig = new BackupRig().WithHistory();
        rig.Drive.Content = content;

        var result = await rig.BackUp();

        Assert.True(result.Outcome == BodyBackupOutcome.RefusedUnreadable, why);
        Assert.Equal(0, rig.Drive.Writes);
        Assert.Equal(content, rig.Drive.Content);
        Assert.True(result.NeedsAttention);
    }

    [Fact]
    public async Task AMistypedPassphraseNeverReplacesAGoodBackup()
    {
        var phone = new BackupRig().WithHistory();
        await phone.BackUp();
        var good = phone.Drive.Content;
        phone.Store.Upsert([BackupInvented.One(BodySignal.Steps, new DateOnly(2026, 11, 20), 9000)]);

        var refused = await phone.BackUp(OtherPassphrase);

        Assert.Equal(BodyBackupOutcome.RefusedUnreadable, refused.Outcome);
        Assert.Equal(good, phone.Drive.Content);
        Assert.Equal(1, phone.Drive.Writes);

        // The right passphrase, typed again, goes through and carries the newer reading.
        Assert.Equal(BodyBackupOutcome.BackedUp, (await phone.BackUp()).Outcome);
        Assert.Equal(28, OnDrive(phone.Drive).Readings.Count);
    }

    [Fact]
    public async Task AFileFromANewerBuildIsNeverOverwritten()
    {
        const string newer =
            """{"version":2,"installDay":"2026-11-01","readings":{"weight":[{"day":"2026-11-02","value":180.5}]}}""";
        const string unknownSignal =
            """{"version":1,"readings":{"weight":[{"day":"2026-11-02","value":180.5}],"blood-pressure":[{"day":"2026-11-02","value":120}]}}""";

        foreach (var plaintext in new[] { newer, unknownSignal })
        {
            var rig = new BackupRig().WithHistory();
            rig.Drive.Content = Sealed(plaintext);
            var before = rig.Drive.Content;

            var result = await rig.BackUp();

            Assert.Equal(BodyBackupOutcome.RefusedNewerFormat, result.Outcome);
            Assert.Equal(0, rig.Drive.Writes);
            Assert.Equal(before, rig.Drive.Content);
        }
    }

    // ---- backup: the shape of what is written ----

    [Fact]
    public async Task TheFirstBackupCreatesTheSealedFile()
    {
        var phone = new BackupRig().WithHistory();

        var result = await phone.BackUp();

        Assert.Equal(BodyBackupOutcome.BackedUp, result.Outcome);
        Assert.Equal(1, phone.Drive.Writes);
        Assert.True(OnDrive(phone.Drive).HasSameContentAs(BodyHistory.From(phone.Store)));
    }

    [Fact]
    public async Task AWriteIsOnlyEverTheEnvelopeNeverPlaintext()
    {
        var phone = new BackupRig().WithHistory();
        await phone.BackUp();

        var envelope = JsonSerializer.Deserialize<SecretsEnvelope>(phone.Drive.Content!, VaultEnvelope.SerializerOptions)!;

        Assert.Equal(SecretsEnvelope.CurrentVersion, envelope.Version);
        Assert.Equal(SecretsEnvelope.Pbkdf2Sha256, envelope.Kdf);
        Assert.False(string.IsNullOrEmpty(envelope.Ciphertext));
        foreach (var plain in new[] { "weight", "version", "installDay", "2026-11-02", "readings" }.Concat(BackupInvented.EveryValueText()))
        {
            Assert.DoesNotContain(plain, phone.Drive.Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ALaterBackupCarriesTheNewReadings()
    {
        var phone = new BackupRig().WithHistory();
        await phone.BackUp();
        phone.Store.Upsert([BackupInvented.One(BodySignal.Weight, new DateOnly(2026, 11, 20), 177.25)]);

        var result = await phone.BackUp();

        Assert.Equal(BodyBackupOutcome.BackedUp, result.Outcome);
        Assert.Equal(2, phone.Drive.Writes);
        Assert.Equal(28, OnDrive(phone.Drive).Readings.Count);
    }

    [Fact]
    public async Task NothingNewWritesNothing()
    {
        var phone = new BackupRig().WithHistory();
        await phone.BackUp();
        var content = phone.Drive.Content;

        var again = await phone.BackUp();

        Assert.Equal(BodyBackupOutcome.Unchanged, again.Outcome);
        Assert.Equal(1, phone.Drive.Writes);
        Assert.Equal(content, phone.Drive.Content);
    }

    // ---- backup: never blank or shrink what is there ----

    [Fact]
    public async Task AnEmptyInstallNeverBlanksTheBackup()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();
        var content = original.Drive.Content;

        // A reinstalled phone, before its restore has run, asks for a backup.
        var fresh = new BackupRig(original.Drive);
        var result = await fresh.BackUp();

        Assert.Equal(BodyBackupOutcome.NothingToBackup, result.Outcome);
        Assert.Equal(1, original.Drive.Writes);
        Assert.Equal(content, original.Drive.Content);
    }

    [Fact]
    public async Task AnEmptyInstallWithNoFileCreatesNone()
    {
        var fresh = new BackupRig();

        var result = await fresh.BackUp();

        Assert.Equal(BodyBackupOutcome.NothingToBackup, result.Outcome);
        Assert.Equal(0, fresh.Drive.Writes);
        Assert.Null(fresh.Drive.Content);
    }

    [Fact]
    public async Task ASmallerHistoryNeverShrinksTheBackup()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();

        // The reinstalled phone read its first days before the restore: it is not empty, so the restore
        // will skip it, and its backup must add to the file, not replace it.
        var fresh = new BackupRig(original.Drive);
        var newDay = new DateOnly(2026, 11, 25);
        fresh.Store.SetInstallDay(newDay);
        fresh.Store.Upsert([BackupInvented.One(BodySignal.Weight, newDay, 176.75)]);

        Assert.Equal(BodyRestoreOutcome.SkippedNotEmpty, (await fresh.Restore()).Outcome);
        var result = await fresh.BackUp();

        Assert.Equal(BodyBackupOutcome.BackedUp, result.Outcome);
        var onDrive = OnDrive(original.Drive);
        Assert.Equal(28, onDrive.Readings.Count);
        Assert.Contains(BackupInvented.One(BodySignal.Weight, newDay, 176.75), onDrive.Readings);
        foreach (var kept in original.Store.All())
        {
            Assert.Contains(kept, onDrive.Readings);
        }

        Assert.Equal(BackupInvented.InstallDay, onDrive.InstallDay);
    }

    [Fact]
    public async Task OnADayBothHoldThisInstallsReadingWins()
    {
        var original = new BackupRig().WithHistory();
        await original.BackUp();
        var other = new BackupRig(original.Drive);
        other.Store.Upsert([BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay, 1.5)]);

        await other.BackUp();

        var onDrive = OnDrive(original.Drive);
        Assert.Equal(27, onDrive.Readings.Count);
        Assert.Equal(1.5, onDrive.Readings.Single(r => r.Signal == BodySignal.Weight && r.Day == BackupInvented.FirstDay).Value);
    }

    // ---- backup: when Drive misbehaves ----

    [Fact]
    public async Task DriveBeingUnreachableWritesNothing()
    {
        var phone = new BackupRig().WithHistory();
        phone.Drive.ReadFailure = new HttpRequestException("no network at all");

        var result = await phone.BackUp();

        Assert.Equal(BodyBackupOutcome.Unreachable, result.Outcome);
        Assert.Contains("HttpRequestException", result.Detail);
        Assert.Equal(0, phone.Drive.Writes);
        Assert.False(result.NeedsAttention);
    }

    [Fact]
    public async Task ARefusedWriteIsAFailureWithItsReason()
    {
        var phone = new BackupRig().WithHistory();
        phone.Drive.WriteFailure = new HttpRequestException("drive rejected the write");

        var result = await phone.BackUp();

        Assert.Equal(BodyBackupOutcome.Failed, result.Outcome);
        Assert.Contains("drive rejected the write", result.Detail);
        Assert.True(result.NeedsAttention);
    }

    [Fact]
    public async Task AWriteThatDidNotLandIntactPutsTheOldFileBack()
    {
        var phone = new BackupRig().WithHistory();
        await phone.BackUp();
        var good = phone.Drive.Content;
        phone.Store.Upsert([BackupInvented.One(BodySignal.Steps, new DateOnly(2026, 11, 20), 9000)]);

        // The next upload is cut short on its way to Drive; the rollback goes through untouched.
        var writesBefore = phone.Drive.Writes;
        phone.Drive.OnWrite = (number, sent) => number == writesBefore + 1 ? sent[..(sent.Length / 2)] : sent;

        var result = await phone.BackUp();

        Assert.Equal(BodyBackupOutcome.Failed, result.Outcome);
        Assert.Equal(good, phone.Drive.Content);
        Assert.Equal(writesBefore + 2, phone.Drive.Writes);
    }

    [Fact]
    public async Task AFirstWriteThatDidNotLandLeavesNoUnreadableFile()
    {
        var phone = new BackupRig().WithHistory();
        phone.Drive.OnWrite = (number, sent) => number == 1 ? sent[..(sent.Length / 2)] : sent;

        var result = await phone.BackUp();

        Assert.Equal(BodyBackupOutcome.Failed, result.Outcome);
        Assert.True(string.IsNullOrWhiteSpace(phone.Drive.Content));

        // A blank file is as good as none: the next backup goes straight through.
        Assert.Equal(BodyBackupOutcome.BackedUp, (await phone.BackUp()).Outcome);
    }

    [Fact]
    public async Task AWriteWhoseCheckCouldNotBeReadIsRetriedNotTrusted()
    {
        var phone = new BackupRig().WithHistory();
        phone.Drive.FailReadsAfterAWrite = true;

        var first = await phone.BackUp();

        Assert.Equal(BodyBackupOutcome.Unreachable, first.Outcome);
        Assert.Equal(1, phone.Drive.Writes);

        // The upload had landed after all: the next attempt reads it, finds nothing to add and stops.
        phone.Drive.FailReadsAfterAWrite = false;
        Assert.Equal(BodyBackupOutcome.Unchanged, (await phone.BackUp()).Outcome);
        Assert.Equal(1, phone.Drive.Writes);
    }

    [Fact]
    public async Task WithoutAPassphraseNothingIsRead()
    {
        var phone = new BackupRig().WithHistory();

        foreach (var passphrase in new string?[] { null, string.Empty })
        {
            Assert.Equal(BodyBackupOutcome.NoPassphrase, (await phone.BackUp(passphrase)).Outcome);
        }

        Assert.Equal(0, phone.Drive.Reads);
        Assert.Equal(0, phone.Drive.Writes);
    }

    [Fact]
    public async Task ACancelledBackupThrowsRatherThanReportingAnOutcome()
    {
        var phone = new BackupRig().WithHistory();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => phone.Backup.BackupAsync(Passphrase, phone.Store, cancelled.Token));
        Assert.Equal(0, phone.Drive.Writes);
    }

    [Fact]
    public async Task TwoBackupsAtOnceWriteOnce()
    {
        var phone = new BackupRig().WithHistory();
        var hold = new TaskCompletionSource();
        phone.Drive.HoldReads = hold;

        var first = phone.BackUp();
        var second = phone.BackUp();
        await WaitUntil(() => phone.Drive.Reads >= 1);
        await Task.Delay(100);
        var readsWhileHeld = phone.Drive.Reads;
        phone.Drive.HoldReads = null;
        hold.SetResult();
        var outcomes = (await Task.WhenAll(first, second)).Select(result => result.Outcome).OrderBy(outcome => outcome).ToArray();

        Assert.Equal(1, readsWhileHeld);
        Assert.Equal(1, phone.Drive.Writes);
        Assert.Equal([BodyBackupOutcome.BackedUp, BodyBackupOutcome.Unchanged], outcomes);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The condition was not met in time.");
    }

    // ---- the account's own file ----

    [Fact]
    public async Task EachAccountKeepsItsOwnFile()
    {
        var owner = new BackupRig().WithHistory([BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay, 190.25)]);
        var partner = new BackupRig().WithHistory([BackupInvented.One(BodySignal.Weight, BackupInvented.FirstDay, 140.75)]);

        await owner.BackUp();
        await partner.BackUp();

        Assert.NotSame(owner.Drive, partner.Drive);
        Assert.Equal(190.25, OnDrive(owner.Drive).Readings.Single().Value);
        Assert.Equal(140.75, OnDrive(partner.Drive).Readings.Single().Value);

        // A phone bound to the partner's Drive restores the partner's history and none of the owner's.
        var replacement = new BackupRig(partner.Drive);
        await replacement.Restore();
        Assert.Equal(140.75, replacement.Store.All().Single().Value);
    }

    // ---- debounced ----

    [Fact]
    public async Task ManyChangesMakeOneWrite()
    {
        var phone = new BackupRig();
        var debouncer = new BodyBackupDebouncer();
        for (var index = 0; index < 20; index++)
        {
            phone.Store.Upsert([BackupInvented.One(BodySignal.Steps, BackupInvented.FirstDay.AddDays(index), 1000 + index)]);
            debouncer.Changed(Noon.AddSeconds(index));
        }

        var stillQuiet = await phone.Backup.BackupWhenDueAsync(Passphrase, phone.Store, debouncer, Noon.AddSeconds(30), default);
        Assert.Null(stillQuiet);
        Assert.Equal(0, phone.Drive.Writes);

        var due = Noon.AddSeconds(19) + BodyBackupDebouncer.QuietWindow;
        var written = await phone.Backup.BackupWhenDueAsync(Passphrase, phone.Store, debouncer, due, default);
        Assert.Equal(BodyBackupOutcome.BackedUp, written!.Outcome);
        Assert.Equal(1, phone.Drive.Writes);

        Assert.Null(await phone.Backup.BackupWhenDueAsync(Passphrase, phone.Store, debouncer, due.AddHours(1), default));
        Assert.Equal(1, phone.Drive.Writes);
    }

    [Fact]
    public async Task AFailedBackupIsTriedAgainAfterAnotherQuietWindow()
    {
        var phone = new BackupRig().WithHistory();
        var debouncer = new BodyBackupDebouncer();
        debouncer.Changed(Noon);
        phone.Drive.WriteFailure = new HttpRequestException("drive rejected the write");
        var due = Noon + BodyBackupDebouncer.QuietWindow;

        var failed = await phone.Backup.BackupWhenDueAsync(Passphrase, phone.Store, debouncer, due, default);

        Assert.Equal(BodyBackupOutcome.Failed, failed!.Outcome);
        Assert.True(debouncer.HasPending);
        Assert.Equal(due + BodyBackupDebouncer.QuietWindow, debouncer.DueAt);

        phone.Drive.WriteFailure = null;
        Assert.Null(await phone.Backup.BackupWhenDueAsync(Passphrase, phone.Store, debouncer, due.AddSeconds(1), default));
        var retried = await phone.Backup.BackupWhenDueAsync(Passphrase, phone.Store, debouncer, debouncer.DueAt!.Value, default);
        Assert.Equal(BodyBackupOutcome.BackedUp, retried!.Outcome);
        Assert.False(debouncer.HasPending);
    }

    [Fact]
    public async Task ARefusalIsNotRetriedUntilSomethingChanges()
    {
        var phone = new BackupRig().WithHistory();
        phone.Drive.Content = "{ not an envelope";
        var debouncer = new BodyBackupDebouncer();
        debouncer.Changed(Noon);

        var refused = await phone.Backup.BackupWhenDueAsync(Passphrase, phone.Store, debouncer, Noon + BodyBackupDebouncer.QuietWindow, default);

        Assert.Equal(BodyBackupOutcome.RefusedUnreadable, refused!.Outcome);
        Assert.False(debouncer.HasPending);
        Assert.Null(await phone.Backup.BackupWhenDueAsync(Passphrase, phone.Store, debouncer, Noon.AddDays(1), default));
    }

    [Fact]
    public async Task AMissingPassphraseKeepsTheChangeWaiting()
    {
        var phone = new BackupRig().WithHistory();
        var debouncer = new BodyBackupDebouncer();
        debouncer.Changed(Noon);

        var skipped = await phone.Backup.BackupWhenDueAsync(null, phone.Store, debouncer, Noon + BodyBackupDebouncer.QuietWindow, default);

        Assert.Equal(BodyBackupOutcome.NoPassphrase, skipped!.Outcome);
        Assert.True(debouncer.HasPending);
    }

    [Fact]
    public async Task FlushingWritesBeforeTheWindowHasPassed()
    {
        var phone = new BackupRig().WithHistory();
        var debouncer = new BodyBackupDebouncer();

        Assert.Null(await phone.Backup.FlushAsync(Passphrase, phone.Store, debouncer, Noon, default));

        debouncer.Changed(Noon);
        var flushed = await phone.Backup.FlushAsync(Passphrase, phone.Store, debouncer, Noon.AddSeconds(1), default);

        Assert.Equal(BodyBackupOutcome.BackedUp, flushed!.Outcome);
        Assert.False(debouncer.HasPending);
        Assert.Null(await phone.Backup.FlushAsync(Passphrase, phone.Store, debouncer, Noon.AddSeconds(2), default));
    }
}
