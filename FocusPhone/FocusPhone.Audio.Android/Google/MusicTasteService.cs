using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;
using Storyworthtelling.Voice;
using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Music;

namespace FocusPhone.Audio.Google;

/// <summary>
/// The device-side Listening Genius stack: reuses the shared Claude key + model tiers from the Drive voice
/// config (the owner enters the key once, in Storyboarder), refines the Taste Profile from the on-device
/// listening events, and stores the summary as one JSON file in Drive. Mirrors the FocusPhone voice greeting
/// wiring. Every call is best-effort — <see cref="TasteProfileRefresher"/> degrades to a no-change when the
/// key, profile, or Drive access isn't there, so the caller never has to guard.
///
/// Writing the profile needs the full <c>drive</c> scope (music reads alone were read-only); the token
/// provider now requests it and the owner registers the broadened scope on the OAuth client. Not yet
/// live-verified against Drive — the first real run is the verification, and every failure path is a no-op.
/// </summary>
public sealed class MusicTasteService
{
    private readonly DriveVoiceOptions options;
    private readonly ITasteProfileStore store;
    private readonly TasteProfileRefresher refresher;

    public MusicTasteService(Func<Task<string>> accessToken, string applicationName)
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

        options = new DriveVoiceOptions(
            new DriveVoiceConfigStore(new DriveTextFile(driveFactory, DriveVoiceConfigStore.FileName)));
        var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()) { Timeout = TimeSpan.FromMinutes(2) };
        var client = new TasteProfileClient(http, options);
        store = new DriveTasteProfileStore(new DriveTextFile(driveFactory, DriveTasteProfileStore.FileName));
        refresher = new TasteProfileRefresher(client, store, options);
    }

    /// <summary>Refreshes the Drive profile from the on-device events. Returns the profile (possibly unchanged).</summary>
    public async Task<TasteProfile> RefreshAsync(IReadOnlyList<ListeningEvent> events, CancellationToken cancellationToken)
    {
        try
        {
            await options.RefreshAsync(cancellationToken); // pull the shared key fresh from Drive
            return await refresher.RefreshAsync(events, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return TasteProfile.Empty;
        }
    }

    /// <summary>Reads the current Taste Profile from Drive, or Empty when there's none or on any failure.</summary>
    public async Task<TasteProfile> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return TasteProfile.Empty;
        }
    }
}
