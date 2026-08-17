using Codex.Domain.Policy;

namespace Codex.Domain.Apps;

/// <summary>An installed, tray-eligible app: its package and Android category.</summary>
public record TrayApp(string PackageName, int AndroidCategory);

/// <summary>A non-empty folder and the apps placed in it, in display order.</summary>
public record TrayFolder(AppFolder Folder, IReadOnlyList<TrayApp> Apps);

/// <summary>
/// The tray as three ordered sections: the owner's own repo apps (kept loose at
/// the root), the non-empty category folders, and the leftover loose apps.
/// </summary>
public record TrayModel(
    IReadOnlyList<TrayApp> RepoApps,
    IReadOnlyList<TrayFolder> Folders,
    IReadOnlyList<TrayApp> LooseApps);

/// <summary>
/// Builds the tray layout from the allowed apps: the owner's non-FocusPhone
/// sibling apps stay loose at the root; every other app is placed by the owner's
/// override, else the curated seed, else Android's declared category — and lands
/// loose when none of those decide. Folders that end up empty are omitted, so
/// the caller never renders a folder with nothing in it.
/// </summary>
public static class AppTrayLayout
{
    public static TrayModel Build(IEnumerable<TrayApp> apps, IReadOnlyDictionary<string, AppFolder> overrides)
    {
        var repoApps = new List<TrayApp>();
        var looseApps = new List<TrayApp>();
        var buckets = new Dictionary<AppFolder, List<TrayApp>>();

        foreach (var app in apps)
        {
            // The four FocusPhone apps live on the home tiles, never the tray.
            // The tray already filters them out; drop them here too so the layout
            // is correct on its own.
            if (PolicyContext.FocusPhonePackages.Contains(app.PackageName))
            {
                continue;
            }

            // The owner's other apps (Storyboarder, Ledger, DailyLoader, …) always
            // sit loose at the root — never foldered — so an override can't move them.
            if (IsRepoApp(app.PackageName))
            {
                repoApps.Add(app);
                continue;
            }

            var folder = ResolveFolder(app, overrides);
            if (folder is null)
            {
                looseApps.Add(app);
                continue;
            }

            if (!buckets.TryGetValue(folder.Value, out var members))
            {
                buckets[folder.Value] = members = [];
            }
            members.Add(app);
        }

        var folders = AppFolders.All
            .Where(info => buckets.ContainsKey(info.Folder))
            .Select(info => new TrayFolder(info.Folder, buckets[info.Folder]))
            .ToList();

        return new TrayModel(repoApps, folders, looseApps);
    }

    private static bool IsRepoApp(string packageName) =>
        packageName.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal)
        && !PolicyContext.FocusPhonePackages.Contains(packageName);

    private static AppFolder? ResolveFolder(TrayApp app, IReadOnlyDictionary<string, AppFolder> overrides)
    {
        if (overrides.TryGetValue(app.PackageName, out var overridden))
        {
            return overridden;
        }
        if (AppFolders.CuratedSeed.TryGetValue(app.PackageName, out var seeded))
        {
            return seeded;
        }
        return AppFolders.FromAndroidCategory(app.AndroidCategory);
    }
}
