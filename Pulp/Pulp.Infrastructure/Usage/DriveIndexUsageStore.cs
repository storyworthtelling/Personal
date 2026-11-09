using Pulp.Domain.Services;
using Pulp.Domain.Usage;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Pulp.Infrastructure.Usage;

/// <summary>
/// The usage file exists in Drive and this phone cannot open it, or has no passphrase to try. Thrown instead of
/// starting a fresh file, and before any write (G14, the prayer-archive rule): a file that will not open may hold
/// months this phone never saw.
/// </summary>
public sealed class IndexUsageUnreadableException(string message) : Exception(message);

/// <summary>
/// <see cref="IndexUsageFile.FileName"/> in the owner's Drive, encrypted with <see cref="VaultEnvelope"/> under the
/// household passphrase (D320). Read, merge, write only if something changed, under the suite's sealed-file rule: a
/// write is checked against the revision it read at, so the other phone's write in between is merged in (read
/// again, up to <see cref="MaxConflicts"/> times) rather than rolling its month back (contracts M6), and every write
/// is read back. Drive being unreachable is not this class's to decide about: the transport's exception propagates
/// unchanged.
/// </summary>
public sealed class DriveIndexUsageStore
{
    /// <summary>How many times one publish reads again after finding the file changed under it.</summary>
    public const int MaxConflicts = 3;

    private readonly SealedDriveFile sealedFile;
    private readonly Func<string> passphrase;
    private readonly ServiceCatalogue catalogue;
    private readonly Func<bool> mayCreate;

    public DriveIndexUsageStore(IDriveTextFile file, Func<string> passphrase, ServiceCatalogue catalogue, Func<bool> mayCreate)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(catalogue);
        sealedFile = new SealedDriveFile(file);
        this.passphrase = passphrase;
        ArgumentNullException.ThrowIfNull(mayCreate);
        this.catalogue = catalogue;
        this.mayCreate = mayCreate;
    }

    public async Task PublishAsync(IReadOnlyList<IndexUsagePayload> payloads, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        var key = passphrase();
        if (string.IsNullOrEmpty(key))
        {
            throw new IndexUsageUnreadableException(
                $"'{IndexUsageFile.FileName}' in Drive was neither read nor written: there is no vault passphrase on this phone.");
        }

        for (var attempt = 1; ; attempt++)
        {
            var read = await sealedFile.ReadAsync(key, cancellationToken);
            read.ThrowIfUnreachable();
            IReadOnlyList<IndexUsageEntry> existing = [];
            switch (read.Status)
            {
                case SealedReadStatus.Absent:
                    break;
                case SealedReadStatus.Loaded:
                    if (!IndexUsageFile.TryRead(read.Plaintext, out var entries))
                    {
                        throw new IndexUsageUnreadableException(
                            $"'{IndexUsageFile.FileName}' in Drive opened but is not a usage file this version can read; it is left exactly as it is.");
                    }

                    existing = entries!;
                    break;
                default:
                    throw new IndexUsageUnreadableException(
                        $"'{IndexUsageFile.FileName}' in Drive does not open with this phone's passphrase ({read.Status}); it is left exactly as it is.");
            }

            var merged = IndexUsageFile.Apply(existing, payloads, catalogue);
            if (read.Status == SealedReadStatus.Loaded && merged.SequenceEqual(existing))
            {
                return;
            }

            // Only the owner's account creates the file, as with the household's own: one made in her Drive would
            // be a second file the owner's install never finds. An absent file waits for the owner to share it.
            if (read.Status == SealedReadStatus.Absent && (merged.Count == 0 || !mayCreate()))
            {
                return;
            }

            var written = await sealedFile.WriteAsync(
                IndexUsageFile.Serialize(merged, catalogue), key, read, new SealedWriteOptions(MayCreate: mayCreate()), cancellationToken);
            written.ThrowIfFailed();
            if (written.Succeeded)
            {
                return;
            }
            if (written.Outcome == SealedWriteOutcome.Conflict && attempt <= MaxConflicts)
            {
                continue;
            }

            throw new IOException($"'{IndexUsageFile.FileName}' was not written: {written.Detail}");
        }
    }
}
