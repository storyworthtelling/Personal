using Android.Content;
using Android.Content.PM;
using Codex.Domain.Apps;
using Codex.Domain.Policy;

namespace Flyleaf;

/// <summary>
/// Builds the continuous bookcase's shelf list from what's installed: it filters the launchable
/// apps the same way the old tray did (our own four heads, blocked apps, browsers, and games are
/// excluded), places the rest with the owner's category catalog + overrides, and composes the
/// clock / home / sibling / category / loose shelves via the pure <see cref="ShelfComposer"/>.
/// The home's featured six and the usage-driven spine sizes are folded in here too.
/// </summary>
public class ShelfSceneBuilder(Context context)
{
    public IReadOnlyList<ShelfSpec> Build(CategoryCatalog catalog, HomeFeatureSet home)
    {
        var packageManager = context.PackageManager!;
        var launcherIntent = new Intent(Intent.ActionMain);
        launcherIntent.AddCategory(Intent.CategoryLauncher);

        var blocked = new BlocklistStore(context).Load().BlockedPackages.ToHashSet();
        var browsers = BrowserPackages(packageManager);

        var trayApps = new List<TrayApp>();
        var seen = new HashSet<string>();
        foreach (var resolveInfo in packageManager.QueryIntentActivities(launcherIntent, (PackageInfoFlags)0)
                     .OrderBy(r => r.LoadLabel(packageManager).ToString()))
        {
            var package = resolveInfo.ActivityInfo!.PackageName!;
            if (!seen.Add(package) || !IsAllowedInTray(resolveInfo, blocked, browsers))
            {
                continue;
            }
            var category = (int)(resolveInfo.ActivityInfo.ApplicationInfo?.Category ?? ApplicationCategories.Undefined);
            trayApps.Add(new TrayApp(package, category));
        }

        var tray = AppTrayLayout.Build(trayApps, new AppFolderStore(context).Load(), catalog);
        var usage = UsageStatsReader.ForegroundTime(context);
        return ShelfComposer.Compose(home, tray, usage);
    }

    private bool IsAllowedInTray(ResolveInfo resolveInfo, HashSet<string> blocked, HashSet<string> browsers)
    {
        var package = resolveInfo.ActivityInfo!.PackageName!;
        if (package == context.PackageName || PolicyContext.FocusPhonePackages.Contains(package))
        {
            return false; // our own heads are featured on the home shelf, never the tray
        }
        if (blocked.Contains(package) || browsers.Contains(package))
        {
            return false;
        }
        return resolveInfo.ActivityInfo.ApplicationInfo?.Category != ApplicationCategories.Game;
    }

    private static HashSet<string> BrowserPackages(PackageManager packageManager)
    {
        var view = new Intent(Intent.ActionView, Android.Net.Uri.Parse("https://example.com"));
        view.AddCategory(Intent.CategoryBrowsable);
        return packageManager.QueryIntentActivities(view, PackageInfoFlags.MatchAll)
            .Select(r => r.ActivityInfo!.PackageName!)
            .ToHashSet();
    }
}
