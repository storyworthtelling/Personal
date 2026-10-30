namespace Storyworthtelling.Voice.Secrets;

// The small orchestration each app uses to keep its section of the shared vault in sync. It pairs the
// Drive-backed store with the app's local passphrase (a Func, because the passphrase lives in each app's own
// settings) and provides the two safe operations every app repeats:
//
//   • LoadAsync  — read+decrypt the vault (for restoring secrets on startup after a reset).
//   • PublishAsync — load, replace only this app's section, and write back, WITHOUT ever overwriting a
//     vault that couldn't be read (a wrong passphrase must never destroy another app's secrets).
//
// Both degrade quietly when no passphrase is set, so an app that hasn't been given one keeps working from
// its local settings and simply doesn't participate in the vault yet.
public sealed class SecretsVaultService
{
    private readonly ISecretsVaultStore store;
    private readonly Func<string?> passphrase;

    public SecretsVaultService(ISecretsVaultStore store, Func<string?> passphrase)
    {
        this.store = store;
        this.passphrase = passphrase;
    }

    public bool HasPassphrase => !string.IsNullOrEmpty(passphrase());

    public Task<SecretsVaultLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var pass = passphrase();
        return string.IsNullOrEmpty(pass)
            ? Task.FromResult(SecretsVaultLoadResult.Empty)
            : store.LoadAsync(pass, cancellationToken);
    }

    // Merges this app's section into the vault and saves it, reporting the outcome instead of returning a
    // bare bool. Skips quietly with no passphrase; refuses to write (VaultUnreadable) when the existing vault
    // can't be read, so a mistyped passphrase can't blow away the file; and — the point of the hardening —
    // converts a write failure (no `drive` scope, no network) into a WriteFailed result rather than an
    // escaping exception, so the caller can surface it instead of the backup silently never happening.
    public async Task<SecretsVaultPublishResult> PublishAsync(Func<SecretsVault, SecretsVault> merge, CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
            return SecretsVaultPublishResult.NoPassphrase;

        var result = await store.LoadAsync(pass, cancellationToken);
        if (result.Status is SecretsVaultStatus.WrongPassphrase or SecretsVaultStatus.Corrupt)
            return SecretsVaultPublishResult.VaultUnreadable;

        // Refused here rather than at each of the suite's publish call sites: one
        // choke point is what makes "the partner's install never writes the household
        // vault" true everywhere at once instead of nine places remembering. The answer is
        // the vault file's own Drive ownership, not anything stored inside it.
        if (!VaultOwnership.Decide(result.FileOwnership).CanWrite())
        {
            return SecretsVaultPublishResult.ReadOnly;
        }

        try
        {
            await store.SaveAsync(merge(result.Vault), pass, cancellationToken);
        }
        catch (Exception ex)
        {
            return SecretsVaultPublishResult.WriteFailed(ex.Message);
        }

        return SecretsVaultPublishResult.Published;
    }
}
