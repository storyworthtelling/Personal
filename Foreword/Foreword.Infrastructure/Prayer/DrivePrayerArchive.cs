using Foreword.Domain.Interfaces;
using Foreword.Domain.Prayer;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Foreword.Infrastructure.Prayer;

/// <summary>
/// Thrown when a save would write over an archive this code could not read.
///
/// <para>It is not an error condition to work around. It is the archive refusing to be destroyed.</para>
/// </summary>
public sealed class PrayerArchiveLockedException(string message) : Exception(message);

/// <summary>
/// The private prayer archive: one file in the owner's Drive, encrypted with the vault passphrase
/// they already enter.
///
/// <para>Encryption was the owner's choice over plaintext, and the reason is what is in here:
/// anyone who reaches the Drive — a shared laptop, a future connector, a support session — sees
/// ciphertext. The cost was stated and accepted: lose the passphrase and the archive is gone, and it
/// cannot be read by eye.</para>
///
/// <para><b>The rule that outranks every feature here:</b> a save can never write over an archive
/// this code could not read. The journal page is trashed off the tablet once it has been
/// transcribed, so this file is the only copy — and the failure it would otherwise have is the worst
/// shape a failure can take: a morning where the passphrase is wrong, an empty archive is dutifully
/// encrypted over the top of a real one, and nothing anywhere says so. So <see cref="SaveAsync"/>
/// reads the file back first and refuses rather than overwriting.</para>
///
/// <para>This is one of the four declared <see cref="IPrayerReader"/>s: writing the file is the one
/// moment the text has to become a string.</para>
/// </summary>
public sealed class DrivePrayerArchive(IDriveTextFile file) : IPrayerArchive, IPrayerReader
{
    /// <summary>
    /// The address, not a description. The archive is found by name across installs, so a rename
    /// orphans a file that is still sitting there holding everything the owner wrote.
    /// </summary>
    public const string FileName = "storyworthtelling-prayer-archive.json";

    private readonly SealedDriveFile sealedFile = new(file);

    public async Task<PrayerArchiveRead> LoadAsync(
        string passphrase, CancellationToken cancellationToken)
    {
        // No passphrase means no archive. Not an error — it is the state of an app whose passphrase
        // a factory reset has wiped, and the answer is a page with nothing on it until it is
        // re-entered.
        if (string.IsNullOrEmpty(passphrase))
        {
            await file.ReadAsync(cancellationToken);
            return PrayerArchiveRead.Nothing(PrayerArchiveStatus.Empty);
        }

        var read = await sealedFile.ReadAsync(passphrase, cancellationToken);
        read.ThrowIfUnreachable();

        return read.Status == SealedReadStatus.Loaded
            ? new PrayerArchiveRead(PrayerArchiveStatus.Loaded, PrayerArchiveCodec.Read(read.Plaintext))
            : PrayerArchiveRead.Nothing(Told(read.Status));
    }

    public async Task SaveAsync(
        PrayerArchive archive, string passphrase, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new PrayerArchiveLockedException(
                "The prayer archive was not written: there is no vault passphrase, and encrypting "
                + "the journal under an empty one would make it unreadable to every other install.");
        }

        // Read it before writing, under the suite's sealed-file rule. The archive is the only copy, so
        // "I could not open it" has to stop the write rather than be logged beside it; and the write is
        // read back, so a damaged upload is put back rather than trusted before the page is trashed.
        var existing = await sealedFile.ReadAsync(passphrase, cancellationToken);
        existing.ThrowIfUnreachable();
        if (!existing.Writable)
        {
            throw new PrayerArchiveLockedException(
                "The prayer archive was not written: the file already in Drive could not be "
                + $"read ({Told(existing.Status)}), and it is the only copy of what was written.");
        }

        var written = await sealedFile.WriteAsync(
            PrayerArchiveCodec.Write(archive, this), passphrase, existing, SealedWriteOptions.Default, cancellationToken);
        written.ThrowIfFailed();
        if (written.Outcome == SealedWriteOutcome.Refused)
        {
            throw new PrayerArchiveLockedException("The prayer archive was not written: " + written.Detail);
        }
        if (!written.Succeeded)
        {
            // Not confirmed: the morning stops here, so the journal page is not trashed off the tablet.
            throw new IOException("The prayer archive was not confirmed in Drive: " + written.Detail);
        }
    }

    /// <summary>
    /// The vault's four statuses in the Domain's own words. Mapped rather than shared because Domain
    /// carries no project references — it is one switch, and it is the only place the two vocabularies
    /// meet.
    /// </summary>
    private static PrayerArchiveStatus Told(SealedReadStatus status) => status switch
    {
        SealedReadStatus.Loaded => PrayerArchiveStatus.Loaded,
        SealedReadStatus.WrongPassphrase => PrayerArchiveStatus.WrongPassphrase,
        SealedReadStatus.Absent => PrayerArchiveStatus.Empty,
        _ => PrayerArchiveStatus.Corrupt,
    };
}
