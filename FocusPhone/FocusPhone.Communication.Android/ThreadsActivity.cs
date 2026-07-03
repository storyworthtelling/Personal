using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Widget;
using FocusPhone.Domain.Timeline;

namespace FocusPhone.Communication;

/// <summary>
/// The main screen: every conversation (calls, texts, email) merged into one
/// list of per-contact threads, most recent first.
/// </summary>
[Activity(
    Label = "Communication",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class ThreadsActivity : ListActivity
{
    public static readonly Color Paper = Color.ParseColor("#FAF8F2");

    private IReadOnlyList<ContactThread> threads = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ListView!.SetBackgroundColor(Paper);

        ListView.ItemClick += (_, e) =>
        {
            var intent = new Intent(this, typeof(ThreadActivity));
            intent.PutExtra(ThreadActivity.ExtraContactKey, threads[e.Position].ContactKey);
            StartActivity(intent);
        };

        ListView.ItemLongClick += (_, e) =>
        {
            StartActivity(new Intent(this, typeof(SettingsActivity)));
            e.Handled = true;
        };

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

    private void LoadThreads()
    {
        var items = new List<TimelineItem>();
        try
        {
            items.AddRange(CallLogReader.ReadAll(this));
        }
        catch (Java.Lang.SecurityException)
        {
            // READ_CALL_LOG not yet granted (pre-provisioning); show what we have.
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
        ListAdapter = new ThreadListAdapter(this, threads);
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

        public override Android.Views.View GetView(int position, Android.Views.View? convertView,
            Android.Views.ViewGroup? parent)
        {
            var view = convertView ?? activity.LayoutInflater.Inflate(
                Android.Resource.Layout.SimpleListItem2, parent, false)!;
            var thread = threads[position];
            var latest = thread.Items[^1];

            view.FindViewById<TextView>(Android.Resource.Id.Text1)!.Text =
                ContactNameResolver.DisplayName(activity, thread.ContactKey);
            view.FindViewById<TextView>(Android.Resource.Id.Text2)!.Text =
                $"{latest.TimestampUtc.ToLocalTime():MMM d HH:mm} · {latest.Snippet}";
            return view;
        }
    }
}
