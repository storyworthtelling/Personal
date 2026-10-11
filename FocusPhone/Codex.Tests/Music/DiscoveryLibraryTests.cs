using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class DiscoveryLibraryTests
{
    private static Track Found(string id, string title, string artist, string album) =>
        new(Id: $"tidal:{id}", Title: title, ArtistName: artist, AlbumName: album, AlbumArtist: artist,
            TrackNumber: 0, DiscNumber: 0, Genre: string.Empty, Duration: TimeSpan.FromMinutes(3),
            Availability: TrackAvailability.Streaming);

    [Fact]
    public void FindAlbumTrack_ReturnsTheTrackOnTheAlbumTheAlbumPickNames()
    {
        var pick = new Recommendation("Parachutes", "Coldplay", Kind: DiscoveryKind.Album);
        var found = new[]
        {
            Found("1", "Yellow", "Coldplay", "Parachutes"),
            Found("2", "Parachutes", "Someone Else", "Parachutes"),
        };

        var match = Discoveries.FindAlbumTrack(found, pick.Title, pick.Artist);

        Assert.NotNull(match);
        Assert.Equal("tidal:1", match.Id);
    }

    [Fact]
    public void FindAlbumTrack_IgnoresAMatchingSongTitleOnAnotherAlbum()
    {
        var pick = new Recommendation("Parachutes", "Coldplay", Kind: DiscoveryKind.Album);
        var found = new[] { Found("1", "Parachutes", "Coldplay", "A Rush of Blood to the Head") };

        Assert.Null(Discoveries.FindAlbumTrack(found, pick.Title, pick.Artist));
    }

    [Fact]
    public void FindAlbumTrack_ReturnsNullWhenNothingMatches()
    {
        var pick = new Recommendation("Parachutes", "Coldplay", Kind: DiscoveryKind.Album);

        Assert.Null(Discoveries.FindAlbumTrack([], pick.Title, pick.Artist));
    }
}
