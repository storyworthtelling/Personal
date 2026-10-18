namespace Constitution.Domain.Body;

/// <summary>
/// Fills a night the watch missed from somewhere else (the bed). A seam, so Domain does not know the
/// bed exists: the Infrastructure implementation wraps the SleepIQ supplier, and an install with no side
/// of the bed chosen has no filler at all and reads the watch alone.
/// </summary>
public interface ISleepGapFiller
{
    /// <summary>
    /// Hours slept on the night that ended on <paramref name="day"/>, but only when the watch's own
    /// <paramref name="watchHours"/> was nothing and the bed supplied the night. Null in every other case:
    /// the watch covered it, the bed had no reading, or the bed could not be reached.
    /// </summary>
    Task<double?> BedHoursAsync(DateOnly day, double? watchHours, CancellationToken cancellationToken);
}
