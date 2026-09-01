using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class PlaylistTests
{
    private static Track T(string id, string artist) =>
        new(id, $"Track {id}", artist, "Album", artist, 1, 1, "Rock", TimeSpan.FromMinutes(3));

    private static readonly IReadOnlyList<Track> Catalog = [T("1", "Switchfoot"), T("2", "Switchfoot"), T("3", "Bartók")];

    [Fact]
    public void Manual_ResolvesItsIdsInTheOrderTheyWereAdded()
    {
        var list = Playlist.Manual("p1", "Practice", ["3", "1"]);

        Assert.Equal(["3", "1"], list.Tracks(Catalog).Select(track => track.Id));
    }

    // A playlist keeps ids rather than tracks, so one that is temporarily missing
    // from the catalog is skipped rather than crashing the screen — that is the
    // whole reason for storing ids.
    [Fact]
    public void Manual_SkipsATrackTheCatalogNoLongerHas()
    {
        var list = Playlist.Manual("p1", "Practice", ["1", "gone", "2"]);

        Assert.Equal(["1", "2"], list.Tracks(Catalog).Select(track => track.Id));
    }

    /// <summary>The count is what the row's sub-line reads, so it counts what resolves.</summary>
    [Fact]
    public void Manual_CountsOnlyWhatResolves()
    {
        Assert.Equal(2, Playlist.Manual("p1", "Practice", ["1", "gone", "2"]).Count(Catalog));
    }

    [Fact]
    public void Rules_ResolvesFromTheCatalogRatherThanStoredIds()
    {
        var list = Playlist.FromRules("p2", "Switchfoot", new PlaylistRules(
            RuleMatch.All, [new PlaylistRule(TrackField.Artist, RuleOperator.Is, "Switchfoot")]));

        Assert.Equal(["1", "2"], list.Tracks(Catalog).Select(track => track.Id));
    }

    // The point of a rule playlist: it takes in what arrives later without editing.
    [Fact]
    public void Rules_PickUpTracksAddedAfterTheListWasMade()
    {
        var list = Playlist.FromRules("p2", "Switchfoot", new PlaylistRules(
            RuleMatch.All, [new PlaylistRule(TrackField.Artist, RuleOperator.Is, "Switchfoot")]));

        IReadOnlyList<Track> later = [.. Catalog, T("4", "Switchfoot")];

        Assert.Equal(3, list.Count(later));
    }

    [Fact]
    public void Generated_BehavesLikeAFixedListOnceItHasBeenMade()
    {
        var list = Playlist.Generated("p3", "Something quiet", ["2", "3"]);

        Assert.Equal(PlaylistKind.Generated, list.Kind);
        Assert.Equal(["2", "3"], list.Tracks(Catalog).Select(track => track.Id));
    }

    // Provenance only — sharing sends a copy, so a received list is an ordinary one
    // that happens to remember who sent it.
    [Fact]
    public void APlaylistCanRememberWhoSentIt()
    {
        var list = Playlist.Manual("p1", "Sunday", ["1"]) with { From = "Jordan" };

        Assert.Equal("Jordan", list.From);
        Assert.Equal(PlaylistKind.Manual, list.Kind);
    }

    [Fact]
    public void EachKindDescribesItselfForTheRowSubLine()
    {
        Assert.Equal("2 tracks", Playlist.Manual("a", "A", ["1", "2"]).Describe(Catalog));
        Assert.Equal("1 track", Playlist.Manual("a", "A", ["1"]).Describe(Catalog));
        Assert.Equal("Rules · 2 tracks", Playlist.FromRules("b", "B", new PlaylistRules(
            RuleMatch.All, [new PlaylistRule(TrackField.Artist, RuleOperator.Is, "Switchfoot")])).Describe(Catalog));
        Assert.Equal("Generated · 1 track", Playlist.Generated("c", "C", ["3"]).Describe(Catalog));
        Assert.Equal("2 tracks · from Jordan",
            (Playlist.Manual("d", "D", ["1", "2"]) with { From = "Jordan" }).Describe(Catalog));
    }

    [Fact]
    public void AnEmptyPlaylistSaysSoRatherThanReadingZeroTracks()
    {
        Assert.Equal("Empty", Playlist.Manual("a", "A", []).Describe(Catalog));
    }
}
