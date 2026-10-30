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

    public DriveSecretsVaultStore(IDriveTextFile file)
    {
        this.file = file;
    }

    public async Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken)
    {
        // Read first, then ask for ownership: the same file either way. A file that cannot report it counts
        // as shared, so the install reads it and is refused any write.
        var ownership = file is IDriveFileOwnership owned
            ? await owned.OwnershipAsync(cancellationToken)
            : DriveFileOwnership.Shared;

        var (status, plaintext) = VaultEnvelope.Read(await file.ReadAsync(cancellationToken), passphrase);
        if (status != SecretsVaultStatus.Loaded)
        {
            return new SecretsVaultLoadResult(status, SecretsVault.Empty, ownership);
        }

        try
        {
            var vault = JsonSerializer.Deserialize<SecretsVault>(plaintext!, VaultEnvelope.SerializerOptions);
            return vault is null
                ? new SecretsVaultLoadResult(SecretsVaultStatus.Corrupt, SecretsVault.Empty, ownership)
                : new SecretsVaultLoadResult(SecretsVaultStatus.Loaded, vault, ownership);
        }
        catch (JsonException)
        {
            return new SecretsVaultLoadResult(SecretsVaultStatus.Corrupt, SecretsVault.Empty, ownership);
        }
    }

    public Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken) =>
        file.WriteAsync(
            VaultEnvelope.Write(JsonSerializer.Serialize(vault, VaultEnvelope.SerializerOptions), passphrase),
            cancellationToken);
}
