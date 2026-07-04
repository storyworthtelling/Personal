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
using FocusPhone.Domain.Design;
using FocusPhone.Infrastructure.Podcasts;
using FocusPhone.Ui;

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
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class AudioActivity : ListActivity
{
    private const string YtMusicPackage = "com.google.android.apps.youtube.music";

    private PodcastStore? store;
    private PodcastLibrary library = new([]);
    private IReadOnlyList<Episode> queue = [];
    private MediaController? controller;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        store = new PodcastStore(this);
        SetContentView(BuildLayout());
        PaperUi.StylePaperList(this, ListView!);

        ListView!.ItemClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < queue.Count)
            {
                Play(queue[e.Position]);
            }
        };

        ListView.ItemLongClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < queue.Count)
            {
                library.MarkListened(queue[e.Position].Id);
                store.SaveListenedIds(library.ExportListenedIds());
                RenderQueue();
            }
            e.Handled = true;
        };

        ConnectController();
    }

    // ListActivity binds to the ListView with the platform list id, letting the
    // action row live outside the list (so the empty state can show).
    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(PaperTheme.Paper);
        root.AddView(BuildActionRow());

        var frame = new FrameLayout(this);
        var list = new ListView(this) { Id = Android.Resource.Id.List };
        frame.AddView(list, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var empty = PaperUi.EmptyState(this, "Nothing new to hear.");
        frame.AddView(empty, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        list.EmptyView = empty;

        root.AddView(frame, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return root;
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
        ListAdapter = new PaperListAdapter(this, queue
            .Select(episode => new PaperListRow(
                episode.Title,
                $"{episode.PublishedUtc.ToLocalTime():MMM d}" +
                (episode.Duration is { } d ? $" · {(int)d.TotalMinutes} min" : "")))
            .ToList());
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
        var horizontal = PaperTheme.Dp(this, DesignTokens.Space.X3);
        var vertical = PaperTheme.Dp(this, DesignTokens.Space.X4);
        row.SetPadding(horizontal, vertical, horizontal, vertical);
        AddAction(row, PaperUi.SecondaryButton(this, "Add feed", ShowAddFeedDialog));
        AddAction(row, PaperUi.QuietButton(this, "YT Music", ShowYtMusicSearch));
        return row;
    }

    private void AddAction(LinearLayout row, Button button)
    {
        row.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent)
        {
            Weight = 1,
            MarginEnd = PaperTheme.Dp(this, DesignTokens.Space.X3),
        });
    }

    private void ShowAddFeedDialog()
    {
        var input = PaperUi.MonoField(this, "RSS feed URL");
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
        var input = PaperUi.Field(this, "Search YouTube Music");
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

}
