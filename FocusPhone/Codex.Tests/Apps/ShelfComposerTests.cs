using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class ShelfComposerTests
{
    private static readonly HomeFeatureSet Home = HomeFeatureSet.Default;
    private static readonly IReadOnlyDictionary<string, long> NoUsage = new Dictionary<string, long>();

    private static TrayApp App(string package) => new(package, -1);

    private static TrayFolder Folder(string id, string name, string glyph, params string[] apps) =>
        new(new Category(id, name, glyph, 0, Builtin: true), apps.Select(App).ToList());

    [Fact]
    public void ClockThenHome_LeadTheShelves_TwoBooksPerShelf_AllMarkedHome()
    {
        var shelves = ShelfComposer.Compose(Home, new TrayModel([], [], []), NoUsage);

        // The clock plus three cover-out home shelves of two books each (six featured apps), all Home.
        Assert.Equal(ShelfFace.Clock, shelves[0].Face);
        Assert.True(shelves[0].Home);
        var homeCoverShelves = shelves.Skip(1).Take(3).ToList();
        Assert.All(homeCoverShelves, s =>
        {
            Assert.Equal(ShelfFace.CoverOut, s.Face);
            Assert.True(s.Home);
            Assert.True(s.Entries.Count <= 2);
        });
        Assert.Equal(Home.Packages, homeCoverShelves.SelectMany(s => s.Entries).Select(e => e.Key));
    }

    [Fact]
    public void Order_IsClockHomeSiblingCategoriesLoose()
    {
        var tray = new TrayModel(
            RepoApps: [App("com.storyworthtelling.ledger")],
            Folders: [Folder(CategoryCatalog.Finance, "Finance", "folder_finance", "com.some.bank")],
            LooseApps: [App("com.unknown.mystery")]);

        var shelves = ShelfComposer.Compose(Home, tray, NoUsage);

        // Clock, three home cover shelves (2 each), sibling, category, loose.
        Assert.Collection(shelves,
            s => Assert.Equal(ShelfFace.Clock, s.Face),
            s => { Assert.Equal(ShelfFace.CoverOut, s.Face); Assert.True(s.Home); },   // home 1
            s => { Assert.Equal(ShelfFace.CoverOut, s.Face); Assert.True(s.Home); },   // home 2
            s => { Assert.Equal(ShelfFace.CoverOut, s.Face); Assert.True(s.Home); },   // home 3
            s => { Assert.Equal(ShelfFace.CoverOut, s.Face); Assert.False(s.Home); Assert.Equal("com.storyworthtelling.ledger", s.Entries[0].Key); }, // sibling
            s => { Assert.Equal(ShelfFace.SpineOut, s.Face); Assert.Equal("Finance", s.BookendName); }, // category
            s => { Assert.Equal(ShelfFace.SpineOut, s.Face); Assert.Null(s.BookendName); Assert.Equal("com.unknown.mystery", s.Entries[0].Key); }); // loose
    }

    [Fact]
    public void SiblingAndLooseShelves_AreDropped_WhenEmpty()
    {
        var tray = new TrayModel([], [Folder(CategoryCatalog.Media, "Media", "folder_media", "com.a")], []);

        var shelves = ShelfComposer.Compose(Home, tray, NoUsage);

        // Clock, three home cover shelves, one category — no sibling, no loose.
        Assert.Equal(5, shelves.Count);
        Assert.Equal(ShelfFace.SpineOut, shelves[4].Face);
    }

    [Fact]
    public void CategoryShelves_CarryTheirBookendGlyphAndName()
    {
        var tray = new TrayModel([], [Folder(CategoryCatalog.Travel, "Travel & Transit", "folder_travel", "com.waze")], []);

        var category = ShelfComposer.Compose(Home, tray, NoUsage)[4];

        Assert.Equal("folder_travel", category.BookendGlyph);
        Assert.Equal("Travel & Transit", category.BookendName);
    }

    [Fact]
    public void FeaturedApps_AreNotShownOnAnyTrayShelf()
    {
        // Post is featured on home, Maps too; the tray still holds them as installed apps.
        var tray = AppTrayLayout.Build(
            [new TrayApp(HomeFeatureSet.CommunicationPackage, -1), new TrayApp(HomeFeatureSet.MapsPackage, -1),
             new TrayApp("com.storyworthtelling.ledger", -1), new TrayApp("com.unknown.mystery", -1)],
            new Dictionary<string, string>(),
            CategoryCatalog.Seed());

        var shelves = ShelfComposer.Compose(Home, tray, NoUsage);

        var onTray = shelves.Skip(4).SelectMany(s => s.Entries).Select(e => e.Key).ToList();
        Assert.DoesNotContain(HomeFeatureSet.CommunicationPackage, onTray);
        Assert.DoesNotContain(HomeFeatureSet.MapsPackage, onTray);
        Assert.Contains("com.storyworthtelling.ledger", onTray);
        Assert.Contains("com.unknown.mystery", onTray);
    }

    [Fact]
    public void ReplacingAFeaturedHeadApp_PutsItBackOnTheCodexShelf()
    {
        var installed = new[] { new TrayApp(HomeFeatureSet.CommunicationPackage, -1), new TrayApp("com.acme.newmail", -1) };
        var overrides = new Dictionary<string, string>();
        var catalog = CategoryCatalog.Seed();

        var featuredAtStart = ShelfComposer.Compose(Home, AppTrayLayout.Build(installed, overrides, catalog), NoUsage);
        var replaced = Home.Replace(HomeFeatureSet.CommunicationPackage, "com.acme.newmail");
        var afterReplace = ShelfComposer.Compose(replaced, AppTrayLayout.Build(installed, overrides, catalog), NoUsage);

        // Post is on home at the start, so it is absent from the Codex shelf…
        Assert.DoesNotContain(HomeFeatureSet.CommunicationPackage,
            featuredAtStart.Where(s => !s.Home).SelectMany(s => s.Entries).Select(e => e.Key));
        // …and once replaced it returns to the Codex shelf, while the newcomer leaves it.
        var codex = Assert.Single(afterReplace, s => !s.Home && s.Face == ShelfFace.CoverOut);
        Assert.Equal(new[] { HomeFeatureSet.CommunicationPackage }, codex.Entries.Select(e => e.Key));
        Assert.DoesNotContain("com.acme.newmail", afterReplace.Where(s => !s.Home).SelectMany(s => s.Entries).Select(e => e.Key));
    }

    [Fact]
    public void ReplacingAFeaturedCategoryApp_PutsItBackOnItsOriginalShelf()
    {
        var installed = new[] { new TrayApp(HomeFeatureSet.MapsPackage, -1), new TrayApp("com.acme.newmail", -1) };
        // The owner filed Maps on Travel; featuring it must not change that, only hide it from the shelf.
        var overrides = new Dictionary<string, string> { [HomeFeatureSet.MapsPackage] = CategoryCatalog.Travel };
        var catalog = CategoryCatalog.Seed();

        var replaced = Home.Replace(HomeFeatureSet.MapsPackage, "com.acme.newmail");
        var shelves = ShelfComposer.Compose(replaced, AppTrayLayout.Build(installed, overrides, catalog), NoUsage);

        var travel = Assert.Single(shelves, s => s.BookendName == "Travel & Transit");
        Assert.Equal(new[] { HomeFeatureSet.MapsPackage }, travel.Entries.Select(e => e.Key));
    }

    [Fact]
    public void SpineSizes_AreRankedAcrossTheWholeTray_ByUsage()
    {
        var usage = new Dictionary<string, long> { ["com.busy"] = 1000, ["com.quiet"] = 0 };
        var tray = new TrayModel(
            [],
            [Folder(CategoryCatalog.Media, "Media", "folder_media", "com.busy")],
            LooseApps: [App("com.quiet")]);

        var shelves = ShelfComposer.Compose(Home, tray, usage);

        var busy = shelves.First(s => s.Face == ShelfFace.SpineOut && s.BookendName is not null).Entries[0];
        var quiet = shelves.Last().Entries[0];
        Assert.Equal(SpineSizing.Size.Broad, busy.Size);
        Assert.Equal(SpineSizing.Size.Slim, quiet.Size);
    }
}
