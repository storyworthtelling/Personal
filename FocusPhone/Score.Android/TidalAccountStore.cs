using Android.Content;

namespace Score;

/// <summary>One person's TIDAL sign-in, mirroring <c>PersonalVault.TidalAccount</c>.</summary>
public sealed record TidalAccount(string UserId, string RefreshToken);

/// <summary>
/// Local-only holder for the TIDAL refresh token a device-code pairing produces.
///
/// <c>Storyworthtelling.Voice.Secrets.PersonalVault.TidalAccount</c> already models
/// where this belongs long-term — one person's sign-in, in that person's own Drive,
/// separate from the shared vault so two installs never share one subscription's
/// single stream. Nothing writes or reads that Drive file yet: there is no pairing
/// screen calling <c>TidalDeviceAuthClient.PollUntilAuthorizedAsync</c> to produce a
/// token in the first place, and there is not yet a Discover UI that gives someone a
/// TIDAL track to reach. Until that pairing flow exists, this local
/// SharedPreferences store is what <see cref="PlaybackService"/> reads before
/// attempting to build a TIDAL session — <see cref="Load"/> returning null is the
/// same "quietly do nothing" shape every other vault-backed feature in this app takes
/// when it is not yet configured, not a special case.
/// </summary>
public sealed class TidalAccountStore(Context context)
{
    private const string PrefsName = "tidal_account";
    private const string UserIdKey = "userId";
    private const string RefreshTokenKey = "refreshToken";

    private ISharedPreferences Prefs => context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    public TidalAccount? Load()
    {
        var refreshToken = Prefs.GetString(RefreshTokenKey, null);
        if (string.IsNullOrEmpty(refreshToken))
        {
            return null;
        }

        return new TidalAccount(Prefs.GetString(UserIdKey, "") ?? "", refreshToken);
    }

    public void Save(TidalAccount account) => Prefs.Edit()!
        .PutString(UserIdKey, account.UserId)!
        .PutString(RefreshTokenKey, account.RefreshToken)!
        .Apply();

    public void Clear() => Prefs.Edit()!.Clear()!.Apply();
}
