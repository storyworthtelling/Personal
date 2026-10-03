using Index.Domain.Household;
using Index.Infrastructure.Services;
using Index.Infrastructure.Vault;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Index.Android;

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
    private readonly InstallRoleService roles;

    public VaultSync(
        SecretsVaultService vault, AccountService accounts, InMemoryDriveFileIdStore fileIdStore, InstallRoleService roles)
    {
        this.roles = roles;
        this.vault = vault;
        this.accounts = accounts;
        this.fileIdStore = fileIdStore;
    }

    // Writes the Plaid credentials + all bank access tokens into the vault, leaving other apps' sections
    // alone. An install holding no Plaid credentials of its own (hers, always) leaves the vault's Plaid
    // section exactly as it found it (PlaidVaultMerge) rather than replacing it with blanks. Returns the
    // publish outcome so the caller can surface a failed backup instead of hiding it.
    public async Task<SecretsVaultPublishResult> PublishAsync(CancellationToken cancellationToken)
    {
        // Her install owns nothing in this vault. Refused at the choke point, so no call site (Save,
        // connect, remove) has to remember — and so she can never claim an unowned vault by writing it.
        if (!InstallRoleGate.MayRunPlaidWork(roles.Current))
            return SecretsVaultPublishResult.ReadOnly;

        var credentials = await accounts.GetInstitutionCredentialsAsync(cancellationToken);
        var items = credentials
            .Select(c => new PlaidItem(c.ItemId, c.AccessToken, c.Name))
            .ToList();

        return await vault.PublishAsync(v => PlaidVaultMerge.Apply(
                v,
                SettingsStore.PlaidClientId,
                SettingsStore.PlaidSecret,
                SettingsStore.PlaidEnvironment,
                items) with
            {
                // Release F: the reMarkable device token is a SHARED field (RemarkableSecrets, not a
                // per-app vault section) that Foreword's own pairing already writes — Index never pairs
                // itself, so publish only when this install actually holds a token of its own (restored
                // from the vault, or entered directly); otherwise this install's blank local copy must
                // never overwrite what Foreword already published.
                Remarkable = string.IsNullOrEmpty(SettingsStore.RemarkableDeviceToken)
                    ? v.Remarkable
                    : new RemarkableSecrets(SettingsStore.RemarkableDeviceToken),
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
        // Restoring copies the owner's Plaid client id/secret and every bank access token onto this
        // device. Her install reads the household snapshot and never talks to Plaid, so it never does.
        if (!InstallRoleGate.MayRunPlaidWork(roles.Current))
            return;

        var result = await vault.LoadAsync(cancellationToken);
        if (result.Status != SecretsVaultStatus.Loaded)
            return;

        // Seed the Drive-by-ID cache so well-known files resolve by remembered id (surviving a rename).
        foreach (var (name, id) in result.Vault.DriveFileIds)
            fileIdStore.Set(name, id);

        var plaid = result.Vault.Plaid;
        // Deliberately not restored: plaid.Environment. Index talks to Plaid's production environment
        // only (D144) — there is no environment setting in the app, so a vault written by the old
        // Ledger app (which could carry "sandbox" here) must never be allowed to pick a different host.
        // PlaidSecrets.Environment stays on the wire for compatibility; Index just never reads it back.
        if (string.IsNullOrEmpty(SettingsStore.PlaidClientId) && !string.IsNullOrEmpty(plaid.ClientId))
        {
            SettingsStore.PlaidClientId = plaid.ClientId;
        }
        if (string.IsNullOrEmpty(SettingsStore.PlaidSecret) && !string.IsNullOrEmpty(plaid.Secret))
            SettingsStore.PlaidSecret = plaid.Secret;

        var restored = plaid.Items.Select(i => new InstitutionCredential(i.ItemId, i.AccessToken, i.Name));
        await accounts.RestoreInstitutionsAsync(restored, cancellationToken);

        // Release F: restore the shared reMarkable device token if this install doesn't already hold
        // one — pairing once in Foreword lets Index's own worksheet delivery use it too.
        if (string.IsNullOrEmpty(SettingsStore.RemarkableDeviceToken) && !string.IsNullOrEmpty(result.Vault.Remarkable.DeviceToken))
        {
            SettingsStore.RemarkableDeviceToken = result.Vault.Remarkable.DeviceToken;
        }
    }
}
