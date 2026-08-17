namespace Codex.Domain.Music;

/// <summary>
/// The iPod-style hierarchy over the library: Songs, Albums, Artists, Genres —
/// all derived purely from the track tags, so it is fully unit-testable. The
/// music analogue of <c>PodcastLibrary</c>. Grouping uses the tracks' Display*
/// fallbacks, so untagged files land under Unknown Artist/Album rather than
/// vanishing, and compilations stay together under their album artist.
/// </summary>
public class MusicCatalog
{
    private static readonly StringComparer Name = StringComparer.OrdinalIgnoreCase;

    private readonly List<Track> tracks;

    public MusicCatalog(IEnumerable<Track> tracks) => this.tracks = [.. tracks];

    public IReadOnlyList<Track> Songs() =>
        [.. tracks.OrderBy(track => track.DisplayTitle, Name)];

    public IReadOnlyList<Album> Albums() =>
        [.. tracks
            .GroupBy(track => (Artist: track.DisplayAlbumArtist, Album: track.DisplayAlbum))
            .Select(group => new Album(
                group.Key.Album,
                group.Key.Artist,
                InAlbumOrder(group),
                group.Select(track => track.ArtRef).FirstOrDefault(art => art is not null)))
            .OrderBy(album => album.AlbumArtist, Name)
            .ThenBy(album => album.Name, Name)];

    public IReadOnlyList<Artist> Artists() =>
        [.. tracks
            .GroupBy(track => track.DisplayArtist)
            .Select(group => new Artist(group.Key, InAlbumOrder(group)))
            .OrderBy(artist => artist.Name, Name)];

    public IReadOnlyList<string> Genres() =>
        [.. tracks.Select(track => track.DisplayGenre).Distinct(Name).OrderBy(genre => genre, Name)];

    // ---- Scoped selections (feed the shuffle planner) ----

    public IReadOnlyList<Track> TracksByArtist(string artistName) =>
        InAlbumOrder(tracks.Where(track => Name.Equals(track.DisplayArtist, artistName)));

    public IReadOnlyList<Track> TracksInGenre(string genre) =>
        InAlbumOrder(tracks.Where(track => Name.Equals(track.DisplayGenre, genre)));

    public IReadOnlyList<Track> TracksInAlbum(string albumArtist, string albumName) =>
        InAlbumOrder(tracks.Where(track =>
            Name.Equals(track.DisplayAlbumArtist, albumArtist)
            && Name.Equals(track.DisplayAlbum, albumName)));

    private static List<Track> InAlbumOrder(IEnumerable<Track> group) =>
        [.. group
            .OrderBy(track => track.DiscNumber)
            .ThenBy(track => track.TrackNumber)
            .ThenBy(track => track.DisplayTitle, Name)];
}
