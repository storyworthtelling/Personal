using Android.Content;
using Android.Views;
using AndroidX.Media3.Common;
using Codex.Domain.Audio;
using Codex.Domain.Design;
using Codex.Domain.Time;
using Codex.Infrastructure.Podcasts;
using Codex.Ui;
using Score.Google;

namespace Score;

/// <summary>
/// The podcast area: a per-show shelf that drills into a show's episodes, and a
/// reorderable queue with three auto-arrange strategies and cross-show auto-continue.
///
/// It owns every podcast store and all the podcast state, including which episode is
/// playing — the shell no longer knows what an episode is. The feed refresh runs on
/// resume whether or not this area is showing, because a shelf that is stale when you
/// finally switch to it is worse than a background fetch nobody watched.
/// </summary>
public sealed class PodcastsArea : ScoreArea
{
    private readonly ScorePlayback playback;
    private readonly PodcastStore store;
    private readonly PlaybackProgressStore progressStore;
    private readonly PlaybackSettingsStore settingsStore;
    private readonly SubscriptionStore subscriptionStore;
    private readonly EpisodeCacheStore episodeCache;
    private readonly QueueStore queueStore;
    private readonly PodcastLibrarySync librarySync;
    private readonly PlaybackQueue queue = new();

    private PlaybackSettingsSnapshot settings = PlaybackSettingsSnapshot.Empty();
    private SubscriptionList subscriptions = new();
    private List<Episode> episodes = [];
    private Dictionary<string, string> feedTitles = [];
    private IReadOnlySet<string> doneIds = new HashSet<string>();

    private Episode? nowPlaying;
    private string? openFeedId;
    private bool showingQueue;
    private bool isRefreshing; // a feed refresh is in flight — the empty shelf says so instead of "nothing new"

    // What went wrong last, shown as a line on the page. §7 has no toasts: a message
    // that floats over the content and then leaves is unreadable if you looked away,
    // and unreachable if you want it again.
    private string? status;

    // The shelf filter, held by the area rather than the field, so drilling into a
    // show and coming back does not silently show a narrowed shelf as if it were
    // the whole one.
    private string shelfQuery = string.Empty;

    public PodcastsArea(Activity host, ScorePlayback playback, PlaybackProgressStore progress) : base(host)
    {
        this.playback = playback;
        progressStore = progress;
        store = new PodcastStore(host);
        settingsStore = new PlaybackSettingsStore(host);
        subscriptionStore = new SubscriptionStore(host);
        episodeCache = new EpisodeCacheStore(host);
        queueStore = new QueueStore(host);
        queue.ReplaceWith(queueStore.Load()); // restore the queue built in a previous session (pruned to live episodes on render)
        librarySync = new PodcastLibrarySync(
            new DeviceAccountTokenProvider().GetAccessToken, "Score",
            store, subscriptionStore, progressStore, settingsStore, new EqualizerStore(host));

        settings = settingsStore.Load();
        subscriptions = new SubscriptionList(subscriptionStore.Load());
        MigrateLegacyFeedsIfNeeded();
        LoadCachedEpisodes(); // paint the shelf from the previous fetch before the network refresh
        RestoreLibraryFromDrive();
    }

    public override BinderArea Tab => new("Podcasts", Resource.Drawable.area_podcasts, PaperTheme.ScoreMark, PaperTheme.ScoreFill);

    public override View BuildView()
    {
        var pane = BuildPodcastsPane();
        if (status is null)
        {
            return pane;
        }

        // Above the content, in the flow — it moves the page rather than covering it,
        // which is what makes it survive looking away.
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.Secondary(Host, status));
        column.AddView(pane, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return column;
    }

    // Says what went wrong, in place. Each new message replaces the last, and the
    // actions that can fail drop it as they start — a stale complaint is worse
    // than none.
    private void Say(string message)
    {
        status = message;
        Invalidate();
    }


    /// <summary>The area's own actions. The shell hosts it under the now-playing bar.</summary>
    public override View? BuildBottomBar() => PaperUi.BottomBar(Host,
        PaperUi.SecondaryButton(Host, "Shows", () => { openFeedId = null; showingQueue = false; Invalidate(); }),
        PaperUi.QuietButton(Host, "Queue", () => { showingQueue = true; Invalidate(); }),
        PaperUi.QuietButton(Host, "Manage", () => Host.StartActivity(new Intent(Host, typeof(ManageSubscriptionsActivity)))));

    public override bool GoBack()
    {
        if (showingQueue)
        {
            showingQueue = false;
            Invalidate();
            return true;
        }

        if (openFeedId is not null)
        {
            openFeedId = null;
            Invalidate();
            return true;
        }

        return false;
    }

    public override void Resume()
    {
        subscriptions = new SubscriptionList(subscriptionStore.Load());
        settings = settingsStore.Load();
        _ = RefreshFeedsAsync();
    }

