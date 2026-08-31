namespace Storyworthtelling.Voice.Secrets;

// The on-Drive shape of the encrypted vault: everything needed to decrypt the ciphertext given the owner's
// passphrase, and nothing that reveals the secrets. All binary fields are base64. The plaintext inside the
// ciphertext is a serialized SecretsVault. Versioned so the format can evolve without silently misreading
// an older file.
public sealed record SecretsEnvelope(
    int Version,
    string Kdf,
    int Iterations,
    string Salt,
    string Nonce,
    string Ciphertext,
    string Tag)
{
    public const int CurrentVersion = 1;
    public const string Pbkdf2Sha256 = "pbkdf2-sha256";
}
