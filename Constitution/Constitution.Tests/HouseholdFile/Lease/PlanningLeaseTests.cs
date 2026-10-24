using Constitution.Domain.Household.Lease;
using Storyworthtelling.Household;
using static Constitution.Tests.HouseholdFile.LeaseRig;

namespace Constitution.Tests.HouseholdFile;

/// <summary>
/// C-6.1.3 (G15, R8) — the planning lease. Sam's install is the owner's, Lee's the partner's. Each test
/// plays the head's loop: beat, ask, write what was asked, share the file. The acceptance, in order:
/// the owner heartbeating means only it plans; its heartbeat silent past the lease means the partner's
/// install takes the lease and plans; the owner back means the lease returns at the next week boundary,
/// not before.
/// </summary>
public class PlanningLeaseTests
{
    [Fact]
    public void OnlyTheHolderPlans()
    {
        var (sam, lee) = Phones();
        var open = OpeningMonday;

        // Nothing claimed yet. The owner is to take; the partner waits for an owner it can hear.
        sam.Beat(open);
        Share(sam, lee);
        Assert.Equal(LeaseVerdict.Take, sam.Ask(open).Verdict);
        var waiting = lee.Ask(open);
        Assert.Equal(LeaseVerdict.DoNotPlan, waiting.Verdict);
        Assert.Equal(LeaseReason.OwnerIsAlive, waiting.Reason);

        // Taking is not planning: a fresh claim has to stand for the settle window first.
        Assert.Equal(LeaseVerdict.Take, sam.Pass(open).Verdict);
        Share(sam, lee);
        Assert.Equal(LeaseReason.ClaimNotSettled, sam.Ask(open.AddHours(1)).Reason);
        Assert.Equal(LeaseReason.ClaimNotSettled, sam.Ask(open + Settle - TimeSpan.FromMinutes(1)).Reason);

        // Then, however the week goes, with Sam heard from, only Sam is told to plan.
        for (var at = open + Settle; at < Est(11, 1, 23, 0); at += LeaseTerms.HeartbeatPeriod)
        {
            sam.Beat(at);
            Share(sam, lee);
            var holder = sam.Ask(at);
            var other = lee.Ask(at);
            Assert.Equal(LeaseVerdict.Plan, holder.Verdict);
            Assert.Equal(Sam, holder.Holder);
            Assert.Equal(LeaseVerdict.DoNotPlan, other.Verdict);
            Assert.Equal(LeaseReason.HeldByAnother, other.Reason);
        }
    }

    [Fact]
    public void TheHolderIsNotToldToPlanOnAnEmptyFile()
    {
        var (sam, lee) = Phones();
        Assert.NotEqual(LeaseVerdict.Plan, sam.Ask(OpeningMonday).Verdict);
        Assert.NotEqual(LeaseVerdict.Plan, lee.Ask(OpeningMonday).Verdict);
    }

    [Fact]
    public void SilenceHandsItOver()
    {
        var (sam, lee) = Phones();

        // Sam's phone was last heard on Sunday evening, before the week opened, and has said nothing since.
        var lastHeard = Edt(10, 25, 20);
        sam.Beat(lastHeard);
        Share(sam, lee);
        lee.Beat(lastHeard);

        // Inside the lease, and at exactly the lease, Lee waits: silent *past* the lease is the test.
        Assert.Equal(LeaseReason.OwnerIsAlive, lee.Ask(lastHeard + (Lease / 2)).Reason);
        Assert.Equal(LeaseReason.OwnerIsAlive, lee.Ask(lastHeard + Lease).Reason);

        // One minute past it, Lee takes the lease, and plans once its own claim has settled.
        var past = lastHeard + Lease + TimeSpan.FromMinutes(1);
        var taken = lee.Pass(past);
        Assert.Equal(LeaseVerdict.Take, taken.Verdict);
        Assert.Equal(Lee, taken.Holder);
        Assert.Equal(LeaseReason.ClaimNotSettled, lee.Ask(past.AddHours(1)).Reason);

        var later = past + Settle;
        lee.Beat(later);
        var plans = lee.Ask(later);
        Assert.Equal(LeaseVerdict.Plan, plans.Verdict);
        Assert.Equal(Lee, plans.Holder);
    }

