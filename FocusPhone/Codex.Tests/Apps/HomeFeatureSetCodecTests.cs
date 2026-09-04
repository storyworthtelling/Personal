using Codex.Domain.Apps;
using Codex.Infrastructure.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class HomeFeatureSetCodecTests
{
    [Fact]
    public void RoundTrips_AFeaturedSet()
    {
        var featured = HomeFeatureSet.Default.Replace(HomeFeatureSet.MapsPackage, "com.some.promoted");

        var decoded = HomeFeatureSetCodec.Decode(HomeFeatureSetCodec.Encode(featured));

        Assert.Equal(featured.Packages, decoded.Packages);
    }

    [Fact]
    public void AbsentPayload_DecodesToDefault()
    {
        Assert.Equal(HomeFeatureSet.Default.Packages, HomeFeatureSetCodec.Decode(null).Packages);
        Assert.Equal(HomeFeatureSet.Default.Packages, HomeFeatureSetCodec.Decode("").Packages);
    }

    [Fact]
    public void GarbledPayload_DecodesToDefault()
    {
        Assert.Equal(HomeFeatureSet.Default.Packages, HomeFeatureSetCodec.Decode("{ not json ]").Packages);
    }

    [Fact]
    public void EmptyStoredSet_DecodesToDefault_TheHomeIsNeverBlank()
    {
        Assert.Equal(HomeFeatureSet.Default.Packages, HomeFeatureSetCodec.Decode("[]").Packages);
    }
}
