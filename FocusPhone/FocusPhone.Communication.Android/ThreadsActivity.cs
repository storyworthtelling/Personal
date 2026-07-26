using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Time;
using FocusPhone.Domain.Timeline;
using FocusPhone.Ui;

namespace FocusPhone.Communication;

/// <summary>
/// The main screen: every conversation (calls, texts, email) merged into one
/// list of per-contact threads, most recent first. A header exposes Settings
/// (always reachable), and an empty-state message guides setup when there is
/// no data yet.
/// </summary>
[Activity(
    Label = "Communication",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class ThreadsActivity : Activity
{
    // The timeline cache is read a page at a time (newest first) so a large
    // history never has to be pulled into memory at once; more pages load as the
    // owner scrolls. Live call/SMS rows are the small recent tail and come in
    // whole on each reload.
    private const int CachePageSize = 300;
    private const int LoadMoreThreshold = 30;

    private ListView? threadList;
    private TextView? emptyView;
    private TextView? statusLine;
    private EditText? filterField;
    private Button? syncButton;

    // Owner's-voice greeting under the header (sign-in lives on the Settings screen).
    private readonly FocusPhone.Ui.Voice.VoiceGoogleAccount voiceAccount = new();
    private FocusPhone.Ui.Voice.VoiceGreeting? greeting;
    private TextView? greetingView;

    // Accumulated inputs, only ever mutated on the UI thread once a background
    // load returns; the merge/title work itself runs off the UI thread.
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

    // A prepared list row: identity to open, plus the strings the list renders.
    // Titles are resolved off the UI thread (a Contacts query each), so binding
    // and filtering stay cheap.
    private sealed record ThreadRow(string ContactKey, string Title, string Subtitle, bool HasUnread);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        greeting = new FocusPhone.Ui.Voice.VoiceGreeting(voiceAccount.GetAccessToken, "FocusPhone.Communication");
        SetContentView(BuildLayout());

        threadList!.ItemClick += (_, e) =>
        {
            if (e.Position < 0 || e.Position >= shownRows.Count)
            {
                return;
            }
            var intent = new Intent(this, typeof(ThreadActivity));
            intent.PutExtra(ThreadActivity.ExtraContactKey, shownRows[e.Position].ContactKey);
            StartActivity(intent);
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

        RequestRuntimePermissions();

        try
        {
            EmailSyncWorker.Schedule(this);
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Could not schedule email sync: {e}");
            CommunicationDiagnostics.LogWarning("EmailSync", "Could not schedule email sync", e);
        }

        CrashReporter.ShowPendingCrash(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        ReloadThreads();
        ShowGreetingAsync();
    }

    // A greeting in the owner's voice under the header, shown only when signed in (Settings) and a shared
    // profile + key exist; hidden otherwise. Best-effort, never throws.
    private async void ShowGreetingAsync()
    {
        if (greeting is null || greetingView is null || !FocusPhone.Ui.Voice.VoiceGoogleAccount.IsSignedIn)
            return;
        var message = await greeting.ComposeAsync("welcome", "the owner is opening their messages", CancellationToken.None);
        if (string.IsNullOrWhiteSpace(message))
            return;
        greetingView.Text = message;
        greetingView.Visibility = ViewStates.Visible;
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Space.X4), PaperTheme.Dp(this, DesignTokens.Space.X8),
            PaperTheme.Dp(this, DesignTokens.Space.X3), PaperTheme.Dp(this, DesignTokens.Space.X3));
        header.SetGravity(GravityFlags.CenterVertical);

        var title = PaperUi.Display(this, "Communication");
        header.AddView(title,
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var newConversation = PaperUi.SecondaryButton(this, "New", ShowNewConversationDialog);
        header.AddView(newConversation);

        // Bring the cache up to date with Gmail (and drain the bridge), dropping
        // messages deleted at the source so they stop cluttering the list.
        syncButton = PaperUi.QuietButton(this, "Sync", SyncNow);
        header.AddView(syncButton);

        var settings = PaperUi.QuietButton(this, "Settings", () =>
            StartActivity(new Intent(this, typeof(SettingsActivity))));
        header.AddView(settings);

        root.AddView(header, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        greetingView = PaperUi.Secondary(this, string.Empty);
        greetingView.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Space.X4), 0, PaperTheme.Dp(this, DesignTokens.Space.X4),
            PaperTheme.Dp(this, DesignTokens.Space.X3));
        greetingView.Visibility = ViewStates.Gone;
        root.AddView(greetingView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        // Lightweight find-a-person filter (names only, no full-text search).
        filterField = PaperUi.Field(this, "Find a person");
        filterField.TextChanged += (_, _) => BindRows(filterField.Text ?? "");
        var filterParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var edge = PaperTheme.Dp(this, DesignTokens.Space.X4);
        filterParams.SetMargins(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X2));
        root.AddView(filterField, filterParams);

        // A quiet status sentence for loading/sync (DESIGN.md §8: a sentence, not
        // a spinner). Hidden when idle so it takes no space.
        statusLine = PaperUi.Secondary(this, "");
        statusLine.Visibility = ViewStates.Gone;
        var statusParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        statusParams.SetMargins(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X2));
        root.AddView(statusLine, statusParams);

        var frame = new FrameLayout(this);
        threadList = new ListView(this);
        PaperUi.StylePaperList(this, threadList);
        frame.AddView(threadList, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        emptyView = PaperUi.EmptyState(this, "");
        var pad = PaperTheme.Dp(this, DesignTokens.Space.X6);
        emptyView.SetPadding(pad, 0, pad, 0);
        frame.AddView(emptyView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        threadList.EmptyView = emptyView;

        root.AddView(frame, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return root;
    }

    private void RequestRuntimePermissions()
    {
        var wanted = new[]
        {
            Manifest.Permission.ReadContacts,
            Manifest.Permission.PostNotifications,
            // Placing calls from the new-conversation flow (ActionCall).
            Manifest.Permission.CallPhone,
            // READ_CALL_LOG and READ_SMS are hard-restricted: these requests
            // auto-deny unless the APK was installed with -g / whitelisted,
            // but they take effect post-cutover.
            Manifest.Permission.ReadCallLog,
            Manifest.Permission.ReadSms,
        };
        var toRequest = wanted
            .Where(permission => CheckSelfPermission(permission) != Permission.Granted)
            .ToArray();
        if (toRequest.Length > 0)
        {
            RequestPermissions(toRequest, 0);
        }
    }

    public override void OnRequestPermissionsResult(
        int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        ReloadThreads();
    }

    // Full reload: re-read the live call/SMS tail and the first cache page, then
    // rebuild the list. All I/O and merging happen off the UI thread so the
    // screen never blocks (the ANR the owner was hitting); only the final bind
    // touches views.
    private void ReloadThreads()
    {
        var generation = ++loadGeneration;
        loading = true;
        SetStatus("Loading…");
        emptyView!.Text = ""; // Suppress the empty message until the load resolves.
        var titleSeed = allRows.ToDictionary(row => row.ContactKey, row => row.Title);

        System.Threading.Tasks.Task.Run(() =>
        {
            var live = ReadLiveTail();
            var (page, rows) = ReadPageAndBuild(live, [], 0, titleSeed);
            RunOnUiThread(() =>
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
                BindRows(filterField?.Text ?? "");
                UpdateEmptyState();
                ClearStatus();
            });
        });
    }

    // Appends the next cache page to what is already shown. Kept in the same
    // generation as the reload it extends, so a concurrent reload discards it.
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

        System.Threading.Tasks.Task.Run(() =>
        {
            var (page, rows) = ReadPageAndBuild(live, prior, offset, titleSeed);
            RunOnUiThread(() =>
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
                BindRows(filterField?.Text ?? "");
            });
        });
    }

    private List<TimelineItem> ReadLiveTail()
    {
        var live = new List<TimelineItem>();
        try
        {
            live.AddRange(CallLogReader.ReadAll(this));
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_CALL_LOG not granted yet; show what we have.
        }
        try
        {
            live.AddRange(SmsLogReader.ReadAll(this));
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_SMS not granted yet; show what we have.
        }
        return live;
    }

    // Reads one cache page at the given offset and merges it with the live tail
    // and the pages already loaded into an ordered set of list rows. Runs on a
    // background thread. Returns the page (so the caller can advance its offset)
    // alongside the rebuilt rows.
    private (List<TimelineItem> Page, List<ThreadRow> Rows) ReadPageAndBuild(
        List<TimelineItem> live, List<TimelineItem> priorCache, int offset,
        Dictionary<string, string> titleSeed)
    {
        var page = new List<TimelineItem>();
        IReadOnlyList<ContactThread> merged;
        try
        {
            using var database = TimelineDatabase.Open(this);
            page.AddRange(database.Cache.LoadRecent(CachePageSize, offset));

            var all = new List<TimelineItem>(live.Count + priorCache.Count + page.Count);
            all.AddRange(live);
            all.AddRange(priorCache);
            all.AddRange(page);

            // Drop items the owner deleted (text-delete fallback), merge by resolved
            // person identity, then stamp unread from the per-thread read record.
            var visible = database.Hidden.Filter(all);
            var resolver = ContactAssociations.BuildResolver(this, database.IdentityLinks);
            var readState = new ReadState(database.ReadState.LoadAll());
            merged = new TimelineMerger().Merge(visible, resolver)
                .Select(readState.Apply)
                .ToList();
        }
        catch (Exception e)
        {
            // The timeline cache is a convenience over the live call log; never let
            // a database problem crash the screen — fall back to a plain merge.
            Android.Util.Log.Warn("FocusPhoneComm", $"Timeline stores unavailable: {e}");
            CommunicationDiagnostics.LogWarning("Timeline", "Timeline stores unavailable; using plain merge", e);
            merged = new TimelineMerger().Merge(live.Concat(priorCache).Concat(page));
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

    private void BindRows(string query)
    {
        var trimmed = query.Trim();
        shownRows = trimmed.Length == 0
            ? allRows
            : allRows.Where(row =>
                row.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();

        threadList!.Adapter = new PaperListAdapter(this, shownRows
            // A subtle leading dot marks unread (DESIGN.md: a mark, not a badge).
            .Select(row => new PaperListRow((row.HasUnread ? "• " : "") + row.Title, row.Subtitle))
            .ToList());
    }

    private void UpdateEmptyState() =>
        emptyView!.Text = new AccountStore(this).IsConfigured
            ? "No conversations yet.\n\nCall and text history appears once permissions are granted."
            : "No conversations yet.\n\nTap Settings to connect your Gmail account.";

    // Reconcile the cache with Gmail (and drain the bridge) on demand. Runs off
    // the UI thread; success is the refreshed list (DESIGN.md §7: no success
    // toast), failure a quiet status sentence.
    private void SyncNow()
    {
        if (syncing)
        {
            return;
        }
        if (!new AccountStore(this).IsConfigured && !GmBridgeClient.Shared(this).IsPaired)
        {
            SetStatus("Connect your Gmail account in Settings to sync.");
            return;
        }

        syncing = true;
        syncButton!.Enabled = false;
        SetStatus("Syncing…");

        System.Threading.Tasks.Task.Run(() =>
        {
            var ok = true;
            try
            {
                CommunicationSync.RunReconcile(this);
            }
            catch (Exception e)
            {
                ok = false;
                Android.Util.Log.Warn("FocusPhoneComm", $"Manual sync failed: {e}");
                CommunicationDiagnostics.LogWarning("EmailSync", "Manual sync failed", e);
            }
            RunOnUiThread(() =>
            {
                syncing = false;
                syncButton!.Enabled = true;
                if (ok)
                {
                    ReloadThreads(); // Reflect the freshly reconciled cache; clears status.
                }
                else
                {
                    SetStatus("Sync failed. Check your connection and try again.");
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
        statusLine.Text = "";
        statusLine.Visibility = ViewStates.Gone;
    }

    private string ThreadTitle(ContactThread thread)
    {
        if (thread.IsGroup)
        {
            var names = thread.Participants.Take(3)
                .Select(key => ContactNameResolver.DisplayName(this, key));
            var joined = string.Join(", ", names);
            if (joined.Length == 0)
            {
                return "Group";
            }
            return thread.Participants.Count > 3 ? $"{joined} +{thread.Participants.Count - 3}" : joined;
        }
        return ContactNameResolver.DisplayName(this, thread.ContactKey);
    }

    private enum NewConversationKind { Call, Text, Email }

    // Person-first (DESIGN: pick who, then how to reach them). Contacts are
    // offered as people; the last option types an unknown number/address.
    private void ShowNewConversationDialog()
    {
        IReadOnlyList<ContactPicker.Person> people;
        try
        {
            people = ContactPicker.People(this);
        }
        catch (Java.Lang.SecurityException)
        {
            people = []; // READ_CONTACTS not granted yet; manual entry still works.
        }

        var labels = people.Select(person => person.Name).ToList();
        labels.Add("Type a number or address…");

        new AlertDialog.Builder(this)
            .SetTitle("New")!
            .SetItems(labels.ToArray(), (_, e) =>
            {
                if (e.Which < people.Count)
                {
                    ShowChannelChooser(people[e.Which]);
                }
                else
                {
                    ShowManualEntry();
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void ShowChannelChooser(ContactPicker.Person person)
    {
        var options = new List<(string Label, NewConversationKind Kind, string Address)>();
        if (person.Phones.Count > 0)
        {
            options.Add(("Call", NewConversationKind.Call, person.Phones[0]));
            options.Add(("Text", NewConversationKind.Text, person.Phones[0]));
        }
        if (person.Emails.Count > 0)
        {
            options.Add(("Email", NewConversationKind.Email, person.Emails[0]));
        }
        if (options.Count == 0)
        {
            return;
        }

        new AlertDialog.Builder(this)
            .SetTitle(person.Name)!
            .SetItems(options.Select(option => option.Label).ToArray(),
                (_, e) => Dispatch(options[e.Which].Kind, options[e.Which].Address))!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void ShowManualEntry()
    {
        var input = PaperUi.MonoField(this, "Number or email");
        var dialog = new AlertDialog.Builder(this)
            .SetTitle("New")!
            .SetView(input)!
            .SetPositiveButton("Go", (IDialogInterfaceOnClickListener?)null)!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Create()!;

        dialog.Show();
        // Wired after Show so invalid input keeps the dialog open.
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += (_, _) =>
        {
            var raw = input.Text ?? "";
            if (raw.Contains('@') && RecipientInput.TryCreateEmail(raw, out var email))
            {
                dialog.Dismiss();
                Dispatch(NewConversationKind.Email, email);
            }
            else if (RecipientInput.TryCreatePhone(raw, out var phone))
            {
                dialog.Dismiss();
                ShowChannelChooser(new ContactPicker.Person(phone, [phone], []));
            }
            else
            {
                Toast.MakeText(this, "Enter a phone number or email.", ToastLength.Short)!.Show();
            }
        };
    }

    private void Dispatch(NewConversationKind kind, string address)
    {
        switch (kind)
        {
            case NewConversationKind.Call:
                if (RecipientInput.TryCreatePhone(address, out var callNumber))
                {
                    StartActivity(new Intent(Intent.ActionCall, Android.Net.Uri.Parse($"tel:{callNumber}")));
                }
                break;
            case NewConversationKind.Text:
                if (RecipientInput.TryCreatePhone(address, out var textNumber))
                {
                    StartActivity(new Intent(Intent.ActionSendto, Android.Net.Uri.Parse($"smsto:{textNumber}")));
                }
                break;
            case NewConversationKind.Email:
                EmailComposeDialog.Show(this, address, ContactNameResolver.DisplayName(this, address));
                break;
        }
    }
}
