using AndroidX.Lifecycle;
using AndroidX.Work;
using Codex.Domain.Audio;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Ui.Voice;
using Score.Google;
using Storyworthtelling.Voice.Secrets;

namespace Score;

/// <summary>
/// The music side's state and operations, with no views of its own: the Drive
/// account and folder, the scanned catalog, and starting playback from it.
///
/// It is separate from <see cref="MusicArea"/> because two areas read it — browse
/// and Discover both need the catalog and both start tracks — and neither should own
/// state the other depends on. Raising <see cref="Changed"/> rather than calling the
/// shell is what lets both of them be redrawn by one scan finishing.
/// </summary>
public sealed class MusicLibrary(
    Activity host,
    DeviceAccountTokenProvider tokens,
    ScorePlayback playback,
    ListeningLog listening,
    PlaybackProgressStore progress)
{
    // TIDAL's own catalogue coverage is the deciding factor for this, not the
    // owner's locale — see the B7 streaming-decision note in CLAUDE.md — so this is
    // fixed rather than read from the device, matching every other hardcoded default
    // in this app until a real region setting exists anywhere. Shared with
    // DiscoverArea's search so a track validates against the same regional
    // availability it was found under.
    public const string TidalCountryCode = "US";

    // Re-checking every saved track costs one HTTP call each, so this is deliberately
    // infrequent — losing streaming access to a track a day late is a much smaller
    // problem than a background pass that runs on every app open.
    private static readonly TimeSpan RevalidateInterval = TimeSpan.FromDays(1);

    private readonly DriveMusicClient driveClient = new(tokens);
    private readonly SavedTidalTracksStore savedTidal = new(host);

    // Music had no queue at all: it played by pushing items straight into the
    // player's timeline, which died with the process and could not be looked at.
    private readonly PlaylistStore playlistStore = new(host);
    private readonly PlaybackQueue queue = new();
    private readonly QueueStore queueStore = QueueStore.ForMusic(host);
    private bool queueRestored;

    /// <summary>Raised when the catalog, the folder list or the status line moved.</summary>
    public event Action? Changed;

    /// <summary>Never null — an unscanned library is an empty one, not a missing one.</summary>
    public MusicCatalog Catalog { get; private set; } = new([]);

    /// <summary>A sentence about work in flight, or null. DESIGN.md §7: status, not a spinner.</summary>
    public string? Status { get; private set; }

    /// <summary>Drive folders offered by the picker; null until fetched.</summary>
    public IReadOnlyList<DriveMusicClient.DriveFolder>? Folders { get; private set; }

    public bool IsSignedIn => !string.IsNullOrEmpty(MusicSettingsStore.GoogleAccountEmail);

    public bool HasFolder => !string.IsNullOrEmpty(MusicSettingsStore.MusicFolderId);

    public ListeningLog Listening => listening;

    /// <summary>The owner's playlists, newest last, as they were saved.</summary>
    public IReadOnlyList<Playlist> Playlists() => playlistStore.Load();

    public void SavePlaylists(IReadOnlyList<Playlist> playlists) => playlistStore.Save(playlists);

    // Restored lazily rather than in the constructor, because the catalog may not
    // have loaded yet at construction.
    //
    // Nothing reads the restored queue back yet, and that is deliberate rather than
    // forgotten: showing a queue after a restart is only worth anything alongside a
    // resume position, which is the next slice. Writing it has to come first — there
    // is nothing to restore until something has been saved.
    private void Restore()
    {
        if (!queueRestored)
        {
            queueRestored = true;
            queue.ReplaceWith(queueStore.Load());
        }
    }

    public void LoadCatalog()
    {
        using var database = MusicDatabase.Open(host);

        // One library out of both sources. The streamed half is empty until the
        // provider is wired, and this is deliberately the seam rather than a branch
        // added later: when it fills, nothing downstream learns that it did — which
        // is the whole of the merged-library rule.
        Catalog = new MusicCatalog(LibraryMerge.Merge(database.Catalog.LoadAll(), Streamed()));
    }

    // What the streaming provider carries: the tracks the owner has explicitly saved
    // from a Discover search (see SavedTidalTracksStore) — never a bare search result,
    // which is why this is not the same thing as "everything TIDAL search has ever
    // returned". This is the fill spoken of above: nothing downstream had to change
    // to pick these up once there was something here.
    private IReadOnlyList<Track> Streamed() => savedTidal.Load();

    public bool IsTrackSaved(string trackId) => savedTidal.Contains(trackId);

    /// <summary>
    /// Saves a TIDAL search result into the library. Inline state change, not a
    /// dialog (§7) — the caller re-renders the row to say Saved once this returns.
    /// Reloads the merged catalog immediately, so the track shows up in Songs and
    /// playlists without waiting for the next scan, which may never come.
    /// </summary>
    public void SaveTrack(Track track)
    {
        savedTidal.Add(track);
        LoadCatalog();
        Changed?.Invoke();
        _ = EnrichSavedTrackAsync(track);
    }

    /// <summary>
    /// Fills in what TIDAL search results never carry — cover art (its own
    /// relationship, not a search result attribute; see
    /// <c>TidalCatalogClient.GetTrackArtworkUrlAsync</c>'s remarks) and tempo (TIDAL's
    /// catalogue does not report BPM at all, the same reason Drive files fall back to
    /// Deezer's keyless lookup — see CLAUDE.md's "Not from TIDAL" note). Run after
    /// the save completes, not before, so the inline Saved state change (§7) never
    /// waits on two extra network calls; best-effort throughout, since a track with
    /// no artwork or no known tempo is still a perfectly usable saved track.
    /// </summary>
    private async Task EnrichSavedTrackAsync(Track track)
    {
        try
        {
            var probe = await new TidalAccessTokenProvider(host).RefreshAsync(CancellationToken.None);
            if (probe.Status != TidalTokenPollStatus.Success || probe.AccessToken is not { Length: > 0 } accessToken)
            {
                return;
            }

            var rawId = track.Id.StartsWith("tidal:", StringComparison.Ordinal)
                ? track.Id["tidal:".Length..]
                : track.Id;
            var catalog = new TidalCatalogClient(new HttpClient());
            var artworkUrl = await catalog.GetTrackArtworkUrlAsync(
                accessToken, rawId, TidalCountryCode, CancellationToken.None);
            var artPath = artworkUrl is null
                ? null
                : await new TidalArtworkCache(host).DownloadAsync(rawId, artworkUrl, CancellationToken.None);

            var bpm = await new DeezerTempoClient(new HttpClient())
                .LookupAsync(track.ArtistName, track.Title, CancellationToken.None);

            if (artPath is null && bpm == 0)
            {
                return; // Nothing learned — no point rewriting the record.
            }

            savedTidal.Replace(track with { ArtRef = artPath ?? track.ArtRef, Bpm = bpm != 0 ? bpm : track.Bpm });
            LoadCatalog();
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning("MusicLibrary", $"EnrichSavedTrackAsync threw: {e}");
        }
    }

    /// <summary>The undo side of <see cref="SaveTrack"/> — a saved track leaving the
    /// library the same way a subscription does, immediately and reversibly.</summary>
    public void RemoveSavedTrack(string trackId)
    {
        savedTidal.Remove(trackId);
        LoadCatalog();
        Changed?.Invoke();
    }

    /// <summary>
    /// Re-checks every saved TIDAL track against the catalogue and marks the ones
    /// that no longer resolve as <see cref="TrackAvailability.Unavailable"/> — the
    /// one thing a save alone can never do, since nothing else in this app ever asks
    /// TIDAL about a track again after it is saved. A track that comes back
    /// <i>available</i> after having been marked Unavailable is restored to
    /// Streaming too — a lapsed subscription renewing, or a region change reversing,
    /// should not leave a track permanently struck off.
    ///
    /// Best-effort and silently skipped, not just on failure but by design, when
    /// there is nothing signed in or nothing saved — this runs unconditionally from
    /// app launch (see ScoreActivity), so it has to be exactly as quiet as
    /// RestoreFolderFromVault about a TIDAL account that does not exist yet.
    /// Throttled to <see cref="RevalidateInterval"/> so a saved list of any real size
    /// does not mean a burst of network calls on every cold start.
    /// </summary>
    public async Task RevalidateSavedTidalTracksAsync()
    {
        try
        {
            var last = savedTidal.LastValidatedUtc;
            if (last is { } checkedAt && DateTimeOffset.UtcNow - checkedAt < RevalidateInterval)
            {
                return;
            }

            var saved = savedTidal.Load();
            if (saved.Count == 0)
            {
                return;
            }

            var probe = await new TidalAccessTokenProvider(host).RefreshAsync(CancellationToken.None);
            if (probe.Status != TidalTokenPollStatus.Success || probe.AccessToken is not { Length: > 0 } accessToken)
            {
                return; // Not signed in, or a transient failure — try again next launch.
            }

            var catalog = new TidalCatalogClient(new HttpClient());
            var changed = false;
            foreach (var track in saved)
            {
                var rawId = track.Id.StartsWith("tidal:", StringComparison.Ordinal)
                    ? track.Id["tidal:".Length..]
                    : track.Id;
                var found = await catalog.GetTrackAsync(accessToken, rawId, TidalCountryCode, CancellationToken.None);
                var isAvailable = found is not null;
                var wasAvailable = track.Availability != TrackAvailability.Unavailable;
                if (isAvailable == wasAvailable)
                {
                    continue;
                }

                savedTidal.Replace(track with
                {
                    Availability = isAvailable ? TrackAvailability.Streaming : TrackAvailability.Unavailable,
                });
                changed = true;
            }

            savedTidal.LastValidatedUtc = DateTimeOffset.UtcNow;
            if (changed)
            {
                LoadCatalog();
                Changed?.Invoke();
            }
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning("MusicLibrary", $"RevalidateSavedTidalTracksAsync threw: {e}");
        }
    }

    public async Task SignInAsync()
    {
        try
        {
            await DeviceAccountSignIn.SignIn(host);
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Toast.MakeText(host, e.Message, ToastLength.Long)!.Show();
        }
    }

    public async Task LoadFoldersAsync()
    {
        try
        {
            Folders = await driveClient.ListFoldersAsync("root", CancellationToken.None);
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Toast.MakeText(host, e.Message, ToastLength.Long)!.Show();
        }
    }

    public void ChooseFolder(string folderId)
    {
        MusicSettingsStore.MusicFolderId = folderId;
        PublishFolderToVault(folderId);
        Folders = null;
        Changed?.Invoke();
        _ = ScanAsync();
    }

    // Held so the observer can be torn down when the scan reaches a terminal state.
    private WorkInfoObserver? scanObserver;

    /// <summary>
    /// Kicks off a library scan under WorkManager (see <see cref="MusicScanWorker"/>)
    /// so it survives Score being backgrounded, and watches its progress while this
    /// screen is up. Returns as soon as the work is enqueued — the status line and the
    /// catalog reload now come from the observer, not from awaiting the scan inline.
    /// </summary>
    public Task ScanAsync()
    {
        if (MusicSettingsStore.MusicFolderId is null)
        {
            return Task.CompletedTask;
        }

        MusicScanWorker.Enqueue(host);
        Status = "Scanning your library…";
        Changed?.Invoke();
        ObserveScan();
        return Task.CompletedTask;
    }

    private void ObserveScan() => host.RunOnUiThread(() =>
    {
        if (scanObserver is not null)
        {
            return; // Already watching this unique work.
        }

        scanObserver = new WorkInfoObserver(this);
        WorkManager.GetInstance(host)
            .GetWorkInfosForUniqueWorkLiveData(MusicScanWorker.WorkName)!
            .ObserveForever(scanObserver);
    });

    private void StopObservingScan()
    {
        if (scanObserver is null)
        {
            return;
        }

        WorkManager.GetInstance(host)
            .GetWorkInfosForUniqueWorkLiveData(MusicScanWorker.WorkName)!
            .RemoveObserver(scanObserver);
        scanObserver = null;
    }

    private void OnScanWorkInfo(WorkInfo info)
    {
        var state = info.GetState();
        if (state == WorkInfo.State.Running)
        {
            var done = info.Progress!.GetInt(MusicScanWorker.ProgressDone, 0);
            var total = info.Progress.GetInt(MusicScanWorker.ProgressTotal, 0);
            Status = total > 0 && done < total ? $"Scanning… {done} of {total}" : "Scanning your library…";
            Changed?.Invoke();
            return;
        }

        var finished = state == WorkInfo.State.Succeeded
            || state == WorkInfo.State.Failed
            || state == WorkInfo.State.Cancelled;
        if (!finished)
        {
            return; // Enqueued/Blocked: nothing to show yet.
        }

        if (state == WorkInfo.State.Succeeded)
        {
            LoadCatalog();
            Status = null;
        }
        else
        {
            Status = state == WorkInfo.State.Failed ? "Scan couldn't finish." : null;
        }

        StopObservingScan();
        Changed?.Invoke();
    }

    // WorkManager hands progress and terminal state back on the main thread as a list
    // of WorkInfo for the unique work — there is only ever one, so the first is it.
    private sealed class WorkInfoObserver(MusicLibrary owner) : Java.Lang.Object, IObserver
    {
        public void OnChanged(Java.Lang.Object? value)
        {
            if (value is not Android.Runtime.JavaList list)
            {
                return;
            }

            foreach (var item in list)
            {
                if (item is WorkInfo info)
                {
                    owner.OnScanWorkInfo(info);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// After a reinstall
    /// from the encrypted vault, so the library reappears without re-picking it. Runs
    /// only when none is set locally; best-effort and off the UI thread.
    /// </summary>
    public void RestoreFolderFromVault() => Task.Run(async () =>
    {
        try
        {
            if (HasFolder)
            {
                return;
            }

            var access = new SecretsVaultAccess(tokens.GetAccessToken, "Score", () => ScoreIdentity.Account);
            var result = await access.Service.LoadAsync(CancellationToken.None);
            if (result.Status != SecretsVaultStatus.Loaded)
            {
                return;
            }

            // Drive-by-ID (#13): pick up the ids persisted for the shared files.
            SharedDriveFileIds.SeedFrom(result.Vault.DriveFileIds);
            if (!string.IsNullOrEmpty(result.Vault.Music.FolderId) && !HasFolder)
            {
                MusicSettingsStore.MusicFolderId = result.Vault.Music.FolderId;
                host.RunOnUiThread(() =>
                {
                    Changed?.Invoke();
                    _ = ScanAsync();
                });
            }
        }
        catch
        {
            // Ignore: restore is a convenience; the folder picker is always available.
        }
    });

    public void PlayTracks(IReadOnlyList<Track> tracks, int index, PlaybackLaunch launch = PlaybackLaunch.Album)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        // playback.WhenReady, not an early return on Controller is null: a tap landing in the
        // brief cold-start window before the controller connects used to be dropped with no
        // feedback at all (the bottom bar reads persisted state directly, so it looked queued
        // while nothing actually started) — see WhenReady's remarks.
        playback.WhenReady(() =>
        {
            // Putting something on REPLACES the music run — choosing an album is choosing
            // what to listen to, not adding to a list. Only Enqueue builds one up.
            Restore();
            queue.ReplaceWith(tracks.Select(track => track.Id));
            queueStore.Save(queue.Export());

            // Speed 1.0 and skip-silence off: both are podcast settings, and a track must
            // not inherit them from whatever episode played last.
            playback.Start(MediaKind.Music, [.. tracks.Select(BuildItem)], index, 0L, 1.0f, skipSilence: false);

            // Start a listening sitting for Genius; replay is judged against the saved progress book.
            var book = new PlaybackProgressBook(progress.Load());
            listening.BeginSelection(tracks, index, launch, book.IsComplete);
        });
    }

    /// <summary>
    /// Adds to the end of the music run — the only thing that builds a queue up
    /// rather than replacing it.
    /// </summary>
    public void Enqueue(IReadOnlyList<Track> tracks) => AddToQueue(tracks, next: false);

    /// <summary>Adds directly behind what is playing.</summary>
    public void PlayNext(IReadOnlyList<Track> tracks) => AddToQueue(tracks, next: true);

    private void AddToQueue(IReadOnlyList<Track> tracks, bool next)
    {
        if (playback.Controller is not { } controller || tracks.Count == 0)
        {
            return;
        }

        Restore();

        // Nothing of ours is playing, so there is no run to add to — and a queue the
        // app does not yet show anywhere would be an invisible no-op. Start it.
        if (playback.Kind != MediaKind.Music || controller.MediaItemCount == 0)
        {
            PlayTracks(tracks, 0);
            return;
        }

        // Anything already queued is left where it is. The queue holds an id once,
        // and the player's timeline is kept in lockstep with it by only ever adding
        // what is genuinely new — a second copy in the timeline is a drift that shows
        // up later as the wrong track playing.
        var fresh = tracks.Where(track => !queue.Items.Contains(track.Id)).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        var items = fresh.Select(BuildItem).ToList();
        if (next)
        {
            queue.InsertAfter(playback.CurrentMediaId, fresh.Select(track => track.Id));
            controller.AddMediaItems(controller.CurrentMediaItemIndex + 1, items);
        }
        else
        {
            foreach (var track in fresh)
            {
                queue.Add(track.Id);
            }

            controller.AddMediaItems(items);
        }

        queueStore.Save(queue.Export());
    }

    /// <summary>
    /// Plays one record to hear what it is, without filing it. Discover's Play — it
    /// starts the track and leaves the stored music queue exactly where it was, so
    /// auditioning something cannot cost you a queue you assembled.
    /// </summary>
    public void Audition(Track track) => playback.WhenReady(() =>
    {
        playback.Start(MediaKind.Music, [BuildItem(track)], 0, 0L, 1.0f, skipSilence: false);

        var book = new PlaybackProgressBook(progress.Load());
        listening.BeginSelection([track], 0, PlaybackLaunch.Album, book.IsComplete);
    });

    /// <summary>
    /// Starts a workout run: an <b>ephemeral</b> queue that is neither saved nor
    /// logged.
    ///
    /// Both halves of that are deliberate. It does not touch the stored music queue,
    /// so a workout cannot cost you a run you assembled; and it does not open a
    /// listening sitting, so what you ran to never reaches the taste profile. Music
    /// chosen for its tempo is not evidence of taste — it is evidence of a pace — and
    /// letting it in would teach the profile the wrong thing about the owner.
    /// </summary>
    public void StartWorkout(IReadOnlyList<Track> tracks, bool fade)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        playback.WhenReady(() => StartWorkoutReady(tracks, fade));
    }

    private void StartWorkoutReady(IReadOnlyList<Track> tracks, bool fade)
    {
        var items = tracks.Select(BuildItem).ToList();
        if (fade)
        {
            playback.FadeInto(MediaKind.Music, items, 0);
            return;
        }

        playback.Start(MediaKind.Music, items, 0, 0L, 1.0f, skipSilence: false);
    }

    public void PlayShuffled(IReadOnlyList<Track> tracks) =>
        PlayTracks(new ShufflePlanner().Shuffle(tracks), 0, PlaybackLaunch.Shuffle);

    public void PlaySmartMix(Track seed) =>
        PlayTracks(new SmartMixBuilder(new ShufflePlanner()).Build(seed, Catalog), 0, PlaybackLaunch.Shuffle);

    private static AndroidX.Media3.Common.MediaItem BuildItem(Track track)
    {
        var metadata = new AndroidX.Media3.Common.MediaMetadata.Builder()
            .SetTitle(track.DisplayTitle)!
            .SetArtist(track.DisplayArtist)!
            .SetAlbumTitle(track.DisplayAlbum)!
            .Build()!;
        // A TIDAL track's uri is inert — TidalMediaSessionPlayer.HandleSetMediaItems
        // reads only MediaId (stripping the "tidal:" prefix to recover the SDK's
        // product id; TIDAL's own Player resolves the actual stream, never Media3's
        // DataSource pipeline) — but MediaItem still needs a well-formed uri, and
        // "drive://tidal:…" would be actively misleading if ever inspected.
        var uri = track.Availability == TrackAvailability.Streaming ? track.Id : $"drive://{track.ResolvedSourceId}";
        var builder = new AndroidX.Media3.Common.MediaItem.Builder()
            .SetMediaId(track.Id)!
            .SetUri(uri)!
            .SetMediaMetadata(metadata)!;

        // A hidden-track split segment plays a clipped range of the file it shares
        // with its sibling segment, rather than a copy of it — the silence between
        // them is never in either range, so it is never decoded or heard either.
        if (track.ClipStart is { } start)
        {
            builder.SetClippingConfiguration(new AndroidX.Media3.Common.MediaItem.ClippingConfiguration.Builder()
                .SetStartPositionMs((long)start.TotalMilliseconds)!
                .SetEndPositionMs((long)(start + track.Duration).TotalMilliseconds)!
                .Build()!);
        }

        return builder.Build()!;
    }

    // Backs the chosen folder id up to the encrypted vault. Best-effort, off the UI thread.
    private async void PublishFolderToVault(string folderId)
    {
        try
        {
            var access = new SecretsVaultAccess(tokens.GetAccessToken, "Score", () => ScoreIdentity.Account);
            await access.Service.PublishAsync(
                v => v with { Music = new MusicSecrets(folderId) }, CancellationToken.None);
        }
        catch
        {
            // Best-effort: the vault picks the folder up on the next successful publish.
        }
    }
}
