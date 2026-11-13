using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Voice.Secrets;

/// <summary>
/// Which value a restore keeps for one credential (audit 11 §13). Restore used to be "local wins": an empty slot took
/// the vault's value and a filled one never did, so once the owner re-paired the tablet or rotated a key in one app,
/// every other app kept its dead copy. Now a local value whose last use was refused yields to a different vault value.
/// A working or untried local value stays, and a blank vault never clears anything.
/// </summary>
public static class VaultRestore
{
    /// <returns>The value to store, or null to keep the local one.</returns>
    public static string? Take(string? local, string? vault, CredentialHealth localHealth)
    {
        if (string.IsNullOrWhiteSpace(vault))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(local))
        {
            return vault;
        }

        return localHealth.Status == CredentialStatus.Failing && !string.Equals(local, vault, StringComparison.Ordinal)
            ? vault
            : null;
    }

    /// <summary>
    /// Applies <see cref="Take"/>: stores the vault value through <paramref name="store"/> when it should be taken, and
    /// forgets the replaced value's refusal in <paramref name="health"/> so Settings stops asking to pair again.
    /// </summary>
    /// <returns>Whether the value was replaced.</returns>
    public static bool Apply(string? local, string? vault, string credentialId, CredentialHealthStore health, Action<string> store)
    {
        var taken = Take(local, vault, health.Load().Get(credentialId));
        if (taken is null)
        {
            return false;
        }

        store(taken);
        health.Forget(credentialId);
        return true;
    }
}
