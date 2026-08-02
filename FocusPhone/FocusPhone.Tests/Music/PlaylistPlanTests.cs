using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class PlaylistPlanTests
{
    private static Track T(string id) => new(id, "Title " + id, "Artist", "Album", "Artist", 1, 1, "Jazz", TimeSpan.FromMinutes(3));

    private static readonly IReadOnlyList<Track> Catalog = [T("a"), T("b"), T("c"), T("d")];

    [Fact]
    public void Resolve_MapsKnownIdsInOrder()
    {
        var playlist = PlaylistPlan.Resolve("Nightcap", ["c", "a", "b"], Catalog, maxTracks: 25);

        Assert.Equal("Nightcap", playlist.Title);
        Assert.Equal(["c", "a", "b"], playlist.Tracks.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void Resolve_DropsUnknownIds()
    {
        var playlist = PlaylistPlan.Resolve("Mix", ["a", "zzz", "b"], Catalog, maxTracks: 25);
        Assert.Equal(["a", "b"], playlist.Tracks.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void Resolve_DedupesRepeatedIds()
    {
        var playlist = PlaylistPlan.Resolve("Mix", ["a", "a", "b", "a"], Catalog, maxTracks: 25);
        Assert.Equal(["a", "b"], playlist.Tracks.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void Resolve_CapsToMaxTracks()
    {
        var playlist = PlaylistPlan.Resolve("Mix", ["a", "b", "c", "d"], Catalog, maxTracks: 2);
        Assert.Equal(["a", "b"], playlist.Tracks.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void Resolve_NoMatches_IsEmpty()
    {
        var playlist = PlaylistPlan.Resolve("Mix", ["x", "y"], Catalog, maxTracks: 25);
        Assert.True(playlist.IsEmpty);
    }

    [Fact]
    public void Resolve_NonPositiveMax_IsEmpty()
    {
        var playlist = PlaylistPlan.Resolve("Mix", ["a", "b"], Catalog, maxTracks: 0);
        Assert.True(playlist.IsEmpty);
    }
}
