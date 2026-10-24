using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Body;
using Constitution.Domain.Body;
using Constitution.Domain.Areas;
using Constitution.Domain.Layout;
using Constitution.Domain.Waiting;
using Constitution.FirstRun;
using Constitution.ReadBack;
using Constitution.WorkoutWeek;
using Constitution.Services;
using Constitution.Settings;
using Constitution.Waiting;
using Constitution.WorkoutsTab;

namespace Constitution;

/// <summary>
/// Constitution's app shell, and only the shell: the page with its Serif Black header, the binder
/// gutter down the right edge with the visible tabs, and the pinned bottom bar an area can fill.
/// It holds no plan, workout or meal state; each <see cref="IConstitutionArea"/> owns what it
/// shows, and <see cref="ConstitutionAreas"/> decides who the areas are.
///
/// <para>It is a base class because the launcher activity, <see cref="MainActivity"/>, keeps its
/// frozen Java name and simply is this shell.</para>
///
/// <para>Laid out as Pulp draws it: the gutter runs the full height of the screen and the bottom
/// bar sits under the page only, to the left of the gutter.</para>
/// </summary>
public abstract class ConstitutionActivity : Activity
{
    private IReadOnlyDictionary<string, IConstitutionArea> areasById = new Dictionary<string, IConstitutionArea>();
    private IReadOnlyList<IConstitutionArea> visible = [];
    private string activeId = ConstitutionAreas.All[0].Id;

    private PageFrame frame = null!;
    private BinderTabs binder = null!;
    private FrameLayout overlay = null!;
    private PillarStore pillarStore = null!;

    private SettingsArea? settingsArea;

    // First run: shown instead of the tabs until FirstRunModel says all three steps are satisfied.
    private FirstRunPage firstRun = null!;

    // The corner fold, and the Waiting page it turns to. The page is not an area: it takes the well
    // (on the crinkled ground) while the binder shows nothing lit, and any tab turns back.
    private DogEarHost dogEar = null!;
    private BackToPage backToPage = null!;
    private bool waitingOpen;

    // A signal's page (book p.16) turned to from a Body chart. Like Waiting it is a state of the shell rather than an
    // area: it takes the well and the header, the Body tab stays lit behind it, and back (or any tab) turns it away.
    private BodyArea? bodyArea;
    private BackToPage backToSignal = null!;
    private BodySignal? openSignal;

    private IConstitutionArea Active => areasById[activeId];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        pillarStore = new PillarStore(this);
        settingsArea = new SettingsArea(this);
        bodyArea = new BodyArea(this);
        bodyArea.SignalRequested += OpenSignal;
        firstRun = new FirstRunPage(this, RenderPage);
        var areas = new IConstitutionArea[]
        {
            new TodayArea(this),
            new WorkoutsArea(this),
            new MealsArea(this),
            bodyArea,
            settingsArea,
        };
        areasById = areas.ToDictionary(area => area.Definition.Id);
        foreach (var area in areas)
        {
            var which = area;
            area.Changed += () =>
            {
                // A pillar switched on or off hides or shows its tab at once, not at the next resume.
                RefreshTabs();
                if (Active == which)
                {
                    RenderPage();
                }
            };
        }

        SetContentView(BuildLayout());
        dogEar = new DogEarHost(this, overlay);
        dogEar.OpenWaiting += OpenWaiting;
        dogEar.UndoTurned += OnUndoTurned;
        backToPage = new BackToPage(this, CloseWaiting);
        backToSignal = new BackToPage(this, CloseSignal);
        RefreshTabs();
        RenderPage();

        // After a reset or reinstall, refill the connections from the Drive vault and then the Body history from its
        // backup, in that order and in the background (no-op without a passphrase); the page redraws if history landed.
        BodyBackups.RestoreAtLaunch(this, () => RunOnUiThread(() =>
        {
            if (!IsFinishing && !IsDestroyed)
            {
                RenderPage();
            }
        }));

        // Body's overnight read, under its frozen unique name; Keep means a relaunch changes nothing (C-7.1.3).
        BodySyncWorker.Schedule(this);

        // Yesterday's ink read off the tablet each morning after Foreword's run (C-2.2.3, D87), and any question it left
        // unanswered put back on the Waiting page (the board only lives as long as the process).
        ReadBackWorker.Schedule(this);
        ReadBackWaiting.Restore(this);

