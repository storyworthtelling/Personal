using Storyworthtelling.Voice.Secrets;

namespace Index.Infrastructure.Vault;

/// <summary>
/// How an install folds its Plaid section into the shared vault. A Save merges; it never replaces.
///
/// <para>An install that holds no Plaid credentials of its own — hers, always; the owner's before
/// restore has run — has nothing in this section to say. Writing its blank values would erase the
/// owner's client id, secret and every bank access token from the vault, which is the only copy that
/// survives a factory reset. So with no local credentials the section is returned untouched. With
/// credentials the section is written as before, except that a blank field never overwrites a
/// non-blank one already in the vault.</para>
/// </summary>
public static class PlaidVaultMerge
{
    public static SecretsVault Apply(
        SecretsVault vault, string localClientId, string localSecret, string environment,
        IReadOnlyList<PlaidItem> localItems)
    {
        if (string.IsNullOrEmpty(localClientId) && string.IsNullOrEmpty(localSecret))
        {
            return vault;
        }

        var existing = vault.Plaid;
        return vault with
        {
            Plaid = new PlaidSecrets(
                string.IsNullOrEmpty(localClientId) ? existing.ClientId : localClientId,
                string.IsNullOrEmpty(localSecret) ? existing.Secret : localSecret,
                environment)
            {
                Items = localItems,
            },
        };
    }
}
