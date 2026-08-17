using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class AppTrayLayoutTests
{
    // Android's ApplicationInfo.Category values (stable ints), named here so the
    // tests read intentionally rather than as magic numbers.
    private const int Undefined = -1;
    private const int Audio = 1;
    private const int Maps = 6;
    private const int Productivity = 7;

    private static readonly IReadOnlyDictionary<string, AppFolder> NoOverrides =
        new Dictionary<string, AppFolder>();

    private static TrayApp App(string package, int category = Undefined) => new(package, category);

    [Fact]
    public void RepoApps_GoToRoot_NotFoldersOrLoose()
    {
        var model = AppTrayLayout.Build(
            [App("com.storyworthtelling.storyboarder"), App("com.storyworthtelling.ledger"),
             App("com.storyworthtelling.dailyloader")],
            NoOverrides);

        Assert.Equal(
            new[] { "com.storyworthtelling.storyboarder", "com.storyworthtelling.ledger", "com.storyworthtelling.dailyloader" },
            model.RepoApps.Select(a => a.PackageName));
        Assert.Empty(model.Folders);
        Assert.Empty(model.LooseApps);
    }

    [Fact]
    public void FocusPhoneApps_AreExcludedEntirely()
    {
        var model = AppTrayLayout.Build(
            [App("com.storyworthtelling.focusphone.audio"), App("com.storyworthtelling.focusphone.communication"),
             App("com.storyworthtelling.focusphone.launcher")],
            NoOverrides);

        Assert.Empty(model.RepoApps);
        Assert.Empty(model.Folders);
        Assert.Empty(model.LooseApps);
    }

    [Fact]
    public void CuratedSeed_PlacesKnownAppIntoItsFolder()
    {
        var model = AppTrayLayout.Build([App("com.amazon.mShop.android.shopping")], NoOverrides);

        var folder = Assert.Single(model.Folders);
        Assert.Equal(AppFolder.Shopping, folder.Folder);
        Assert.Equal("com.amazon.mShop.android.shopping", Assert.Single(folder.Apps).PackageName);
        Assert.Empty(model.LooseApps);
    }

    [Fact]
    public void AndroidCategory_PlacesAppWhenNotInCuratedSeed()
    {
        var model = AppTrayLayout.Build(
            [App("com.unknown.mapthing", Maps), App("com.unknown.podcaster", Audio)],
            NoOverrides);

        Assert.Equal(AppFolder.Travel, model.Folders.Single(f => f.Apps.Any(a => a.PackageName == "com.unknown.mapthing")).Folder);
        Assert.Equal(AppFolder.Media, model.Folders.Single(f => f.Apps.Any(a => a.PackageName == "com.unknown.podcaster")).Folder);
    }

    [Fact]
    public void Override_WinsOverCuratedSeedAndCategory()
    {
        var overrides = new Dictionary<string, AppFolder>
        {
            ["com.amazon.mShop.android.shopping"] = AppFolder.Media, // curated says Shopping
            ["com.unknown.mapthing"] = AppFolder.Finance,           // category (Maps) says Travel
        };

        var model = AppTrayLayout.Build(
            [App("com.amazon.mShop.android.shopping"), App("com.unknown.mapthing", Maps)],
            overrides);

        Assert.Equal(AppFolder.Media, model.Folders.Single(f => f.Apps.Any(a => a.PackageName == "com.amazon.mShop.android.shopping")).Folder);
        Assert.Equal(AppFolder.Finance, model.Folders.Single(f => f.Apps.Any(a => a.PackageName == "com.unknown.mapthing")).Folder);
    }

    [Fact]
    public void OverrideOnRepoApp_IsIgnored_RepoAppStaysAtRoot()
    {
        var overrides = new Dictionary<string, AppFolder> { ["com.storyworthtelling.ledger"] = AppFolder.Finance };

        var model = AppTrayLayout.Build([App("com.storyworthtelling.ledger")], overrides);

        Assert.Equal("com.storyworthtelling.ledger", Assert.Single(model.RepoApps).PackageName);
        Assert.Empty(model.Folders);
    }

    [Fact]
    public void UnmatchedApp_SitsLoose()
    {
        var model = AppTrayLayout.Build([App("com.unknown.mystery", Undefined)], NoOverrides);

        Assert.Empty(model.Folders);
        Assert.Equal("com.unknown.mystery", Assert.Single(model.LooseApps).PackageName);
    }

    [Fact]
    public void EmptyFolders_AreOmitted()
    {
        // Only a Shopping app is present, so no other folder should appear.
        var model = AppTrayLayout.Build([App("com.amazon.mShop.android.shopping")], NoOverrides);

        Assert.Equal(AppFolder.Shopping, Assert.Single(model.Folders).Folder);
    }

    [Fact]
    public void Folders_AreReturnedInMetadataSortOrder()
    {
        // Deliberately supply apps whose folders are out of sort order in the input.
        var overrides = new Dictionary<string, AppFolder>
        {
            ["com.a.media"] = AppFolder.Media,
            ["com.b.finance"] = AppFolder.Finance,
            ["com.c.shopping"] = AppFolder.Shopping,
        };
        var model = AppTrayLayout.Build(
            [App("com.a.media"), App("com.b.finance"), App("com.c.shopping")], overrides);

        var expected = AppFolders.All
            .Where(i => i.Folder is AppFolder.Finance or AppFolder.Shopping or AppFolder.Media)
            .Select(i => i.Folder);
        Assert.Equal(expected, model.Folders.Select(f => f.Folder));
    }

    [Fact]
    public void AppOrderWithinFolderAndRoot_IsPreserved()
    {
        var overrides = new Dictionary<string, AppFolder>
        {
            ["com.first.finance"] = AppFolder.Finance,
            ["com.second.finance"] = AppFolder.Finance,
        };
        var model = AppTrayLayout.Build(
            [App("com.first.finance"), App("com.second.finance"),
             App("com.first.loose"), App("com.second.loose")],
            overrides);

        Assert.Equal(new[] { "com.first.finance", "com.second.finance" },
            model.Folders.Single().Apps.Select(a => a.PackageName));
        Assert.Equal(new[] { "com.first.loose", "com.second.loose" },
            model.LooseApps.Select(a => a.PackageName));
    }
}
