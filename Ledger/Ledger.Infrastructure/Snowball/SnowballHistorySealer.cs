using Index.Domain.Model;
using Storyworthtelling.Voice.Secrets;

namespace Index.Infrastructure.Snowball;

/// <summary>The history document handed to <see cref="SnowballHistorySealer.Seal"/> is not the shape the
/// app reads. The message names a position, never a value.</summary>
public sealed class SnowballHistoryFormatException(string message) : Exception(message);

/// <summary>
/// Seals the owner's Debt Snowball history for their own Drive and opens it again — the one place that
/// happens, so the console tool that writes the file and the app that reads it cannot drift apart. It is
/// the same <see cref="VaultEnvelope"/> (PBKDF2-SHA256 into AES-256-GCM, keyed by the vault passphrase) the
/// secrets vault and the household snapshot already use; nothing about the cryptography is new.
/// </summary>
public static class SnowballHistorySealer
{
    /// <summary>
    /// Validates <paramref name="plaintextJson"/> against the history shape and returns the envelope JSON
    /// text to upload. Throws <see cref="SnowballHistoryFormatException"/> for a document that is not a
    /// history (nothing is encrypted in that case) and <see cref="ArgumentException"/> for no passphrase.
    /// </summary>
    public static string Seal(string plaintextJson, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            throw new ArgumentException("A passphrase is required to seal the history.", nameof(passphrase));
        }

        var parsed = SnowballHistoryCodec.Parse(plaintextJson);
        if (!parsed.Ok)
        {
            throw new SnowballHistoryFormatException(parsed.Error!);
        }

        return VaultEnvelope.Write(plaintextJson, passphrase);
    }

    /// <summary>
    /// Opens an envelope. Never throws for a bad file or passphrase: each is a status, and the reason on an
    /// unreadable result is shape-only (never the payload, never the passphrase).
    /// </summary>
    public static SnowballHistoryReadResult Open(string? envelopeJson, string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return SnowballHistoryReadResult.NeedsPassphrase;
        }

        (SecretsVaultStatus Status, string? Plaintext) read;
        try
        {
            read = VaultEnvelope.Read(envelopeJson, passphrase);
        }
        catch (ArgumentException ex)
        {
            // A structurally damaged envelope (a missing field, an impossible iteration count or nonce size)
            // reaches the cipher as an argument error rather than a failed tag: the file is damaged, which
            // is not the same as a wrong passphrase.
            return SnowballHistoryReadResult.Unreadable($"The history file is damaged ({ex.GetType().Name}).");
        }

        switch (read.Status)
        {
            case SecretsVaultStatus.Empty:
                return SnowballHistoryReadResult.Absent;
            case SecretsVaultStatus.WrongPassphrase:
                return SnowballHistoryReadResult.WrongPassphrase;
            case SecretsVaultStatus.Loaded:
                var parsed = SnowballHistoryCodec.Parse(read.Plaintext);
                return parsed.Ok
                    ? SnowballHistoryReadResult.Loaded(parsed.Data!)
                    : SnowballHistoryReadResult.Unreadable(parsed.Error!);
            default:
                return SnowballHistoryReadResult.Unreadable("The history file is damaged.");
        }
    }
}
