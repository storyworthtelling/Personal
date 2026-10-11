using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Domain;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using Pulp.Domain.Shell;
using Pulp.FirstRun;
using Pulp.Shell;

namespace Pulp;

/// <summary>
/// Pulp's app shell, and only the shell (mockup 01, D212): the page with its Serif header, the
/// binder gutter down the right edge with the six areas, and the pinned bottom bar each area can
/// fill. It holds no catalogue state of its own — each <see cref="PulpAreaPage"/> owns what it
/// shows, and <see cref="Pulp.Domain.Shell.PulpAreas"/> decides who the areas are.
///
/// <para>The shell is this class rather than a separately named one because its Java name is
/// registered explicitly from <see cref="PulpIdentity.MainActivityName"/>: the home-screen entry
/// points at it, so a rename of this class or namespace cannot move it (guarded by
/// <c>PulpInvariantsTests</c>). The launcher icon is named once, on <see cref="PulpApplication"/>.</para>
///
/// <para>A sub-page that draws the binder (the title page, Recently watched) opens the shell on the
/// tab tapped through <see cref="OpenOn"/>: an intent naming the area (<see cref="AreaRequest"/>),
/// sent ClearTop|SingleTop so the running shell under the sub-pages comes forward and reads it in
/// <see cref="OnNewIntent"/>, or a fresh one reads it in <see cref="OnCreate"/>. Score and Post hand
/// the area over in a static instead, because their sub-pages only ever finish back to the shell; a
/// Pulp sub-page can sit on another (Recently watched opens a title), and ClearTop closes the lot.</para>
///
/// <para>Laid out as drawn: the gutter runs the full height of the screen and the bottom bar sits
/// under the page only, to the left of the gutter — Score and Post run their bar under both.</para>
///
/// <para><b>The child's view is a mode of the shell</b> (D213, D179, D231). "Watch as a child" picks the
/// child or children (<see cref="ChildPicker"/>), and the shell then shows only the child's three areas
/// (<see cref="ChildViewAreas"/>), names who is watching on the line under the header, and tells every
/// area to read as them (<see cref="PulpAreaPage.ViewAs"/>). Every area a sub-page or an intent asks
/// for is routed through the session, so a Settings tab tapped on the title page's binder lands on the
/// child's home. "Back to me" returns to the device owner on the area they left (no PIN on the phone,
/// D43). The session is kept on this phone (<see cref="ChildViewStore"/>) so it survives the process.</para>
/// </summary>
[Activity(
    Name = PulpIdentity.MainActivityName,
    Label = "Pulp",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class MainActivity : Activity
{
    private readonly CancellationTokenSource lifetime = new();
    private PulpAreaPage[] all = [];
    private PulpAreaPage[] areas = [];
    private int activeIndex;
    private ChildViewSession? childView;
    private ChildViewStore store = null!;
    private TextView viewerLine = null!;
    private SyncWatch syncWatch = null!;
    private HouseholdView? household;

    private LinearLayout headerSlot = null!;
    private TextView greetingView = null!;
    private FrameLayout content = null!;
    private FrameLayout bottomBarSlot = null!;
    private BinderTabs binder = null!;

    private PulpAreaPage Active => areas[activeIndex];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        // Strip order is PulpAreas' order; the home (Watching) is first and is where Pulp opens.
        all =
        [
            new WatchingArea(this),
            new PlaylistsArea(this),
            new DiscoverArea(this),
            new SportsArea(this),
            new CalendarArea(this),
            new SettingsArea(this),
        ];
        foreach (var area in all)
        {
            var which = area;
            area.Changed += () => { if (Active == which) RenderContent(); };
        }

        var watching = all.OfType<WatchingArea>().Single();
        watching.ChildViewRequested += () => ChildPicker.Show(this, Active.Overlay, Active.Spec.Area, EnterChildView);
        watching.BackToMeRequested += BackToMe;

        store = new ChildViewStore(this);
        syncWatch = new SyncWatch(this, PulpServices.For(this), OnHouseholdMoved);
        childView = store.Load();
        foreach (var area in all)
        {
            area.ViewAs(childView);
        }

        areas = Showing();
        activeIndex = RequestedIndex(Intent) ?? 0;
        SetContentView(BuildLayout());
        RenderContent();
        OpenFirstRunIfNeeded();
    }

    /// <summary>The first run, once per launch, asked off the UI thread (Release D).</summary>
    private void OpenFirstRunIfNeeded()
    {
        var token = lifetime.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                if (await FirstRunLaunch.ShouldOpenAsync(this, token))
                {
                    RunOnUiThread(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            StartActivity(FirstRunActivity.For(this));
                        }
                    });
                }
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The shell went away before the answer came back; the next launch asks again.
            }
            catch (Exception exception)
            {
                Android.Util.Log.Warn("Pulp", "Whether first run is needed could not be decided: " + exception);
            }
        }, token);
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        base.OnDestroy();
    }

    /// <summary>The areas the binder shows now: all six, or the child's three (D231).</summary>
    private PulpAreaPage[] Showing()
    {
        var showing = ChildViewAreas.For(childView);
        return showing.Select(area => all.Single(page => page.Spec.Area == area)).ToArray();
    }

    private void EnterChildView(ChildViewSession session) => ChangeView(session, PulpArea.Watching);

    /// <summary>"Back to me" (D213): the device owner's view, on the area they left from.</summary>
    private void BackToMe()
    {
        if (childView is { } session)
        {
            ChangeView(null, session.BackToMe());
        }
    }

    private void ChangeView(ChildViewSession? session, PulpArea landOn)
    {
        foreach (var area in areas)
        {
            area.Pause();
        }

        store.Save(session);
        childView = session;
        foreach (var area in all)
        {
            area.ViewAs(session);
        }

        areas = Showing();
        activeIndex = IndexOf(childView?.Route(landOn) ?? landOn);
        binder.SetAreas(areas.Select(area => area.Tab), activeIndex);
        foreach (var area in areas)
        {
            area.Resume();
        }

        RenderContent();
    }

    private int IndexOf(PulpArea area)
    {
        var index = Array.FindIndex(areas, page => page.Spec.Area == area);
        return index < 0 ? 0 : index;
    }

    /// <summary>Opens the shell on the area a sub-page's binder tab at <paramref name="tabIndex"/> stands for, closing the sub-pages above it.</summary>
    public static void OpenOn(Activity from, int tabIndex)
    {
        ArgumentNullException.ThrowIfNull(from);
        if (AreaRequest.ForTab(tabIndex) is { } area)
        {
            OpenOn(from, area);
        }
    }

    /// <summary>The same, for a sub-page whose binder is not the full six (a child's three): it names the area, not a strip position.</summary>
    public static void OpenOn(Activity from, PulpArea area)
    {
        ArgumentNullException.ThrowIfNull(from);
        var intent = new Intent(from, typeof(MainActivity));
        intent.PutExtra(AreaRequest.Extra, AreaRequest.Word(area));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        from.StartActivity(intent);
        from.Finish();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        if (RequestedIndex(intent) is { } index)
        {
            SwitchTo(index);
        }
    }

    /// <summary>
    /// The area an intent asks for, as an index into the binder showing now. In a child's view the
    /// session routes it first (D231), so no request can land on an area the child does not see.
    /// </summary>
    private int? RequestedIndex(Intent? intent)
    {
        if (AreaRequest.Read(intent?.GetStringExtra(AreaRequest.Extra)) is not { } area)
        {
            return null;
        }

        return IndexOf(childView?.Route(area) ?? area);
    }

    /// <summary>A tab's switch: tapping the area already showing returns it to its root, so this is not guarded on the index having changed.</summary>
    private void SwitchTo(int index)
    {
        if (index == activeIndex)
        {
            while (Active.GoBack())
            {
            }
        }

        activeIndex = index;
        binder.SetActive(index);
        Active.Resume();
        RenderContent();
    }

    private View BuildLayout()
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        // Rebuilt per area: each has its own title, and the rule under it is the identity (§9).
        headerSlot = new LinearLayout(this) { Orientation = Orientation.Vertical };
        headerSlot.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        page.AddView(headerSlot);

        // Who is watching, under the header, in a child's view only (D179; mockup 06's "Watching as Robin").
        viewerLine = PaperUi.Label(this, string.Empty);
        viewerLine.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X1), edge, PaperTheme.Dp(this, DesignTokens.Space.X2));
        viewerLine.Visibility = ViewStates.Gone;
        page.AddView(viewerLine);

        // The owner's voice under the header, hidden until something is composed for it (D118).
        greetingView = PaperUi.Voice(this, string.Empty);
        greetingView.SetPadding(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X4));
        greetingView.Visibility = ViewStates.Gone;
        page.AddView(greetingView);

        content = new FrameLayout(this);
        content.SetPadding(edge, 0, edge, 0);
        page.AddView(content, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // The overlay host wraps the whole page, header included, and pads itself below the status
        // bar, so a dog-ear or an action card sits at the very top of the page.
        var pageOverlayHost = PaperUi.WrapForTopInset(this, page);
        foreach (var area in all)
        {
            area.Overlay = pageOverlayHost;
        }

        // The page over its bar, as one column to the left of the gutter (mockup 01).
        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(pageOverlayHost, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // A slot rather than a bar: each area supplies its own actions, and one with none leaves it empty.
        bottomBarSlot = new FrameLayout(this);
        column.AddView(bottomBarSlot, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var spread = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        spread.AddView(column, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.MatchParent) { Weight = 1 });

        binder = new BinderTabs(this);
        binder.SetAreas(areas.Select(area => area.Tab), activeIndex);
        binder.AreaSelected += SwitchTo;
        spread.AddView(binder, new LinearLayout.LayoutParams(
            PaperTheme.Dp(this, BinderTabs.WidthDp), ViewGroup.LayoutParams.MatchParent));

        return spread;
    }

    private void RenderContent()
    {
        headerSlot.RemoveAllViews();
        headerSlot.AddView(PaperUi.ScreenHeader(this, Active.Title, Active.Rule));

        viewerLine.Text = childView?.Label(NameFor) ?? string.Empty;
        viewerLine.Visibility = childView is null ? ViewStates.Gone : ViewStates.Visible;

        var greeting = Active.Greeting;
        greetingView.Text = greeting ?? string.Empty;
        greetingView.Visibility = string.IsNullOrWhiteSpace(greeting) ? ViewStates.Gone : ViewStates.Visible;

        content.RemoveAllViews();
        content.AddView(Active.BuildView(), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        bottomBarSlot.RemoveAllViews();
        if (Active.BuildBottomBar() is { } bar)
        {
            bottomBarSlot.AddView(bar);
        }
    }

    /// <summary>The household moved under the screen (a sync pass brought records home, or its status changed): names and the showing area are read again.</summary>
    private void OnHouseholdMoved()
    {
        ReadNames();
        Active.Resume();
    }

    /// <summary>
    /// Reads the household once off the UI thread so the line under the header names the children by
    /// the name the household holds (<see cref="HouseholdView.NameOf"/>), not by an id.
    /// </summary>
    private void ReadNames()
    {
        var token = lifetime.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var read = await PulpServices.For(this).HouseholdAsync(token);
                RunOnUiThread(() =>
                {
                    household = read;
                    if (childView is not null && !token.IsCancellationRequested)
                    {
                        RenderContent();
                    }
                });
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The shell went away; nothing is waiting for the names.
            }
            catch (Exception exception)
            {
                PulpServices.For(this).Diagnostics.Warn("shell", "The household could not be read for the names under the header.", exception);
            }
        }, token);
    }

    private string NameFor(ViewerId id) => household?.NameOf(id) ?? ViewerNaming.Display(id);

    protected override void OnResume()
    {
        base.OnResume();
        ReadNames();
        syncWatch.Resume();
        foreach (var area in areas)
        {
            area.Resume();
        }

        RenderContent();
    }

    protected override void OnPause()
    {
        base.OnPause();
        syncWatch.Pause();
        foreach (var area in areas)
        {
            area.Pause();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        foreach (var area in all)
        {
            area.OnActivityResult(requestCode, resultCode, data);
        }
    }

    public override void OnBackPressed()
    {
        // The area gets first refusal — only it knows whether it is drilled into something.
        if (!Active.GoBack())
        {
            base.OnBackPressed();
        }
    }
}
