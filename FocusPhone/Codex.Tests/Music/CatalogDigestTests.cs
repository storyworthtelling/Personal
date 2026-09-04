using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class CatalogDigestTests
{
    private static Track T(string id, string artist) =>
        new(id, $"Track {id}", artist, "Album", artist, 1, 1, "Rock", TimeSpan.FromMinutes(3));

    private static IReadOnlyList<Track> Run(string artist, int count, string prefix) =>
        [.. Enumerable.Range(0, count).Select(i => T($"{prefix}{i}", artist))];

    [Fact]
    public void Sample_KeepsTheWholeCatalogWhenItFits()
    {
        IReadOnlyList<Track> catalog = [T("1", "A"), T("2", "B"), T("3", "C")];

        Assert.Equal(catalog, CatalogDigest.Sample(catalog, 10));
    }

    [Fact]
    public void Sample_NeverExceedsTheLimit()
    {
        var catalog = Run("A", 50, "a");

        Assert.Equal(7, CatalogDigest.Sample(catalog, 7).Count);
    }

    // The whole reason this exists: taking the head of a catalog sorted by artist
    // shows the model the letter A and tells it that is the library.
    [Fact]
    public void Sample_DoesNotLetOneArtistCrowdOutTheRest()
    {
        List<Track> catalog = [.. Run("Prolific", 500, "p")];
        catalog.AddRange([T("b", "B"), T("c", "C"), T("d", "D")]);

        var sample = CatalogDigest.Sample(catalog, 10);

        Assert.Contains(sample, track => track.ArtistName == "B");
        Assert.Contains(sample, track => track.ArtistName == "C");
        Assert.Contains(sample, track => track.ArtistName == "D");
    }

    [Fact]
    public void Sample_GivesEveryArtistOneBeforeAnyArtistGetsTwo()
    {
        List<Track> catalog = [.. Run("A", 3, "a")];
        catalog.AddRange(Run("B", 3, "b"));
        catalog.AddRange(Run("C", 3, "c"));

        var sample = CatalogDigest.Sample(catalog, 4);

        Assert.Equal(3, sample.Take(3).Select(track => track.ArtistName).Distinct().Count());
    }

    [Fact]
    public void Sample_ReturnsRealCatalogTracksOnlyOnce()
    {
        List<Track> catalog = [.. Run("A", 4, "a")];
        catalog.AddRange(Run("B", 4, "b"));

        var sample = CatalogDigest.Sample(catalog, 5);

        Assert.Equal(5, sample.Select(track => track.Id).Distinct().Count());
        Assert.All(sample, track => Assert.Contains(track, catalog));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Sample_ReturnsNothingForANonsenseLimit(int limit)
    {
        Assert.Empty(CatalogDigest.Sample(Run("A", 3, "a"), limit));
    }

    [Fact]
    public void Sample_SurvivesAnEmptyCatalog()
    {
        Assert.Empty(CatalogDigest.Sample([], 10));
    }
}
