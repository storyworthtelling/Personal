using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Foreword.Android;

// Keeps Foreword's section of the shared Drive secrets vault in sync with this app's local
// CredentialStore: the NYT session cookie + API key, the reMarkable device token, the handwriting Anthropic
// key, and the calendar-mapping JSON. Publishing runs after a settings save or a reMarkable registration;
// restoring runs at startup so a reinstalled app (e.g. after the FocusPhone cutover's factory reset)
// repopulates its credentials from Drive instead of re-pairing and re-pasting them. Best-effort — the app
// works from local Preferences whether or not the vault is reachable.
public sealed class VaultSync
{
    private readonly SecretsVaultService vault;
    private readonly InMemoryDriveFileIdStore fileIdStore;

    public VaultSync(SecretsVaultService vault, InMemoryDriveFileIdStore fileIdStore)
    {
        this.vault = vault;
        this.fileIdStore = fileIdStore;
    }

    // Writes Foreword's credentials into the vault, leaving every other app's section untouched.
    public Task<SecretsVaultPublishResult> PublishAsync(CancellationToken cancellationToken) =>
        vault.PublishAsync(v => RememberHousehold(v) with
        {
            Nyt = new NytSecrets(CredentialStore.NytSessionCookie, CredentialStore.NytApiKey),
            Remarkable = new RemarkableSecrets(CredentialStore.RemarkableDeviceToken),
            Foreword = new ForewordSecrets(CredentialStore.AnthropicApiKey, CredentialStore.CalendarMappingJson),
            // The Maps key is shared rather than Foreword's own: it bills a project, and the house
            // beside it is the household's address, not this app's setting.
            Maps = new MapsSecrets(
                CredentialStore.MapsApiKey,
                CredentialStore.HomeAddress,
                CredentialStore.HomeLatitude,
                CredentialStore.HomeLongitude),
            // Fold this session's captured Drive file ids into the shared map (id wins per name).
            DriveFileIds = MergeIds(v.DriveFileIds, fileIdStore.Snapshot())
        }, cancellationToken);

    private static IReadOnlyDictionary<string, string> MergeIds(
        IReadOnlyDictionary<string, string> existing, IReadOnlyDictionary<string, string> captured)
    {
        var merged = new Dictionary<string, string>(existing);
        foreach (var (name, id) in captured)
            merged[name] = id;
        return merged;
    }

    // Fills in any credential this install is missing from the vault. Existing local values win — restore
    // never overwrites something the owner has already entered here.
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var result = await vault.LoadAsync(cancellationToken);
        if (result.Status != SecretsVaultStatus.Loaded)
            return;

        var vaultData = RememberHousehold(result.Vault);
        // Seed the Drive-by-ID cache so well-known files resolve by remembered id (surviving a rename).
        foreach (var (name, id) in vaultData.DriveFileIds)
            fileIdStore.Set(name, id);

        FillIfEmpty(CredentialStore.NytSessionCookie, vaultData.Nyt.SessionCookie, v => CredentialStore.NytSessionCookie = v);
        FillIfEmpty(CredentialStore.NytApiKey, vaultData.Nyt.ApiKey, v => CredentialStore.NytApiKey = v);
        FillIfEmpty(CredentialStore.RemarkableDeviceToken, vaultData.Remarkable.DeviceToken, v => CredentialStore.RemarkableDeviceToken = v);
        FillIfEmpty(CredentialStore.AnthropicApiKey, vaultData.Foreword.AnthropicApiKey, v => CredentialStore.AnthropicApiKey = v);
        FillIfEmpty(CredentialStore.CalendarMappingJson, vaultData.Foreword.CalendarMappingJson, v => CredentialStore.CalendarMappingJson = v);
        FillIfEmpty(CredentialStore.MapsApiKey, vaultData.Maps.ApiKey, v => CredentialStore.MapsApiKey = v);
        FillIfEmpty(CredentialStore.HomeAddress, vaultData.Maps.HomeAddress, v => CredentialStore.HomeAddress = v);

        // The house's point is two numbers, and zero is a real coordinate — it just happens to be in
        // the Atlantic. "Not set" is therefore both of them being zero, not either one.
        if (CredentialStore.HomeLatitude == 0 && CredentialStore.HomeLongitude == 0
            && (vaultData.Maps.HomeLatitude != 0 || vaultData.Maps.HomeLongitude != 0))
        {
            CredentialStore.HomeLatitude = vaultData.Maps.HomeLatitude;
            CredentialStore.HomeLongitude = vaultData.Maps.HomeLongitude;
        }
    }

    // Whose phone this is is inferred from the account that owns the shared vault, so every read of
    // the vault refreshes the cached owner. An unowned vault leaves the cache as it was.
    private static SecretsVault RememberHousehold(SecretsVault read)
    {
        if (!string.IsNullOrWhiteSpace(read.OwnerAccount))
            CredentialStore.HouseholdAccount = read.OwnerAccount.Trim();
        return read;
    }

    private static void FillIfEmpty(string current, string fromVault, Action<string> set)
    {
        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(fromVault))
            set(fromVault);
    }
}