    /// <summary>
    /// Mirror the library to Drive on the way out — it captures anything marked
    /// played this visit, resume points the playback service saved, and subscription
    /// edits made in Manage.
    /// </summary>
    public override void Pause() => BackupLibraryToDrive();

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

            Host.RunOnUiThread(() =>
            {
                subscriptions = new SubscriptionList(subscriptionStore.Load());
                settings = settingsStore.Load(); // restored speed/skip-silence takes effect on next play
                RecomputeDone();
                Invalidate();
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


    private View WrapScroll(View inner)
    {
        var scroll = new ScrollView(Host);
        scroll.AddView(inner);
        return scroll;
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
            return PaperUi.EmptyState(Host, EmptyShelfMessage());
        }

        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var rows = new FrameLayout(Host);

        void Fill()
        {
            var matches = string.IsNullOrEmpty(shelfQuery)
                ? shelf
                : shelf.Where(show => FilterMatch.Matches(show.Title, shelfQuery)).ToList();

            rows.RemoveAllViews();
            rows.AddView(matches.Count == 0
                ? PaperUi.EmptyState(Host, $"No show matches \u201c{shelfQuery}\u201d.")
                : PaperUi.List(Host,
                    [.. matches.Select(show => new PaperListRow(show.Title, $"{show.UnplayedCount} unplayed"))],
                    position =>
                    {
                        openFeedId = matches[position].FeedId;
                        Invalidate();
                    },
                    position => ShowShelfActions(matches[position])));
        }

        if (FilterMatch.IsWarranted(shelf.Count))
        {
            var field = new FilterField(Host, "Filter shows", PaperTheme.ScoreMark);
            field.SetQuery(shelfQuery);
            // Rows only — rebuilding the pane per keystroke would take the field's
            // focus and the keyboard with it.
            field.QueryChanged += text =>
            {
                shelfQuery = text;
                Fill();
            };
            column.AddView(field);
        }

        Fill();
        column.AddView(rows, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return column;
    }

    // Long-press a show on the shelf: its one bulk action, marking every episode
    // played. §7's action card rather than a dialog — it is not asking, it does the
    // thing, and the undo dog-ear is what makes that safe.
    private void ShowShelfActions(ShowSummary show)
    {
        ActionCard.Show(Host, Overlay, show.Title, null,
            new CardAction("Mark all as played",
                () => MarkManyWithUndo(BulkListen.AllInShow(episodes, show.FeedId), "Marked show played.")));
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
            return PaperUi.EmptyState(Host, "All caught up.");
        }

        var progress = new PlaybackProgressBook(progressStore.Load());
        var rows = showEpisodes
            .Select(episode => new PaperListRow(episode.Title, EpisodeSubtitle(episode, progress)))
            .ToList();
        return PaperUi.List(Host, rows,
            position => EnqueueAndPlay(showEpisodes[position]),
            position => ShowEpisodeActions(showEpisodes[position]));
    }

    // Long-press an episode: queue it without playing, mark just it, or clear
    // everything older beneath it. Tapping the row (not long-press) still plays.
    private void ShowEpisodeActions(Episode episode)
    {
        ActionCard.Show(Host, Overlay, episode.Title, feedTitles.GetValueOrDefault(episode.FeedId),
            new CardAction("Add to queue", () => AddToQueueWithUndo(episode), Lead: true),
            new CardAction("Mark played", () => MarkDoneWithUndo(episode)),
            new CardAction("Mark older as played",
                () => MarkManyWithUndo(BulkListen.OlderInShow(episodes, episode), "Marked older played.")));
    }

    private View BuildQueuePane()
    {
        // Prune finished/unknown episodes so the display order matches queue indices.
        queue.ReplaceWith(queue.Items.Where(id =>
            !doneIds.Contains(id) && episodes.Any(episode => episode.Id == id)));
        SaveQueue();

        var container = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        var head = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        head.SetGravity(GravityFlags.CenterVertical);
        head.AddView(PaperUi.Heading(Host, "Queue"),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        if (queue.Count > 0)
        {
            head.AddView(PaperUi.QuietButton(Host, "Clear", ClearQueueWithUndo));
        }

        container.AddView(head);
        container.AddView(BuildArrangePicker());

        var queued = queue.Items
            .Select(id => episodes.First(episode => episode.Id == id))
            .ToList();
        if (queued.Count == 0)
        {
            container.AddView(PaperUi.EmptyState(Host, "Queue is empty."));
            return container;
        }

        var scroll = new ScrollView(Host);
        var list = new LinearLayout(Host) { Orientation = Orientation.Vertical };
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
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        var vertical = PaperTheme.Dp(Host, DesignTokens.Space.X3);
        row.SetPadding(0, vertical, 0, vertical);

        var title = PaperUi.Body(Host, episode.Title);
        title.Clickable = true;
        title.Click += (_, _) => PlayQueue(episode.Id);
        row.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        if (index > 0)
        {
            row.AddView(PaperUi.QuietButton(Host, "↑", () => { queue.Move(index, index - 1); SaveQueue(); Invalidate(); }));
        }

        if (index < total - 1)
        {
            row.AddView(PaperUi.QuietButton(Host, "↓", () => { queue.Move(index, index + 1); SaveQueue(); Invalidate(); }));
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
        Invalidate();
        if (wasQueued)
        {
            return; // already in the queue; nothing new to undo
        }

        DogEar.Show(Host, Overlay, "Added to queue.", onUndo: () =>
        {
            queue.Remove(episode.Id);
            SaveQueue();
            Invalidate();
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
        Invalidate();
        DogEar.Show(Host, Overlay, "Cleared queue.", onUndo: () =>
        {
            queue.ReplaceWith(snapshot);
            SaveQueue();
            Invalidate();
        });
    }

    // ---- Playback ----

    private void EnqueueAndPlay(Episode episode)
    {
        queue.Add(episode.Id);
        SaveQueue();
        PlayQueue(episode.Id);
        Invalidate();
    }

    private void PlayQueue(string startId)
    {
        status = null;

        // The controller is built asynchronously on cold start; a tap before it
        // connects can't play, so say so (the episode is already queued) rather
        // than fail silently, and record it for later diagnosis.
        if (playback.Controller is null)
        {
            AudioDiagnostics.LogWarning("Playback", "Play requested before the media controller was ready.");
            Say(PodcastPlaybackDiagnostics.ControllerNotReady);
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
            Say(PodcastPlaybackDiagnostics.NoPlayableAudio(title));
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

            playback.Start(MediaKind.Podcast, items, index, startMs, showSettings.Speed, showSettings.SkipSilence,
                settingsKey: startEpisode.FeedId);
        }
        catch (Exception e)
        {
            // Never let a media-stack fault die silently — capture the stack trace
            // to the rolling log and give the user a brief, honest message.
            AudioDiagnostics.LogWarning("Playback", "Starting playback failed.", e);
            Say("Couldn't start playback — see Manage → Diagnostics.");
        }
    }

    private static MediaItem BuildItem(Episode episode)
    {
        // The description rides along so the Playing area can show notes without
        // reaching back into podcast state for the episode behind the media id.
        var metadata = new MediaMetadata.Builder()
            .SetTitle(episode.Title)!
            .SetDescription(episode.Description)!
            .Build()!;
        return new MediaItem.Builder()
            .SetMediaId(episode.Id)!
            .SetUri(episode.AudioUrl)!
            .SetMediaMetadata(metadata)!
            .Build()!;
    }

    // ---- Queue arrangement ----

    // §7's picker: the field expands in place. No dialog, and no remembered
    // selection either — arranging is an act, not a mode, so the field reads
    // "Auto-arrange:" until you pick one and does it.
    private View BuildArrangePicker()
    {
        var arrangements = new[]
        {
            (QueueArrangement.RoundRobin, "Round-robin", "One from each show in turn."),
            (QueueArrangement.Chronological, "Chronological", "Oldest first, across every show."),
            (QueueArrangement.BacklogBuster, "Backlog-buster", "Finish one show before starting the next."),
        };

        var picker = new AccordionPicker(
            Host,
            "Auto-arrange:",
            arrangements.Select(a => new PickerOption(a.Item2, a.Item3)),
            0);
        picker.Chosen += index => AutoArrange(arrangements[index].Item1);
        return picker;
    }

    private void AutoArrange(QueueArrangement arrangement)
    {
        status = null;

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
                Say(PodcastPlaybackDiagnostics.NothingToQueue(isRefreshing));
                return;
            }

            queue.ReplaceWith(strategy.Arrange(unplayed).Select(episode => episode.Id));
            SaveQueue();
            Invalidate(); // the filled queue is the confirmation (inline state change, DESIGN.md §5)
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning("Queue", $"Auto-arrange ({arrangement}) failed.", e);
            Say("Couldn't arrange the queue — see Manage → Diagnostics.");
        }
    }

    // ---- Done state ----

    private void MarkDoneWithUndo(Episode episode)
    {
        SetListened(episode.Id, listened: true);
        DogEar.Show(Host, Overlay, "Marked played.", onUndo: () => SetListened(episode.Id, listened: false));
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
        Invalidate();
        DogEar.Show(Host, Overlay, message, onUndo: () =>
        {
            var ids = store.ListenedIds.ToHashSet();
            ids.ExceptWith(added);
            store.SaveListenedIds(ids);
            RecomputeDone();
            Invalidate();
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
        Invalidate();
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
        if (!showingQueue && openFeedId is null && episodes.Count == 0)
        {
            Invalidate();
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

        // Failure may speak (DESIGN.md §7), but not once per feed: summarize when several fail.
        if (failedHosts.Count == 1)
        {
            Say($"{failedHosts[0]} didn't answer. It retries on the next refresh.");
        }
        else if (failedHosts.Count > 1)
        {
            Say($"{failedHosts.Count} shows didn't answer. They retry on the next refresh.");
        }

        RecomputeDone();
        Invalidate();
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



}
