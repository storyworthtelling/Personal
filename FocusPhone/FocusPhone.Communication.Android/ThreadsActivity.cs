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
    private ListView? threadList;
    private TextView? emptyView;
    private EditText? filterField;
    private IReadOnlyList<ContactThread> allThreads = [];
    private IReadOnlyList<ContactThread> threads = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        SetContentView(BuildLayout());

        threadList!.ItemClick += (_, e) =>
        {
            var intent = new Intent(this, typeof(ThreadActivity));
            intent.PutExtra(ThreadActivity.ExtraContactKey, threads[e.Position].ContactKey);
            StartActivity(intent);
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
        LoadThreads();
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

        var settings = PaperUi.QuietButton(this, "Settings", () =>
            StartActivity(new Intent(this, typeof(SettingsActivity))));
        header.AddView(settings);

        root.AddView(header, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        // Lightweight find-a-person filter (names only, no full-text search).
        filterField = PaperUi.Field(this, "Find a person");
        filterField.TextChanged += (_, _) => BindThreads(filterField.Text ?? "");
        var filterParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var edge = PaperTheme.Dp(this, DesignTokens.Space.X4);
        filterParams.SetMargins(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X2));
        root.AddView(filterField, filterParams);

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
        LoadThreads();
    }

    private void LoadThreads()
    {
        var items = new List<TimelineItem>();
        try
        {
            items.AddRange(CallLogReader.ReadAll(this));
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_CALL_LOG not granted yet; show what we have.
        }
        try
        {
            items.AddRange(SmsLogReader.ReadAll(this));
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_SMS not granted yet; show what we have.
        }

        IReadOnlyList<ContactThread> merged;
        try
        {
            using var database = TimelineDatabase.Open(this);
            items.AddRange(database.Cache.LoadAll());

            // Drop items the owner deleted (text-delete fallback), merge by resolved
            // person identity, then stamp unread from the per-thread read record.
            var visible = database.Hidden.Filter(items);
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
            merged = new TimelineMerger().Merge(items);
        }

        allThreads = InboxOrdering.UnreadFirstThenRecent(merged);
        BindThreads(filterField?.Text ?? "");

        emptyView!.Text = new AccountStore(this).IsConfigured
            ? "No conversations yet.\n\nCall and text history appears once permissions are granted."
            : "No conversations yet.\n\nTap Settings to connect your Gmail account.";
    }

    private void BindThreads(string query)
    {
        var trimmed = query.Trim();
        threads = trimmed.Length == 0
            ? allThreads
            : allThreads.Where(thread =>
                ThreadTitle(thread).Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();

        threadList!.Adapter = new PaperListAdapter(this, threads
            .Select(thread =>
            {
                var latest = thread.Items[^1];
                // A subtle leading dot marks unread (DESIGN.md: a mark, not a badge).
                var name = (thread.HasUnread ? "• " : "") + ThreadTitle(thread);
                return new PaperListRow(
                    name,
                    $"{RelativeTime.Format(latest.TimestampUtc.LocalDateTime, DateTime.Now)} · {TimelineItemFormatter.Describe(latest)}");
            })
            .ToList());
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
