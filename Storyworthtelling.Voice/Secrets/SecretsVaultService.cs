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
    private readonly Func<string?> signedInAccount;

    /// <param name="signedInAccount">
    /// The Google account this install is bound to, which is what decides whether it
    /// may write the shared vault. Optional, and null means "cannot tell" rather than
    /// "not the owner": an app that has not wired identity — or one running before
    /// sign-in during setup — publishes exactly as it did before ownership existed.
    ///
    /// That is a deliberate hole rather than an oversight. Refusing writes from an
    /// unidentified install would break the vault on the owner's own phone mid-setup,
    /// to guard against a case that does not arise: the second install is Score bound
    /// to her Google account, so it always knows perfectly well who it is.
    /// </param>
    public SecretsVaultService(
        ISecretsVaultStore store, Func<string?> passphrase, Func<string?>? signedInAccount = null)
    {
        this.store = store;
        this.passphrase = passphrase;
        this.signedInAccount = signedInAccount ?? (() => null);
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
        // choke point is what makes "her install never writes the household vault"
        // true everywhere at once instead of nine places remembering.
        var account = signedInAccount();
        if (!string.IsNullOrWhiteSpace(account)
            && !VaultOwnership.Decide(account, result.Vault).CanWrite())
        {
            return SecretsVaultPublishResult.ReadOnly;
        }

        // Claiming rides the ordinary publish: the first install to write becomes the
        // owner by doing it, and an owned vault is never re-stamped.
        var merged = VaultOwnership.Claim(merge(result.Vault), account);
        try
        {
            await store.SaveAsync(merged, pass, cancellationToken);
        }
        catch (Exception ex)
        {
            return SecretsVaultPublishResult.WriteFailed(ex.Message);
        }

        return SecretsVaultPublishResult.Published;
    }
}
