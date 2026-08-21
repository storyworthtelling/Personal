using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class CategoryCatalogTests
{
    [Fact]
    public void Seed_HasNineBuiltinShelvesInOrder()
    {
        var seed = CategoryCatalog.Seed();

        Assert.Equal(9, seed.Categories.Count);
        Assert.All(seed.Categories, c => Assert.True(c.Builtin));
        Assert.Equal(seed.Categories.OrderBy(c => c.Order).Select(c => c.Id), seed.Categories.Select(c => c.Id));
    }

    [Fact]
    public void Seed_EveryShelfHasNameAndFolderGlyph_AndUniqueIds()
    {
        var seed = CategoryCatalog.Seed();

        foreach (var category in seed.Categories)
        {
            Assert.False(string.IsNullOrWhiteSpace(category.Name), $"{category.Id} name");
            Assert.StartsWith("folder_", category.Glyph);
        }
        Assert.Equal(seed.Categories.Count, seed.Categories.Select(c => c.Id).Distinct().Count());
        Assert.Equal(seed.Categories.Count, seed.Categories.Select(c => c.Order).Distinct().Count());
    }

    [Fact]
    public void BuiltinIds_AreTheOldEnumNames_SoStoredOverridesStillResolve()
    {
        // Migration-free: a device that stored "Finance" must still map to the Finance shelf.
        var ids = CategoryCatalog.Seed().Categories.Select(c => c.Id).ToHashSet();
        Assert.Contains("Finance", ids);
        Assert.Contains("SmartHome", ids);
        Assert.Equal(CategoryCatalog.Finance, "Finance");
    }

    [Theory]
    [InlineData(6, CategoryCatalog.Travel)]        // Maps
    [InlineData(1, CategoryCatalog.Media)]         // Audio
    [InlineData(2, CategoryCatalog.Media)]         // Video
    [InlineData(3, CategoryCatalog.Media)]         // Image
    [InlineData(7, CategoryCatalog.Productivity)]  // Productivity
    [InlineData(8, CategoryCatalog.Utilities)]     // Accessibility
    public void FromAndroidCategory_MapsReliableCategories(int androidCategory, string expected)
    {
        Assert.Equal(expected, CategoryCatalog.FromAndroidCategory(androidCategory));
    }

    [Theory]
    [InlineData(-1)] // Undefined
    [InlineData(0)]  // Game
    [InlineData(4)]  // Social
    [InlineData(5)]  // News
    public void FromAndroidCategory_ReturnsNullForUnmappedCategories(int androidCategory)
    {
        Assert.Null(CategoryCatalog.FromAndroidCategory(androidCategory));
    }

    [Fact]
    public void CuratedSeed_IsNonEmpty_AndMapsToBuiltinIds()
    {
        var ids = CategoryCatalog.Seed().Categories.Select(c => c.Id).ToHashSet();
        Assert.NotEmpty(CategoryCatalog.CuratedSeed);
        Assert.All(CategoryCatalog.CuratedSeed.Values, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void CuratedSeed_PlacesRepresentativeApps()
    {
        Assert.Equal(CategoryCatalog.Shopping, CategoryCatalog.CuratedSeed["com.amazon.mShop.android.shopping"]);
        Assert.Equal(CategoryCatalog.Media, CategoryCatalog.CuratedSeed["com.spotify.music"]);
        Assert.Equal(CategoryCatalog.Travel, CategoryCatalog.CuratedSeed["com.ubercab"]);
    }

    // ---- Editing (feature #11): add / rename / remove / reorder ----

    [Fact]
    public void WithAdded_AppendsAnOwnerShelf_WithAUniqueSlugIdAtTheEnd()
    {
        var catalog = CategoryCatalog.Seed().WithAdded("Reading & Reference", "glyph_reading");

        var added = catalog.Categories.Last();
        Assert.Equal("reading-reference", added.Id);
        Assert.Equal("Reading & Reference", added.Name);
        Assert.Equal("glyph_reading", added.Glyph);
        Assert.False(added.Builtin);
        Assert.Equal(catalog.Categories.Max(c => c.Order), added.Order);
    }

    [Fact]
    public void WithAdded_DisambiguatesAClashingSlug()
    {
        var catalog = CategoryCatalog.Seed().WithAdded("Finance", "g1").WithAdded("Finance", "g2");

        var ids = catalog.Categories.Select(c => c.Id).ToList();
        // The built-in "Finance" id is taken, so the two owner shelves get unique slugs.
        Assert.Contains("finance", ids);
        Assert.Contains("finance-2", ids);
    }

    [Fact]
    public void WithAdded_RejectsABlankName()
    {
        Assert.Throws<ArgumentException>(() => CategoryCatalog.Seed().WithAdded("   ", "g"));
    }

    [Fact]
    public void WithRenamed_ChangesNameKeepsIdAndGlyph()
    {
        var catalog = CategoryCatalog.Seed().WithRenamed(CategoryCatalog.Media, "Watch & Listen");

        var media = catalog.ById(CategoryCatalog.Media)!;
        Assert.Equal("Watch & Listen", media.Name);
        Assert.Equal("folder_media", media.Glyph);
    }

    [Fact]
    public void WithRemoved_DropsAShelf_BuiltinsToo()
    {
        var catalog = CategoryCatalog.Seed().WithRemoved(CategoryCatalog.Finance);

        Assert.False(catalog.Contains(CategoryCatalog.Finance));
        Assert.Equal(8, catalog.Categories.Count);
    }

    [Fact]
    public void Reordered_MovesNamedShelvesFirst_InTheGivenOrder()
    {
        var catalog = CategoryCatalog.Seed()
            .Reordered([CategoryCatalog.Media, CategoryCatalog.Finance]);

        Assert.Equal(CategoryCatalog.Media, catalog.Categories[0].Id);
        Assert.Equal(CategoryCatalog.Finance, catalog.Categories[1].Id);
        // Orders are re-normalized to 0..n so the new sequence is stable.
        Assert.Equal(catalog.Categories.OrderBy(c => c.Order).Select(c => c.Id), catalog.Categories.Select(c => c.Id));
    }
}
