namespace Codex.Domain.Music;

/// <summary>A track paired with the receiver-fetchable URL to cast it.</summary>
public sealed record CastQueueItem(Track Track, string Url);

/// <summary>
/// Splits a track list into what can be cast and what can't. A Cast receiver fetches the URL itself, so a
/// track only casts if it resolves to a directly-fetchable URL (the resolver is supplied by the Android
/// side — a Drive download URL or the local proxy). Unresolved tracks are skipped rather than stalling the
/// queue, mirroring how the storyboard renderer degrades on a missing correlation. Pure.
/// </summary>
public sealed record CastQueuePlan(IReadOnlyList<CastQueueItem> Playable, IReadOnlyList<Track> Skipped)
{
    public static CastQueuePlan Build(IReadOnlyList<Track> tracks, Func<Track, string?> resolveUrl)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(resolveUrl);

        var playable = new List<CastQueueItem>();
        var skipped = new List<Track>();
        foreach (var track in tracks)
        {
            var url = resolveUrl(track);
            if (string.IsNullOrEmpty(url))
            {
                skipped.Add(track);
            }
            else
            {
                playable.Add(new CastQueueItem(track, url));
            }
        }

        return new CastQueuePlan(playable, skipped);
    }
}
