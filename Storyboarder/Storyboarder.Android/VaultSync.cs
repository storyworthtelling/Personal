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

    public VaultSync(SecretsVaultService vault)
    {
        this.vault = vault;
    }

    // Writes the current Claude + Gemini keys into the vault, leaving every other app's section untouched.
    public Task<bool> PublishAsync(CancellationToken cancellationToken) =>
        vault.PublishAsync(v => v with
        {
            Claude = new ClaudeSecrets(SettingsStore.ClaudeApiKey),
            Gemini = new GeminiSecrets(SettingsStore.GeminiApiKey)
        }, cancellationToken);

    // Fills in any key this install is missing from the vault. Existing local keys win — restore never
    // overwrites a value the owner has already entered here.
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var result = await vault.LoadAsync(cancellationToken);
        if (result.Status != SecretsVaultStatus.Loaded)
            return;

        if (string.IsNullOrEmpty(SettingsStore.ClaudeApiKey) && !string.IsNullOrEmpty(result.Vault.Claude.ApiKey))
            SettingsStore.ClaudeApiKey = result.Vault.Claude.ApiKey;
        if (string.IsNullOrEmpty(SettingsStore.GeminiApiKey) && !string.IsNullOrEmpty(result.Vault.Gemini.ApiKey))
            SettingsStore.GeminiApiKey = result.Vault.Gemini.ApiKey;
    }
}
