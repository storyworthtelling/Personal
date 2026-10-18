using Constitution.Domain.Body;
using Constitution.Infrastructure.SleepNumber;

namespace Constitution.Infrastructure.Body;

/// <summary>
/// Carries <see cref="BedSleepSupplier"/> to the reader: the bed's hours for a night, but only when the bed (not the
/// watch) supplied it. The supplier already refuses to ask the bed on a night the watch covered; this reduces its
/// answer to hours, and turns a bed that could not answer into a null with the kind of failure in the log. How that
/// looks to the owner ("Waiting for the bed") is a later story (C-7.2.2); here a missing night is a gap and nothing more.
/// </summary>
public sealed class BedSleepGapFiller : ISleepGapFiller
{
    private readonly BedSleepSupplier supplier;
    private readonly IBodyLog log;

    private BedSleepGapFiller(BedSleepSupplier supplier, IBodyLog log)
    {
        this.supplier = supplier;
        this.log = log;
    }

    /// <summary>
    /// The filler for this install, or null when no side of the bed is chosen: the bed is then not part of sleep at
    /// all, and the watch is read alone.
    /// </summary>
    public static BedSleepGapFiller? Create(ISleepIqClient client, ISleeperChoice choice, IBodyLog log)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(log);

        return choice.Side is null ? null : new BedSleepGapFiller(new BedSleepSupplier(client, choice), log);
    }

    public async Task<double?> BedHoursAsync(DateOnly day, double? watchHours, CancellationToken cancellationToken)
    {
        var result = await supplier.SleepAsync(day, watchHours, cancellationToken).ConfigureAwait(false);
        if (!result.IsOk)
        {
            log.Outcome($"body bed sleep: {result.Failure.ToString().ToLowerInvariant()}");
            return null;
        }

        return result.Value is { Source: SleepSource.Bed } night ? night.Hours : null;
    }
}
