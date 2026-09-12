namespace Storyworthtelling.Voice.Secrets;

/// <summary>
/// A head's own vault: read it at launch, write it when something in it changes.
///
/// Thinner than <see cref="SecretsVaultService"/> because it has less to arbitrate.
/// The shared vault needs a merge that never clobbers another app's section and a rule
/// about who may write at all; this file has exactly one writer — the install it
/// belongs to — so a save is a save.
///
/// Same quiet degradation as everything else vault-backed: no passphrase, no Drive
/// scope, or no file yet all mean the head carries on from its local settings. The
/// personal vault is the off-device backup, never the source of truth on the device.
///
/// It lives here rather than in a head because there are two of them now — Score, which
/// keeps a TIDAL sign-in in it, and Foreword, which keeps whose install this is and the
/// mark its documents sign off with. Nothing in it was ever Android: the platform half is
/// the Drive glue that builds the store, and that stays where it is. A second copy of
/// "refuses to write a vault it could not read" is the drift this library exists to
/// prevent.
/// </summary>
public sealed class PersonalVaultAccess
{
    private readonly DrivePersonalVaultStore store;
    private readonly Func<string?> passphrase;

    public PersonalVaultAccess(DrivePersonalVaultStore store, Func<string?> passphrase)
    {
        this.store = store;
        this.passphrase = passphrase;
    }

    public async Task<PersonalVaultLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
        {
            return PersonalVaultLoadResult.Empty;
        }

        try
        {
            return await store.LoadAsync(pass, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // No Drive scope, no network. The head keeps its local copy either way.
            return PersonalVaultLoadResult.Empty;
        }
    }

    /// <summary>
    /// Merges into this person's vault and writes it back. Refuses to write a vault it
    /// could not read, for the same reason the shared one does: a mistyped passphrase
    /// must not replace a good file with a nearly-empty one.
    /// </summary>
    public async Task<bool> PublishAsync(
        Func<PersonalVault, PersonalVault> merge, CancellationToken cancellationToken)
    {
        var pass = passphrase();
        if (string.IsNullOrEmpty(pass))
        {
            return false;
        }

        try
        {
            var loaded = await store.LoadAsync(pass, cancellationToken);
            if (loaded.Status is PersonalVaultStatus.WrongPassphrase or PersonalVaultStatus.Corrupt)
            {
                return false;
            }

            await store.SaveAsync(merge(loaded.Vault), pass, cancellationToken);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }
}
