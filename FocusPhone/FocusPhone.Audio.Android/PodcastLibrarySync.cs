using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;
using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;
using Storyworthtelling.Voice;

namespace FocusPhone.Audio;

/// <summary>
/// Backs the audio library (subscriptions + listened + resume positions + playback
/// speed/skip-silence settings + equalizer curve/presets/loudness) up to the owner's
/// Google Drive and restores it onto a fresh device, so the FocusPhone cutover's
/// factory reset — or a new phone — no longer loses which shows you follow, what you've
/// heard, or how you tuned playback. The phone is the single writer (a future watch will
/// read through the phone, not Drive), so this is a one-writer mirror: local wins while
/// it has shows and the cloud only repopulates a device with none. The reconcile verdict
/// is the unit-tested <see cref="AudioLibrarySync"/>; this class only carries it out,
/// moving state between the SharedPreferences stores and one Drive JSON file.
///
/// Writing the file needs the full <c>drive</c> scope the Audio head already carries
/// for the taste profile. Every call is best-effort: without Google sign-in or on any
/// failure it degrades to a no-op and the app keeps working from local storage. Like
/// the rest of the Drive rollout, it is not yet live-verified against Drive.
/// </summary>
public sealed class PodcastLibrarySync
{
    private readonly DriveAudioLibraryStore store;
    private readonly PodcastStore podcastStore;
    private readonly SubscriptionStore subscriptionStore;
    private readonly PlaybackProgressStore progressStore;
    private readonly PlaybackSettingsStore settingsStore;
    private readonly EqualizerStore equalizerStore;

    public PodcastLibrarySync(
        Func<Task<string>> accessToken,
        string applicationName,
        PodcastStore podcastStore,
        SubscriptionStore subscriptionStore,
        PlaybackProgressStore progressStore,
        PlaybackSettingsStore settingsStore,
        EqualizerStore equalizerStore)
    {
        this.podcastStore = podcastStore;
        this.subscriptionStore = subscriptionStore;
        this.progressStore = progressStore;
        this.settingsStore = settingsStore;
        this.equalizerStore = equalizerStore;

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

        store = new DriveAudioLibraryStore(new DriveTextFile(
            driveFactory, DriveAudioLibraryStore.FileName, Codex.Ui.Voice.SharedDriveFileIds.Store));
    }

    /// <summary>The current on-device library, read straight from the stores.</summary>
    private AudioLibrarySnapshot BuildLocal() =>
        new(
            subscriptionStore.Load(),
            [.. podcastStore.ListenedIds],
            progressStore.Load(),
            settingsStore.Load(),
            equalizerStore.LoadLibrary(),
            equalizerStore.LoudnessMillibels);

    /// <summary>
    /// Adopts the Drive backup when this device has no shows of its own (a reset or new
    /// phone). Returns true when it wrote local state, so the caller can re-render and
    /// refresh feeds. A no-op — and false — in every other case, including no backup.
    /// </summary>
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        var remote = await store.LoadAsync(cancellationToken);
        if (AudioLibrarySync.Decide(BuildLocal(), remote) != LibrarySyncDirection.Restore || remote is null)
        {
            return false;
        }

        subscriptionStore.Save(remote.Subscriptions);
        podcastStore.SaveListenedIds([.. remote.ListenedEpisodeIds]);
        progressStore.Save(remote.Progress);
        if (remote.Settings is { } settings)
        {
            settingsStore.Save(settings);
        }

        // Band gains are hardware-shaped; the editor and effect already ignore/clamp a mismatched
        // count (EqualizerActivity adopts a stored curve only when the band count matches, AudioEffects
        // applies the overlap), so restoring across devices degrades safely rather than crashing.
        if (remote.Equalizer is { } equalizer)
        {
            equalizerStore.SaveLibrary(equalizer);
        }

        equalizerStore.LoudnessMillibels = remote.LoudnessMillibels;
        return true;
    }

    /// <summary>
    /// Mirrors local up to Drive when it has drifted from the backup (or none exists).
    /// Reads the remote copy first so an unchanged library skips the write.
    /// </summary>
    public async Task BackupAsync(CancellationToken cancellationToken)
    {
        var local = BuildLocal();
        var remote = await store.LoadAsync(cancellationToken);
        if (AudioLibrarySync.Decide(local, remote) == LibrarySyncDirection.Backup)
        {
            await store.SaveAsync(local, cancellationToken);
        }
    }

    // Routes the Google client's HttpClient through AndroidMessageHandler (SELinux blocks the default
    // socket handler in an app process), matching the head's other Google clients.
    private sealed class AndroidGoogleHttpClientFactory : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
            new Xamarin.Android.Net.AndroidMessageHandler();
    }
}
