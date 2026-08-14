using Ledger.Infrastructure.Services;
using Storyworthtelling.Voice.Secrets;

namespace Ledger.Android;

// Keeps Ledger's section of the shared Drive secrets vault in sync: the Plaid client id/secret/environment
// (local Preferences) and every connected bank's durable access token (the SQLite database, via
// AccountService). Publishing runs after a settings save, a connect, or a remove; restoring runs at startup
// so a reinstalled app (e.g. after the FocusPhone cutover's factory reset) repopulates its Plaid credentials
// and re-creates its institutions from Drive instead of re-linking every bank by hand. Best-effort — Ledger
// works from local state whether or not the vault is reachable.
public sealed class VaultSync
{
    private readonly SecretsVaultService vault;
    private readonly AccountService accounts;

    public VaultSync(SecretsVaultService vault, AccountService accounts)
    {
        this.vault = vault;
        this.accounts = accounts;
    }

    // Writes the Plaid credentials + all bank access tokens into the vault, leaving other apps' sections
    // alone. Returns the publish outcome so the caller can surface a failed backup instead of hiding it.
    public async Task<SecretsVaultPublishResult> PublishAsync(CancellationToken cancellationToken)
    {
        var credentials = await accounts.GetInstitutionCredentialsAsync(cancellationToken);
        var items = credentials
            .Select(c => new PlaidItem(c.ItemId, c.AccessToken, c.Name))
            .ToList();

        return await vault.PublishAsync(v => v with
        {
            Plaid = new PlaidSecrets(
                SettingsStore.PlaidClientId,
                SettingsStore.PlaidSecret,
                SettingsStore.PlaidEnvironment)
            {
                Items = items
            }
        }, cancellationToken);
    }

    // Fills in the Plaid credentials this install is missing and re-creates any bank the database doesn't
    // have. Existing local values win — restore never overwrites credentials or tokens already present here.
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var result = await vault.LoadAsync(cancellationToken);
        if (result.Status != SecretsVaultStatus.Loaded)
            return;

        var plaid = result.Vault.Plaid;
        if (string.IsNullOrEmpty(SettingsStore.PlaidClientId) && !string.IsNullOrEmpty(plaid.ClientId))
        {
            SettingsStore.PlaidClientId = plaid.ClientId;
            // The environment isn't a secret and defaults to "sandbox"; adopt the vault's value only on this
            // fresh restore, so it can't later flip a deliberate local choice.
            if (!string.IsNullOrEmpty(plaid.Environment))
                SettingsStore.PlaidEnvironment = plaid.Environment;
        }
        if (string.IsNullOrEmpty(SettingsStore.PlaidSecret) && !string.IsNullOrEmpty(plaid.Secret))
            SettingsStore.PlaidSecret = plaid.Secret;

        var restored = plaid.Items.Select(i => new InstitutionCredential(i.ItemId, i.AccessToken, i.Name));
        await accounts.RestoreInstitutionsAsync(restored, cancellationToken);
    }
}
