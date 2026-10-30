using Android.Content;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Score;

/// <summary>
/// Keeps the TIDAL sign-in in this person's own vault, so a factory reset costs a
/// passphrase rather than a re-pairing.
///
/// The device stays the source of truth — <see cref="TidalAccountStore"/> is what
/// playback reads, and it is read on every token refresh. This only mirrors it off the
/// phone and back, which is exactly the role the vault plays for every other
/// credential in the suite.
///
/// It writes to the <b>personal</b> vault rather than the shared one on purpose. A
/// refresh token carries one person's Family membership; putting it where both
/// installs could restore it would put them on one membership, back into the
/// single-stream problem Family was bought to solve, and would mean either person's
/// sign-out silently signing the other out too.
///
/// Best-effort throughout: no passphrase, no Drive scope, no file yet all leave the
/// local pairing exactly as it was.
/// </summary>
public sealed class TidalAccountSync(Context context)
{
    private readonly TidalAccountStore store = new(context);

    private static PersonalVaultAccess Vault() => new SecretsVaultAccess(
        new Score.Google.DeviceAccountTokenProvider().GetAccessToken, "Score").Personal;

    /// <summary>
    /// Restores a pairing this phone does not have. Never overwrites one it does: the
    /// device is the source of truth, and a phone that is signed in is already right
    /// whatever the backup says.
    /// </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (store.Load() is not null)
        {
            return;
        }

        var loaded = await Vault().LoadAsync(cancellationToken);
        if (loaded.Status != PersonalVaultStatus.Loaded)
        {
            return;
        }

        var account = loaded.Vault.TidalAccount;
        if (!string.IsNullOrEmpty(account.RefreshToken))
        {
            store.Save(new TidalAccount(account.UserId, account.RefreshToken));
        }
    }

    /// <summary>Mirrors a fresh pairing up. Called after a device-code flow succeeds.</summary>
    public Task BackUpAsync(TidalAccount account, CancellationToken cancellationToken) =>
        Vault().PublishAsync(
            v => v with { TidalAccount = new Storyworthtelling.Voice.Secrets.TidalAccount(account.UserId, account.RefreshToken) },
            cancellationToken);

    /// <summary>
    /// Clears the pairing here and in the backup. Signing out has to reach the vault
    /// too, or the next launch restores the account the owner just signed out of.
    /// </summary>
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        store.Clear();
        await Vault().PublishAsync(
            v => v with { TidalAccount = new Storyworthtelling.Voice.Secrets.TidalAccount() }, cancellationToken);
    }
}
