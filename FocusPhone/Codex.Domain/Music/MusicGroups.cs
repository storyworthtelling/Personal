namespace Codex.Domain.Music;

/// <summary>An album: its tracks in disc/track order, grouped under the album artist.</summary>
public record Album(string Name, string AlbumArtist, IReadOnlyList<Track> Tracks, string? ArtRef);

/// <summary>An artist and every track credited to them.</summary>
public record Artist(string Name, IReadOnlyList<Track> Tracks);

/// <summary>How a playlist decides what is in it.</summary>
public enum PlaylistKind
{
    /// <summary>A list someone assembled. It is exactly what they put in it.</summary>
    Manual,

    /// <summary>A standing query over the library. It takes in what arrives later.</summary>
    Rules,

    /// <summary>Made from a prompt or a seed track, then fixed like a manual one.</summary>
    Generated,
}

/// <summary>
/// A named playlist. The three kinds share one list and one row, which is why they
/// are one type: the difference shows up in the sub-line, not in the shape.
///
/// Manual and generated lists carry <see cref="TrackIds"/>; a rules list derives its
/// tracks from the catalog every time it is read, which is what lets it pick up
/// music added after it was written. Either way the storage is <b>ids, never
/// tracks</b>, so a file that is temporarily missing from the catalog is skipped
/// rather than taking the screen with it.
/// </summary>
public record Playlist(
    string Id,
    string Name,
    PlaylistKind Kind,
    IReadOnlyList<string> TrackIds,
    PlaylistRules? Rules = null,
    string? From = null)
{
    public static Playlist Manual(string id, string name, IReadOnlyList<string> trackIds) =>
        new(id, name, PlaylistKind.Manual, trackIds);

    public static Playlist Generated(string id, string name, IReadOnlyList<string> trackIds) =>
        new(id, name, PlaylistKind.Generated, trackIds);

    public static Playlist FromRules(string id, string name, PlaylistRules rules) =>
        new(id, name, PlaylistKind.Rules, [], rules);

    /// <summary>The tracks in this list, in its own order, skipping what is missing.</summary>
    public IReadOnlyList<Track> Tracks(IReadOnlyList<Track> catalog)
    {
        if (Kind == PlaylistKind.Rules)
        {
            return PlaylistRules.Apply(Rules ?? PlaylistRules.Empty, catalog);
        }

        var byId = catalog.ToDictionary(track => track.Id, StringComparer.Ordinal);
        return [.. TrackIds.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    public int Count(IReadOnlyList<Track> catalog) => Tracks(catalog).Count;

    /// <summary>
    /// The row's sub-line. It carries the kind rather than a badge — a badge would
    /// need its own legend, where "Rules · 126 tracks" reads on sight. Provenance
    /// rides along because a list from someone else is a copy, not a link, so who
    /// sent it is the only trace left.
    /// </summary>
    public string Describe(IReadOnlyList<Track> catalog)
    {
        var count = Count(catalog);
        if (count == 0 && From is null)
        {
            return "Empty";
        }

        var tracks = $"{count} track{(count == 1 ? "" : "s")}";
        var kind = Kind switch
        {
            PlaylistKind.Rules => $"Rules · {tracks}",
            PlaylistKind.Generated => $"Generated · {tracks}",
            _ => tracks,
        };

        return From is null ? kind : $"{kind} · from {From}";
    }
}