        // The week's workouts planned each morning before the read-back (P13), and once on open when the last plan is stale.
        WorkoutWeekWorker.Schedule(this);
        WorkoutWeekWorker.RunOnOpen(this);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        // The Google account picker opened from Settings answers here.
        settingsArea?.OnActivityResult(requestCode, resultCode, data);
        firstRun.OnActivityResult(requestCode, resultCode, data);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        // Health Connect's answer for the signal being allowed from its page.
        bodyArea?.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    protected override void OnResume()
    {
        base.OnResume();
        foreach (var area in areasById.Values)
        {
            area.Resume();
        }

        // Where background reads are not granted the app's own opening is the refresh (R5); off the UI thread.
        BodyServices.RefreshAtOpen(this);

        // The pillars may have changed since the shell was last on screen.
        RefreshTabs();
        RenderPage();
    }

    protected override void OnPause()
    {
        base.OnPause();
        dogEar.Pause();
        foreach (var area in areasById.Values)
        {
            area.Pause();
        }

        // A change still inside the backup's quiet window is written now: Android can end the process once the
        // app is in the background, and the debounce would otherwise lose the last write. Not waited for.
        BodyBackups.FlushOnPause(this);
    }

    private View BuildLayout()
    {
        var spread = new LinearLayout(this) { Orientation = Orientation.Horizontal };

        frame = new PageFrame(this);
        // The overlay wraps the whole page, header included, and pads itself down by the status
        // bar inset, so a dog-ear sits at the very top corner of the page.
        overlay = PaperUi.WrapForTopInset(this, frame);
        foreach (var area in areasById.Values)
        {
            area.Overlay = overlay;
        }

        spread.AddView(overlay, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent) { Weight = 1 });

