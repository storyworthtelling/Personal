namespace Codex.Domain.Music;

/// <summary>A stretch of near-silence found while scanning a track's audio levels.</summary>
public sealed record SilenceSpan(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Finds long silent gaps in a track's decoded audio — the boundary a hidden track
/// hides behind. Works over a level series rather than raw PCM so the decode itself
/// (platform-specific — Android's MediaCodec) stays out of pure logic entirely: this
/// only answers "given how loud each frame was, where did it go quiet for a while".
/// </summary>
public static class HiddenTrackDetector
{
    /// <summary>
    /// <paramref name="levels"/> is one amplitude/RMS sample per fixed-size analysis
    /// frame, in order, each already normalized to 0 (silent) .. 1 (loudest). A run of
    /// consecutive frames at or under <paramref name="silenceThreshold"/> becomes a
    /// <see cref="SilenceSpan"/> once it reaches <paramref name="minGap"/> — shorter
    /// than that is an ordinary quiet passage within a song, not a track boundary.
    ///
    /// Reports every qualifying run, including one that runs to the very end of the
    /// track with no loud audio after it — that is dead air, not a hidden track, but
    /// telling the two apart is <see cref="HiddenTrackSplitter"/>'s job, not this one's.
    /// </summary>
    public static IReadOnlyList<SilenceSpan> FindGaps(
        IReadOnlyList<float> levels, TimeSpan frameDuration, float silenceThreshold, TimeSpan minGap)
    {
        var gaps = new List<SilenceSpan>();
        var runStart = -1;

        for (var i = 0; i <= levels.Count; i++)
        {
            var silent = i < levels.Count && levels[i] <= silenceThreshold;
            if (silent && runStart < 0)
            {
                runStart = i;
            }
            else if (!silent && runStart >= 0)
            {
                var span = new SilenceSpan(frameDuration * runStart, frameDuration * i);
                if (span.Duration >= minGap)
                {
                    gaps.Add(span);
                }

                runStart = -1;
            }
        }

        return gaps;
    }
}

/// <summary>One playable stretch of a track's audio.</summary>
public sealed record TrackSegment(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Turns the gaps <see cref="HiddenTrackDetector"/> found into playable segments — the
/// actual "split the file" decision, kept separate from finding the gaps because not
/// every long silence is a split: one right at the very start or end is dead air, not
/// a boundary between two songs, and cutting on it would either orphan a sliver too
/// short to be its own track or, at the very end, split off nothing at all.
/// </summary>
public static class HiddenTrackSplitter
{
    /// <summary>
    /// A gap only becomes a split when there is a real track's worth of audio on
    /// <b>both</b> sides of it — at least <paramref name="minSegmentDuration"/>,
    /// measured from the start of the track (or the previous accepted split) to the
    /// gap on one side, and from the gap to the very end of the track on the other.
    /// That rules out a fade-in's leading silence and a fade-out's trailing silence.
    ///
    /// Returns the whole track as one segment when nothing qualifies, so a caller can
    /// treat "one segment" as "no hidden track" without a separate check.
    /// </summary>
    public static IReadOnlyList<TrackSegment> Plan(
        TimeSpan totalDuration, IReadOnlyList<SilenceSpan> gaps, TimeSpan minSegmentDuration)
    {
        var boundaries = new List<TimeSpan> { TimeSpan.Zero };
        var cursor = TimeSpan.Zero;

        foreach (var gap in gaps.OrderBy(gap => gap.Start))
        {
            var before = gap.Start - cursor;
            var after = totalDuration - gap.End;
            if (before < minSegmentDuration || after < minSegmentDuration)
            {
                continue;
            }

            boundaries.Add(gap.Start);
            boundaries.Add(gap.End);
            cursor = gap.End;
        }

        boundaries.Add(totalDuration);

        var segments = new List<TrackSegment>();
        for (var i = 0; i < boundaries.Count; i += 2)
        {
            segments.Add(new TrackSegment(boundaries[i], boundaries[i + 1]));
        }

        return segments;
    }
}
