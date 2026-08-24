using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Score.Google;
using Codex.Domain.Audio;
using Codex.Domain.Design;
using Codex.Domain.Time;
using Codex.Infrastructure.Podcasts;
using Codex.Ui;
using Codex.Ui.Voice;

namespace Score;

/// <summary>
/// Score's app shell: a Podcasts | Music toggle over a swappable content pane,
/// with a shared now-playing bar. The podcast side is a per-show home (fully-read
/// shows hidden) that drills into a show's episodes, plus a reorderable queue with
/// three auto-arrange strategies and cross-show auto-continue. Music is a
/// placeholder until the Drive-backed player lands.
/// </summary>
[Activity(
    Label = "Score",
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
    private EpisodeCacheStore episodeCache = null!;
    private QueueStore queueStore = null!;
    private PodcastLibrarySync librarySync = null!;

    private PlaybackSettingsSnapshot settings = PlaybackSettingsSnapshot.Empty();
    private SubscriptionList subscriptions = new();
    private List<Episode> episodes = [];
    private Dictionary<string, string> feedTitles = [];
    private IReadOnlySet<string> doneIds = new HashSet<string>();
    private readonly PlaybackQueue queue = new();

    private ScorePlayback playback = null!;
    private Episode? nowPlaying;

    private int side; // 0 = podcasts, 1 = music
    private string? openFeedId;
    private bool showingQueue;
    private bool isRefreshing; // a feed refresh is in flight — the empty shelf says so instead of "nothing new"

    // Music side (Phase 3: sign-in + scan + browse; playback lands in Phase 4).
    private readonly DeviceAccountTokenProvider musicTokens = new();

    // Listening Genius (Phase 6): passive on-device recording -> Drive-backed Taste Profile.
    private ListeningLog listening = null!;
    private MusicLibrary musicLibrary = null!;
    private MusicArea music = null!;

    // Discovery (Phase 7): prompted playlists + the quiet weekly shelf.

    // Owner's-voice greeting, reusing the music Google account to read the shared voice profile from Drive.
    private Codex.Ui.Voice.VoiceGreeting? greeting;
    private TextView? greetingView;

    private FrameLayout content = null!;
    private NowPlayingBar nowPlayingBar = null!;
    private View podcastBottomBar = null!; // Shows/Queue/Manage/Speed — hidden on the music side
    private Handler? uiHandler;
    private Java.Lang.Runnable? ticker;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        playback = new ScorePlayback(this);
        store = new PodcastStore(this);
        progressStore = new PlaybackProgressStore(this);
        settingsStore = new PlaybackSettingsStore(this);
        subscriptionStore = new SubscriptionStore(this);
        episodeCache = new EpisodeCacheStore(this);
        queueStore = new QueueStore(this);
        queue.ReplaceWith(queueStore.Load()); // restore the queue built in a previous session (pruned to live episodes on render)
        librarySync = new PodcastLibrarySync(
            musicTokens.GetAccessToken, "Score",
            store, subscriptionStore, progressStore, settingsStore, new EqualizerStore(this));
        settings = settingsStore.Load();
        subscriptions = new SubscriptionList(subscriptionStore.Load());
        MigrateLegacyFeedsIfNeeded();
        LoadCachedEpisodes(); // paint the shelf from the previous fetch before the network refresh
        RestoreLibraryFromDrive();

        listening = new ListeningLog(
            this, new Score.Google.MusicTasteService(musicTokens.GetAccessToken, "Score"));
        musicLibrary = new MusicLibrary(this, musicTokens, playback, listening, progressStore);
        musicLibrary.LoadCatalog();
        music = new MusicArea(this, musicLibrary, new DiscoverArea(this, musicLibrary));
        // The shell redraws only for the area that is showing — a scan finishing behind
        // the podcast side should not rebuild the podcast side.
        music.Changed += () => { if (side == 1) RenderContent(); };
        FridayPicksWorker.Schedule(this); // weekly "New for you"; no-ops until signed in

        greeting = new Codex.Ui.Voice.VoiceGreeting(musicTokens.GetAccessToken, "Score");
        SetContentView(BuildLayout());
        playback.Connect();
        ShowGreetingAsync();
        musicLibrary.RestoreFolderFromVault();
        CrashReporter.ShowPendingCrash(this);
        RenderContent();
    }

    // After a reinstall or the cutover's factory reset, refill the chosen music-folder id from the encrypted
    // Drive vault (needs the vault passphrase set + Google signed in), so the library reappears without
    // re-picking the folder. Runs only when none is set locally. Best-effort and off the UI thread.
    // Pulls the podcast library (shows + listened + resume points) down from Drive when this device has
    // none of its own — a factory reset or a new phone. Best-effort and off the UI thread; on success it
    // reloads the local state and re-renders. Local always wins when it already has shows, so this never
    // clobbers a working device (see PodcastLibrarySync / AudioLibrarySync).
    private void RestoreLibraryFromDrive() => Task.Run(async () =>
    {
        try
        {
            if (!await librarySync.RestoreAsync(CancellationToken.None))
                return;

            RunOnUiThread(() =>
            {
                subscriptions = new SubscriptionList(subscriptionStore.Load());
                settings = settingsStore.Load(); // restored speed/skip-silence takes effect on next play
                RecomputeDone();
                RenderContent();
                _ = RefreshFeedsAsync();
            });
        }
        catch
        {
            // Ignore: restore is a convenience; the app works from local storage regardless.
        }
    });

    // Mirrors the podcast library up to Drive when it has drifted from the backup. Best-effort and off the
    // UI thread; called when leaving the screen and after a bulk mark so the cloud copy tracks the device.
    private void BackupLibraryToDrive() => Task.Run(async () =>
    {
        try
        {
            await librarySync.BackupAsync(CancellationToken.None);
        }
        catch
        {
            // Best-effort: the next successful backup catches the cloud copy up.
        }
    });

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

        var header = PaperUi.PageHeader(this, "Score");
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

        nowPlayingBar = new NowPlayingBar(this, playback.SkipBack, playback.TogglePlayPause, playback.SkipForward,
            onOpen: () => StartActivity(new Intent(this, typeof(NowPlayingActivity))));
        root.AddView(nowPlayingBar);

        // The podcast-only bar. Held in a field so RenderContent can hide it on the
        // music side (its actions are all podcast-oriented); the now-playing bar
        // above it stays shared for both sides.
        podcastBottomBar = PaperUi.BottomBar(this,
            PaperUi.SecondaryButton(this, "Shows", () => { side = 0; openFeedId = null; showingQueue = false; RenderContent(); }),
            PaperUi.QuietButton(this, "Queue", () => { side = 0; showingQueue = true; RenderContent(); }),
            PaperUi.QuietButton(this, "Manage", () => StartActivity(new Intent(this, typeof(ManageSubscriptionsActivity)))),
            PaperUi.QuietButton(this, "Speed", ShowSpeedDialog));
        root.AddView(podcastBottomBar);
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
        listening.Stop();
        listening.MaybeRefreshTaste();

        // Mirror the podcast library to Drive on the way out — captures anything marked played this visit,
        // plus resume points the playback service saved, and subscription edits made in Manage.
        BackupLibraryToDrive();
    }

    protected override void OnDestroy()
    {
        playback.Release();
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
        else if (side == 1 && music.GoBack())
        {
            // The area came up a level and asked for the redraw itself.
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
        var pane = side == 1 ? music.BuildView() : BuildPodcastsPane();
        content.AddView(pane, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // The podcast bar (Shows/Queue/Manage/Speed) has no meaning on the music side.
        podcastBottomBar.Visibility = side == 1 ? ViewStates.Gone : ViewStates.Visible;
    }

    private View WrapScroll(View inner)
    {
        var scroll = new ScrollView(this);
        scroll.AddView(inner);
        return scroll;
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
        return PaperUi.List(this, rows,
            position =>
            {
                openFeedId = shelf[position].FeedId;
                RenderContent();
            },
            position => ShowShelfActions(shelf[position]));
    }

    // Long-press a show on the shelf: its one bulk action, marking every episode played.
    private void ShowShelfActions(ShowSummary show)
    {
        new AlertDialog.Builder(this)
            .SetTitle(show.Title)!
            .SetItems(new[] { "Mark all as played" }, (_, _) =>
                MarkManyWithUndo(BulkListen.AllInShow(episodes, show.FeedId), "Marked show played."))!
            .Show();
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
        return PaperUi.List(this, rows,
            position => EnqueueAndPlay(showEpisodes[position]),
            position => ShowEpisodeActions(showEpisodes[position]));
    }

    // Long-press an episode: queue it without playing, mark just it, or clear
    // everything older beneath it. Tapping the row (not long-press) still plays.
    private void ShowEpisodeActions(Episode episode)
    {
        new AlertDialog.Builder(this)
            .SetTitle(episode.Title)!
            .SetItems(new[] { "Add to queue", "Mark played", "Mark older as played" }, (_, args) =>
            {
                switch (args.Which)
                {
                    case 0:
                        AddToQueueWithUndo(episode);
                        break;
                    case 1:
                        MarkDoneWithUndo(episode);
                        break;
                    default:
                        MarkManyWithUndo(BulkListen.OlderInShow(episodes, episode), "Marked older played.");
                        break;
                }
            })!
            .Show();
    }

    private View BuildQueuePane()
    {
        // Prune finished/unknown episodes so the display order matches queue indices.
        queue.ReplaceWith(queue.Items.Where(id =>
            !doneIds.Contains(id) && episodes.Any(episode => episode.Id == id)));
        SaveQueue();

        var container = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var head = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        head.SetGravity(GravityFlags.CenterVertical);
        head.AddView(PaperUi.Heading(this, "Queue"),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        head.AddView(PaperUi.SecondaryButton(this, "Auto-arrange", ShowArrangeDialog));
        if (queue.Count > 0)
        {
            head.AddView(PaperUi.QuietButton(this, "Clear", ClearQueueWithUndo));
        }

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
            row.AddView(PaperUi.QuietButton(this, "↑", () => { queue.Move(index, index - 1); SaveQueue(); RenderContent(); }));
        }

        if (index < total - 1)
        {
            row.AddView(PaperUi.QuietButton(this, "↓", () => { queue.Move(index, index + 1); SaveQueue(); RenderContent(); }));
        }

        return row;
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

    // ---- Queue editing ----

    private void SaveQueue() => queueStore.Save(queue.Export());

    // Add to the queue without playing. Not destructive, but offered undo-first for
    // reversible feedback, matching the mark-played idiom (DESIGN.md §7). A duplicate
    // add is a no-op in PlaybackQueue, so undo only removes what this add introduced.
    private void AddToQueueWithUndo(Episode episode)
    {
        var wasQueued = queue.Items.Contains(episode.Id);
        queue.Add(episode.Id);
        SaveQueue();
        RenderContent();
        if (wasQueued)
        {
            return; // already in the queue; nothing new to undo
        }

        UndoBar.Show(this, content, "Added to queue.", onUndo: () =>
        {
            queue.Remove(episode.Id);
            SaveQueue();
            RenderContent();
        });
    }

    private void ClearQueueWithUndo()
    {
        if (queue.Count == 0)
        {
            return;
        }

        var snapshot = queue.Export();
        queue.Clear();
        SaveQueue();
        RenderContent();
        UndoBar.Show(this, content, "Cleared queue.", onUndo: () =>
        {
            queue.ReplaceWith(snapshot);
            SaveQueue();
            RenderContent();
        });
    }

    // ---- Playback ----

    private void EnqueueAndPlay(Episode episode)
    {
        queue.Add(episode.Id);
        SaveQueue();
        PlayQueue(episode.Id);
        RenderContent();
    }

    private void PlayQueue(string startId)
    {
        // The controller is built asynchronously on cold start; a tap before it
        // connects can't play, so say so (the episode is already queued) rather
        // than fail silently, and record it for later diagnosis.
        if (playback.Controller is null)
        {
            AudioDiagnostics.LogWarning("Playback", "Play requested before the media controller was ready.");
            Toast.MakeText(this, PodcastPlaybackDiagnostics.ControllerNotReady, ToastLength.Short)!.Show();
            return;
        }

        // Keep only playable, still-present episodes so start index and items align.
        queue.ReplaceWith(queue.Items.Where(id =>
            episodes.Any(episode => episode.Id == id && episode.AudioUrl is not null)));
        SaveQueue();

        var ids = queue.Items;
        var index = ids.ToList().IndexOf(startId);
        if (index < 0)
        {
            // The chosen episode has no playable enclosure (it was pruned above),
            // so it can never start — tell the user which one instead of no-op.
            var title = episodes.FirstOrDefault(episode => episode.Id == startId)?.Title;
            AudioDiagnostics.LogWarning("Playback", $"Episode has no playable audio: id={startId}.");
            Toast.MakeText(this, PodcastPlaybackDiagnostics.NoPlayableAudio(title), ToastLength.Short)!.Show();
            return;
        }

        try
        {
            var items = ids.Select(id => BuildItem(episodes.First(episode => episode.Id == id))).ToList();
            var startEpisode = episodes.First(episode => episode.Id == startId);
            nowPlaying = startEpisode;
            var showSettings = settings.For(startEpisode.FeedId);
            var resume = new PlaybackProgressBook(progressStore.Load())
                .ResumeWithRewind(startEpisode.Id, DateTimeOffset.Now);
            var startMs = resume is { } point ? (long)point.TotalMilliseconds : 0L;

            playback.Start(MediaKind.Podcast, items, index, startMs, showSettings.Speed, showSettings.SkipSilence);
        }
        catch (Exception e)
        {
            // Never let a media-stack fault die silently — capture the stack trace
            // to the rolling log and give the user a brief, honest message.
            AudioDiagnostics.LogWarning("Playback", "Starting playback failed.", e);
            Toast.MakeText(this, "Couldn't start playback — see Manage → Diagnostics.", ToastLength.Short)!.Show();
        }
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
        if (strategy is null)
        {
            return;
        }

        try
        {
            var unplayed = episodes.Where(episode => !doneIds.Contains(episode.Id)).ToList();
            if (unplayed.Count == 0)
            {
                // Nothing to arrange (feeds not fetched yet, or all caught up):
                // say why and leave any existing queue intact rather than wiping it.
                AudioDiagnostics.LogWarning("Queue",
                    $"Auto-arrange ({arrangement}) found no unplayed episodes; refreshing={isRefreshing}.");
                Toast.MakeText(this, PodcastPlaybackDiagnostics.NothingToQueue(isRefreshing), ToastLength.Short)!.Show();
                return;
            }

            queue.ReplaceWith(strategy.Arrange(unplayed).Select(episode => episode.Id));
            SaveQueue();
            RenderContent(); // the filled queue is the confirmation (inline state change, DESIGN.md §5)
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning("Queue", $"Auto-arrange ({arrangement}) failed.", e);
            Toast.MakeText(this, "Couldn't arrange the queue — see Manage → Diagnostics.", ToastLength.Short)!.Show();
        }
    }

    // ---- Done state ----

    private void MarkDoneWithUndo(Episode episode)
    {
        SetListened(episode.Id, listened: true);
        UndoBar.Show(this, content, "Marked played.", onUndo: () => SetListened(episode.Id, listened: false));
    }

    // Marks a batch played, remembering only the ids this action newly added so undo
    // restores exactly them and leaves already-played episodes alone. A no-op batch
    // (nothing left to mark) skips the undo bar rather than showing an empty promise.
    private void MarkManyWithUndo(IReadOnlyList<string> episodeIds, string message)
    {
        var listened = store.ListenedIds.ToHashSet();
        var added = episodeIds.Where(id => listened.Add(id)).ToList();
        if (added.Count == 0)
        {
            return;
        }

        store.SaveListenedIds(listened);
        RecomputeDone();
        RenderContent();
        UndoBar.Show(this, content, message, onUndo: () =>
        {
            var ids = store.ListenedIds.ToHashSet();
            ids.ExceptWith(added);
            store.SaveListenedIds(ids);
            RecomputeDone();
            RenderContent();
        });
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

    // Seed the shelf from the last fetch so a cold launch shows shows immediately instead of a blank
    // "Fetching episodes…". The background RefreshFeedsAsync then updates it in place.
    private void LoadCachedEpisodes()
    {
        var cache = episodeCache.Load();
        episodes = [.. cache.Episodes];
        feedTitles = new Dictionary<string, string>(cache.FeedTitles);
        RecomputeDone();
    }

    private sealed record FeedFetchResult(PodcastSubscription Subscription, PodcastFeed? Feed, string? FailedHost);

    private async Task RefreshFeedsAsync()
    {
        isRefreshing = true;
        // Only blank to "Fetching episodes…" when there's nothing cached to show yet (true first run).
        if (side == 0 && !showingQueue && openFeedId is null && episodes.Count == 0)
        {
            RenderContent();
        }

        var ordered = subscriptions.Ordered.ToList();
        FeedFetchResult[] results;
        try
        {
            using var http = new HttpClient();
            var client = new RssFeedClient(http);
            // Fetch every feed concurrently — wall time is now the slowest feed, not the sum of all.
            results = await Task.WhenAll(ordered.Select(subscription => FetchFeedAsync(client, subscription)));
        }
        finally
        {
            // Always leave the refreshing state — otherwise an unexpected failure
            // wedges the shelf on "Fetching episodes…" until the app is restarted.
            isRefreshing = false;
        }

        // Start from the current (cached) episodes, pruned to still-subscribed feeds, so a feed that
        // failed to fetch keeps its last-known episodes rather than vanishing on a flaky network.
        var subscribed = ordered.Select(subscription => subscription.FeedUrl).ToHashSet();
        var byFeed = episodes
            .Where(episode => subscribed.Contains(episode.FeedId))
            .GroupBy(episode => episode.FeedId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Episode>)group.ToList());
        var titles = feedTitles
            .Where(pair => subscribed.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        var failedHosts = new List<string>();
        foreach (var result in results)
        {
            if (result.Feed is { } feed)
            {
                byFeed[result.Subscription.FeedUrl] = feed.Episodes;
                titles[result.Subscription.FeedUrl] = feed.Title;
                subscriptions.UpdateTitle(result.Subscription.FeedUrl, feed.Title);
            }
            else if (result.FailedHost is { } host)
            {
                failedHosts.Add(host);
            }
        }

        subscriptionStore.Save(subscriptions.Export());
        episodes = [.. byFeed.Values.SelectMany(feedEpisodes => feedEpisodes)];
        feedTitles = titles;
        episodeCache.Save(episodes, titles); // persist for the next cold launch

        // Failure may speak (DESIGN.md §7), but a wall of toasts must not: summarize when several feeds fail.
        if (failedHosts.Count == 1)
        {
            Toast.MakeText(this, $"{failedHosts[0]} didn't answer. It retries on the next refresh.",
                ToastLength.Short)!.Show();
        }
        else if (failedHosts.Count > 1)
        {
            Toast.MakeText(this, $"{failedHosts.Count} shows didn't answer. They retry on the next refresh.",
                ToastLength.Short)!.Show();
        }

        RecomputeDone();
        RenderContent();
    }

    // Fetches one feed, folding the family of expected feed failures into a result rather than throwing, so
    // one bad feed never sinks the whole concurrent refresh: a moved feed serving HTML (InvalidDataException),
    // a network drop (HttpRequestException), a timeout (OperationCanceledException), or a corrupt stored URL
    // (UriFormatException) all come back as a failed host.
    private static async Task<FeedFetchResult> FetchFeedAsync(RssFeedClient client, PodcastSubscription subscription)
    {
        try
        {
            var feed = await client.FetchAsync(new Uri(subscription.FeedUrl));
            return new FeedFetchResult(subscription, feed, null);
        }
        catch (Exception e) when (
            e is HttpRequestException or InvalidDataException or System.OperationCanceledException or UriFormatException)
        {
            var host = Uri.TryCreate(subscription.FeedUrl, UriKind.Absolute, out var uri)
                ? uri.Host
                : subscription.FeedUrl;
            return new FeedFetchResult(subscription, null, host);
        }
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
        if (playback.Controller is not null)
        {
            nowPlayingBar.Update(playback.CurrentTitle, playback.IsPlaying);
            ObserveListening();
        }

        uiHandler?.PostDelayed(ticker!, 1000);
    }

    // Sample the music player for Genius. Only music counts (a podcast sets nowPlaying); the recorder turns
    // id changes into completed/skipped signals itself.
    private void ObserveListening()
    {
        if (playback.Kind != MediaKind.Music || playback.CurrentMediaId is null)
        {
            return;
        }

        listening.Observe(playback.CurrentMediaId, playback.Position, playback.Duration);
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
        playback.ApplySettings(updated.Speed, updated.SkipSilence);
    }

    private static string SpeedLabel(float speed) => $"Speed {speed:0.0}×";

    private static string TrimLabel(bool on) => on ? "Trim silence: on" : "Trim silence: off";
}