        binder = new BinderTabs(this);
        binder.AreaSelected += OnTabSelected;
        spread.AddView(binder, new LinearLayout.LayoutParams(
            PaperTheme.Dp(this, BinderTabs.WidthDp), ViewGroup.LayoutParams.MatchParent));
        return spread;
    }

    private void OnTabSelected(int index)
    {
        if (index < 0 || index >= visible.Count)
        {
            return;
        }

        CloseWaitingState();
        CloseSignalState();
        activeId = visible[index].Definition.Id;
        binder.SetActive(index);
        TabLettering.Apply(binder, visible, index);
        RenderPage();
    }

    // Re-reads the pillars and rebuilds the strip from whatever ConstitutionAreas.Visible says.
    // If the area being shown has just been switched off, the page falls back to Today.
    private void RefreshTabs()
    {
        var shown = ConstitutionAreas.Visible(pillarStore.Read());
        visible = shown.Select(definition => areasById[definition.Id]).ToList();
        if (!visible.Any(area => area.Definition.Id == activeId))
        {
            activeId = ConstitutionAreas.All[0].Id;
        }

        var index = visible.ToList().FindIndex(area => area.Definition.Id == activeId);
        binder.SetAreas(visible.Select(area => area.Tab), index);
        TabLettering.Apply(binder, visible, index);
    }

    private void RenderPage()
    {
        if (firstRun.Current is not null)
        {
            RenderFirstRun();
            return;
        }

        if (waitingOpen)
        {
            RenderWaiting();
            dogEar.Refresh();
            return;
        }

        if (openSignal is { } signal && bodyArea is not null && Active == bodyArea)
        {
            RenderSignal(bodyArea, signal);
            dogEar.Refresh();
            return;
        }

        RenderArea();
        dogEar.Refresh();
    }

    private void RenderArea()
    {
        var active = Active;
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var header = PaperUi.ScreenHeader(this, active.Title, active.Tab.Hue);
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        if (active.Subtitle is { Length: > 0 } subtitle)
        {
            // Muted Serif under the title, in the header and so outside the well (D43).
            var line = new TextView(this) { Text = subtitle };
            line.SetTextSize(Android.Util.ComplexUnitType.Sp, DesignTokens.TypeSp.Heading);
            line.SetTextColor(PaperTheme.InkMuted);
            line.Typeface = PaperTheme.SerifSemibold(this);
            line.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X1), 0, 0);
            header.AddView(line, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        frame.SetHeader(header);

        // The well is bare: the area insets its own page to the screen edge (ConstitutionAreaBase.BuildView),
        // because the crinkled ground has to reach the well's own edges (D43).
        var well = new FrameLayout(this);
        well.AddView(active.BuildView(), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        frame.SetWell(well);

        frame.SetBar(active.BuildBottomBar());
    }

    // Plain paper, no tab lit, the step's action in the pinned bar (C-8.3.1).
    private void RenderFirstRun()
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var header = PaperUi.ScreenHeader(this, "Welcome", PaperTheme.Ink);
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        frame.SetHeader(header);

        var well = new FrameLayout(this);
        well.AddView(firstRun.BuildWell(), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        frame.SetWell(well);
        frame.SetBar(firstRun.BuildBar());

        binder.SetActive(-1);
        TabLettering.Apply(binder, visible, -1);
    }

    // ---- the dog-ear and the Waiting page (book p.18) ----

    private void OpenWaiting()
    {
        CloseSignalState();
        waitingOpen = true;
        backToPage.Register();
        RefreshTabs();
        RenderPage();
    }

    private void CloseWaiting()
    {
        CloseWaitingState();
        RefreshTabs();
        RenderPage();
    }

    private void CloseWaitingState()
    {
        waitingOpen = false;
        backToPage.Unregister();
    }

    // ---- a signal's page (book p.16) ----

    private void OpenSignal(BodySignal signal)
    {
        openSignal = signal;
        backToSignal.Register();
        RenderPage();
    }

    private void CloseSignal()
    {
        CloseSignalState();
        RenderPage();
    }

    private void CloseSignalState()
    {
        openSignal = null;
        backToSignal.Unregister();
    }

    // The signal's name as the Serif header, the page on the crinkled ground, and the pinned bar only while the chart is
    // waiting on Health Connect. The Body tab stays lit: this is a page of Body, not a place of its own.
    private void RenderSignal(BodyArea body, BodySignal signal)
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var header = PaperUi.ScreenHeader(this, body.SignalTitle(signal), body.Tab.Hue);
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        frame.SetHeader(header);

        var well = new FrameLayout(this);
        well.AddView(body.BuildSignalView(signal), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        frame.SetWell(well);
        frame.SetBar(body.BuildSignalBar(signal));
    }

    private void RenderWaiting()
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var header = PaperUi.ScreenHeader(this, "Waiting", PaperTheme.Ink);
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        frame.SetHeader(header);

        var model = ConnectionNoticeBoard.With(board => WaitingPageModel.From(board, DateTimeOffset.UtcNow));
        var page = WaitingPageView.Build(this, model, OnWaitingAction, OnWaitingChip);

        // Written straight onto the crinkled ground, in the app's own frame (D43). The ground is
        // reached through Today's painter, the seam an area exposes: only the area base class may
        // name the crinkle (SettingsIsPlainTests), and Settings must never lend its plain one.
        var ground = areasById[ConstitutionAreas.All[0].Id].GroundPainter;
        var well = new FrameLayout(this);
        well.AddView(ground.Paint(page), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        frame.SetWell(well);
        frame.SetBar(null);

        // No tab is lit behind the turned page.
        binder.SetActive(-1);
        TabLettering.Apply(binder, visible, -1);
    }

    // PairAgain and SignInAgain go to Settings, where the connections are mended. The other acts have
    // no screen to go to yet (the scan, the box and Keep arrive with Releases C-E), so a tap does nothing.
    private void OnWaitingAction(WaitingActionId action)
    {
        if (action is not (WaitingActionId.PairAgain or WaitingActionId.SignInAgain))
        {
            return;
        }

        var index = visible.ToList().FindIndex(area => area.Definition.Id == "settings");
        if (index >= 0)
        {
            OnTabSelected(index);
        }
    }

    private void OnWaitingChip(WaitingItem item, ChipChoice choice)
    {
        // The pick is the answer and the row simply goes; the read-back keeps the pick with the result that asked it,
        // so the question is not put back and whatever uses the answer can find it.
        ConnectionNoticeBoard.With(board => WaitingPageModel.Answer(board, item, choice));
        ReadBackWaiting.Answered(this, item.CauseKey, choice.Value);
        RenderPage();
    }

    // An undo fold was turned: the area that raised it reverses its act, then the page redraws.
    private void OnUndoTurned(string actKey)
    {
        foreach (var area in areasById.Values.OfType<IUndoableArea>())
        {
            area.UndoTurned(actKey);
        }

        RenderPage();
    }
}
