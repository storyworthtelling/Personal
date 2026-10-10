using Pulp.Domain.Catalogue;

namespace Pulp.Tests.Catalogue;

/// <summary>
/// The one name a poster is kept under in the cache folder. Four screens each named their copies
/// their own way (two hash lengths, two folders), so one poster was fetched and kept up to three
/// times. Https only: a poster address is a download the phone makes on its own.
/// </summary>
public class PosterCacheNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://images.example.com/poster-a.jpg")]
    [InlineData("ftp://images.example.com/poster-a.jpg")]
    [InlineData("/poster-a.jpg")]
    [InlineData("not a url")]
    public void OnlyAnHttpsAddressHasAName(string? url) => Assert.Null(PosterCacheName.For(url));

    [Fact]
    public void OneAddressIsOneName() =>
        Assert.Equal(
            PosterCacheName.For("https://images.example.com/poster-a.jpg"),
            PosterCacheName.For("https://images.example.com/poster-a.jpg"));

    [Fact]
    public void TwoAddressesAreTwoNames() =>
        Assert.NotEqual(
            PosterCacheName.For("https://images.example.com/poster-a.jpg"),
            PosterCacheName.For("https://images.example.com/poster-b.jpg"));

    [Fact]
    public void TheNameIsAPlainFileName()
    {
        var name = PosterCacheName.For("https://images.example.com/posters/../poster-a.jpg?w=342&h=513");

        Assert.NotNull(name);
        Assert.Matches("^[0-9a-f]{32}\\.img$", name);
    }
}
