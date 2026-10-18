using Constitution.Domain.Body;
using Constitution.Infrastructure.Vault;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Infrastructure.Backup;

/// <summary>
/// One person's Body history, backed up encrypted to their own Drive and restored to a fresh install
/// (C-7.4.1). The file is <see cref="FileName"/>, sealed in the suite's <see cref="VaultEnvelope"/> with
/// the vault passphrase the owner already enters, and found by name in <i>that account's</i> Drive: the
/// <see cref="IDriveTextFile"/> a head hands in is bound to the signed-in account, so the owner's file
/// and the partner's never meet, and nothing here knows whose it is. It is private by construction: the
/// household file is a different file in a different place and carries no Body (G13).
///
/// <para><b>Three rules outrank every feature here, and each has a test that names it.</b></para>
/// <list type="number">
/// <item><b>A restore only ever adds.</b> It is a merge into whatever this install holds: a day the
/// install already read keeps its own reading, every other reading of the backup is added, and the
/// install day becomes the earlier of the two. (It used to land only on an empty install, which forfeited
/// the restore the moment the reader stored a first reading, and the reader does that before the owner has
/// typed the passphrase.) It still writes nothing to Drive, and the file it reads is the one in the
/// signed-in account's own Drive.</item>
/// <item><b>Nothing is ever written over a file that could not be read.</b> The Body history is kept for
/// ever and this file may be the only copy of it, so a backup reads what is there first and refuses
/// (<see cref="BodyBackupOutcome.RefusedUnreadable"/>) when it exists and will not open: a mistyped
/// passphrase, a damaged file, a file that is not a Body backup. Writing a fresh file over a good one
/// because the passphrase was mistyped is the worst shape this failure can take, because nothing
/// anywhere says so. A file holding something this build does not understand (a newer format, a signal
/// it has no signal for) is treated the same way, since the write would drop it
/// (<see cref="BodyBackupOutcome.RefusedNewerFormat"/>).</item>
/// <item><b>An install with less history never shrinks or blanks the backup of one with more.</b> An
/// install with no readings writes nothing at all. One with some, which has not been restored (a
/// reinstalled phone reads its first days before the owner has typed the passphrase), is merged into
/// what Drive holds rather than written over it: the file only ever gains readings. Nothing new
/// writes nothing.</item>
/// </list>
///
/// <para>A write is read back and must open, or the previous file is put back: the check is that the
/// bytes decrypt (the cipher authenticates them), not that they equal what was sent, so another install
/// of the same account writing at the same moment is not mistaken for damage.</para>
///
/// <para><b>The passphrase</b> is an argument to each call, used for that call's reads and writes and
/// never kept, logged or put in a result. Results and log phrases carry an outcome and, for a failure,
/// the exception's type and message with the passphrase taken out. They cannot carry a reading: the
/// result types have nowhere to put one.</para>
///
/// <para>Calls on one instance are serialised, so a flush from the app leaving cannot interleave with a
/// backup the debouncer started. Two installs of one account are not coordinated (the install is the
/// person, brief, Two installs); the merge is what keeps the worst case a readings-short file that the
/// next backup tops up.</para>
/// </summary>
public sealed class BodyBackup
{
    /// <summary>
    /// The address, not a description: the file is found by name across installs, so a rename orphans a
    /// backup that is still sitting in Drive holding the person's whole history. The same name in the
    /// owner's Drive and the partner's, which never meet.
    /// </summary>
    public const string FileName = "storyworthtelling-constitution-body.json";

