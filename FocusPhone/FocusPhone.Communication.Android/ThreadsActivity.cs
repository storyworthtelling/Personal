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

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetPadding(
            PaperTheme.Dp(this, DesignTokens.Space.X4), PaperTheme.Dp(this, DesignTokens.Space.X8),
            PaperTheme.Dp(this, DesignTokens.Space.X3), PaperTheme.Dp(this, DesignTokens.Space.X3));
        header.SetGravity(GravityFlags.CenterVertical);

        var title = PaperUi.Display(this, "Communication");
        header.AddView(title,
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var settings = PaperUi.QuietButton(this, "Settings", () =>
            StartActivity(new Intent(this, typeof(SettingsActivity))));
        header.AddView(settings);

        root.AddView(header, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

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
            // READ_CALL_LOG is hard-restricted: this request auto-denies unless the
            // APK was installed with -g / whitelisted, but it takes effect post-cutover.
            Manifest.Permission.ReadCallLog,
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
        threadList!.Adapter = new PaperListAdapter(this, threads
            .Select(thread =>
            {
                var latest = thread.Items[^1];
                return new PaperListRow(
                    ContactNameResolver.DisplayName(this, thread.ContactKey),
                    $"{RelativeTime.Format(latest.TimestampUtc.ToLocalTime(), DateTime.Now)} · {latest.Snippet}");
            })
            .ToList());

        emptyView!.Text = new AccountStore(this).IsConfigured
            ? "No conversations yet.\n\nCall and text history appears once permissions are granted."
            : "No conversations yet.\n\nTap Settings to connect your Gmail account.";
    }
}
