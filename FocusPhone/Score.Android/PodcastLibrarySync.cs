using Storyworthtelling.Google.Android;
using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;
using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;
using Storyworthtelling.Voice;

namespace Score;

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
/// Writing the file needs the full <c>drive</c> scope Score already carries
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
            return GoogleDrive.Service(token, applicationName);
        };

        store = new DriveAudioLibraryStore(new DriveTextFile(
            driveFactory, DriveAudioLibraryStore.FileName, Codex.Ui.Voice.SharedDriveFileIds.Store, readCache: DriveReadCache.Shared));
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
    // Whether this install has once read the Drive backup (found it, or found none). Until it has, a reconcile
    // on a phone with shows of its own merges both ways instead of letting its first show replace the library
    // (error-handling §17); after it, the phone is the authority and an unsubscribe propagates.
    private const string PrefsName = "library-sync";
    private const string RestoreConfirmedKey = "restore-confirmed";

    private static bool RestoreConfirmed
    {
        get => global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!
            .GetBoolean(RestoreConfirmedKey, false);
        set => global::Android.App.Application.Context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!
            .Edit()!.PutBoolean(RestoreConfirmedKey, value)!.Commit();
    }

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        var read = await store.ReadAsync(cancellationToken);
        if (!read.Readable)
        {
            // A damaged backup is left for the owner rather than replaced or restored from.
            return false;
        }

        var remote = read.Snapshot;
        var direction = AudioLibrarySync.Decide(BuildLocal(), remote, RestoreConfirmed);
        if (direction == LibrarySyncDirection.Restore && remote is not null)
        {
            Apply(remote);
            RestoreConfirmed = true;
            return true;
        }
        if (direction == LibrarySyncDirection.Merge && remote is not null)
        {
            var merged = AudioLibrarySync.Union(BuildLocal(), remote);
            Apply(merged);
            await store.SaveAsync(merged, cancellationToken);
            RestoreConfirmed = true;
            return true;
        }

        RestoreConfirmed = true;
        return false;
    }

    public async Task BackupAsync(CancellationToken cancellationToken)
    {
        var read = await store.ReadAsync(cancellationToken);
        if (!read.Readable)
        {
            return;
        }

        var local = BuildLocal();
        switch (AudioLibrarySync.Decide(local, read.Snapshot, RestoreConfirmed))
        {
            case LibrarySyncDirection.Backup:
                await store.SaveAsync(local, cancellationToken);
                break;
            case LibrarySyncDirection.Merge:
                var merged = AudioLibrarySync.Union(local, read.Snapshot!);
                Apply(merged);
                await store.SaveAsync(merged, cancellationToken);
                break;
        }

        RestoreConfirmed = true;
    }

    private void Apply(AudioLibrarySnapshot library)
    {
        subscriptionStore.Save(library.Subscriptions);
        podcastStore.SaveListenedIds([.. library.ListenedEpisodeIds]);
        progressStore.Save(library.Progress);
        if (library.Settings is { } settings)
        {
            settingsStore.Save(settings);
        }

        if (library.Equalizer is { } equalizer)
        {
            equalizerStore.SaveLibrary(equalizer);
        }

        equalizerStore.LoudnessMillibels = library.LoudnessMillibels;
    }

}
