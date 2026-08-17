namespace Codex.Domain.Music;

/// <summary>An album: its tracks in disc/track order, grouped under the album artist.</summary>
public record Album(string Name, string AlbumArtist, IReadOnlyList<Track> Tracks, string? ArtRef);

/// <summary>An artist and every track credited to them.</summary>
public record Artist(string Name, IReadOnlyList<Track> Tracks);

/// <summary>
/// A named playlist as an ordered list of track ids. Ids (not tracks) so a
/// playlist survives a track that is temporarily missing from the catalog.
/// </summary>
public record Playlist(string Id, string Name, IReadOnlyList<string> TrackIds);
