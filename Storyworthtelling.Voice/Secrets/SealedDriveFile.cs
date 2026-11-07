namespace Storyworthtelling.Voice.Secrets;

/// <summary>What reading a sealed file came to. Only <see cref="Loaded"/> carries plaintext.</summary>
public enum SealedReadStatus
{
    /// <summary>There is no file.</summary>
    Absent,

    /// <summary>The file opened.</summary>
    Loaded,

    /// <summary>The file is there and the key does not open it.</summary>
    WrongPassphrase,

    /// <summary>The file is there and is not an envelope, or is blank.</summary>
    Corrupt,

    /// <summary>The file opened and was written by a newer app than this one.</summary>
    NewerFormat,

    /// <summary>Drive could not be asked.</summary>
    Unreachable,
}

/// <summary>
/// One read of a sealed file: what it came to, the plaintext when it opened, and what a later write needs —
/// the raw file (to put back if the write does not read back) and the revision it was read at. Detail is for a
/// log: it names the failure, never the contents or the key.
/// </summary>
public sealed record SealedRead(SealedReadStatus Status, string? Plaintext, string? Raw, string? Revision, string? Detail = null)
{
    /// <summary>What Drive threw, when the read was <see cref="SealedReadStatus.Unreachable"/>.</summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// Rethrows Drive's own exception, unchanged and with its stack, for a store whose callers tell "offline"
    /// from "signed out" by it. A store that may not read an unreachable file as empty calls this first.
    /// </summary>
    public void ThrowIfUnreachable()
    {
        if (Status == SealedReadStatus.Unreachable && Error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Error).Throw();
        }
    }

    /// <summary>Whether a write over this read is allowed at all (before the create rule and the revision check).</summary>
    public bool Writable => Status is SealedReadStatus.Loaded or SealedReadStatus.Absent;
}

public enum SealedWriteOutcome
{
    /// <summary>Written and read back.</summary>
    Written,

    /// <summary>Not written: what is there must not be written over (G14), or this install may not create it.</summary>
    Refused,

    /// <summary>Not written: the file changed since it was read. Read it again, merge and try again.</summary>
    Conflict,

    /// <summary>Written but it did not read back, so the previous file was put back.</summary>
    Unconfirmed,

    /// <summary>Written, but it could not be read back to check. Neither trusted nor undone.</summary>
    Unverified,

    /// <summary>The write itself failed; the file is as it was.</summary>
    Failed,
}

public sealed record SealedWriteResult(SealedWriteOutcome Outcome, string? Detail = null)
{
    public bool Succeeded => Outcome == SealedWriteOutcome.Written;

    /// <summary>What Drive threw, when the write itself <see cref="SealedWriteOutcome.Failed"/>.</summary>
    public Exception? Error { get; init; }

    /// <summary>Rethrows Drive's own exception, unchanged, when the upload itself failed.</summary>
    public void ThrowIfFailed()
    {
        if (Outcome == SealedWriteOutcome.Failed && Error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Error).Throw();
        }
    }
}

/// <param name="MayCreate">Whether this install may create the file when there is none. An install that has
/// seen the file before, or one that only reads (the partner's), passes false, so a search that missed it
/// cannot create a second copy beside it.</param>
/// <param name="Verify">Read the file back after writing and put the previous one back if it does not open.</param>
public sealed record SealedWriteOptions(bool MayCreate = true, bool Verify = true)
{
    public static SealedWriteOptions Default { get; } = new();
}

/// <summary>
/// The suite's one rule for an encrypted Drive file (G14), which six files used to implement six ways
/// (duplication audit §5): write only over a file that opened, or over none when this install may create it;
/// never over a wrong passphrase, a damaged file, a newer format or a read that failed; never over a file that
/// changed since it was read; and read every write back, putting the previous file back if it does not open.
/// The payload is the caller's: this class seals and opens text and knows nothing of what is inside, except
/// through the optional <c>isNewerFormat</c> test on the plaintext.
/// </summary>
public sealed class SealedDriveFile
{
    private readonly IDriveTextFile file;
    private readonly Func<string, bool>? isNewerFormat;

    public SealedDriveFile(IDriveTextFile file, Func<string, bool>? isNewerFormat = null)
    {
        this.file = file;
        this.isNewerFormat = isNewerFormat;
    }

    public async Task<SealedRead> ReadAsync(string key, CancellationToken cancellationToken)
    {
        string? revision;
        string? raw;
        try
        {
            // The revision first: a write landing between the two makes the content newer than the revision,
            // which can only cause a needless conflict, never a missed one.
            revision = await RevisionAsync(cancellationToken);
            raw = await file.ReadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SealedRead(SealedReadStatus.Unreachable, null, null, null, Describe("Drive could not be read", ex)) { Error = ex };
        }

        return Open(raw, key, revision);
    }

