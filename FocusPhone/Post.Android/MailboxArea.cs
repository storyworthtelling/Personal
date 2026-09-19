using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Post;
using Codex.Domain.Time;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Email;
using Codex.Ui;
using Codex.Ui.Voice;

namespace Post;

/// <summary>
/// The Mailbox: everything that has arrived and is not yet dealt with, newest first, in
/// one undivided list — calls, texts and email merged into a row per person (§D2/D4).
/// This is Post's home area, so its tab takes the app's identity hue (§9).
///
/// The list has <b>no pinned action bar</b>: what you do here you do to a row, not to
/// the screen. Errors do not appear on the page either — they fold into the dog-ear and
/// wait to be turned to (§13).
///
/// The Mailbox has a second "screen" folded in alongside the thread list: one open
/// correspondence, opened by <see cref="OpenThread"/> and closed by <see cref="GoBack"/>.
/// This used to be a separate, full-screen <c>ThreadActivity</c> with no binder tab bar of
/// its own, which is exactly what made re-tapping the Mailbox tab to return to the list
/// (see <see cref="PostArea"/>'s remarks) dead code — leaving a correspondence meant
/// leaving the Activity that drew the tabs entirely, not "going back" inside one that
/// <see cref="GoBack"/> could ever answer for. Folding it in here makes the tab bar a true
/// visual constant across the whole app and gives <see cref="GoBack"/> something real to do.
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

    // The correspondence's title while one is open (§7's Serif header, drawn by the shell
    // from Title); the Mailbox's own tab label otherwise.
    public override string Title => openContactKey is not null ? openTitle : base.Title;

    public override View BuildView() => openContactKey is not null ? BuildThreadView() : BuildThreadListView();

    private View BuildThreadListView()
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
            OpenThread(shownRows[e.Position].ContactKey);
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
        return column;
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

    public override bool GoBack()
    {
        if (openContactKey is null)
        {
            return false;
        }

        // Choosing is a mode, not a level: back leaves the mode and keeps the
        // correspondence open, exactly as it does for Plate's Darkroom cull.
        if (openSelection.IsActive)
        {
            EndOpenSelection();
            return true;
        }

        CloseThread();
        return true;
    }

    /// <summary>
    /// The area's own actions, offered while the shell's pinned bottom bar would
    /// otherwise sit empty: only while a Choose selection inside the open
    /// correspondence is active. The Mailbox list itself has none — its acts are
    /// per-row — and neither does an open correspondence outside Choose mode, whose
    /// channel actions (Call/Text/Email) are drawn in-page instead.
    /// </summary>
    public override View? BuildBottomBar()
    {
        if (openContactKey is null || !openSelection.IsActive)
        {
            return null;
        }

        return PaperUi.BottomBar(Host,
            PaperUi.QuietButton(Host, "Cancel", EndOpenSelection),
            PaperUi.SecondaryButton(Host,
                openSelection.Count == 0 ? "File" : $"File {openSelection.Count}", FileSelected),
            PaperUi.DestructiveButton(Host,
                openSelection.Count == 0 ? "Delete" : $"Delete {openSelection.Count}", DeleteSelected));
    }

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
            var (page, rows) = ReadPageAndBuild(live, [], 0, titleSeed);
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
                loading = false;
                RefreshEmptyLine(rows.Count);
                BindRows();
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
            var (page, rows) = ReadPageAndBuild(live, prior, offset, titleSeed);
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
                loading = false;
                BindRows();
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
    private (List<TimelineItem> Page, List<ThreadRow> Rows) ReadPageAndBuild(
        List<TimelineItem> live, List<TimelineItem> priorCache, int offset,
        Dictionary<string, string> titleSeed)
    {
        var page = new List<TimelineItem>();
        IReadOnlyList<ContactThread> merged;
        try
        {
            using var database = TimelineDatabase.Open(Host);
            page.AddRange(database.Cache.LoadRecent(CachePageSize, offset));

            var all = new List<TimelineItem>(live.Count + priorCache.Count + page.Count);
            all.AddRange(live);
            all.AddRange(priorCache);
            all.AddRange(page);

            // Drop items the owner deleted (permanently, or still inside a bulk delete's
            // undo window via the shared delete buffer), merge by resolved person identity,
            // then stamp unread from the per-thread read record.
            var visible = deleteBuffer.Filter(database.Hidden.Filter(all));
            var resolver = ContactAssociations.BuildResolver(Host, database.IdentityLinks);
            var readState = new ReadState(database.ReadState.LoadAll());
            merged = new TimelineMerger().Merge(visible, resolver)
                .Select(readState.Apply)
                .ToList();
        }
        catch (Exception e)
        {
            // The timeline cache is a convenience over the live call log; never let a
            // database problem take the screen down — fall back to a plain merge.
            Android.Util.Log.Warn("Post", $"Timeline stores unavailable: {e}");
            PostDiagnostics.LogWarning("Timeline", "Timeline stores unavailable; using plain merge", e);
            merged = new TimelineMerger().Merge(deleteBuffer.Filter(live.Concat(priorCache).Concat(page)));
        }

        var rows = InboxOrdering.UnreadFirstThenRecent(merged)
            .Select(thread => BuildRow(thread, titleSeed))
            .ToList();
        return (page, rows);
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
            new CardAction("File this correspondence", () => FileCorrespondence(row), Lead: true),
            new CardAction("Delete this correspondence", () => DeleteCorrespondence(row), Destructive: true));
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

    // Deleting a correspondence whole is permanent — unlike filing, nothing is kept: no
    // ghost, no archive entry. It reuses the same per-kind delete-at-source routing as a
    // single-item delete (see ScheduleBulkDelete/CommitDelete), just over every item the
    // correspondence has, batched into one fold rather than one per item.
    private void DeleteCorrespondence(ThreadRow row)
    {
        var items = ItemsFor(row.ContactKey);
        if (items.Count == 0)
        {
            return;
        }

        ScheduleBulkDelete(items, $"Deleted {row.Title}.", ReloadThreads);
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
                            new Intent(Host, typeof(DiagnosticsActivity)))));

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

    // ---- Open correspondence ----
    //
    // One person's or group's unified history — texts, calls, voicemails, and email
    // interleaved by time, each row channel-labelled. A pinned action row offers the
    // channels that person has (call/text/email); opening the thread marks it read.
    // Long-pressing an item offers call-back / text-back and an undo-first delete
    // (email → Gmail trash, call/voicemail → call log, text → hidden locally until the
    // bridge can remote-delete). Texting still hands off to Google Messages until the
    // in-app bridge lands.
    //
    // Folded in from the former ThreadActivity: everything below builds and drives the
    // Mailbox's second "screen" rather than a separate Activity, so it shares this area's
    // Overlay (dog-ears/action cards) instead of wiring its own inset host.

    private string? openContactKey;
    private string openTitle = string.Empty;
    private View? openView;
    private ListView? openListView;
    private LinearLayout? openChooserRow;
    private LinearLayout? openActionRow;
    private List<TimelineItem> openItems = [];
    private IReadOnlyList<string> openMemberKeys = [];
    private bool openIsGroup;
    private string? openBridgeConversationId;

    // Which items are picked while "Choose" is on, inside the open correspondence. Bulk
    // file/delete reuse the same Selection type Score's picker screens use — pick order
    // does not matter here, but the checkbox/wash rendering and All/None toggle do.
    private readonly Selection openSelection = new();

    // Shared by the whole-correspondence delete (thread list) and the per-item delete —
    // single or Choose-mode bulk — inside the open correspondence: an item scheduled here
    // disappears from BOTH views at once (see ReadPageAndBuild/BindOpenThread), because a
    // delete made from either screen should read as done everywhere, not just where it
    // was made.
    private readonly DeleteBuffer deleteBuffer = new();
    private readonly Handler deleteHandler = new(Looper.MainLooper!);
    private const int PickBridgePhoto = 71;
    private string? pendingPhotoConversationId;
    private string? pendingPhotoNumber;

    /// <summary>
    /// Opens one correspondence in-frame — called both from a tap on this area's own list
    /// and, via <see cref="PostActivity.OpenCorrespondence"/>, from outside the area
    /// entirely (a notification tap, the SMS role's SENDTO handler).
    /// </summary>
    public void OpenThread(string contactKey)
    {
        openContactKey = contactKey;
        openTitle = ContactNameResolver.DisplayName(Host, contactKey);
        openView = null;
        openListView = null;
        openChooserRow = null;
        openActionRow = null;
        openItems = [];
        openMemberKeys = [];
        openIsGroup = false;
        openBridgeConversationId = null;
        openSelection.Cancel();
        Invalidate();
        LoadOpenThread();
        ShowAnyProposedDate();
    }

    // Closing hands the Mailbox back to the list — and, since nothing else brings this
    // area's Resume() around a mid-session close the way returning from a separate Activity
    // used to, refreshes it here so filing/deleting/sending inside the correspondence is
    // reflected the moment the owner is back looking at the list.
    private void CloseThread()
    {
        openContactKey = null;
        openView = null;
        openListView = null;
        openChooserRow = null;
        openActionRow = null;
        openItems = [];
        openMemberKeys = [];
        openIsGroup = false;
        openBridgeConversationId = null;
        openSelection.Cancel();
        DogEar.SetNotice(Host, Overlay, NoticeKeys.ProposedDate, null);
        DogEar.SetNotice(Host, Overlay, NoticeKeys.Block, null);
        ReloadThreads();
        Invalidate();
    }

    // Built once per open and then updated in place (see BindOpenThread) rather than
    // rebuilt on every load, so the list keeps its scroll position the way the original
    // Activity's did.
    private View BuildThreadView()
    {
        if (openView is not null)
        {
            return openView;
        }

        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        // The way into Choose mode, and — once in it — the running count and an
        // All/None toggle over what the correspondence holds. Built once here and
        // repainted in place by BuildChooserRow, same as the channel-action row below.
        openChooserRow = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        openChooserRow.SetGravity(GravityFlags.CenterVertical);
        var chooserVertical = PaperTheme.Dp(Host, DesignTokens.Space.X3);
        openChooserRow.SetPadding(0, chooserVertical, 0, chooserVertical);
        column.AddView(openChooserRow, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        openListView = new ListView(Host);
        PaperUi.StylePaperList(Host, openListView);
        openListView.ItemLongClick += (_, e) =>
        {
            if (e.Position < 0 || e.Position >= openItems.Count)
            {
                return;
            }
            if (openSelection.IsActive)
            {
                // Already choosing — a long-press is just another pick.
                openSelection.Toggle(openItems[e.Position].SourceId);
                RefreshOpenView();
                return;
            }
            ShowItemActions(openItems[e.Position]);
        };

        // Email in a stream is a subject and a snippet; the message itself is its own page.
        // While choosing, a tap picks and nothing opens — the list is one control for as
        // long as the mode is on.
        openListView.ItemClick += (_, e) =>
        {
            if (e.Position < 0 || e.Position >= openItems.Count)
            {
                return;
            }
            if (openSelection.IsActive)
            {
                openSelection.Toggle(openItems[e.Position].SourceId);
                RefreshOpenView();
                return;
            }
            var tapped = openItems[e.Position];
            if (tapped.Kind != TimelineItemKind.Email)
            {
                return;
            }
            var reader = new Intent(Host, typeof(MailReaderActivity));
            reader.PutExtra(MailReaderActivity.ExtraSourceId, tapped.SourceId);
            Host.StartActivity(reader);
        };
        column.AddView(openListView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        openActionRow = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        var vertical = PaperTheme.Dp(Host, DesignTokens.Space.X4);
        openActionRow.SetPadding(0, vertical, 0, vertical);
        column.AddView(openActionRow, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        openView = column;
        BindOpenThread(null);
        return openView;
    }

    // Gathering the live logs and merging the cache is I/O-heavy, so it runs off
    // the UI thread; only the bind touches views. Refreshes (send, delete, undo)
    // call this and let the list update a moment later.
    private void LoadOpenThread()
    {
        var contactKey = openContactKey;
        if (contactKey is null)
        {
            return;
        }

        Task.Run(() =>
        {
            var gathered = new List<TimelineItem>();
            try
            {
                gathered.AddRange(CallLogReader.ReadAll(Host));
            }
            catch (Java.Lang.SecurityException)
            {
            }
            try
            {
                gathered.AddRange(SmsLogReader.ReadAll(Host));
            }
            catch (Java.Lang.SecurityException)
            {
            }

            ContactThread? thread = null;
            try
            {
                using var database = TimelineDatabase.Open(Host);
                gathered.AddRange(database.Cache.LoadAll());

                var visible = database.Hidden.Filter(gathered);
                var resolver = ContactAssociations.BuildResolver(Host, database.IdentityLinks);
                thread = new TimelineMerger().Merge(visible, resolver)
                    .FirstOrDefault(candidate => candidate.ContactKey == contactKey);

                if (thread is not null)
                {
                    // Opening a thread is seeing it: stamp read so it drops out of the
                    // inbox's unread section on return.
                    database.ReadState.MarkRead(thread.ContactKey, DateTimeOffset.Now);
                }
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("FocusPhoneComm", $"Timeline stores unavailable: {e}");
                PostDiagnostics.LogWarning("Timeline", "Timeline stores unavailable; using plain merge", e);
                thread = new TimelineMerger().Merge(gathered)
                    .FirstOrDefault(candidate => candidate.ContactKey == contactKey);
            }

            var loaded = thread;
            Host.RunOnUiThread(() =>
            {
                if (openContactKey == contactKey)
                {
                    BindOpenThread(loaded);
                }
            });
        });
    }

    private void BindOpenThread(ContactThread? thread)
    {
        if (openContactKey is null)
        {
            return; // Closed while the background load was still in flight.
        }

        openMemberKeys = thread?.MemberKeys ?? [];
        openIsGroup = thread?.IsGroup ?? false;
        openTitle = OpenThreadTitle(thread);

        // Hide anything the owner just deleted (still inside its undo window, single or
        // bulk — see the shared deleteBuffer's remarks).
        openItems = deleteBuffer.Filter(thread?.Items ?? []).ToList();
        // A pick whose item filed, deleted, or aged out from under it is not a pick
        // anymore — drop it rather than let the count and the fold disagree.
        openSelection.RetainKnown(openItems.Select(item => item.SourceId));
        // The conversation id to reply through the bridge (learned from the most
        // recent bridge-captured message in this thread).
        openBridgeConversationId =
            openItems.LastOrDefault(item => item.ExternalConversationId is not null)?.ExternalConversationId;

        if (openListView is not null)
        {
            openListView.Adapter = new PaperListAdapter(Host, BuildOpenRows(), openSelection.IsActive);
        }

        // The shell reads Title fresh only on a render, so a rename mid-load (the contact
        // resolved, or a group's members changed) needs its own header repaint.
        if (Host is PostActivity postActivity)
        {
            postActivity.RefreshHeader();
        }

        BuildChooserRow();
        BuildActionRow();
    }

    private List<PaperListRow> BuildOpenRows() => openItems
        .Select(item => new PaperListRow(
            TimelineItemFormatter.Describe(item),
            $"{(item.Incoming ? "←" : "→")} {RelativeTime.Format(item.TimestampUtc.LocalDateTime, DateTime.Now)}",
            Picked: openSelection.Contains(item.SourceId)))
        .ToList();

    // Repaints the list, the chooser row, and the channel-action row in place after a
    // local change that does not need a fresh read from the source — a pick, an
    // All/None toggle, ending the mode. LoadOpenThread re-reads calls/texts/cache, which
    // a mere selection change never needs.
    private void RefreshOpenView()
    {
        if (openListView is not null)
        {
            openListView.Adapter = new PaperListAdapter(Host, BuildOpenRows(), openSelection.IsActive);
        }
        BuildChooserRow();
        BuildActionRow();
        // The shell's pinned bottom bar (File N / Delete N) is only rebuilt on a render.
        Invalidate();
    }

    // The way into Choose mode, and — once in it — what the picker controls offer above
    // the list: the running count and an All/None toggle. The bulk actions themselves live
    // in the shell's pinned bottom bar (see BuildBottomBar), matching DESIGN.md §7.
    private void BuildChooserRow()
    {
        if (openChooserRow is null)
        {
            return;
        }
        openChooserRow.RemoveAllViews();

        if (!openSelection.IsActive)
        {
            openChooserRow.AddView(PaperUi.QuietButton(Host, "Choose", () =>
            {
                openSelection.Begin();
                RefreshOpenView();
            }));
            return;
        }

        openChooserRow.AddView(
            PaperUi.Secondary(Host, openSelection.Count == 0 ? "Nothing chosen" : $"{openSelection.Count} chosen"),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var ids = openItems.Select(item => item.SourceId).ToList();
        openChooserRow.AddView(PaperUi.QuietButton(Host, openSelection.AllSelected(ids) ? "None" : "All", () =>
        {
            openSelection.ToggleAll(ids);
            RefreshOpenView();
        }));
    }

    private void EndOpenSelection()
    {
        openSelection.Cancel();
        RefreshOpenView();
    }

    // The long-press door into Choose mode, entered already holding the item that was
    // pressed — the way it actually begins: you see the one you meant first.
    private void BeginChoosingWith(TimelineItem item)
    {
        openSelection.Begin(item.SourceId);
        RefreshOpenView();
    }

    // Bulk-files everything picked, exactly as filing a whole correspondence does for
    // every one of its items: the ghost is kept and the source delete is owed per item,
    // batched into one fold rather than one per item.
    private void FileSelected()
    {
        var picked = new HashSet<string>(openSelection.Items);
        var items = openItems.Where(item => picked.Contains(item.SourceId)).ToList();
        if (items.Count == 0)
        {
            return;
        }

        openSelection.Cancel();
        var now = DateTimeOffset.UtcNow;
        foreach (var item in items)
        {
            Filing.File(Host, item, now);
        }
        LoadOpenThread();

        DogEar.Show(Host, Overlay, CountMessage(items.Count, "filed"), onUndo: () =>
        {
            foreach (var item in items)
            {
                Filing.Unfile(Host, item.SourceId);
            }
            LoadOpenThread();
        });

        Syncs.After(Host, PostAction.Filed);
    }

    // Bulk-deletes everything picked, permanently — unlike filing, nothing is kept.
    private void DeleteSelected()
    {
        var picked = new HashSet<string>(openSelection.Items);
        var items = openItems.Where(item => picked.Contains(item.SourceId)).ToList();
        if (items.Count == 0)
        {
            return;
        }

        openSelection.Cancel();
        ScheduleBulkDelete(items, CountMessage(items.Count, "deleted"), LoadOpenThread);
    }

    private static string CountMessage(int count, string verb) => count == 1 ? $"1 {verb}." : $"{count} {verb}.";

    // Schedules a batch of items for undo-first delete (§7): every item disappears from
    // whichever view is watching at once via the shared deleteBuffer, and the fold
    // restores the whole batch together rather than one item at a time. Once the window
    // closes, each item is deleted at the source through the same per-kind routing
    // (CommitDelete) a single-item delete already uses.
    private void ScheduleBulkDelete(IReadOnlyList<TimelineItem> items, string message, params Action[] refresh)
    {
        var now = DateTimeOffset.Now;
        var window = TimeSpan.FromMilliseconds(DesignTokens.Motion.UndoWindowMs);
        foreach (var item in items)
        {
            deleteBuffer.Schedule(item, now, window);
        }
        foreach (var action in refresh)
        {
            action();
        }

        DogEar.Show(Host, Overlay, message, onUndo: () =>
        {
            foreach (var item in items)
            {
                deleteBuffer.Undo(item.Kind, item.SourceId);
            }
            foreach (var action in refresh)
            {
                action();
            }
        });

        deleteHandler.PostDelayed(() =>
        {
            foreach (var item in items)
            {
                // Commit only what was not undone in the meantime.
                if (deleteBuffer.Undo(item.Kind, item.SourceId))
                {
                    CommitDelete(item);
                }
            }
        }, DesignTokens.Motion.UndoWindowMs);
    }

    private string OpenThreadTitle(ContactThread? thread)
    {
        if (thread is null)
        {
            return ContactNameResolver.DisplayName(Host, openContactKey!);
        }
        if (thread.IsGroup)
        {
            var names = thread.Participants.Take(3).Select(key => ContactNameResolver.DisplayName(Host, key));
            var joined = string.Join(", ", names);
            return joined.Length == 0 ? "Group" : joined;
        }
        return ContactNameResolver.DisplayName(Host, thread.ContactKey);
    }

    private void BuildActionRow()
    {
        if (openActionRow is null)
        {
            return;
        }
        openActionRow.RemoveAllViews();
        if (openIsGroup)
        {
            return; // Group replies need the bridge; no per-channel deep link yet.
        }
        if (openSelection.IsActive)
        {
            return; // Choosing takes over the row's job; the channel actions come back when it ends.
        }

        var phone = openMemberKeys.FirstOrDefault(IsPhone) ?? (IsPhone(openContactKey!) ? openContactKey : null);
        var email = openMemberKeys.FirstOrDefault(IsEmail) ?? (IsEmail(openContactKey!) ? openContactKey : null);

        if (phone is not null)
        {
            AddAction("Call", () => PlaceCall(phone));
            AddAction("Text", () => SendText(phone));
        }
        if (email is not null)
        {
            AddAction("Email", () => EmailComposeDialog.Show(Host, email, openTitle));
        }
    }

    private void AddAction(string label, Action onClick)
    {
        var button = PaperUi.SecondaryButton(Host, label, onClick);
        openActionRow!.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent)
        {
            Weight = 1,
            MarginEnd = PaperTheme.Dp(Host, DesignTokens.Space.X3),
        });
    }

    // Long-press: what can be done to this one message. The shared action card rather than
    // a dialog — §7 makes the card the app's only overflow, and it is never a confirm
    // step: everything here does something, and the destructive things are undone from the
    // corner rather than asked about first.
    private void ShowItemActions(TimelineItem item)
    {
        var actions = new List<CardAction>();

        var phone = openMemberKeys.FirstOrDefault(IsPhone) ?? (IsPhone(openContactKey!) ? openContactKey : null);
        if (phone is not null && !openIsGroup)
        {
            actions.Add(new CardAction("Call back", () => PlaceCall(phone)));
            actions.Add(new CardAction("Text back", () => SendText(phone), Lead: true));
        }

        var email = openMemberKeys.FirstOrDefault(IsEmail) ?? (IsEmail(openContactKey!) ? openContactKey : null);
        if (item.Kind == TimelineItemKind.Email && email is not null)
        {
            actions.Add(new CardAction("Reply", () => EmailComposeDialog.Show(
                Host, email, openTitle, ReplySubject(item.Subject), item.MessageId),
                Lead: true));
        }

        if (item.Kind == TimelineItemKind.Call && phone is not null)
        {
            actions.Add(new CardAction("Write a note on this call", () => WriteCallNote(phone)));
        }

        actions.Add(new CardAction("File", () => FileWithUndo(item)));
        actions.Add(new CardAction("Delete", () => DeleteWithUndo(item), Destructive: true));
        // How Choose actually begins: you see the one you meant first, exactly as a
        // long-press starts Plate's Darkroom cull with that picture already picked.
        actions.Add(new CardAction("Choose", () => BeginChoosingWith(item)));

        if (phone is not null && !openIsGroup)
        {
            actions.Add(new CardAction("Block this number", () => Block(phone), Destructive: true));
        }

        ActionCard.Show(Host, Overlay, openTitle, TimelineItemFormatter.Describe(item), actions.ToArray());
    }

    // The owner's own note on a call, since a transcript is not deliverable on Android.
    // It asks for a summary and nothing else — no lead-in explaining why, because the
    // owner already knows why and reads it every time.
    private void WriteCallNote(string number)
    {
        var input = PaperUi.Field(Host, "A summary, for future reference");
        new AlertDialog.Builder(Host)
            .SetTitle("Note on this call")!
            .SetView(input)!
            .SetPositiveButton("Keep", (_, _) =>
            {
                var text = (input.Text ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    return;
                }
                using var database = TimelineDatabase.Open(Host);
                database.Cache.Upsert([CallNote.For(number, text, DateTimeOffset.UtcNow)]);
                LoadOpenThread();
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    // Blocking is handed to Android's own blocked-numbers list rather than kept here, so
    // it applies everywhere on the phone instead of only inside Post — a number blocked in
    // one app and ringing through in another is worse than not blocking it.
    private void Block(string number)
    {
        try
        {
            var values = new ContentValues();
            values.Put(Android.Provider.BlockedNumberContract.BlockedNumbers.ColumnOriginalNumber, number);
            Host.ContentResolver!.Insert(
                Android.Provider.BlockedNumberContract.BlockedNumbers.ContentUri!, values);

            DogEar.Show(Host, Overlay, $"Blocked {number}.", onUndo: () => Unblock(number));
        }
        catch (Exception e)
        {
            // Only the default SMS or dialer app may write the blocked list. Post holds the
            // SMS role, so this should work — but if it does not, say so rather than
            // leaving the owner believing a number is blocked when it is not.
            Android.Util.Log.Warn("Post", $"Could not block {number}: {e}");
            PostDiagnostics.LogWarning("Block", $"Could not block {number}", e);
            DogEar.SetNotice(Host, Overlay, NoticeKeys.Block, new DogEar.Notice(
                "That number was not blocked.",
                "The phone would not accept it. Blocking is the system's list, not Post's, and it "
                + "refused — so calls from this number will still come through.",
                PaperTheme.ProblemMark));
        }
    }

    private void Unblock(string number)
    {
        try
        {
            Host.ContentResolver!.Delete(
                Android.Provider.BlockedNumberContract.BlockedNumbers.ContentUri!,
                $"{Android.Provider.BlockedNumberContract.BlockedNumbers.ColumnOriginalNumber} = ?",
                [number]);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("Block", $"Could not unblock {number}", e);
        }
    }

    // Filing one message: the ghost is kept and the source delete is owed. The correspondence
    // screen's unit is the message; the Mailbox's is the whole correspondence.
    private void FileWithUndo(TimelineItem item)
    {
        Filing.File(Host, item, DateTimeOffset.UtcNow);
        LoadOpenThread();

        DogEar.Show(Host, Overlay, $"Filed {ChannelWord(item)}.", onUndo: () =>
        {
            Filing.Unfile(Host, item.SourceId);
            LoadOpenThread();
        });

        Syncs.After(Host, PostAction.Filed);
    }

    // A date this correspondence proposed, waiting behind the fold. Offered, never written
    // silently and never always-asked: the owner decides, and Post says what the day
    // already holds so the decision is worth something.
    private void ShowAnyProposedDate()
    {
        var contactKey = openContactKey;
        if (contactKey is null)
        {
            return;
        }

        var offer = ProposedDates.For(Host, contactKey);
        if (offer is null)
        {
            DogEar.SetNotice(Host, Overlay, NoticeKeys.ProposedDate, null);
            return;
        }

        var when = offer.Event.StartsAt is { } time
            ? $"{offer.Event.StartsOn:ddd d MMM} at {time:h:mm tt}"
            : $"{offer.Event.StartsOn:ddd d MMM}, all day";

        var detail = offer.Clashed switch
        {
            true => $"{when}. Something else is already in that day.",
            false => $"{when}. The day is clear.",
            null => $"{when}. Post could not read your calendar, so the day has not been checked.",
        };

        DogEar.SetNotice(Host, Overlay, NoticeKeys.ProposedDate, new DogEar.Notice(
            offer.Event.Title,
            detail,
            offer.Clashed == true ? PaperTheme.CautionMark : PaperTheme.PostMark,
            "Put it in the calendar",
            () => AddToCalendar(offer.Event)));
    }

    private void AddToCalendar(ProposedEvent proposal)
    {
        var contactKey = openContactKey;
        try
        {
            if (CalendarAccess.Write(Host, proposal))
            {
                if (contactKey is not null)
                {
                    ProposedDates.Clear(Host, contactKey);
                }
                ShowAnyProposedDate();
                FieldNotesStore.Record(Host, notes => notes.WithCalendarWrite(written: true));
                return;
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("Post", $"Could not write a calendar event: {e}");
            PostDiagnostics.LogWarning("Calendar", "Could not write the event", e);
        }

        // Reported rather than swallowed. Believing something is in the calendar when it is
        // not is the one outcome worse than never having been offered it.
        DogEar.SetNotice(Host, Overlay, NoticeKeys.ProposedDate, new DogEar.Notice(
            "That did not reach your calendar.",
            "Post could not write to it — there may be no calendar it is allowed to add to. "
            + "The date is still here; nothing was lost.",
            PaperTheme.ProblemMark));
        FieldNotesStore.Record(Host, notes => notes.WithCalendarWrite(written: false));
    }

    private void DeleteWithUndo(TimelineItem item) =>
        ScheduleBulkDelete([item], $"Deleted {ChannelWord(item)}.", LoadOpenThread);

    // Performs the irreversible, channel-specific delete once the undo window closes.
    private void CommitDelete(TimelineItem item)
    {
        Task.Run(() =>
        {
            try
            {
                switch (item.Kind)
                {
                    case TimelineItemKind.Email:
                        DeleteEmail(item);
                        break;
                    case TimelineItemKind.Call:
                    case TimelineItemKind.Voicemail:
                        DeleteCallOrVoicemail(item);
                        break;
                    default:
                        DeleteText(item);
                        break;
                }
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("FocusPhoneComm", $"Delete of {item.Kind}:{item.SourceId} failed: {e}");
                PostDiagnostics.LogWarning("Delete", $"Delete of {item.Kind}:{item.SourceId} failed", e);
            }
        });
    }

    private void DeleteEmail(TimelineItem item)
    {
        var account = new AccountStore(Host);
        if (item.MessageId is not null)
        {
            switch (MailRoute.Choose(VoiceGoogleAccount.IsSignedIn, account.IsConfigured))
            {
                case MailTransport.GmailApi:
                    // A real trash, recoverable for 30 days — the same delete the Gmail app
                    // performs, rather than IMAP's move into a folder that happens to be named
                    // Trash. This is what makes filing safe enough to be a first-class verb.
                    MailAccess.Api(Host, account).TrashAsync(item.MessageId).GetAwaiter().GetResult();
                    break;
                case MailTransport.AppPassword:
                    new GmailClient(account.GmailAddress!, account.GmailAppPassword!)
                        .DeleteAsync(item.MessageId).GetAwaiter().GetResult();
                    break;
            }
        }
        using var database = TimelineDatabase.Open(Host);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    // Deleting a text for real depends on where it came from, and the routing is decided
    // by pure tested logic rather than here. Holding the SMS role is what unlocks the
    // provider: only the default app may write to it, which is exactly why Post takes it.
    private void DeleteText(TimelineItem item)
    {
        var holdsRole = SmsRoleAccess.Status(Host) == SmsRoleStatus.Held;
        switch (TextDelete.Choose(item.SourceId, holdsRole, GmBridgeClient.Shared(Host).IsConnected))
        {
            case TextDeletePath.SmsProvider:
                DeleteProviderRow(Android.Provider.Telephony.Sms.ContentUri!, item, "sms:");
                return;
            case TextDeletePath.MmsProvider:
                DeleteProviderRow(Android.Provider.Telephony.Mms.ContentUri!, item, "mms:");
                return;
            case TextDeletePath.Bridge:
                var messageId = item.SourceId["gm:".Length..];
                if (!messageId.Contains(':') && GmBridgeClient.Shared(Host).TryDelete(messageId))
                {
                    using var database = TimelineDatabase.Open(Host);
                    database.Cache.Delete(item.Kind, item.SourceId);
                    return;
                }
                break;
        }

        HideLocally(item);
    }

    // Removes the row the timeline item came from. The provider is the phone's own record
    // of the owner's texts, so this is the delete that actually means something — every
    // other app that reads it sees the message gone too.
    private void DeleteProviderRow(Android.Net.Uri contentUri, TimelineItem item, string prefix)
    {
        var rowId = item.SourceId[prefix.Length..];
        Host.ContentResolver!.Delete(contentUri, "_id = ?", [rowId]);
        using var database = TimelineDatabase.Open(Host);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    private void DeleteCallOrVoicemail(TimelineItem item)
    {
        if (item.SourceId.All(char.IsAsciiDigit))
        {
            // A live call-log row: remove it at the source (needs WRITE_CALL_LOG).
            // "_id" is the stable BaseColumns row-id name for the call-log provider.
            Host.ContentResolver!.Delete(Android.Provider.CallLog.Calls.ContentUri!,
                "_id = ?", [item.SourceId]);
        }
        using var database = TimelineDatabase.Open(Host);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    private void HideLocally(TimelineItem item)
    {
        using var database = TimelineDatabase.Open(Host);
        database.Hidden.Hide(item.Kind, item.SourceId);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    private void PlaceCall(string number) =>
        Host.StartActivity(new Intent(Intent.ActionCall, Android.Net.Uri.Parse($"tel:{number}")));

    // The bridge first (the only route that carries RCS and full-quality pictures), then
    // Post sending the SMS itself as role holder, then — only if neither is possible —
    // handing the owner to Google Messages. Leaving Post to answer someone is the failure
    // this app is measured against, so the deep link is genuinely last.
    private void SendText(string number)
    {
        var holdsRole = SmsRoleAccess.Status(Host) == SmsRoleStatus.Held;
        switch (TextSend.Choose(
            GmBridgeClient.Shared(Host).IsConnected, openBridgeConversationId is not null, holdsRole))
        {
            case TextSendPath.Bridge:
                ShowBridgeCompose(openBridgeConversationId!, number);
                return;
            case TextSendPath.NativeSms:
                ShowNativeCompose(number);
                return;
            default:
                Host.StartActivity(new Intent(Intent.ActionSendto, Android.Net.Uri.Parse($"smsto:{number}")));
                return;
        }
    }

    // Post sends the text itself. As role holder it is also responsible for the record, so
    // the sent message is written into the provider — Android does not do it for us, and a
    // sent text missing from the phone's own store is a text the owner cannot find later.
    private void ShowNativeCompose(string number)
    {
        var input = new EditText(Host) { Hint = "Message" };
        new AlertDialog.Builder(Host)
            .SetTitle("Text")!
            .SetView(input)!
            .SetPositiveButton("Send", (_, _) =>
            {
                var text = input.Text ?? string.Empty;
                if (text.Length == 0)
                {
                    return;
                }
                try
                {
                    SmsSender.Send(Host, number, text);
                    LoadOpenThread();
                    Syncs.After(Host, PostAction.Sent);
                }
                catch (Exception e)
                {
                    Android.Util.Log.Warn("Post", $"Native SMS send failed: {e}");
                    PostDiagnostics.LogWarning("Sms", "Sending a text failed", e);
                    DogEar.ShowNotices(Host, Overlay,
                    [
                        new DogEar.Notice("That text did not go out.",
                            "Post holds the texting role but the send failed. It is still in the box below.",
                            PaperTheme.ProblemMark),
                    ]);
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void ShowBridgeCompose(string conversationId, string number)
    {
        var input = new EditText(Host) { Hint = "Message" };
        new AlertDialog.Builder(Host)
            .SetTitle("Text")!
            .SetView(input)!
            .SetPositiveButton("Send", (_, _) =>
            {
                var text = input.Text ?? "";
                if (text.Length == 0)
                {
                    return;
                }
                if (GmBridgeClient.Shared(Host).TrySend(conversationId, text))
                {
                    EchoOutgoing(conversationId, number, text);
                    LoadOpenThread();
                    Syncs.After(Host, PostAction.Sent);
                }
                else
                {
                    Toast.MakeText(Host, "Couldn't send — opening Messages.", ToastLength.Short)!.Show();
                    Host.StartActivity(new Intent(Intent.ActionSendto, Android.Net.Uri.Parse($"smsto:{number}")));
                }
            })!
            .SetNeutralButton("Photo", (_, _) => PickPhotoForBridge(conversationId, number))!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    // Picture text: pick an image and send it through the bridge; the result
    // arrives in OnActivityResult (the dialog is already gone by then).
    private void PickPhotoForBridge(string conversationId, string number)
    {
        pendingPhotoConversationId = conversationId;
        pendingPhotoNumber = number;
        var intent = new Intent(Intent.ActionGetContent);
        intent.SetType("image/*");
        intent.AddCategory(Intent.CategoryOpenable);
        Host.StartActivityForResult(Intent.CreateChooser(intent, "Photo"), PickBridgePhoto);
    }

    public override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != PickBridgePhoto || resultCode != Result.Ok || data?.Data is null
            || pendingPhotoConversationId is null)
        {
            return;
        }

        var conversationId = pendingPhotoConversationId;
        var number = pendingPhotoNumber ?? "";
        var mime = Host.ContentResolver!.GetType(data.Data) ?? "image/jpeg";
        var bytes = ReadBytes(data.Data);
        pendingPhotoConversationId = null;
        pendingPhotoNumber = null;
        if (bytes is null)
        {
            return;
        }

        Task.Run(() =>
        {
            if (GmBridgeClient.Shared(Host).TrySendMedia(conversationId, bytes, "photo", mime))
            {
                Host.RunOnUiThread(() =>
                {
                    EchoOutgoing(conversationId, number, "📷 Photo");
                    LoadOpenThread();
                });
            }
            else
            {
                Host.RunOnUiThread(() => Toast.MakeText(Host, "Couldn't send photo.", ToastLength.Short)!.Show());
            }
        });
    }

    private byte[]? ReadBytes(Android.Net.Uri uri)
    {
        try
        {
            using var input = Host.ContentResolver!.OpenInputStream(uri);
            if (input is null)
            {
                return null;
            }
            using var memory = new MemoryStream();
            input.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Reading photo failed: {e}");
            PostDiagnostics.LogWarning("Photo", "Reading photo attachment failed", e);
            return null;
        }
    }

    // The bridge only reports incoming messages, so record the sent one locally
    // (keyed to the person) to reflect it in the thread immediately.
    private void EchoOutgoing(string conversationId, string number, string text)
    {
        try
        {
            using var database = TimelineDatabase.Open(Host);
            database.Cache.Upsert([new TimelineItem(
                TimelineItemKind.Message,
                ContactKeyNormalizer.NormalizePhone(number),
                DateTimeOffset.UtcNow, text, Incoming: false,
                SourceId: $"gmout:{conversationId}:{DateTimeOffset.UtcNow.Ticks}")
            {
                ExternalConversationId = conversationId,
            }]);
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Outgoing echo failed: {e}");
            PostDiagnostics.LogWarning("Bridge", "Recording sent message locally failed", e);
        }
    }

    private static string ReplySubject(string? subject)
    {
        var trimmed = (subject ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return "Re:";
        }
        return trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? trimmed : $"Re: {trimmed}";
    }

    private static string ChannelWord(TimelineItem item) => item.Kind switch
    {
        TimelineItemKind.Email => "email",
        TimelineItemKind.Call => "call",
        TimelineItemKind.Voicemail => "voicemail",
        _ => "text",
    };

    private static bool IsPhone(string key) => key.Length > 0 && key.All(char.IsAsciiDigit);

    private static bool IsEmail(string key) => key.Contains('@');
}
