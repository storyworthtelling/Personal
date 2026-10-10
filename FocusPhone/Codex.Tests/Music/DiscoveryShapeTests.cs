using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class DiscoveryShapeTests
{
    private static readonly Recommendation Song1 = new("Song one", "A", Kind: DiscoveryKind.Song);
    private static readonly Recommendation Song2 = new("Song two", "A", Kind: DiscoveryKind.Song);
    private static readonly Recommendation Album1 = new("Album one", "B", Kind: DiscoveryKind.Album);
    private static readonly Recommendation Show1 = new("Show one", "", Kind: DiscoveryKind.Podcast);
    private static readonly Recommendation Show2 = new("Show two", "", Kind: DiscoveryKind.Podcast);

    // The owner asked for a count of each, so the crate is held to that count whatever the
    // recommender returned. A model that ignores the number must not overfill or underfill.
    [Fact]
    public void Shape_KeepsTheRequestedNumberOfMusicAndOfPodcastsIndependently()
    {
        var picked = new[] { Song1, Show1, Song2, Show2, Album1 };

        var shaped = Discoveries.Shape(picked, music: 2, podcasts: 1);

        Assert.Equal(["Song one", "Show one", "Song two"], shaped.Select(pick => pick.Title));
    }

    [Fact]
    public void Shape_TreatsAlbumsAsMusic()
    {
        var picked = new[] { Album1, Song1 };

        var shaped = Discoveries.Shape(picked, music: 2, podcasts: 0);

        Assert.Equal(2, shaped.Count);
    }

    [Fact]
    public void Shape_ZeroForAKindDropsEveryPickOfThatKind()
    {
        var picked = new[] { Song1, Show1, Show2 };

        var shaped = Discoveries.Shape(picked, music: 5, podcasts: 0);

        Assert.Equal(["Song one"], shaped.Select(pick => pick.Title));
    }

    [Fact]
    public void Shape_ReturnsWhatItWasGivenWhenTheModelFellShort()
    {
        var picked = new[] { Song1 };

        var shaped = Discoveries.Shape(picked, music: 10, podcasts: 4);

        Assert.Single(shaped);
    }

    [Fact]
    public void Shape_KeepsTheOrderTheyArrivedIn()
    {
        var picked = new[] { Show1, Song2, Song1 };

        var shaped = Discoveries.Shape(picked, music: 2, podcasts: 1);

        Assert.Equal(["Show one", "Song two", "Song one"], shaped.Select(pick => pick.Title));
    }
}
