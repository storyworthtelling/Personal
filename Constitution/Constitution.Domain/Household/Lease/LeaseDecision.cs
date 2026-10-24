namespace Constitution.Domain.Household.Lease;

/// <summary>What an install should do about planning the week, once it has read the household file.</summary>
public enum LeaseVerdict
{
    /// <summary>This install holds the week's lease, it has stood long enough, and nobody else may plan: plan.</summary>
    Plan,

    /// <summary>Do not plan this week from this install. The reason says why; some reasons mean "try again after a heartbeat".</summary>
    DoNotPlan,

    /// <summary>Write a claim for this week (and a heartbeat). It does not plan: a claim is planned on only once it has stood for the settle window.</summary>
    Take,

    /// <summary>The lease this install held for an earlier week has ended: withdraw its claim. Housekeeping; it never affects who plans.</summary>
    Release,
}

/// <summary>Why a <see cref="LeaseVerdict.DoNotPlan"/> was said. Never carries a household fact, only the shape of the problem.</summary>
public enum LeaseReason
{
    None,

    /// <summary>The household file could not be read (no passphrase, wrong one, damaged, a newer format). Fail closed.</summary>
    DocumentUnreadable,

    /// <summary>The lease length asked for is below <see cref="LeaseTerms.MinimumLeaseLength"/>.</summary>
    LeaseLengthTooShort,

    /// <summary>Another install holds the week, and has been heard from (or its sign of life is dated ahead of this clock; never read as silence).</summary>
    HeldByAnother,

    /// <summary>
    /// Another install holds the week and has gone quiet past the lease. A standing claim stays its holder's
    /// until the Monday, because the holder may already have planned; the lease changes hands at the boundary.
    /// </summary>
    HolderGoneQuiet,

    /// <summary>The week's claim record is there but cannot be read. Fail closed.</summary>
    ClaimUnreadable,

    /// <summary>This install's claim has not yet stood for the settle window.</summary>
    ClaimNotSettled,

    /// <summary>This install's claim is dated ahead of this clock beyond the tolerance.</summary>
    ClaimDatedInTheFuture,

    /// <summary>This install's own heartbeat is missing or older than the lease: the other install may already consider it silent. Beat, publish, then ask again.</summary>
    OwnHeartbeatStale,

    /// <summary>This install's own heartbeat is dated ahead of its own clock. Beat again.</summary>
    OwnHeartbeatFromTheFuture,

    /// <summary>Partner install, no claim yet: the owner's install is alive, and plans.</summary>
    OwnerIsAlive,

    /// <summary>Partner install: no heartbeat from an owner's install has ever been seen, so its silence cannot be judged. Fail closed.</summary>
    OwnerNeverSeen,

    /// <summary>Partner install: an owner's heartbeat is dated ahead of this clock beyond the tolerance. Never an early takeover.</summary>
    OwnerHeartbeatFromTheFuture,

    /// <summary>Partner install: a heartbeat record in the file cannot be read, so the owner's silence cannot be judged. Fail closed.</summary>
    HeartbeatUnreadable,
}

/// <summary>
/// The answer to "may this install plan the week?". <see cref="WeekIndex"/> is the household-zone week
/// the answer is about (<see cref="LeaseWeek"/>); <see cref="Holder"/> is the install that holds it when
/// one does.
/// </summary>
public sealed record LeaseDecision(LeaseVerdict Verdict, LeaseReason Reason, long WeekIndex, string? Holder)
{
    public bool MayPlan => Verdict == LeaseVerdict.Plan;

    public static LeaseDecision Plan(long week, string holder) => new(LeaseVerdict.Plan, LeaseReason.None, week, holder);

    public static LeaseDecision DoNotPlan(LeaseReason reason, long week, string? holder = null) => new(LeaseVerdict.DoNotPlan, reason, week, holder);

    public static LeaseDecision Take(long week, string holder) => new(LeaseVerdict.Take, LeaseReason.None, week, holder);

    public static LeaseDecision Release(long week, string holder) => new(LeaseVerdict.Release, LeaseReason.None, week, holder);
}
