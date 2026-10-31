using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Storyworthtelling.Household;
using static Constitution.Tests.HouseholdFile.LeaseRig;

namespace Constitution.Tests.HouseholdFile;

/// <summary>
/// The lease fails closed: when the file, a record or a clock cannot be trusted, the answer is "do not
/// plan", and no amount of doubt ever hands a week over early. A wrong "plan" is silent (two planners);
/// a wrong "do not" is a late plan the owner can see.
/// </summary>
public class LeaseFailClosedTests
{
    private static readonly DateTimeOffset Now = Edt(10, 28, 12);

    private static LeaseDecision Ask(HouseholdDocument? document, RosterRole role, string install = Lee, TimeSpan? lease = null) =>
        PlanningLease.Decide(document, install, role, Now, Zone, lease ?? Lease);

    private static HlcTimestamp Stamp(DateTimeOffset at, int counter = 0) => new(at.ToUnixTimeMilliseconds(), counter);

    [Theory]
    [InlineData(RosterRole.Owner)]
    [InlineData(RosterRole.Partner)]
    public void AnUnreadableFileIsNeverPlanned(RosterRole role)
    {
        var decision = Ask(null, role, role == RosterRole.Owner ? Sam : Lee);
        Assert.Equal(LeaseVerdict.DoNotPlan, decision.Verdict);
        Assert.Equal(LeaseReason.DocumentUnreadable, decision.Reason);
    }

    [Fact]
    public void ALeaseShorterThanTwoMissedBeatsIsRefused()
    {
        var tooShort = LeaseTerms.MinimumLeaseLength - TimeSpan.FromMinutes(1);
        Assert.Equal(LeaseReason.LeaseLengthTooShort, Ask(HouseholdDocument.Empty, RosterRole.Owner, Sam, tooShort).Reason);
        Assert.Equal(LeaseReason.LeaseLengthTooShort, Ask(HouseholdDocument.Empty, RosterRole.Owner, Sam, TimeSpan.Zero).Reason);
        Assert.Equal(LeaseReason.LeaseLengthTooShort, Ask(HouseholdDocument.Empty, RosterRole.Owner, Sam, TimeSpan.FromHours(-1)).Reason);
        Assert.NotEqual(LeaseReason.LeaseLengthTooShort, Ask(HouseholdDocument.Empty, RosterRole.Owner, Sam, LeaseTerms.MinimumLeaseLength).Reason);
    }

    [Fact]
    public void AGarbledClaimBlocksBothInstalls()
    {
        var week = WeekOf(Now);
        var garbled = HouseholdRecord.Write(LeaseKeys.ClaimKey(week), 1, new Dictionary<string, string> { ["week"] = "not a number" }, Stamp(Now), Sam);
        var document = HouseholdDocument.Empty.With(garbled).With(BeatRecord(Sam, "owner", Now, Stamp(Now)));

        Assert.Equal(LeaseReason.ClaimUnreadable, Ask(document, RosterRole.Owner, Sam).Reason);
        Assert.Equal(LeaseReason.ClaimUnreadable, Ask(document, RosterRole.Partner, Lee).Reason);
    }

    [Fact]
    public void AClaimThatContradictsItsKeyIsUnreadable()
    {
        // Filed under this week's key but saying it is next week's: the key is what is looked up, the field is checked against it.
        var week = WeekOf(Now);
        var sayingNextWeek = ClaimRecord(Sam, week + 1, Now, Stamp(Now), Sam);
        var atThisKey = HouseholdRecord.Write(LeaseKeys.ClaimKey(week), 1, sayingNextWeek.Fields, Stamp(Now, 1), Sam);
        var document = HouseholdDocument.Empty.With(atThisKey).With(BeatRecord(Sam, "owner", Now, Stamp(Now, 2)));

        Assert.Equal(LeaseReason.ClaimUnreadable, Ask(document, RosterRole.Partner, Lee).Reason);
    }

    [Fact]
    public void ADeletedClaimIsNoClaim()
    {
        var week = WeekOf(Now);
        var claim = ClaimRecord(Sam, week, Now.AddDays(-1), Stamp(Now.AddDays(-1)), Sam);
        var document = HouseholdDocument.Empty
            .With(claim)
            .With(claim.Delete(Stamp(Now.AddHours(-12)), Sam))
            .With(BeatRecord(Sam, "owner", Now.AddMinutes(-5), Stamp(Now)));

        Assert.Equal(LeaseVerdict.Take, Ask(document, RosterRole.Owner, Sam).Verdict);
    }

