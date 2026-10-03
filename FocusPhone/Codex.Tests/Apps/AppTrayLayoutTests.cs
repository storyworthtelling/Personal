using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class AppTrayLayoutTests
{
    // Android's ApplicationInfo.Category values (stable ints), named so the tests read intentionally.
    private const int Undefined = -1;
    private const int Audio = 1;
    private const int Maps = 6;

    private static readonly IReadOnlyDictionary<string, string> NoOverrides = new Dictionary<string, string>();
    private static readonly CategoryCatalog Catalog = CategoryCatalog.Seed();

    private static TrayApp App(string package, int category = Undefined) => new(package, category);

    private static TrayModel Build(IEnumerable<TrayApp> apps, IReadOnlyDictionary<string, string>? overrides = null) =>
        AppTrayLayout.Build(apps, overrides ?? NoOverrides, Catalog);

    [Fact]
    public void RepoApps_GoToRoot_NotFoldersOrLoose()
    {
        var model = Build(
            [App("com.storyworthtelling.storyboarder"), App("com.storyworthtelling.index"),
             App("com.storyworthtelling.dailyloader")]);

        Assert.Equal(
            new[] { "com.storyworthtelling.storyboarder", "com.storyworthtelling.index", "com.storyworthtelling.dailyloader" },
            model.RepoApps.Select(a => a.PackageName));
        Assert.Empty(model.Folders);
        Assert.Empty(model.LooseApps);
    }

    [Fact]
    public void FocusPhoneApps_AreExcludedEntirely()
    {
        var model = Build(
            [App("com.storyworthtelling.focusphone.audio"), App("com.storyworthtelling.focusphone.communication"),
             App("com.storyworthtelling.focusphone.launcher")]);

        Assert.Empty(model.RepoApps);
        Assert.Empty(model.Folders);
        Assert.Empty(model.LooseApps);
    }

    [Fact]
    public void CuratedSeed_PlacesKnownAppIntoItsShelf()
    {
        var model = Build([App("com.amazon.mShop.android.shopping")]);

        var folder = Assert.Single(model.Folders);
        Assert.Equal(CategoryCatalog.Shopping, folder.Category.Id);
        Assert.Equal("com.amazon.mShop.android.shopping", Assert.Single(folder.Apps).PackageName);
        Assert.Empty(model.LooseApps);
    }

    [Fact]
    public void AndroidCategory_PlacesAppWhenNotInCuratedSeed()
    {
        var model = Build([App("com.unknown.mapthing", Maps), App("com.unknown.podcaster", Audio)]);

        Assert.Equal(CategoryCatalog.Travel, model.Folders.Single(f => f.Apps.Any(a => a.PackageName == "com.unknown.mapthing")).Category.Id);
        Assert.Equal(CategoryCatalog.Media, model.Folders.Single(f => f.Apps.Any(a => a.PackageName == "com.unknown.podcaster")).Category.Id);
    }

    [Fact]
    public void Override_WinsOverCuratedSeedAndCategory()
    {
        var overrides = new Dictionary<string, string>
        {
            ["com.amazon.mShop.android.shopping"] = CategoryCatalog.Media, // curated says Shopping
            ["com.unknown.mapthing"] = CategoryCatalog.Finance,           // category (Maps) says Travel
        };

        var model = Build([App("com.amazon.mShop.android.shopping"), App("com.unknown.mapthing", Maps)], overrides);

        Assert.Equal(CategoryCatalog.Media, model.Folders.Single(f => f.Apps.Any(a => a.PackageName == "com.amazon.mShop.android.shopping")).Category.Id);
        Assert.Equal(CategoryCatalog.Finance, model.Folders.Single(f => f.Apps.Any(a => a.PackageName == "com.unknown.mapthing")).Category.Id);
    }

    [Fact]
    public void OverrideToARemovedCategory_FallsLoose()
    {
        // The owner removed the Finance shelf but an old override still points there.
        var catalog = CategoryCatalog.Seed().WithRemoved(CategoryCatalog.Finance);
        var overrides = new Dictionary<string, string> { ["com.some.bank"] = CategoryCatalog.Finance };

        var model = AppTrayLayout.Build([App("com.some.bank")], overrides, catalog);

        Assert.Empty(model.Folders);
        Assert.Equal("com.some.bank", Assert.Single(model.LooseApps).PackageName);
    }

    [Fact]
    public void OverrideOnRepoApp_IsIgnored_RepoAppStaysAtRoot()
    {
        var overrides = new Dictionary<string, string> { ["com.storyworthtelling.index"] = CategoryCatalog.Finance };

        var model = Build([App("com.storyworthtelling.index")], overrides);

        Assert.Equal("com.storyworthtelling.index", Assert.Single(model.RepoApps).PackageName);
        Assert.Empty(model.Folders);
    }

    [Fact]
    public void UnmatchedApp_SitsLoose()
    {
        var model = Build([App("com.unknown.mystery", Undefined)]);

        Assert.Empty(model.Folders);
        Assert.Equal("com.unknown.mystery", Assert.Single(model.LooseApps).PackageName);
    }

    [Fact]
    public void EmptyFolders_AreOmitted()
    {
        var model = Build([App("com.amazon.mShop.android.shopping")]);

        Assert.Equal(CategoryCatalog.Shopping, Assert.Single(model.Folders).Category.Id);
    }

    [Fact]
    public void Folders_AreReturnedInCatalogOrder()
    {
        var overrides = new Dictionary<string, string>
        {
            ["com.a.media"] = CategoryCatalog.Media,
            ["com.b.finance"] = CategoryCatalog.Finance,
            ["com.c.shopping"] = CategoryCatalog.Shopping,
        };
        var model = Build([App("com.a.media"), App("com.b.finance"), App("com.c.shopping")], overrides);

        // Catalog seed order is Finance(0) < Shopping(2) < Media(7).
        Assert.Equal(
            new[] { CategoryCatalog.Finance, CategoryCatalog.Shopping, CategoryCatalog.Media },
            model.Folders.Select(f => f.Category.Id));
    }

    [Fact]
    public void Folders_FollowAnOwnerReorder()
    {
        var catalog = CategoryCatalog.Seed().Reordered([CategoryCatalog.Media, CategoryCatalog.Finance]);
        var overrides = new Dictionary<string, string>
        {
            ["com.a.media"] = CategoryCatalog.Media,
            ["com.b.finance"] = CategoryCatalog.Finance,
        };

        var model = AppTrayLayout.Build([App("com.a.media"), App("com.b.finance")], overrides, catalog);

        Assert.Equal(new[] { CategoryCatalog.Media, CategoryCatalog.Finance }, model.Folders.Select(f => f.Category.Id));
    }

    [Fact]
    public void AppOrderWithinFolderAndRoot_IsPreserved()
    {
        var overrides = new Dictionary<string, string>
        {
            ["com.first.finance"] = CategoryCatalog.Finance,
            ["com.second.finance"] = CategoryCatalog.Finance,
        };
        var model = Build(
            [App("com.first.finance"), App("com.second.finance"), App("com.first.loose"), App("com.second.loose")],
            overrides);

        Assert.Equal(new[] { "com.first.finance", "com.second.finance" },
            model.Folders.Single().Apps.Select(a => a.PackageName));
        Assert.Equal(new[] { "com.first.loose", "com.second.loose" },
            model.LooseApps.Select(a => a.PackageName));
    }
}
