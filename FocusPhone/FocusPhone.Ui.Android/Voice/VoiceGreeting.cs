using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;
using Storyworthtelling.Voice;

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

        options = new DriveVoiceOptions(new DriveVoiceConfigStore(new DriveTextFile(driveFactory, DriveVoiceConfigStore.FileName)));
        var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()) { Timeout = TimeSpan.FromMinutes(2) };
        var client = new VoiceClient(http, options);
        messenger = new VoicedMessenger(new DriveVoiceProfileStore(new DriveTextFile(driveFactory, DriveVoiceProfileStore.FileName)), client);
    }

    // Composes a greeting in the owner's voice for the occasion, or "" when it can't (no profile, no key,
    // no Drive access, or a call failed). The key is pulled fresh from the shared Drive config first.
    public async Task<string> ComposeAsync(string purpose, string context, CancellationToken cancellationToken)
    {
        try
        {
            await options.RefreshAsync(cancellationToken);
            return await messenger.ComposeAsync(new MessageRequest(purpose, context), fallback: string.Empty, cancellationToken);
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
