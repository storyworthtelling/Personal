namespace Constitution.Infrastructure.SleepNumber;

/// <summary>
/// A sleeper's place on the bed. It deliberately holds no name: the person-to-side choice is made by
/// side ("which side is mine"), so a name has no job here and is never read from the service's reply.
/// <see cref="SleeperId"/> is the service's opaque id; it is needed to ask for a night but is never logged.
/// </summary>
public sealed record BedSleeper(string SleeperId, BedSide Side)
{
    /// <summary>The side only; the id stays out of any text that could reach a log.</summary>
    public override string ToString() => $"sleeper on the {Side} side";
}
