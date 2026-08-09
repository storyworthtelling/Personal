using Storyworthtelling.Voice.Secrets;

namespace DailyLoader.Android;

// Keeps DailyLoader's section of the shared Drive secrets vault in sync with this app's local
// CredentialStore: the NYT session cookie + API key, the reMarkable device token, the handwriting Anthropic
// key, and the calendar-mapping JSON. Publishing runs after a settings save or a reMarkable registration;
// restoring runs at startup so a reinstalled app (e.g. after the FocusPhone cutover's factory reset)
// repopulates its credentials from Drive instead of re-pairing and re-pasting them. Best-effort — the app
// works from local Preferences whether or not the vault is reachable.
public sealed class VaultSync
{
    private readonly SecretsVaultService vault;

    public VaultSync(SecretsVaultService vault)
    {
        this.vault = vault;
    }

    // Writes DailyLoader's credentials into the vault, leaving every other app's section untouched.
    public Task<bool> PublishAsync(CancellationToken cancellationToken) =>
        vault.PublishAsync(v => v with
        {
            Nyt = new NytSecrets(CredentialStore.NytSessionCookie, CredentialStore.NytApiKey),
            Remarkable = new RemarkableSecrets(CredentialStore.RemarkableDeviceToken),
            DailyLoader = new DailyLoaderSecrets(CredentialStore.AnthropicApiKey, CredentialStore.CalendarMappingJson)
        }, cancellationToken);

    // Fills in any credential this install is missing from the vault. Existing local values win — restore
    // never overwrites something the owner has already entered here.
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var result = await vault.LoadAsync(cancellationToken);
        if (result.Status != SecretsVaultStatus.Loaded)
            return;

        var vaultData = result.Vault;
        FillIfEmpty(CredentialStore.NytSessionCookie, vaultData.Nyt.SessionCookie, v => CredentialStore.NytSessionCookie = v);
        FillIfEmpty(CredentialStore.NytApiKey, vaultData.Nyt.ApiKey, v => CredentialStore.NytApiKey = v);
        FillIfEmpty(CredentialStore.RemarkableDeviceToken, vaultData.Remarkable.DeviceToken, v => CredentialStore.RemarkableDeviceToken = v);
        FillIfEmpty(CredentialStore.AnthropicApiKey, vaultData.DailyLoader.AnthropicApiKey, v => CredentialStore.AnthropicApiKey = v);
        FillIfEmpty(CredentialStore.CalendarMappingJson, vaultData.DailyLoader.CalendarMappingJson, v => CredentialStore.CalendarMappingJson = v);
    }

    private static void FillIfEmpty(string current, string fromVault, Action<string> set)
    {
        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(fromVault))
            set(fromVault);
    }
}
