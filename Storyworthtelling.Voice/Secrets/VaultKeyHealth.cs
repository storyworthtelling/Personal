using System.Security.Cryptography;
using System.Text;
using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Voice.Secrets;

/// <summary>
/// Carries the verdict on each shared credential between apps through the vault (audit 11 §13). A 401 seen by Post or
/// Plate used to stay in that app's own health store, so Folio, which holds the Claude key field, kept saying
/// "Working ✓".
///
/// <para>A verdict is about one value. Each entry is stamped with a fingerprint of the vault's value when it was
/// written, and is applied only while the vault still holds that value and the app reading it uses the same one. A
/// rotated key therefore starts with no verdict rather than inheriting the old key's refusal, and an app holding an old
/// local copy neither pushes its refusal of that copy nor takes on a verdict about a value it does not use.</para>
/// </summary>
public static class VaultKeyHealth
{
    /// <summary>The credentials whose verdicts travel: the ones every app reads from the vault.</summary>
    public static IReadOnlyList<string> Ids { get; } =
        [SharedCredentialIds.Claude, SharedCredentialIds.Gemini, SharedCredentialIds.Remarkable];

    /// <summary>The first 16 hex digits of the value's SHA-256: enough to tell keys apart, nothing to use.</summary>
    public static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())))[..16].ToLowerInvariant();

    /// <summary>The vault's value for a shared credential.</summary>
    public static string ValueOf(SecretsVault vault, string id) => id switch
    {
        SharedCredentialIds.Claude => vault.Claude.ApiKey,
        SharedCredentialIds.Gemini => vault.Gemini.ApiKey,
        SharedCredentialIds.Remarkable => vault.Remarkable.DeviceToken,
        _ => string.Empty,
    };

    /// <summary>
    /// The vault with this app's newer verdicts written in, or null when there is nothing new to write.
    /// </summary>
    /// <param name="localValue">This app's own copy of a credential, or null when it uses the vault's value directly.</param>
    public static SecretsVault? Push(SecretsVault vault, CredentialHealthReport local, Func<string, string?> localValue)
    {
        var entries = new Dictionary<string, SharedKeyHealth>(vault.KeyHealth);
        var changed = false;
        foreach (var id in Ids)
        {
            var health = local.Get(id);
            var value = ValueOf(vault, id);
            if (health.Status == CredentialStatus.Unknown || health.CheckedAtUtc is not { } checkedAt
                || string.IsNullOrWhiteSpace(value) || !UsesVaultValue(localValue(id), value))
            {
                continue;
            }

            var fingerprint = Fingerprint(value);
            if (entries.TryGetValue(id, out var known) && known.Fingerprint == fingerprint && known.CheckedAtUtc >= checkedAt)
            {
                continue;
            }

            entries[id] = new SharedKeyHealth(health.Status, health.Message, checkedAt, fingerprint);
            changed = true;
        }

        return changed ? vault with { KeyHealth = entries } : null;
    }

    /// <summary>This app's report with the vault's newer verdicts taken in, for the values this app uses.</summary>
    public static CredentialHealthReport Pull(SecretsVault vault, CredentialHealthReport local, Func<string, string?> localValue)
    {
        var report = local;
        foreach (var id in Ids)
        {
            var value = ValueOf(vault, id);
            if (!vault.KeyHealth.TryGetValue(id, out var entry) || string.IsNullOrWhiteSpace(value)
                || entry.Fingerprint != Fingerprint(value) || !UsesVaultValue(localValue(id), value))
            {
                continue;
            }

            if (local.Get(id).CheckedAtUtc is { } mine && mine >= entry.CheckedAtUtc)
            {
                continue;
            }

            report = report.With(id, new CredentialHealth(entry.Status, entry.Message, entry.CheckedAtUtc));
        }

        return report;
    }

    private static bool UsesVaultValue(string? local, string vaultValue) =>
        local is null || string.Equals(local.Trim(), vaultValue.Trim(), StringComparison.Ordinal);
}