    [Fact]
    public void ANewerWritersRecordsAreIgnored()
    {
        var extra = HouseholdRecord.Write(new RecordKey(ConstitutionKinds.PlanningLease, "something:new"), 9, new Dictionary<string, string> { ["x"] = "y" }, Stamp(Now), Sam);
        var document = HouseholdDocument.Empty.With(extra).With(BeatRecord(Sam, "owner", Now.AddMinutes(-5), Stamp(Now)));

        Assert.Equal(LeaseVerdict.Take, Ask(document, RosterRole.Owner, Sam).Verdict);
        Assert.Empty(LeaseView.Read(document).UnreadableRecords);
    }

    [Fact]
    public void APartnerWhoNeverHeardTheOwnerWaits()
    {
        var document = HouseholdDocument.Empty.With(BeatRecord(Lee, "partner", Now, Stamp(Now)));
        var decision = Ask(document, RosterRole.Partner);
        Assert.Equal(LeaseVerdict.DoNotPlan, decision.Verdict);
        Assert.Equal(LeaseReason.OwnerNeverSeen, decision.Reason);
    }

    [Fact]
    public void AnUnreadableBeatBlocksTheTakeover()
    {
        var garbled = HouseholdRecord.Write(LeaseKeys.HeartbeatKey(Sam), 1, new Dictionary<string, string> { ["install"] = Sam }, Stamp(Now), Sam);
        var decision = Ask(HouseholdDocument.Empty.With(garbled), RosterRole.Partner);
        Assert.Equal(LeaseVerdict.DoNotPlan, decision.Verdict);
        Assert.Equal(LeaseReason.HeartbeatUnreadable, decision.Reason);
    }

    [Fact]
    public void AFutureHeartbeatNeverHandsItOverEarly()
    {
        // Sam's clock ran five days ahead when it last beat.
        var beat = Now.AddDays(5);
        var document = HouseholdDocument.Empty.With(BeatRecord(Sam, "owner", beat, Stamp(Now)));

        var decision = Ask(document, RosterRole.Partner);
        Assert.Equal(LeaseVerdict.DoNotPlan, decision.Verdict);
        Assert.Equal(LeaseReason.OwnerHeartbeatFromTheFuture, decision.Reason);

        // Not at any time before the beat's own date plus the lease; one minute after, the usual rule applies.
        foreach (var hours in new[] { 1, 24, 24 * 5, 24 * 5 + 24, 24 * 5 + 47 })
        {
            var at = Now.AddHours(hours);
            Assert.NotEqual(LeaseVerdict.Take, PlanningLease.Decide(document, Lee, RosterRole.Partner, at, Zone, Lease).Verdict);
        }

        var past = beat + Lease + TimeSpan.FromMinutes(1);
        Assert.Equal(LeaseVerdict.Take, PlanningLease.Decide(document, Lee, RosterRole.Partner, past, Zone, Lease).Verdict);
    }

    [Fact]
    public void ABeatJustAheadWithinToleranceIsAlive()
    {
        var document = HouseholdDocument.Empty.With(BeatRecord(Sam, "owner", Now + LeaseTerms.FutureTolerance - TimeSpan.FromMinutes(1), Stamp(Now)));
        Assert.Equal(LeaseReason.OwnerIsAlive, Ask(document, RosterRole.Partner).Reason);
    }

    [Fact]
    public void ABeatJustPastTheToleranceIsSuspect()
    {
        var document = HouseholdDocument.Empty.With(BeatRecord(Sam, "owner", Now + LeaseTerms.FutureTolerance + TimeSpan.FromMinutes(1), Stamp(Now)));
        Assert.Equal(LeaseReason.OwnerHeartbeatFromTheFuture, Ask(document, RosterRole.Partner).Reason);
    }

    [Fact]
    public void AHolderBeatingFromTheFutureKeepsTheWeek()
    {
        var week = WeekOf(Now);
        var document = HouseholdDocument.Empty
            .With(ClaimRecord(Sam, week, Now.AddDays(-2), Stamp(Now.AddDays(-2)), Sam))
            .With(BeatRecord(Sam, "owner", Now.AddDays(4), Stamp(Now)));

        var decision = Ask(document, RosterRole.Partner);
        Assert.Equal(LeaseReason.HeldByAnother, decision.Reason);
    }

    [Fact]
    public void ALongerDeclaredLeaseIsHonoured()
    {
        var long5 = TimeSpan.FromDays(5);
        var document = HouseholdDocument.Empty.With(BeatRecord(Sam, "owner", Now.AddHours(-60), Stamp(Now), long5));

        // Sixty hours is past our 48-hour lease but not the five days Sam says it keeps.
        Assert.Equal(LeaseReason.OwnerIsAlive, Ask(document, RosterRole.Partner).Reason);
    }

