namespace Codex.Domain.Music;

/// <summary>
/// Turns a <see cref="HiddenTrackSplitter"/> plan into the actual <see cref="Track"/>
/// records to replace the original with — where <see cref="HiddenTrackDetector"/>
/// finding gaps and <see cref="HiddenTrackSplitter"/> deciding which ones qualify
/// finally becomes a library change.
/// </summary>
public static class HiddenTrackSplit
{
    /// <summary>Shown when a hidden track was split off but Claude did not know (or
    /// was not confident of) its actual title — still findable and playable, just
    /// honestly unnamed rather than carrying a guess.</summary>
    public const string UnnamedTitle = "Hidden Track";

    /// <summary>
    /// One row per segment, each sharing the original's file via
    /// <see cref="Track.SourceFileId"/>/<see cref="Track.ClipStart"/> rather than a
    /// copy of it, so nothing downstream — the resolving data source, the download
    /// cache — needs to know a split ever happened. A single segment (nothing worth
    /// splitting) returns the original track unchanged, same reference and all, so a
    /// caller can write back exactly what it read when there was nothing to do.
    ///
    /// <paramref name="hiddenTitle"/> names the <b>last</b> segment: a hidden track is
    /// always the one appended after the visible song, never a leading one. Every
    /// other segment — the shortened original among them — keeps the original's own
    /// title. A missing or blank name falls back to <see cref="UnnamedTitle"/> rather
    /// than leaving the split half without a title at all.
    /// </summary>
    public static IReadOnlyList<Track> Apply(
        Track original, IReadOnlyList<TrackSegment> segments, string? hiddenTitle)
    {
        if (segments.Count <= 1)
        {
            return [original];
        }

        var tracks = new List<Track>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var isLast = i == segments.Count - 1;
            var title = isLast
                ? (string.IsNullOrWhiteSpace(hiddenTitle) ? UnnamedTitle : hiddenTitle)
                : original.Title;

            tracks.Add(original with
            {
                Id = $"{original.Id}#{i}",
                Title = title,
                Duration = segment.Duration,
                SourceFileId = original.Id,
                ClipStart = segment.Start,
            });
        }

        return tracks;
    }
}
