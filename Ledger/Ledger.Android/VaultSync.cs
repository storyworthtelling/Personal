using Ledger.Infrastructure.Services;
using Storyworthtelling.Voice;
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
    private readonly InMemoryDriveFileIdStore fileIdStore;

    public VaultSync(SecretsVaultService vault, AccountService accounts, InMemoryDriveFileIdStore fileIdStore)
    {
        this.vault = vault;
        this.accounts = accounts;
        this.fileIdStore = fileIdStore;
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
            },
            // Fold this session's captured Drive file ids into the shared map (id wins per name).
            DriveFileIds = MergeIds(v.DriveFileIds, fileIdStore.Snapshot())
        }, cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> MergeIds(
        IReadOnlyDictionary<string, string> existing, IReadOnlyDictionary<string, string> captured)
    {
        var merged = new Dictionary<string, string>(existing);
        foreach (var (name, id) in captured)
            merged[name] = id;
        return merged;
    }

    // Fills in the Plaid credentials this install is missing and re-creates any bank the database doesn't
    // have. Existing local values win — restore never overwrites credentials or tokens already present here.
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var result = await vault.LoadAsync(cancellationToken);
        if (result.Status != SecretsVaultStatus.Loaded)
            return;

        // Seed the Drive-by-ID cache so well-known files resolve by remembered id (surviving a rename).
        foreach (var (name, id) in result.Vault.DriveFileIds)
            fileIdStore.Set(name, id);

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
