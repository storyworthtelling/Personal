namespace FocusPhone.Domain.Music;

/// <summary>
/// Resolves a model-generated track-id list into a playable <see cref="DiscoveryPlaylist"/> against the
/// catalog — the safety net between Claude's reply and the player. Unknown ids are dropped, repeats are
/// collapsed, order is preserved, and the result is capped, so a mis-referenced or hallucinated id degrades
/// to a shorter playlist rather than a crash (the same posture as the storyboard renderer's correlation
/// gaps). Pure.
/// </summary>
public static class PlaylistPlan
{
    public static DiscoveryPlaylist Resolve(
        string title, IEnumerable<string> trackIds, IReadOnlyList<Track> catalog, int maxTracks)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        ArgumentNullException.ThrowIfNull(catalog);
        if (maxTracks <= 0)
        {
            return new DiscoveryPlaylist(title, []);
        }

        var byId = new Dictionary<string, Track>();
        foreach (var track in catalog)
        {
            byId.TryAdd(track.Id, track);
        }

        var seen = new HashSet<string>();
        var resolved = new List<Track>();
        foreach (var id in trackIds)
        {
            if (id is null || !seen.Add(id) || !byId.TryGetValue(id, out var track))
            {
                continue;
            }

            resolved.Add(track);
            if (resolved.Count >= maxTracks)
            {
                break;
            }
        }

        return new DiscoveryPlaylist(title, resolved);
    }
}
