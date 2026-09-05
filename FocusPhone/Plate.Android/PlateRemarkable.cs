using Android.App;
using Android.Content;
using Codex.Ui.Voice;
using Microsoft.Extensions.Options;
using Storyworthtelling.Paper;
using Storyworthtelling.Remarkable;
using Storyworthtelling.Voice.Secrets;

namespace Plate;

/// <summary>
/// Plate's end of the reMarkable, and the only piece of a scan's life that does not go
/// through another app on the phone.
///
/// <para><b>The delivery goes to the account root</b>, not into a folder of Plate's own.
/// That is a risk reduction rather than a simplification: the repo records the reMarkable
/// v2 cloud path as reconstructed and not live-verified, and the folder/collection wire
/// format especially so. The plain root upload is the one part of that library DailyLoader
/// has actually exercised, so Plate depends on nothing else.</para>
///
/// <para>The device token is the reset-survival credential — a one-time pairing that would
/// otherwise have to be repeated on every wipe — so it lives in the shared vault and is
/// restored from there, exactly as DailyLoader and Canonizer restore it. Pairing in any one
/// of the three is enough for all of them.</para>
/// </summary>
public sealed class PlateRemarkable(VoiceGoogleAccount account)
{
    /// <summary>Plate's own copy of the paired token, refilled from the vault after a reset.</summary>
    private const string PrefsName = "remarkable";
    private const string DeviceTokenKey = "deviceToken";

    private static ISharedPreferences Prefs() =>
        Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    public static string DeviceToken
    {
        get => Prefs().GetString(DeviceTokenKey, string.Empty) ?? string.Empty;
        set => Prefs().Edit()!.PutString(DeviceTokenKey, value)!.Apply();
    }

    public static bool IsPaired => !string.IsNullOrEmpty(DeviceToken);

    /// <summary>
    /// The one-time device-code pairing. The owner reads a code off my.remarkable.com and
    /// types it in; what comes back is the long-lived token everything else exchanges.
    /// </summary>
    public static async Task<string> Pair(string oneTimeCode, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
        var token = await new RemarkableRegistrationClient(http).Register(oneTimeCode, cancellationToken);
        DeviceToken = token;
        return token;
    }

    /// <summary>
    /// Sends a finished PDF to the root of the paired tablet. Throws rather than swallowing:
    /// the caller is a screen with a status line, and a delivery that silently did nothing
    /// is the failure this phase exists to stop.
    /// </summary>
    public async Task DeliverToRoot(string title, byte[] pdf, CancellationToken cancellationToken = default)
    {
        if (!IsPaired)
        {
            throw new InvalidOperationException("The reMarkable is not paired on this phone.");
        }

        // Two clients rather than one: the auth exchange and the storage calls are separate
        // hosts, and the shared library takes an HttpClient each.
        using var authHttp = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
        using var storageHttp = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());

        var auth = new RemarkableAuthService(
            authHttp, Options.Create(new RemarkableOptions { DeviceToken = DeviceToken }));
        var delivery = new PaperDelivery(new RemarkableStorageClient(storageHttp, auth));
        await delivery.DeliverToRemarkableRoot(title, pdf, cancellationToken);
    }

    /// <summary>
    /// Refills the token from the shared vault when this phone has none — after a reinstall,
    /// or the cutover's factory reset. Never overwrites a token that is already here, and
    /// never throws: a head that cannot reach Drive still works from whatever it has, and
    /// Settings always accepts a fresh pairing by hand.
    /// </summary>
    public Task RestoreFromVault() => Task.Run(async () =>
    {
        try
        {
            if (IsPaired || !VaultPassphraseStore.IsSet || !VoiceGoogleAccount.IsSignedIn)
            {
                return;
            }

            var vault = new SecretsVaultAccess(account.GetAccessToken, "Plate");
            var result = await vault.Service.LoadAsync(CancellationToken.None);
            if (result.Status != SecretsVaultStatus.Loaded)
            {
                return;
            }

            SharedDriveFileIds.SeedFrom(result.Vault.DriveFileIds);
            if (!string.IsNullOrEmpty(result.Vault.Remarkable.DeviceToken))
            {
                DeviceToken = result.Vault.Remarkable.DeviceToken;
            }
        }
        catch
        {
            // Restore is a convenience. Settings pairs by hand when it cannot happen.
        }
    });

    /// <summary>
    /// Backs a fresh pairing up so the other apps inherit it. Best-effort for the same
    /// reasons as the restore, and it merges rather than replaces — publishing a section
    /// over a vault this head could not read would take the other apps' keys with it.
    /// </summary>
    public Task PublishToVault() => Task.Run(async () =>
    {
        try
        {
            if (!IsPaired || !VaultPassphraseStore.IsSet || !VoiceGoogleAccount.IsSignedIn)
            {
                return;
            }

            var vault = new SecretsVaultAccess(account.GetAccessToken, "Plate");
            await vault.Service.PublishAsync(
                current => current with { Remarkable = new RemarkableSecrets(DeviceToken) },
                CancellationToken.None);
        }
        catch
        {
            // The pairing still works on this phone; only the backup did not happen.
        }
    });
}