    private readonly IDriveTextFile file;
    private readonly IConstitutionVaultLog log;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <param name="file">This account's own <see cref="FileName"/> in its own Drive.</param>
    /// <param name="log">Where outcome phrases go: short, and carrying no value, name or passphrase.</param>
    public BodyBackup(IDriveTextFile file, IConstitutionVaultLog log)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(log);
        this.file = file;
        this.log = log;
    }

    // ---- restore ----

    /// <summary>
    /// Merges the backed-up history into <paramref name="store"/> without costing it anything it has
    /// (<see cref="IBodyStore.MergeRestored"/>). Writes nothing to Drive.
    /// </summary>
    public async Task<BodyRestoreResult> RestoreAsync(string? passphrase, IBodyStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var result = await RestoreLockedAsync(passphrase, store, cancellationToken);
            log.Outcome(Words(result.Outcome));
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BodyRestoreResult> RestoreLockedAsync(string? passphrase, IBodyStore store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            return new BodyRestoreResult(BodyRestoreOutcome.NoPassphrase);
        }

        var remote = await ReadRemoteAsync(passphrase, cancellationToken);
        switch (remote.Kind)
        {
            case RemoteKind.Unreachable:
                return new BodyRestoreResult(BodyRestoreOutcome.Unreachable, remote.Detail);
            case RemoteKind.WrongPassphrase:
                return new BodyRestoreResult(BodyRestoreOutcome.WrongPassphrase);
            case RemoteKind.Corrupt:
                return new BodyRestoreResult(BodyRestoreOutcome.Corrupt, remote.Detail);
            case RemoteKind.Absent:
                return new BodyRestoreResult(BodyRestoreOutcome.NoBackup);
        }

        var history = remote.Backup!.History;
        if (history.IsEmpty)
        {
            return new BodyRestoreResult(BodyRestoreOutcome.NoBackup);
        }

        // Merged: local days win, the rest is added, the earlier install day wins. A failure in the store is the
        // store's to report; it is not turned into an outcome here.
        var changed = store.MergeRestored(history.InstallDay, history.Readings);
        return new BodyRestoreResult(changed ? BodyRestoreOutcome.Restored : BodyRestoreOutcome.NothingNew);
    }

    // ---- backup ----

    /// <summary>
    /// Merges <paramref name="store"/>'s history into the file in Drive and writes it sealed, unless the
    /// file exists and cannot be read, or there is nothing new to say.
    /// </summary>
    public async Task<BodyBackupResult> BackupAsync(string? passphrase, IBodyStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var result = await BackupLockedAsync(passphrase, store, cancellationToken);
            log.Outcome(Words(result.Outcome));
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BodyBackupResult> BackupLockedAsync(string? passphrase, IBodyStore store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            return new BodyBackupResult(BodyBackupOutcome.NoPassphrase);
        }

        // Rule 3, first half: an install with nothing to say does not even look at the file.
        if (store.IsEmpty)
        {
            return new BodyBackupResult(BodyBackupOutcome.NothingToBackup);
        }

        // Rule 2: read what is there first, and refuse to write over anything that will not open.
        var remote = await ReadRemoteAsync(passphrase, cancellationToken);
        switch (remote.Kind)
        {
            case RemoteKind.Unreachable:
                return new BodyBackupResult(BodyBackupOutcome.Unreachable, remote.Detail);
            case RemoteKind.WrongPassphrase:
            case RemoteKind.Corrupt:
                return new BodyBackupResult(BodyBackupOutcome.RefusedUnreadable, remote.Detail);
        }

        if (remote.Backup is { HasUnreadParts: true })
        {
            return new BodyBackupResult(BodyBackupOutcome.RefusedNewerFormat);
        }

        // Taken after the read, so it is the freshest this install has.
        var local = BodyHistory.From(store);
        if (local.IsEmpty)
        {
            return new BodyBackupResult(BodyBackupOutcome.NothingToBackup);
        }

        // Rule 3, second half: add to what is there; never replace it with less.
        var existing = remote.Backup?.History;
        var merged = existing is null ? local : existing.MergedWith(local);
        if (existing is not null && merged.HasSameContentAs(existing))
        {
            return new BodyBackupResult(BodyBackupOutcome.Unchanged);
        }

        var sealedText = VaultEnvelope.Write(BodyBackupCodec.Write(merged), passphrase);
        try
        {
            await file.WriteAsync(sealedText, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BodyBackupResult(BodyBackupOutcome.Failed, Describe(ex, passphrase));
        }

        return await ConfirmAsync(passphrase, remote.Raw, cancellationToken);
    }

    // Reads the file back: it must open. If Drive cannot be asked, the write is neither trusted nor
    // undone (the next backup reads it, finds nothing to add and stops). If it does not open, the
    // previous contents are put back, so a damaged upload costs one attempt and not the backup.
    private async Task<BodyBackupResult> ConfirmAsync(string passphrase, string? previous, CancellationToken cancellationToken)
    {
        string? readBack;
        try
        {
            readBack = await file.ReadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BodyBackupResult(BodyBackupOutcome.Unreachable, Describe(ex, passphrase));
        }

        if (Open(readBack, passphrase).Kind == RemoteKind.Readable)
        {
            return new BodyBackupResult(BodyBackupOutcome.BackedUp);
        }

        try
        {
            // Not the caller's token: leaving mid-call must not leave a damaged file in place. A blank
            // file reads as no file, which is as close to "nothing was here" as the seam can get.
            await file.WriteAsync(previous ?? string.Empty, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BodyBackupResult(
                BodyBackupOutcome.Failed,
                "The file written could not be read back, and the previous file could not be put back: " + Describe(ex, passphrase));
        }

        return new BodyBackupResult(
            BodyBackupOutcome.Failed,
            "The file written could not be read back; the previous file was put back.");
    }

    // ---- debounced ----

    /// <summary>
    /// Backs up if the debouncer says the quiet window has passed, and returns null when it has not. A
    /// run that did not finish its job (Drive unreachable, a failed write, no passphrase yet) puts the
    /// change back, so it is tried again after another quiet window; a refusal does not, since the file
    /// will be no more readable in two minutes than it is now.
    /// </summary>
    public async Task<BodyBackupResult?> BackupWhenDueAsync(
        string? passphrase, IBodyStore store, BodyBackupDebouncer debouncer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(debouncer);
        return debouncer.TakeIfDue(now) ? await BackupTakenAsync(passphrase, store, debouncer, now, cancellationToken) : null;
    }

    /// <summary>
    /// Backs up now if anything is pending, whether or not it is due: for when the app is leaving.
    /// Returns null when nothing was pending.
    /// </summary>
    public async Task<BodyBackupResult?> FlushAsync(
        string? passphrase, IBodyStore store, BodyBackupDebouncer debouncer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(debouncer);
        return debouncer.TakeNow() ? await BackupTakenAsync(passphrase, store, debouncer, now, cancellationToken) : null;
    }

    private async Task<BodyBackupResult> BackupTakenAsync(
        string? passphrase, IBodyStore store, BodyBackupDebouncer debouncer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var finished = false;
        try
        {
            var result = await BackupAsync(passphrase, store, cancellationToken);
            finished = result.Outcome is not (BodyBackupOutcome.NoPassphrase or BodyBackupOutcome.Unreachable or BodyBackupOutcome.Failed);
            return result;
        }
        finally
        {
            // Also reached by an exception or a cancellation: the change is not forgotten.
            if (!finished)
            {
                debouncer.Changed(now);
            }
        }
    }

    // ---- reading the file ----

    private enum RemoteKind
    {
        Absent,
        Readable,
        WrongPassphrase,
        Corrupt,
        Unreachable,
    }

    private sealed record RemoteFile(RemoteKind Kind, string? Raw, DecodedBodyBackup? Backup, string? Detail);

    // The IDriveTextFile seam throws whatever the Google client and the HTTP stack throw (no network, no
    // Drive scope granted, a timeout), so there is no narrower type to catch. The failure is not dropped:
    // it becomes an Unreachable result that names the exception's type and carries its message.
    // Cancellation is the caller leaving and is never turned into a result.
    private async Task<RemoteFile> ReadRemoteAsync(string passphrase, CancellationToken cancellationToken)
    {
        string? raw;
        try
        {
            raw = await file.ReadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RemoteFile(RemoteKind.Unreachable, null, null, Describe(ex, passphrase));
        }

        return Open(raw, passphrase);
    }

    private static RemoteFile Open(string? raw, string passphrase)
    {
        (SecretsVaultStatus Status, string? Plaintext) opened;
        try
        {
            opened = VaultEnvelope.Read(raw, passphrase);
        }
        catch (ArgumentException ex)
        {
            // The envelope maps a failed tag (a wrong passphrase), bad base64 and bad JSON to statuses,
            // but a nonce, tag or iteration count of the wrong size reaches the crypto as an argument
            // error. That is a damaged file, not a crash.
            return new RemoteFile(RemoteKind.Corrupt, raw, null, Describe(ex, passphrase));
        }

        switch (opened.Status)
        {
            case SecretsVaultStatus.Empty:
                return new RemoteFile(RemoteKind.Absent, raw, null, null);
            case SecretsVaultStatus.WrongPassphrase:
                return new RemoteFile(RemoteKind.WrongPassphrase, raw, null, null);
            case SecretsVaultStatus.Loaded:
                break;
            default:
                return new RemoteFile(RemoteKind.Corrupt, raw, null, null);
        }

        try
        {
            return new RemoteFile(RemoteKind.Readable, raw, BodyBackupCodec.Read(opened.Plaintext!), null);
        }
        catch (BodyBackupFormatException ex)
        {
            // Its message names what is wrong with the shape and never what was there.
            return new RemoteFile(RemoteKind.Corrupt, raw, null, ex.Message);
        }
    }

    // ---- words ----

    // The failure's type and message, for the screen that tells the owner what went wrong. The
    // passphrase is taken back out of it first: nothing here writes it into a message, but the text
    // comes from the Drive client and the HTTP stack, and "never appears in a result" should not depend
    // on every layer below behaving.
    private static string Describe(Exception ex, string passphrase) =>
        Redact($"{ex.GetType().Name}: {ex.Message}", passphrase);

    private static string Redact(string text, string passphrase) =>
        string.IsNullOrEmpty(passphrase) ? text : text.Replace(passphrase, "[passphrase]", StringComparison.Ordinal);

    // Written as statements rather than switch expressions on purpose: the NoKeysTests guard reads an
    // arrow from a passphrase-named enum member to a string as a secret being assigned.
    private static string Words(BodyRestoreOutcome outcome)
    {
        switch (outcome)
        {
            case BodyRestoreOutcome.Restored:
                return "body restore: restored";
            case BodyRestoreOutcome.NothingNew:
                return "body restore: nothing new";
            case BodyRestoreOutcome.NoBackup:
                return "body restore: no backup found";
            case BodyRestoreOutcome.WrongPassphrase:
                return "body restore: wrong passphrase";
            case BodyRestoreOutcome.Corrupt:
                return "body restore: backup unreadable";
            case BodyRestoreOutcome.Unreachable:
                return "body restore: unreachable";
            case BodyRestoreOutcome.NoPassphrase:
                return "body restore: skipped (no passphrase)";
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a restore outcome this backup knows.");
        }
    }

    private static string Words(BodyBackupOutcome outcome)
    {
        switch (outcome)
        {
            case BodyBackupOutcome.BackedUp:
                return "body backup: saved";
            case BodyBackupOutcome.Unchanged:
                return "body backup: nothing new";
            case BodyBackupOutcome.NothingToBackup:
                return "body backup: nothing to save";
            case BodyBackupOutcome.NoPassphrase:
                return "body backup: skipped (no passphrase)";
            case BodyBackupOutcome.RefusedUnreadable:
                return "body backup: refused, file unreadable";
            case BodyBackupOutcome.RefusedNewerFormat:
                return "body backup: refused, file is from a newer version";
            case BodyBackupOutcome.Unreachable:
                return "body backup: unreachable";
            case BodyBackupOutcome.Failed:
                return "body backup: failed";
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a backup outcome this backup knows.");
        }
    }
}
