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
/// household passphrase (D320). Read, merge, write only if something changed. Drive being unreachable is not this
/// class's to decide about: the transport's exception propagates unchanged.
/// </summary>
public sealed class DriveIndexUsageStore
{
    private readonly IDriveTextFile file;
    private readonly Func<string> passphrase;
    private readonly ServiceCatalogue catalogue;
    private readonly Func<bool> mayCreate;

    public DriveIndexUsageStore(IDriveTextFile file, Func<string> passphrase, ServiceCatalogue catalogue, Func<bool> mayCreate)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(catalogue);
        this.file = file;
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

        var content = await file.ReadAsync(cancellationToken);
        var (status, plaintext) = VaultEnvelope.Read(content, key);
        IReadOnlyList<IndexUsageEntry> existing = [];
        switch (status)
        {
            case SecretsVaultStatus.Empty:
                break;
            case SecretsVaultStatus.Loaded:
                if (!IndexUsageFile.TryRead(plaintext, out var read))
                {
                    throw new IndexUsageUnreadableException(
                        $"'{IndexUsageFile.FileName}' in Drive opened but is not a usage file this version can read; it is left exactly as it is.");
                }

                existing = read!;
                break;
            default:
                throw new IndexUsageUnreadableException(
                    $"'{IndexUsageFile.FileName}' in Drive does not open with this phone's passphrase ({status}); it is left exactly as it is.");
        }

        var merged = IndexUsageFile.Apply(existing, payloads, catalogue);
        if (status == SecretsVaultStatus.Loaded && merged.SequenceEqual(existing))
        {
            return;
        }

        // Only the owner's account creates the file, as with the household's own: one made in her Drive would
        // be a second file the owner's install never finds. An absent file waits for the owner to share it.
        if (status == SecretsVaultStatus.Empty && (merged.Count == 0 || !mayCreate()))
        {
            return;
        }

        await file.WriteAsync(VaultEnvelope.Write(IndexUsageFile.Serialize(merged, catalogue), key), cancellationToken);
    }
}
