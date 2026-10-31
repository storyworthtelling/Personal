namespace Constitution.Infrastructure.SleepNumber;

/// <summary>Where a night's sleep came from.</summary>
public enum SleepSource
{
    Watch,
    Bed,
}

/// <summary>A night's sleep in hours and where it came from, so the chart can say so.</summary>
public sealed record SleepReading(double Hours, SleepSource Source);

/// <summary>The rule: the bed stands in only on a night the watch read zero or nothing.</summary>
public static class WatchOrBed
{
    /// <summary>
    /// The watch wins whenever it read anything above zero. Zero, missing, negative or not a number all
    /// count as "the watch read nothing", and then a bed night above zero fills in. With neither there is
    /// no reading at all (null), which the chart draws as a gap rather than a zero.
    /// </summary>
    public static SleepReading? Choose(double? watchHours, BedNight? bedNight)
    {
        if (watchHours is { } worn && double.IsFinite(worn) && worn > 0)
        {
            return new SleepReading(worn, SleepSource.Watch);
        }

        if (bedNight is not null && bedNight.Asleep > TimeSpan.Zero)
        {
            return new SleepReading(bedNight.Asleep.TotalHours, SleepSource.Bed);
        }

        return null;
    }
}
