using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class AppFolderTests
{
    [Fact]
    public void All_ContainsEveryFolderExactlyOnce()
    {
        var declared = Enum.GetValues<AppFolder>();
        var listed = AppFolders.All.Select(i => i.Folder).ToList();

        Assert.Equal(declared.Length, listed.Count);
        Assert.Equal(declared.OrderBy(f => f), listed.OrderBy(f => f));
    }

    [Fact]
    public void EveryFolder_HasDisplayNameAndDrawableResource()
    {
        foreach (var info in AppFolders.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.DisplayName), $"{info.Folder} display name");
            Assert.False(string.IsNullOrWhiteSpace(info.IconResourceName), $"{info.Folder} icon resource");
            Assert.StartsWith("folder_", info.IconResourceName);
        }
    }

    [Fact]
    public void SortOrders_AreUnique()
    {
        var orders = AppFolders.All.Select(i => i.Order).ToList();
        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public void All_IsOrderedBySortOrder()
    {
        Assert.Equal(AppFolders.All.OrderBy(i => i.Order).Select(i => i.Folder), AppFolders.All.Select(i => i.Folder));
    }

    [Fact]
    public void Info_RoundTripsForEveryFolder()
    {
        foreach (var folder in Enum.GetValues<AppFolder>())
        {
            Assert.Equal(folder, AppFolders.Info(folder).Folder);
        }
    }

    [Theory]
    [InlineData(6, AppFolder.Travel)]        // Maps
    [InlineData(1, AppFolder.Media)]         // Audio
    [InlineData(2, AppFolder.Media)]         // Video
    [InlineData(3, AppFolder.Media)]         // Image
    [InlineData(7, AppFolder.Productivity)]  // Productivity
    [InlineData(8, AppFolder.Utilities)]     // Accessibility
    public void FromAndroidCategory_MapsReliableCategories(int androidCategory, AppFolder expected)
    {
        Assert.Equal(expected, AppFolders.FromAndroidCategory(androidCategory));
    }

    [Theory]
    [InlineData(-1)] // Undefined
    [InlineData(0)]  // Game (excluded upstream anyway)
    [InlineData(4)]  // Social — no folder in our taxonomy
    [InlineData(5)]  // News — no folder in our taxonomy
    public void FromAndroidCategory_ReturnsNullForUnmappedCategories(int androidCategory)
    {
        Assert.Null(AppFolders.FromAndroidCategory(androidCategory));
    }

    [Fact]
    public void CuratedSeed_IsNonEmpty_AndMapsToDefinedFolders()
    {
        Assert.NotEmpty(AppFolders.CuratedSeed);
        foreach (var folder in AppFolders.CuratedSeed.Values)
        {
            Assert.True(Enum.IsDefined(folder));
        }
    }

    [Fact]
    public void CuratedSeed_PlacesRepresentativeApps()
    {
        Assert.Equal(AppFolder.Shopping, AppFolders.CuratedSeed["com.amazon.mShop.android.shopping"]);
        Assert.Equal(AppFolder.Media, AppFolders.CuratedSeed["com.spotify.music"]);
        Assert.Equal(AppFolder.Travel, AppFolders.CuratedSeed["com.ubercab"]);
    }
}
