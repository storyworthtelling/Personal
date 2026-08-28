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

    // The standing rule that rebuilds the queue, and what the owner pinned into it
    // by hand. Both persist; the queue itself is derived from them plus what is
    // still unplayed, which is why a rebuild is safe to run whenever that changes.
    private QueueArrangement arrangement;
    private readonly List<string> pinned = [];

    // Picking episodes. Held by the area, like the filter, so drilling out of a show
    // and back does not leave a half-built selection behind a screen you cannot see.
    private readonly Selection selection = new();

    public PodcastsArea(Activity host, ScorePlayback playback, PlaybackProgressStore progress) : base(host)
    {
        this.playback = playback;
        progressStore = progress;
        store = new PodcastStore(host);
        settingsStore = new PlaybackSettingsStore(host);
        subscriptionStore = new SubscriptionStore(host);
        episodeCache = new EpisodeCacheStore(host);
        queueStore = QueueStore.ForPodcasts(host);
        queue.ReplaceWith(queueStore.Load()); // restore the queue built in a previous session (pruned to live episodes on render)
        arrangement = queueStore.LoadArrangement();
        pinned.AddRange(queueStore.LoadPinned());
        librarySync = new PodcastLibrarySync(
            new DeviceAccountTokenProvider().GetAccessToken, "Score",
            store, subscriptionStore, progressStore, settingsStore, new EqualizerStore(host));

        settings = settingsStore.Load();
        subscriptions = new SubscriptionList(subscriptionStore.Load());
        MigrateLegacyFeedsIfNeeded();
        LoadCachedEpisodes(); // paint the shelf from the previous fetch before the network refresh
        RestoreLibraryFromDrive();
    }

    public override BinderArea Tab => new("Podcasts", Resource.Drawable.area_podcasts, PaperTheme.ScoreMark);

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


    /// <summary>
    /// The area's own actions. The shell hosts it under the now-playing bar — except
    /// while picking, when the selection's actions take the bar and the now-playing
    /// bar gives up its place: the list is not playing anything just then.
    /// </summary>
    public override View? BuildBottomBar() => selection.IsActive
        ? PaperUi.BottomBar(Host,
            PaperUi.QuietButton(Host, "Cancel", EndSelection),
            PaperUi.SecondaryButton(Host, "Play next", () => QueuePicked(next: true)),
            PaperUi.PrimaryButton(Host, $"Add {selection.Count}", () => QueuePicked(next: false)))
        : PaperUi.BottomBar(Host,
            PaperUi.SecondaryButton(Host, "Shows", () => { openFeedId = null; showingQueue = false; Invalidate(); }),
            PaperUi.QuietButton(Host, "Queue", () => { showingQueue = true; Invalidate(); }),
            PaperUi.QuietButton(Host, "Manage", () => Host.StartActivity(new Intent(Host, typeof(ManageSubscriptionsActivity)))));

    public override bool ShowsNowPlayingBar => !selection.IsActive;

    public override bool GoBack()
    {
        // Picking is a mode, not a level: back leaves the mode and keeps the list.
        if (selection.IsActive)
        {
            EndSelection();
            return true;
        }

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
                RecomputeDoneAndRebuild();
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
            .Select(episode => new PaperListRow(
                episode.Title, EpisodeSubtitle(episode, progress),
                Picked: selection.Contains(episode.Id)))
            .ToList();

        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(BuildPickerControls([.. showEpisodes.Select(episode => episode.Id)]));

        // While picking, a tap picks and nothing plays — the list is one control for
        // as long as the mode is on.
        var list = selection.IsActive
            ? PaperUi.List(Host, rows, position =>
            {
                selection.Toggle(showEpisodes[position].Id);
                Invalidate();
            }, picking: true)
            : PaperUi.List(Host, rows,
                position => EnqueueAndPlay(showEpisodes[position]),
                position => ShowEpisodeActions(showEpisodes[position]));

        column.AddView(list, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return column;
    }

    /// <summary>
    /// The way into picking, and what it offers above the list: the running count, an
    /// All that flips to None once everything showing is picked, and the bulk mark —
    /// which lives here because the bottom bar is already carrying the three things
    /// you do with a selection, and a five-button bar is a menu.
    /// </summary>
    private View BuildPickerControls(IReadOnlyList<string> visible)
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        if (!selection.IsActive)
        {
            row.AddView(PaperUi.QuietButton(Host, "Select", () =>
            {
                selection.Begin();
                Invalidate();
            }));
            return row;
        }

        row.AddView(PaperUi.Secondary(Host, $"{selection.Count} picked"),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(PaperUi.QuietButton(Host, selection.AllSelected(visible) ? "None" : "All", () =>
        {
            selection.ToggleAll(visible);
            Invalidate();
        }));

        if (selection.Count > 0)
        {
            row.AddView(PaperUi.QuietButton(Host, "Mark played", () =>
            {
                var ids = selection.Items.ToList();
                EndSelection();
                MarkManyWithUndo(ids, "Marked played.");
            }));
        }

        return row;
    }

    /// <summary>
    /// Queue everything picked. Queueing by hand pins it, exactly as the single add
    /// does — an explicit instruction outlives every rebuild of the standing rule.
    ///
    /// <i>Play next</i> puts the picks straight after whatever is playing, or at the
    /// head of the pins when what is playing came from the rule rather than by hand;
    /// the rule lays pinned episodes down first and in their own order, so the head
    /// of the pins is the front of the run either way.
    /// </summary>
    private void QueuePicked(bool next)
    {
        var added = selection.Items.Where(id => !pinned.Contains(id)).ToList();
        EndSelection();
        if (added.Count == 0)
        {
            return;
        }

        var at = nowPlaying is not null ? pinned.IndexOf(nowPlaying.Id) : -1;
        if (next)
        {
            pinned.InsertRange(at + 1, added);
        }
        else
        {
            pinned.AddRange(added);
        }

        SavePinned();
        RebuildQueue();
        Invalidate();

        UndoBar.Show(Host, Overlay, $"Added {added.Count} to queue.", onUndo: () =>
        {
            // Only what this add introduced: anything that was already pinned was
            // filtered out above, so undo cannot take a pin someone else made.
            pinned.RemoveAll(added.Contains);
            SavePinned();
            RebuildQueue();
            Invalidate();
        });
    }

    private void EndSelection()
    {
        selection.Cancel();
        Invalidate();
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
        // One authority: the rule decides what is in the queue, here as everywhere
        // else. This used to prune inline, which meant two places could disagree.
        RebuildQueue();

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

    /// <summary>
    /// Re-applies the standing rule. Called whenever the pool of unplayed episodes
    /// changes — a refresh brought new ones in, something was marked played — which
    /// is the whole difference between a rule and the one-shot this replaced.
    /// </summary>
    private void RebuildQueue()
    {
        // Only episodes that can actually play: an entry with no audio is noise in a
        // list whose whole purpose is what to play next.
        var unplayed = episodes
            .Where(episode => !doneIds.Contains(episode.Id) && episode.AudioUrl is not null)
            .ToList();
        queue.ReplaceWith(StandingQueue.Rebuild(arrangement, pinned, unplayed));
        SaveQueue();
    }

    private void SavePinned() => queueStore.SavePinned(pinned);

    // Add to the queue without playing. Not destructive, but offered undo-first for
    // reversible feedback, matching the mark-played idiom (DESIGN.md §7). A duplicate
    // add is a no-op in PlaybackQueue, so undo only removes what this add introduced.
    private void AddToQueueWithUndo(Episode episode)
    {
        if (pinned.Contains(episode.Id))
        {
            return; // already put there by hand; nothing new to undo
        }

        // Queueing by hand pins it: an explicit instruction outlives every rebuild.
        pinned.Add(episode.Id);
        SavePinned();
        RebuildQueue();
        Invalidate();

        UndoBar.Show(Host, Overlay, "Added to queue.", onUndo: () =>
        {
            pinned.Remove(episode.Id);
            SavePinned();
            RebuildQueue();
            Invalidate();
        });
    }

    private void ClearQueueWithUndo()
    {
        if (queue.Count == 0)
        {
            return;
        }

        // Clearing has to stop the rule as well as empty the list. Leave the rule
        // running and the next rebuild puts everything straight back, so the button
        // would appear to do nothing.
        var previousPins = pinned.ToList();
        var previousArrangement = arrangement;

        pinned.Clear();
        arrangement = QueueArrangement.Manual;
        SavePinned();
        queueStore.SaveArrangement(arrangement);
        queue.Clear();
        SaveQueue();
        Invalidate();

        UndoBar.Show(Host, Overlay, "Cleared queue.", onUndo: () =>
        {
            pinned.Clear();
            pinned.AddRange(previousPins);
            arrangement = previousArrangement;
            SavePinned();
            queueStore.SaveArrangement(arrangement);
            RebuildQueue();
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

        // Prune to playable, still-present episodes and locate the chosen one in what
        // survives — in that order, so the index matches the list the player gets.
        var start = QueueStart.Locate(
            queue.Items,
            id => episodes.Any(episode => episode.Id == id && episode.AudioUrl is not null),
            startId);
        queue.ReplaceWith(start.Ids);
        SaveQueue();

        if (!start.Found)
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
            var items = start.Ids.Select(id => BuildItem(episodes.First(episode => episode.Id == id))).ToList();
            var startEpisode = episodes.First(episode => episode.Id == startId);
            nowPlaying = startEpisode;
            var showSettings = settings.For(startEpisode.FeedId);
            var resume = new PlaybackProgressBook(progressStore.Load())
                .ResumeWithRewind(startEpisode.Id, DateTimeOffset.Now);
            var startMs = resume is { } point ? (long)point.TotalMilliseconds : 0L;

            playback.Start(MediaKind.Podcast, items, start.Index, startMs, showSettings.Speed, showSettings.SkipSilence,
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

    // §7's picker: the field expands in place. It is a setting rather than a button
    // now — it shows which rule is running, and Manual is how you turn it off.
    private View BuildArrangePicker()
    {
        var arrangements = new[]
        {
            (QueueArrangement.Manual, "By hand", "Only what you add. Nothing rearranges it."),
            (QueueArrangement.RoundRobin, "Round-robin", "One from each show in turn."),
            (QueueArrangement.Chronological, "Chronological", "Oldest first, across every show."),
            (QueueArrangement.BacklogBuster, "Backlog-buster", "Finish one show before starting the next."),
        };

        var current = Array.FindIndex(arrangements, a => a.Item1 == arrangement);
        var picker = new AccordionPicker(
            Host,
            "Keep the queue:",
            arrangements.Select(a => new PickerOption(a.Item2, a.Item3)),
            current < 0 ? 0 : current);
        picker.Chosen += index => ChooseArrangement(arrangements[index].Item1);
        return picker;
    }

    private void ChooseArrangement(QueueArrangement chosen)
    {
        status = null;
        arrangement = chosen;
        queueStore.SaveArrangement(arrangement);

        try
        {
            RebuildQueue();

            // Under a rule, an empty result means the pool is empty — not that the
            // arrangement failed. Say which, rather than leaving a blank queue.
            if (queue.Count == 0 && arrangement != QueueArrangement.Manual)
            {
                AudioDiagnostics.LogWarning("Queue",
                    $"Arrangement ({arrangement}) found no unplayed episodes; refreshing={isRefreshing}.");
                Say(PodcastPlaybackDiagnostics.NothingToQueue(isRefreshing));
                return;
            }

            Invalidate(); // the filled queue is the confirmation (inline state change, DESIGN.md §5)
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning("Queue", $"Arrangement ({arrangement}) failed.", e);
            Say("Couldn't arrange the queue — see Manage → Diagnostics.");
        }
    }

    // ---- Done state ----

    private void MarkDoneWithUndo(Episode episode)
    {
        SetListened(episode.Id, listened: true);
        UndoBar.Show(Host, Overlay, "Marked played.", onUndo: () => SetListened(episode.Id, listened: false));
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
        RecomputeDoneAndRebuild();
        Invalidate();
        UndoBar.Show(Host, Overlay, message, onUndo: () =>
        {
            var ids = store.ListenedIds.ToHashSet();
            ids.ExceptWith(added);
            store.SaveListenedIds(ids);
            RecomputeDoneAndRebuild();
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
        RecomputeDoneAndRebuild();
        Invalidate();
    }

    private void RecomputeDoneAndRebuild()
    {
        RecomputeDone();
        RebuildQueue();
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
        RecomputeDoneAndRebuild();
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

        RecomputeDoneAndRebuild();
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
