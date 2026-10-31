using Constitution.Domain.Household.Lease;
using Constitution.Infrastructure.Household;
using Constitution.Tests.HouseholdHead;
using Constitution.Domain.Household;
using Storyworthtelling.Household;

namespace Constitution.Tests.PlanningClaim;

/// <summary>
/// D148: a phone opens the week (syncs, claims if it may, syncs the claim) before it is asked whether it plans. Two invented phones meet in one
/// in-memory Drive file.
/// </summary>
public class OpenTheWeekTests
{
    private static IReadOnlyDictionary<long, LeaseClaim> ClaimsInDrive(HeadRig rig) => LeaseView.Read(rig.Opened()).Claims;

    [Fact]
    public async Task TheOwnerWritesTheClaimAndItReachesDrive()
    {
        using var rig = new HeadRig();

        var standing = await new HouseholdPlanningStanding(rig.Service).OpenTheWeekAsync(CancellationToken.None);

        var claim = Assert.Single(ClaimsInDrive(rig)).Value;
        Assert.Equal(rig.Service.InstallId, claim.Holder);
        Assert.False(standing.MayPlan);
        Assert.True(standing.Waiting);
    }

    [Fact]
    public async Task TheOwnerPlansOnceTheClaimHasSettled()
    {
        using var rig = new HeadRig();
        var standing = new HouseholdPlanningStanding(rig.Service);
        await standing.OpenTheWeekAsync(CancellationToken.None);

        rig.Now = rig.Now.Add(LeaseTerms.SettleWindow).AddMinutes(1);
        var later = await standing.OpenTheWeekAsync(CancellationToken.None);

        Assert.True(later.MayPlan);
        Assert.Single(ClaimsInDrive(rig));
    }

    [Fact]
    public async Task APartnerDoesNotStealALiveOwnersWeek()
    {
        using var owner = new HeadRig();
        using var partner = new HeadRig(owner.Drive, HouseholdFileRole.SharedWith);
        await new HouseholdPlanningStanding(owner.Service).OpenTheWeekAsync(CancellationToken.None);

        var standing = await new HouseholdPlanningStanding(partner.Service).OpenTheWeekAsync(CancellationToken.None);

        var claim = Assert.Single(ClaimsInDrive(owner)).Value;
        Assert.Equal(owner.Service.InstallId, claim.Holder);
        Assert.False(standing.MayPlan);
        Assert.False(standing.Waiting);
    }

    [Fact]
    public async Task APartnerWaitsRatherThanClaimWhileTheOwnerIsAlive()
    {
        using var owner = new HeadRig();
        using var partner = new HeadRig(owner.Drive, HouseholdFileRole.SharedWith);
        await owner.Service.SyncAsync(CancellationToken.None);

        var standing = await new HouseholdPlanningStanding(partner.Service).OpenTheWeekAsync(CancellationToken.None);

        Assert.Empty(ClaimsInDrive(owner));
        Assert.False(standing.MayPlan);
        Assert.True(standing.Waiting);
    }

    [Fact]
    public async Task APartnerClaimsAWeekWhoseOwnerHasGoneQuiet()
    {
        using var owner = new HeadRig();
        using var partner = new HeadRig(owner.Drive, HouseholdFileRole.SharedWith);
        await owner.Service.SyncAsync(CancellationToken.None);
        partner.Now = owner.Now.Add(LeaseTerms.DefaultLeaseLength).AddHours(1);

        await new HouseholdPlanningStanding(partner.Service).OpenTheWeekAsync(CancellationToken.None);

        var claim = Assert.Single(ClaimsInDrive(owner)).Value;
        Assert.Equal(partner.Service.InstallId, claim.Holder);
    }

    [Fact]
    public async Task AnUnsyncedPhoneClaimsNothingAndSaysItIsWaiting()
    {
        using var rig = new HeadRig { HasAccount = false };

        var standing = await new HouseholdPlanningStanding(rig.Service).OpenTheWeekAsync(CancellationToken.None);

        Assert.False(standing.MayPlan);
        Assert.True(standing.Waiting);
        Assert.Equal(0, rig.Drive.Writes);
        Assert.Empty(LeaseView.Read(await rig.Local.LoadAsync(CancellationToken.None)).Claims);
    }

    [Fact]
    public async Task AnUnknownRoleDoesNotEvenReachDrive()
    {
        using var rig = new HeadRig(role: HouseholdFileRole.Unknown);

        var standing = await new HouseholdPlanningStanding(rig.Service).OpenTheWeekAsync(CancellationToken.None);

        Assert.False(standing.MayPlan);
        Assert.True(standing.Waiting);
        Assert.Equal(0, rig.Drive.Reads);
    }

    [Fact]
    public async Task ASettledClaimOnAStaleCopyIsNotEnoughWhileDriveCannotBeReached()
    {
        using var rig = new HeadRig();
        var standing = new HouseholdPlanningStanding(rig.Service);
        await standing.OpenTheWeekAsync(CancellationToken.None);
        rig.Now = rig.Now.Add(LeaseTerms.SettleWindow).AddMinutes(1);
        rig.Drive.Failure = new IOException("invented outage");

        var offline = await standing.OpenTheWeekAsync(CancellationToken.None);
        rig.Drive.Failure = null;
        var back = await standing.OpenTheWeekAsync(CancellationToken.None);

        Assert.False(offline.MayPlan);
        Assert.True(offline.Waiting);
        Assert.True(back.MayPlan);
    }

    [Fact]
    public async Task OnePassPublishesTheHeartbeatItWrites()
    {
        using var rig = new HeadRig();

        await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(RosterRole.Owner, LeaseView.Read(rig.Opened()).Heartbeats[rig.Service.InstallId].Role);
    }
}
