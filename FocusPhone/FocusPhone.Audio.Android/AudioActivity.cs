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
using FocusPhone.Domain.Time;
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
    private FrameLayout? undoHost;
    private PodcastLibrary library = new([]);
    private IReadOnlyList<Episode> queue = [];
    private MediaController? controller;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
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
                MarkListenedWithUndo(queue[e.Position]);
            }
            e.Handled = true;
        };

        ConnectController();
    }

    // ListActivity binds to the ListView with the platform list id. The action
    // row is a pinned bottom bar; the undo host overlays the list.
    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var header = PaperUi.PageHeader(this, "Audio");
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        root.AddView(header);

        undoHost = new FrameLayout(this);
        var list = new ListView(this) { Id = Android.Resource.Id.List };
        PaperUi.StylePaperList(this, list);
        undoHost.AddView(list, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var empty = PaperUi.EmptyState(this, "Nothing new to hear.");
        undoHost.AddView(empty, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        list.EmptyView = empty;

        root.AddView(undoHost, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(PaperUi.BottomBar(this,
            PaperUi.SecondaryButton(this, "Add feed", ShowAddFeedDialog),
            PaperUi.QuietButton(this, "YT Music", ShowYtMusicSearch)));
        return root;
    }

    // Undo-first (DESIGN.md §7): mark immediately, offer the reversal.
    private void MarkListenedWithUndo(Episode episode)
    {
        library.MarkListened(episode.Id);
        store!.SaveListenedIds(library.ExportListenedIds());
        RenderQueue();
        UndoBar.Show(this, undoHost!, "Marked listened.", onUndo: () =>
        {
            library.MarkUnlistened(episode.Id);
            store.SaveListenedIds(library.ExportListenedIds());
            RenderQueue();
        });
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
                // Failure may speak (DESIGN.md §7); worded per the error template.
                Toast.MakeText(this, $"{new Uri(feedUrl).Host} didn't answer. It retries on the next refresh.",
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
                RelativeTime.Format(episode.PublishedUtc.ToLocalTime(), DateTime.Now) +
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
