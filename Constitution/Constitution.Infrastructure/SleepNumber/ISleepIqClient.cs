namespace Constitution.Infrastructure.SleepNumber;

/// <summary>The bed, as the rest of the app sees it. Every method returns a typed result and never throws for the network.</summary>
public interface ISleepIqClient
{
    /// <summary>Signs in with the household login. <see cref="SleepIqFailure.None"/> means a session is held in memory.</summary>
    Task<SleepIqFailure> SignInAsync(CancellationToken cancellationToken);

    /// <summary>Who sleeps on which side of the bed.</summary>
    Task<SleepIqResult<IReadOnlyList<BedSleeper>>> SleepersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The night that ended on <paramref name="day"/> for the sleeper on <paramref name="side"/>. An Ok result
    /// holding null means the bed has no reading for that night.
    /// </summary>
    Task<SleepIqResult<BedNight?>> NightAsync(BedSide side, DateOnly day, CancellationToken cancellationToken);
}
