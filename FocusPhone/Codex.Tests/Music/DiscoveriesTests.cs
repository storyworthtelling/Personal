using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class DiscoveriesTests
{
    private static Track Owned(string id, string title, string artist) =>
        new(id, title, artist, "Album", artist, 1, 1, "Rock", TimeSpan.FromMinutes(3));

    // The reported defect: "Today Discover ran and only grabbed music from inside my
    // library." The crate is meant to be records the owner does NOT have, so anything
    // already owned is dropped here rather than trusted not to be recommended.
    [Fact]
    public void Unowned_DropsARecordTheLibraryAlreadyHas()
    {
        var owned = new[] { Owned("1", "Dare You to Move", "Switchfoot") };
        var picked = new[]
        {
            new Recommendation("Dare You to Move", "Switchfoot", "you play them often"),
            new Recommendation("Meant to Live", "Switchfoot", "same record"),
        };

        var kept = Discoveries.Unowned(picked, owned);

        Assert.Single(kept);
        Assert.Equal("Meant to Live", kept[0].Title);
    }

    // The library's own naming is not the recommender's. If the two have to match
    // character for character, the exclusion does nothing on real data.
    [Fact]
    public void Unowned_MatchesThroughEditionNoiseAndCase()
    {
        var owned = new[] { Owned("1", "Meant to Live - 2011 Remaster", "SWITCHFOOT") };
        var picked = new[] { new Recommendation("Meant to Live", "Switchfoot", null) };

        Assert.Empty(Discoveries.Unowned(picked, owned));
    }

    [Fact]
    public void Unowned_KeepsTheSameTitleByADifferentArtist()
    {
        var owned = new[] { Owned("1", "Hallelujah", "Leonard Cohen") };
        var picked = new[] { new Recommendation("Hallelujah", "Jeff Buckley", null) };

        Assert.Single(Discoveries.Unowned(picked, owned));
    }

    [Fact]
    public void Unowned_DropsARepeatedRecommendation()
    {
        var picked = new[]
        {
            new Recommendation("Only Hope", "Switchfoot", null),
            new Recommendation("only hope", "SWITCHFOOT", null),
        };

        Assert.Single(Discoveries.Unowned(picked, []));
    }

    [Fact]
    public void Unowned_DropsAnythingWithNoTitleOrNoArtist()
    {
        var picked = new[]
        {
            new Recommendation("", "Switchfoot", null),
            new Recommendation("Only Hope", "  ", null),
            new Recommendation("Only Hope", "Switchfoot", null),
        };

        Assert.Single(Discoveries.Unowned(picked, []));
    }

    [Fact]
    public void Unowned_OnAnEmptyLibrary_KeepsEverythingItWasGiven()
    {
        var picked = new[]
        {
            new Recommendation("Only Hope", "Switchfoot", null),
            new Recommendation("Meant to Live", "Switchfoot", null),
        };

        Assert.Equal(2, Discoveries.Unowned(picked, []).Count);
    }

    // The crate persists across a launch and the veto ledger keys off it, so a pick
    // needs an id that is the same next time it is derived.
    [Fact]
    public void Key_IsStableAcrossEquivalentSpellings()
    {
        Assert.Equal(
            Discoveries.Key(new Recommendation("Only Hope", "Switchfoot", null)),
            Discoveries.Key(new Recommendation("only hope ", " SWITCHFOOT", "different reason")));
    }

    [Fact]
    public void Key_DiffersByArtist()
    {
        Assert.NotEqual(
            Discoveries.Key(new Recommendation("Hallelujah", "Leonard Cohen", null)),
            Discoveries.Key(new Recommendation("Hallelujah", "Jeff Buckley", null)));
    }

    // An Album pick is "owned" once any of its tracks are — the library never stores a
    // record for an album as a whole, only tracks grouped by (album, album artist).
    [Fact]
    public void Unowned_DropsAnAlbumTheLibraryAlreadyHasATrackFrom()
    {
        var owned = new[]
        {
            new Track("1", "Meant to Live", "Switchfoot", "New Way to Be Human", "Switchfoot",
                1, 1, "Rock", TimeSpan.FromMinutes(3)),
        };
        var picked = new[]
        {
            new Recommendation("New Way to Be Human", "Switchfoot", Kind: DiscoveryKind.Album),
            new Recommendation("The Beautiful Letdown", "Switchfoot", Kind: DiscoveryKind.Album),
        };

        var kept = Discoveries.Unowned(picked, owned);

        Assert.Single(kept);
        Assert.Equal("The Beautiful Letdown", kept[0].Title);
    }

    [Fact]
    public void Unowned_DropsAShowAlreadySubscribedTo()
    {
        var picked = new[]
        {
            new Recommendation("Reply All", "", Kind: DiscoveryKind.Podcast),
            new Recommendation("Radiolab", "", Kind: DiscoveryKind.Podcast),
        };

        var kept = Discoveries.Unowned(picked, [], subscribedShows: ["reply all"]);

        Assert.Single(kept);
        Assert.Equal("Radiolab", kept[0].Title);
    }

    // A podcast pick names a show, not an artist — a blank network/publisher is not a
    // half-named pick the way a blank Artist would be for a Song or Album.
    [Fact]
    public void Unowned_KeepsAPodcastPickWithNoNetworkNamed()
    {
        var picked = new[] { new Recommendation("Radiolab", "", Kind: DiscoveryKind.Podcast) };

        Assert.Single(Discoveries.Unowned(picked, []));
    }

    [Fact]
    public void Unowned_StillDropsAPodcastPickWithNoTitle()
    {
        var picked = new[] { new Recommendation("", "", Kind: DiscoveryKind.Podcast) };

        Assert.Empty(Discoveries.Unowned(picked, []));
    }
}