    [Fact]
    public void AStandingClaimIsNotTakenMidWeek()
    {
        // Sam claims on Monday, may well have planned, and then goes quiet. However long, Lee does not plan Sam's week:
        // two installs planning one week is the silent failure, so the lease changes hands at the Monday.
        var (sam, lee) = Phones();
        sam.Pass(OpeningMonday);
        Share(sam, lee);
        lee.Beat(OpeningMonday);

        foreach (var at in new[] { OpeningMonday + Lease + TimeSpan.FromMinutes(1), Edt(10, 30, 9), Est(11, 1, 23, 59) })
        {
            lee.Beat(at);
            var decision = lee.Ask(at);
            Assert.Equal(LeaseVerdict.DoNotPlan, decision.Verdict);
            Assert.Equal(LeaseReason.HolderGoneQuiet, decision.Reason);
        }

        // At the boundary Sam has been silent past the lease with nothing claimed, and the week is Lee's to take.
        var boundary = Est(11, 2, 0, 0);
        lee.Beat(boundary);
        Assert.Equal(LeaseVerdict.Take, lee.Pass(boundary).Verdict);
        lee.Beat(boundary + Settle);
        Assert.Equal(LeaseVerdict.Plan, lee.Ask(boundary + Settle).Verdict);
    }

    [Fact]
    public void SilenceBeforeAnyClaimAlsoHandsItOver()
    {
        var (sam, lee) = Phones();
        sam.Beat(OpeningMonday);
        Share(sam, lee);

        Assert.Equal(LeaseReason.OwnerIsAlive, lee.Ask(OpeningMonday + Lease).Reason);
        Assert.Equal(LeaseVerdict.Take, lee.Ask(OpeningMonday + Lease + TimeSpan.FromMinutes(1)).Verdict);
    }

    [Fact]
    public void AnAliveOwnerNeverLosesTheLease()
    {
        var (sam, lee) = Phones();
        for (var at = OpeningMonday; at < OpeningMonday.AddDays(6); at += LeaseTerms.HeartbeatPeriod)
        {
            sam.Beat(at);
            Share(sam, lee);
            Assert.NotEqual(LeaseVerdict.Take, lee.Ask(at).Verdict);
            Assert.NotEqual(LeaseVerdict.Plan, lee.Ask(at).Verdict);
        }
    }

    [Fact]
    public void ItReturnsAtTheWeekBoundary()
    {
        var (sam, lee) = Phones();
        var week = WeekOf(OpeningMonday);

        // Sam is last heard on Monday morning. On Wednesday Lee takes the week and, once settled, plans it.
        sam.Beat(OpeningMonday);
        Share(sam, lee);
        var wednesday = Edt(10, 28, 8, 1);
        Assert.Equal(LeaseVerdict.Take, lee.Pass(wednesday).Verdict);
        var wednesdayNight = wednesday + Settle;
        lee.Beat(wednesdayNight);
        Assert.Equal(LeaseVerdict.Plan, lee.Ask(wednesdayNight).Verdict);

        // Sam's phone is back on Thursday. The week is Lee's until it ends: Sam is told to wait, every day.
        sam.Read(lee.Local);
        for (var at = Edt(10, 29, 9); at < Est(11, 1, 23, 59); at = at.AddHours(6))
        {
            sam.Beat(at);
            lee.Beat(at);
            Share(sam, lee);
            var samSays = sam.Ask(at);
            Assert.Equal(LeaseVerdict.DoNotPlan, samSays.Verdict);
            Assert.Equal(LeaseReason.HeldByAnother, samSays.Reason);
            Assert.Equal(LeaseVerdict.Plan, lee.Ask(at).Verdict);
        }

        // The last minute of the week is still Lee's.
        var lastMinute = Est(11, 1, 23, 59);
        sam.Beat(lastMinute);
        lee.Beat(lastMinute);
        Share(sam, lee);
        Assert.Equal(LeaseVerdict.DoNotPlan, sam.Ask(lastMinute).Verdict);
        Assert.Equal(LeaseVerdict.Plan, lee.Ask(lastMinute).Verdict);

        // At the boundary it goes home: Sam takes the new week, and Lee's old claim is let go.
        var boundary = Est(11, 2, 0, 0);
        sam.Beat(boundary);
        lee.Beat(boundary);
        Share(sam, lee);
        Assert.NotEqual(week, WeekOf(boundary));
        var samTakes = sam.Ask(boundary);
        Assert.Equal(LeaseVerdict.Take, samTakes.Verdict);
        Assert.Equal(WeekOf(boundary), samTakes.WeekIndex);
        var leeLetsGo = lee.Ask(boundary);
        Assert.Equal(LeaseVerdict.Release, leeLetsGo.Verdict);
        Assert.Equal(week, leeLetsGo.WeekIndex);

        sam.Pass(boundary);
        lee.Pass(boundary);
        Share(sam, lee);
        var settled = boundary + Settle;
        sam.Beat(settled);
        lee.Beat(settled);
        Share(sam, lee);
        Assert.Equal(LeaseVerdict.Plan, sam.Ask(settled).Verdict);
        var leeNow = lee.Ask(settled);
        Assert.Equal(LeaseVerdict.DoNotPlan, leeNow.Verdict);
        Assert.Equal(LeaseReason.HeldByAnother, leeNow.Reason);
    }

