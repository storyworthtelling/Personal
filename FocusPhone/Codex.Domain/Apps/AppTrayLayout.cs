using Codex.Domain.Policy;

namespace Codex.Domain.Apps;

/// <summary>An installed, tray-eligible app: its package and Android category.</summary>
public record TrayApp(string PackageName, int AndroidCategory);

/// <summary>A non-empty category shelf and the apps placed on it, in display order.</summary>
public record TrayFolder(Category Category, IReadOnlyList<TrayApp> Apps);

/// <summary>
/// The tray as three ordered sections: the owner's own repo apps (kept loose at
/// the root), the non-empty category shelves, and the leftover loose apps.
/// </summary>
public record TrayModel(
    IReadOnlyList<TrayApp> RepoApps,
    IReadOnlyList<TrayFolder> Folders,
    IReadOnlyList<TrayApp> LooseApps);

/// <summary>
/// Builds the tray layout from the allowed apps and the owner's category catalog: the owner's
/// sibling apps (FocusPhone heads included) stay loose at the root; every other app is placed by the owner's
/// override, else the curated seed, else Android's declared category — and lands loose when none
/// of those name a category that still exists in the catalog. Apps in <paramref name="hidden"/> are
/// left out entirely. Folders come back in the catalog's
/// (owner-chosen) order, and empty ones are omitted, so the caller never renders a bare shelf.
/// </summary>
public static class AppTrayLayout
{
    public static TrayModel Build(
        IEnumerable<TrayApp> apps,
        IReadOnlyDictionary<string, string> overrides,
        CategoryCatalog catalog,
        HiddenApps? hidden = null)
    {
        var repoApps = new List<TrayApp>();
        var looseApps = new List<TrayApp>();
        var buckets = new Dictionary<string, List<TrayApp>>();

        foreach (var app in apps)
        {
            // Pulp's ticked streaming services (D92): reachable only through Pulp, so not on the
            // shelf at all, whatever the owner's override says.
            if (hidden is not null && hidden.Contains(app.PackageName))
            {
                continue;
            }

            // The launcher is the shelf itself; it is never one of the books on it.
            if (app.PackageName == PolicyContext.LauncherPackage)
            {
                continue;
            }

            // The owner's sibling apps (Post, Score, Plate, Index, Foreword, …) always sit loose
            // at the root — never foldered — so an override can't move them. Featuring one on home
            // takes it off this shelf (ShelfComposer), and removing it from home brings it back here.
            if (IsRepoApp(app.PackageName))
            {
                repoApps.Add(app);
                continue;
            }

            var categoryId = ResolveCategoryId(app, overrides);
            if (categoryId is null || !catalog.Contains(categoryId))
            {
                looseApps.Add(app);
                continue;
            }

            if (!buckets.TryGetValue(categoryId, out var members))
            {
                buckets[categoryId] = members = [];
            }
            members.Add(app);
        }

        var folders = catalog.Categories
            .Where(category => buckets.ContainsKey(category.Id))
            .Select(category => new TrayFolder(category, buckets[category.Id]))
            .ToList();

        return new TrayModel(repoApps, folders, looseApps);
    }

    /// <summary>
    /// True for the owner's own apps (every <c>com.storyworthtelling.*</c> package, FocusPhone heads
    /// included), which the shelf keeps loose at the root rather than foldered. The launcher itself is
    /// excluded before this is asked.
    /// </summary>
    public static bool IsRepoApp(string packageName) =>
        packageName.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal);

    private static string? ResolveCategoryId(TrayApp app, IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.TryGetValue(app.PackageName, out var overridden))
        {
            return overridden;
        }
        if (CategoryCatalog.CuratedSeed.TryGetValue(app.PackageName, out var seeded))
        {
            return seeded;
        }
        return CategoryCatalog.FromAndroidCategory(app.AndroidCategory);
    }
}
