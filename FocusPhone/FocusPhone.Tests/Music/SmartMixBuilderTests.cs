using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class SmartMixBuilderTests
{
    private static Track Tr(string id, string artist, string genre, string album = "Album") =>
        new(id, $"T{id}", artist, album, artist, 1, 1, genre, TimeSpan.FromMinutes(3));

    [Fact]
    public void Build_LeadsWithTheSeed()
    {
        var seed = Tr("seed", "Band", "Rock");
        var catalog = new MusicCatalog([seed, Tr("1", "Band", "Rock"), Tr("2", "Other", "Rock")]);

        var mix = new SmartMixBuilder(new ShufflePlanner(new Random(1))).Build(seed, catalog);

        Assert.Equal("seed", mix[0].Id);
    }

    [Fact]
    public void Build_RanksSameArtistAboveMerelySameGenre()
    {
        var seed = Tr("seed", "Band", "Rock");
        var sameArtist = Tr("same", "Band", "Rock");
        var sameGenreOnly = Tr("genre", "Other", "Rock");
        var catalog = new MusicCatalog([seed, sameArtist, sameGenreOnly]);

        var mix = new SmartMixBuilder(new ShufflePlanner(new Random(1))).Build(seed, catalog);

        var sameArtistIndex = IndexOf(mix, "same");
        var sameGenreIndex = IndexOf(mix, "genre");
        Assert.True(sameArtistIndex < sameGenreIndex);
    }

    [Fact]
    public void Build_ExcludesUnrelatedTracks()
    {
        var seed = Tr("seed", "Band", "Rock");
        var unrelated = Tr("x", "NobodyElse", "Polka");
        var catalog = new MusicCatalog([seed, unrelated]);

        var mix = new SmartMixBuilder(new ShufflePlanner(new Random(1))).Build(seed, catalog);

        Assert.Equal(["seed"], mix.Select(track => track.Id).ToArray());
    }

    [Fact]
    public void Build_HonorsTheLimit()
    {
        var seed = Tr("seed", "Band", "Rock");
        var others = Enumerable.Range(1, 10).Select(n => Tr($"{n}", "Band", "Rock"));
        var catalog = new MusicCatalog([seed, .. others]);

        var mix = new SmartMixBuilder(new ShufflePlanner(new Random(1))).Build(seed, catalog, limit: 3);

        Assert.Equal(4, mix.Count); // seed + 3
    }

    private static int IndexOf(IReadOnlyList<Track> mix, string id)
    {
        for (var i = 0; i < mix.Count; i++)
        {
            if (mix[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
