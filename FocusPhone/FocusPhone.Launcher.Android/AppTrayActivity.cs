using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Ui;

namespace FocusPhone.Launcher;

/// <summary>
/// The swipe-up tray of launchable apps, filtered for distraction-free use:
/// our own apps (reached from the home tiles), blocked packages, browsers, and
/// games are excluded. Long-press blocks an app — the block is permanent
/// (survives reinstall), removes it from the tray immediately, and, once the
/// launcher is device owner, suspends it via a policy re-apply.
/// </summary>
[Activity(
    Label = "Apps",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class AppTrayActivity : ListActivity
{
    private List<ResolveInfo> apps = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperUi.StylePaperList(this, ListView!);
        LoadApps();

        ListView.ItemClick += (_, e) =>
        {
            var info = apps[e.Position].ActivityInfo!;
            var intent = PackageManager!.GetLaunchIntentForPackage(info.PackageName!);
            if (intent is not null)
            {
                StartActivity(intent);
            }
        };

        ListView.ItemLongClick += (_, e) =>
        {
            ConfirmBlock(apps[e.Position]);
            e.Handled = true;
        };
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

        ListAdapter = new PaperListAdapter(this,
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

    private void ConfirmBlock(ResolveInfo app)
    {
        var packageName = app.ActivityInfo!.PackageName!;
        var label = app.LoadLabel(PackageManager!).ToString();

        new AlertDialog.Builder(this)
            .SetTitle($"Block {label}?")!
            .SetMessage($"{label} stays blocked even if it's reinstalled. Unblocking needs admin mode and its 72-hour delay.")!
            .SetPositiveButton("Block", (_, _) =>
            {
                var store = new BlocklistStore(this);
                var categorizer = new Domain.Apps.AppCategorizer(store.Load());
                categorizer.Block(packageName);
                store.Save(categorizer.Export());
                PolicyManager.ApplyCurrentPolicy(this);
                LoadApps();
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }
}
