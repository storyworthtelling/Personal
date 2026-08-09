using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using MediaController = AndroidX.Media3.Session.MediaController;
using FocusPhone.Audio.Google;
using FocusPhone.Domain.Audio;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Music;
using FocusPhone.Domain.Time;
using FocusPhone.Infrastructure.Music;
using FocusPhone.Infrastructure.Podcasts;
using FocusPhone.Ui;
using FocusPhone.Ui.Voice;
using Storyworthtelling.Voice.Secrets;

namespace FocusPhone.Audio;

/// <summary>
/// The Audio app shell: a Podcasts | Music toggle over a swappable content pane,
/// with a shared now-playing bar. The podcast side is a per-show home (fully-read
/// shows hidden) that drills into a show's episodes, plus a reorderable queue with
/// three auto-arrange strategies and cross-show auto-continue. Music is a
/// placeholder until the Drive-backed player lands.
/// </summary>
[Activity(
    Label = "Audio",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class AudioActivity : Activity
{
    private PodcastStore store = null!;
    private PlaybackProgressStore progressStore = null!;
    private PlaybackSettingsStore settingsStore = null!;
    private SubscriptionStore subscriptionStore = null!;

    private PlaybackSettingsSnapshot settings = PlaybackSettingsSnapshot.Empty();
    private SubscriptionList subscriptions = new();
    private List<Episode> episodes = [];
    private Dictionary<string, string> feedTitles = [];
    private IReadOnlySet<string> doneIds = new HashSet<string>();
    private readonly PlaybackQueue queue = new();

    private MediaController? controller;
    private Episode? nowPlaying;

    private int side; // 0 = podcasts, 1 = music
    private string? openFeedId;
    private bool showingQueue;
    private bool isRefreshing; // a feed refresh is in flight — the empty shelf says so instead of "nothing new"

    // Music side (Phase 3: sign-in + scan + browse; playback lands in Phase 4).
    private readonly DeviceAccountTokenProvider musicTokens = new();

    // Listening Genius (Phase 6): passive on-device recording -> Drive-backed Taste Profile.
    private ListeningEventStore listeningStore = null!;
    private readonly MusicListeningRecorder listeningRecorder = new();
    private FocusPhone.Audio.Google.MusicTasteService? tasteService;

    // Discovery (Phase 7): prompted playlists + the quiet weekly shelf.
    private FridayPicksStore fridayPicksStore = null!;
    private string? discoveryStatus;

    // Owner's-voice greeting, reusing the music Google account to read the shared voice profile from Drive.
    private FocusPhone.Ui.Voice.VoiceGreeting? greeting;
    private TextView? greetingView;
    private DriveMusicClient? driveClient;
    private MusicTagReader? tagReader;
    private MusicCatalog? musicCatalog;
    private IReadOnlyList<DriveMusicClient.DriveFolder>? driveFolders;
    private string? musicStatus;
    private MusicView musicView = MusicView.Menu;

    private FrameLayout content = null!;
    private NowPlayingBar nowPlayingBar = null!;
    private Handler? uiHandler;
    private Java.Lang.Runnable? ticker;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        store = new PodcastStore(this);
        progressStore = new PlaybackProgressStore(this);
        settingsStore = new PlaybackSettingsStore(this);
        subscriptionStore = new SubscriptionStore(this);
        settings = settingsStore.Load();
        subscriptions = new SubscriptionList(subscriptionStore.Load());
        MigrateLegacyFeedsIfNeeded();

        driveClient = new DriveMusicClient(musicTokens);
        tagReader = new MusicTagReader(Path.Combine(CacheDir!.AbsolutePath, "art"));
        LoadMusicCatalog();

        listeningStore = new ListeningEventStore(this);
        listeningRecorder.LearningEnabled = MusicSettingsStore.ListeningGeniusEnabled;
        tasteService = new FocusPhone.Audio.Google.MusicTasteService(musicTokens.GetAccessToken, "FocusPhone.Audio");
        fridayPicksStore = new FridayPicksStore(this);
        FridayPicksWorker.Schedule(this); // weekly "New for you"; no-ops until signed in

        greeting = new FocusPhone.Ui.Voice.VoiceGreeting(musicTokens.GetAccessToken, "FocusPhone.Audio");
        SetContentView(BuildLayout());
        ConnectController();
        ShowGreetingAsync();
        RestoreMusicFolderFromVault();
        CrashReporter.ShowPendingCrash(this);
        RenderContent();
    }

    // After a reinstall or the cutover's factory reset, refill the chosen music-folder id from the encrypted
    // Drive vault (needs the vault passphrase set + Google signed in), so the library reappears without
    // re-picking the folder. Runs only when none is set locally. Best-effort and off the UI thread.
    private void RestoreMusicFolderFromVault() => Task.Run(async () =>
    {
        try
        {
            if (!string.IsNullOrEmpty(MusicSettingsStore.MusicFolderId))
                return;

            var access = new SecretsVaultAccess(musicTokens.GetAccessToken, "FocusPhone.Audio");
            var result = await access.Service.LoadAsync(System.Threading.CancellationToken.None);
            if (result.Status == SecretsVaultStatus.Loaded
                && !string.IsNullOrEmpty(result.Vault.Music.FolderId)
                && string.IsNullOrEmpty(MusicSettingsStore.MusicFolderId))
            {
                MusicSettingsStore.MusicFolderId = result.Vault.Music.FolderId;
                RunOnUiThread(() => { RenderContent(); _ = ScanAsync(); });
            }
        }
        catch
        {
            // Ignore: restore is a convenience; the folder picker is always available.
        }
    });

    // Backs the chosen music-folder id up to the encrypted vault. Best-effort and off the UI thread.
    private async void PublishMusicFolderToVault(string folderId)
    {
        try
        {
            var access = new SecretsVaultAccess(musicTokens.GetAccessToken, "FocusPhone.Audio");
            await access.Service.PublishAsync(
                v => v with { Music = new MusicSecrets(folderId) },
                System.Threading.CancellationToken.None);
        }
        catch
        {
            // Best-effort: the vault picks the folder up on the next successful publish.
        }
    }

    private void LoadMusicCatalog()
    {
        using var database = MusicDatabase.Open(this);
        musicCatalog = new MusicCatalog(database.Catalog.LoadAll());
    }

    // The old store persisted a bare feed-URL set; seed the ordered subscription
    // list from it the first time so no shows are lost on upgrade.
    private void MigrateLegacyFeedsIfNeeded()
    {
        if (subscriptions.Ordered.Count > 0)
        {
            return;
        }

        foreach (var url in store.FeedUrls)
        {
            subscriptions.Add(url, url, DateTimeOffset.Now);
        }

        subscriptionStore.Save(subscriptions.Export());
    }

    // Composes a greeting in the owner's voice (from the shared Drive profile + key) and shows it under the
    // header. Best-effort: it stays hidden when there's no signed-in account, profile, key, or on any error.
    private async void ShowGreetingAsync()
    {
        if (greeting is null || greetingView is null)
            return;
        var message = await greeting.ComposeAsync(
            "welcome", "the owner is opening their music and podcasts", CancellationToken.None);
        if (string.IsNullOrWhiteSpace(message))
            return;
        greetingView.Text = message;
        greetingView.Visibility = ViewStates.Visible;
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var header = PaperUi.PageHeader(this, "Audio");
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        root.AddView(header);

        // A greeting in the owner's voice, hidden until composed (and only when a shared profile exists).
        greetingView = PaperUi.Secondary(this, string.Empty);
        greetingView.SetPadding(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X4));
        greetingView.Visibility = ViewStates.Gone;
        root.AddView(greetingView);

        var toggle = PaperUi.SegmentedToggle(this, "Podcasts", "Music", index =>
        {
            side = index;
            RenderContent();
        });
        toggle.SetPadding(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X4));
        root.AddView(toggle);

        content = new FrameLayout(this);
        content.SetPadding(edge, 0, edge, 0);
        root.AddView(content, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        nowPlayingBar = new NowPlayingBar(this, SkipBack, TogglePlayPause, SkipForward,
            onOpen: () => StartActivity(new Intent(this, typeof(NowPlayingActivity))));
        root.AddView(nowPlayingBar);

        root.AddView(PaperUi.BottomBar(this,
            PaperUi.SecondaryButton(this, "Shows", () => { side = 0; openFeedId = null; showingQueue = false; RenderContent(); }),
            PaperUi.QuietButton(this, "Queue", () => { side = 0; showingQueue = true; RenderContent(); }),
            PaperUi.QuietButton(this, "Manage", () => StartActivity(new Intent(this, typeof(ManageSubscriptionsActivity)))),
            PaperUi.QuietButton(this, "Speed", ShowSpeedDialog)));
        return root;
    }

    protected override void OnResume()
    {
        base.OnResume();
        subscriptions = new SubscriptionList(subscriptionStore.Load());
        settings = settingsStore.Load();
        StartTicking();
        _ = RefreshFeedsAsync();
    }

    protected override void OnPause()
    {
        base.OnPause();
        StopTicking();

        // Finalize whatever was mid-play, then fold the day's listening into the Taste Profile (throttled).
        RecordListening(listeningRecorder.Stop());
        MaybeRefreshTaste();
    }

    // ---- Listening Genius ----

    private void RecordListening(IReadOnlyList<ListeningEvent> events)
    {
        if (events.Count > 0)
        {
            listeningStore.Append(events);
        }
    }

    // Refreshes the Taste Profile at most once a day, off the UI thread; best-effort and never fatal.
    private void MaybeRefreshTaste()
    {
        if (!MusicSettingsStore.ListeningGeniusEnabled || tasteService is null)
        {
            return;
        }

        var last = MusicSettingsStore.TasteLastRefreshUtc;
        if (last is { } when && DateTimeOffset.UtcNow - when < TimeSpan.FromHours(20))
        {
            return;
        }

        var events = listeningStore.Load();
        if (events.Count == 0)
        {
            return;
        }

        MusicSettingsStore.TasteLastRefreshUtc = DateTimeOffset.UtcNow;
        _ = tasteService.RefreshAsync(events, CancellationToken.None);
    }

    protected override void OnDestroy()
    {
        controller?.Release();
        base.OnDestroy();
    }

    public override void OnBackPressed()
    {
        if (side == 0 && showingQueue)
        {
            showingQueue = false;
            RenderContent();
        }
        else if (side == 0 && openFeedId is not null)
        {
            openFeedId = null;
            RenderContent();
        }
        else if (side == 1 && musicView != MusicView.Menu)
        {
            musicView = MusicView.Menu;
            RenderContent();
        }
        else
        {
            base.OnBackPressed();
        }
    }

    // ---- Content rendering ----

    private void RenderContent()
    {
        content.RemoveAllViews();
        var pane = side == 1 ? BuildMusicPane() : BuildPodcastsPane();
        content.AddView(pane, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    private View BuildMusicPane()
    {
        if (string.IsNullOrEmpty(MusicSettingsStore.GoogleAccountEmail))
        {
            return BuildDriveSetup();
        }

        return string.IsNullOrEmpty(MusicSettingsStore.MusicFolderId)
            ? BuildFolderPicker()
            : BuildMusicBrowse();
    }

    private View BuildDriveSetup()
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.AddView(PaperUi.Body(this, "Stream your own music from Google Drive."));
        layout.AddView(PaperUi.Secondary(this,
            "First register this app as an Android OAuth client (Drive API enabled), then sign in:\n\n" +
            $"Package: {AppSigningInfo.PackageName}\nSHA-1: {AppSigningInfo.Sha1Fingerprint()}"));

        // The secrets-vault passphrase (optional): set the same one as your other apps to back up and restore
        // your keys from Drive, encrypted. Persisted when you sign in.
        var passphrase = PaperUi.MonoField(this, "Secrets vault passphrase (optional)");
        passphrase.Text = VaultPassphraseStore.Passphrase;
        passphrase.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        passphrase.Typeface = PaperTheme.Mono(this);
        layout.AddView(passphrase);

        layout.AddView(PaperUi.PrimaryButton(this, "Sign in with Google", () =>
        {
            VaultPassphraseStore.Passphrase = passphrase.Text ?? string.Empty;
            _ = SignInAsync();
        }));
        return WrapScroll(layout);
    }

    private View BuildFolderPicker()
    {
        if (driveFolders is null)
        {
            _ = LoadFoldersAsync();
            return PaperUi.EmptyState(this, "Finding your Drive folders…");
        }

        var container = new LinearLayout(this) { Orientation = Orientation.Vertical };
        container.AddView(PaperUi.Body(this, "Pick the folder your music lives in."));
        container.AddView(PaperUi.SecondaryButton(this, "Use all of My Drive", () => ChooseFolder("root")));

        var rows = driveFolders.Select(folder => new PaperListRow(folder.Name)).ToList();
        var list = BuildList(rows, position => ChooseFolder(driveFolders![position].Id));
        container.AddView(list, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return container;
    }

    private void ChooseFolder(string folderId)
    {
        MusicSettingsStore.MusicFolderId = folderId;
        PublishMusicFolderToVault(folderId);
        driveFolders = null;
        RenderContent();
        _ = ScanAsync();
    }

    private View BuildMusicBrowse()
    {
        var container = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var head = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        head.SetGravity(GravityFlags.CenterVertical);
        head.AddView(PaperUi.Heading(this, musicView.ToString()),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        head.AddView(PaperUi.QuietButton(this, "Shuffle", () => PlayShuffled((musicCatalog ?? new MusicCatalog([])).Songs())));
        head.AddView(PaperUi.QuietButton(this, "Sound", () => StartActivity(new Intent(this, typeof(EqualizerActivity)))));
        head.AddView(PaperUi.QuietButton(this, "Rescan", () => _ = ScanAsync()));
        container.AddView(head);

        if (!string.IsNullOrEmpty(musicStatus))
        {
            container.AddView(PaperUi.Secondary(this, musicStatus));
        }

        // An empty catalog (and no scan running) is the cold-start dead-end: show the
        // "how to add music" guide instead of a bare "no music" line under every browse view.
        var catalogEmpty = (musicCatalog ?? new MusicCatalog([])).Songs().Count == 0;
        var body = catalogEmpty && string.IsNullOrEmpty(musicStatus)
            ? BuildMusicEmptyGuide()
            : BuildMusicBody();
        container.AddView(body, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return container;
    }

    // The cold-start guide: how to get music into the Drive folder. Tone stays quiet-neutral
    // (DESIGN.md §11) and the supported extensions come straight from AudioFileTypes so the
    // guide can't drift from what the scanner actually accepts.
    private View BuildMusicEmptyGuide()
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.AddView(PaperUi.Body(this, "No music in this folder yet. Here's how to add some."));
        layout.AddView(PaperUi.Secondary(this,
            "Put audio files in your Drive music folder — rip your CDs to tagged FLAC or MP3, copy the " +
            "files iTunes already made, or buy real downloads (Bandcamp, Qobuz, Amazon Music). Streaming " +
            "apps like YouTube Music can't be saved here."));
        layout.AddView(PaperUi.Secondary(this,
            "What organizes the library is each file's embedded tags — title, artist, album artist, album, " +
            "track number, genre. Folder names and structure don't matter; the tags do."));
        layout.AddView(PaperUi.Secondary(this,
            "Playable file types: " + string.Join(", ", AudioFileTypes.SupportedExtensions) + "."));
        layout.AddView(PaperUi.PrimaryButton(this, "Scan again", () => _ = ScanAsync()));
        return WrapScroll(layout);
    }

    private View BuildMusicBody()
    {
        var catalog = musicCatalog ?? new MusicCatalog([]);
        switch (musicView)
        {
            case MusicView.Artists:
                var artists = catalog.Artists();
                return artists.Count == 0
                    ? PaperUi.EmptyState(this, "No music yet — scan your library.")
                    : BuildList([.. artists.Select(artist => new PaperListRow(artist.Name, $"{artist.Tracks.Count} tracks"))],
                        position => PlayTracks(catalog.TracksByArtist(artists[position].Name), 0),
                        position => PlayShuffled(catalog.TracksByArtist(artists[position].Name)));

            case MusicView.Albums:
                var albums = catalog.Albums();
                return albums.Count == 0
                    ? PaperUi.EmptyState(this, "No music yet — scan your library.")
                    : BuildList([.. albums.Select(album => new PaperListRow(album.Name, album.AlbumArtist))],
                        position => PlayTracks(catalog.TracksInAlbum(albums[position].AlbumArtist, albums[position].Name), 0),
                        position => PlayShuffled(catalog.TracksInAlbum(albums[position].AlbumArtist, albums[position].Name)));

            case MusicView.Songs:
                var songs = catalog.Songs();
                return songs.Count == 0
                    ? PaperUi.EmptyState(this, "No music yet — scan your library.")
                    : BuildList([.. songs.Select(track => new PaperListRow(track.DisplayTitle, track.DisplayArtist))],
                        position => PlayTracks(songs, position),
                        position => PlaySmartMix(songs[position], catalog));

            case MusicView.Genres:
                var genres = catalog.Genres();
                return genres.Count == 0
                    ? PaperUi.EmptyState(this, "No music yet — scan your library.")
                    : BuildList([.. genres.Select(genre => new PaperListRow(genre))],
                        position => PlayTracks(catalog.TracksInGenre(genres[position]), 0),
                        position => PlayShuffled(catalog.TracksInGenre(genres[position])));

            case MusicView.Playlists:
                return PaperUi.EmptyState(this, "No playlists yet.");

            case MusicView.Discovery:
                return BuildDiscovery(catalog);

            default:
                var items = new[] { "Discovery", "Playlists", "Artists", "Albums", "Songs", "Genres" };
                return BuildList([.. items.Select(item => new PaperListRow(item))], position =>
                {
                    musicView = Enum.Parse<MusicView>(items[position]);
                    RenderContent();
                });
        }
    }

    // Discovery: a prompted playlist from the owner's own library, a quiet weekly "New for you" shelf, and
    // the Listening Genius control + honest data note. Playlists generate only when asked (DESIGN.md: no
    // surprise work); loading is a sentence, and the shelf is dismissable — never a toast, push, or badge.
    private View BuildDiscovery(MusicCatalog catalog)
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var picks = ResolveFridayPicks(catalog);
        if (picks is not null && !fridayPicksStore.Dismissed)
        {
            layout.AddView(PaperUi.Heading(this, "New for you"));
            layout.AddView(PaperUi.Secondary(this, picks.Title));
            layout.AddView(PaperUi.SecondaryButton(this, "Play these", () => PlayTracks(picks.Tracks, 0)));
            layout.AddView(PaperUi.QuietButton(this, "Dismiss", () =>
            {
                fridayPicksStore.Dismissed = true;
                RenderContent();
            }));
        }

        layout.AddView(PaperUi.Body(this, "Make a playlist from your library."));
        var input = PaperUi.Field(this, "What do you want to hear? A mood, a genre, an idea.");
        layout.AddView(input);
        layout.AddView(PaperUi.PrimaryButton(this, "Make a playlist", () => _ = MakePlaylistAsync(input.Text, catalog)));

        if (!string.IsNullOrEmpty(discoveryStatus))
        {
            layout.AddView(PaperUi.Secondary(this, discoveryStatus));
        }

        layout.AddView(PaperUi.SecondaryButton(this, GeniusToggleLabel(), ToggleListeningGenius));
        layout.AddView(PaperUi.Secondary(this,
            "Listening Genius learns your taste on this phone from what you play — nothing you rate — and " +
            "sends a short summary to Claude with your key to describe it. Turn it off any time; that also " +
            "clears what's been gathered."));
        return WrapScroll(layout);
    }

    private DiscoveryPlaylist? ResolveFridayPicks(MusicCatalog catalog)
    {
        var selection = fridayPicksStore.LoadSelection();
        if (selection.TrackIds.Count == 0)
        {
            return null;
        }

        var playlist = PlaylistPlan.Resolve(selection.Title, selection.TrackIds, catalog.Songs(), 25);
        return playlist.IsEmpty ? null : playlist;
    }

    private async Task MakePlaylistAsync(string? seed, MusicCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(seed) || tasteService is null)
        {
            return;
        }

        discoveryStatus = "Building a playlist…";
        RenderContent();

        var playlist = await tasteService.MakePlaylistAsync(
            catalog.Songs(), new PlaylistRequest(seed.Trim()), CancellationToken.None);
        if (playlist.IsEmpty)
        {
            discoveryStatus = "Couldn't build a playlist from that. Try another idea.";
            RenderContent();
            return;
        }

        discoveryStatus = null;
        PlayTracks(playlist.Tracks, 0);
        RenderContent();
    }

    private string GeniusToggleLabel() =>
        MusicSettingsStore.ListeningGeniusEnabled ? "Listening Genius: on" : "Listening Genius: off";

    private void ToggleListeningGenius()
    {
        var enabled = !MusicSettingsStore.ListeningGeniusEnabled;
        MusicSettingsStore.ListeningGeniusEnabled = enabled;
        listeningRecorder.LearningEnabled = enabled;
        if (!enabled)
        {
            listeningStore.Clear(); // off means forget what was gathered, not just stop
        }

        RenderContent();
    }

    private void PlayTracks(IReadOnlyList<Track> tracks, int index, PlaybackLaunch launch = PlaybackLaunch.Album)
    {
        if (controller is null || tracks.Count == 0)
        {
            return;
        }

        nowPlaying = null; // music, not a podcast episode
        var items = tracks.Select(BuildTrackItem).ToList();
        controller.SetMediaItems(items, index, 0L);
        controller.SetPlaybackSpeed(1.0f);
        controller.Prepare();
        controller.Play();
        SendSkipSilence(false); // skip-silence is a podcast setting; off for music

        // Start a listening sitting for Genius; replay is judged against the saved progress book.
        var progress = new PlaybackProgressBook(progressStore.Load());
        RecordListening(listeningRecorder.BeginSelection(tracks, index, launch, progress.IsComplete));
    }

    private void PlayShuffled(IReadOnlyList<Track> tracks) =>
        PlayTracks(new ShufflePlanner().Shuffle(tracks), 0, PlaybackLaunch.Shuffle);

    private void PlaySmartMix(Track seed, MusicCatalog catalog) =>
        PlayTracks(new SmartMixBuilder(new ShufflePlanner()).Build(seed, catalog), 0, PlaybackLaunch.Shuffle);

    private static MediaItem BuildTrackItem(Track track)
    {
        var metadata = new MediaMetadata.Builder()
            .SetTitle(track.DisplayTitle)!
            .SetArtist(track.DisplayArtist)!
            .SetAlbumTitle(track.DisplayAlbum)!
            .Build()!;
        return new MediaItem.Builder()
            .SetMediaId(track.Id)!
            .SetUri($"drive://{track.Id}")!
            .SetMediaMetadata(metadata)!
            .Build()!;
    }

    private View WrapScroll(View inner)
    {
        var scroll = new ScrollView(this);
        scroll.AddView(inner);
        return scroll;
    }

    private async Task SignInAsync()
    {
        try
        {
            await DeviceAccountSignIn.SignIn(this);
            RenderContent();
        }
        catch (Exception e)
        {
            Toast.MakeText(this, e.Message, ToastLength.Long)!.Show();
        }
    }

    private async Task LoadFoldersAsync()
    {
        try
        {
            driveFolders = await driveClient!.ListFoldersAsync("root", CancellationToken.None);
            RenderContent();
        }
        catch (Exception e)
        {
            Toast.MakeText(this, e.Message, ToastLength.Long)!.Show();
        }
    }

    private async Task ScanAsync()
    {
        var folderId = MusicSettingsStore.MusicFolderId;
        if (folderId is null || driveClient is null || tagReader is null)
        {
            return;
        }

        var scanner = new MusicScanner(driveClient, musicTokens, tagReader);
        musicStatus = "Scanning your library…";
        RenderContent();
        try
        {
            using var database = MusicDatabase.Open(this);
            await scanner.ScanAsync(folderId, database.Catalog, (done, total) =>
            {
                if (done == total || done % 25 == 0)
                {
                    RunOnUiThread(() =>
                    {
                        musicStatus = done >= total ? null : $"Scanning… {done} of {total}";
                        if (side == 1)
                        {
                            RenderContent();
                        }
                    });
                }
            }, CancellationToken.None);
            LoadMusicCatalog();
            musicStatus = null;
        }
        catch (Exception e)
        {
            musicStatus = $"Scan couldn't finish: {e.Message}";
        }

        RenderContent();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == DeviceAccountSignIn.RequestCode)
        {
            DeviceAccountSignIn.HandleChooserResult(resultCode, data);
        }
    }

    private View BuildPodcastsPane()
    {
        if (showingQueue)
        {
            return BuildQueuePane();
        }

        return openFeedId is not null ? BuildShowEpisodes(openFeedId) : BuildShelf();
    }

    private View BuildShelf()
    {
        var shelf = PodcastShelf.Build(episodes, feedTitles, doneIds);
        if (shelf.Count == 0)
        {
            return PaperUi.EmptyState(this, EmptyShelfMessage());
        }

        var rows = shelf
            .Select(show => new PaperListRow(show.Title, $"{show.UnplayedCount} unplayed"))
            .ToList();
        return BuildList(rows, position =>
        {
            openFeedId = shelf[position].FeedId;
            RenderContent();
        });
    }

    // The empty shelf must never read like a dead-end: no shows points at Manage, a
    // refresh in flight says so (loading is a sentence, DESIGN.md §6), otherwise caught up.
    private string EmptyShelfMessage()
    {
        if (subscriptions.Ordered.Count == 0)
        {
            return "No shows yet. Tap Manage to add a podcast or import an OPML file.";
        }

        return isRefreshing ? "Fetching episodes…" : "Nothing new to hear.";
    }

    private View BuildShowEpisodes(string feedId)
    {
        var showEpisodes = episodes
            .Where(episode => episode.FeedId == feedId && !doneIds.Contains(episode.Id))
            .OrderByDescending(episode => episode.PublishedUtc)
            .ToList();
        if (showEpisodes.Count == 0)
        {
            return PaperUi.EmptyState(this, "All caught up.");
        }

        var progress = new PlaybackProgressBook(progressStore.Load());
        var rows = showEpisodes
            .Select(episode => new PaperListRow(episode.Title, EpisodeSubtitle(episode, progress)))
            .ToList();
        return BuildList(rows,
            position => EnqueueAndPlay(showEpisodes[position]),
            position => MarkDoneWithUndo(showEpisodes[position]));
    }

    private View BuildQueuePane()
    {
        // Prune finished/unknown episodes so the display order matches queue indices.
        queue.ReplaceWith(queue.Items.Where(id =>
            !doneIds.Contains(id) && episodes.Any(episode => episode.Id == id)));

        var container = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var head = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        head.SetGravity(GravityFlags.CenterVertical);
        head.AddView(PaperUi.Heading(this, "Queue"),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        head.AddView(PaperUi.SecondaryButton(this, "Auto-arrange", ShowArrangeDialog));
        container.AddView(head);

        var queued = queue.Items
            .Select(id => episodes.First(episode => episode.Id == id))
            .ToList();
        if (queued.Count == 0)
        {
            container.AddView(PaperUi.EmptyState(this, "Queue is empty."));
            return container;
        }

        var scroll = new ScrollView(this);
        var list = new LinearLayout(this) { Orientation = Orientation.Vertical };
        for (var i = 0; i < queued.Count; i++)
        {
            list.AddView(BuildQueueRow(queued[i], i, queued.Count));
        }

        scroll.AddView(list);
        container.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return container;
    }

    private View BuildQueueRow(Episode episode, int index, int total)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        var vertical = PaperTheme.Dp(this, DesignTokens.Space.X3);
        row.SetPadding(0, vertical, 0, vertical);

        var title = PaperUi.Body(this, episode.Title);
        title.Clickable = true;
        title.Click += (_, _) => PlayQueue(episode.Id);
        row.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        if (index > 0)
        {
            row.AddView(PaperUi.QuietButton(this, "↑", () => { queue.Move(index, index - 1); RenderContent(); }));
        }

        if (index < total - 1)
        {
            row.AddView(PaperUi.QuietButton(this, "↓", () => { queue.Move(index, index + 1); RenderContent(); }));
        }

        return row;
    }

    private ListView BuildList(IReadOnlyList<PaperListRow> rows, Action<int> onClick, Action<int>? onLongClick = null)
    {
        var list = new ListView(this);
        PaperUi.StylePaperList(this, list);
        list.Adapter = new PaperListAdapter(this, rows);
        list.ItemClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < rows.Count)
            {
                onClick(e.Position);
            }
        };
        if (onLongClick is not null)
        {
            list.ItemLongClick += (_, e) =>
            {
                if (e.Position >= 0 && e.Position < rows.Count)
                {
                    onLongClick(e.Position);
                }

                e.Handled = true;
            };
        }

        return list;
    }

    private string EpisodeSubtitle(Episode episode, PlaybackProgressBook progress)
    {
        var when = RelativeTime.Format(episode.PublishedUtc.LocalDateTime, DateTime.Now);
        if (episode.Duration is not { } duration)
        {
            return when;
        }

        var position = progress.Resume(episode.Id) ?? TimeSpan.Zero;
        var remaining = duration - position;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        return when + " · " + PlaybackFormatting.TimeLeftAtSpeed(remaining, settings.For(episode.FeedId).Speed);
    }

    // ---- Playback ----

    private void EnqueueAndPlay(Episode episode)
    {
        queue.Add(episode.Id);
        PlayQueue(episode.Id);
        RenderContent();
    }

    private void PlayQueue(string startId)
    {
        if (controller is null)
        {
            return;
        }

        // Keep only playable, still-present episodes so start index and items align.
        queue.ReplaceWith(queue.Items.Where(id =>
            episodes.Any(episode => episode.Id == id && episode.AudioUrl is not null)));

        var ids = queue.Items;
        var index = ids.ToList().IndexOf(startId);
        if (index < 0)
        {
            return;
        }

        var items = ids.Select(id => BuildItem(episodes.First(episode => episode.Id == id))).ToList();
        var startEpisode = episodes.First(episode => episode.Id == startId);
        nowPlaying = startEpisode;
        var showSettings = settings.For(startEpisode.FeedId);
        var resume = new PlaybackProgressBook(progressStore.Load())
            .ResumeWithRewind(startEpisode.Id, DateTimeOffset.Now);
        var startMs = resume is { } point ? (long)point.TotalMilliseconds : 0L;

        controller.SetMediaItems(items, index, startMs);
        controller.SetPlaybackSpeed(showSettings.Speed);
        controller.Prepare();
        controller.Play();
        SendSkipSilence(showSettings.SkipSilence);
    }

    private static MediaItem BuildItem(Episode episode)
    {
        var metadata = new MediaMetadata.Builder().SetTitle(episode.Title)!.Build()!;
        return new MediaItem.Builder()
            .SetMediaId(episode.Id)!
            .SetUri(episode.AudioUrl)!
            .SetMediaMetadata(metadata)!
            .Build()!;
    }

    private void SendSkipSilence(bool enabled)
    {
        var intent = new Intent(this, typeof(PlaybackService));
        intent.SetAction(AudioPlaybackCommands.ActionSetSkipSilence);
        intent.PutExtra(AudioPlaybackCommands.ExtraEnabled, enabled);
        StartService(intent);
    }

    private void SkipBack() => controller?.SeekTo(Math.Max(0, (controller?.CurrentPosition ?? 0) - 30_000));

    private void SkipForward() => controller?.SeekTo((controller?.CurrentPosition ?? 0) + 30_000);

    private void TogglePlayPause()
    {
        if (controller is null)
        {
            return;
        }

        if (controller.IsPlaying)
        {
            controller.Pause();
        }
        else
        {
            controller.Play();
        }
    }

    // ---- Queue arrangement ----

    private void ShowArrangeDialog()
    {
        var options = new[]
        {
            "Round-robin (mix shows)",
            "Chronological (oldest first)",
            "Backlog-buster (finish shows)",
        };
        new AlertDialog.Builder(this)
            .SetTitle("Auto-arrange queue")!
            .SetItems(options, (_, args) =>
            {
                var arrangement = args.Which switch
                {
                    0 => QueueArrangement.RoundRobin,
                    1 => QueueArrangement.Chronological,
                    _ => QueueArrangement.BacklogBuster,
                };
                AutoArrange(arrangement);
            })!
            .Show();
    }

    private void AutoArrange(QueueArrangement arrangement)
    {
        var strategy = EpisodeQueueStrategy.For(arrangement);
        if (strategy is not null)
        {
            var unplayed = episodes.Where(episode => !doneIds.Contains(episode.Id)).ToList();
            queue.ReplaceWith(strategy.Arrange(unplayed).Select(episode => episode.Id));
        }

        RenderContent();
    }

    // ---- Done state ----

    private void MarkDoneWithUndo(Episode episode)
    {
        SetListened(episode.Id, listened: true);
        UndoBar.Show(this, content, "Marked played.", onUndo: () => SetListened(episode.Id, listened: false));
    }

    private void SetListened(string episodeId, bool listened)
    {
        var ids = store.ListenedIds.ToHashSet();
        if (listened)
        {
            ids.Add(episodeId);
        }
        else
        {
            ids.Remove(episodeId);
        }

        store.SaveListenedIds(ids);
        RecomputeDone();
        RenderContent();
    }

    private void RecomputeDone() =>
        doneIds = PodcastShelf.DoneEpisodes(episodes, store.ListenedIds, new PlaybackProgressBook(progressStore.Load()));

    // ---- Feeds ----

    private async Task RefreshFeedsAsync()
    {
        isRefreshing = true;
        if (side == 0 && !showingQueue && openFeedId is null)
        {
            RenderContent(); // surface "Fetching episodes…" while the first pull is in flight
        }

        var all = new List<Episode>();
        var titles = new Dictionary<string, string>();
        try
        {
            using var http = new HttpClient();
            var client = new RssFeedClient(http);
            foreach (var subscription in subscriptions.Ordered)
            {
                try
                {
                    var feed = await client.FetchAsync(new Uri(subscription.FeedUrl));
                    all.AddRange(feed.Episodes);
                    titles[subscription.FeedUrl] = feed.Title;
                    subscriptions.UpdateTitle(subscription.FeedUrl, feed.Title);
                }
                catch (Exception e) when (
                    e is HttpRequestException or InvalidDataException or System.OperationCanceledException or UriFormatException)
                {
                    // One bad feed must never sink the whole refresh (or the app): a
                    // moved feed serving HTML (InvalidDataException), a network drop
                    // (HttpRequestException), a timeout (OperationCanceledException),
                    // or a corrupt stored URL (UriFormatException) all fall through
                    // to the same spoken failure. Failure may speak (DESIGN.md §7).
                    var host = Uri.TryCreate(subscription.FeedUrl, UriKind.Absolute, out var uri)
                        ? uri.Host
                        : subscription.FeedUrl;
                    Toast.MakeText(this, $"{host} didn't answer. It retries on the next refresh.",
                        ToastLength.Short)!.Show();
                }
            }

            subscriptionStore.Save(subscriptions.Export());
            episodes = all;
            feedTitles = titles;
        }
        finally
        {
            // Always leave the refreshing state — otherwise an unexpected failure
            // wedges the shelf on "Fetching episodes…" until the app is restarted.
            isRefreshing = false;
        }

        RecomputeDone();
        RenderContent();
    }

    // ---- Now-playing bar poll ----

    private void StartTicking()
    {
        uiHandler ??= new Handler(Looper.MainLooper!);
        ticker ??= new Java.Lang.Runnable(Tick);
        uiHandler.PostDelayed(ticker, 1000);
    }

    private void StopTicking()
    {
        if (uiHandler is not null && ticker is not null)
        {
            uiHandler.RemoveCallbacks(ticker);
        }
    }

    private void Tick()
    {
        if (controller is not null)
        {
            nowPlayingBar.Update(controller.MediaMetadata?.Title?.ToString(), controller.IsPlaying);
            ObserveListening();
        }

        uiHandler?.PostDelayed(ticker!, 1000);
    }

    // Sample the music player for Genius. Only music counts (a podcast sets nowPlaying); the recorder turns
    // id changes into completed/skipped signals itself.
    private void ObserveListening()
    {
        if (controller is null || nowPlaying is not null || controller.CurrentMediaItem is null)
        {
            return;
        }

        var mediaId = controller.CurrentMediaItem.MediaId;
        var position = TimeSpan.FromMilliseconds(Math.Max(0, controller.CurrentPosition));
        var durationMs = controller.Duration;
        var duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : TimeSpan.Zero;
        RecordListening(listeningRecorder.Observe(mediaId, position, duration));
    }

    private void ConnectController()
    {
        var token = new SessionToken(this, new ComponentName(this, Java.Lang.Class.FromType(typeof(PlaybackService))));
        var future = new MediaController.Builder(this, token).BuildAsync();
        future.AddListener(new Java.Lang.Runnable(() =>
        {
            controller = (MediaController)future.Get()!;
        }), AndroidX.Core.Content.ContextCompat.GetMainExecutor(this));
    }

    // ---- Speed dialog (per-show speed + skip-silence) ----

    private void ShowSpeedDialog()
    {
        var feedId = nowPlaying?.FeedId;
        var current = feedId is null ? settings.Default : settings.For(feedId);

        var edge = PaperTheme.Dp(this, DesignTokens.Space.X4);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(edge, edge, edge, edge);

        var label = PaperUi.Title(this, SpeedLabel(current.Speed));
        layout.AddView(label);

        Button? trim = null;

        void Apply(PlaybackSettings updated)
        {
            current = updated;
            label.Text = SpeedLabel(current.Speed);
            if (trim is not null)
            {
                trim.Text = TrimLabel(current.SkipSilence);
            }

            PersistAndApply(feedId, current);
        }

        var presetRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        presetRow.AddView(PaperUi.SecondaryButton(this, "−", () => Apply(current.Slower())));
        foreach (var preset in PlaybackSettings.Presets)
        {
            var value = preset;
            presetRow.AddView(PaperUi.QuietButton(this, $"{value:0.0}×", () => Apply(current.WithSpeed(value))));
        }
        presetRow.AddView(PaperUi.SecondaryButton(this, "+", () => Apply(current.Faster())));
        layout.AddView(presetRow);

        trim = PaperUi.SecondaryButton(this, TrimLabel(current.SkipSilence),
            () => Apply(current with { SkipSilence = !current.SkipSilence }));
        layout.AddView(trim);

        new AlertDialog.Builder(this)
            .SetTitle("Playback speed")!
            .SetView(layout)!
            .SetPositiveButton("Done", (_, _) => { })!
            .Show();
    }

    private void PersistAndApply(string? feedId, PlaybackSettings updated)
    {
        settings = feedId is null
            ? settings with { Default = updated }
            : settings.With(feedId, updated);
        settingsStore.Save(settings);
        controller?.SetPlaybackSpeed(updated.Speed);
        SendSkipSilence(updated.SkipSilence);
    }

    private static string SpeedLabel(float speed) => $"Speed {speed:0.0}×";

    private static string TrimLabel(bool on) => on ? "Trim silence: on" : "Trim silence: off";
}

/// <summary>The music side's browse levels — the iPod top menu and its lists.</summary>
internal enum MusicView
{
    Menu,
    Playlists,
    Artists,
    Albums,
    Songs,
    Genres,
    Discovery,
}
