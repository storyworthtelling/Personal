using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Areas;
using Constitution.Domain.Layout;
using Constitution.Services;

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

    private IConstitutionArea Active => areasById[activeId];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        pillarStore = new PillarStore(this);
        settingsArea = new SettingsArea(this);
        var areas = new IConstitutionArea[]
        {
            new TodayArea(this),
            new WorkoutsArea(this),
            new MealsArea(this),
            new BodyArea(this),
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
        RefreshTabs();
        RenderPage();

        // After a reset or reinstall, refill the connections from the Drive vault (background; no-op without a passphrase).
        ConstitutionVaultRunner.RestoreAtLaunch(this);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        // The Google account picker opened from Settings answers here.
        settingsArea?.OnActivityResult(requestCode, resultCode, data);
    }

    protected override void OnResume()
    {
        base.OnResume();
        foreach (var area in areasById.Values)
        {
            area.Resume();
        }

        // The pillars may have changed since the shell was last on screen.
        RefreshTabs();
        RenderPage();
    }

    protected override void OnPause()
    {
        base.OnPause();
        foreach (var area in areasById.Values)
        {
            area.Pause();
        }
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
        var active = Active;
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var header = PaperUi.ScreenHeader(this, active.Title, active.Tab.Hue);
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        frame.SetHeader(header);

        // The well is bare: the area insets its own page to the screen edge (ConstitutionAreaBase.BuildView),
        // because the crinkled ground has to reach the well's own edges (D43).
        var well = new FrameLayout(this);
        well.AddView(active.BuildView(), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        frame.SetWell(well);

        frame.SetBar(active.BuildBottomBar());
    }
}
