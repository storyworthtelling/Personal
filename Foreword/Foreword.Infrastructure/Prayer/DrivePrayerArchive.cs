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

    public async Task<PrayerArchiveRead> LoadAsync(
        string passphrase, CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);

        // No passphrase means no archive. Not an error — it is the state of an app whose passphrase
        // a factory reset has wiped, and the answer is a page with nothing on it until it is
        // re-entered.
        if (string.IsNullOrEmpty(passphrase))
        {
            return PrayerArchiveRead.Nothing(PrayerArchiveStatus.Empty);
        }

        var (status, plaintext) = VaultEnvelope.Read(content, passphrase);

        return status == SecretsVaultStatus.Loaded
            ? new PrayerArchiveRead(PrayerArchiveStatus.Loaded, PrayerArchiveCodec.Read(plaintext))
            : PrayerArchiveRead.Nothing(Told(status));
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

        // Read it back before writing. The archive is the only copy, so "I could not open it" has to
        // stop the write rather than be logged beside it.
        var existing = await file.ReadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            var (status, _) = VaultEnvelope.Read(existing, passphrase);
            if (status != SecretsVaultStatus.Loaded)
            {
                throw new PrayerArchiveLockedException(
                    "The prayer archive was not written: the file already in Drive could not be "
                    + $"read ({Told(status)}), and it is the only copy of what was written.");
            }
        }

        await file.WriteAsync(
            VaultEnvelope.Write(PrayerArchiveCodec.Write(archive, this), passphrase),
            cancellationToken);
    }

    /// <summary>
    /// The vault's four statuses in the Domain's own words. Mapped rather than shared because Domain
    /// carries no project references — it is one switch, and it is the only place the two vocabularies
    /// meet.
    /// </summary>
    private static PrayerArchiveStatus Told(SecretsVaultStatus status) => status switch
    {
        SecretsVaultStatus.Loaded => PrayerArchiveStatus.Loaded,
        SecretsVaultStatus.WrongPassphrase => PrayerArchiveStatus.WrongPassphrase,
        SecretsVaultStatus.Corrupt => PrayerArchiveStatus.Corrupt,
        _ => PrayerArchiveStatus.Empty,
    };
}
