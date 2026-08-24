using Android.App;
using Android.Widget;
using Codex.Domain.Audio;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Infrastructure.Podcasts;
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
    private readonly DriveMusicClient driveClient = new(tokens);
    private readonly MusicTagReader tagReader = new(Path.Combine(host.CacheDir!.AbsolutePath, "art"));

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

    public void LoadCatalog()
    {
        using var database = MusicDatabase.Open(host);
        Catalog = new MusicCatalog(database.Catalog.LoadAll());
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

    public async Task ScanAsync()
    {
        var folderId = MusicSettingsStore.MusicFolderId;
        if (folderId is null)
        {
            return;
        }

        var scanner = new MusicScanner(driveClient, tokens, tagReader);
        Status = "Scanning your library…";
        Changed?.Invoke();
        try
        {
            using var database = MusicDatabase.Open(host);
            // The scanner reports once per parallel batch, so surface each callback
            // rather than re-throttling; the shell drops the redraw if music isn't showing.
            await scanner.ScanAsync(folderId, database.Catalog, (done, total) =>
            {
                host.RunOnUiThread(() =>
                {
                    Status = done >= total ? null : $"Scanning… {done} of {total}";
                    Changed?.Invoke();
                });
            }, CancellationToken.None);
            LoadCatalog();
            Status = null;
        }
        catch (Exception e)
        {
            Status = $"Scan couldn't finish: {e.Message}";
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// After a reinstall or the cutover's factory reset, refills the chosen folder id
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

            var access = new SecretsVaultAccess(tokens.GetAccessToken, "Score");
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
        if (playback.Controller is null || tracks.Count == 0)
        {
            return;
        }

        // Speed 1.0 and skip-silence off: both are podcast settings, and a track must
        // not inherit them from whatever episode played last.
        playback.Start(MediaKind.Music, [.. tracks.Select(BuildItem)], index, 0L, 1.0f, skipSilence: false);

        // Start a listening sitting for Genius; replay is judged against the saved progress book.
        var book = new PlaybackProgressBook(progress.Load());
        listening.BeginSelection(tracks, index, launch, book.IsComplete);
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
        return new AndroidX.Media3.Common.MediaItem.Builder()
            .SetMediaId(track.Id)!
            .SetUri($"drive://{track.Id}")!
            .SetMediaMetadata(metadata)!
            .Build()!;
    }

    // Backs the chosen folder id up to the encrypted vault. Best-effort, off the UI thread.
    private async void PublishFolderToVault(string folderId)
    {
        try
        {
            var access = new SecretsVaultAccess(tokens.GetAccessToken, "Score");
            await access.Service.PublishAsync(
                v => v with { Music = new MusicSecrets(folderId) }, CancellationToken.None);
        }
        catch
        {
            // Best-effort: the vault picks the folder up on the next successful publish.
        }
    }
}
