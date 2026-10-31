namespace Constitution.Infrastructure.SleepNumber;

/// <summary>
/// Supplies a night's sleep: the watch's when it read anything, otherwise the bed's for the side this
/// install chose. The bed is not even asked on a night the watch covered.
/// </summary>
public sealed class BedSleepSupplier
{
    private readonly ISleepIqClient client;
    private readonly ISleeperChoice choice;

    public BedSleepSupplier(ISleepIqClient client, ISleeperChoice choice)
    {
        this.client = client;
        this.choice = choice;
    }

    /// <summary>
    /// An Ok result holding null means neither the watch nor the bed had a reading. A failure means the
    /// bed was needed and could not answer (offline, rejected, shape, or no side chosen).
    /// </summary>
    public async Task<SleepIqResult<SleepReading?>> SleepAsync(DateOnly day, double? watchHours, CancellationToken cancellationToken)
    {
        if (WatchOrBed.Choose(watchHours, null) is { } worn)
        {
            return SleepIqResult<SleepReading?>.Ok(worn);
        }

        if (choice.Side is not { } side)
        {
            return SleepIqResult<SleepReading?>.Failed(SleepIqFailure.NoSuchSide);
        }

        var night = await client.NightAsync(side, day, cancellationToken).ConfigureAwait(false);
        return night.IsOk
            ? SleepIqResult<SleepReading?>.Ok(WatchOrBed.Choose(watchHours, night.Value))
            : SleepIqResult<SleepReading?>.Failed(night.Failure);
    }
}
