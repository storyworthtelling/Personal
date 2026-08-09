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

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IDriveTextFile file;

    public DriveSecretsVaultStore(IDriveTextFile file)
    {
        this.file = file;
    }

    public async Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return SecretsVaultLoadResult.Empty;

        SecretsEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SecretsEnvelope>(content, SerializerOptions);
        }
        catch (JsonException)
        {
            return SecretsVaultLoadResult.Corrupt;
        }

        if (envelope is null || string.IsNullOrEmpty(envelope.Ciphertext))
            return SecretsVaultLoadResult.Corrupt;

        string plaintext;
        try
        {
            plaintext = SecretsCrypto.Decrypt(envelope, passphrase);
        }
        catch (WrongPassphraseException)
        {
            return SecretsVaultLoadResult.WrongPassphrase;
        }
        catch (FormatException)
        {
            // Base64 fields that aren't valid base64 — the envelope is malformed, not the passphrase.
            return SecretsVaultLoadResult.Corrupt;
        }

        try
        {
            var vault = JsonSerializer.Deserialize<SecretsVault>(plaintext, SerializerOptions);
            return vault is null ? SecretsVaultLoadResult.Corrupt : SecretsVaultLoadResult.Loaded(vault);
        }
        catch (JsonException)
        {
            return SecretsVaultLoadResult.Corrupt;
        }
    }

    public Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken)
    {
        var plaintext = JsonSerializer.Serialize(vault, SerializerOptions);
        var envelope = SecretsCrypto.Encrypt(plaintext, passphrase);
        return file.WriteAsync(JsonSerializer.Serialize(envelope, SerializerOptions), cancellationToken);
    }
}
