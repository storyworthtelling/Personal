using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class HomeFeatureSetTests
{
    [Fact]
    public void Default_IsTheSixApprovedHomeApps_InOrder()
    {
        Assert.Equal(HomeFeatureSet.Slots, HomeFeatureSet.Default.Packages.Count);
        Assert.Equal(
            new[]
            {
                HomeFeatureSet.CommunicationPackage,
                HomeFeatureSet.CameraPackage,
                HomeFeatureSet.AudioPackage,
                HomeFeatureSet.DrivePackage,
                HomeFeatureSet.GoogleHomePackage,
                HomeFeatureSet.MapsPackage,
            },
            HomeFeatureSet.Default.Packages);
    }

    [Fact]
    public void Replace_SwapsInPlace_KeepingSlotPositionAndCount()
    {
        var featured = HomeFeatureSet.Default.Replace(HomeFeatureSet.MapsPackage, "com.some.promoted");

        Assert.Equal(HomeFeatureSet.Slots, featured.Packages.Count);
        // Maps was the sixth slot; the promoted app takes exactly that slot.
        Assert.Equal("com.some.promoted", featured.Packages[5]);
        Assert.False(featured.Contains(HomeFeatureSet.MapsPackage));
        Assert.True(featured.Contains("com.some.promoted"));
    }

    [Fact]
    public void Replace_OnAnAppNotFeatured_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            HomeFeatureSet.Default.Replace("com.not.featured", "com.some.promoted"));
    }

    [Fact]
    public void Replace_WithAnAlreadyFeaturedApp_IsANoOp_NoDuplicateSlot()
    {
        // Featuring Audio (already on the shelf) over Maps would duplicate it — refuse silently.
        var featured = HomeFeatureSet.Default.Replace(HomeFeatureSet.MapsPackage, HomeFeatureSet.AudioPackage);

        Assert.Equal(HomeFeatureSet.Default.Packages, featured.Packages);
    }
}
