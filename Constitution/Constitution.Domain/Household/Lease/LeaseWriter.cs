using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household.Lease;

/// <summary>
/// Writes the lease's records through the library, the way every Constitution writer does: the install's
/// clock is first moved past everything it has read (<see cref="HybridLogicalClock.Witness"/>), so what it
/// writes outranks what it read however slow its own clock is, and a record already there is edited rather
/// than replaced, so a field a newer build wrote survives. Time and the clock come from the caller; this
/// reads neither.
/// </summary>
public static class LeaseWriter
{
    /// <summary>
    /// Whether this install's heartbeat should be written now: it has none, it is a full
    /// <see cref="LeaseTerms.HeartbeatPeriod"/> old, or it is dated ahead of this clock beyond the tolerance
    /// (an earlier fast clock wrote it; a fresh beat puts the true time back).
    /// </summary>
    public static bool NeedsHeartbeat(HouseholdDocument document, string install, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!LeaseView.Read(document).Heartbeats.TryGetValue(install, out var beat))
        {
            return true;
        }

        var age = now - beat.Beat;
        return age >= LeaseTerms.HeartbeatPeriod || age < -LeaseTerms.FutureTolerance;
    }

    /// <summary>The document with this install's heartbeat written at <paramref name="now"/>.</summary>
    public static HouseholdDocument Beat(HouseholdDocument document, HybridLogicalClock clock, string install, RosterRole role, DateTimeOffset now, TimeSpan lease) =>
        Put(document, clock, LeaseMaps.Heartbeat, new LeaseHeartbeat(install, role, now, lease), now, install);

    /// <summary>
    /// The document with a heartbeat <b>and</b> a claim on the household-zone week that contains
    /// <paramref name="now"/>. The beat goes in with the claim so the file never shows a claim whose
    /// holder has not been heard from.
    /// </summary>
    public static HouseholdDocument Take(HouseholdDocument document, HybridLogicalClock clock, string install, RosterRole role, DateTimeOffset now, TimeZoneInfo householdZone, TimeSpan lease) =>
        TakeWeek(document, clock, install, role, LeaseWeek.IndexOf(now, householdZone), now, lease);

    /// <summary>
    /// The document with this install's claim on a week withdrawn (a tombstone). A claim that is not this
    /// install's, or is not there, or cannot be read, is left exactly as it is: one install never cancels
    /// another's claim, and a file part-unreadable is not written into.
    /// </summary>
    public static HouseholdDocument Release(HouseholdDocument document, HybridLogicalClock clock, string install, long weekIndex, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(install);
        if (!LeaseView.Read(document).Claims.TryGetValue(weekIndex, out var claim)
            || !string.Equals(claim.Holder, install, StringComparison.OrdinalIgnoreCase))
        {
            return document;
        }

        var stamp = clock.Witness(document.MaxStamp, now.ToUnixTimeMilliseconds());
        return HouseholdRecords.Remove(document, LeaseKeys.ClaimKey(weekIndex), stamp, install);
    }

    /// <summary>
    /// Does what a decision asked: writes the claim for a Take, withdraws it for a Release, and leaves the
    /// document alone for a Plan or a DoNotPlan. The decision's own week is the one written, so a pass that
    /// crosses the Sunday boundary cannot claim the wrong week.
    /// </summary>
    public static HouseholdDocument Apply(HouseholdDocument document, LeaseDecision decision, HybridLogicalClock clock, string install, RosterRole role, DateTimeOffset now, TimeZoneInfo householdZone, TimeSpan lease)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Verdict is LeaseVerdict.Take or LeaseVerdict.Release
            && !string.Equals(decision.Holder, install, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The decision was made for " + (decision.Holder ?? "no install") + ", not for this install, and is not written here.", nameof(decision));
        }

        return decision.Verdict switch
        {
            LeaseVerdict.Take => TakeWeek(document, clock, install, role, decision.WeekIndex, now, lease),
            LeaseVerdict.Release => Release(document, clock, install, decision.WeekIndex, now),
            _ => document,
        };
    }

    private static HouseholdDocument TakeWeek(HouseholdDocument document, HybridLogicalClock clock, string install, RosterRole role, long weekIndex, DateTimeOffset now, TimeSpan lease)
    {
        var beaten = Beat(document, clock, install, role, now, lease);
        return Put(beaten, clock, LeaseMaps.Claim, new LeaseClaim(install, role, weekIndex, now, lease), now, install);
    }

    private static HouseholdDocument Put<T>(HouseholdDocument document, HybridLogicalClock clock, RecordMap<T> map, T value, DateTimeOffset now, string install)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(install);
        var stamp = clock.Witness(document.MaxStamp, now.ToUnixTimeMilliseconds());
        return HouseholdRecords.Put(document, map, value, stamp, install);
    }
}
