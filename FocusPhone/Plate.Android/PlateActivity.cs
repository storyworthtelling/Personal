using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;

namespace Plate;

/// <summary>
/// Plate's app shell, and only the shell: the frame, the binder gutter, and which area is
/// showing. It holds no capture, page or setting state of its own — each
/// <see cref="PlateArea"/> owns what it shows.
///
/// <para>The one thing no area can own is the camera, because two of them want it and a
/// sensor cannot be bound twice. So the shell holds it (<see cref="PlateCamera"/>) and
/// hands it to whichever area is on screen.</para>
///
/// <para>There is deliberately no voiced greeting here. Plate's voice belongs to the
/// Darkroom's captions and its empty state, which is Release C; a greeting over a live
/// viewfinder would be the voice standing next to content, which §14 rules out.</para>
/// </summary>
[Activity(
    Label = "Plate",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper.AppCompat")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class PlateActivity : AppCompatActivity
{
    /// <summary>The one holder of the camera. Areas ask it for a preview; nobody else binds.</summary>
    public PlateCamera Camera { get; private set; } = null!;

    // One Google sign-in for the whole head, and it exists for one reason: the encrypted
    // secrets vault, which is where the reMarkable's device token comes back from after a
    // reset. Plate has no account of its own and wants none.
    public VoiceGoogleAccount Account { get; } = new();

    /// <summary>The tablet. Scan's third door, and the only one that is not another app.</summary>
    public PlateRemarkable Remarkable { get; private set; } = null!;

    private PlateArea[] areas = [];
    private int activeIndex;

    private FrameLayout content = null!;
    private FrameLayout bottomBarSlot = null!;
    private BinderTabs binder = null!;
    private LinearLayout headerSlot = null!;

    private PlateArea Active => areas[activeIndex];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        Camera = new PlateCamera(this);
        Remarkable = new PlateRemarkable(Account);
        areas =
        [
            new CameraArea(this),
            new ScanArea(this),
            new SettingsArea(this),
        ];
        foreach (var area in areas)
        {
            var which = area;
            area.Changed += () => { if (Active == which) RenderContent(); };
        }

        SetContentView(BuildLayout());

        var wanted = new[]
        {
            Android.Manifest.Permission.Camera,
            // Sound on a video. Refused, recording still works — silently.
            Android.Manifest.Permission.RecordAudio,
        };
        var toRequest = wanted
            .Where(permission => CheckSelfPermission(permission)
                != Android.Content.PM.Permission.Granted)
            .ToArray();
        if (toRequest.Length > 0)
        {
            RequestPermissions(toRequest, 0);
        }

        // After a reinstall or the cutover's factory reset, refill the reMarkable pairing
        // from the shared vault. Best-effort and off the UI thread; Settings pairs by hand
        // when it cannot happen.
        _ = Remarkable.RestoreFromVault();

        CrashReporter.ShowPendingCrash(this);
        RenderContent();
        Active.OnShown(true);
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        // The page, and the binder gutter down its right edge. Everything above the bottom
        // bar lives inside the page; the tabs sit outside it, which is what stops them ever
        // overlapping content (§9's reserved gutter). The bottom bar is added to the root
        // after the spread, so it spans the full width, under the gutter too.
        var spread = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        headerSlot = new LinearLayout(this) { Orientation = Orientation.Vertical };
        headerSlot.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        page.AddView(headerSlot);

        content = new FrameLayout(this);
        foreach (var area in areas)
        {
            area.Overlay = content; // dog-ears and action cards overlay the content frame
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
            // Tapping the tab you are already on returns that area to its root, which is why
            // this is not guarded on the index having changed.
            if (index == activeIndex)
            {
                while (Active.GoBack())
                {
                }
                RenderContent();
                return;
            }

            Active.OnShown(false);
            activeIndex = index;
            binder.SetActive(index);
            RenderContent();
            Active.OnShown(true);
        };
        spread.AddView(binder, new LinearLayout.LayoutParams(
            PaperTheme.Dp(this, BinderTabs.WidthDp), ViewGroup.LayoutParams.MatchParent));

        root.AddView(spread, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // A slot rather than a bar: each area supplies its own actions, and an area with
        // none simply leaves it empty.
        bottomBarSlot = new FrameLayout(this);
        root.AddView(bottomBarSlot);
        return root;
    }

    private void RenderContent()
    {
        headerSlot.RemoveAllViews();
        headerSlot.AddView(PaperUi.ScreenHeader(this, Active.Title, Active.Tab.Hue));

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
        Active.OnShown(true);
    }

    protected override void OnPause()
    {
        base.OnPause();
        foreach (var area in areas)
        {
            area.Pause();
        }
    }

    public override void OnBackPressed()
    {
        // The area gets first refusal — only it knows whether it is drilled into something.
        if (Active.GoBack())
        {
            RenderContent();
            return;
        }
        base.OnBackPressed();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        foreach (var area in areas)
        {
            area.OnPermissionsResult();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == VoiceGoogleAccount.RequestCode)
        {
            VoiceGoogleAccount.HandleChooserResult(resultCode, data);
        }

        foreach (var area in areas)
        {
            area.OnActivityResult(requestCode, resultCode, data);
        }
    }
}