    [Fact]
    public void AStaleOwnBeatMayNotPlanOnItsOwnClaim()
    {
        var week = WeekOf(Now);
        var claimed = Now.AddDays(-2);
        var document = HouseholdDocument.Empty
            .With(ClaimRecord(Sam, week, claimed, Stamp(claimed), Sam))
            .With(BeatRecord(Sam, "owner", Now - Lease - TimeSpan.FromMinutes(1), Stamp(claimed, 1)));

        var decision = Ask(document, RosterRole.Owner, Sam);
        Assert.Equal(LeaseVerdict.DoNotPlan, decision.Verdict);
        Assert.Equal(LeaseReason.OwnHeartbeatStale, decision.Reason);
    }

    [Fact]
    public void ADocumentWithNoBeatOfItsOwnIsStaleToo()
    {
        var week = WeekOf(Now);
        var claimed = Now.AddDays(-2);
        var document = HouseholdDocument.Empty.With(ClaimRecord(Sam, week, claimed, Stamp(claimed), Sam));
        Assert.Equal(LeaseReason.OwnHeartbeatStale, Ask(document, RosterRole.Owner, Sam).Reason);
    }

    [Fact]
    public void AnOwnBeatFromTheFutureIsNotPlannedOn()
    {
        var week = WeekOf(Now);
        var claimed = Now.AddDays(-2);
        var document = HouseholdDocument.Empty
            .With(ClaimRecord(Sam, week, claimed, Stamp(claimed), Sam))
            .With(BeatRecord(Sam, "owner", Now + LeaseTerms.FutureTolerance + TimeSpan.FromMinutes(1), Stamp(claimed, 1)));
        Assert.Equal(LeaseReason.OwnHeartbeatFromTheFuture, Ask(document, RosterRole.Owner, Sam).Reason);
        Assert.True(LeaseWriter.NeedsHeartbeat(document, Sam, Now));
    }

    [Fact]
    public void AClaimDatedInTheFutureIsNotPlannedOn()
    {
        var week = WeekOf(Now);
        var taken = Now + LeaseTerms.FutureTolerance + TimeSpan.FromMinutes(1);
        var document = HouseholdDocument.Empty
            .With(ClaimRecord(Sam, week, taken, Stamp(Now), Sam))
            .With(BeatRecord(Sam, "owner", Now, Stamp(Now, 1)));
        Assert.Equal(LeaseReason.ClaimDatedInTheFuture, Ask(document, RosterRole.Owner, Sam).Reason);
    }

    [Fact]
    public void ASettledClaimPlansAtExactlyTheWindow()
    {
        var week = WeekOf(Now);
        var inside = HouseholdDocument.Empty
            .With(ClaimRecord(Sam, week, Now - LeaseTerms.SettleWindow + TimeSpan.FromMilliseconds(1), Stamp(Now.AddDays(-1)), Sam))
            .With(BeatRecord(Sam, "owner", Now, Stamp(Now.AddDays(-1), 1)));
        var exactly = HouseholdDocument.Empty
            .With(ClaimRecord(Sam, week, Now - LeaseTerms.SettleWindow, Stamp(Now.AddDays(-1)), Sam))
            .With(BeatRecord(Sam, "owner", Now, Stamp(Now.AddDays(-1), 1)));

        Assert.Equal(LeaseReason.ClaimNotSettled, Ask(inside, RosterRole.Owner, Sam).Reason);
        Assert.Equal(LeaseVerdict.Plan, Ask(exactly, RosterRole.Owner, Sam).Verdict);
    }

    [Fact]
    public void ASilentHoldersWeekIsNotTakenByEither()
    {
        var week = WeekOf(Now);
        var then = Now - Lease - TimeSpan.FromHours(1);
        var document = HouseholdDocument.Empty
            .With(ClaimRecord("install-robin", week, then, Stamp(then), "install-robin"))
            .With(BeatRecord("install-robin", "partner", then, Stamp(then, 1)))
            .With(BeatRecord(Sam, "owner", Now.AddMinutes(-30), Stamp(Now)));

        Assert.Equal(LeaseReason.HolderGoneQuiet, Ask(document, RosterRole.Partner).Reason);
        Assert.Equal(LeaseReason.HolderGoneQuiet, Ask(document, RosterRole.Owner, Sam).Reason);
    }
}
