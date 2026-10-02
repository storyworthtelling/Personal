using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Foreword.Android;

// Keeps Foreword's section of the shared Drive secrets vault in sync with this app's local
// CredentialStore: the NYT session cookie + API key, the reMarkable device token and the handwriting
// Anthropic key. The calendar choices — the columns this account prints and how calendars reach them —
// are this account's own, so they go to its PERSONAL vault instead. Publishing runs after a settings
// save or a reMarkable registration;
// restoring runs at startup so a reinstalled app (e.g. after the FocusPhone cutover's factory reset)
// repopulates its credentials from Drive instead of re-pairing and re-pasting them. Best-effort — the app
// works from local Preferences whether or not the vault is reachable.
public sealed class VaultSync
{
    private readonly SecretsVaultService vault;
    private readonly PersonalVaultAccess personal;
    private readonly InMemoryDriveFileIdStore fileIdStore;

    public VaultSync(SecretsVaultService vault, PersonalVaultAccess personal, InMemoryDriveFileIdStore fileIdStore)
    {
        this.vault = vault;
        this.personal = personal;
        this.fileIdStore = fileIdStore;
    }

    // Writes Foreword's credentials into the shared vault, leaving every other app's section untouched,
    // then this account's calendar choices into its personal vault.
    public async Task<SecretsVaultPublishResult> PublishAsync(CancellationToken cancellationToken)
    {
        var result = await vault.PublishAsync(v => RememberHousehold(v) with
        {
            Nyt = new NytSecrets(CredentialStore.NytSessionCookie, CredentialStore.NytApiKey),
            Remarkable = new RemarkableSecrets(CredentialStore.RemarkableDeviceToken),
            // The calendar mapping is no longer written here: it is per account, and lives in each
            // account's personal vault. The copy already in this file is left exactly as it is, so the
            // owner's install can still restore from it once while it moves across.
            Foreword = new ForewordSecrets(CredentialStore.AnthropicApiKey, v.Foreword.CalendarMappingJson),
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

        await PublishCalendarAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// This account's calendar choices, backed up to its own personal vault. Best-effort like the rest:
    /// false means no passphrase, no Drive, or a vault it could not read, and the phone carries on from
    /// its local copy.
    /// </summary>
    public Task<bool> PublishCalendarAsync(CancellationToken cancellationToken) =>
        personal.PublishAsync(v => v with
        {
            CalendarMappingJson = CredentialStore.CalendarMappingJson,
            WorkScheduleJson = CredentialStore.WorkScheduleJson,
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
        // This account's own choices first: they are the ones that belong to it.
        var mine = await personal.LoadAsync(cancellationToken);
        if (mine.Status == PersonalVaultStatus.Loaded)
        {
            FillIfEmpty(CredentialStore.CalendarMappingJson, mine.Vault.CalendarMappingJson, v => CredentialStore.CalendarMappingJson = v);
            FillIfEmpty(CredentialStore.WorkScheduleJson, mine.Vault.WorkScheduleJson, v => CredentialStore.WorkScheduleJson = v);
        }

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
        // The shared vault's mapping is the one the first install wrote before the choices became per
        // account. Only that account takes it: another account restoring it would print somebody else's
        // columns as its own.
        if (IsHouseholdAccount(vaultData))
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

    private static bool IsHouseholdAccount(SecretsVault read) =>
        !string.IsNullOrWhiteSpace(read.OwnerAccount)
        && string.Equals(read.OwnerAccount.Trim(), CredentialStore.GoogleAccountEmail.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void FillIfEmpty(string current, string fromVault, Action<string> set)
    {
        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(fromVault))
            set(fromVault);
    }
}
