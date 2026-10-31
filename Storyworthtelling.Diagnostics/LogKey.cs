using System.Security.Cryptography;
using System.Text;

namespace Storyworthtelling.Diagnostics;

/// <summary>
/// The log key (plan §6.2, A4): PBKDF2-SHA256 over the vault passphrase with the salt
/// <c>storyworthtelling-log-key/&lt;epoch&gt;</c>, 210,000 iterations (the vault's own count), 32 bytes, as
/// lower-case hex. Phones derive it in memory and seal batches with it; the reader's environment holds this
/// key and never the passphrase, and PBKDF2 is one-way, so a leaked session can read the logs but not the
/// vault, the prayer archive, the snowball history, the household file or the body backup.
/// </summary>
public static class LogKey
{
    /// <summary>Carried in every batch header. Bump it in a reviewed PR to rotate the key.</summary>
    public const int Epoch = 1;

    public const int Iterations = 210_000;
    public const string SaltPrefix = "storyworthtelling-log-key/";
    private const int KeyBytes = 32;

    public static string Derive(string passphrase, int epoch)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            throw new ArgumentException("A log key needs the vault passphrase; an empty one would be a key anyone could guess.", nameof(passphrase));
        }
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            Encoding.UTF8.GetBytes(SaltPrefix + epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Iterations,
            HashAlgorithmName.SHA256,
            KeyBytes);
        return Convert.ToHexString(key).ToLowerInvariant();
    }
}
