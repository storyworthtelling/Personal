using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household.Lease;

/// <summary>
/// Who may plan the week (C-6.1.3, PC7, R8). <b>The decision only</b>: nothing here plans, and nothing here
/// writes; <see cref="LeaseWriter"/> writes what a decision asks for. Pure — time, the zone and the file are
/// passed in.
///
/// <para><b>The rules.</b> The lease covers one household-zone week (<see cref="LeaseWeek"/>). Each install
/// keeps a <b>heartbeat</b> (its own record, so two installs never overwrite each other's). A week is held
/// by whoever's <b>claim</b> stands on it (one record per week, so the library's total order leaves exactly
/// one). The owner's install takes an unclaimed week; the partner's takes one only when every owner install
/// it has heard of has been silent <i>past</i> the lease. <b>A standing claim is never taken mid-week</b>, by
/// either install, whether its holder is alive, back, or gone quiet: the holder may already have planned,
/// and a second planner is the failure this lease exists to prevent. So the lease changes hands only on
/// an unclaimed week, which is to say at the Sunday boundary, and the owner back mid-week waits for the
/// next one. The cost, stated: an owner who claims on Sunday and then vanishes leaves the week unplanned
/// by anyone until the next Sunday (and the settle window after it). Taking is not planning: the holder
/// plans only once its claim has stood for <see cref="LeaseTerms.SettleWindow"/> and its own heartbeat is
/// current in the file.</para>
///
/// <para><b>Fail closed.</b> A file that could not be read, a record that cannot be read, a lease below
/// <see cref="LeaseTerms.MinimumLeaseLength"/>, or a partner that has never heard an owner all say do not
/// plan. A wrong "plan" is silent and doubles a week; a wrong "do not plan" is a late plan.</para>
///
/// <para><b>Clock skew.</b> A heartbeat dated ahead of this clock by more than
/// <see cref="LeaseTerms.FutureTolerance"/> is <i>suspect</i>: it is taken at face value for how long the
/// install is held to be alive (so it can only delay a takeover, to the beat's own date plus the lease),
/// never as silence. Reading it as stale would let a partner take a week from an owner whose clock merely
/// runs fast. Within the tolerance it is simply alive. The residual: a partner whose own clock runs
/// <i>ahead</i> of the owner's by more than the margin between the heartbeat period and the lease would
/// see a live owner as silent; <see cref="LeaseTerms.MinimumLeaseLength"/> and the 48-hour default keep
/// that margin wide.</para>
///
/// <para><b>What the decision cannot know.</b> It judges the document it is handed. Drive v3 has no
/// conditional write, so the caller must hand it the file as read in this pass and merged with the install's
/// own, after the publish cycle has settled; the settle window covers the time two installs need to have
/// read each other's claim, not an install that stays dark for longer than that after a write of its was
/// lost to the other's stale write. Also, the lease says who may plan <i>during</i> the week; which week
/// the plan is for is the planner's rule, and it must be one fixed function of the lease week.</para>
/// </summary>
public static class PlanningLease
{
    private enum Pulse
    {
        Alive,
        FromTheFuture,
        Silent,
    }

    /// <summary>
    /// May this install plan the week that contains <paramref name="now"/> on the household's clock?
    /// A null <paramref name="document"/> means the file could not be read (no passphrase, wrong passphrase,
    /// damaged, a newer format): the answer is do not plan.
    /// </summary>
    public static LeaseDecision Decide(HouseholdDocument? document, string myInstall, RosterRole myRole, DateTimeOffset now, TimeZoneInfo householdZone, TimeSpan leaseLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(myInstall);
        ArgumentNullException.ThrowIfNull(householdZone);
        var week = LeaseWeek.IndexOf(now, householdZone);
        if (document is null)
        {
            return LeaseDecision.DoNotPlan(LeaseReason.DocumentUnreadable, week);
        }

        if (leaseLength < LeaseTerms.MinimumLeaseLength)
        {
            return LeaseDecision.DoNotPlan(LeaseReason.LeaseLengthTooShort, week);
        }

        var asked = new Question(LeaseView.Read(document), myInstall, myRole, now, leaseLength, week);
        var answer = Choose(asked);
        return answer.Verdict == LeaseVerdict.DoNotPlan ? LetGoOfAnOldClaim(asked, answer) : answer;
    }

    private static LeaseDecision Choose(Question asked)
    {
        if (asked.View.UnreadableClaims.Any(unreadable => unreadable.Key == LeaseKeys.ClaimKey(asked.Week)))
        {
            return LeaseDecision.DoNotPlan(LeaseReason.ClaimUnreadable, asked.Week);
        }

        if (!asked.View.Claims.TryGetValue(asked.Week, out var claim))
        {
            return MayITake(asked);
        }

        if (Same(claim.Holder, asked.Me))
        {
            return IAmTheHolder(asked, claim);
        }

        // A standing claim is its holder's for the whole week, heard from or not: the holder may already
        // have planned, and a second planner is the one wrong answer that is silent. Only the reason differs.
        asked.View.Heartbeats.TryGetValue(claim.Holder, out var holderBeat);
        var lastHeard = holderBeat is null || claim.Taken > holderBeat.Beat ? claim.Taken : holderBeat.Beat;
        var lease = Longest(asked.Lease, holderBeat?.Lease, claim.Lease);
        return PulseOf(asked.Now, lastHeard, lease) == Pulse.Silent
            ? LeaseDecision.DoNotPlan(LeaseReason.HolderGoneQuiet, asked.Week, claim.Holder)
            : LeaseDecision.DoNotPlan(LeaseReason.HeldByAnother, asked.Week, claim.Holder);
    }

