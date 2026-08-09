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

    // Merges this app's section into the vault and saves it. Returns false (a no-op) when there is no
    // passphrase, or when the existing vault can't be read — refusing to write in that case so a mistyped
    // passphrase can't blow away the file. Returns true when the vault was written.
    public async Task<bool> PublishAsync(Func<SecretsVault, SecretsVault> merge, CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
            return false;

        var result = await store.LoadAsync(pass, cancellationToken);
        if (result.Status is SecretsVaultStatus.WrongPassphrase or SecretsVaultStatus.Corrupt)
            return false;

        await store.SaveAsync(merge(result.Vault), pass, cancellationToken);
        return true;
    }
}
