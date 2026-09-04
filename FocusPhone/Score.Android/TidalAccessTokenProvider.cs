using Android.Content;
using Codex.Infrastructure.Music;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Score;

/// <summary>
/// Turns a stored <see cref="TidalAccountStore"/> refresh token into a fresh access
/// token — reading the app's <c>TidalSecrets.ClientId</c>/<c>ClientSecret</c> from the
/// shared vault each time, since nothing keeps them any closer than that.
///
/// Pulled out on its own because two different callers need exactly this: TIDAL's
/// SDK re-asks for credentials over a Player's whole lifetime, so
/// <c>TidalCredentialsProvider</c> in <see cref="PlaybackService"/> wraps this as its
/// repeatable refresh function; a catalogue search just needs one token for one call.
/// Same degrade-quietly shape either way — no account, no vault, or a rejected token
/// all just come back as a non-Success <see cref="TidalTokenPollResult"/> for the
/// caller to react to, never an exception.
/// </summary>
public sealed class TidalAccessTokenProvider(Context context)
{
    public async Task<TidalTokenPollResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var account = new TidalAccountStore(context).Load();
        if (account is null)
        {
            return new TidalTokenPollResult(TidalTokenPollStatus.Denied);
        }

        var vaultAccess = new SecretsVaultAccess(
            new Score.Google.DeviceAccountTokenProvider().GetAccessToken, "Score",
            () => ScoreIdentity.Account);
        var vaultResult = await vaultAccess.Service.LoadAsync(cancellationToken);
        if (vaultResult.Status != SecretsVaultStatus.Loaded || string.IsNullOrEmpty(vaultResult.Vault.Tidal.ClientId))
        {
            return new TidalTokenPollResult(TidalTokenPollStatus.Error);
        }

        var result = await new TidalDeviceAuthClient(new HttpClient()).RefreshAsync(
            vaultResult.Vault.Tidal.ClientId, vaultResult.Vault.Tidal.ClientSecret, account.RefreshToken,
            cancellationToken);

        // A rotated refresh token invalidates the old one on TIDAL's side, so the new
        // one has to be saved immediately or the next refresh fails.
        if (!string.IsNullOrEmpty(result.RefreshToken))
        {
            new TidalAccountStore(context).Save(new TidalAccount(account.UserId, result.RefreshToken));
        }

        return result;
    }
}
