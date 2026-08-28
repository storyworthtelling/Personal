using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Score.Google;
using Codex.Domain.Design;
using Codex.Ui;

namespace Score;

/// <summary>
/// Score's app shell, and only the shell: the frame, the now-playing bar, and which
/// area is showing.
///
/// It holds no podcast or music state of its own — each <see cref="ScoreArea"/> owns
/// what it shows. What is left here is the three things no area can own: the player,
/// which both share; the listening log, which samples the player rather than any one
/// area; and the greeting, which belongs to the app rather than to a tab.
/// </summary>
[Activity(
    Label = "Score",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class ScoreActivity : Activity
{
    private readonly DeviceAccountTokenProvider musicTokens = new();

    private ScorePlayback playback = null!;
    private ListeningLog listening = null!;

    private ScoreArea[] areas = [];
    private int activeIndex;

    // Owner's-voice greeting, reusing the music Google account to read the shared voice profile from Drive.
    private Codex.Ui.Voice.VoiceGreeting? greeting;
    private TextView? greetingView;
    private string greetingText = string.Empty;

    private FrameLayout content = null!;
    private FrameLayout bottomBarSlot = null!;
    private BinderTabs binder = null!;
    private LinearLayout headerSlot = null!;
    private NowPlayingBar nowPlayingBar = null!;
    private Handler? uiHandler;
    private Java.Lang.Runnable? ticker;

    private ScoreArea Active => areas[activeIndex];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        playback = new ScorePlayback(this);

        listening = new ListeningLog(
            this, new Score.Google.MusicTasteService(musicTokens.GetAccessToken, "Score"));
        var progressStore = new PlaybackProgressStore(this);
        var musicLibrary = new MusicLibrary(this, musicTokens, playback, listening, progressStore);
        musicLibrary.LoadCatalog();

        areas =
        [
            new MusicArea(this, musicLibrary),
            new PodcastsArea(this, playback, progressStore),
            new PlayingArea(this, playback),
            new DiscoverArea(this, musicLibrary),
            new WorkoutArea(this, musicLibrary, playback),
            new SettingsArea(this, musicLibrary),
        ];
        foreach (var area in areas)
        {
            // Redraw only for the area that is showing: a scan or a feed refresh
            // finishing behind the other tab should not rebuild the one you are on.
            var which = area;
            area.Changed += () => { if (Active == which) RenderContent(); };
        }

        FridayPicksWorker.Schedule(this); // weekly "New for you"; no-ops until signed in

        greeting = new Codex.Ui.Voice.VoiceGreeting(musicTokens.GetAccessToken, "Score");
        SetContentView(BuildLayout());
        playback.Connect();
        ShowGreetingAsync();
        musicLibrary.RestoreFolderFromVault();

        // The ten-day sweep. Here rather than on a schedule: it has nothing to do with
        // what the app is doing, and a worker for it would be a worker that usually
        // does nothing.
        Downloads.Sweep(this, new DownloadStore(this), DateTimeOffset.UtcNow);
        CrashReporter.ShowPendingCrash(this);
        RenderContent();
    }

    // Composes a greeting in the owner's voice (from the shared Drive profile + key) and shows it under the
    // header. Best-effort: it stays hidden when there's no signed-in account, profile, key, or on any error.
    private async void ShowGreetingAsync()
    {
        if (greeting is null || greetingView is null)
            return;
        var message = await greeting.ComposeAsync(
            "welcome", "the owner is opening their music and podcasts", CancellationToken.None);
        if (string.IsNullOrWhiteSpace(message))
            return;
        greetingText = message;
        greetingView.Text = message;
        RenderContent(); // the seat belongs to two areas, so the shell decides where it shows
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        // The page, and the binder gutter down its right edge. Everything above the
        // now-playing bar lives inside the page; the tabs sit outside it, which is
        // what stops them ever overlapping content (§9's reserved gutter).
        var spread = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        // The header is rebuilt per area — each carries its own title and the stroke
        // beneath it is in the area's hue.
        headerSlot = new LinearLayout(this) { Orientation = Orientation.Vertical };
        headerSlot.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        page.AddView(headerSlot);

        // A greeting in the owner's voice, hidden until composed (and only when a shared profile exists).
        // The calligraphy hand, in ink-blue (DESIGN.md §4): the owner's voice is the
        // one thing set in it, and setting it in Sans would make it read as a status
        // line, which is exactly what the voice must never be.
        greetingView = PaperUi.Voice(this, string.Empty);
        greetingView.SetPadding(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X4));
        greetingView.Visibility = ViewStates.Gone;
        page.AddView(greetingView);

        content = new FrameLayout(this);
        foreach (var area in areas)
        {
            area.Overlay = content; // undo bars and action cards overlay the content frame
        }

        content.SetPadding(edge, 0, edge, 0);
        page.AddView(content, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        spread.AddView(page, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.MatchParent) { Weight = 1 });

        binder = new BinderTabs(this);
        binder.SetAreas(areas.Select(area => area.Tab), activeIndex);
        binder.AreaSelected += index =>
        {
            // Tapping the tab you are on returns that area to its root, which is why
            // this is not guarded on the index having changed.
            if (index == activeIndex)
            {
                while (Active.GoBack())
                {
                }
            }

            activeIndex = index;
            binder.SetActive(index);
            RenderContent();
        };
        spread.AddView(binder, new LinearLayout.LayoutParams(
            PaperTheme.Dp(this, BinderTabs.WidthDp), ViewGroup.LayoutParams.MatchParent));

        root.AddView(spread, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        nowPlayingBar = new NowPlayingBar(this, playback.SkipBack, playback.TogglePlayPause, playback.SkipForward,
            onOpen: () => TurnTo(typeof(PlayingArea)));
        root.AddView(nowPlayingBar);

        // A slot rather than a bar: each area supplies its own actions, and an area
        // with none simply leaves it empty.
        bottomBarSlot = new FrameLayout(this);
        root.AddView(bottomBarSlot);
        return root;
    }

    protected override void OnResume()
    {
        base.OnResume();
        StartTicking();
        foreach (var area in areas)
        {
            area.Resume();
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        StopTicking();

        // Finalize whatever was mid-play, then fold the day's listening into the Taste Profile (throttled).
        listening.Stop();
        listening.MaybeRefreshTaste();

        foreach (var area in areas)
        {
            area.Pause();
        }
    }

    protected override void OnDestroy()
    {
        playback.Release();
        base.OnDestroy();
    }

    public override void OnBackPressed()
    {
        // The area gets first refusal — only it knows whether it is drilled into
        // something. It raises its own redraw if it used the press.
        if (!Active.GoBack())
        {
            base.OnBackPressed();
        }
    }

    // ---- Content rendering ----

    /// <summary>Turns the binder to an area by type, as the now-playing bar does.</summary>
    private void TurnTo(Type areaType)
    {
        var index = Array.FindIndex(areas, area => area.GetType() == areaType);
        if (index < 0 || index == activeIndex)
        {
            return;
        }

        activeIndex = index;
        binder.SetActive(index);
        RenderContent();
    }

    private void RenderContent()
    {
        headerSlot.RemoveAllViews();
        headerSlot.AddView(PaperUi.ScreenHeader(this, Active.Title, Active.Tab.Hue));

        content.RemoveAllViews();
        content.AddView(Active.BuildView(), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // The bar is chrome for areas that are not themselves the current track.
        nowPlayingBar.Visibility = Active.ShowsNowPlayingBar ? ViewStates.Visible : ViewStates.Gone;

        // The voice sits on the Music and Podcasts roots and nowhere else, so the
        // shell asks the area rather than showing it everywhere it has room.
        if (greetingView is not null)
        {
            greetingView.Visibility = Active.ShowsVoice && greetingText.Length > 0
                ? ViewStates.Visible
                : ViewStates.Gone;
        }

        bottomBarSlot.RemoveAllViews();
        if (Active.BuildBottomBar() is { } bar)
        {
            bottomBarSlot.AddView(bar);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == DeviceAccountSignIn.RequestCode)
        {
            DeviceAccountSignIn.HandleChooserResult(resultCode, data);
            return;
        }

        foreach (var area in areas)
        {
            area.OnActivityResult(requestCode, resultCode, data);
        }
    }

    private void StartTicking()
    {
        uiHandler ??= new Handler(Looper.MainLooper!);
        ticker ??= new Java.Lang.Runnable(Tick);
        uiHandler.PostDelayed(ticker, 1000);
    }

    private void StopTicking()
    {
        if (uiHandler is not null && ticker is not null)
        {
            uiHandler.RemoveCallbacks(ticker);
        }
    }

    private void Tick()
    {
        if (playback.Controller is not null)
        {
            nowPlayingBar.Update(playback.CurrentTitle, playback.IsPlaying);
            ObserveListening();
            Active.OnTick();
        }

        uiHandler?.PostDelayed(ticker!, 1000);
    }

    // Sample the music player for Genius. Only music counts — the player remembers what
    // kind of thing it started — and the recorder turns id changes into completed and
    // skipped signals itself.
    private void ObserveListening()
    {
        if (playback.Kind != MediaKind.Music || playback.CurrentMediaId is null)
        {
            return;
        }

        listening.Observe(playback.CurrentMediaId, playback.Position, playback.Duration);
    }
}
