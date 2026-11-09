using System.Text.Json;

namespace Storyworthtelling.Voice.Secrets;

// The encrypted vault as ONE JSON file in the owner's Google Drive, found by name over the same narrow
// IDriveTextFile seam the voice profile/config use. The file on Drive is a SecretsEnvelope (opaque
// ciphertext); this store turns it into a decrypted SecretsVault and back. Every failure short of a wrong
// passphrase degrades to a distinct, non-throwing status so a restore flow never crashes an app.
public sealed class DriveSecretsVaultStore : ISecretsVaultStore
{
    // The shared file's name — one vault, referenced by name across every app (like the voice files).
    public const string FileName = "storyworthtelling-secrets.json";

    private readonly IDriveTextFile file;
    private readonly SealedDriveFile sealedFile;

    public DriveSecretsVaultStore(IDriveTextFile file)
    {
        this.file = file;
        sealedFile = new SealedDriveFile(file, plaintext => VaultSchema.Of(plaintext) > SecretsVault.CurrentSchema);
    }

    public async Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken)
    {
        // Read first, then ask for ownership: the same file either way. A file that cannot report it counts
        // as shared, so the install reads it and is refused any write.
        var ownership = file is IDriveFileOwnership owned
            ? await owned.OwnershipAsync(cancellationToken)
            : DriveFileOwnership.Shared;

        var read = await sealedFile.ReadAsync(passphrase, cancellationToken);
        // Not a status: a vault Drive could not be asked about is not an empty one (error-handling §1).
        read.ThrowIfUnreachable();
        if (read.Status != SealedReadStatus.Loaded)
        {
            return new SecretsVaultLoadResult(Map(read.Status), SecretsVault.Empty, ownership)
            {
                Basis = read,
                IsNewerFormat = read.Status == SealedReadStatus.NewerFormat,
            };
        }

        try
        {
            var vault = JsonSerializer.Deserialize<SecretsVault>(read.Plaintext!, VaultEnvelope.SerializerOptions);
            return vault is null
                ? new SecretsVaultLoadResult(SecretsVaultStatus.Corrupt, SecretsVault.Empty, ownership) { Basis = read with { Status = SealedReadStatus.Corrupt } }
                : new SecretsVaultLoadResult(SecretsVaultStatus.Loaded, vault, ownership) { Basis = read };
        }
        catch (JsonException)
        {
            return new SecretsVaultLoadResult(SecretsVaultStatus.Corrupt, SecretsVault.Empty, ownership) { Basis = read with { Status = SealedReadStatus.Corrupt } };
        }
    }

    public async Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken) =>
        await WriteOverAsync(vault, passphrase, await sealedFile.ReadAsync(passphrase, cancellationToken), cancellationToken);

    public Task SaveAsync(SecretsVault vault, string passphrase, SecretsVaultLoadResult basis, CancellationToken cancellationToken) =>
        basis.Basis is { } read
            ? WriteOverAsync(vault, passphrase, read, cancellationToken)
            : SaveAsync(vault, passphrase, cancellationToken);

    private async Task WriteOverAsync(SecretsVault vault, string passphrase, SealedRead basis, CancellationToken cancellationToken)
    {
        var result = await sealedFile.WriteAsync(
            JsonSerializer.Serialize(vault, VaultEnvelope.SerializerOptions), passphrase, basis, SealedWriteOptions.Default, cancellationToken);
        if (!result.Succeeded)
        {
            throw new VaultWriteException(result);
        }
    }

    private static SecretsVaultStatus Map(SealedReadStatus status) => status switch
    {
        SealedReadStatus.Absent => SecretsVaultStatus.Empty,
        SealedReadStatus.WrongPassphrase => SecretsVaultStatus.WrongPassphrase,
        _ => SecretsVaultStatus.Corrupt,
    };
}
