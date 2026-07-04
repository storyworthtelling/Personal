using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Timeline;

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
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class ThreadsActivity : Activity
{
    public static readonly Color Paper = Color.ParseColor("#FAF8F2");
    private static readonly Color Ink = Color.ParseColor("#3A3A33");
    private static readonly Color InkMuted = Color.ParseColor("#8A8578");

    private ListView? threadList;
    private TextView? emptyView;
    private IReadOnlyList<ContactThread> threads = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
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
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        LoadThreads();
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Paper);

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetPadding(48, 72, 32, 24);
        header.SetGravity(GravityFlags.CenterVertical);

        var title = new TextView(this) { Text = "Communication", TextSize = 26 };
        title.SetTextColor(Ink);
        header.AddView(title,
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var newConversation = new Button(this) { Text = "New" };
        newConversation.Click += (_, _) => ShowNewConversationDialog();
        header.AddView(newConversation);

        var settings = new Button(this) { Text = "Settings" };
        settings.Click += (_, _) => StartActivity(new Intent(this, typeof(SettingsActivity)));
        header.AddView(settings);

        root.AddView(header, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var frame = new FrameLayout(this);
        threadList = new ListView(this);
        threadList.SetBackgroundColor(Paper);
        frame.AddView(threadList, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        emptyView = new TextView(this) { TextSize = 16 };
        emptyView.SetTextColor(InkMuted);
        emptyView.Gravity = GravityFlags.Center;
        emptyView.SetPadding(64, 0, 64, 0);
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
        try
        {
            using var database = TimelineDatabase.Open(this);
            items.AddRange(database.Cache.LoadAll());
        }
        catch (Exception e)
        {
            // The timeline cache is a convenience over the live call log; never
            // let a database problem crash the screen on open.
            Android.Util.Log.Warn("FocusPhoneComm", $"Timeline cache unavailable: {e}");
        }

        threads = new TimelineMerger().Merge(items);
        threadList!.Adapter = new ThreadListAdapter(this, threads);

        emptyView!.Text = new AccountStore(this).IsConfigured
            ? "No conversations yet.\n\nCall and text history appears once permissions are granted."
            : "No conversations yet.\n\nTap Settings to connect your Gmail account.";
    }

    private enum NewConversationKind { Call, Text, Email }

    private void ShowNewConversationDialog()
    {
        string[] options = ["Call", "Text", "Email"];
        new AlertDialog.Builder(this)
            .SetTitle("New")!
            .SetItems(options, (_, e) => ShowRecipientPicker((NewConversationKind)e.Which))!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void ShowRecipientPicker(NewConversationKind kind)
    {
        var entries = LoadPickerEntries(kind);
        var isEmail = kind == NewConversationKind.Email;

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(32, 16, 32, 0);
        var manual = new EditText(this)
        {
            Hint = isEmail ? "Email address" : "Phone number",
            InputType = isEmail
                ? Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationEmailAddress
                : Android.Text.InputTypes.ClassPhone,
        };
        layout.AddView(manual);

        var contactList = new ListView(this)
        {
            Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1,
                entries.Select(entry => $"{entry.Name} — {entry.Address}").ToList()),
        };
        layout.AddView(contactList, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var dialog = new AlertDialog.Builder(this)
            .SetTitle($"New {kind.ToString().ToLowerInvariant()}")!
            .SetView(layout)!
            .SetPositiveButton("Go", (IDialogInterfaceOnClickListener?)null)!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Create()!;

        contactList.ItemClick += (_, e) =>
        {
            dialog.Dismiss();
            Dispatch(kind, entries[e.Position].Address);
        };

        dialog.Show();
        // Wired after Show so invalid manual input keeps the dialog open.
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += (_, _) =>
        {
            if (TryValidateManualEntry(kind, manual.Text ?? "", out var address))
            {
                dialog.Dismiss();
                Dispatch(kind, address);
            }
            else
            {
                Toast.MakeText(this,
                    isEmail ? "Enter a valid email address." : "Enter a phone number.",
                    ToastLength.Short)!.Show();
            }
        };
    }

    private IReadOnlyList<ContactPicker.Entry> LoadPickerEntries(NewConversationKind kind)
    {
        try
        {
            return kind == NewConversationKind.Email
                ? ContactPicker.EmailContacts(this)
                : ContactPicker.PhoneContacts(this);
        }
        catch (Java.Lang.SecurityException)
        {
            return []; // READ_CONTACTS not granted yet; manual entry still works.
        }
    }

    private static bool TryValidateManualEntry(NewConversationKind kind, string raw, out string address) =>
        kind == NewConversationKind.Email
            ? RecipientInput.TryCreateEmail(raw, out address)
            : RecipientInput.TryCreatePhone(raw, out address);

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

    private class ThreadListAdapter : BaseAdapter
    {
        private readonly ThreadsActivity activity;
        private readonly IReadOnlyList<ContactThread> threads;

        public ThreadListAdapter(ThreadsActivity activity, IReadOnlyList<ContactThread> threads)
        {
            this.activity = activity;
            this.threads = threads;
        }

        public override int Count => threads.Count;

        public override Java.Lang.Object? GetItem(int position) => null;

        public override long GetItemId(int position) => position;

        public override View GetView(int position, View? convertView, ViewGroup? parent)
        {
            var view = convertView ?? activity.LayoutInflater.Inflate(
                Android.Resource.Layout.SimpleListItem2, parent, false)!;
            var thread = threads[position];
            var latest = thread.Items[^1];

            view.FindViewById<TextView>(Android.Resource.Id.Text1)!.Text =
                ContactNameResolver.DisplayName(activity, thread.ContactKey);
            view.FindViewById<TextView>(Android.Resource.Id.Text2)!.Text =
                $"{latest.TimestampUtc.ToLocalTime():MMM d HH:mm} · {TimelineItemFormatter.Describe(latest)}";
            return view;
        }
    }
}
