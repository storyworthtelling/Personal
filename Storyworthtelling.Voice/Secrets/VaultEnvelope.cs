using System.Text.Json;

namespace Storyworthtelling.Voice.Secrets;

/// <summary>
/// The part of a vault that has nothing to do with what is inside it: turning the
/// opaque file on Drive into plaintext and back, and telling the four ways that can
/// go apart.
///
/// Extracted because there are two vaults now — the household's shared one and one
/// per person — and they differ only in their payload type and their file name.
/// Everything else is identical, and two copies of "which failure is a wrong
/// passphrase and which is a corrupt file" is exactly the code that drifts: the day
/// they disagree, one vault tells someone to re-pair a phone that is fine.
/// </summary>
internal static class VaultEnvelope
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Decrypts a file's contents, or says which way it failed. The plaintext is
    /// non-null only when the status is <see cref="SecretsVaultStatus.Loaded"/>, and
    /// even then the caller still has to make sense of it as JSON.
    /// </summary>
    internal static (SecretsVaultStatus Status, string? Plaintext) Read(string? content, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (SecretsVaultStatus.Empty, null);
        }

        SecretsEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SecretsEnvelope>(content, SerializerOptions);
        }
        catch (JsonException)
        {
            return (SecretsVaultStatus.Corrupt, null);
        }

        if (envelope is null || string.IsNullOrEmpty(envelope.Ciphertext))
        {
            return (SecretsVaultStatus.Corrupt, null);
        }

        try
        {
            return (SecretsVaultStatus.Loaded, SecretsCrypto.Decrypt(envelope, passphrase));
        }
        catch (WrongPassphraseException)
        {
            return (SecretsVaultStatus.WrongPassphrase, null);
        }
        catch (FormatException)
        {
            // Base64 fields that aren't valid base64 — the envelope is malformed, not
            // the passphrase.
            return (SecretsVaultStatus.Corrupt, null);
        }
    }

    internal static string Write(string plaintext, string passphrase) =>
        JsonSerializer.Serialize(SecretsCrypto.Encrypt(plaintext, passphrase), SerializerOptions);
}
