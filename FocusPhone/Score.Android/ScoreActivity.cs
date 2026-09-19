using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Score.Google;
using Codex.Domain.Design;
using Codex.Domain.Music;
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
    // Set by a secondary screen's binder tab (via BinderFrame) just before it finishes,
    // naming the area this shell should land on once it resumes. A same-instance hand-off
    // rather than an Intent extra: those screens are ordinary children on THIS Activity's own
    // back stack (StartActivity/StartActivityForResult, not a new task), so the instance is
    // never recreated and OnNewIntent never runs for it — only OnResume does.
    internal static int? PendingArea;

    private readonly DeviceAccountTokenProvider musicTokens = new();
    private BackCallback? backCallback;

    private ScorePlayback playback = null!;
    private ListeningLog listening = null!;

    private ScoreArea[] areas = [];
    private int activeIndex;

    // Owner's-voice greeting, reusing the music Google account to read the shared voice profile from Drive.
    private Codex.Ui.Voice.VoiceGreeting? greeting;
    private TextView? greetingView;
    private string greetingText = string.Empty;
    private RescanOutcomeStore rescanOutcomeStore = null!;

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

        // ScoreActivity is a plain Activity, not AndroidX's ComponentActivity, so it never gets
        // ComponentActivity's OnBackPressedDispatcher compatibility shim for free. On API 33+,
        // with the system's predictive-back gesture enabled, the gesture is resolved by the
        // platform's OnBackInvokedDispatcher and can bypass the deprecated OnBackPressed()
        // override entirely — which is what let the gesture fall through past this app (into
        // whatever was behind it, i.e. the launcher) instead of reaching Active.GoBack(), the
        // exact "back exits the app instead of returning to the Albums list" report. Registering
        // here routes both paths to the same GoBack() logic; OnBackPressed() stays for pre-33.
        if ((int)Build.VERSION.SdkInt >= 33)
        {
            backCallback = new BackCallback(this);
            // 0 is PRIORITY_DEFAULT (android.window.OnBackInvokedDispatcher) — a raw literal
            // rather than a named constant, since the constant's exact binding surface isn't
            // worth guessing at when the value itself is stable platform API.
            OnBackInvokedDispatcher?.RegisterOnBackInvokedCallback(0, backCallback);
        }

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
        MusicScanWorker.ScheduleNightly(this); // nightly incremental rescan; no-ops until a folder is chosen

        // Keeps Score current where nothing else will. On the locked phone the
        // launcher is the custodian and this resolves to Deferred, so it costs a
        // device-owner check a day and does nothing else.
        ScoreUpdateService.Schedule(this);

        greeting = new Codex.Ui.Voice.VoiceGreeting(musicTokens.GetAccessToken, "Score");
        rescanOutcomeStore = new RescanOutcomeStore(this);
        SetContentView(BuildLayout());
        playback.Connect();
        ShowGreetingAsync();
        musicLibrary.RestoreFolderFromVault();

        // The TIDAL pairing, from this person's own vault. Only when this phone has
        // none — a phone that is signed in is already right, whatever the backup says.
        _ = new TidalAccountSync(this).RestoreAsync(CancellationToken.None);
        _ = musicLibrary.RevalidateSavedTidalTracksAsync();

        // The ten-day sweep. Here rather than on a schedule: it has nothing to do with
        // what the app is doing, and a worker for it would be a worker that usually
        // does nothing.
        Downloads.Sweep(this, new DownloadStore(this), DateTimeOffset.UtcNow);
        CrashReporter.ShowPendingCrash(this);
        RenderContent();
    }

    // Composes a greeting in the owner's voice (from the shared Drive profile + key) and shows it under the
    // header. Best-effort: it stays hidden when there's no signed-in account, profile, key, or on any error.
    //
    // On the day's first such greeting (Codex.Domain.Music.FirstOpenOfDay), the context fed to the
    // composer carries what the nightly rescan (MusicScanWorker.ScheduleNightly) found, when it found
    // anything — RescanOutcomeStore.Completed is the last fully-drained scan's outcome, whether that
    // scan ran overnight or was triggered by hand from Settings. Nothing found is left unmentioned
    // rather than described as "nothing new" (quiet by default, DESIGN.md's own standing rule):
    // the owner asked for the greeting to be ABOUT a finding, not to announce the absence of one.
    // The date is only advanced once a greeting actually displays, so a composer that returns
    // nothing (no profile, no key, a failed call) leaves the day's "first" open for the next try.
    private async void ShowGreetingAsync()
    {
        if (greeting is null || greetingView is null)
            return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var isFirstOpenToday = FirstOpenOfDay.IsFirst(today, MusicSettingsStore.LastGreetingLocalDate);
        var outcome = isFirstOpenToday ? rescanOutcomeStore.Completed : null;

        var context = outcome is { IsEmpty: false } found
            ? "the owner is opening their music and podcasts for the first time today; " +
              $"their automatic overnight library rescan found this: {found.Describe()}"
            : "the owner is opening their music and podcasts";

        var message = await greeting.ComposeAsync("welcome", context, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(message))
            return;

        greetingText = message;
        greetingView.Text = message;
        if (isFirstOpenToday)
        {
            MusicSettingsStore.LastGreetingLocalDate = today;
        }

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
        content.SetPadding(edge, 0, edge, 0);
        page.AddView(content, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // The overlay host wraps the WHOLE page — header included — not just the content frame.
        // A dog-ear posted against `content` alone sat below the header/greeting, which read as
        // "inside the binder tabs" rather than at the top of the page the way a folded corner
        // should. Wrapping the header in it too is what makes the fold read as sitting at the
        // very top of the page. PaperUi.WrapForTopInset pads the host down by the status-bar/
        // display-cutout inset under enforced edge-to-edge (API 35+, same reasoning as
        // PaperUi.BottomBar's navigation-bar inset padding) — the shared version of the fix this
        // Activity originated; Post and Plate now use it too instead of their own fixed padding.
        var pageOverlayHost = PaperUi.WrapForTopInset(this, page);
        foreach (var area in areas)
        {
            area.Overlay = pageOverlayHost; // undo bars, notices, and action cards overlay the whole page
        }

        spread.AddView(pageOverlayHost, new LinearLayout.LayoutParams(
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

        ApplyPendingArea();
    }

    // Consumes a switch requested by a secondary screen's binder tap. Cleared unconditionally
    // so a stale request never fires on some later, unrelated resume.
    private void ApplyPendingArea()
    {
        var pending = PendingArea;
        PendingArea = null;
        if (pending is not { } index || index < 0 || index >= areas.Length || index == activeIndex)
        {
            return;
        }

        activeIndex = index;
        binder.SetActive(index);
        RenderContent();
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
        if (backCallback is not null)
        {
            OnBackInvokedDispatcher?.UnregisterOnBackInvokedCallback(backCallback);
        }

        playback.Release();
        base.OnDestroy();
    }

    public override void OnBackPressed()
    {
        // The area gets first refusal — only it knows whether it is drilled into
        // something. It raises its own redraw if it used the press. Pre-33 path; see
        // BackCallback for the API-33+ predictive-back path that reaches the same GoBack().
        if (!Active.GoBack())
        {
            base.OnBackPressed();
        }
    }

    // The API-33+ predictive-back path — see the registration in OnCreate for why this exists
    // alongside the deprecated OnBackPressed() override rather than instead of it.
    private sealed class BackCallback(ScoreActivity activity) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked()
        {
            if (!activity.Active.GoBack())
            {
                activity.Finish();
            }
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
