using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Post;
using Codex.Domain.Time;
using Codex.Domain.Timeline;
using Codex.Ui;

namespace Post;

/// <summary>
/// The Mailbox: everything that has arrived and is not yet dealt with, newest first, in
/// one undivided list — calls, texts and email merged into a row per person (§D2/D4).
/// This is Post's home area, so its tab takes the app's identity hue (§9).
///
/// The list has <b>no pinned action bar</b>: what you do here you do to a row, not to
/// the screen. Errors do not appear on the page either — they fold into the dog-ear and
/// wait to be turned to (§13).
/// </summary>
public sealed class MailboxArea(Activity host) : PostArea(host)
{
    // The timeline cache is read a page at a time (newest first) so a large history never
    // has to be pulled into memory at once; more pages load as the owner scrolls. Live
    // call/SMS rows are the small recent tail and come in whole on each reload.
    private const int CachePageSize = 300;
    private const int LoadMoreThreshold = 30;

    private ListView? threadList;
    private TextView? emptyView;
    private TextView? statusLine;
    private FilterField? filter;
    private string query = string.Empty;

    // Accumulated inputs, only ever mutated on the UI thread once a background load
    // returns; the merge/title work itself runs off the UI thread.
    private readonly List<TimelineItem> liveItems = [];
    private readonly List<TimelineItem> cachedItems = [];
    private int cacheOffset;
    private bool cacheExhausted;
    private bool loading;
    private bool syncing;
    // Bumped on every full reload so a superseded background load discards itself.
    private int loadGeneration;

    private IReadOnlyList<ThreadRow> allRows = [];
    private IReadOnlyList<ThreadRow> shownRows = [];

    // Marketing-classified mail, set aside from the ordinary list into its own quiet,
    // collapsible bucket (see remarks on BuildMarketingSection). It ages out on exactly the
    // same one-day schedule as before (Codex.Domain.Post.MailAgeing) — this only changes
    // where it is seen in the meantime, not when it goes.
    private IReadOnlyList<ThreadRow> marketingRows = [];
    private bool marketingExpanded;
    private LinearLayout? marketingSection;
    private TextView? marketingHeader;
    private LinearLayout? marketingItemsHost;

    // What the empty state last said, and how far up any ladder it had climbed. Held for
    // the life of the screen and persisted by the store, so opening an unchanged empty
    // Mailbox again is what steps the line up.
    private readonly EmptyMailboxStore emptyState = new(host);
    private string emptyLine = string.Empty;
    private int lastSeenRowCount = -1;

    // A prepared list row: identity to open, plus the strings the list renders. Titles are
    // resolved off the UI thread (a Contacts query each), so binding and filtering stay cheap.
    private sealed record ThreadRow(string ContactKey, string Title, string Subtitle, bool HasUnread);

    public override BinderArea Tab =>
        new("Mailbox", Resource.Drawable.area_mailbox, PaperTheme.PostMark, PaperTheme.PostFill);

