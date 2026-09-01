namespace Storyworthtelling.Voice.Secrets;

// Bridges the vault to the existing voice stack: an IVoiceConfigStore that sources the shared Claude key
// from the encrypted vault instead of the old plaintext storyworthtelling-voice-config.json. Because the
// vault needs the owner's passphrase (which IVoiceConfigStore.LoadAsync doesn't take), it is supplied by a
// small accessor the app wires to its local passphrase store. With no passphrase set, or any load failure,
// this returns VoiceConfig.Empty — exactly the neutral-copy fallback the voice stack already expects — so
// nothing downstream changes. The rest of the voice pipeline (DriveVoiceOptions, VoiceClient) is untouched.
public sealed class VaultVoiceConfigStore : IVoiceConfigStore
{
    private readonly ISecretsVaultStore vault;
    private readonly Func<string?> passphrase;

    public VaultVoiceConfigStore(ISecretsVaultStore vault, Func<string?> passphrase)
    {
        this.vault = vault;
        this.passphrase = passphrase;
    }

    public async Task<VoiceConfig> LoadAsync(CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
            return VoiceConfig.Empty;

        var result = await vault.LoadAsync(pass, cancellationToken);
        if (result.Status != SecretsVaultStatus.Loaded || string.IsNullOrWhiteSpace(result.Vault.Claude.ApiKey))
            return VoiceConfig.Empty;

        // Default model tiers — the vault carries only the key, like the old key-only voice config.
        return new VoiceConfig(result.Vault.Claude.ApiKey);
    }

    // Merges the Claude key into the vault, leaving every other app's section intact. Only the key-owning
    // app (Storyboarder) publishes it; readers never call this. A missing passphrase makes it a no-op so a
    // reader that can't decrypt can't accidentally blank the vault.
    public async Task SaveAsync(VoiceConfig config, CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
            return;

        var result = await vault.LoadAsync(pass, cancellationToken);
        // Refuse to write over a vault we couldn't read (wrong passphrase / corrupt) — that would destroy it.
        if (result.Status is SecretsVaultStatus.WrongPassphrase or SecretsVaultStatus.Corrupt)
            return;

        var merged = result.Vault with { Claude = new ClaudeSecrets(config.ApiKey) };
        await vault.SaveAsync(merged, pass, cancellationToken);
    }
}
