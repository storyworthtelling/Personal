using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Ui;

namespace FocusPhone.Launcher;

/// <summary>
/// The swipe-up tray of launchable apps, filtered for distraction-free use:
/// our own apps (reached from the home tiles), blocked packages, browsers, and
/// games are excluded. Long-press blocks an app immediately — undo-first per
/// DESIGN.md §7: the block happens at once and a 6-second undo bar offers the
/// reversal (the same unblock admin mode uses).
/// </summary>
[Activity(
    Label = "Apps",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class AppTrayActivity : Activity
{
    private List<ResolveInfo> apps = [];
    private ListView? listView;
    private FrameLayout? undoHost;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        SetContentView(BuildLayout());
        LoadApps();

        listView!.ItemClick += (_, e) =>
        {
            var info = apps[e.Position].ActivityInfo!;
            var intent = PackageManager!.GetLaunchIntentForPackage(info.PackageName!);
            if (intent is not null)
            {
                StartActivity(intent);
            }
        };

        listView.ItemLongClick += (_, e) =>
        {
            BlockWithUndo(apps[e.Position]);
            e.Handled = true;
        };
    }

    private View BuildLayout()
    {
        undoHost = new FrameLayout(this);

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        column.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        column.AddView(PaperUi.PageHeader(this, "Apps"));

        listView = new ListView(this);
        PaperUi.StylePaperList(this, listView);
        column.AddView(listView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        undoHost.AddView(column, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        return undoHost;
    }

    private void LoadApps()
    {
        var launcherIntent = new Intent(Intent.ActionMain);
        launcherIntent.AddCategory(Intent.CategoryLauncher);

        var blocked = new BlocklistStore(this).Load().BlockedPackages.ToHashSet();
        var browsers = BrowserPackages();

        apps = PackageManager!
            .QueryIntentActivities(launcherIntent, (PackageInfoFlags)0)
            .Where(resolveInfo => IsAllowedInTray(resolveInfo, blocked, browsers))
            .OrderBy(resolveInfo => resolveInfo.LoadLabel(PackageManager).ToString())
            .ToList();

        listView!.Adapter = new PaperListAdapter(this,
            apps.Select(resolveInfo => new PaperListRow(resolveInfo.LoadLabel(PackageManager).ToString()!)).ToList());
    }

    private bool IsAllowedInTray(ResolveInfo resolveInfo, HashSet<string> blocked, HashSet<string> browsers)
    {
        var package = resolveInfo.ActivityInfo!.PackageName!;
        if (package == PackageName || Domain.Policy.PolicyContext.FocusPhonePackages.Contains(package))
        {
            return false; // Our own apps are reached from the home tiles, not the tray.
        }
        if (blocked.Contains(package) || browsers.Contains(package))
        {
            return false;
        }
        return resolveInfo.ActivityInfo!.ApplicationInfo?.Category != ApplicationCategories.Game;
    }

    private HashSet<string> BrowserPackages()
    {
        var view = new Intent(Intent.ActionView, Android.Net.Uri.Parse("https://example.com"));
        view.AddCategory(Intent.CategoryBrowsable);
        return PackageManager!
            .QueryIntentActivities(view, PackageInfoFlags.MatchAll)
            .Select(resolveInfo => resolveInfo.ActivityInfo!.PackageName!)
            .ToHashSet();
    }

    // Undo-first: block immediately, then offer the reversal for six seconds.
    private void BlockWithUndo(ResolveInfo app)
    {
        var packageName = app.ActivityInfo!.PackageName!;
        var label = app.LoadLabel(PackageManager!).ToString();

        SetBlocked(packageName, blocked: true);
        UndoBar.Show(this, undoHost!, $"Blocked {label}.", onUndo: () =>
            SetBlocked(packageName, blocked: false));
    }

    private void SetBlocked(string packageName, bool blocked)
    {
        var store = new BlocklistStore(this);
        var categorizer = new Domain.Apps.AppCategorizer(store.Load());
        if (blocked)
        {
            categorizer.Block(packageName);
        }
        else
        {
            categorizer.Unblock(packageName);
        }
        store.Save(categorizer.Export());
        PolicyManager.ApplyCurrentPolicy(this);
        LoadApps();
    }
}