    [Fact]
    public void TheOwnerBackMidWeekDoesNotTakeItBack()
    {
        var (sam, lee) = Phones();
        sam.Beat(OpeningMonday);
        Share(sam, lee);
        lee.Pass(Edt(10, 28, 8, 1));
        Share(sam, lee);

        for (var at = Edt(10, 28, 9); at < Est(11, 2, 0, 0); at = at.AddHours(1))
        {
            sam.Beat(at);
            lee.Beat(at);
            Share(sam, lee);
            Assert.NotEqual(LeaseVerdict.Take, sam.Ask(at).Verdict);
            Assert.NotEqual(LeaseVerdict.Plan, sam.Ask(at).Verdict);
        }
    }

    [Fact]
    public void BothTakingAtOnceLeavesOneHolder()
    {
        var (sam, lee) = Phones();
        sam.Beat(OpeningMonday);
        Share(sam, lee);

        // Sam's phone wakes at the very minute Lee decides Sam is silent, neither having read the other.
        var meeting = Edt(10, 28, 8, 1);
        Assert.Equal(LeaseVerdict.Take, sam.Pass(meeting).Verdict);
        Assert.Equal(LeaseVerdict.Take, lee.Pass(meeting).Verdict);

        // Neither plans on a claim it wrote from a stale view...
        Assert.NotEqual(LeaseVerdict.Plan, sam.Ask(meeting.AddMinutes(30)).Verdict);
        Assert.NotEqual(LeaseVerdict.Plan, lee.Ask(meeting.AddMinutes(30)).Verdict);

        // ...and once the two have read each other there is one claim, one holder, one planner.
        Share(sam, lee);
        var week = WeekOf(meeting);
        var holder = LeaseView.Read(sam.Local).Claims[week].Holder;
        var later = meeting + Settle;
        sam.Beat(later);
        lee.Beat(later);
        Share(sam, lee);
        var planners = new[] { sam, lee }.Where(phone => phone.Ask(later).Verdict == LeaseVerdict.Plan).Select(phone => phone.Install).ToList();
        Assert.Equal([holder], planners);
    }

    [Fact]
    public void ASimultaneousClaimGoesToTheLargerId()
    {
        // The library's tie-break, relied on here: the same moment, the larger writer id wins. Ordinal: "install-sam" > "install-lee".
        var week = WeekOf(OpeningMonday);
        var stamp = new HlcTimestamp(OpeningMonday.ToUnixTimeMilliseconds(), 0);
        var samClaim = ClaimRecord(Sam, week, OpeningMonday, stamp, Sam);
        var leeClaim = ClaimRecord(Lee, week, OpeningMonday, stamp, Lee);

        var one = HouseholdDocument.Empty.With(samClaim).With(leeClaim);
        var other = HouseholdDocument.Empty.With(leeClaim).With(samClaim);

        Assert.Equal(Sam, LeaseView.Read(one).Claims[week].Holder);
        Assert.Equal(Sam, LeaseView.Read(other).Claims[week].Holder);
    }

    [Fact]
    public void TwoInstallsNeverPlanOneWeek()
    {
        var weeksPlanned = 0;
        var plannedByOwner = 0;
        var plannedByPartner = 0;
        var partnerTakes = 0;

        for (var seed = 1; seed <= LeaseSimulation.SeedCount; seed++)
        {
            var run = LeaseSimulation.Run(seed);
            foreach (var (week, planners) in run.Planners)
            {
                Assert.True(planners.Count <= 1, $"seed {seed}: week {week} was planned by {string.Join(" and ", planners)}. Trace:\n{string.Join('\n', run.Trace.Where(line => line.Contains($"week {week}", StringComparison.Ordinal)))}");
                weeksPlanned++;
                plannedByOwner += planners.Contains(Sam) ? 1 : 0;
                plannedByPartner += planners.Contains(Lee) ? 1 : 0;
            }

            Assert.True(run.Converged, $"seed {seed}: the two copies of the file did not converge after a final sync.");
            partnerTakes += run.PartnerTakes;
        }

        // The property is not vacuous: weeks were planned, by each install, and the partner did take over.
        Assert.True(weeksPlanned > LeaseSimulation.SeedCount, $"only {weeksPlanned} weeks were planned across the seeds.");
        Assert.True(plannedByOwner > 0, "the owner never planned in any seed");
        Assert.True(plannedByPartner > 0, "the partner never planned in any seed");
        Assert.True(partnerTakes > 0, "the partner never took the lease in any seed: silence was never exercised");
    }
}