    /// <summary>The claim names this install: plan only if it has settled and the file shows this install alive.</summary>
    private static LeaseDecision IAmTheHolder(Question asked, LeaseClaim claim)
    {
        if (!asked.View.Heartbeats.TryGetValue(asked.Me, out var own))
        {
            return LeaseDecision.DoNotPlan(LeaseReason.OwnHeartbeatStale, asked.Week, asked.Me);
        }

        var ownAge = asked.Now - own.Beat;
        if (ownAge < -LeaseTerms.FutureTolerance)
        {
            return LeaseDecision.DoNotPlan(LeaseReason.OwnHeartbeatFromTheFuture, asked.Week, asked.Me);
        }

        if (ownAge > Longest(asked.Lease, own.Lease, null))
        {
            return LeaseDecision.DoNotPlan(LeaseReason.OwnHeartbeatStale, asked.Week, asked.Me);
        }

        var claimAge = asked.Now - claim.Taken;
        if (claimAge < -LeaseTerms.FutureTolerance)
        {
            return LeaseDecision.DoNotPlan(LeaseReason.ClaimDatedInTheFuture, asked.Week, asked.Me);
        }

        return claimAge < LeaseTerms.SettleWindow
            ? LeaseDecision.DoNotPlan(LeaseReason.ClaimNotSettled, asked.Week, asked.Me)
            : LeaseDecision.Plan(asked.Week, asked.Me);
    }

    /// <summary>The week has no standing claim: may this install write one?</summary>
    private static LeaseDecision MayITake(Question asked) =>
        asked.Role == RosterRole.Owner
            ? LeaseDecision.Take(asked.Week, asked.Me)
            : ThePartnerMayTake(asked);

    private static LeaseDecision ThePartnerMayTake(Question asked)
    {
        var myBeatKey = LeaseKeys.HeartbeatKey(asked.Me);
        if (asked.View.UnreadableHeartbeats.Any(unreadable => unreadable.Key != myBeatKey))
        {
            return LeaseDecision.DoNotPlan(LeaseReason.HeartbeatUnreadable, asked.Week);
        }

        var owners = asked.View.Heartbeats.Values
            .Where(beat => beat.Role == RosterRole.Owner && !Same(beat.Install, asked.Me))
            .Select(beat => PulseOf(asked.Now, beat.Beat, Longest(asked.Lease, beat.Lease, null)))
            .ToList();
        if (owners.Count == 0)
        {
            return LeaseDecision.DoNotPlan(LeaseReason.OwnerNeverSeen, asked.Week);
        }

        if (owners.Contains(Pulse.FromTheFuture))
        {
            return LeaseDecision.DoNotPlan(LeaseReason.OwnerHeartbeatFromTheFuture, asked.Week);
        }

        return owners.Contains(Pulse.Alive)
            ? LeaseDecision.DoNotPlan(LeaseReason.OwnerIsAlive, asked.Week)
            : LeaseDecision.Take(asked.Week, asked.Me);
    }

    /// <summary>
    /// When the answer is "not this install" and it still holds a claim on an earlier week, say so: that
    /// lease has ended and the claim can be withdrawn. Never when the file is part-unreadable.
    /// </summary>
    private static LeaseDecision LetGoOfAnOldClaim(Question asked, LeaseDecision answer)
    {
        if (answer.Reason is LeaseReason.ClaimUnreadable or LeaseReason.HeartbeatUnreadable)
        {
            return answer;
        }

        var old = asked.View.Claims.Values
            .Where(claim => claim.WeekIndex < asked.Week && Same(claim.Holder, asked.Me))
            .OrderByDescending(claim => claim.WeekIndex)
            .FirstOrDefault();
        return old is null ? answer : LeaseDecision.Release(old.WeekIndex, asked.Me);
    }

    /// <summary>
    /// Alive until strictly <i>past</i> the lease (silent for exactly the lease is not yet silent). A time
    /// ahead of this clock beyond the tolerance is suspect, and stays suspect until this clock passes it
    /// by the lease; the sign is never read as silence.
    /// </summary>
    private static Pulse PulseOf(DateTimeOffset now, DateTimeOffset lastHeard, TimeSpan lease)
    {
        var age = now - lastHeard;
        if (age > lease)
        {
            return Pulse.Silent;
        }

        return age < -LeaseTerms.FutureTolerance ? Pulse.FromTheFuture : Pulse.Alive;
    }

    /// <summary>The longest lease anyone involved declared: an install is never judged on a shorter lease than it said it runs on.</summary>
    private static TimeSpan Longest(TimeSpan mine, TimeSpan? theirs, TimeSpan? claimed) =>
        TimeSpan.FromTicks(Math.Max(mine.Ticks, Math.Max(theirs?.Ticks ?? 0, claimed?.Ticks ?? 0)));

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed record Question(LeaseView View, string Me, RosterRole Role, DateTimeOffset Now, TimeSpan Lease, long Week);
}
