using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using MediaController = AndroidX.Media3.Session.MediaController;
using FocusPhone.Domain.Audio;
using FocusPhone.Infrastructure.Podcasts;

namespace FocusPhone.Audio;

/// <summary>
/// The podcast queue: unlistened episodes newest-first (the unit-tested
/// PodcastLibrary default view). Tap to play in the background service,
/// long-press to toggle listened. YT Music search hands off to the YT Music
/// app via a targeted play-from-search intent.
/// </summary>
[Activity(
    Label = "Audio",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class AudioActivity : ListActivity
{
    private const string YtMusicPackage = "com.google.android.apps.youtube.music";
    private static readonly Color Paper = Color.ParseColor("#FAF8F2");

    private PodcastStore? store;
    private PodcastLibrary library = new([]);
    private IReadOnlyList<Episode> queue = [];
    private MediaController? controller;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        store = new PodcastStore(this);
        ListView!.SetBackgroundColor(Paper);
        ListView.AddHeaderView(BuildActionRow());

        ListView.ItemClick += (_, e) =>
        {
            var index = e.Position - 1; // Header occupies position 0.
            if (index >= 0 && index < queue.Count)
            {
                Play(queue[index]);
            }
        };

        ListView.ItemLongClick += (_, e) =>
        {
            var index = e.Position - 1;
            if (index >= 0 && index < queue.Count)
            {
                library.MarkListened(queue[index].Id);
                store.SaveListenedIds(library.ExportListenedIds());
                RenderQueue();
            }
            e.Handled = true;
        };

        ConnectController();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _ = RefreshFeedsAsync();
    }

    protected override void OnDestroy()
    {
        controller?.Release();
        base.OnDestroy();
    }

    private void ConnectController()
    {
        var token = new SessionToken(this, new ComponentName(this, Java.Lang.Class.FromType(typeof(PlaybackService))));
        var future = new MediaController.Builder(this, token).BuildAsync();
        future.AddListener(new Java.Lang.Runnable(() =>
        {
            controller = (MediaController)future.Get()!;
        }), AndroidX.Core.Content.ContextCompat.GetMainExecutor(this));
    }

    private async Task RefreshFeedsAsync()
    {
        var episodes = new List<Episode>();
        using var http = new HttpClient();
        var client = new RssFeedClient(http);
        foreach (var feedUrl in store!.FeedUrls)
        {
            try
            {
                var feed = await client.FetchAsync(new Uri(feedUrl));
                episodes.AddRange(feed.Episodes);
            }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException)
            {
                Toast.MakeText(this, $"Feed failed: {new Uri(feedUrl).Host} — {e.Message}",
                    ToastLength.Short)!.Show();
            }
        }

        library = new PodcastLibrary(episodes, store.ListenedIds);
        RenderQueue();
    }

    private void RenderQueue()
    {
        queue = library.GetDefaultView();
        ListAdapter = new EpisodeAdapter(this, queue);
    }

    private void Play(Episode episode)
    {
        if (controller is null || episode.AudioUrl is null)
        {
            return;
        }
        var metadata = new MediaMetadata.Builder().SetTitle(episode.Title)!.Build()!;
        var item = new MediaItem.Builder()
            .SetUri(episode.AudioUrl)!
            .SetMediaMetadata(metadata)!
            .Build()!;
        controller.SetMediaItem(item);
        controller.Prepare();
        controller.Play();
    }

    private View BuildActionRow()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetPadding(16, 24, 16, 24);
        AddAction(row, "Add feed", ShowAddFeedDialog);
        AddAction(row, "YT Music", ShowYtMusicSearch);
        return row;
    }

    private void AddAction(LinearLayout row, string label, Action onClick)
    {
        var button = new Button(this) { Text = label };
        button.Click += (_, _) => onClick();
        row.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent)
        {
            Weight = 1,
            MarginEnd = 16,
        });
    }

    private void ShowAddFeedDialog()
    {
        var input = new EditText(this) { Hint = "RSS feed URL" };
        new AlertDialog.Builder(this)
            .SetTitle("Subscribe to podcast")!
            .SetView(input)!
            .SetPositiveButton("Add", (_, _) =>
            {
                if (Uri.TryCreate(input.Text, UriKind.Absolute, out _))
                {
                    store!.AddFeed(input.Text!);
                    _ = RefreshFeedsAsync();
                }
                else
                {
                    Toast.MakeText(this, "Not a valid URL.", ToastLength.Short)!.Show();
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void ShowYtMusicSearch()
    {
        var input = new EditText(this) { Hint = "Search YouTube Music" };
        new AlertDialog.Builder(this)
            .SetTitle("YT Music")!
            .SetView(input)!
            .SetPositiveButton("Search", (_, _) =>
            {
                var intent = new Intent(Android.Provider.MediaStore.IntentActionMediaPlayFromSearch);
                intent.SetPackage(YtMusicPackage);
                intent.PutExtra(Android.App.SearchManager.Query, input.Text);
                try
                {
                    StartActivity(intent);
                }
                catch (ActivityNotFoundException)
                {
                    Toast.MakeText(this, "YT Music is not installed.", ToastLength.Short)!.Show();
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private class EpisodeAdapter : BaseAdapter
    {
        private readonly AudioActivity activity;
        private readonly IReadOnlyList<Episode> episodes;

        public EpisodeAdapter(AudioActivity activity, IReadOnlyList<Episode> episodes)
        {
            this.activity = activity;
            this.episodes = episodes;
        }

        public override int Count => episodes.Count;

        public override Java.Lang.Object? GetItem(int position) => null;

        public override long GetItemId(int position) => position;

        public override View GetView(int position, View? convertView, ViewGroup? parent)
        {
            var view = convertView ?? activity.LayoutInflater.Inflate(
                Android.Resource.Layout.SimpleListItem2, parent, false)!;
            var episode = episodes[position];
            view.FindViewById<TextView>(Android.Resource.Id.Text1)!.Text = episode.Title;
            view.FindViewById<TextView>(Android.Resource.Id.Text2)!.Text =
                $"{episode.PublishedUtc.ToLocalTime():MMM d}" +
                (episode.Duration is { } d ? $" · {(int)d.TotalMinutes} min" : "");
            return view;
        }
    }
}
