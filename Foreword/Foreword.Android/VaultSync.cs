using Storyworthtelling.Voice;
using Foreword.Infrastructure.Vault;
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

    // What the owner has changed on this phone since the last publish that landed, by
    // ForewordVaultSection's field names. Settings commit as they are typed, so the page records each change
    // here and the next publish carries it; anything not recorded leaves the vault's value alone.
    private readonly HashSet<string> changed = [];
    private readonly object changedLock = new();

    public void MarkChanged(string field)
    {
        lock (changedLock)
        {
            changed.Add(field);
        }
    }

    // Writes Foreword's credentials into the shared vault, leaving every other app's section untouched, under
    // the suite's merge rule (VaultMerge.Pick): a blank never overwrites, and a value the vault already holds
    // is replaced only by one the owner just changed here. Then this account's calendar choices go to its
    // personal vault.
    public async Task<SecretsVaultPublishResult> PublishAsync(CancellationToken cancellationToken)
    {
        string[] fields;
        lock (changedLock)
        {
            fields = [.. changed];
        }

        var local = new ForewordVaultSection.Local(
            CredentialStore.NytSessionCookie,
            CredentialStore.NytApiKey,
            CredentialStore.RemarkableDeviceToken,
            CredentialStore.AnthropicApiKey,
            CredentialStore.MapsApiKey,
            CredentialStore.HomeAddress,
            CredentialStore.HomeLatitude,
            CredentialStore.HomeLongitude);
        var result = await vault.PublishAsync(
            v => ForewordVaultSection.Merge(RememberHousehold(v), local, fields, fileIdStore.Snapshot()), cancellationToken);
        if (result.Succeeded)
        {
            lock (changedLock)
            {
                changed.ExceptWith(fields);
            }
        }

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
        // A pairing the tablet refused yields to the vault's newer one (audit 11 §13).
        Storyworthtelling.Voice.Secrets.VaultRestore.Apply(
            CredentialStore.RemarkableDeviceToken, vaultData.Remarkable.DeviceToken,
            Storyworthtelling.Remarkable.RemarkableCredentialId.Value, Storyworthtelling.Maui.Health.MauiCredentialHealth.Store(),
            v => CredentialStore.RemarkableDeviceToken = v);
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
