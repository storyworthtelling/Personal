using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Claude;
using Storyworthtelling.Voice;
using Folio.Infrastructure.Vault;
using Storyworthtelling.Voice.Secrets;

namespace Folio.Android;

// Keeps Folio's section of the shared Drive secrets vault (the Claude + Gemini keys) in sync with
// this app's local Preferences. Publishing runs from the Settings page after a save; restoring runs at
// startup so a reinstalled app (e.g. after the FocusPhone cutover's factory reset) repopulates its keys
// from Drive instead of needing them re-typed. Both are best-effort — the app works from local Preferences
// whether or not the vault is reachable.
public sealed class VaultSync
{
    private readonly SecretsVaultService vault;
    private readonly InMemoryDriveFileIdStore fileIdStore;

    public VaultSync(SecretsVaultService vault, InMemoryDriveFileIdStore fileIdStore)
    {
        this.vault = vault;
        this.fileIdStore = fileIdStore;
    }

    // Writes the Claude + Gemini keys and the reMarkable device token into the vault, leaving every other app's
    // section untouched, under the suite's merge rule: a blank never overwrites (so a Folio that never paired
    // cannot wipe the token Foreword published), and a value the vault already holds is replaced only by one
    // the owner just changed here (`changed`, FolioVaultSection's field names). Captured Drive file ids are
    // folded into the shared map, so a reinstall restores where the well-known files live.
    public Task<SecretsVaultPublishResult> PublishAsync(IReadOnlyCollection<string> changed, CancellationToken cancellationToken) =>
        vault.PublishAsync(v => FolioVaultSection.Merge(
            v,
            new FolioVaultSection.Local(SettingsStore.ClaudeApiKey, SettingsStore.GeminiApiKey, SettingsStore.RemarkableDeviceToken),
            changed,
            fileIdStore.Snapshot()), cancellationToken);

    // Fills in any key this install is missing from the vault; the shared Claude and Gemini keys follow the vault.
    // True when the vault opened.
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        var result = await vault.LoadAsync(cancellationToken);
        if (result.Status != SecretsVaultStatus.Loaded)
            return false;

        // Seed the Drive-by-ID cache from the vault so a reinstalled app resolves its well-known files by
        // their remembered ids (surviving a rename) before falling back to a name search.
        foreach (var (name, id) in result.Vault.DriveFileIds)
            fileIdStore.Set(name, id);

        // The shared Claude and Gemini keys belong to the vault (audit 01 §3): its value replaces a copy that has
        // gone stale, so a key rotated in another app reaches Folio on its next launch. A blank never clears one.
        SettingsStore.ClaudeApiKey = ClaudeKeySource.Choose(result.Vault.Claude.ApiKey, SettingsStore.ClaudeApiKey);
        SettingsStore.GeminiApiKey = ClaudeKeySource.Choose(result.Vault.Gemini.ApiKey, SettingsStore.GeminiApiKey);
        // The reMarkable token may have been paired in a sibling app (e.g. Foreword) — restore it so
        // Folio delivers without a separate pairing.
        // A pairing the tablet refused yields to the vault's newer one (audit 11 §13).
        VaultRestore.Apply(
            SettingsStore.RemarkableDeviceToken, result.Vault.Remarkable.DeviceToken,
            Storyworthtelling.Remarkable.RemarkableCredentialId.Value, Storyworthtelling.Maui.Health.MauiCredentialHealth.Store(),
            value => SettingsStore.RemarkableDeviceToken = value);

        // Verdicts on the shared keys, both ways (audit 11 §13): a 401 Post or Plate saw now reaches Folio's Claude
        // field, and Folio's reach them. Folio keeps its own copies of all three.
        await vault.SyncKeyHealthAsync(
            Storyworthtelling.Maui.Health.MauiCredentialHealth.Store(),
            id => id switch
            {
                SharedCredentialIds.Claude => SettingsStore.ClaudeApiKey,
                SharedCredentialIds.Gemini => SettingsStore.GeminiApiKey,
                SharedCredentialIds.Remarkable => SettingsStore.RemarkableDeviceToken,
                _ => null,
            },
            cancellationToken);
        return true;
    }

    /// <summary>The launch restore: at most every six hours, at once when a key is missing or refused (efficiency §24).</summary>
    public Task RestoreAtLaunchAsync(CancellationToken cancellationToken) =>
        Storyworthtelling.Maui.Health.LaunchRestore.RunAsync(
            () => [SettingsStore.ClaudeApiKey, SettingsStore.GeminiApiKey, SettingsStore.RemarkableDeviceToken],
            RestoreAsync, cancellationToken);
}