    public async Task<SealedWriteResult> WriteAsync(
        string plaintext, string key, SealedRead basis, SealedWriteOptions options, CancellationToken cancellationToken)
    {
        if (!basis.Writable)
        {
            return new SealedWriteResult(SealedWriteOutcome.Refused, RefusalFor(basis));
        }
        if (basis.Status == SealedReadStatus.Absent && !options.MayCreate)
        {
            return new SealedWriteResult(SealedWriteOutcome.Refused,
                "There is no file, and this install may not create one; it may only write over the file it has read.");
        }

        try
        {
            if (await RevisionAsync(cancellationToken) != basis.Revision)
            {
                return new SealedWriteResult(SealedWriteOutcome.Conflict, "The file changed since it was read.");
            }

            await file.WriteAsync(VaultEnvelope.Write(plaintext, key), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SealedWriteResult(SealedWriteOutcome.Failed, Describe("The file could not be written", ex)) { Error = ex };
        }

        return options.Verify
            ? await ConfirmAsync(plaintext, key, basis, cancellationToken)
            : new SealedWriteResult(SealedWriteOutcome.Written);
    }

    private async Task<SealedWriteResult> ConfirmAsync(string plaintext, string key, SealedRead basis, CancellationToken cancellationToken)
    {
        string? readBack;
        try
        {
            readBack = await file.ReadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SealedWriteResult(SealedWriteOutcome.Unverified, Describe("The file was written but could not be read back", ex));
        }

        var opened = Open(readBack, key, null);
        if (opened.Status == SealedReadStatus.Loaded && opened.Plaintext == plaintext)
        {
            return new SealedWriteResult(SealedWriteOutcome.Written);
        }

        try
        {
            // Not the caller's token: leaving mid-call must not leave a damaged file in place.
            if (basis.Raw is not null)
            {
                await file.WriteAsync(basis.Raw, CancellationToken.None);
            }
            else if (file is IDriveFileTrash trash)
            {
                await trash.TrashAsync(CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SealedWriteResult(SealedWriteOutcome.Failed,
                Describe("The file did not read back, and the previous file could not be put back", ex));
        }

        return new SealedWriteResult(SealedWriteOutcome.Unconfirmed, "The file did not read back as written, so the previous file was put back.");
    }

    private SealedRead Open(string? raw, string key, string? revision)
    {
        (SecretsVaultStatus Status, string? Plaintext) opened;
        try
        {
            opened = VaultEnvelope.Read(raw, key);
        }
        catch (ArgumentException ex)
        {
            // A nonce, tag or iteration count of the wrong size reaches the cipher as an argument error.
            return new SealedRead(SealedReadStatus.Corrupt, null, raw, revision, "The file is damaged (" + ex.GetType().Name + ").");
        }

        return opened.Status switch
        {
            SecretsVaultStatus.Empty => new SealedRead(SealedReadStatus.Absent, null, null, revision),
            SecretsVaultStatus.WrongPassphrase => new SealedRead(SealedReadStatus.WrongPassphrase, null, raw, revision),
            SecretsVaultStatus.Corrupt => new SealedRead(SealedReadStatus.Corrupt, null, raw, revision),
            _ when isNewerFormat?.Invoke(opened.Plaintext!) == true =>
                new SealedRead(SealedReadStatus.NewerFormat, null, raw, revision),
            _ => new SealedRead(SealedReadStatus.Loaded, opened.Plaintext, raw, revision),
        };
    }

    private async Task<string?> RevisionAsync(CancellationToken cancellationToken) =>
        file is IDriveFileRevision revisioned ? await revisioned.RevisionAsync(cancellationToken) : null;

    private static string RefusalFor(SealedRead basis) => basis.Status switch
    {
        SealedReadStatus.WrongPassphrase => "The file there does not open with this passphrase, so it is not written over.",
        SealedReadStatus.Corrupt => "The file there is damaged, so it is not written over.",
        SealedReadStatus.NewerFormat => "The file there was written by a newer app; update this one first.",
        SealedReadStatus.Unreachable => "Drive could not be read, so nothing was written.",
        _ => "The file is not one this install may write over.",
    };

    // The exception's type and message only: Drive's messages name the request, not the payload.
    private static string Describe(string what, Exception ex) => $"{what}: {ex.GetType().Name}: {ex.Message}";
}
