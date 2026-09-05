namespace Codex.Domain.Music;

/// <summary>A prompted request for a generated playlist: a free-text seed and a length budget.</summary>
public sealed record PlaylistRequest(string Seed, int MaxTracks = 25);

/// <summary>A generated playlist resolved to real, playable catalog tracks.</summary>
public sealed record DiscoveryPlaylist(string Title, IReadOnlyList<Track> Tracks)
{
    public static DiscoveryPlaylist Empty { get; } = new(string.Empty, []);

    public bool IsEmpty => Tracks.Count == 0;
}
