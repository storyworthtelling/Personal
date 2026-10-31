namespace Constitution.Domain.Household.Lease;

/// <summary>
/// The numbers the planning lease runs on. <b>Every one is a guess</b> made from how the installs behave
/// (a background pass when the app is opened and a few times a day), not a measured figure: they are named
/// here so a measurement on the owner's phones changes one line, and so the tests state their times
/// relative to them rather than as bare numbers.
///
/// <para>The reasoning that ties them together: the lease has to be much longer than the heartbeat (one
/// missed beat must not hand the week over), the future tolerance only has to cover two phones whose clocks
/// are a few minutes apart, and the settle window has to be longer than two passes of the slower install, so
/// a claim made from a stale view has been merged with its rival before anyone plans on it.</para>
/// </summary>
public static class LeaseTerms
{
    /// <summary>How long an install may stay silent before the other may take over. Guess: two days.</summary>
    public static readonly TimeSpan DefaultLeaseLength = TimeSpan.FromHours(48);

    /// <summary>How often an install refreshes its heartbeat while it is running. Guess: four times a day.</summary>
    public static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromHours(6);

    /// <summary>How far ahead of the reader's clock a heartbeat may be dated before it is suspect. Guess: ten minutes of phone-clock drift.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a claim must have stood before its holder plans on it. Guess: two heartbeat periods, the
    /// time two installs need to have each read the other's claim. The cost is real: a lease taken at the
    /// start of the week cannot plan before noon, so the head takes it as the week opens.
    /// </summary>
    public static readonly TimeSpan SettleWindow = TimeSpan.FromHours(12);

    /// <summary>The shortest lease a caller may ask for: two missed heartbeats and the clock tolerance. Anything shorter hands a week over to a phone that was only asleep.</summary>
    public static readonly TimeSpan MinimumLeaseLength = HeartbeatPeriod + HeartbeatPeriod + FutureTolerance;
}
