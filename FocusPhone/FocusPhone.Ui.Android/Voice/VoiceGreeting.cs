using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace FocusPhone.Ui.Voice;

// Builds the owner's-voice stack for a native FocusPhone head and composes a greeting in the owner's
// voice. A head supplies only an access-token getter (from its device-account sign-in); this owns the
// Google Drive glue, the Claude client, and the shared profile/config stores. ComposeAsync degrades to the
// empty string — never throws — when the profile, key, or Drive access isn't there, so a head just hides
// the greeting line and shows nothing extra.
public sealed class VoiceGreeting
{
    private readonly DriveVoiceOptions options;
    private readonly VoicedMessenger messenger;

    public VoiceGreeting(Func<Task<string>> accessToken, string applicationName)
    {
        Func<CancellationToken, Task<DriveService>> driveFactory = async _ =>
        {
            var token = await accessToken();
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token),
                ApplicationName = applicationName,
                HttpClientFactory = new AndroidGoogleHttpClientFactory()
            });
        };

        // The shared Claude key now lives in the encrypted secrets vault (the single source of truth, in
        // place of the old plaintext voice config). VaultVoiceConfigStore reads it back given this head's
        // passphrase; with no passphrase or no key it yields VoiceConfig.Empty and the greeting stays neutral.
        var vaultStore = new DriveSecretsVaultStore(new DriveTextFile(
            driveFactory, DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store));
        options = new DriveVoiceOptions(new VaultVoiceConfigStore(vaultStore, () => VaultPassphraseStore.Passphrase));
        var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()) { Timeout = TimeSpan.FromMinutes(2) };
        var client = new VoiceClient(http, options);
        messenger = new VoicedMessenger(new DriveVoiceProfileStore(new DriveTextFile(
            driveFactory, DriveVoiceProfileStore.FileName, SharedDriveFileIds.Store)), client);
    }

    // Composes a greeting in the owner's voice for the occasion, or "" when it can't (no profile, no key,
    // no Drive access, or a call failed). The key is pulled fresh from the shared Drive config first. A
    // greeting is a screen the owner reads, so it defaults to the strong model (highVisibility).
    public async Task<string> ComposeAsync(
        string purpose, string context, CancellationToken cancellationToken, bool highVisibility = true)
    {
        try
        {
            await options.RefreshAsync(cancellationToken);
            return await messenger.ComposeAsync(
                new MessageRequest(purpose, context, HighVisibility: highVisibility),
                fallback: string.Empty, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    // Routes the Google API client's HttpClient through AndroidMessageHandler (SELinux blocks the default
    // socket handler in an app process), matching the app's other Google clients.
    private sealed class AndroidGoogleHttpClientFactory : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
            new Xamarin.Android.Net.AndroidMessageHandler();
    }
}
