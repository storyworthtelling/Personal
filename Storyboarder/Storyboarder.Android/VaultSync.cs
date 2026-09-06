using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Storyboarder.Android;

// Keeps Storyboarder's section of the shared Drive secrets vault (the Claude + Gemini keys) in sync with
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

    // Writes the current Claude + Gemini keys and the reMarkable device token into the vault, leaving every
    // other app's section untouched. The Remarkable section is shared with the other apps that deliver to the
    // same tablet, so publishing the token here also lets them restore it.
    public Task<SecretsVaultPublishResult> PublishAsync(CancellationToken cancellationToken) =>
        vault.PublishAsync(v => v with
        {
            Claude = new ClaudeSecrets(SettingsStore.ClaudeApiKey),
            Gemini = new GeminiSecrets(SettingsStore.GeminiApiKey),
            Remarkable = new RemarkableSecrets(SettingsStore.RemarkableDeviceToken),
            // Fold this session's captured Drive file ids into the shared map (id wins per name), so a
            // reinstall restores where the well-known files live even after a rename/move.
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

    // Fills in any key this install is missing from the vault. Existing local keys win — restore never
    // overwrites a value the owner has already entered here.
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var result = await vault.LoadAsync(cancellationToken);
        if (result.Status != SecretsVaultStatus.Loaded)
            return;

        // Seed the Drive-by-ID cache from the vault so a reinstalled app resolves its well-known files by
        // their remembered ids (surviving a rename) before falling back to a name search.
        foreach (var (name, id) in result.Vault.DriveFileIds)
            fileIdStore.Set(name, id);

        if (string.IsNullOrEmpty(SettingsStore.ClaudeApiKey) && !string.IsNullOrEmpty(result.Vault.Claude.ApiKey))
            SettingsStore.ClaudeApiKey = result.Vault.Claude.ApiKey;
        if (string.IsNullOrEmpty(SettingsStore.GeminiApiKey) && !string.IsNullOrEmpty(result.Vault.Gemini.ApiKey))
            SettingsStore.GeminiApiKey = result.Vault.Gemini.ApiKey;
        // The reMarkable token may have been paired in a sibling app (e.g. Foreword) — restore it so
        // Canonizer delivers without a separate pairing.
        if (string.IsNullOrEmpty(SettingsStore.RemarkableDeviceToken) && !string.IsNullOrEmpty(result.Vault.Remarkable.DeviceToken))
            SettingsStore.RemarkableDeviceToken = result.Vault.Remarkable.DeviceToken;
    }
}
