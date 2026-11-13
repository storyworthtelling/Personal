using Storyworthtelling.Voice.Health;
using Storyworthtelling.Diagnostics;

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
    private readonly Action<bool, string>? trail;

    /// <param name="trail">
    /// Told every restore and publish outcome as one <see cref="VaultTrail"/> line (a problem flag and the words), so a
    /// head writes it to its log. They used to be swallowed at every call site (audit 08 §6).
    /// </param>
    public SecretsVaultService(ISecretsVaultStore store, Func<string?> passphrase, Action<bool, string>? trail = null)
    {
        this.store = store;
        this.passphrase = passphrase;
        this.trail = trail;
    }

    public bool HasPassphrase => !string.IsNullOrEmpty(passphrase());

    public async Task<SecretsVaultLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
        {
            Tell(VaultTrail.Restored(VaultTrail.Shared, "NoPassphrase"));
            return SecretsVaultLoadResult.Empty;
        }

        try
        {
            var result = await store.LoadAsync(pass, cancellationToken);
            Tell(VaultTrail.Restored(VaultTrail.Shared, result.IsNewerFormat ? "NewerFormat" : result.Status.ToString()));
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Tell(VaultTrail.Restored(VaultTrail.Shared, "Unreachable", ex));
            throw;
        }
    }

    // Merges this app's section into the vault and saves it, reporting the outcome instead of returning a
    // bare bool. Skips quietly with no passphrase; refuses to write (VaultUnreadable) when the existing vault
    // can't be read, so a mistyped passphrase can't blow away the file; and — the point of the hardening —
    // converts a write failure (no `drive` scope, no network) into a WriteFailed result rather than an
    // escaping exception, so the caller can surface it instead of the backup silently never happening.
    public async Task<SecretsVaultPublishResult> PublishAsync(Func<SecretsVault, SecretsVault> merge, CancellationToken cancellationToken)
    {
        var (result, failure) = await PublishOnce(merge, cancellationToken);
        Tell(VaultTrail.Published(VaultTrail.Shared, result.Status.ToString(), failure));
        return result;
    }

    private void Tell((bool Problem, string Line) line) => trail?.Invoke(line.Problem, line.Line);

    private async Task<(SecretsVaultPublishResult Result, Exception? Failure)> PublishOnce(
        Func<SecretsVault, SecretsVault> merge, CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
            return (SecretsVaultPublishResult.NoPassphrase, null);

        // A conflict means another app or phone wrote the vault since it was read: read it again and merge
        // this section into what is there now, rather than writing over their change (contracts M6).
        for (var attempt = 1; ; attempt++)
        {
            SecretsVaultLoadResult result;
            try
            {
                result = await store.LoadAsync(pass, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A vault Drive could not be read is not an empty one; nothing is written.
                return (SecretsVaultPublishResult.WriteFailed($"Drive could not be read ({ex.GetType().Name}: {ex.Message})."), ex);
            }

            if (result.IsNewerFormat)
                return (SecretsVaultPublishResult.VaultUnreadable with { Error = SecretsVaultPublishResult.NewerAppError }, null);
            if (result.Status is SecretsVaultStatus.WrongPassphrase or SecretsVaultStatus.Corrupt)
                return (SecretsVaultPublishResult.VaultUnreadable, null);

            // Refused here rather than at each of the suite's publish call sites: one
            // choke point is what makes "the partner's install never writes the household
            // vault" true everywhere at once instead of nine places remembering. The answer is
            // the vault file's own Drive ownership, not anything stored inside it.
            if (!VaultOwnership.Decide(result.FileOwnership).CanWrite())
            {
                return (SecretsVaultPublishResult.ReadOnly, null);
            }

            try
            {
                await store.SaveAsync(merge(result.Vault), pass, result, cancellationToken);
                return (SecretsVaultPublishResult.Published, null);
            }
            catch (VaultWriteException ex) when (ex.IsConflict && attempt < MaxConflictRetries)
            {
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (SecretsVaultPublishResult.WriteFailed(ex.Message), ex);
            }
        }
    }

    /// <summary>
    /// Carries the verdicts on the shared credentials both ways (audit 11 §13; see <see cref="VaultKeyHealth"/>): the
    /// vault's newer ones into <paramref name="health"/>, then this app's newer ones into the vault — only when there is
    /// something new, and never from a phone that may only read the vault. Never throws for an unreadable vault or a
    /// failed write; those are trailed by <see cref="LoadAsync"/> and <see cref="PublishAsync"/>.
    /// </summary>
    /// <param name="localValue">This app's own copy of a shared credential, or null when it uses the vault's value.</param>
    public async Task SyncKeyHealthAsync(
        CredentialHealthStore health, Func<string, string?> localValue, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(cancellationToken);
        if (loaded.Status != SecretsVaultStatus.Loaded)
        {
            return;
        }

        health.Change(report => VaultKeyHealth.Pull(loaded.Vault, report, localValue));
        if (VaultKeyHealth.Push(loaded.Vault, health.Load(), localValue) is null
            || !VaultOwnership.Decide(loaded.FileOwnership).CanWrite())
        {
            return;
        }

        var report = health.Load();
        await PublishAsync(vault => VaultKeyHealth.Push(vault, report, localValue) ?? vault, cancellationToken);
    }

    /// <summary>How many times a publish reads again after finding the vault changed under it.</summary>
    public const int MaxConflictRetries = 3;
}