    public override View BuildView()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        // A find-a-person filter, offered only once the list outruns a screen.
        filter = new FilterField(Host, "Find a person", PaperTheme.PostMark);
        filter.SetQuery(query);
        filter.QueryChanged += text =>
        {
            query = text;
            BindRows();
        };
        column.AddView(filter, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        column.AddView(BuildMarketingSection(), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        // A quiet status sentence for loading/sync (§8: a sentence, not a spinner).
        // Hidden when idle so it takes no space.
        statusLine = PaperUi.Secondary(Host, string.Empty);
        statusLine.Visibility = ViewStates.Gone;
        column.AddView(statusLine, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var frame = new FrameLayout(Host);
        threadList = new ListView(Host);
        PaperUi.StylePaperList(Host, threadList);
        threadList.ItemClick += (_, e) =>
        {
            if (e.Position < 0 || e.Position >= shownRows.Count)
            {
                return;
            }
            var intent = new Android.Content.Intent(Host, typeof(ThreadActivity));
            intent.PutExtra(ThreadActivity.ExtraContactKey, shownRows[e.Position].ContactKey);
            Host.StartActivity(intent);
        };

        // Filing is the Mailbox's one destructive act, and it is undo-first: the
        // correspondence goes at once and the corner folds (§7).
        threadList.ItemLongClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < shownRows.Count)
            {
                ShowRowActions(shownRows[e.Position]);
            }
        };

        // Pull the next page as the list nears its end (only while more remains).
        threadList.Scroll += (_, e) =>
        {
            if (!loading && !cacheExhausted && e.TotalItemCount > 0
                && e.FirstVisibleItem + e.VisibleItemCount >= e.TotalItemCount - LoadMoreThreshold)
            {
                LoadMore();
            }
        };
        frame.AddView(threadList, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        emptyView = PaperUi.EmptyState(Host, emptyLine);
        frame.AddView(emptyView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        threadList.EmptyView = emptyView;

        column.AddView(frame, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        BindRows();
        BindMarketingSection();
        return column;
    }

    // A quiet, collapsible bucket for marketing-classified mail (Codex.Domain.Post.MailClass
    // .Marketing), set aside from the ordinary list rather than mixed into it — moving it
    // here is itself a form of dealt-with, the way filing or blocking a sender are. It is
    // not a new binder tab (that is a bigger decision than this one) and it is not a
    // confirm step or a toast: the header just says how many are waiting and expands in
    // place, updating on the normal render cycle like everything else in the Mailbox.
    //
    // Marketing mail still ages out on exactly the schedule it always has
    // (Codex.Domain.Post.MailAgeing, one day) — this section only changes where it is seen
    // until then, never when it goes.
    private View BuildMarketingSection()
    {
        marketingSection = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        marketingHeader = PaperUi.Secondary(Host, string.Empty);
        var verticalPadding = PaperTheme.Dp(Host, DesignTokens.Space.X3);
        marketingHeader.SetPadding(0, verticalPadding, 0, verticalPadding);
        marketingHeader.Click += (_, _) =>
        {
            marketingExpanded = !marketingExpanded;
            BindMarketingSection();
        };
        marketingSection.AddView(marketingHeader, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        marketingItemsHost = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        marketingSection.AddView(marketingItemsHost, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        marketingSection.Visibility = ViewStates.Gone;
        return marketingSection;
    }

    // Redraws the section from the current marketingRows — hidden entirely while the
    // bucket is empty, so an owner with no marketing mail waiting never sees it. Tapping a
    // row opens that correspondence exactly the way a Mailbox row does (ThreadActivity),
    // which is where the full set of actions (file, delete, …) already live.
    private void BindMarketingSection()
    {
        if (marketingSection is null || marketingHeader is null || marketingItemsHost is null)
        {
            return;
        }

        if (marketingRows.Count == 0)
        {
            marketingSection.Visibility = ViewStates.Gone;
            marketingExpanded = false;
            return;
        }

        marketingSection.Visibility = ViewStates.Visible;
        var arrow = marketingExpanded ? "▾" : "▸";
        marketingHeader.Text = $"{arrow} Marketing ({marketingRows.Count})";

        marketingItemsHost.RemoveAllViews();
        marketingItemsHost.Visibility = marketingExpanded ? ViewStates.Visible : ViewStates.Gone;
        if (marketingExpanded)
        {
            var rows = marketingRows;
            var list = PaperUi.List(Host, rows
                .Select(row => new PaperListRow(row.Title, row.Subtitle))
                .ToList(),
                position =>
                {
                    var intent = new Android.Content.Intent(Host, typeof(ThreadActivity));
                    intent.PutExtra(ThreadActivity.ExtraContactKey, rows[position].ContactKey);
                    Host.StartActivity(intent);
                });
            marketingItemsHost.AddView(list, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }
    }

    public override void Resume()
    {
        // The cache paints first so the Mailbox is never blank while the network is
        // consulted, then the sync brings it up to date behind that. Refresh on every open
        // plus the 30-minute worker; no last-synced line, and no Sync button — the Mailbox
        // has no pinned actions because what you do here you do to a row.
        ReloadThreads();
        SyncInBackground();
    }

    public override void OnPermissionsResult() => ReloadThreads();

    // Full reload: re-read the live call/SMS tail and the first cache page, then rebuild
    // the list. All I/O and merging happen off the UI thread so the screen never blocks;
    // only the final bind touches views.
    private void ReloadThreads()
    {
        var generation = ++loadGeneration;
        loading = true;
        SetStatus("Loading…");
        var titleSeed = allRows.ToDictionary(row => row.ContactKey, row => row.Title);

        Task.Run(() =>
        {
            var live = ReadLiveTail();
            var (page, rows, marketing) = ReadPageAndBuild(live, [], 0, titleSeed);
            Host.RunOnUiThread(() =>
            {
                if (generation != loadGeneration)
                {
                    return; // A newer reload has taken over.
                }
                liveItems.Clear();
                liveItems.AddRange(live);
                cachedItems.Clear();
                cachedItems.AddRange(page);
                cacheOffset = page.Count;
                cacheExhausted = page.Count < CachePageSize;
                allRows = rows;
                marketingRows = marketing;
                loading = false;
                RefreshEmptyLine(rows.Count);
                BindRows();
                BindMarketingSection();
                ClearStatus();
            });
        });
    }

    // Appends the next cache page to what is already shown. Kept in the same generation as
    // the reload it extends, so a concurrent reload discards it.
    private void LoadMore()
    {
        if (loading || cacheExhausted)
        {
            return;
        }
        loading = true;
        var generation = loadGeneration;
        var live = liveItems.ToList();
        var prior = cachedItems.ToList();
        var offset = cacheOffset;
        var titleSeed = allRows.ToDictionary(row => row.ContactKey, row => row.Title);

        Task.Run(() =>
        {
            var (page, rows, marketing) = ReadPageAndBuild(live, prior, offset, titleSeed);
            Host.RunOnUiThread(() =>
            {
                if (generation != loadGeneration)
                {
                    return;
                }
                cachedItems.AddRange(page);
                cacheOffset += page.Count;
                cacheExhausted = page.Count < CachePageSize;
                allRows = rows;
                marketingRows = marketing;
                loading = false;
                BindRows();
                BindMarketingSection();
            });
        });
    }

    private List<TimelineItem> ReadLiveTail()
    {
        var live = new List<TimelineItem>();
        try
        {
            live.AddRange(CallLogReader.ReadAll(Host));
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_CALL_LOG not granted yet; show what we have.
        }
        try
        {
            live.AddRange(SmsLogReader.ReadAll(Host));
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_SMS not granted yet; show what we have.
        }
        return live;
    }

    // Reads one cache page at the given offset and merges it with the live tail and the
    // pages already loaded into an ordered set of list rows. Runs on a background thread.
    //
    // Marketing-classified mail is split out of the merge before the ordinary rows are
    // built (MailboxSections.SplitMarketing) and merged into its own thread list the same
    // way, so the Mailbox's rows never include it — it surfaces only in the marketing
    // section (BuildMarketingSection). This is a visibility split, not a new expiry path:
    // MailAgeing still decides when a marketing item goes, unchanged.
    private (List<TimelineItem> Page, List<ThreadRow> Rows, List<ThreadRow> MarketingRows) ReadPageAndBuild(
        List<TimelineItem> live, List<TimelineItem> priorCache, int offset,
        Dictionary<string, string> titleSeed)
    {
        var page = new List<TimelineItem>();
        IReadOnlyList<ContactThread> merged;
        IReadOnlyList<ContactThread> marketingThreads;
        try
        {
            using var database = TimelineDatabase.Open(Host);
            page.AddRange(database.Cache.LoadRecent(CachePageSize, offset));

            var all = new List<TimelineItem>(live.Count + priorCache.Count + page.Count);
            all.AddRange(live);
            all.AddRange(priorCache);
            all.AddRange(page);

            // Drop items the owner deleted, merge by resolved person identity, then stamp
            // unread from the per-thread read record.
            var visible = database.Hidden.Filter(all);
            var (ordinary, marketing) = MailboxSections.SplitMarketing(visible);
            var resolver = ContactAssociations.BuildResolver(Host, database.IdentityLinks);
            var readState = new ReadState(database.ReadState.LoadAll());
            merged = new TimelineMerger().Merge(ordinary, resolver)
                .Select(readState.Apply)
                .ToList();
            marketingThreads = new TimelineMerger().Merge(marketing, resolver)
                .Select(readState.Apply)
                .ToList();
        }
        catch (Exception e)
        {
            // The timeline cache is a convenience over the live call log; never let a
            // database problem take the screen down — fall back to a plain merge.
            Android.Util.Log.Warn("Post", $"Timeline stores unavailable: {e}");
            PostDiagnostics.LogWarning("Timeline", "Timeline stores unavailable; using plain merge", e);
            var (ordinary, marketing) = MailboxSections.SplitMarketing(live.Concat(priorCache).Concat(page));
            merged = new TimelineMerger().Merge(ordinary);
            marketingThreads = new TimelineMerger().Merge(marketing);
        }

        var rows = InboxOrdering.UnreadFirstThenRecent(merged)
            .Select(thread => BuildRow(thread, titleSeed))
            .ToList();
        var marketingRows = InboxOrdering.UnreadFirstThenRecent(marketingThreads)
            .Select(thread => BuildRow(thread, titleSeed))
            .ToList();
        return (page, rows, marketingRows);
    }

    private ThreadRow BuildRow(ContactThread thread, Dictionary<string, string> titleSeed)
    {
        if (!titleSeed.TryGetValue(thread.ContactKey, out var title))
        {
            title = ThreadTitle(thread);
            titleSeed[thread.ContactKey] = title;
        }
        var latest = thread.Items[^1];
        var subtitle =
            $"{RelativeTime.Format(latest.TimestampUtc.LocalDateTime, DateTime.Now)} · {TimelineItemFormatter.Describe(latest)}";
        return new ThreadRow(thread.ContactKey, title, subtitle, thread.HasUnread);
    }

    private string ThreadTitle(ContactThread thread)
    {
        if (thread.IsGroup)
        {
            var names = thread.Participants.Take(3)
                .Select(key => ContactNameResolver.DisplayName(Host, key));
            var joined = string.Join(", ", names);
            if (joined.Length == 0)
            {
                return "Group";
            }
            return thread.Participants.Count > 3 ? $"{joined} +{thread.Participants.Count - 3}" : joined;
        }
        return ContactNameResolver.DisplayName(Host, thread.ContactKey);
    }

    private void BindRows()
    {
        if (threadList is null)
        {
            return;
        }
        var trimmed = query.Trim();
        shownRows = trimmed.Length == 0
            ? allRows
            : allRows.Where(row =>
                row.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();

        threadList.Adapter = new PaperListAdapter(Host, shownRows
            // A subtle leading dot marks unread (a mark, not a badge).
            .Select(row => new PaperListRow((row.HasUnread ? "• " : "") + row.Title, row.Subtitle))
            .ToList());

        if (filter is not null)
        {
            filter.Visibility = FilterMatch.IsWarranted(allRows.Count) ? ViewStates.Visible : ViewStates.Gone;
        }
    }

    // The empty Mailbox is the one place the owner's voice speaks in Post. The line is
    // redrawn only when the Mailbox is EMPTY, and the pool is told whether anything
    // arrived since the last look — that is what decides whether a ladder climbs or
    // starts over.
    private void RefreshEmptyLine(int rowCount)
    {
        if (rowCount > 0)
        {
            lastSeenRowCount = rowCount;
            return;
        }

        var mailArrived = lastSeenRowCount > 0;
        lastSeenRowCount = 0;
        emptyLine = emptyState.Draw(mailArrived);
        if (emptyView is not null)
        {
            emptyView.Text = emptyLine;
        }
    }

    private void ShowRowActions(ThreadRow row)
    {
        ActionCard.Show(Host, Overlay, row.Title, row.Subtitle,
            new CardAction("File this correspondence", () => FileCorrespondence(row), Lead: true));
    }

    // A correspondence is filed whole: every item in it goes to the archive and every
    // source delete is owed. The owner deals with a PERSON on this screen, not with one
    // message, which is why the Mailbox's unit is the thread and the correspondence
    // screen's is the message.
    private void FileCorrespondence(ThreadRow row)
    {
        var now = DateTimeOffset.UtcNow;
        var filed = ItemsFor(row.ContactKey);
        if (filed.Count == 0)
        {
            return;
        }

        foreach (var item in filed)
        {
            Filing.File(Host, item, now);
        }

        ReloadThreads();
        DogEar.Show(Host, Overlay, $"Filed {row.Title}.", onUndo: () =>
        {
            foreach (var item in filed)
            {
                Filing.Unfile(Host, item.SourceId);
            }
            ReloadThreads();
        });

        // Filing owes a delete at the source. Carry it out as soon as the undo window
        // closes rather than leaving it for the next screen-open: mail that stays in the
        // inbox after being filed is what makes filing look like it does not work.
        Syncs.After(Host, PostAction.Filed);
    }

    private IReadOnlyList<TimelineItem> ItemsFor(string contactKey) =>
        liveItems.Concat(cachedItems)
            .Where(item => item.ContactKey == contactKey)
            .ToList();

    // Brings the cache up to date with the mail account. Success is the refreshed list —
    // no toast, no banner, no "last synced" line. A failure folds into the corner and
    // waits, rather than putting machine text on the page (§13).
    private void SyncInBackground()
    {
        if (syncing)
        {
            return;
        }
        syncing = true;

        Task.Run(async () =>
        {
            string? failure = null;
            try
            {
                await PostSync.RunReconcileAsync(Host);
            }
            catch (Exception e)
            {
                failure = e.Message;
                Android.Util.Log.Warn("Post", $"Sync failed: {e}");
                PostDiagnostics.LogWarning("Sync", "Sync failed", e);
            }

            Host.RunOnUiThread(() =>
            {
                syncing = false;
                DogEar.SetNotice(Host, Overlay, NoticeKeys.Sync, failure is null
                    ? null
                    : new DogEar.Notice(
                        "Your mail did not come through.",
                        "Post could not reach the account. What is already here is still here; it will "
                        + "try again shortly. Diagnostics has the details.",
                        PaperTheme.ProblemMark,
                        "What happened →",
                        () => Host.StartActivity(
                            new Android.Content.Intent(Host, typeof(DiagnosticsActivity)))));

                if (failure is null)
                {
                    ReloadThreads();
                }
            });
        });
    }

    private void SetStatus(string text)
    {
        if (statusLine is null)
        {
            return;
        }
        statusLine.Text = text;
        statusLine.Visibility = ViewStates.Visible;
    }

    private void ClearStatus()
    {
        if (statusLine is null)
        {
            return;
        }
        statusLine.Text = string.Empty;
        statusLine.Visibility = ViewStates.Gone;
    }
}
