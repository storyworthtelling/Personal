using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain;
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
/// <para>Laid out as drawn: the gutter runs the full height of the screen and the bottom bar sits
/// under the page only, to the left of the gutter — Score and Post run their bar under both.</para>
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
    private PulpAreaPage[] areas = [];
    private int activeIndex;

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
        areas =
        [
            new WatchingArea(this),
            new PlaylistsArea(this),
            new DiscoverArea(this),
            new SportsArea(this),
            new CalendarArea(this),
            new SettingsArea(this),
        ];
        foreach (var area in areas)
        {
            var which = area;
            area.Changed += () => { if (Active == which) RenderContent(); };
        }

        SetContentView(BuildLayout());
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
        foreach (var area in areas)
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
        binder.AreaSelected += index =>
        {
            // Tapping the tab you are already on returns that area to its root, so this is not
            // guarded on the index having changed.
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
        };
        spread.AddView(binder, new LinearLayout.LayoutParams(
            PaperTheme.Dp(this, BinderTabs.WidthDp), ViewGroup.LayoutParams.MatchParent));

        return spread;
    }

    private void RenderContent()
    {
        headerSlot.RemoveAllViews();
        headerSlot.AddView(PaperUi.ScreenHeader(this, Active.Title, Active.Rule));

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

    protected override void OnResume()
    {
        base.OnResume();
        foreach (var area in areas)
        {
            area.Resume();
        }

        RenderContent();
    }

    protected override void OnPause()
    {
        base.OnPause();
        foreach (var area in areas)
        {
            area.Pause();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        foreach (var area in areas)
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
