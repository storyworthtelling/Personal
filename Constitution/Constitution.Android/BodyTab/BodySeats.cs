using Android.Content;
using Codex.Ui.Voice;
using Constitution.Domain.Voice;
using Constitution.Infrastructure.Voice;
using Constitution.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.BodyTab;

/// <summary>
/// Composes Body's seats (the month's sentence and a signal's) in the owner's voice, once for the same
/// facts and day, and otherwise hands back the plain sentence the request came with. Never throws a
/// failure at the page and never hides the line: with no sign-in, no key, no profile or no network the
/// answer is the fallback. Built like the sibling heads' voice stack (<c>VoiceGreeting</c>): Drive reached
/// through the signed-in Google account, the Claude key read from the vault.
/// </summary>
internal static class BodySeats
{
    private const string ApplicationName = "Constitution";

    private static readonly object Gate = new();
    private static SeatCache? cache;

    /// <summary>The line for a request. The plain sentence at once when nobody is signed in to reach the voice with.</summary>
    public static async Task<SeatLine> ComposeAsync(Context context, SeatRequest request, CancellationToken cancellationToken)
    {
        if (!VoiceGoogleAccount.IsSignedIn)
        {
            return new SeatLine(request.Seat, request.Fallback, IsVoiced: false);
        }

        return await CacheFor(context).ComposeAsync(request, DateTimeOffset.Now, cancellationToken);
    }

    private static SeatCache CacheFor(Context context)
    {
        lock (Gate)
        {
            return cache ??= Build(context.ApplicationContext ?? context);
        }
    }

    private static SeatCache Build(Context context)
    {
        var account = ConstitutionGoogle.Create();
        Func<CancellationToken, Task<DriveService>> driveFactory = async _ =>
        {
            var token = await account.GetAccessToken();
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token),
                ApplicationName = ApplicationName,
                HttpClientFactory = new AndroidGoogleHttpClientFactory(),
            });
        };

        // The Claude key is read from the shared vault with this head's passphrase; with none, the options stay empty and the seat is plain.
        var vault = new DriveSecretsVaultStore(new DriveTextFile(driveFactory, DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store));
        var options = new DriveVoiceOptions(new VaultVoiceConfigStore(vault, () => VaultPassphraseStore.Passphrase));
        var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()) { Timeout = TimeSpan.FromMinutes(2) };
        var messenger = new VoicedMessenger(
            new DriveVoiceProfileStore(new DriveTextFile(driveFactory, DriveVoiceProfileStore.FileName, SharedDriveFileIds.Store)),
            new VoiceClient(http, options));

        return new SeatCache(new SeatComposer(messenger, options), new BodySeatLineStore(context));
    }

    // Routes the Google API client through AndroidMessageHandler, as the sibling heads' Drive clients do.
    private sealed class AndroidGoogleHttpClientFactory : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
            new Xamarin.Android.Net.AndroidMessageHandler();
    }
}
