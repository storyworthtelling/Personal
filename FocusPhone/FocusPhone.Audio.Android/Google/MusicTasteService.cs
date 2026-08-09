using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Music;
using FocusPhone.Ui.Voice;

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
    private readonly IDiscoveryClient discovery;

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

        // The shared Claude key now lives in the encrypted secrets vault (single source of truth, in place of
        // the old plaintext voice config); VaultVoiceConfigStore reads it back with this head's passphrase.
        var vaultStore = new DriveSecretsVaultStore(new DriveTextFile(driveFactory, DriveSecretsVaultStore.FileName));
        options = new DriveVoiceOptions(
            new VaultVoiceConfigStore(vaultStore, () => VaultPassphraseStore.Passphrase));
        var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()) { Timeout = TimeSpan.FromMinutes(2) };
        var client = new TasteProfileClient(http, options);
        store = new DriveTasteProfileStore(new DriveTextFile(driveFactory, DriveTasteProfileStore.FileName));
        refresher = new TasteProfileRefresher(client, store, options);
        discovery = new DiscoveryClient(http, options);
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

    /// <summary>Generates a prompted playlist and resolves it to playable catalog tracks. Empty on any failure.</summary>
    public async Task<DiscoveryPlaylist> MakePlaylistAsync(
        IReadOnlyList<Track> catalog, PlaylistRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await options.RefreshAsync(cancellationToken);
            var profile = await store.LoadAsync(cancellationToken);
            var selection = await discovery.GeneratePlaylistAsync(profile, catalog, request, cancellationToken);
            return PlaylistPlan.Resolve(selection.Title, selection.TrackIds, catalog, request.MaxTracks);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return DiscoveryPlaylist.Empty;
        }
    }

    /// <summary>Picks the weekly "New for you" set as a raw selection (title + ids) for the worker to persist.</summary>
    public async Task<DiscoverySelection> FridayPicksAsync(
        IReadOnlyList<Track> catalog, int maxTracks, CancellationToken cancellationToken)
    {
        try
        {
            await options.RefreshAsync(cancellationToken);
            var profile = await store.LoadAsync(cancellationToken);
            if (profile.IsEmpty)
            {
                return DiscoverySelection.Empty; // no taste learned yet -> nothing to surface
            }

            return await discovery.FridayPicksAsync(profile, catalog, maxTracks, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return DiscoverySelection.Empty;
        }
    }
}
