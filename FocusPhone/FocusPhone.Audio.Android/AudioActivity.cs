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
    private PlaybackProgressStore? progressStore;
    private PlaybackSettingsStore? settingsStore;
    private PlaybackSettingsSnapshot settings = PlaybackSettingsSnapshot.Empty();
    private FrameLayout? undoHost;
    private PodcastLibrary library = new([]);
    private IReadOnlyList<Episode> queue = [];
    private MediaController? controller;
    private Episode? nowPlaying;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        store = new PodcastStore(this);
        progressStore = new PlaybackProgressStore(this);
        settingsStore = new PlaybackSettingsStore(this);
        settings = settingsStore.Load();
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
        CrashReporter.ShowPendingCrash(this);
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
            PaperUi.QuietButton(this, "Speed", ShowSpeedDialog),
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
        var progress = new PlaybackProgressBook(progressStore!.Load());
        ListAdapter = new PaperListAdapter(this, queue
            .Select(episode => new PaperListRow(
                episode.Title,
                RelativeTime.Format(episode.PublishedUtc.LocalDateTime, DateTime.Now) +
                    DurationSuffix(episode, progress)))
            .ToList());
    }

    // Episode length shown as true time-left at the show's speed, minus any
    // resume position — so a half-heard episode reads how long is actually left.
    private string DurationSuffix(Episode episode, PlaybackProgressBook progress)
    {
        if (episode.Duration is not { } duration)
        {
            return "";
        }

        var position = progress.Resume(episode.Id) ?? TimeSpan.Zero;
        var remaining = duration - position;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        return " · " + PlaybackFormatting.TimeLeftAtSpeed(remaining, settings.For(episode.FeedId).Speed);
    }

    private void Play(Episode episode)
    {
        if (controller is null || episode.AudioUrl is null)
        {
            return;
        }

        nowPlaying = episode;
        var showSettings = settings.For(episode.FeedId);
        var metadata = new MediaMetadata.Builder().SetTitle(episode.Title)!.Build()!;
        var item = new MediaItem.Builder()
            .SetMediaId(episode.Id)!
            .SetUri(episode.AudioUrl)!
            .SetMediaMetadata(metadata)!
            .Build()!;

        var resume = new PlaybackProgressBook(progressStore!.Load())
            .ResumeWithRewind(episode.Id, DateTimeOffset.Now);
        var startMs = resume is { } point ? (long)point.TotalMilliseconds : 0L;

        controller.SetMediaItem(item, startMs);
        controller.SetPlaybackSpeed(showSettings.Speed);
        controller.Prepare();
        controller.Play();
        SendSkipSilence(showSettings.SkipSilence);
    }

    private void SendSkipSilence(bool enabled)
    {
        var intent = new Intent(this, typeof(PlaybackService));
        intent.SetAction(AudioPlaybackCommands.ActionSetSkipSilence);
        intent.PutExtra(AudioPlaybackCommands.ExtraEnabled, enabled);
        StartService(intent);
    }

    // Per-show playback speed + skip-silence in a plain Paper dialog (no new
    // tokens). Edits the currently-playing show's settings, or the global default
    // when nothing is playing; applies live and persists.
    private void ShowSpeedDialog()
    {
        var feedId = nowPlaying?.FeedId;
        var current = feedId is null ? settings.Default : settings.For(feedId);

        var edge = PaperTheme.Dp(this, DesignTokens.Space.X4);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(edge, edge, edge, edge);

        var label = PaperUi.Title(this, SpeedLabel(current.Speed));
        layout.AddView(label);

        Button? trim = null;

        void Apply(PlaybackSettings updated)
        {
            current = updated;
            label.Text = SpeedLabel(current.Speed);
            if (trim is not null)
            {
                trim.Text = TrimLabel(current.SkipSilence);
            }

            PersistAndApply(feedId, current);
        }

        var presetRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        presetRow.AddView(PaperUi.SecondaryButton(this, "−", () => Apply(current.Slower())));
        foreach (var preset in PlaybackSettings.Presets)
        {
            var value = preset;
            presetRow.AddView(PaperUi.QuietButton(this, $"{value:0.0}×", () => Apply(current.WithSpeed(value))));
        }
        presetRow.AddView(PaperUi.SecondaryButton(this, "+", () => Apply(current.Faster())));
        layout.AddView(presetRow);

        trim = PaperUi.SecondaryButton(this, TrimLabel(current.SkipSilence),
            () => Apply(current with { SkipSilence = !current.SkipSilence }));
        layout.AddView(trim);

        new AlertDialog.Builder(this)
            .SetTitle("Playback speed")!
            .SetView(layout)!
            .SetPositiveButton("Done", (_, _) => { })!
            .Show();
    }

    private void PersistAndApply(string? feedId, PlaybackSettings updated)
    {
        settings = feedId is null
            ? settings with { Default = updated }
            : settings.With(feedId, updated);
        settingsStore!.Save(settings);
        controller?.SetPlaybackSpeed(updated.Speed);
        SendSkipSilence(updated.SkipSilence);
    }

    private static string SpeedLabel(float speed) => $"Speed {speed:0.0}×";

    private static string TrimLabel(bool on) => on ? "Trim silence: on" : "Trim silence: off";

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
