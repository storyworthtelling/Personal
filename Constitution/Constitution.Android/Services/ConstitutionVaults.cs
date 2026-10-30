using Android.Content;
using Codex.Ui.Voice;
using Constitution.Infrastructure.Vault;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Services;

/// <summary>
/// Carries <see cref="ConstitutionVaultRunner"/> to the device: builds the Drive stores over the signed-in
/// Google account the way the sibling heads do and reads the passphrase from the shared
/// <see cref="VaultPassphraseStore"/> (the one passphrase store; this adds no second). The orchestration
/// itself, which is what the tests exercise, lives in the runner; this is only the wiring. Drive is built
/// with <see cref="SecretsVaultAccess"/>'s own recipe (its stores are private to it, and the sync needs
/// the stores themselves rather than the access object).
/// </summary>
public static class ConstitutionVaults
{
    private const string ApplicationName = "Constitution";

    /// <summary>The runner wired to this device: shared passphrase store, the head's Google account, Drive.</summary>
    public static ConstitutionVaultRunner Create(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var log = new ConstitutionVaultLog(appContext);
        var account = ConstitutionGoogle.Create();
        return new ConstitutionVaultRunner(
            () => VaultPassphraseStore.Passphrase,
            () => VoiceGoogleAccount.IsSignedIn,
            () => BuildSync(appContext, account, log),
            log);
    }

    /// <summary>
    /// The single call a launch makes: restores in the background when there is a passphrase and an
    /// account, and otherwise does nothing. Fire-and-forget by design; the result goes to the trail.
    /// </summary>
    public static void RestoreAtLaunch(Context context)
    {
        var runner = Create(context);
        _ = runner.RestoreAsync(CancellationToken.None);
    }

    /// <summary>
    /// The Drive service over the signed-in account's token, built fresh for each call. The vault and the household file both
    /// read and write through it, so there is one recipe for the client.
    /// </summary>
    public static Func<CancellationToken, Task<DriveService>> DriveFactory(VoiceGoogleAccount account) => async _ =>
    {
        var token = await account.GetAccessToken();
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(token),
            ApplicationName = ApplicationName,
            HttpClientFactory = new AndroidGoogleHttpClientFactory(),
        });
    };

    private static ConstitutionVaultSync BuildSync(Context context, VoiceGoogleAccount account, IConstitutionVaultLog log)
    {
        var driveFactory = DriveFactory(account);

        var shared = new DriveSecretsVaultStore(new DriveTextFile(
            driveFactory, DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store));
        var personal = new DrivePersonalVaultStore(new DriveTextFile(
            driveFactory, DrivePersonalVaultStore.FileName, SharedDriveFileIds.Store));
        return new ConstitutionVaultSync(
            shared, personal, () => VoiceGoogleAccount.Email, new ConstitutionConnections(context), log);
    }

    // Routes the Google API client through AndroidMessageHandler, as the sibling heads' Drive clients do.
    private sealed class AndroidGoogleHttpClientFactory : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
            new Xamarin.Android.Net.AndroidMessageHandler();
    }
}
