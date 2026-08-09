using System.Security.Cryptography;
using System.Text;

namespace Storyworthtelling.Voice.Secrets;

// Passphrase-based authenticated encryption for the vault, using only the BCL: PBKDF2-SHA256 derives a
// 256-bit key from the passphrase and a random per-file salt, and AES-256-GCM encrypts the plaintext with
// a random nonce, producing an authentication tag. Decrypt verifies that tag, so a wrong passphrase (or any
// tampering) fails loudly with WrongPassphraseException rather than returning garbage. No third-party crypto
// dependency — this satisfies the repo's "implement it directly / Microsoft only" rule.
public static class SecretsCrypto
{
    // High enough to be costly to brute-force, low enough to stay instant on a phone. Stored in the envelope
    // so an older file with a different count still decrypts.
    public const int DefaultIterations = 210_000;

    private const int SaltSize = 16;
    private const int NonceSize = 12; // AES-GCM standard nonce length
    private const int TagSize = 16;   // AES-GCM standard tag length
    private const int KeySize = 32;   // AES-256

    public static SecretsEnvelope Encrypt(string plaintext, string passphrase, int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("A passphrase is required to encrypt the vault.", nameof(passphrase));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(passphrase, salt, iterations);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return new SecretsEnvelope(
            SecretsEnvelope.CurrentVersion,
            SecretsEnvelope.Pbkdf2Sha256,
            iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(cipherBytes),
            Convert.ToBase64String(tag));
    }

    // Returns the decrypted plaintext, or throws WrongPassphraseException if the passphrase is wrong or the
    // envelope was tampered with (both surface as a failed GCM tag).
    public static string Decrypt(SecretsEnvelope envelope, string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("A passphrase is required to decrypt the vault.", nameof(passphrase));

        var salt = Convert.FromBase64String(envelope.Salt);
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var cipherBytes = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.Tag);
        var key = DeriveKey(passphrase, salt, envelope.Iterations);

        var plainBytes = new byte[cipherBytes.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }
        catch (CryptographicException ex)
        {
            throw new WrongPassphraseException(ex);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, KeySize);
}

// Thrown when the vault can't be decrypted with the supplied passphrase — the passphrase is wrong or the
// file was altered. Kept distinct so the UI can say "wrong passphrase" instead of a generic crypto error.
public sealed class WrongPassphraseException : Exception
{
    public WrongPassphraseException(Exception inner)
        : base("The vault could not be decrypted — the passphrase is wrong or the file is corrupt.", inner)
    {
    }
}
